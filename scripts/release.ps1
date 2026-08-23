param(
    [string]$OutputDirectory = "artifacts/release-candidate"
)

$ErrorActionPreference = "Stop"
$workspace = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$localDotnet = Join-Path $workspace ".tools\dotnet\dotnet.exe"
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { "dotnet" }
$output = [IO.Path]::GetFullPath((Join-Path $workspace $OutputDirectory))

if (-not $output.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Output directory must be inside the repository: $output"
}
if (Test-Path -LiteralPath $output) {
    throw "Output directory already exists: $output"
}

Push-Location $workspace
try {
    if (git status --porcelain) {
        throw "Release verification requires a clean working tree."
    }

    function Invoke-DotNet([string[]]$Arguments) {
        & $dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }

    Invoke-DotNet -Arguments @("build", "-c", "Release", "-warnaserror")
    Invoke-DotNet -Arguments @("run", "--project", "tests/EventFast.Tests", "-c", "Release", "--", "--integration", "--excel", "--leak", "--ui")

    New-Item -ItemType Directory -Path $output | Out-Null
    $benchmark = Join-Path $output "benchmark.txt"
    & $dotnet run --project benchmarks/EventFast.Benchmarks -c Release -- --large | Tee-Object -FilePath $benchmark
    if ($LASTEXITCODE -ne 0) {
        throw "Benchmark failed with exit code $LASTEXITCODE."
    }

    $publish = Join-Path $output "win-x64"
    Invoke-DotNet -Arguments @("publish", "-p:PublishProfile=win-x64", "-p:PublishDir=$publish\")
    $files = @(Get-ChildItem -LiteralPath $publish -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne "EventFast.exe") {
        throw "Single-file verification failed: $($files.Name -join ', ')"
    }

    $exe = $files[0].FullName
    $selfTest = Start-Process -FilePath $exe -ArgumentList "--self-test" -PassThru -Wait -WindowStyle Hidden
    if ($selfTest.ExitCode -ne 0) {
        throw "Published self-test failed with exit code $($selfTest.ExitCode)."
    }

    $ui = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    if ($ui.HasExited) {
        throw "Published UI exited early with exit code $($ui.ExitCode)."
    }
    Stop-Process -Id $ui.Id

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $publish "EventFast.exe.sha256") -Value "$hash  EventFast.exe" -Encoding ascii
    Write-Output "Release candidate verified: $exe"
    Write-Output "SHA256: $hash"
    Write-Output "Manual Clean Windows and Event Viewer comparison gates remain required."
}
finally {
    Pop-Location
}

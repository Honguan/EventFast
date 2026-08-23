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
    $extract = Join-Path $output "bundle-extract"
    if (-not $extract.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Bundle extraction directory must be inside the release output: $extract"
    }
    New-Item -ItemType Directory -Path $extract | Out-Null
    $ui = $null
    try {
        $startInfo = [Diagnostics.ProcessStartInfo]::new($exe)
        $startInfo.UseShellExecute = $false
        $startInfo.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = $extract
        $startup = [Diagnostics.Stopwatch]::StartNew()
        $ui = [Diagnostics.Process]::Start($startInfo)
        while (-not $ui.HasExited -and $ui.MainWindowHandle -eq [IntPtr]::Zero -and $startup.ElapsedMilliseconds -lt 2000) {
            Start-Sleep -Milliseconds 10
            $ui.Refresh()
        }
        $startup.Stop()
        if ($ui.HasExited) {
            throw "Published UI exited early with exit code $($ui.ExitCode)."
        }
        if ($ui.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "Published UI did not create a window within 2 seconds."
        }
        if ($startup.ElapsedMilliseconds -ge 1000) {
            throw "Cold UI startup missed the 1 second target: $($startup.ElapsedMilliseconds) ms."
        }
        if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) -or
            -not (Get-Command Get-NetUDPEndpoint -ErrorAction SilentlyContinue)) {
            throw "NetTCPIP cmdlets are required for the local-only verification."
        }
        $tcp = @(Get-NetTCPConnection -OwningProcess $ui.Id -ErrorAction SilentlyContinue)
        $udp = @(Get-NetUDPEndpoint -OwningProcess $ui.Id -ErrorAction SilentlyContinue)
        if ($tcp.Count -ne 0 -or $udp.Count -ne 0) {
            throw "Published UI opened network endpoints: TCP $($tcp.Count), UDP $($udp.Count)."
        }
        Set-Content -LiteralPath (Join-Path $output "startup.txt") -Value "Cold UI startup: $($startup.ElapsedMilliseconds) ms" -Encoding ascii
        Set-Content -LiteralPath (Join-Path $output "privacy.txt") -Value "Network endpoints: TCP 0, UDP 0" -Encoding ascii
    }
    finally {
        if ($ui -and -not $ui.HasExited) {
            Stop-Process -Id $ui.Id
        }
        Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
    }

    $selfTest = Start-Process -FilePath $exe -ArgumentList "--self-test" -PassThru -Wait -WindowStyle Hidden
    if ($selfTest.ExitCode -ne 0) {
        throw "Published self-test failed with exit code $($selfTest.ExitCode)."
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $publish "EventFast.exe.sha256") -Value "$hash  EventFast.exe" -Encoding ascii
    Write-Output "Release candidate verified: $exe"
    Write-Output "Cold UI startup: $($startup.ElapsedMilliseconds) ms"
    Write-Output "SHA256: $hash"
    Write-Output "Manual Clean Windows and Event Viewer comparison gates remain required."
}
finally {
    Pop-Location
}

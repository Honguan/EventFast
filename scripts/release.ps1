param(
    [string]$OutputDirectory = "artifacts/release-candidate",
    [switch]$ExtendedChecks
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
    Invoke-DotNet -Arguments @("run", "--project", "tests/EventFast.Tests", "-c", "Release", "--", "--integration", "--ui")

    New-Item -ItemType Directory -Path $output | Out-Null
    if ($ExtendedChecks) {
        Invoke-DotNet -Arguments @("run", "--project", "tests/EventFast.Tests", "-c", "Release", "--", "--excel", "--leak")
        $benchmark = Join-Path $output "benchmark.txt"
        & $dotnet run --project benchmarks/EventFast.Benchmarks -c Release -- --large | Tee-Object -FilePath $benchmark
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Optional benchmark failed with exit code $LASTEXITCODE."
        }
    }

    $publish = Join-Path $output "win-x64"
    Invoke-DotNet -Arguments @("publish", "-p:PublishProfile=win-x64", "-p:PublishDir=$publish\")
    $files = @(Get-ChildItem -LiteralPath $publish -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne "EventFast.exe") {
        throw "Single-file verification failed: $($files.Name -join ', ')"
    }

    $exe = $files[0].FullName
    if (-not (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) -or
        -not (Get-Command Get-NetUDPEndpoint -ErrorAction SilentlyContinue)) {
        throw "NetTCPIP cmdlets are required for the local-only verification."
    }
    $startupTimes = @()
    foreach ($iteration in 1..3) {
        $extract = Join-Path $output "bundle-extract-$iteration"
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
            $startupTimes += $startup.ElapsedMilliseconds
            $tcp = @(Get-NetTCPConnection -OwningProcess $ui.Id -ErrorAction SilentlyContinue)
            $udp = @(Get-NetUDPEndpoint -OwningProcess $ui.Id -ErrorAction SilentlyContinue)
            if ($tcp.Count -ne 0 -or $udp.Count -ne 0) {
                throw "Published UI opened network endpoints: TCP $($tcp.Count), UDP $($udp.Count)."
            }
            if (-not $ui.CloseMainWindow()) {
                throw "Published UI rejected graceful close."
            }
            if (-not $ui.WaitForExit(2000)) {
                throw "Published UI did not exit within 2 seconds after graceful close."
            }
        }
        finally {
            if ($ui -and -not $ui.HasExited) {
                Stop-Process -Id $ui.Id
            }
            Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    $startupMedian = ($startupTimes | Sort-Object)[1]
    Set-Content -LiteralPath (Join-Path $output "startup.txt") -Value "Cold UI startup: $($startupTimes -join ', ') ms; median $startupMedian ms" -Encoding ascii
    Set-Content -LiteralPath (Join-Path $output "privacy.txt") -Value "Three launches, network endpoints: TCP 0, UDP 0" -Encoding ascii
    Set-Content -LiteralPath (Join-Path $output "lifecycle.txt") -Value "Three launches exited within 2 seconds after graceful close" -Encoding ascii

    $selfTest = Start-Process -FilePath $exe -ArgumentList "--self-test" -PassThru -Wait -WindowStyle Hidden
    if ($selfTest.ExitCode -ne 0) {
        throw "Published self-test failed with exit code $($selfTest.ExitCode)."
    }

    [xml]$project = Get-Content -LiteralPath (Join-Path $workspace "EventFast.csproj")
    $version = $project.Project.PropertyGroup.Version
    $assetName = "EventFast-v$version-win-x64.exe"
    $asset = Join-Path $output $assetName
    Copy-Item -LiteralPath $exe -Destination $asset
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $asset).Hash.ToLowerInvariant()
    Set-Content -LiteralPath (Join-Path $output "EventFast-v$version-win-x64.sha256") -Value "$hash  $assetName" -Encoding ascii
    Write-Output "Release candidate verified: $asset"
    Write-Output "Cold UI startup: $($startupTimes -join ', ') ms; median $startupMedian ms"
    Write-Output "Graceful close: 3/3 exited within 2 seconds"
    Write-Output "SHA256: $hash"
    Write-Output "Optional CI, clean-machine, large-EVTX, Event Viewer comparison, and GUI bridge checks do not block release."
}
finally {
    Pop-Location
}

param(
    [string]$Distribution = "Ubuntu-24.04",
    [ValidateRange(1, 64)]
    [int]$SizeMB = 4
)

$ErrorActionPreference = "Stop"
$workspace = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$dotnet = Join-Path $workspace ".tools\dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = "dotnet"
}

$linuxDirectory = (& wsl.exe -d $Distribution -- mktemp -d /tmp/eventfast-diskfull.XXXXXX).Trim()
if ($LASTEXITCODE -ne 0 -or $linuxDirectory -notmatch '^/tmp/eventfast-diskfull\.[A-Za-z0-9]+$') {
    throw "Failed to create a safe WSL test directory: $linuxDirectory"
}

$mounted = $false
try {
    $mountOptions = "size=${SizeMB}m,nodev,nosuid,noexec"
    & wsl.exe -d $Distribution -- sudo mount -t tmpfs -o $mountOptions tmpfs $linuxDirectory
    if ($LASTEXITCODE -ne 0) { throw "tmpfs mount failed." }
    $mounted = $true

    $uid = (& wsl.exe -d $Distribution -- id -u).Trim()
    $gid = (& wsl.exe -d $Distribution -- id -g).Trim()
    & wsl.exe -d $Distribution -- sudo chown "${uid}:${gid}" $linuxDirectory
    if ($LASTEXITCODE -ne 0) { throw "tmpfs chown failed." }

    $uncDirectory = "\\wsl.localhost\$Distribution" + ($linuxDirectory -replace '/', '\')
    if (-not (Test-Path -LiteralPath $uncDirectory)) {
        throw "WSL tmpfs is not visible from Windows: $uncDirectory"
    }

    $output = Join-Path $uncDirectory "actual-disk-full.xlsx"
    & $dotnet run --project (Join-Path $workspace "tests\EventFast.Tests") -c Release -- --actual-disk-full $output
    if ($LASTEXITCODE -ne 0) { throw "Actual disk-full test failed." }
}
finally {
    if ($mounted) {
        & wsl.exe -d $Distribution -- sudo umount $linuxDirectory
        if ($LASTEXITCODE -ne 0) { throw "Failed to unmount test tmpfs: $linuxDirectory" }
    }
    & wsl.exe -d $Distribution -- rmdir $linuxDirectory
    if ($LASTEXITCODE -ne 0) { throw "Failed to remove test directory: $linuxDirectory" }
}

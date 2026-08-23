param(
    [string]$OutputPath = "artifacts/testdata/security_big_sample.evtx"
)

$ErrorActionPreference = "Stop"
$workspace = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$output = [IO.Path]::GetFullPath((Join-Path $workspace $OutputPath))
$expected = "b3f8498d8a99740f7381518fd332cbb67c0bfed0a5b4320d407e485b3ee682fb"
$url = "https://raw.githubusercontent.com/Yamato-Security/hayabusa-evtx/main/samples/security_big_sample.evtx"

if (-not $output.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Output path must be inside the repository: $output"
}

New-Item -ItemType Directory -Force -Path (Split-Path $output) | Out-Null
if (-not (Test-Path -LiteralPath $output)) {
    Invoke-WebRequest -Headers @{ "User-Agent" = "EventFast-validation" } -Uri $url -OutFile $output
}

$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $output).Hash.ToLowerInvariant()
if ($actual -ne $expected) {
    throw "Large EVTX SHA256 mismatch. Expected $expected, got $actual."
}

Write-Output "Verified test data: $output"
Write-Output "SHA256: $actual"

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?$')]
    [string]$Version,
    [string]$BuildDirectory = (Join-Path $PSScriptRoot '..\artifacts\bin'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\release')
)

$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path ([IO.Path]::GetFullPath($BuildDirectory)) 'GpuPowerSlider.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw 'Build the app before packaging it.' }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$name = "GpuPowerSlider-$Version-win-x64"
$bundlePath = Join-Path $outputPath $name
New-Item -ItemType Directory -Path $bundlePath -Force | Out-Null

# Deliberately include only the app and public documentation. Backend binaries,
# driver resolver caches, logs and the test executable never enter the release.
Copy-Item -LiteralPath $exePath -Destination (Join-Path $bundlePath 'GpuPowerSlider.exe') -Force
foreach ($file in @('README.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $repoPath $file) -Destination (Join-Path $bundlePath $file) -Force
}
$files = @('GpuPowerSlider.exe', 'README.md', 'LICENSE') | ForEach-Object { Join-Path $bundlePath $_ }
$zipPath = Join-Path $outputPath "$name.zip"
Compress-Archive -LiteralPath $files -DestinationPath $zipPath -Force
$checksums = @($exePath, $zipPath) | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path -Leaf $_)
}
[IO.File]::WriteAllText((Join-Path $outputPath 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n", [Text.Encoding]::ASCII)
Write-Output "Release archive: $zipPath"

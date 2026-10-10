[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\bin'),
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework 4.x C# compiler is required.'
}
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

Push-Location $PSScriptRoot
try {
    $appPath = Join-Path $outputPath 'GpuPowerSlider.exe'
    & $compiler /nologo /platform:x64 /target:winexe /r:System.Drawing.dll /r:System.Windows.Forms.dll /win32manifest:app.manifest "/out:$appPath" Backend.cs MainForm.cs Program.cs
    if ($LASTEXITCODE -ne 0) { throw 'App build failed' }

    $testManifest = Join-Path $outputPath 'test.manifest'
    (Get-Content -LiteralPath 'app.manifest' -Raw).Replace('requireAdministrator','asInvoker') | Set-Content -LiteralPath $testManifest -Encoding UTF8
    $testPath = Join-Path $outputPath 'SmokeTests.exe'
    & $compiler /nologo /platform:x64 /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll "/win32manifest:$testManifest" "/out:$testPath" /main:GpuPowerSlider.Tests Backend.cs MainForm.cs Tests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }

    if ($Test) {
        & $testPath | Tee-Object -FilePath (Join-Path $outputPath 'test-output.txt')
        if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed' }
    }
    Write-Output "Build output: $outputPath"
} finally { Pop-Location }

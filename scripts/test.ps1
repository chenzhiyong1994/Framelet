param(
    [string]$DotnetPath = $env:DOTNET_EXE,
    [switch]$WithRecording
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $DotnetPath) { $command = Get-Command dotnet -ErrorAction SilentlyContinue; if ($command) { $DotnetPath = $command.Source } }
if (-not $DotnetPath) { throw '请通过 -DotnetPath 或 DOTNET_EXE 指定 .NET 10 SDK。' }
& (Join-Path $PSScriptRoot 'build.ps1') -DotnetPath $DotnetPath
$testOutput = Join-Path $projectRoot 'artifacts\self-tests'
$dll = Join-Path $projectRoot 'src\CaptureReplica\bin\Release\net10.0-windows\Framelet.dll'
$appArgs = @('--self-test', '--output', $testOutput)
if ($WithRecording) { $appArgs += '--with-recording' }
& $DotnetPath $dll @appArgs
if ($LASTEXITCODE -ne 0) { throw "应用自检失败：$testOutput\results.json" }
& $DotnetPath run --project (Join-Path $projectRoot 'src\CaptureReplica\Capture\Verification\CaptureChecks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw '捕捉/拼接验证失败。' }
Get-Content -LiteralPath (Join-Path $testOutput 'results.json')

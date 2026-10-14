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
$resultPath = Join-Path $testOutput 'results.json'
# 不将上次运行的报告误认为本次结果。
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$dll = Join-Path $projectRoot 'src\Framelet\bin\Release\net10.0-windows\Framelet.dll'
$appArgs = @('--self-test', '--output', $testOutput)
if ($WithRecording) { $appArgs += '--with-recording' }
& $DotnetPath $dll @appArgs
$selfTestExitCode = $LASTEXITCODE
$hasReport = Test-Path -LiteralPath $resultPath
if ($hasReport) { Get-Content -LiteralPath $resultPath }
else { Write-Warning "应用自检未生成报告：$resultPath（退出码 $selfTestExitCode）。" }
if ($selfTestExitCode -ne 0) { throw "应用自检失败（退出码 $selfTestExitCode）：$resultPath" }
if (-not $hasReport) { throw "应用自检报告缺失：$resultPath" }
& $DotnetPath run --project (Join-Path $projectRoot 'src\Framelet\Capture\Verification\CaptureChecks.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw '捕捉/拼接验证失败。' }

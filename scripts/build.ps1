param(
    [string]$DotnetPath = $env:DOTNET_EXE,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $DotnetPath) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { $DotnetPath = $command.Source }
}
if (-not $DotnetPath -or -not (Test-Path -LiteralPath $DotnetPath)) {
    throw '需要 .NET SDK 10.0.401。请提供 -DotnetPath 或设置 DOTNET_EXE。'
}
$sdk = & $DotnetPath --version
if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.0\.4\d\d$') { throw "不支持的 SDK：$sdk；请使用 global.json 指定的 10.0.4xx。" }
$project = Join-Path $projectRoot 'src\CaptureReplica\CaptureReplica.csproj'
Push-Location $projectRoot
try {
    & $DotnetPath build $project -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
    if ($Publish) {
        $destination = Join-Path $projectRoot 'dist\Framelet'
        & $DotnetPath publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $destination --nologo
        if ($LASTEXITCODE -ne 0) { throw '发布失败。' }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $destination 'README.md')
        foreach ($notice in @('LICENSE', 'THIRD-PARTY-NOTICES.md')) {
            Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination (Join-Path $destination $notice) -Force
        }
        # 从实际解析的运行时包保留许可；不依赖某台电脑的 NuGet 路径。
        $runtimeJson = & $DotnetPath msbuild $project -target:ResolveFrameworkReferences -property:RuntimeIdentifier=win-x64 -property:SelfContained=true -getItem:ResolvedRuntimePack -nologo
        if ($LASTEXITCODE -ne 0) { throw '无法解析运行时许可来源。' }
        $runtimePacks = ($runtimeJson | Out-String | ConvertFrom-Json).Items.ResolvedRuntimePack
        foreach ($framework in @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App')) {
            $pack = @($runtimePacks | Where-Object FrameworkName -eq $framework)
            if ($pack.Count -ne 1 -or -not $pack[0].PackageDirectory) { throw "无法确定运行时包：$framework" }
            $pack = $pack[0]
            $notices = @(Get-ChildItem -LiteralPath $pack.PackageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\..*)?$' })
            if (-not ($notices | Where-Object Name -match '^LICENSE')) { throw "运行时缺少许可证：$framework" }
            $licenseDestination = Join-Path $destination ("licenses/{0}/{1}" -f $pack.Identity, $pack.NuGetPackageVersion)
            New-Item -ItemType Directory -Path $licenseDestination -Force | Out-Null
            foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $licenseDestination -Force }
        }
        # 按相对文件路径覆盖，重复发布时不会产生 docs/docs 嵌套。
        $documentationSource = Join-Path $projectRoot 'docs'
        $documentationDestination = Join-Path $destination 'docs'
        foreach ($document in Get-ChildItem -LiteralPath $documentationSource -File -Recurse) {
            $relativePath = $document.FullName.Substring($documentationSource.Length + 1)
            $documentDestination = Join-Path $documentationDestination $relativePath
            New-Item -ItemType Directory -Path (Split-Path -Parent $documentDestination) -Force | Out-Null
            Copy-Item -LiteralPath $document.FullName -Destination $documentDestination -Force
        }
        $brandDestination = Join-Path $destination 'brand'
        New-Item -ItemType Directory -Path $brandDestination -Force | Out-Null
        foreach ($asset in @('Framelet.svg', 'Framelet-wordmark.svg', 'Framelet.png', 'Framelet.ico')) {
            Copy-Item -LiteralPath (Join-Path $projectRoot "src\CaptureReplica\Assets\$asset") -Destination (Join-Path $brandDestination $asset) -Force
        }
        Write-Output "便携程序：$destination\Framelet.exe"
    }
} finally { Pop-Location }

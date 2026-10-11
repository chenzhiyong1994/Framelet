param([string]$AssetsDirectory = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AssetsDirectory)) { $AssetsDirectory = Join-Path $projectRoot 'src\CaptureReplica\Assets' }
$assets = [IO.Path]::GetFullPath($AssetsDirectory)
New-Item -ItemType Directory -Path $assets -Force | Out-Null

# Original geometry in a shared 32-unit coordinate system. No third-party assets.
$palette = [ordered]@{
    WarmWhite = '#F5F4EF'; Ink = '#1C302E'; Accent = '#147D70'; AccentPale = '#E5F2EC'
    Muted = '#73817B'; Border = '#D9E1DB'; Canvas = '#EAEFEB'; Coral = '#E57761'
}

function New-RoundedPath([single]$X, [single]$Y, [single]$Width, [single]$Height, [single]$Radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    [single]$diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-FrameletBitmap([int]$Size) {
    # 4x sampling plus optical weight adjustment for the 16/24 px variants.
    $large = [Drawing.Bitmap]::new($Size * 4, $Size * 4, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($large)
    $brushes = [Collections.Generic.List[Drawing.SolidBrush]]::new()
    $paths = [Collections.Generic.List[Drawing.Drawing2D.GraphicsPath]]::new()
    $pen = $null
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.ScaleTransform([single]($Size * 4 / 32), [single]($Size * 4 / 32))
        foreach ($key in @('Accent', 'WarmWhite', 'AccentPale', 'Ink')) {
            $brushes.Add([Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($palette[$key])))
        }
        $tile = New-RoundedPath 1 1 30 30 6.5
        $paths.Add($tile)
        $graphics.FillPath($brushes[0], $tile)
        [single]$weight = if ($Size -le 24) { 3.0 } else { 2.5 }
        $pen = [Drawing.Pen]::new($brushes[1].Color, $weight)
        $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        $corners = @(
            @([Drawing.PointF]::new(14, 8), [Drawing.PointF]::new(8, 8), [Drawing.PointF]::new(8, 14)),
            @([Drawing.PointF]::new(8, 18), [Drawing.PointF]::new(8, 24), [Drawing.PointF]::new(14, 24)),
            @([Drawing.PointF]::new(18, 24), [Drawing.PointF]::new(24, 24), [Drawing.PointF]::new(24, 18))
        )
        foreach ($corner in $corners) { $graphics.DrawLines($pen, [Drawing.PointF[]]$corner) }
        $graphics.FillPolygon($brushes[2], [Drawing.PointF[]]@(
            [Drawing.PointF]::new(18, 8), [Drawing.PointF]::new(24, 8), [Drawing.PointF]::new(24, 14)
        ))
        $fold = [Drawing.Drawing2D.GraphicsPath]::new()
        $paths.Add($fold)
        $fold.AddLine(18, 8, 24, 14)
        $fold.AddLine(24, 14, 20, 14)
        # Cubic equivalent of SVG Q18 14 18 12.
        $fold.AddBezier(20, 14, [single](18 + 2.0 / 3), 14, 18, [single](12 + 4.0 / 3), 18, 12)
        $fold.CloseFigure()
        $graphics.FillPath($brushes[3], $fold)
    }
    finally {
        if ($null -ne $pen) { $pen.Dispose() }
        foreach ($path in $paths) { $path.Dispose() }
        foreach ($brush in $brushes) { $brush.Dispose() }
        $graphics.Dispose()
    }
    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $downsample = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $downsample.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $downsample.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $downsample.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $downsample.DrawImage($large, [Drawing.Rectangle]::new(0, 0, $Size, $Size))
    }
    finally { $downsample.Dispose(); $large.Dispose() }
    return $bitmap
}

$symbol = @'
  <rect x="1" y="1" width="30" height="30" rx="6.5" fill="#147D70"/>
  <path d="M14 8H8V14 M8 18V24H14 M18 24H24V18" fill="none" stroke="#F5F4EF" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M18 8H24V14Z" fill="#E5F2EC"/>
  <path d="M18 8L24 14H20Q18 14 18 12Z" fill="#1C302E"/>
'@
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 32 32" role="img" aria-labelledby="title desc">
  <title id="title">帧简 Framelet</title>
  <desc id="desc">三个取景角与一个折角构成留白的图像帧。</desc>
$symbol
</svg>
"@
$wordmark = @"
<svg xmlns="http://www.w3.org/2000/svg" width="404" height="96" viewBox="0 0 404 96" role="img" aria-labelledby="title desc">
  <title id="title">帧简 Framelet</title>
  <desc id="desc">帧简 Framelet 横向品牌标志，透明背景。</desc>
  <g transform="translate(0 8) scale(2.5)">
$symbol
  </g>
  <text x="101" y="47" fill="#1C302E" font-family="Microsoft YaHei UI, PingFang SC, sans-serif" font-size="36" font-weight="600" letter-spacing="2">帧简</text>
  <text x="103" y="76" fill="#73817B" font-family="Segoe UI, Arial, sans-serif" font-size="20" letter-spacing="1">Framelet</text>
</svg>
"@
[IO.File]::WriteAllText((Join-Path $assets 'Framelet.svg'), $svg, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $assets 'Framelet-wordmark.svg'), $wordmark, [Text.UTF8Encoding]::new($false))

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$payloads = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $bitmap = New-FrameletBitmap $size
    $memory = [IO.MemoryStream]::new()
    try {
        $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
        $payloads.Add($memory.ToArray())
        if ($size -eq 256) { $bitmap.Save((Join-Path $assets 'Framelet.png'), [Drawing.Imaging.ImageFormat]::Png) }
    }
    finally { $memory.Dispose(); $bitmap.Dispose() }
}
$iconPath = Join-Path $assets 'Framelet.ico'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($iconPath))
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    [uint32]$offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        [byte]$dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write($dimension)
        $writer.Write($dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$payloads[$index].Length)
        $writer.Write($offset)
        $offset += $payloads[$index].Length
    }
    foreach ($payload in $payloads) { $writer.Write($payload) }
}
finally { $writer.Dispose() }

# Validate directory, PNG frames and the platform's ICO decoder.
$reader = [IO.BinaryReader]::new([IO.File]::OpenRead($iconPath))
try {
    if ($reader.ReadUInt16() -ne 0 -or $reader.ReadUInt16() -ne 1 -or $reader.ReadUInt16() -ne $sizes.Count) { throw 'Invalid ICO header.' }
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $width = $reader.ReadByte()
        $height = $reader.ReadByte()
        $null = $reader.ReadUInt16()
        $planes = $reader.ReadUInt16()
        $bits = $reader.ReadUInt16()
        $length = $reader.ReadUInt32()
        $entryOffset = $reader.ReadUInt32()
        if ($width -eq 0) { $width = 256 }
        if ($height -eq 0) { $height = 256 }
        if ($width -ne $sizes[$index] -or $height -ne $sizes[$index] -or $planes -ne 1 -or $bits -ne 32) { throw "Invalid ICO directory entry $index." }
        $directoryPosition = $reader.BaseStream.Position
        $reader.BaseStream.Position = $entryOffset
        $png = $reader.ReadBytes($length)
        if ([Convert]::ToHexString($png[0..7]) -ne '89504E470D0A1A0A') { throw 'ICO payload is not PNG.' }
        $pngWidth = ([uint32]$png[16] -shl 24) -bor ([uint32]$png[17] -shl 16) -bor ([uint32]$png[18] -shl 8) -bor [uint32]$png[19]
        $pngHeight = ([uint32]$png[20] -shl 24) -bor ([uint32]$png[21] -shl 16) -bor ([uint32]$png[22] -shl 8) -bor [uint32]$png[23]
        if ($pngWidth -ne $width -or $pngHeight -ne $height) { throw 'PNG dimensions do not match ICO directory.' }
        $reader.BaseStream.Position = $directoryPosition
        $payloadStream = [IO.MemoryStream]::new($png)
        $decodedFrame = [Drawing.Image]::FromStream($payloadStream)
        try { if ($decodedFrame.Size -ne [Drawing.Size]::new($width, $height)) { throw 'PNG decoder returned unexpected dimensions.' } }
        finally { $decodedFrame.Dispose(); $payloadStream.Dispose() }
        # System.Drawing.Icon's best-fit selection does not select the 256 px
        # zero-byte-dimension entry reliably. Decode that PNG directly above;
        # validate platform icon selection for all other physical icon sizes.
        if ($width -lt 256) {
            $decodedIcon = [Drawing.Icon]::new($iconPath, [Drawing.Size]::new($width, $height))
            try { if ($decodedIcon.Size -ne [Drawing.Size]::new($width, $height)) { throw 'Icon decoder chose an unexpected frame.' } }
            finally { $decodedIcon.Dispose() }
        }
    }
}
finally { $reader.Dispose() }
foreach ($filename in @('Framelet.svg', 'Framelet-wordmark.svg')) {
    $document = [xml][IO.File]::ReadAllText((Join-Path $assets $filename))
    if ($document.DocumentElement.LocalName -ne 'svg' -or $document.DocumentElement.NamespaceURI -ne 'http://www.w3.org/2000/svg') { throw "Invalid SVG: $filename" }
}

# Decode actual ICO layers into a native-size preview sheet.
$preview = [Drawing.Bitmap]::new(1056, 426)
$previewGraphics = [Drawing.Graphics]::FromImage($preview)
$titleFont = [Drawing.Font]::new('Microsoft YaHei UI', 34, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
$nameFont = [Drawing.Font]::new('Segoe UI', 25, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$smallFont = [Drawing.Font]::new('Segoe UI', 14, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
$inkBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($palette.Ink))
$mutedBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($palette.Muted))
try {
    $previewGraphics.Clear([Drawing.ColorTranslator]::FromHtml($palette.WarmWhite))
    $previewGraphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $previewGraphics.DrawString('帧简', $titleFont, $inkBrush, 42, 30)
    $previewGraphics.DrawString('Framelet', $nameFont, $mutedBrush, 141, 37)
    $previewGraphics.DrawString('Original frame + folded corner', $smallFont, $mutedBrush, 42, 80)
    [int]$x = 44
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $frameStream = [IO.MemoryStream]::new($payloads[$index])
        $decoded = [Drawing.Image]::FromStream($frameStream)
        try {
            [int]$y = 355 - $size
            $previewGraphics.DrawImageUnscaled($decoded, $x, $y)
            $previewGraphics.DrawString("$size px", $smallFont, $mutedBrush, $x, 368)
            $x += [Math]::Max(74, $size + 32)
        }
        finally { $decoded.Dispose(); $frameStream.Dispose() }
    }
    $preview.Save((Join-Path $assets 'Framelet-preview.png'), [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $inkBrush.Dispose(); $mutedBrush.Dispose()
    $titleFont.Dispose(); $nameFont.Dispose(); $smallFont.Dispose()
    $previewGraphics.Dispose(); $preview.Dispose()
}
Write-Output 'Framelet assets generated and verified: ICO 16/24/32/48/64/128/256 px, SVG, wordmark SVG, PNG and decoded-icon preview.'
Write-Output "Asset directory: $assets"

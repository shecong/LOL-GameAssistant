# Regenerate the editable SVG, PNG preview and multi-resolution Windows icon.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$iconDirectory = Join-Path $PSScriptRoot '../LOL-GameAssistant/Resources/Brand'
[IO.Directory]::CreateDirectory($iconDirectory) | Out-Null
$iconDirectory = [IO.Path]::GetFullPath($iconDirectory)
$letterS = '58,72 130,72 124,94 75,94 69,115 106,115 119,128 106,176 27,176 33,154 85,154 90,137 54,137 41,124 52,85'
$letterC = '154,72 229,72 223,96 169,96 154,152 208,152 202,176 132,176 119,162 139,87'
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <title>SC application icon</title>
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="1" y2="1">
      <stop stop-color="#193B62"/><stop offset="1" stop-color="#081426"/>
    </linearGradient>
    <linearGradient id="letters" x1="0" y1="0" x2="0" y2="1">
      <stop stop-color="#F4FCFF"/><stop offset="1" stop-color="#89D9FF"/>
    </linearGradient>
    <linearGradient id="accent"><stop stop-color="#16B9F4"/><stop offset="1" stop-color="#3676F5"/></linearGradient>
  </defs>
  <rect x="8" y="8" width="240" height="240" rx="54" fill="url(#tile)" stroke="#356A96" stroke-width="2"/>
  <polygon points="$letterS" fill="url(#letters)"/>
  <polygon points="$letterC" fill="url(#letters)"/>
  <path d="M83 194 H176 L172 201 H79 Z" fill="url(#accent)"/>
</svg>
"@
[IO.File]::WriteAllText((Join-Path $iconDirectory 'sc-icon.svg'), $svg)

function Get-Polygon([string]$coordinates) {
    [System.Drawing.PointF[]]@($coordinates.Split(' ') | ForEach-Object {
        $pair = $_.Split(',')
        [System.Drawing.PointF]::new([single]$pair[0], [single]$pair[1])
    })
}

function New-IconFrame([int]$size) {
    # Supersample every size separately so small taskbar icons retain clean edges.
    $canvas = [Drawing.Bitmap]::new($size * 4, $size * 4)
    $graphics = [Drawing.Graphics]::FromImage($canvas)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 64.0, $size / 64.0)
    $tile = [Drawing.Drawing2D.GraphicsPath]::new()
    foreach ($arc in @(@(8,8,180), @(140,8,270), @(140,140,0), @(8,140,90))) {
        $tile.AddArc($arc[0], $arc[1], 108, 108, $arc[2], 90)
    }
    $tile.CloseFigure()
    $tileBrush = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.Point]::new(8,8), [Drawing.Point]::new(248,248),
        [Drawing.ColorTranslator]::FromHtml('#193B62'), [Drawing.ColorTranslator]::FromHtml('#081426'))
    $edge = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#356A96'), 2)
    $graphics.FillPath($tileBrush, $tile)
    $graphics.DrawPath($edge, $tile)
    $letterBrush = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.Point]::new(0,72), [Drawing.Point]::new(0,176),
        [Drawing.ColorTranslator]::FromHtml('#F4FCFF'), [Drawing.ColorTranslator]::FromHtml('#89D9FF'))
    $graphics.FillPolygon($letterBrush, (Get-Polygon $letterS))
    $graphics.FillPolygon($letterBrush, (Get-Polygon $letterC))
    $accent = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.Point]::new(79,194), [Drawing.Point]::new(176,201),
        [Drawing.ColorTranslator]::FromHtml('#16B9F4'), [Drawing.ColorTranslator]::FromHtml('#3676F5'))
    $graphics.FillPolygon($accent, (Get-Polygon '83,194 176,194 172,201 79,201'))
    $graphics.Dispose()
    $tile.Dispose(); $tileBrush.Dispose(); $edge.Dispose(); $letterBrush.Dispose(); $accent.Dispose()
    $frame = [Drawing.Bitmap]::new($size, $size)
    $target = [Drawing.Graphics]::FromImage($frame)
    $target.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $target.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $target.DrawImage($canvas, [Drawing.Rectangle]::new(0,0,$size,$size), 0,0,$canvas.Width,$canvas.Height,[Drawing.GraphicsUnit]::Pixel)
    $target.Dispose(); $canvas.Dispose()
    return ,$frame
}

$sizes = @(16,20,24,32,40,48,64,128,256)
$frames = @($sizes | ForEach-Object {
    $frame = New-IconFrame $_
    if ($_ -eq 256) { $frame.Save((Join-Path $iconDirectory 'sc-icon.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $stream = [IO.MemoryStream]::new()
    $frame.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose(); $frame.Dispose()
    ,$bytes
})
$file = [IO.File]::Create((Join-Path $iconDirectory 'sc-icon.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frameBytes in $frames) { $writer.Write([byte[]]$frameBytes) }
}
finally { $writer.Dispose() }
Write-Output "Created SC icon (16, 20, 24, 32, 40, 48, 64, 128, 256 px) in $iconDirectory"

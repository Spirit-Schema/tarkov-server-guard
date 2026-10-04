# Generates the compact orange TSG wordmark from a native pixel-letter design.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$brandRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\branding'
$glyphs = @('1111/0010/0010/0010/0010/0010/0010', '1111/1000/1000/1111/0001/0001/1111', '1111/1000/1000/1011/1001/1001/1111')
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$entries = New-Object 'Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bitmap = New-Object Drawing.Bitmap($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $brush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255, 239, 154, 57))
    $scale = [Math]::Max(1, [Math]::Floor($size / 16))
    $left = [int][Math]::Floor(($size - 14 * $scale) / 2)
    $top = [int][Math]::Floor(($size - 7 * $scale) / 2)
    for ($letter = 0; $letter -lt 3; $letter++) {
        $rows = $glyphs[$letter].Split('/')
        for ($y = 0; $y -lt 7; $y++) {
            for ($x = 0; $x -lt 4; $x++) {
                if ($rows[$y][$x] -eq '1') {
                    $graphics.FillRectangle($brush, [int]($left + ($letter * 5 + $x) * $scale), [int]($top + $y * $scale), [int]$scale, [int]$scale)
                }
            }
        }
    }
    $memory = New-Object IO.MemoryStream
    $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
    $entries.Add($memory.ToArray())
    if ($size -eq 16 -or $size -eq 32) { $bitmap.Save((Join-Path $brandRoot ('wordmark-' + $size + '.png'))) }
    $memory.Dispose()
    $brush.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}
$stream = [IO.File]::Create((Join-Path $brandRoot 'tarkov-server-guard-wordmark.ico'))
$writer = New-Object IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$entries[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $entries[$index].Length
    }
    foreach ($entry in $entries) { $writer.Write($entry) }
} finally { $writer.Dispose() }

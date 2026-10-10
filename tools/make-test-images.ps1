# 生成一组测试图，覆盖：中文/中英混排文字、不同格式、不同尺寸、自然排序。
# 用法：powershell -ExecutionPolicy Bypass -File tools\make-test-images.ps1

[CmdletBinding()]
param([string]$OutputDir)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Drawing

# $PSScriptRoot 在 param 默认值中取不到，放到脚本体里求值
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $PSScriptRoot '..\test-images'
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
$OutputDir = (Resolve-Path $OutputDir).Path

function New-TextImage {
    param(
        [int]$Width,
        [int]$Height,
        [string[]]$Lines,
        [int]$FontSize = 22,
        [string]$FileName,
        [System.Drawing.Imaging.ImageFormat]$Format
    )

    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $font = New-Object System.Drawing.Font 'Microsoft YaHei', $FontSize
    $brush = [System.Drawing.Brushes]::Black
    $y = 40

    foreach ($line in $Lines) {
        $g.DrawString($line, $font, $brush, 40, $y)
        $y += $FontSize * 2.2
    }

    $font.Dispose()
    $g.Dispose()

    $path = Join-Path $OutputDir $FileName
    $bmp.Save($path, $Format)
    $bmp.Dispose()

    Write-Host "  $FileName" -ForegroundColor Green
}

function New-GradientImage {
    param([int]$Width, [int]$Height, [string]$FileName, [System.Drawing.Imaging.ImageFormat]$Format)

    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)

    $rect = New-Object System.Drawing.Rectangle 0, 0, $Width, $Height
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect, [System.Drawing.Color]::MidnightBlue, [System.Drawing.Color]::Orange, 45.0)
    $g.FillRectangle($brush, $rect)

    # 画一组细网格，用来肉眼检查放大时像素是否锐利
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 1
    for ($x = 0; $x -lt $Width; $x += 16) { $g.DrawLine($pen, $x, 0, $x, $Height) }
    for ($y = 0; $y -lt $Height; $y += 16) { $g.DrawLine($pen, 0, $y, $Width, $y) }

    $pen.Dispose(); $brush.Dispose(); $g.Dispose()

    $path = Join-Path $OutputDir $FileName
    $bmp.Save($path, $Format)
    $bmp.Dispose()

    Write-Host "  $FileName" -ForegroundColor Green
}

Write-Host "输出目录: $OutputDir" -ForegroundColor Cyan
Write-Host '生成测试图：' -ForegroundColor Cyan

$png = [System.Drawing.Imaging.ImageFormat]::Png
$jpeg = [System.Drawing.Imaging.ImageFormat]::Jpeg
$bmpFmt = [System.Drawing.Imaging.ImageFormat]::Bmp
$gif = [System.Drawing.Imaging.ImageFormat]::Gif
$tiff = [System.Drawing.Imaging.ImageFormat]::Tiff

# 文件名刻意用 1 / 2 / 10，验证自然排序（10 必须排在 2 之后）
New-TextImage -Width 900 -Height 420 -FileName 'sample-1.png' -Format $png -Lines @(
    '这是一段纯中文的测试文字内容',
    '第二行用来验证跨行选择功能',
    '第三行文字 可以从这里拖选到上一行'
)

New-TextImage -Width 900 -Height 420 -FileName 'sample-2.png' -Format $png -Lines @(
    '中英混排 mixed content 测试 OCR engine 效果',
    'The quick brown fox jumps over 13 lazy dogs.',
    'Email: test@example.com  Tel: 138-0000-1234'
)

New-TextImage -Width 1200 -Height 500 -FileName 'sample-10.jpg' -Format $jpeg -FontSize 28 -Lines @(
    'JPEG 格式测试图片',
    '验证 WIC 解码与 EXIF 处理'
)

New-TextImage -Width 800 -Height 300 -FileName 'sample-3.bmp' -Format $bmpFmt -Lines @(
    'BMP 格式测试',
    '无压缩位图'
)

New-TextImage -Width 800 -Height 300 -FileName 'sample-4.gif' -Format $gif -Lines @(
    'GIF 格式测试',
    '索引色位图'
)

New-TextImage -Width 800 -Height 300 -FileName 'sample-5.tif' -Format $tiff -Lines @(
    'TIFF 格式测试'
)

# 大图：验证缩放性能与放大时的像素锐利度
New-GradientImage -Width 4000 -Height 3000 -FileName 'sample-6-large.png' -Format $png

# 小图：验证「适应窗口时不放大小图」
New-GradientImage -Width 160 -Height 120 -FileName 'sample-7-tiny.png' -Format $png

Write-Host ''
Write-Host "共生成 $((Get-ChildItem $OutputDir -File).Count) 个文件" -ForegroundColor Cyan

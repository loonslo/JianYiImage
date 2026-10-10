# OCR 探针：验证系统 OCR 引擎的实际识别能力与词级坐标输出。
# 用途：在实现 WindowsOcrEngine 之前，先确认
#   1. zh-Hans 模型对英文 / 中英混排的识别质量（本机没装英文 OCR 语言包）
#   2. Words[].BoundingRect 是否真的返回可用的词级坐标（实况文本选择的基石）
#
# 用法：powershell -ExecutionPolicy Bypass -File tools\ocr-probe.ps1 [图片路径]
#       不传路径时自动生成一张中英混排测试图。

[CmdletBinding()]
param([string]$ImagePath)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# ---------- WinRT 异步调用 helper（PowerShell 5.1 调 IAsyncOperation 的经典做法）----------
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$asTaskGeneric = ([System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object {
        $_.Name -eq 'AsTask' -and
        $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
    })[0]

function Await($WinRtTask, $ResultType) {
    $asTask = $asTaskGeneric.MakeGenericMethod($ResultType)
    $netTask = $asTask.Invoke($null, @($WinRtTask))
    $netTask.Wait(-1) | Out-Null
    $netTask.Result
}

# ---------- 没给图就生成一张中英混排测试图 ----------
if (-not $ImagePath) {
    Add-Type -AssemblyName System.Drawing
    $ImagePath = Join-Path $env:TEMP 'ocr-probe-sample.png'

    $bmp = New-Object System.Drawing.Bitmap 900, 420
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::White)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

    $black = [System.Drawing.Brushes]::Black
    $fontCjk = New-Object System.Drawing.Font 'Microsoft YaHei', 22
    $fontEn = New-Object System.Drawing.Font 'Segoe UI', 22
    $fontSmall = New-Object System.Drawing.Font 'Microsoft YaHei', 13

    $g.DrawString('这是一段纯中文的测试文字内容', $fontCjk, $black, 40, 40)
    $g.DrawString('The quick brown fox jumps over 13 lazy dogs.', $fontEn, $black, 40, 110)
    $g.DrawString('中英混排 mixed content 测试 OCR engine 效果', $fontCjk, $black, 40, 180)
    $g.DrawString('小字号测试：验证 12px 左右的文字能否被正确识别', $fontSmall, $black, 40, 260)
    $g.DrawString('Email: test@example.com    Tel: 138-0000-1234', $fontEn, $black, 40, 320)

    $g.Dispose()
    $bmp.Save($ImagePath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "已生成测试图: $ImagePath" -ForegroundColor Cyan
}

if (-not (Test-Path $ImagePath)) { throw "图片不存在: $ImagePath" }

# ---------- 加载图片 → SoftwareBitmap ----------
[Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType = WindowsRuntime] | Out-Null
[Windows.Media.Ocr.OcrEngine, Windows.Foundation.UniversalApiContract, ContentType = WindowsRuntime] | Out-Null
[Windows.Globalization.Language, Windows.Foundation.UniversalApiContract, ContentType = WindowsRuntime] | Out-Null

$full = (Resolve-Path $ImagePath).Path
$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($full)) ([Windows.Storage.StorageFile])
$stream = Await ($file.OpenReadAsync()) ([Windows.Storage.Streams.IRandomAccessStreamWithContentType])
$decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
$softwareBitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])

Write-Host ''
Write-Host "图片尺寸 : $($decoder.PixelWidth) x $($decoder.PixelHeight)" -ForegroundColor Cyan
Write-Host "引擎上限 : $([Windows.Media.Ocr.OcrEngine]::MaxImageDimension)" -ForegroundColor Cyan

# ---------- OCR ----------
$engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage(
    (New-Object Windows.Globalization.Language 'zh-Hans'))
if (-not $engine) { throw 'zh-Hans OCR 引擎不可用' }

Write-Host "识别语言 : $($engine.RecognizerLanguage.LanguageTag)" -ForegroundColor Cyan

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$result = Await ($engine.RecognizeAsync($softwareBitmap)) ([Windows.Media.Ocr.OcrResult])
$sw.Stop()

Write-Host "识别耗时 : $($sw.ElapsedMilliseconds) ms" -ForegroundColor Green
Write-Host "识别行数 : $($result.Lines.Count)" -ForegroundColor Green
if ($null -ne $result.TextAngle) {
    Write-Host "文本倾角 : $($result.TextAngle)" -ForegroundColor Cyan
}
Write-Host ''

# ---------- 逐行 / 逐词输出，确认词级坐标可用 ----------
$lineIndex = 0
foreach ($line in $result.Lines) {
    Write-Host ("[行 {0}] {1}" -f $lineIndex, $line.Text) -ForegroundColor Yellow
    foreach ($word in $line.Words) {
        $r = $word.BoundingRect
        Write-Host ("        '{0}'  @ ({1,6:N1}, {2,6:N1})  {3,5:N1} x {4,5:N1}" -f `
                $word.Text, $r.X, $r.Y, $r.Width, $r.Height)
    }
    $lineIndex++
}

Write-Host ''
Write-Host '--- 拼接全文 ---' -ForegroundColor Cyan
Write-Host $result.Text

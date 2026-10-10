using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WinRtOcrEngine = Windows.Media.Ocr.OcrEngine;

namespace ImageViewer.Core.Ocr;

/// <summary>
/// 基于 Windows 自带 OCR（Windows.Media.Ocr）的识别实现。
///
/// 选它的理由：零额外体积、全程离线、实测 900x420 的图只要 68ms，
/// 而且原生返回词级 BoundingRect——实况文本选择正是靠这个。
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly WinRtOcrEngine _engine;

    private WindowsOcrEngine(WinRtOcrEngine engine)
    {
        _engine = engine;
        Language = new OcrLanguageInfo(
            engine.RecognizerLanguage.LanguageTag,
            engine.RecognizerLanguage.DisplayName);
    }

    public string Name => "Windows OCR";

    public OcrLanguageInfo Language { get; }

    public int MaxImageDimension => (int)WinRtOcrEngine.MaxImageDimension;

    /// <summary>
    /// 系统装了识别数据的全部语言。用户在系统设置里装上新的语言包后，
    /// 这里会自动多出对应选项，应用无需改动。
    /// </summary>
    public static IReadOnlyList<OcrLanguageInfo> GetAvailableLanguages()
    {
        try
        {
            return WinRtOcrEngine.AvailableRecognizerLanguages
                .Select(l => new OcrLanguageInfo(l.LanguageTag, l.DisplayName))
                .ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<OcrLanguageInfo>();
        }
    }

    /// <summary>
    /// 创建引擎。<paramref name="languageTag"/> 为空时按用户配置的语言选，
    /// 再不行就退回系统可用的第一个语言。全部失败返回 null。
    /// </summary>
    public static WindowsOcrEngine? TryCreate(string? languageTag = null)
    {
        try
        {
            WinRtOcrEngine? engine = null;

            if (!string.IsNullOrWhiteSpace(languageTag))
            {
                engine = WinRtOcrEngine.TryCreateFromLanguage(new Language(languageTag));
            }

            engine ??= WinRtOcrEngine.TryCreateFromUserProfileLanguages();

            if (engine is null)
            {
                var first = WinRtOcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (first is not null)
                {
                    engine = WinRtOcrEngine.TryCreateFromLanguage(first);
                }
            }

            return engine is null ? null : new WindowsOcrEngine(engine);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<OcrDocument> RecognizeAsync(
        SoftwareBitmap bitmap,
        double scaleToOriginal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (bitmap.PixelWidth > MaxImageDimension || bitmap.PixelHeight > MaxImageDimension)
        {
            throw new ArgumentException(
                $"位图边长超过引擎上限 {MaxImageDimension}，请先降采样后再识别。",
                nameof(bitmap));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = await _engine.RecognizeAsync(bitmap).AsTask(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return Convert(
            result,
            scaleToOriginal,
            (int)Math.Round(bitmap.PixelWidth * scaleToOriginal),
            (int)Math.Round(bitmap.PixelHeight * scaleToOriginal));
    }

    /// <summary>
    /// 把 WinRT 的识别结果转成 Core 的中立表示，顺便把坐标换算回原图坐标系。
    /// </summary>
    private static OcrDocument Convert(
        OcrResult result,
        double scale,
        int originalWidth,
        int originalHeight)
    {
        var lines = new List<OcrLine>(result.Lines.Count);

        foreach (var line in result.Lines)
        {
            var words = new List<WordBox>(line.Words.Count);

            foreach (var word in line.Words)
            {
                var rect = word.BoundingRect;

                words.Add(new WordBox(
                    word.Text,
                    rect.X * scale,
                    rect.Y * scale,
                    rect.Width * scale,
                    rect.Height * scale));
            }

            if (words.Count > 0)
            {
                lines.Add(new OcrLine(words));
            }
        }

        return new OcrDocument(lines, originalWidth, originalHeight);
    }
}

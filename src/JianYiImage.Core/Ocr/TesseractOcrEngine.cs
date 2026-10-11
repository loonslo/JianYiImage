using Tesseract;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace JianYiImage.Core.Ocr;

/// <summary>独立的离线英文/代码模式；不猜测替换另一引擎的字母、数字或缺失字符。</summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    public const string LanguageTag = "en-x-precise";
    private readonly string _dataPath;

    private TesseractOcrEngine(string dataPath) => _dataPath = dataPath;

    public string Name => "Tesseract（英文／代码精细识别）";
    public OcrLanguageInfo Language => new(LanguageTag, "英文／代码（精细识别）");
    public int MaxImageDimension => 10000;

    public static TesseractOcrEngine? TryCreate(string? dataPath = null)
    {
        dataPath ??= Path.Combine(AppContext.BaseDirectory, "tessdata");
        return File.Exists(Path.Combine(dataPath, "eng.traineddata")) ? new(dataPath) : null;
    }

    public async Task<OcrDocument> RecognizeAsync(SoftwareBitmap bitmap, double scaleToOriginal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.PixelWidth > MaxImageDimension || bitmap.PixelHeight > MaxImageDimension)
            throw new ArgumentException("位图超过识别尺寸上限。", nameof(bitmap));
        cancellationToken.ThrowIfCancellationRequested();

        // 编码到内存而非临时文件，图像与 OCR 内容均不会离开本机。
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
        stream.Seek(0);
        using var readStream = stream.AsStreamForRead();
        using var memory = new MemoryStream();
        await readStream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = memory.ToArray();
        var width = (int)Math.Round(bitmap.PixelWidth * scaleToOriginal);
        var height = (int)Math.Round(bitmap.PixelHeight * scaleToOriginal);
        return await Task.Run(() => Recognize(bytes, scaleToOriginal, width, height, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private OcrDocument Recognize(byte[] bytes, double scaleToOriginal, int width, int height, CancellationToken cancellationToken)
    {
        // 每次请求独占原生引擎，避免切图取消后并发请求共用非线程安全的句柄。
        using var engine = new TesseractEngine(_dataPath, "eng", EngineMode.LstmOnly);
        using var source = Pix.LoadFromMemory(bytes);
        using var gray = source.ConvertTo8(1);
        // 原生自适应阈值处理深色面板时会把背景当文字；统一为亮底暗字再识别。
        using var normalized = HasDarkBackground(gray) ? gray.Invert() : gray.Clone();
        // 小字放大；限制额外像素与最长边，避免大照片导致内存突增。
        var zoom = (long)source.Width * source.Height <= 4_000_000 && Math.Max(source.Width, source.Height) <= 2500 ? 2 : 1;
        using var pixels = normalized.Scale(zoom, zoom);
        using var page = engine.Process(pixels, PageSegMode.SparseText);
        cancellationToken.ThrowIfCancellationRequested();
        using var iterator = page.GetIterator();
        var lines = new List<OcrLine>();
        var words = new List<WordBox>();
        var scale = scaleToOriginal / zoom;
        iterator.Begin();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (iterator.IsAtBeginningOf(PageIteratorLevel.TextLine) && words.Count > 0)
            {
                lines.Add(new OcrLine(words));
                words = [];
            }
            var text = iterator.GetText(PageIteratorLevel.Word);
            if (!string.IsNullOrEmpty(text) && iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var rect))
            {
                words.Add(new WordBox(text, rect.X1 * scale, rect.Y1 * scale, rect.Width * scale, rect.Height * scale,
                    SpaceBefore: words.Count > 0));
            }
        } while (iterator.Next(PageIteratorLevel.Word));
        if (words.Count > 0) lines.Add(new OcrLine(words));
        return new OcrDocument(lines, width, height);
    }

    private static unsafe bool HasDarkBackground(Pix gray)
    {
        var data = gray.GetData();
        var pointer = (uint*)data.Data;
        long total = 0;
        var count = 0;
        // 均匀采样至多约 4096 个像素，不再创建整幅图的托管副本。
        for (var y = 0; y < gray.Height; y += Math.Max(1, (gray.Height + 63) / 64))
        for (var x = 0; x < gray.Width; x += Math.Max(1, (gray.Width + 63) / 64))
        {
            total += PixData.GetDataByte(pointer + y * data.WordsPerLine, x);
            count++;
        }
        return total < 128L * count;
    }
}

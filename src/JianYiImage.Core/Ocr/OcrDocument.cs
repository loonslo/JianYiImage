namespace JianYiImage.Core.Ocr;

/// <summary>
/// OCR 识别出的一行。坐标位于原图像素坐标系。
/// </summary>
public sealed class OcrLine
{
    public OcrLine(IReadOnlyList<WordBox> words)
    {
        if (words.Count == 0)
        {
            throw new ArgumentException("一行至少要有一个词。", nameof(words));
        }

        // 行内按左边界排序——OCR 返回的顺序通常已经正确，但不保证。
        var orderedWords = words.OrderBy(w => w.Left).ToArray();
        Words = orderedWords;

        Left = Words.Min(w => w.Left);
        Right = Words.Max(w => w.Right);
        Top = Words.Min(w => w.Top);
        Bottom = Words.Max(w => w.Bottom);

        // 在文档形成时统一纠错，面板与任意部分的划选复制使用同一份词文本。
        OcrUrlCorrection.Apply(orderedWords, Height);
        JoinPrevious = OcrStructuredText.Apply(orderedWords, Height);
    }

    public IReadOnlyList<WordBox> Words { get; }

    internal IReadOnlyList<bool> JoinPrevious { get; }

    public double Left { get; }

    public double Top { get; }

    public double Right { get; }

    public double Bottom { get; }

    public double Width => Right - Left;

    /// <summary>
    /// 行的包围盒高度。用作该行的参考字号——
    /// 单个词的高度不可靠（实测连字符被误识别成「一」时高度只有 1px）。
    /// </summary>
    public double Height => Bottom - Top;

    public double CenterY => (Top + Bottom) / 2;
}

/// <summary>
/// 一张图的完整 OCR 结果。
/// </summary>
/// <param name="Lines">识别出的行，按阅读顺序（先上后下、同高时先左后右）排列。</param>
/// <param name="ImageWidth">识别时所用图像的宽度（原图像素）。</param>
/// <param name="ImageHeight">识别时所用图像的高度（原图像素）。</param>
public sealed record OcrDocument(
    IReadOnlyList<OcrLine> Lines,
    int ImageWidth,
    int ImageHeight)
{
    public static readonly OcrDocument Empty = new(Array.Empty<OcrLine>(), 0, 0);

    /// <summary>是否没识别出任何文字。</summary>
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>识别出的词总数。</summary>
    public int WordCount => Lines.Sum(l => l.Words.Count);
}

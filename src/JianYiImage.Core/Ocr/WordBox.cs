namespace JianYiImage.Core.Ocr;

/// <summary>
/// OCR 识别出的一个词，坐标位于 <b>原图像素坐标系</b>。
/// 这是 Core 层的中立表示——刻意不引用 WinRT 的 OcrWord，
/// 让 <see cref="TextSelectionModel"/> 保持纯算法、可单元测试。
/// </summary>
/// <param name="Text">词的文本内容。</param>
/// <param name="Left">左边界（原图像素）。</param>
/// <param name="Top">上边界（原图像素）。</param>
/// <param name="Width">宽度（原图像素）。</param>
/// <param name="Height">高度（原图像素）。</param>
/// <param name="SpaceBefore">引擎明确返回的词间空格；null 时按字框间距推断。</param>
public readonly record struct WordBox(string Text, double Left, double Top, double Width, double Height, bool? SpaceBefore = null)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double CenterX => Left + Width / 2;

    public double CenterY => Top + Height / 2;

    /// <summary>词是否包含 CJK 字符——决定拼接文本时词间是否补空格。</summary>
    public bool IsCjk => ContainsCjk(Text);

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    internal static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            // CJK 统一汉字、扩展 A、兼容汉字、假名、CJK 标点、全角形式
            if ((ch >= '一' && ch <= '鿿') ||
                (ch >= '㐀' && ch <= '䶿') ||
                (ch >= '豈' && ch <= '﫿') ||
                (ch >= '぀' && ch <= 'ヿ') ||
                (ch >= '　' && ch <= '〿') ||
                (ch >= '＀' && ch <= '￯'))
            {
                return true;
            }
        }

        return false;
    }
}

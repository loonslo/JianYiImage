using System.Globalization;

namespace JianYiImage.Core.Text;

public static class TextTruncation
{
    /// <summary>限制 UTF-16 长度，同时保留完整代理对、组合音标与 ZWJ emoji。</summary>
    public static string AtTextElementBoundary(string text, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
        if (text.Length <= maxLength)
        {
            return text;
        }

        var length = 0;
        while (length < maxLength)
        {
            var next = StringInfo.GetNextTextElementLength(text.AsSpan(length));
            if (length + next > maxLength)
            {
                break;
            }
            length += next;
        }
        return text[..length];
    }
}

namespace JianYiImage.Core.Ocr;

/// <summary>
/// 修正 Windows OCR 把网址开头的 ht 合并识别成 @ 的已知错误。
/// 只改词文本，保留词数与坐标，供面板、划选和缓存共同使用。
/// </summary>
internal static class OcrUrlCorrection
{
    /// <param name="words">由 OcrLine 拥有、已按横坐标排序的词数组。</param>
    internal static void Apply(WordBox[] words, double lineHeight)
    {
        for (var start = 0; start < words.Length; start++)
        {
            if (!words[start].Text.StartsWith('@') &&
                !words[start].Text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 紧贴前一个网址/用户名片段时，@ 可能属于路径、查询参数或邮箱。
            if (start > 0 && !TextSelectionModel.NeedsSpace(words[start - 1], words[start], lineHeight) &&
                words[start - 1].Text.Length > 0 && IsUrlCharacter(words[start - 1].Text[^1]))
            {
                continue;
            }

            // 看完整个协议和主机首字符；OCR 可以把它们拆成多个相邻词。
            var candidate = string.Empty;
            for (var i = start; i < words.Length && candidate.Length < 9; i++)
            {
                if (i > start && words[i].Left - words[i - 1].Right >= Math.Max(lineHeight, 1) * 0.5)
                {
                    break;
                }

                var take = Math.Min(words[i].Text.Length, 9 - candidate.Length);
                candidate += words[i].Text[..take];
            }

            var normalized = candidate.Replace('：', ':').Replace('／', '/');
            var misread = normalized.StartsWith("@tps://", StringComparison.OrdinalIgnoreCase);
            var prefixLength = misread ? 7 : normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? 8 :
                normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 7 : 0;
            if (prefixLength == 0 || normalized.Length <= prefixLength ||
                !(char.IsAsciiLetterOrDigit(normalized[prefixLength]) || normalized[prefixLength] == '['))
            {
                continue;
            }

            var remaining = prefixLength;
            for (var i = start; remaining > 0; i++)
            {
                var text = words[i].Text;
                var take = Math.Min(text.Length, remaining);
                var prefix = text[..take].Replace('：', ':').Replace('／', '/');
                if (misread)
                {
                    prefix = prefix.ToLowerInvariant();
                    if (i == start)
                    {
                        prefix = "ht" + prefix[1..];
                    }
                }

                words[i] = words[i] with { Text = prefix + text[take..] };
                remaining -= take;
            }
        }
    }

    private static bool IsUrlCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || ".-_~:/?#[]@!$&'*+,;=%".Contains(value);
}

using System.Text.RegularExpressions;

namespace JianYiImage.Core.Ocr;

/// <summary>以明确的协议和电话号码结构决定拼接；不猜测域名、数字或缺失字符。</summary>
internal static partial class OcrStructuredText
{
    [GeneratedRegex(@"(?<![A-Za-z0-9])1[3-9][0-9][一－-][0-9]{4}[一－-][0-9]{4}(?![A-Za-z0-9])")]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9])https?://[A-Za-z0-9\[]", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPrefixPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])[A-Za-z0-9][A-Za-z0-9_-]*_[A-Za-z0-9_-]+\.[A-Za-z0-9]{1,10}(?![A-Za-z0-9_])")]
    private static partial Regex FileNamePattern();

    internal static bool[] Apply(WordBox[] words, double lineHeight)
    {
        var joins = new bool[words.Length];
        var offsets = new int[words.Length + 1];
        for (var i = 0; i < words.Length; i++)
        {
            offsets[i + 1] = offsets[i] + words[i].Text.Length;
        }
        var text = string.Concat(words.Select(w => w.Text));
        var maxGap = Math.Max(lineHeight, 1) * 0.5;
        var urlStarts = new bool[words.Length];
        // 只拼接仍含下划线与扩展名的明确文件名，不补造 OCR 已遗漏的下划线。
        foreach (Match match in FileNamePattern().Matches(text))
        {
            var indices = Enumerable.Range(0, words.Length)
                .Where(i => offsets[i] < match.Index + match.Length && offsets[i + 1] > match.Index).ToArray();
            if (indices.Skip(1).Any(i => words[i].Left - words[i - 1].Right >= maxGap)) continue;
            foreach (var i in indices.Skip(1)) joins[i] = true;
        }
        foreach (Match match in UrlPrefixPattern().Matches(text))
        {
            var indices = Enumerable.Range(0, words.Length)
                .Where(i => offsets[i] < match.Index + match.Length && offsets[i + 1] > match.Index).ToArray();
            if (offsets[indices[0]] != match.Index ||
                indices.Skip(1).Any(i => words[i].Left - words[i - 1].Right >= maxGap)) continue;
            foreach (var i in indices.Skip(1)) joins[i] = true;
            urlStarts[indices[^1]] = true;
        }
        foreach (Match match in PhonePattern().Matches(text))
        {
            var indices = Enumerable.Range(0, words.Length)
                .Where(i => offsets[i] < match.Index + match.Length && offsets[i + 1] > match.Index).ToArray();
            if (indices.Skip(1).Any(i => words[i].Left - words[i - 1].Right >= maxGap))
            {
                continue;
            }
            foreach (var i in indices)
            {
                var chars = words[i].Text.ToCharArray();
                for (var j = 0; j < chars.Length; j++)
                {
                    if (offsets[i] + j >= match.Index && offsets[i] + j < match.Index + match.Length &&
                        chars[j] is '一' or '－')
                    {
                        chars[j] = '-';
                    }
                }
                words[i] = words[i] with { Text = new string(chars) };
                if (i > indices[0]) joins[i] = true;
            }
        }

        var inUrl = false;
        for (var i = 0; i < words.Length; i++)
        {
            var current = words[i].Text;
            if (urlStarts[i] || current.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                current.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                inUrl = true;
                continue;
            }
            if (!inUrl || i == 0) continue;
            var previous = words[i - 1].Text;
            if (current.Length == 0 || previous.Length == 0 || words[i].Left - words[i - 1].Right >= maxGap ||
                !current.All(IsUrlCharacter) ||
                !(IsConnector(previous[^1]) || IsConnector(current[0]) ||
                  (char.IsAsciiDigit(previous[^1]) && char.IsAsciiDigit(current[0]))))
            {
                inUrl = false;
                continue;
            }
            joins[i] = true;
        }
        return joins;
    }

    private static bool IsConnector(char value) => "/?#&=.:_%+-~".Contains(value);
    private static bool IsUrlCharacter(char value) => char.IsAsciiLetterOrDigit(value) || ".-_~:/?#[]@!$&'*+,;=%".Contains(value);
}

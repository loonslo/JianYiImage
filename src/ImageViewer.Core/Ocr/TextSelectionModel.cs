using System.Text;

namespace ImageViewer.Core.Ocr;

/// <summary>
/// 选中范围，半开区间 [Start, End)，单位是「caret 位置」。
/// caret i 表示第 i 个词之前的位置，取值 0..WordCount，
/// 因此区间内包含的词是 Start..End-1。这套模型跟文本编辑器一致，
/// 跨行连选天然成立——选中的是词序列上的一段，不是一个矩形。
/// </summary>
public readonly record struct SelectionRange(int Start, int End)
{
    public static readonly SelectionRange Empty = new(0, 0);

    public bool IsEmpty => End <= Start;

    /// <summary>区间内的词数。</summary>
    public int Count => Math.Max(0, End - Start);
}

/// <summary>一块高亮矩形，坐标位于原图像素坐标系。</summary>
public readonly record struct HighlightRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

/// <summary>
/// 实况文本选择的核心算法：命中测试、区间选择、高亮矩形、文本组装。
/// 纯算法、无 UI 依赖，全部行为可单元测试。
/// </summary>
public sealed class TextSelectionModel
{
    /// <summary>
    /// 行内词距小于「行高 × 该比例」时不补空格。
    /// 实测 zh-Hans 引擎会把一个英文单词拆断（over → "ove" + "r"，间距 3px，行高 29px），
    /// 靠这个阈值把它们重新粘回去。
    /// </summary>
    private const double SpaceGapRatio = 0.25;

    /// <summary>
    /// 窄词的最小命中宽度（行高的倍数）。
    /// 实测全角冒号「：」的 BoundingRect 只有 2px 宽，不补偿根本点不中。
    /// </summary>
    private const double MinHitWidthRatio = 0.2;

    private readonly WordBox[] _words;
    private readonly int[] _wordLineIndex;
    private readonly OcrLine[] _lines;
    private readonly int[] _lineFirstWord;

    public TextSelectionModel(OcrDocument document)
    {
        Document = document;

        // 行按阅读顺序重排：先上后下，同高时先左后右。
        // OCR 给出的行顺序在多栏排版下未必正确，这一步决定复制出来的文字顺不顺。
        _lines = document.Lines
            .OrderBy(l => l.Top)
            .ThenBy(l => l.Left)
            .ToArray();

        var words = new List<WordBox>();
        var lineIndex = new List<int>();
        _lineFirstWord = new int[_lines.Length];

        for (var i = 0; i < _lines.Length; i++)
        {
            _lineFirstWord[i] = words.Count;
            foreach (var word in _lines[i].Words)
            {
                words.Add(word);
                lineIndex.Add(i);
            }
        }

        _words = words.ToArray();
        _wordLineIndex = lineIndex.ToArray();
    }

    public OcrDocument Document { get; }

    public int WordCount => _words.Length;

    public int LineCount => _lines.Length;

    public bool IsEmpty => _words.Length == 0;

    public IReadOnlyList<WordBox> Words => _words;

    /// <summary>取某个词所属的行号。</summary>
    public int LineIndexOfWord(int wordIndex) => _wordLineIndex[wordIndex];

    // ---------- 命中测试 ----------

    /// <summary>
    /// 严格命中：点是否落在某个词上。返回 null 表示没落在文字上。
    /// 用于决定鼠标光标形状（在词上显示 I-beam，否则显示抓手可拖动图片）——
    /// 这是左键「选字」与「平移」无需模式切换即可共存的依据。
    /// </summary>
    public int? WordIndexFromPoint(double x, double y)
    {
        var lineIdx = FindLineIndex(y, requireInside: true);
        if (lineIdx < 0)
        {
            return null;
        }

        var line = _lines[lineIdx];
        var minHitWidth = Math.Max(line.Height, 1) * MinHitWidthRatio;
        var first = _lineFirstWord[lineIdx];

        for (var i = first; i < first + line.Words.Count; i++)
        {
            var word = _words[i];

            // 水平方向对窄词补偿到最小命中宽度；
            // 垂直方向直接用行的范围，避免扁词（实测「一」只有 1px 高）无法命中。
            var halfWidth = Math.Max(word.Width, minHitWidth) / 2;
            var center = word.CenterX;

            if (x >= center - halfWidth && x <= center + halfWidth)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// 宽松命中：把任意一点吸附到最近的 caret 位置。
    /// 点在行间空隙或行尾空白处也能得到合理结果，跟拖选网页文字的手感一致。
    /// </summary>
    public int CaretFromPoint(double x, double y)
    {
        if (IsEmpty)
        {
            return 0;
        }

        var lineIdx = FindLineIndex(y, requireInside: false);
        var line = _lines[lineIdx];
        var first = _lineFirstWord[lineIdx];
        var last = first + line.Words.Count - 1;

        // 行首之前 / 行尾之后
        if (x <= _words[first].Left)
        {
            return first;
        }

        if (x >= _words[last].Right)
        {
            return last + 1;
        }

        for (var i = first; i <= last; i++)
        {
            var word = _words[i];

            // 落在词内：以词中心为界，决定 caret 在词前还是词后
            if (x >= word.Left && x <= word.Right)
            {
                return x < word.CenterX ? i : i + 1;
            }

            // 落在本词与下一词的空隙里：caret 位置唯一，就是「第 i 个词之后」
            if (i < last && x > word.Right && x < _words[i + 1].Left)
            {
                return i + 1;
            }
        }

        return last + 1;
    }

    /// <summary>
    /// 按 y 找行。<paramref name="requireInside"/> 为 true 时要求 y 严格落在某行的垂直范围内，
    /// 否则允许吸附到最近的行。
    /// </summary>
    private int FindLineIndex(double y, bool requireInside)
    {
        if (_lines.Length == 0)
        {
            return requireInside ? -1 : 0;
        }

        for (var i = 0; i < _lines.Length; i++)
        {
            if (y >= _lines[i].Top && y <= _lines[i].Bottom)
            {
                return i;
            }
        }

        if (requireInside)
        {
            return -1;
        }

        // 吸附到垂直距离最近的行
        var bestIndex = 0;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < _lines.Length; i++)
        {
            var line = _lines[i];
            var distance = y < line.Top ? line.Top - y : y - line.Bottom;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    // ---------- 区间构造 ----------

    /// <summary>由拖拽的起点与当前点构造选区，自动规范化方向（支持反向拖选）。</summary>
    public SelectionRange CreateRange(int anchorCaret, int focusCaret)
    {
        var start = Math.Clamp(Math.Min(anchorCaret, focusCaret), 0, WordCount);
        var end = Math.Clamp(Math.Max(anchorCaret, focusCaret), 0, WordCount);
        return new SelectionRange(start, end);
    }

    /// <summary>双击选中单个词。</summary>
    public SelectionRange SelectWordAt(int wordIndex)
    {
        if (wordIndex < 0 || wordIndex >= WordCount)
        {
            return SelectionRange.Empty;
        }

        return new SelectionRange(wordIndex, wordIndex + 1);
    }

    /// <summary>三击选中整行。</summary>
    public SelectionRange SelectLineAt(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= _lines.Length)
        {
            return SelectionRange.Empty;
        }

        var first = _lineFirstWord[lineIndex];
        return new SelectionRange(first, first + _lines[lineIndex].Words.Count);
    }

    /// <summary>Ctrl+A 全选。</summary>
    public SelectionRange SelectAll() => new(0, WordCount);

    // ---------- 输出 ----------

    /// <summary>
    /// 选区的高亮矩形，按行拆分、行内连续词合并成一整条。
    /// 垂直方向统一用行的范围，高亮条高度才不会随扁词忽高忽低。
    /// </summary>
    public IReadOnlyList<HighlightRect> GetHighlightRects(SelectionRange range)
    {
        var rects = new List<HighlightRect>();
        if (range.IsEmpty || IsEmpty)
        {
            return rects;
        }

        var start = Math.Clamp(range.Start, 0, WordCount);
        var end = Math.Clamp(range.End, 0, WordCount);

        var i = start;
        while (i < end)
        {
            var lineIdx = _wordLineIndex[i];
            var line = _lines[lineIdx];

            var left = _words[i].Left;
            var right = _words[i].Right;

            // 吃掉本行内所有连续被选中的词
            while (i < end && _wordLineIndex[i] == lineIdx)
            {
                right = Math.Max(right, _words[i].Right);
                i++;
            }

            rects.Add(new HighlightRect(left, line.Top, right - left, line.Height));
        }

        return rects;
    }

    /// <summary>
    /// 把选区组装成可粘贴的文本。
    /// 系统自带的 OcrResult.Text 不能直接用——它把所有词用空格连接，
    /// 中文会变成「这 是 一 段」（实测确认），所以必须自己按规则拼。
    /// </summary>
    public string GetText(SelectionRange range)
    {
        if (range.IsEmpty || IsEmpty)
        {
            return string.Empty;
        }

        var start = Math.Clamp(range.Start, 0, WordCount);
        var end = Math.Clamp(range.End, 0, WordCount);

        var sb = new StringBuilder();
        var currentLine = -1;

        for (var i = start; i < end; i++)
        {
            var lineIdx = _wordLineIndex[i];

            if (lineIdx != currentLine)
            {
                if (currentLine >= 0)
                {
                    sb.Append("\r\n");
                }

                currentLine = lineIdx;
            }
            else if (NeedsSpace(_words[i - 1], _words[i], _lines[lineIdx].Height))
            {
                sb.Append(' ');
            }

            sb.Append(_words[i].Text);
        }

        return sb.ToString();
    }

    /// <summary>整篇文字，用于「复制全部识别文字」。</summary>
    public string GetAllText() => GetText(SelectAll());

    /// <summary>
    /// 判断行内相邻两词之间是否需要补空格。
    /// 两条规则：CJK 之间从不补；其余按词距相对行高的比例判断。
    /// </summary>
    private static bool NeedsSpace(WordBox left, WordBox right, double lineHeight)
    {
        // 中文逐字成词（实测），字与字、字与中文标点之间都不能有空格
        if (left.IsCjk && right.IsCjk)
        {
            return false;
        }

        var gap = right.Left - left.Right;
        var threshold = Math.Max(lineHeight, 1) * SpaceGapRatio;
        return gap >= threshold;
    }
}

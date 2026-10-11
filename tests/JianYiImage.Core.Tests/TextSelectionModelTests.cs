using JianYiImage.Core.Ocr;

namespace JianYiImage.Core.Tests;

/// <summary>
/// 实况文本选择的核心算法测试。用例全部基于真实 OCR 输出（见 <see cref="OcrProbeData"/>）。
/// </summary>
public class TextSelectionModelTests
{
    // ---------- 文本组装：CJK 规则 ----------

    [Fact]
    public void 纯中文行拼接时字与字之间不补空格()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.ChineseLine());

        // 系统自带的 OcrResult.Text 在这里会输出「这 是 一 段 ...」，不可直接使用
        Assert.Equal("这是一段纯中文的测试文字内容", model.GetAllText());
    }

    [Fact]
    public void 中文标点前后同样不补空格()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.SmallFontLine());

        // 「试」「：」「验」三者相邻，全角冒号两侧都不能出现空格
        Assert.Contains("测试：验证", model.GetAllText());
    }

    // ---------- 文本组装：词距规则 ----------

    [Fact]
    public void 被拆断的英文单词按词距重新粘合()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.EnglishLine());
        var text = model.GetAllText();

        // OCR 实际输出的是 "ove" + "r"（间距 3px）和 "1" + "3"（间距 6px），
        // 行高 29px，阈值 7.25px，因此两者都应被粘回去
        Assert.Contains("over", text);
        Assert.Contains("13", text);
        Assert.DoesNotContain("ove r", text);
        Assert.DoesNotContain("1 3", text);
    }

    [Fact]
    public void 正常词距的英文单词之间保留空格()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.EnglishLine());

        // 注意 "foxjumps" 是 OCR 自己丢了空格，属引擎误差，不是拼接逻辑的问题
        Assert.Equal("The quick brown foxjumps over 13 lazy dogs.", model.GetAllText());
    }

    [Fact]
    public void 中英混排时中文与英文之间保留空格()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.MixedLine());

        // 「排」被引擎误认成「扌」+「非」，这是识别误差；此处只验证空格位置正确
        Assert.Equal("中英混扌非 mixed content 测试 OCR engine 效果", model.GetAllText());
    }

    [Fact]
    public void 中文与数字之间按词距判断空格()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.SmallFontLine());

        Assert.Equal("小字号测试：验证 12px 左右的文字能否被正确识别", model.GetAllText());
    }

    [Fact]
    public void 紧邻的非中文片段不被空格切开()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.ContactLine());

        // 明确的手机号码结构把误识别的「一」纠正为横杠，词框仍按原坐标使用。
        Assert.Equal("EmaiI: test@example.com Tel: 138-0000-1234", model.GetAllText());
    }

    // ---------- 命中测试：实测暴露的两个硬骨头 ----------

    [Fact]
    public void 只有两像素宽的全角冒号也能被点中()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.SmallFontLine());

        // 「：」的 BoundingRect 是 (132, 269, 2, 9)，宽度只有 2px。
        // 没有最小命中宽度补偿的话，鼠标几乎不可能点中它。
        var hit = model.WordIndexFromPoint(133, 272);

        Assert.NotNull(hit);
        Assert.Equal("：", model.Words[hit!.Value].Text);
    }

    [Fact]
    public void 只有一像素高的扁词也能被点中()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.ContactLine());

        // 「一」的 BoundingRect 是 (504, 343, 8, 1)，高度只有 1px。
        // 垂直方向必须用整行的范围来判定，否则 y=350 就落空了。
        var hit = model.WordIndexFromPoint(508, 350);

        Assert.NotNull(hit);
        Assert.Equal("-", model.Words[hit!.Value].Text);
    }

    [Fact]
    public void 点在文字之外返回空表示此处应当平移图片()
    {
        var model = OcrProbeData.FullModel();

        // 行与行之间的空白区域——左键在这里应该拖动图片而不是选字
        Assert.Null(model.WordIndexFromPoint(450, 90));

        // 整行右侧的空白区域
        Assert.Null(model.WordIndexFromPoint(880, 60));
    }

    [Fact]
    public void 点在行尾空白处光标吸附到该行末尾()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.ChineseLine());

        var caret = model.CaretFromPoint(880, 60);

        Assert.Equal(model.WordCount, caret);
    }

    [Fact]
    public void 点在行首之前光标吸附到该行开头()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.ChineseLine());

        var caret = model.CaretFromPoint(0, 60);

        Assert.Equal(0, caret);
    }

    [Fact]
    public void 点在词的左半与右半分别落在词前与词后()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.ChineseLine());

        // 「这」占 (45,46,28,28)，中心 x = 59
        Assert.Equal(0, model.CaretFromPoint(48, 60));
        Assert.Equal(1, model.CaretFromPoint(70, 60));
    }

    // ---------- 区间选择 ----------

    [Fact]
    public void 跨行拖选得到的是词序列区间而不是矩形()
    {
        var model = OcrProbeData.FullModel();

        // 从第 0 行的「文」（倒数第 3 个词）拖到第 1 行的 "quick"
        var anchor = model.CaretFromPoint(340, 60);
        var focus = model.CaretFromPoint(160, 130);
        var range = model.CreateRange(anchor, focus);
        var text = model.GetText(range);

        Assert.Contains("\r\n", text);
        Assert.StartsWith("文", text);
        Assert.EndsWith("quick", text);
    }

    [Fact]
    public void 反向拖选与正向拖选结果一致()
    {
        var model = OcrProbeData.FullModel();

        var forward = model.CreateRange(3, 9);
        var backward = model.CreateRange(9, 3);

        Assert.Equal(forward, backward);
        Assert.Equal(model.GetText(forward), model.GetText(backward));
    }

    [Fact]
    public void 跨行选择会把中间整行都包含进来()
    {
        var model = OcrProbeData.FullModel();

        var range = model.CreateRange(0, model.WordCount);
        var lines = model.GetText(range).Split("\r\n");

        Assert.Equal(5, lines.Length);
        Assert.Equal("这是一段纯中文的测试文字内容", lines[0]);
        Assert.Equal("The quick brown foxjumps over 13 lazy dogs.", lines[1]);
    }

    [Fact]
    public void 双击选中单个词()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.EnglishLine());

        var range = model.SelectWordAt(1);

        Assert.Equal(1, range.Count);
        Assert.Equal("quick", model.GetText(range));
    }

    [Fact]
    public void 三击选中整行()
    {
        var model = OcrProbeData.FullModel();

        var range = model.SelectLineAt(1);

        Assert.Equal("The quick brown foxjumps over 13 lazy dogs.", model.GetText(range));
    }

    [Fact]
    public void 全选覆盖所有词()
    {
        var model = OcrProbeData.FullModel();

        var range = model.SelectAll();

        Assert.Equal(model.WordCount, range.Count);
        Assert.Equal(model.GetAllText(), model.GetText(range));
    }

    [Fact]
    public void 空选区返回空文本()
    {
        var model = OcrProbeData.FullModel();

        Assert.Equal(string.Empty, model.GetText(SelectionRange.Empty));
        Assert.Empty(model.GetHighlightRects(SelectionRange.Empty));
    }

    // ---------- 高亮矩形 ----------

    [Fact]
    public void 同一行内的连续词合并成一条高亮()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.EnglishLine());

        var rects = model.GetHighlightRects(model.CreateRange(0, 3));

        Assert.Single(rects);
        Assert.Equal(45, rects[0].Left);
        Assert.Equal(254, rects[0].Right); // "brown" 的右边界
    }

    [Fact]
    public void 跨行选择每行各得一条高亮()
    {
        var model = OcrProbeData.FullModel();
        var chineseCount = OcrProbeData.ChineseLine().Words.Count;

        // 第 0 行最后两个词 + 第 1 行前两个词
        var rects = model.GetHighlightRects(
            model.CreateRange(chineseCount - 2, chineseCount + 2));

        Assert.Equal(2, rects.Count);
    }

    [Fact]
    public void 高亮条的高度取整行高度而不是单词高度()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.ContactLine());

        // 选中 1px 高的「一」，高亮条不能也只有 1px，否则视觉上会断裂
        var rects = model.GetHighlightRects(model.SelectWordAt(4));

        Assert.Single(rects);
        Assert.Equal(29, rects[0].Height); // 整行高度 330..359
    }

    // ---------- 行排序 ----------

    [Fact]
    public void 行按从上到下重新排序()
    {
        // 故意用错乱的顺序传入
        var document = new OcrDocument(
            new[] { OcrProbeData.ContactLine(), OcrProbeData.ChineseLine(), OcrProbeData.EnglishLine() },
            900,
            420);
        var model = new TextSelectionModel(document);

        var lines = model.GetAllText().Split("\r\n");

        Assert.Equal("这是一段纯中文的测试文字内容", lines[0]);
        Assert.Equal("The quick brown foxjumps over 13 lazy dogs.", lines[1]);
        Assert.StartsWith("EmaiI:", lines[2]);
    }

    [Fact]
    public void 同高度的多栏按从左到右排序()
    {
        var left = new OcrLine(new[] { new WordBox("左栏", 10, 100, 60, 20) });
        var right = new OcrLine(new[] { new WordBox("右栏", 500, 100, 60, 20) });

        // 右栏先传入，排序后应仍是左栏在前
        var model = new TextSelectionModel(new OcrDocument(new[] { right, left }, 900, 420));

        Assert.Equal("左栏\r\n右栏", model.GetAllText());
    }

    [Fact]
    public void 行内的词按从左到右排序()
    {
        // 乱序传入同一行的词
        var line = new OcrLine(new[]
        {
            new WordBox("丙", 200, 100, 30, 30),
            new WordBox("甲", 100, 100, 30, 30),
            new WordBox("乙", 150, 100, 30, 30),
        });

        var model = OcrProbeData.ModelOf(line);

        Assert.Equal("甲乙丙", model.GetAllText());
    }

    // ---------- 多栏：同一水平带里有好几条行 ----------

    /// <summary>
    /// 左右两栏同高：OCR 把它们各识别成独立的一行（菜单的名称与价格、表格的各列都是这样）。
    /// 阅读顺序下词序是 甲0 乙1 丙2 丁3。
    /// </summary>
    private static TextSelectionModel TwoColumns() => new(new OcrDocument(
        new[]
        {
            new OcrLine(new[] { new WordBox("甲", 10, 100, 30, 20), new WordBox("乙", 50, 100, 30, 20) }),
            new OcrLine(new[] { new WordBox("丙", 500, 100, 30, 20), new WordBox("丁", 540, 100, 30, 20) }),
        },
        900,
        420));

    [Fact]
    public void 多栏时每一栏的文字都能被点中()
    {
        var model = TwoColumns();

        Assert.Equal(0, model.WordIndexFromPoint(25, 110));
        Assert.Equal(1, model.WordIndexFromPoint(65, 110));

        // 以前只检查"纵向范围包含该点的第一条行"，右栏的字全部点不中，没法从右栏开始拖选
        Assert.Equal(2, model.WordIndexFromPoint(515, 110));
        Assert.Equal(3, model.WordIndexFromPoint(555, 110));
    }

    [Fact]
    public void 两栏之间的空白处不算点中文字()
    {
        Assert.Null(TwoColumns().WordIndexFromPoint(250, 110));
    }

    [Fact]
    public void 多栏时光标吸附到所在的栏()
    {
        var model = TwoColumns();

        Assert.Equal(2, model.CaretFromPoint(505, 110));  // 丙的左半：在丙之前
        Assert.Equal(3, model.CaretFromPoint(525, 110));  // 丙的右半：在丙之后
        Assert.Equal(3, model.CaretFromPoint(545, 110));  // 丁的左半
        Assert.Equal(4, model.CaretFromPoint(565, 110));  // 丁的右半：全部词之后
        Assert.Equal(0, model.CaretFromPoint(15, 110));   // 左栏仍然正常
    }

    [Fact]
    public void 多栏时点在栏外空白处吸附到水平方向最近的栏()
    {
        var model = TwoColumns();

        Assert.Equal(2, model.CaretFromPoint(300, 110));  // 离右栏行首更近
        Assert.Equal(2, model.CaretFromPoint(100, 110));  // 离左栏行尾更近：行尾之后恰好也是 caret 2
        Assert.Equal(4, model.CaretFromPoint(800, 110));  // 右栏行尾之后
    }

    [Fact]
    public void 从右栏开始拖选只选中右栏的词()
    {
        var model = TwoColumns();
        var anchor = model.CaretFromPoint(505, 110);
        var focus = model.CaretFromPoint(565, 110);

        var range = model.CreateRange(anchor, focus);

        Assert.Equal("丙丁", model.GetText(range));
    }

    // ---------- 边界情况 ----------

    [Fact]
    public void 空文档不会抛异常()
    {
        var model = new TextSelectionModel(OcrDocument.Empty);

        Assert.True(model.IsEmpty);
        Assert.Equal(0, model.WordCount);
        Assert.Equal(string.Empty, model.GetAllText());
        Assert.Equal(0, model.CaretFromPoint(100, 100));
        Assert.Null(model.WordIndexFromPoint(100, 100));
    }

    [Fact]
    public void 越界的选区被裁剪到有效范围()
    {
        var model = OcrProbeData.ModelOf(OcrProbeData.ChineseLine());

        var range = model.CreateRange(-50, 9999);

        Assert.Equal(0, range.Start);
        Assert.Equal(model.WordCount, range.End);
    }

    [Fact]
    public void 空行的构造被拒绝()
    {
        Assert.Throws<ArgumentException>(() => new OcrLine(Array.Empty<WordBox>()));
    }
}

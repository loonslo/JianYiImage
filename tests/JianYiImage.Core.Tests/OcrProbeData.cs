using JianYiImage.Core.Ocr;

namespace JianYiImage.Core.Tests;

/// <summary>
/// 测试夹具：<b>真实</b>的 OCR 输出，不是人造数据。
///
/// 来源：本机 Windows OCR（zh-Hans-CN 引擎，系统唯一装有的语言包）
/// 识别 tools/ocr-probe.ps1 生成的 900x420 测试图，逐词坐标照抄自探针输出。
///
/// 用真实数据的原因：这张图暴露了三个人造数据想不到的坑——
/// 英文单词被拆断（over → "ove" + "r"）、全角冒号只有 2px 宽、
/// 连字符被误识别成「一」且高度只有 1px。
/// </summary>
internal static class OcrProbeData
{
    private static WordBox W(string text, double x, double y, double w, double h) => new(text, x, y, w, h);

    /// <summary>第 0 行：纯中文。中文逐字成词，共 14 个词。</summary>
    public static OcrLine ChineseLine() => new(new[]
    {
        W("这", 45, 46, 28, 28),
        W("是", 74, 49, 29, 25),
        W("一", 103, 59, 29, 3),
        W("段", 132, 48, 29, 26),
        W("纯", 162, 47, 28, 27),
        W("中", 192, 48, 25, 27),
        W("文", 219, 45, 29, 28),
        W("的", 249, 46, 27, 28),
        W("测", 277, 47, 28, 27),
        W("试", 306, 47, 29, 28),
        W("文", 335, 45, 29, 28),
        W("字", 364, 46, 29, 28),
        W("内", 396, 48, 23, 25),
        W("容", 422, 47, 29, 27),
    });

    /// <summary>
    /// 第 1 行：纯英文。注意 OCR 把 "over" 拆成了 "ove" + "r"，
    /// 把 "13" 拆成了 "1" + "3"——靠词距规则重新粘回去。
    /// </summary>
    public static OcrLine EnglishLine() => new(new[]
    {
        W("The", 45, 120, 45, 22),
        W("quick", 100, 120, 66, 29),
        W("brown", 176, 120, 78, 22),
        W("foxjumps", 263, 120, 123, 29),
        W("ove", 396, 127, 44, 15),
        W("r", 443, 127, 8, 15),
        W("1", 462, 121, 8, 21),
        W("3", 476, 121, 13, 21),
        W("lazy", 501, 120, 47, 29),
        W("dogs.", 557, 120, 67, 29),
    });

    /// <summary>第 2 行：中英混排。「排」被误识别成「扌」+「非」两个偏旁。</summary>
    public static OcrLine MixedLine() => new(new[]
    {
        W("中", 47, 188, 25, 27),
        W("英", 76, 187, 29, 28),
        W("混", 106, 188, 28, 27),
        W("扌", 136, 187, 9, 27),
        W("非", 144, 187, 21, 27),
        W("mixed", 177, 188, 80, 23),
        W("content", 278, 190, 105, 21),
        W("测", 392, 187, 28, 27),
        W("试", 423, 187, 29, 28),
        W("OCR", 462, 189, 61, 22),
        W("engine", 540, 188, 93, 30),
        W("效", 643, 186, 29, 28),
        W("果", 673, 189, 28, 25),
    });

    /// <summary>第 3 行：小字号。全角冒号「：」的宽度只有 2px——命中测试的硬骨头。</summary>
    public static OcrLine SmallFontLine() => new(new[]
    {
        W("小", 43, 264, 17, 15),
        W("字", 61, 264, 17, 15),
        W("号", 78, 265, 17, 14),
        W("测", 95, 264, 16, 16),
        W("试", 112, 264, 17, 16),
        W("：", 132, 269, 2, 9),
        W("验", 146, 264, 17, 16),
        W("证", 164, 264, 16, 15),
        W("12px", 192, 265, 40, 17),
        W("左", 238, 264, 17, 15),
        W("右", 256, 264, 16, 16),
        W("的", 273, 264, 15, 16),
        W("文", 289, 263, 17, 16),
        W("字", 306, 264, 17, 15),
        W("能", 323, 264, 17, 16),
        W("否", 341, 265, 15, 15),
        W("被", 357, 263, 17, 17),
        W("正", 374, 265, 17, 14),
        W("确", 391, 263, 16, 17),
        W("识", 408, 264, 17, 16),
        W("别", 425, 264, 16, 15),
    });

    /// <summary>
    /// 第 4 行：邮箱与电话。连字符「-」被误识别成汉字「一」，
    /// 且它的 BoundingRect 高度只有 1px——扁词命中测试的硬骨头。
    /// </summary>
    public static OcrLine ContactLine() => new(new[]
    {
        W("EmaiI:", 48, 330, 71, 22),
        W("test@example.com", 128, 330, 241, 29),
        W("Tel:", 403, 330, 42, 22),
        W("138", 457, 331, 44, 21),
        W("一", 504, 343, 8, 1),
        W("0000", 515, 331, 62, 21),
        W("一", 580, 343, 8, 1),
        W("1234", 593, 331, 60, 21),
    });

    /// <summary>完整的五行文档，尺寸与探针测试图一致。</summary>
    public static OcrDocument FullDocument() => new(
        new[] { ChineseLine(), EnglishLine(), MixedLine(), SmallFontLine(), ContactLine() },
        900,
        420);

    public static TextSelectionModel FullModel() => new(FullDocument());

    public static TextSelectionModel ModelOf(params OcrLine[] lines) =>
        new(new OcrDocument(lines, 900, 420));
}

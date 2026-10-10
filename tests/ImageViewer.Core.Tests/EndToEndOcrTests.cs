using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using ImageViewer.Core.Decoding;
using ImageViewer.Core.Ocr;

namespace ImageViewer.Core.Tests;

/// <summary>
/// 端到端链路测试：图片文件 → WIC 解码 → 系统 OCR → 选择模型 → 复制文本。
///
/// 这里不碰 GUI：鼠标拖拽被还原成它的本质——两个图像坐标点，
/// 交给 <see cref="TextSelectionModel"/> 算出选区，与真实拖选走的是同一套代码。
///
/// 机器上没有任何 OCR 语言包时这些测试会自行跳过（断言 Skip 逻辑见各用例开头）。
/// </summary>
public class EndToEndOcrTests : IDisposable
{
    private const string Line1 = "这是第一行中文测试内容";
    private const string Line2 = "The quick brown fox jumps.";
    private const string Line3 = "第三行用来验证跨行选择";

    private readonly string _imagePath;

    public EndToEndOcrTests()
    {
        _imagePath = Path.Combine(
            Path.GetTempPath(),
            $"imageviewer-e2e-{Guid.NewGuid():N}.png");

        CreateTestImage(_imagePath);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_imagePath))
            {
                File.Delete(_imagePath);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响测试结论
        }
    }

    private static void CreateTestImage(string path)
    {
        using var bitmap = new Bitmap(900, 340);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.White);
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var font = new Font("Microsoft YaHei", 24);
        graphics.DrawString(Line1, font, Brushes.Black, 40, 40);
        graphics.DrawString(Line2, font, Brushes.Black, 40, 140);
        graphics.DrawString(Line3, font, Brushes.Black, 40, 240);

        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>跑完整条链路，拿到可供选择的模型。引擎不可用时返回 null。</summary>
    private async Task<TextSelectionModel?> BuildModelAsync()
    {
        var engine = WindowsOcrEngine.TryCreate();
        if (engine is null)
        {
            return null; // 本机没装任何 OCR 语言包
        }

        var decoder = new WicImageDecoder();
        using var image = await decoder.DecodeAsync(_imagePath, 0, CancellationToken.None);

        var document = await engine.RecognizeAsync(image.Bitmap, 1.0, CancellationToken.None);

        return new TextSelectionModel(document);
    }

    [Fact]
    public async Task 能从真实图片文件解码出正确的尺寸与格式()
    {
        var decoder = new WicImageDecoder();

        var metadata = await decoder.ReadMetadataAsync(_imagePath, CancellationToken.None);

        Assert.Equal(900u, metadata.DisplayWidth);
        Assert.Equal(340u, metadata.DisplayHeight);
        Assert.Equal(1u, metadata.FrameCount);
        Assert.False(metadata.IsAnimated);
    }

    [Fact]
    public async Task 解码结果是OCR与渲染都能直接使用的格式()
    {
        var decoder = new WicImageDecoder();

        using var image = await decoder.DecodeAsync(_imagePath, 0, CancellationToken.None);

        Assert.Equal(900, image.PixelWidth);
        Assert.Equal(340, image.PixelHeight);
        Assert.False(image.IsPreview);
        Assert.Equal(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, image.Bitmap.BitmapPixelFormat);
    }

    [Fact]
    public async Task 降采样解码得到的预览尺寸受限且被标记为预览()
    {
        var decoder = new WicImageDecoder();

        using var preview = await decoder.DecodeAsync(_imagePath, 200, CancellationToken.None);

        Assert.True(preview.IsPreview);
        Assert.True(Math.Max(preview.PixelWidth, preview.PixelHeight) <= 200);
    }

    [Fact]
    public async Task 真实OCR能识别出三行文字()
    {
        var model = await BuildModelAsync();
        if (model is null)
        {
            return;
        }

        Assert.False(model.IsEmpty);
        Assert.Equal(3, model.LineCount);
    }

    [Fact]
    public async Task 拖选整行得到的文本中文之间没有空格()
    {
        var model = await BuildModelAsync();
        if (model is null)
        {
            return;
        }

        var line = model.Document.Lines[0];

        // 这正是真实拖拽做的事：起点、终点两个图像坐标
        var anchor = model.CaretFromPoint(line.Left - 10, line.CenterY);
        var focus = model.CaretFromPoint(line.Right + 10, line.CenterY);

        var text = model.GetText(model.CreateRange(anchor, focus));

        Assert.Equal(Line1, text);
    }

    [Fact]
    public async Task 拖选英文行保留单词之间的空格()
    {
        var model = await BuildModelAsync();
        if (model is null)
        {
            return;
        }

        var line = model.Document.Lines[1];
        var anchor = model.CaretFromPoint(line.Left - 10, line.CenterY);
        var focus = model.CaretFromPoint(line.Right + 10, line.CenterY);

        var text = model.GetText(model.CreateRange(anchor, focus));

        // 逐词断言，避开引擎偶发的字母误识别
        Assert.Contains("quick", text);
        Assert.Contains("brown", text);
        Assert.Contains("quick brown", text);
    }

    [Fact]
    public async Task 跨行拖选得到多行文本且以换行分隔()
    {
        var model = await BuildModelAsync();
        if (model is null)
        {
            return;
        }

        var first = model.Document.Lines[0];
        var third = model.Document.Lines[2];

        // 从第一行开头一路拖到第三行结尾
        var anchor = model.CaretFromPoint(first.Left - 10, first.CenterY);
        var focus = model.CaretFromPoint(third.Right + 10, third.CenterY);

        var text = model.GetText(model.CreateRange(anchor, focus));
        var lines = text.Split("\r\n");

        Assert.Equal(3, lines.Length);
        Assert.Equal(Line1, lines[0]);
        Assert.Equal(Line3, lines[2]);
    }

    [Fact]
    public async Task 拖选半行只得到被划过的那部分()
    {
        var model = await BuildModelAsync();
        if (model is null)
        {
            return;
        }

        var line = model.Document.Lines[0];
        var midX = (line.Left + line.Right) / 2;

        var anchor = model.CaretFromPoint(line.Left - 10, line.CenterY);
        var focus = model.CaretFromPoint(midX, line.CenterY);

        var text = model.GetText(model.CreateRange(anchor, focus));

        Assert.NotEmpty(text);
        Assert.True(text.Length < Line1.Length, "半行选择不应覆盖整行");
        Assert.StartsWith(text, Line1, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 悬停在文字上返回词索引而空白处返回空()
    {
        var model = await BuildModelAsync();
        if (model is null)
        {
            return;
        }

        var line = model.Document.Lines[0];

        // 文字上 → 光标应变成 I-beam
        Assert.NotNull(model.WordIndexFromPoint(line.Left + 5, line.CenterY));

        // 三行文字之外的大片空白 → 左键应当是拖动图片
        Assert.Null(model.WordIndexFromPoint(850, 320));
    }

    [Fact]
    public async Task 识别结果缓存命中后返回同一份文档()
    {
        var engine = WindowsOcrEngine.TryCreate();
        if (engine is null)
        {
            return;
        }

        var decoder = new WicImageDecoder();
        var cache = new OcrResultCache();
        var key = OcrResultCache.TryBuildKey(_imagePath);

        Assert.NotNull(key);

        using var image = await decoder.DecodeAsync(_imagePath, 0, CancellationToken.None);
        var first = await engine.RecognizeAsync(image.Bitmap, 1.0, CancellationToken.None);

        cache.Set(key!, first);

        Assert.True(cache.TryGet(key!, out var cached));
        Assert.Same(first, cached);
    }

    [Fact]
    public async Task 文件被修改后缓存键随之变化()
    {
        var key1 = OcrResultCache.TryBuildKey(_imagePath);

        await Task.Delay(20);
        File.SetLastWriteTimeUtc(_imagePath, DateTime.UtcNow.AddMinutes(1));

        var key2 = OcrResultCache.TryBuildKey(_imagePath);

        Assert.NotNull(key1);
        Assert.NotNull(key2);
        Assert.NotEqual(key1, key2);
    }
}

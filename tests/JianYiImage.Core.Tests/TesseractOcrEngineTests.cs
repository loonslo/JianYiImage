using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using JianYiImage.Core.Decoding;
using JianYiImage.Core.Ocr;

namespace JianYiImage.Core.Tests;

public class TesseractOcrEngineTests
{
    [Fact]
    public async Task 无文字位图返回空文档()
    {
        var engine = Assert.IsType<TesseractOcrEngine>(TesseractOcrEngine.TryCreate());
        using var bitmap = new Windows.Graphics.Imaging.SoftwareBitmap(
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, 120, 80, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);
        var document = await engine.RecognizeAsync(bitmap, 1, CancellationToken.None);
        Assert.True(document.IsEmpty);
        Assert.Equal(120, document.ImageWidth);
        Assert.Equal(80, document.ImageHeight);
    }
    [Fact]
    public void 模型缺失时不提供精细模式()
    {
        Assert.Null(TesseractOcrEngine.TryCreate(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 明暗图片能识别邮箱文件名运算符并还原选区坐标(bool dark)
    {
        var path = Path.Combine(Path.GetTempPath(), $"jianyiimage-precise-test-{Guid.NewGuid():N}.png");
        try
        {
            using (var bitmap = new Bitmap(1300, 400))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font("Consolas", 26, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                graphics.Clear(dark ? Color.FromArgb(40, 40, 40) : Color.White);
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                var brush = dark ? Brushes.White : Brushes.Black;
                string[] lines = ["https://example.com/path", "Email: test@example.com", "file_name.py", "a <= b && c >= d", "Tel: 138-0000-1234"];
                for (var i = 0; i < lines.Length; i++) graphics.DrawString(lines[i], font, brush, 30, 25 + i * 65);
                bitmap.Save(path, ImageFormat.Png);
            }
            var engine = Assert.IsType<TesseractOcrEngine>(TesseractOcrEngine.TryCreate());
            using var image = await new WicImageDecoder().DecodeAsync(path, 0, CancellationToken.None);
            var document = await engine.RecognizeAsync(image.Bitmap, 2, CancellationToken.None);
            var model = new TextSelectionModel(document);
            var text = model.GetAllText();
            Assert.Contains("https://example.com/path", text);
            Assert.Contains("test@example.com", text);
            Assert.True(text.Contains("file_name.py"), text);
            Assert.Contains("<=", text);
            Assert.Contains("&&", text);
            Assert.Contains(">=", text);
            Assert.Contains("138-0000-1234", text);
            Assert.Equal(2600, document.ImageWidth);
            Assert.Equal(800, document.ImageHeight);
            Assert.All(model.Words, w =>
            {
                Assert.InRange(w.Left, 0, 2600);
                Assert.InRange(w.Right, w.Left, 2600);
                Assert.InRange(w.Top, 0, 800);
                Assert.InRange(w.Bottom, w.Top, 800);
            });
            var first = document.Lines.OrderBy(l => l.Top).First();
            Assert.InRange(first.Left, 50, 85);
            var anchor = model.CaretFromPoint(first.Left - 5, first.CenterY);
            var focus = model.CaretFromPoint(first.Right + 5, first.CenterY);
            Assert.Equal("https://example.com/path", model.GetText(model.CreateRange(anchor, focus)));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.RecognizeAsync(image.Bitmap, 1, cancelled.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

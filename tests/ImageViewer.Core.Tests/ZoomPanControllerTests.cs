using ImageViewer.Core.Rendering;

namespace ImageViewer.Core.Tests;

public class ZoomPanControllerTests
{
    private static ZoomPanController Create(
        double imageWidth = 4000, double imageHeight = 3000,
        double viewportWidth = 1000, double viewportHeight = 800)
    {
        var controller = new ZoomPanController();
        controller.SetViewport(viewportWidth, viewportHeight);
        controller.SetImage(imageWidth, imageHeight);
        return controller;
    }

    // ---------- 锚点缩放：滚轮手感的核心 ----------

    [Fact]
    public void 以鼠标为锚点缩放时锚点下的图像内容保持不动()
    {
        var controller = Create();
        controller.FitToWindow();

        const double anchorX = 730;
        const double anchorY = 210;
        var before = controller.ViewportToImage(anchorX, anchorY);

        controller.ZoomAt(2.5, anchorX, anchorY);

        var after = controller.ViewportToImage(anchorX, anchorY);

        Assert.Equal(before.X, after.X, precision: 6);
        Assert.Equal(before.Y, after.Y, precision: 6);
    }

    [Fact]
    public void 连续多次锚点缩放不会累积漂移()
    {
        var controller = Create();
        controller.FitToWindow();

        const double anchorX = 420;
        const double anchorY = 640;
        var before = controller.ViewportToImage(anchorX, anchorY);

        for (var i = 0; i < 20; i++)
        {
            controller.ZoomAt(1.2, anchorX, anchorY);
        }

        var after = controller.ViewportToImage(anchorX, anchorY);

        Assert.Equal(before.X, after.X, precision: 4);
        Assert.Equal(before.Y, after.Y, precision: 4);
    }

    [Fact]
    public void 低于百分之百但尚未适应视口时自动收至完整显示比例()
    {
        var controller = Create();
        controller.ActualSize();

        controller.ZoomTo(0.9, 500, 400);

        Assert.Equal(0.25, controller.Scale, precision: 6);
        AssertEntireImageVisible(controller);
    }

    [Fact]
    public void 适应视口后仍可继续缩小且整张图保持可见()
    {
        var controller = Create();
        controller.ActualSize();
        controller.ZoomTo(0.2, 500, 400);

        Assert.Equal(0.2, controller.Scale, precision: 6);
        AssertEntireImageVisible(controller);
    }

    [Fact]
    public void 视口比图片大时百分之百以下不受额外限制()
    {
        var controller = Create(200, 150, 1000, 800);
        controller.ZoomTo(0.9, 500, 400);

        Assert.Equal(0.9, controller.Scale, precision: 6);
        AssertEntireImageVisible(controller);
    }

    [Fact]
    public void 缩放比被限制在上下限之内()
    {
        var controller = Create();

        controller.ZoomTo(1000, 500, 400);
        Assert.Equal(ZoomPanController.MaxScale, controller.Scale);

        controller.ZoomTo(0.0001, 500, 400);
        Assert.Equal(ZoomPanController.MinScale, controller.Scale);
    }

    // ---------- 插值策略：「清晰」的实现 ----------

    [Theory]
    [InlineData(0.25, ScalingQuality.HighQualityCubic)]
    [InlineData(0.99, ScalingQuality.HighQualityCubic)]
    [InlineData(1.0, ScalingQuality.Linear)]
    [InlineData(1.99, ScalingQuality.Linear)]
    [InlineData(2.0, ScalingQuality.NearestNeighbor)]
    [InlineData(8.0, ScalingQuality.NearestNeighbor)]
    public void 插值方式随缩放比切换(double scale, ScalingQuality expected)
    {
        var controller = Create();
        controller.ZoomTo(scale, 500, 400);

        Assert.Equal(expected, controller.Quality);
    }

    // ---------- 适应窗口 ----------

    [Theory]
    [InlineData(960, 620, 1.0)]
    [InlineData(748, 460, 1.0)]
    [InlineData(960, 620, 1.5)]
    [InlineData(960, 620, 2.0)]
    public void 图片先于窗口布局完成时仍在实际视口中完整显示(double width, double height, double dpiScale)
    {
        var controller = Create(1280, 994, 0, 0);
        controller.FitToWindowWithoutUpscale();

        controller.SetViewport(width * dpiScale, height * dpiScale);

        Assert.Equal(Math.Min(1, Math.Min(width * dpiScale / 1280, height * dpiScale / 994)), controller.Scale, precision: 6);
        AssertEntireImageVisible(controller);
    }

    [Fact]
    public void 展开与关闭文字面板后自动适应实际图片区域()
    {
        var controller = Create(1280, 994, 1000, 720);
        controller.FitToWindowWithoutUpscale();
        var originalScale = controller.Scale;

        controller.SetViewport(636, 720); // 340px 文字面板和两侧 12px 边距

        Assert.Equal(636.0 / 1280, controller.Scale, precision: 6);
        AssertEntireImageVisible(controller);

        controller.SetViewport(1000, 720);

        Assert.Equal(originalScale, controller.Scale, precision: 6);
        AssertEntireImageVisible(controller);
    }

    [Fact]
    public void 显式适应窗口随窗口缩小重新计算且允许放大小图()
    {
        var controller = Create(200, 150, 1000, 800);
        controller.FitToWindow();

        controller.SetViewport(500, 300);

        Assert.Equal(2, controller.Scale, precision: 6);
        AssertEntireImageVisible(controller);
    }

    [Fact]
    public void 手动滚轮缩放之后调整窗口保留用户比例()
    {
        var controller = Create();
        controller.FitToWindowWithoutUpscale();
        controller.ZoomAt(1.15, 500, 400);
        var manualScale = controller.Scale;

        controller.SetViewport(636, 500);

        Assert.Equal(manualScale, controller.Scale, precision: 6);
    }

    [Fact]
    public void 点击已处于百分之百的小图也会进入固定比例模式()
    {
        var controller = Create(200, 150, 1000, 800);
        controller.FitToWindowWithoutUpscale();
        controller.ActualSize();

        controller.SetViewport(150, 100);

        Assert.Equal(1, controller.Scale);
        Assert.True(controller.CanPan);
    }

    [Fact]
    public void 视口暂时归零不会破坏适应模式且恢复后完整显示()
    {
        var controller = Create(1280, 994, 1000, 720);
        controller.FitToWindowWithoutUpscale();
        var originalScale = controller.Scale;

        controller.SetViewport(0, 0);
        Assert.Equal(originalScale, controller.Scale);
        controller.SetViewport(636, 460);

        AssertEntireImageVisible(controller);
    }

    private static void AssertEntireImageVisible(ZoomPanController controller)
    {
        const double tolerance = 0.000001;
        Assert.InRange(controller.OffsetX, -tolerance, controller.ViewportWidth);
        Assert.InRange(controller.OffsetY, -tolerance, controller.ViewportHeight);
        Assert.True(controller.OffsetX + controller.ScaledWidth <= controller.ViewportWidth + tolerance);
        Assert.True(controller.OffsetY + controller.ScaledHeight <= controller.ViewportHeight + tolerance);
        Assert.False(controller.CanPan);
    }

    [Fact]
    public void 适应窗口时大图按较小的那个方向缩放()
    {
        var controller = Create(4000, 3000, 1000, 800);

        controller.FitToWindow();

        // 1000/4000 = 0.25 比 800/3000 ≈ 0.267 更小
        Assert.Equal(0.25, controller.Scale, precision: 6);
    }

    [Fact]
    public void 适应窗口后图像在视口中居中()
    {
        var controller = Create(4000, 3000, 1000, 800);

        controller.FitToWindow();

        Assert.Equal(0, controller.OffsetX, precision: 6);
        Assert.Equal((800 - 750) / 2.0, controller.OffsetY, precision: 6);
    }

    [Fact]
    public void 默认打开时小图保持原始大小不被放大()
    {
        var controller = Create(200, 150, 1000, 800);

        controller.FitToWindowWithoutUpscale();

        Assert.Equal(1.0, controller.Scale, precision: 6);
    }

    [Fact]
    public void 默认打开时大图缩小到适应窗口()
    {
        var controller = Create(4000, 3000, 1000, 800);

        controller.FitToWindowWithoutUpscale();

        Assert.Equal(0.25, controller.Scale, precision: 6);
    }

    // ---------- 平移边界 ----------

    [Fact]
    public void 图像小于视口时始终居中且无法平移()
    {
        var controller = Create(200, 150, 1000, 800);
        controller.ActualSize();

        controller.Pan(-500, -500);

        Assert.Equal((1000 - 200) / 2.0, controller.OffsetX, precision: 6);
        Assert.Equal((800 - 150) / 2.0, controller.OffsetY, precision: 6);
        Assert.False(controller.CanPan);
    }

    [Fact]
    public void 图像大于视口时不允许拖出空白边()
    {
        var controller = Create(4000, 3000, 1000, 800);
        controller.ActualSize();

        // 往右下方猛拖，左上角最多只能到 0
        controller.Pan(10000, 10000);
        Assert.Equal(0, controller.OffsetX, precision: 6);
        Assert.Equal(0, controller.OffsetY, precision: 6);

        // 往左上方猛拖，右下角最多只能到视口边缘
        controller.Pan(-100000, -100000);
        Assert.Equal(1000 - 4000, controller.OffsetX, precision: 6);
        Assert.Equal(800 - 3000, controller.OffsetY, precision: 6);
    }

    [Fact]
    public void 图像超出视口时可以平移()
    {
        var controller = Create(4000, 3000, 1000, 800);
        controller.ActualSize();

        Assert.True(controller.CanPan);
    }

    // ---------- 坐标变换 ----------

    [Fact]
    public void 视口与图像坐标之间可以往返转换()
    {
        var controller = Create();
        controller.ZoomTo(3.7, 500, 400);
        controller.Pan(-120, -260);

        var (vx, vy) = controller.ImageToViewport(321, 654);
        var (ix, iy) = controller.ViewportToImage(vx, vy);

        Assert.Equal(321, ix, precision: 6);
        Assert.Equal(654, iy, precision: 6);
    }

    // ---------- 边界情况 ----------

    [Fact]
    public void 没有图像时各项操作不抛异常()
    {
        var controller = new ZoomPanController();
        controller.SetViewport(1000, 800);

        controller.FitToWindow();
        controller.ActualSize();
        controller.ZoomAt(2, 100, 100);
        controller.Pan(50, 50);

        Assert.False(controller.HasImage);
        Assert.False(controller.CanPan);
    }

    [Fact]
    public void 视口尺寸为零时不抛异常()
    {
        var controller = new ZoomPanController();
        controller.SetImage(4000, 3000);
        controller.SetViewport(0, 0);

        controller.FitToWindow();

        Assert.True(controller.HasImage);
    }

    [Fact]
    public void 换图后缩放状态被重置()
    {
        var controller = Create();
        controller.ZoomTo(8, 500, 400);

        controller.SetImage(1024, 768);

        Assert.Equal(1.0, controller.Scale, precision: 6);
        Assert.Equal(0, controller.OffsetX, precision: 6);
        Assert.Equal(0, controller.OffsetY, precision: 6);
    }
}

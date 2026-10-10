namespace ImageViewer.Core.Rendering;

/// <summary>
/// 缩放与平移的状态机。纯几何运算，不碰任何 UI 类型，因此可以完整单测。
///
/// 坐标关系： viewport = image × Scale + Offset
/// </summary>
public sealed class ZoomPanController
{
    /// <summary>超过这个缩放比就切最近邻插值，保住像素边缘。</summary>
    public const double NearestNeighborThreshold = 2.0;

    public const double MinScale = 0.02;
    public const double MaxScale = 64.0;

    private double _viewportWidth;
    private double _viewportHeight;
    private double _imageWidth;
    private double _imageHeight;
    private ViewportFitMode _viewportFitMode;

    private enum ViewportFitMode
    {
        Manual,
        Fit,
        FitWithoutUpscale,
    }

    public double Scale { get; private set; } = 1.0;

    /// <summary>图像左上角在视口中的 X 坐标。</summary>
    public double OffsetX { get; private set; }

    /// <summary>图像左上角在视口中的 Y 坐标。</summary>
    public double OffsetY { get; private set; }

    public double ViewportWidth => _viewportWidth;

    public double ViewportHeight => _viewportHeight;

    public double ImageWidth => _imageWidth;

    public double ImageHeight => _imageHeight;

    /// <summary>图像按当前缩放比占据的宽度。</summary>
    public double ScaledWidth => _imageWidth * Scale;

    /// <summary>图像按当前缩放比占据的高度。</summary>
    public double ScaledHeight => _imageHeight * Scale;

    public bool HasImage => _imageWidth > 0 && _imageHeight > 0;

    /// <summary>图像是否已超出视口（决定是否需要平移）。</summary>
    public bool CanPan => ScaledWidth > _viewportWidth + 0.5 || ScaledHeight > _viewportHeight + 0.5;

    /// <summary>当前缩放比对应的插值方式。</summary>
    public ScalingQuality Quality => Scale switch
    {
        < 1.0 => ScalingQuality.HighQualityCubic,
        < NearestNeighborThreshold => ScalingQuality.Linear,
        _ => ScalingQuality.NearestNeighbor,
    };

    public void SetViewport(double width, double height)
    {
        var newWidth = Math.Max(0, width);
        var newHeight = Math.Max(0, height);
        if (_viewportWidth == newWidth && _viewportHeight == newHeight)
        {
            return;
        }

        _viewportWidth = newWidth;
        _viewportHeight = newHeight;
        ApplyViewportFit();
        ClampOffset();
    }

    /// <summary>换图时调用。会重置缩放状态。</summary>
    public void SetImage(double width, double height)
    {
        _imageWidth = Math.Max(0, width);
        _imageHeight = Math.Max(0, height);
        _viewportFitMode = ViewportFitMode.Manual;
        Scale = 1.0;
        OffsetX = 0;
        OffsetY = 0;
    }

    /// <summary>完全适应窗口——小图也会被放大填满。</summary>
    public void FitToWindow()
    {
        _viewportFitMode = ViewportFitMode.Fit;
        ApplyViewportFit();
    }

    /// <summary>
    /// 打开图片时的默认行为：大图缩小到适应窗口，小图保持 100% 不放大。
    /// 这是绝大多数看图软件的默认手感。
    /// </summary>
    public void FitToWindowWithoutUpscale()
    {
        // 即使首次布局还没有尺寸，也记住请求，在视口就绪时补做适应。
        _viewportFitMode = ViewportFitMode.FitWithoutUpscale;
        ApplyViewportFit();
    }

    private void ApplyViewportFit()
    {
        if (_viewportFitMode == ViewportFitMode.Manual
            || !HasImage || _viewportWidth <= 0 || _viewportHeight <= 0)
        {
            return;
        }

        var fit = Math.Min(_viewportWidth / _imageWidth, _viewportHeight / _imageHeight);
        Scale = Math.Clamp(
            _viewportFitMode == ViewportFitMode.FitWithoutUpscale ? Math.Min(fit, 1.0) : fit,
            MinScale, MaxScale);

        CenterInViewport();
    }

    /// <summary>100% 原始像素（Ctrl+0）。以视口中心为锚点。</summary>
    public void ActualSize() => ZoomTo(1.0, _viewportWidth / 2, _viewportHeight / 2);

    /// <summary>
    /// 以某个视口坐标为锚点缩放——滚轮缩放必须用这个，
    /// 否则鼠标指着的内容会在缩放中跑掉。
    /// </summary>
    public void ZoomAt(double factor, double anchorX, double anchorY)
    {
        if (factor <= 0)
        {
            return;
        }

        ZoomTo(Scale * factor, anchorX, anchorY);
    }

    /// <summary>缩放到指定比例，保持锚点处的图像内容不动。</summary>
    public void ZoomTo(double targetScale, double anchorX, double anchorY)
    {
        if (!HasImage)
        {
            return;
        }

        // 手动缩放（包括已经是 100% 时再次点击实际大小）关闭自动适应。
        _viewportFitMode = ViewportFitMode.Manual;
        var newScale = Math.Clamp(targetScale, MinScale, MaxScale);
        if (newScale < 1.0 && _viewportWidth > 0 && _viewportHeight > 0)
        {
            // 低于 100% 表示缩小查看整张图。若目标比例仍会超出视口，
            // 直接收至完整适应比例，避免把“缩小”变成仅显示图片的一部分。
            var fitScale = Math.Min(_viewportWidth / _imageWidth, _viewportHeight / _imageHeight);
            newScale = Math.Min(newScale, fitScale);
        }

        if (Math.Abs(newScale - Scale) < double.Epsilon)
        {
            return;
        }

        // 锚点对应的图像坐标在缩放前后必须保持一致
        var imageX = (anchorX - OffsetX) / Scale;
        var imageY = (anchorY - OffsetY) / Scale;

        Scale = newScale;
        OffsetX = anchorX - imageX * newScale;
        OffsetY = anchorY - imageY * newScale;

        ClampOffset();
    }

    /// <summary>拖拽平移。</summary>
    public void Pan(double deltaX, double deltaY)
    {
        if (!HasImage)
        {
            return;
        }

        OffsetX += deltaX;
        OffsetY += deltaY;
        ClampOffset();
    }

    public (double X, double Y) ImageToViewport(double imageX, double imageY) =>
        (imageX * Scale + OffsetX, imageY * Scale + OffsetY);

    public (double X, double Y) ViewportToImage(double viewportX, double viewportY) =>
        Scale <= 0 ? (0, 0) : ((viewportX - OffsetX) / Scale, (viewportY - OffsetY) / Scale);

    private void CenterInViewport()
    {
        OffsetX = (_viewportWidth - ScaledWidth) / 2;
        OffsetY = (_viewportHeight - ScaledHeight) / 2;
    }

    /// <summary>
    /// 约束偏移：图像比视口小时居中，比视口大时不允许拖出空白边。
    /// </summary>
    private void ClampOffset()
    {
        if (!HasImage)
        {
            return;
        }

        OffsetX = ClampAxis(OffsetX, ScaledWidth, _viewportWidth);
        OffsetY = ClampAxis(OffsetY, ScaledHeight, _viewportHeight);
    }

    private static double ClampAxis(double offset, double scaledSize, double viewportSize)
    {
        if (scaledSize <= viewportSize)
        {
            return (viewportSize - scaledSize) / 2;
        }

        return Math.Clamp(offset, viewportSize - scaledSize, 0);
    }
}

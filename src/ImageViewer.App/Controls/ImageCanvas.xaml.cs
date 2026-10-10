using System.Numerics;
using ImageViewer.Core.Decoding;
using ImageViewer.Core.Ocr;
using ImageViewer.Core.Rendering;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;

namespace ImageViewer.App.Controls;

/// <summary>
/// 看图画布：负责图像渲染、缩放平移，以及实况文本的选择与高亮。
///
/// 左键的行为按位置自动切换——悬停在识别出的文字上时选字，
/// 其余位置拖动图片。因此不需要任何「模式切换」按钮。
/// </summary>
public sealed partial class ImageCanvas : UserControl
{
    private readonly Color _selectionFill;
    private readonly Color _selectionBorder;

    // 区域截图选区用不同颜色区分——和文字高亮（蓝）一眼能分开
    private static readonly Color RegionSelectionFill = Color.FromArgb(60, 255, 255, 255);
    private static readonly Color RegionSelectionBorder = Color.FromArgb(220, 255, 255, 255);

    private readonly ZoomPanController _zoom = new();

    private SoftwareBitmap? _sourceBitmap;
    private CanvasBitmap? _canvasBitmap;
    private RotationQuarterTurn _rotation = RotationQuarterTurn.None;

    // 动图播放：_sourceBitmap 在播放期间会被定时器逐帧换成 _animation 里的对应帧，
    // 其余渲染/旋转/缩放代码完全不用关心「现在是不是在放动图」
    private AnimatedImage? _animation;
    private DispatcherTimer? _animationTimer;
    private int _animationFrameIndex;

    private TextSelectionModel? _selectionModel;
    private SelectionRange _selection = SelectionRange.Empty;

    // 拖拽状态
    private bool _isPanning;
    private bool _isSelectingText;
    private bool _isRegionSelecting;
    private int _selectionAnchorCaret;
    private Point _lastPointerPixels;
    private Point _regionAnchorBitmapPoint;
    private Rect? _regionSelection; // 位图坐标系下的矩形，无文字时的兜底截图选区
    private uint _capturedPointerId;

    public ImageCanvas()
    {
        InitializeComponent();
        _selectionFill = (Color)Application.Current.Resources["AppSelectionFillColor"];
        _selectionBorder = (Color)Application.Current.Resources["AppSelectionRingColor"];

        SizeChanged += OnSizeChanged;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        PointerWheelChanged += OnPointerWheelChanged;
        DoubleTapped += OnDoubleTapped;
    }

    /// <summary>选区变化时触发，供窗口更新状态栏与复制命令的可用状态。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>缩放比变化时触发。</summary>
    public event EventHandler? ZoomChanged;

    public double ZoomScale => _zoom.Scale;

    /// <summary>
    /// 是否有图可交互——特意不用 `_canvasBitmap is not null`。
    /// `_canvasBitmap` 是 GPU 侧资源，创建时机依赖 Win2D 设备就绪（`Canvas.ReadyToDraw`），
    /// 在虚拟显示适配器（远程桌面场景）下这个资源可能短暂或反复创建失败——
    /// 如果拿它做交互开关，一旦 GPU 资源没建好，画面还停留在最后一帧（视觉上像是"卡住了"），
    /// 但缩放/旋转/选字全部会被这里挡住，静默失效。真正决定「有没有图在看」的是解码结果
    /// `_sourceBitmap` 是否存在，GPU 资源建不建得出来是渲染层自己的事（见 OnDraw 的自愈重试）。
    /// </summary>
    public bool HasImage => _sourceBitmap is not null;

    /// <summary>当前是否选中了文字。</summary>
    public bool HasTextSelection => !_selection.IsEmpty;

    /// <summary>本图是否识别出了可选的文字。</summary>
    public bool HasRecognizedText => _selectionModel is { IsEmpty: false };

    /// <summary>当前是否有一块矩形区域被选中（无文字时的兜底截图选区）。</summary>
    public bool HasRegionSelection => _regionSelection is { Width: >= 1, Height: >= 1 };

    /// <summary>
    /// 展示层旋转（R / Shift+R），换图时重置。
    /// 名字特地不叫 Rotation——UIElement 自带一个同名但含义完全不同的属性
    /// （2D 视觉旋转角度），撞了会很容易看错。
    /// </summary>
    public RotationQuarterTurn DisplayRotation => _rotation;

    /// <summary>当前显示的是不是一张正在播放的动图。</summary>
    public bool IsAnimating => _animation is not null;

    // ---------- 内容设置 ----------

    /// <summary>换静态图。会重置缩放、旋转、选区和识别结果。</summary>
    public void SetImage(DecodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        StopAnimation();

        _sourceBitmap = image.Bitmap;
        _rotation = RotationQuarterTurn.None;
        _selectionModel = null;
        _selection = SelectionRange.Empty;
        _regionSelection = null;

        RecreateCanvasBitmap();

        ApplyZoomImageDimensions();
        UpdateViewportFromControlSize();
        _zoom.FitToWindowWithoutUpscale();

        Canvas.Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 换动图并开始播放。动图不跑 OCR（对着一张不停变化的图做文字选择没有意义），
    /// 旋转/缩放/平移则完全复用静态图的那一套——渲染代码只认「当前这一帧的位图」，
    /// 不关心它是不是会被定时器换掉。
    /// </summary>
    public void SetAnimatedImage(AnimatedImage animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        if (animation.Frames.Count == 0)
        {
            throw new ArgumentException("动图至少要有一帧。", nameof(animation));
        }

        StopAnimation();

        _animation = animation;
        _animationFrameIndex = 0;
        _rotation = RotationQuarterTurn.None;
        _selectionModel = null;
        _selection = SelectionRange.Empty;
        _regionSelection = null;

        ApplyAnimationFrame(0);

        ApplyZoomImageDimensions();
        UpdateViewportFromControlSize();
        _zoom.FitToWindowWithoutUpscale();

        StartAnimationTimer();

        Canvas.Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearImage()
    {
        StopAnimation();

        _sourceBitmap = null;
        _canvasBitmap?.Dispose();
        _canvasBitmap = null;
        _rotation = RotationQuarterTurn.None;
        _selectionModel = null;
        _selection = SelectionRange.Empty;
        _regionSelection = null;

        _zoom.SetImage(0, 0);
        Canvas.Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 装入 OCR 结果，文字随即变为可选。
    /// 识别是后台完成的，所以这一步和 <see cref="SetImage"/> 是分开的。
    /// </summary>
    public void SetOcrDocument(OcrDocument document)
    {
        _selectionModel = new TextSelectionModel(document);
        _selection = SelectionRange.Empty;

        Canvas.Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------- 动图播放 ----------

    private void ApplyAnimationFrame(int index)
    {
        _sourceBitmap = _animation!.Frames[index].Bitmap;
        RecreateCanvasBitmap();
    }

    private void StartAnimationTimer()
    {
        if (_animation is null || _animation.Frames.Count <= 1)
        {
            return; // 只有一帧的话没必要开定时器
        }

        _animationTimer = new DispatcherTimer { Interval = ClampDelay(_animation.Frames[0].Delay) };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void OnAnimationTick(object? sender, object e)
    {
        if (_animation is null || _animationTimer is null)
        {
            return;
        }

        _animationFrameIndex = (_animationFrameIndex + 1) % _animation.Frames.Count;
        ApplyAnimationFrame(_animationFrameIndex);
        Canvas.Invalidate();

        // 每一帧的时长可能不一样，走到下一帧后要把定时器间隔换成下一帧自己的时长
        _animationTimer.Interval = ClampDelay(_animation.Frames[_animationFrameIndex].Delay);
    }

    /// <summary>防止畸形文件里 0 或极小的帧延迟把 CPU 拖到全速刷新。</summary>
    private static TimeSpan ClampDelay(TimeSpan delay) =>
        delay < TimeSpan.FromMilliseconds(20) ? TimeSpan.FromMilliseconds(100) : delay;

    /// <summary>停止播放并释放动图占用的全部帧位图。换成静态图或关闭图片时都要调用。</summary>
    private void StopAnimation()
    {
        if (_animationTimer is not null)
        {
            _animationTimer.Stop();
            _animationTimer.Tick -= OnAnimationTick;
            _animationTimer = null;
        }

        _animation?.Dispose();
        _animation = null;
        _animationFrameIndex = 0;
    }

    // ---------- 缩放命令 ----------

    public void FitToWindow()
    {
        UpdateViewportFromControlSize();
        _zoom.FitToWindow();
        InvalidateAfterZoom();
    }

    public void ZoomToActualSize()
    {
        UpdateViewportFromControlSize();
        _zoom.ActualSize();
        InvalidateAfterZoom();
    }

    public void ZoomIn() => ZoomAtViewportCenter(1.25);

    public void ZoomOut() => ZoomAtViewportCenter(1 / 1.25);

    private void ZoomAtViewportCenter(double factor)
    {
        UpdateViewportFromControlSize();
        _zoom.ZoomAt(factor, _zoom.ViewportWidth / 2, _zoom.ViewportHeight / 2);
        InvalidateAfterZoom();
    }

    // ---------- 旋转命令 ----------

    public void RotateClockwise() => ApplyRotation(_rotation.Next());

    public void RotateCounterClockwise() => ApplyRotation(_rotation.Previous());

    private void ApplyRotation(RotationQuarterTurn rotation)
    {
        if (!HasImage)
        {
            return;
        }

        _rotation = rotation;

        // 宽高可能互换了，重新适应窗口——和换图时的行为保持一致
        ApplyZoomImageDimensions();
        UpdateViewportFromControlSize();
        _zoom.FitToWindow();

        InvalidateAfterZoom();
    }

    /// <summary>把当前旋转状态应用到的逻辑尺寸同步给 ZoomPanController。</summary>
    private void ApplyZoomImageDimensions()
    {
        if (_sourceBitmap is null)
        {
            return;
        }

        var (width, height) = _rotation.ApplyToSize(_sourceBitmap.PixelWidth, _sourceBitmap.PixelHeight);
        _zoom.SetImage(width, height);
    }

    // ---------- 文本选择命令 ----------

    public void SelectAllText()
    {
        if (_selectionModel is null || _selectionModel.IsEmpty)
        {
            return;
        }

        _selection = _selectionModel.SelectAll();
        Canvas.Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        if (_selection.IsEmpty)
        {
            return;
        }

        _selection = SelectionRange.Empty;
        Canvas.Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>取当前选中的文字；没有选中时返回空串。</summary>
    public string GetSelectedText() =>
        _selectionModel is null ? string.Empty : _selectionModel.GetText(_selection);

    /// <summary>取本图识别出的全部文字。</summary>
    public string GetAllText() =>
        _selectionModel is null ? string.Empty : _selectionModel.GetAllText();

    /// <summary>
    /// 右键点在文字上时，先把选区收敛到这一个词（如果点击处不在现有选区内）——
    /// 和大多数文本编辑器右键菜单前的行为一致。<paramref name="controlPoint"/> 是
    /// 相对本控件的 DIP 坐标（来自 ContextRequestedEventArgs.TryGetPosition）。
    /// </summary>
    public void SelectWordAtControlPointIfOutsideSelection(Point controlPoint)
    {
        if (_selectionModel is null)
        {
            return;
        }

        var scale = Canvas.Dpi / 96.0;
        var pixels = new Point(controlPoint.X * scale, controlPoint.Y * scale);

        var wordIndex = HitTestWord(pixels);
        if (wordIndex is null)
        {
            return;
        }

        if (!_selection.IsEmpty && wordIndex.Value >= _selection.Start && wordIndex.Value < _selection.End)
        {
            return; // 已经在当前选区里，保留原有的多词选择
        }

        _selection = _selectionModel.SelectWordAt(wordIndex.Value);
        Canvas.Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearRegionSelection()
    {
        if (_regionSelection is null)
        {
            return;
        }

        _regionSelection = null;
        Canvas.Invalidate();
    }

    // ---------- 剪贴板用的位图输出 ----------

    /// <summary>
    /// 编码出「看到的样子」的 PNG 流：没旋转就是原图，旋转过就把旋转后的像素渲染出来。
    /// Ctrl+C 复制整图时用这个，而不是直接复制磁盘上未旋转的原始像素。
    /// </summary>
    public async Task<IRandomAccessStream?> GetDisplayPngStreamAsync()
    {
        if (_sourceBitmap is null || _canvasBitmap is null)
        {
            return null;
        }

        if (_rotation == RotationQuarterTurn.None)
        {
            return await EncodeToPngAsync(_sourceBitmap);
        }

        var (width, height) = _rotation.ApplyToSize(_sourceBitmap.PixelWidth, _sourceBitmap.PixelHeight);

        using var renderTarget = new CanvasRenderTarget(Canvas.Device, (float)width, (float)height, 96f);

        using (var ds = renderTarget.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            ds.Units = CanvasUnits.Pixels;
            ds.Transform = RotationTransform.BuildBitmapToViewport(
                _sourceBitmap.PixelWidth, _sourceBitmap.PixelHeight, _rotation, 1, 0, 0);
            ds.DrawImage(
                _canvasBitmap,
                new Rect(0, 0, _sourceBitmap.PixelWidth, _sourceBitmap.PixelHeight),
                _canvasBitmap.Bounds);
        }

        var stream = new InMemoryRandomAccessStream();
        await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
        stream.Seek(0);
        return stream;
    }

    /// <summary>
    /// 编码出当前矩形选区裁剪后的 PNG 流——无法识别文字时的兜底：
    /// 框选一块区域直接复制为图片。选区坐标是原始位图坐标系（未旋转），
    /// 拖选时已经通过逆变换换算过一次，所以这里直接按矩形裁剪即可。
    /// </summary>
    public async Task<IRandomAccessStream?> GetRegionSelectionPngStreamAsync()
    {
        if (_canvasBitmap is null || _regionSelection is not { } region || region.Width < 1 || region.Height < 1)
        {
            return null;
        }

        using var renderTarget = new CanvasRenderTarget(
            Canvas.Device, (float)region.Width, (float)region.Height, 96f);

        using (var ds = renderTarget.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            ds.Units = CanvasUnits.Pixels;
            ds.DrawImage(
                _canvasBitmap,
                new Rect(0, 0, region.Width, region.Height),
                region);
        }

        var stream = new InMemoryRandomAccessStream();
        await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);
        stream.Seek(0);
        return stream;
    }

    private static async Task<IRandomAccessStream> EncodeToPngAsync(SoftwareBitmap bitmap)
    {
        var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        stream.Seek(0);
        return stream;
    }

    // ---------- Win2D 资源 ----------

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        // 设备丢失（远程桌面切换、驱动更新）时会再次进来，必须重建位图资源。
        // 这台机器接的是虚拟显示适配器，这条路径是常态而非边角情况。
        RecreateCanvasBitmap();
        UpdateViewportFromControlSize();
    }

    private void RecreateCanvasBitmap()
    {
        _canvasBitmap?.Dispose();
        _canvasBitmap = null;

        if (_sourceBitmap is null || !Canvas.ReadyToDraw)
        {
            return;
        }

        try
        {
            _canvasBitmap = CanvasBitmap.CreateFromSoftwareBitmap(Canvas.Device, _sourceBitmap);
        }
        catch (Exception)
        {
            // 超出 GPU 纹理上限等情况：暂不显示，由上层决定是否降采样重试
            _canvasBitmap = null;
        }
    }

    // ---------- 绘制 ----------

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;

        // 切到物理像素坐标系：看图软件的「100%」必须是 1 图像像素对 1 屏幕像素，
        // 用默认的 DIP 坐标会在高 DPI 屏上把图放大而显得发虚。
        ds.Units = CanvasUnits.Pixels;

        // 首次上屏和 DPI/布局变化时，以已经就绪的实际绘图区域补齐视口。
        // 不能依赖图片解码时控件已完成布局，否则大图会停在 100% 并被裁掉。
        UpdateViewportFromControlSize();

        // 自愈重试：GPU 侧位图资源上次没建成（设备当时没就绪、或设备丢失后还没恢复），
        // 只要还有源图像就每次重绘都再试一次，不用死等某个事件来救它。
        // 代价是失败时每帧都多一次 CreateFromSoftwareBitmap 调用，但失败之外这个分支不会进。
        if (_canvasBitmap is null && _sourceBitmap is not null)
        {
            RecreateCanvasBitmap();
        }

        if (_canvasBitmap is null || _sourceBitmap is null)
        {
            return;
        }

        // 位图坐标 → 视口坐标的完整变换（含旋转）。旋转只在这一处处理，
        // ZoomPanController 全程只知道「旋转后的逻辑尺寸」，缩放平移逻辑完全不用改。
        var transform = GetBitmapToViewportTransform();

        ds.Transform = transform;
        var bitmapRect = new Rect(0, 0, _sourceBitmap.PixelWidth, _sourceBitmap.PixelHeight);
        ds.DrawImage(_canvasBitmap, bitmapRect, _canvasBitmap.Bounds, 1.0f, MapInterpolation(_zoom.Quality));
        ds.Transform = Matrix3x2.Identity;

        DrawSelectionHighlight(ds, transform);
        DrawRegionSelection(ds, transform);
    }

    /// <summary>
    /// 高亮描边要保持 1 个物理像素粗细、不随缩放变化，所以不复用绘制位图时的 Transform——
    /// 而是把矩形的两个角点转换到视口坐标后，在恒等变换下画。
    /// 旋转只有 90° 的整数倍，转换后仍是一个轴对齐矩形，min/max 即可还原。
    /// </summary>
    private void DrawSelectionHighlight(CanvasDrawingSession ds, Matrix3x2 bitmapToViewport)
    {
        if (_selectionModel is null || _selection.IsEmpty)
        {
            return;
        }

        foreach (var rect in _selectionModel.GetHighlightRects(_selection))
        {
            var viewportRect = TransformRectToViewport(rect.Left, rect.Top, rect.Right, rect.Bottom, bitmapToViewport);

            var radius = (float)Math.Min(3 * Canvas.Dpi / 96.0, viewportRect.Height / 2);
            ds.FillRoundedRectangle(viewportRect, radius, radius, _selectionFill);
            ds.DrawRoundedRectangle(viewportRect, radius, radius, _selectionBorder, 1f);
        }
    }

    private void DrawRegionSelection(CanvasDrawingSession ds, Matrix3x2 bitmapToViewport)
    {
        if (_regionSelection is not { } region)
        {
            return;
        }

        var viewportRect = TransformRectToViewport(
            region.Left, region.Top, region.Right, region.Bottom, bitmapToViewport);

        ds.FillRectangle(viewportRect, RegionSelectionFill);
        ds.DrawRectangle(viewportRect, RegionSelectionBorder, 1.5f);
    }

    private static Rect TransformRectToViewport(
        double left, double top, double right, double bottom, Matrix3x2 transform)
    {
        var a = Vector2.Transform(new Vector2((float)left, (float)top), transform);
        var b = Vector2.Transform(new Vector2((float)right, (float)bottom), transform);

        return new Rect(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }

    private Matrix3x2 GetBitmapToViewportTransform() =>
        RotationTransform.BuildBitmapToViewport(
            _sourceBitmap!.PixelWidth, _sourceBitmap.PixelHeight,
            _rotation, _zoom.Scale, _zoom.OffsetX, _zoom.OffsetY);

    /// <summary>视口物理像素坐标 → 原始位图像素坐标（未旋转）。命中测试、区域选择都靠它。</summary>
    private bool TryGetBitmapPoint(Point viewportPixels, out Point bitmapPoint)
    {
        bitmapPoint = default;

        if (_sourceBitmap is null)
        {
            return false;
        }

        if (!RotationTransform.TryGetInverse(GetBitmapToViewportTransform(), out var inverse))
        {
            return false;
        }

        var p = Vector2.Transform(new Vector2((float)viewportPixels.X, (float)viewportPixels.Y), inverse);
        bitmapPoint = new Point(p.X, p.Y);
        return true;
    }

    private static CanvasImageInterpolation MapInterpolation(ScalingQuality quality) => quality switch
    {
        ScalingQuality.HighQualityCubic => CanvasImageInterpolation.HighQualityCubic,
        ScalingQuality.NearestNeighbor => CanvasImageInterpolation.NearestNeighbor,
        _ => CanvasImageInterpolation.Linear,
    };

    // ---------- 视口 ----------

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportFromControlSize();
        Canvas.Invalidate();
    }

    private void UpdateViewportFromControlSize()
    {
        // ZoomPanController 全程按物理像素运算，与绘制坐标系保持一致
        var scale = Canvas.Dpi / 96.0;
        var previousScale = _zoom.Scale;
        _zoom.SetViewport(Canvas.ActualWidth * scale, Canvas.ActualHeight * scale);
        if (Math.Abs(_zoom.Scale - previousScale) > double.Epsilon)
        {
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>把指针位置换算成绘制所用的物理像素坐标。</summary>
    private Point GetPointerPixels(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(this).Position;
        var scale = Canvas.Dpi / 96.0;
        return new Point(position.X * scale, position.Y * scale);
    }

    // ---------- 输入 ----------

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Pointer);

        if (!HasImage)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsRightButtonPressed)
        {
            // 右键交给框架的 ContextRequested 处理右键菜单，这里不抢——
            // 否则会被误当成平移/选字的起点，把右键菜单事件吞掉
            return;
        }

        var pixels = GetPointerPixels(e);
        _lastPointerPixels = pixels;

        var isMiddleButton = point.Properties.IsMiddleButtonPressed;
        var shiftHeld = IsModifierKeyDown(VirtualKey.Shift);
        var spaceHeld = IsModifierKeyDown(VirtualKey.Space);

        if (!isMiddleButton && shiftHeld && TryGetBitmapPoint(pixels, out var regionAnchor))
        {
            // Shift+拖拽：框选一块区域直接复制为图片——识别不准或没有文字时的兜底，
            // 不依赖「是否点在文字上」，随时可用
            _isRegionSelecting = true;
            _isPanning = false;
            _isSelectingText = false;
            _regionAnchorBitmapPoint = regionAnchor;
            _regionSelection = new Rect(regionAnchor, new Size(0, 0));

            if (!_selection.IsEmpty)
            {
                ClearSelection();
            }

            Canvas.Invalidate();
        }
        else
        {
            var wordIndex = HitTestWord(pixels);

            // 中键、按住空格、或没点在文字上 → 拖动图片；点在文字上 → 选字
            if (isMiddleButton || spaceHeld || wordIndex is null)
            {
                _isPanning = _zoom.CanPan;
                _isSelectingText = false;
                _isRegionSelecting = false;

                if (!_selection.IsEmpty)
                {
                    ClearSelection();
                }

                ClearRegionSelection();
            }
            else
            {
                _isSelectingText = true;
                _isPanning = false;
                _isRegionSelecting = false;

                if (TryGetBitmapPoint(pixels, out var anchor))
                {
                    _selectionAnchorCaret = _selectionModel!.CaretFromPoint(anchor.X, anchor.Y);
                }

                _selection = SelectionRange.Empty;
                _regionSelection = null;
                Canvas.Invalidate();
            }
        }

        if (_isPanning || _isSelectingText || _isRegionSelecting)
        {
            _capturedPointerId = e.Pointer.PointerId;
            CapturePointer(e.Pointer);
        }

        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!HasImage)
        {
            return;
        }

        var pixels = GetPointerPixels(e);

        if (_isRegionSelecting)
        {
            if (TryGetBitmapPoint(pixels, out var current))
            {
                var left = Math.Min(_regionAnchorBitmapPoint.X, current.X);
                var top = Math.Min(_regionAnchorBitmapPoint.Y, current.Y);
                var width = Math.Abs(current.X - _regionAnchorBitmapPoint.X);
                var height = Math.Abs(current.Y - _regionAnchorBitmapPoint.Y);

                _regionSelection = new Rect(left, top, width, height);
                Canvas.Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (_isSelectingText && _selectionModel is not null)
        {
            if (TryGetBitmapPoint(pixels, out var focus))
            {
                var focusCaret = _selectionModel.CaretFromPoint(focus.X, focus.Y);

                _selection = _selectionModel.CreateRange(_selectionAnchorCaret, focusCaret);
                Canvas.Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (_isPanning)
        {
            _zoom.Pan(pixels.X - _lastPointerPixels.X, pixels.Y - _lastPointerPixels.Y);
            Canvas.Invalidate();
        }
        else
        {
            UpdateCursor(pixels);
        }

        _lastPointerPixels = pixels;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        // Shift+点击（几乎没拖动）留下的是一个退化的空矩形，直接当作「没有选区」处理，
        // 不然状态栏会显示一块 0x0 的幽灵选区
        if (_isRegionSelecting && _regionSelection is { Width: < 1 } or { Height: < 1 })
        {
            _regionSelection = null;
            Canvas.Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        EndPointerInteraction(e.Pointer);
        UpdateCursor(GetPointerPixels(e));
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        EndPointerInteraction(e.Pointer);

    private void EndPointerInteraction(Pointer pointer)
    {
        if (_capturedPointerId == pointer.PointerId)
        {
            ReleasePointerCapture(pointer);
            _capturedPointerId = 0;
        }

        _isPanning = false;
        _isSelectingText = false;
        _isRegionSelecting = false;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!HasImage)
        {
            return;
        }

        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        var pixels = GetPointerPixels(e);
        var factor = delta > 0 ? 1.15 : 1 / 1.15;

        UpdateViewportFromControlSize();

        // 以鼠标位置为锚点，而不是视口中心——否则鼠标指着的内容会在缩放中跑掉
        _zoom.ZoomAt(factor, pixels.X, pixels.Y);

        InvalidateAfterZoom();
        UpdateCursor(pixels);

        e.Handled = true;
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!HasImage)
        {
            return;
        }

        var position = e.GetPosition(this);
        var scale = Canvas.Dpi / 96.0;
        var pixels = new Point(position.X * scale, position.Y * scale);

        ClearRegionSelection();

        // 双击文字选中该词，双击空白处在「适应窗口」与「100%」之间切换
        var wordIndex = HitTestWord(pixels);

        if (wordIndex is not null && _selectionModel is not null)
        {
            _selection = _selectionModel.SelectWordAt(wordIndex.Value);
            Canvas.Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (Math.Abs(_zoom.Scale - 1.0) < 0.01)
        {
            FitToWindow();
        }
        else
        {
            ZoomToActualSize();
        }

        e.Handled = true;
    }

    private int? HitTestWord(Point pixels)
    {
        if (_selectionModel is null || _selectionModel.IsEmpty)
        {
            return null;
        }

        if (!TryGetBitmapPoint(pixels, out var bitmapPoint))
        {
            return null;
        }

        return _selectionModel.WordIndexFromPoint(bitmapPoint.X, bitmapPoint.Y);
    }

    private void UpdateCursor(Point pixels)
    {
        var cursorShape = InputSystemCursorShape.Arrow;

        if (HasImage)
        {
            if (IsModifierKeyDown(VirtualKey.Shift))
            {
                cursorShape = InputSystemCursorShape.Cross;
            }
            else if (HitTestWord(pixels) is not null)
            {
                cursorShape = InputSystemCursorShape.IBeam;
            }
            else if (_zoom.CanPan)
            {
                cursorShape = InputSystemCursorShape.SizeAll;
            }
        }

        ProtectedCursor = InputSystemCursor.Create(cursorShape);
    }

    private static bool IsModifierKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    private void InvalidateAfterZoom()
    {
        Canvas.Invalidate();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }
}

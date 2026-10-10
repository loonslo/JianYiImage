using JianYiImage.App.Services;
using JianYiImage.Core.Decoding;
using JianYiImage.Core.Imaging;
using JianYiImage.Core.Navigation;
using JianYiImage.Core.Ocr;
using JianYiImage.Core.Rendering;
using JianYiImage.Core.SystemIntegration;
using Microsoft.UI;
using Windows.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
// 起别名而不是 `using Microsoft.UI.Xaml.Controls;`——那个命名空间里也有一个叫 Canvas 的
// 布局面板类型，会跟本类里 x:Name="Canvas"（ImageCanvas 控件字段）撞名。
using WinUIControls = Microsoft.UI.Xaml.Controls;
using WinUIPrimitives = Microsoft.UI.Xaml.Controls.Primitives;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace JianYiImage.App;

public sealed partial class MainWindow : Window
{
    /// <summary>
    /// 图片上屏后等这么久再启动 OCR。
    /// 连续翻页时每次翻页都会重置它，从而只识别用户真正停下来看的那张。
    /// </summary>
    private static readonly TimeSpan OcrDebounceDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>预读当前位置前后各几张。</summary>
    private const int PrefetchRadius = 2;

    private readonly ImageLoader _loader;
    private readonly OcrService _ocr;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly DispatcherTimer _ocrDebounceTimer;

    private FolderPlaylist? _playlist;
    private DecodedImage? _currentImage;
    private string? _currentPath;

    /// <summary>
    /// 状态栏用的尺寸/格式信息，静态图和动图共用同一个字段——
    /// UpdateStatusBar 不需要关心当前到底是走的哪条加载路径。
    /// </summary>
    private ImageMetadata? _currentDisplayMetadata;

    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _ocrCts;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTitleBar();
        AppTitleBar.SizeChanged += (_, _) => UpdateTitleFileNameWidth();
        TitlePosition.SizeChanged += (_, _) => UpdateTitleFileNameWidth();

        Title = FileAssociationRegistration.ApplicationName;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Jianyi.ico"));

        var decoder = new WicImageDecoder();
        _loader = new ImageLoader(decoder);
        _settings = _settingsStore.Load();
        _ocr = new OcrService(CreateOcrEngine(_settings.OcrLanguage), decoder);
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        _ocrDebounceTimer = new DispatcherTimer { Interval = OcrDebounceDelay };
        _ocrDebounceTimer.Tick += OnOcrDebounceTick;

        Canvas.SelectionChanged += (_, _) => UpdateStatusBar();
        Canvas.ZoomChanged += (_, _) => UpdateZoomText();
        Canvas.ContextRequested += OnCanvasContextRequested;

        Closed += OnClosed;

        UpdateStatusBar();
    }

    /// <summary>
    /// 处理资源管理器「用简壹图片打开」传进来的文件。
    /// </summary>
    public async Task OpenFromCommandLineAsync(string[] args)
    {
        var path = args.FirstOrDefault(a =>
            !a.StartsWith('-') && File.Exists(a) && ImageFormatRegistry.IsSupported(a));

        if (path is not null)
        {
            await OpenAsync(path);
        }
    }

    private bool _customTitleBar;

    private void InitializeTitleBar()
    {
        _customTitleBar = AppWindowTitleBar.IsCustomizationSupported();
        if (!_customTitleBar)
        {
            AppTitleBar.Visibility = Visibility.Collapsed;
            return;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var resources = Application.Current.Resources;
        Color ThemeColor(string key) => (Color)resources[key];
        var titleBar = AppWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = ThemeColor("AppTextPrimaryColor");
        titleBar.ButtonInactiveForegroundColor = ThemeColor("AppTextTertiaryColor");
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 45, 45, 45);
        titleBar.ButtonHoverForegroundColor = ThemeColor("AppTextPrimaryColor");
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 56, 56, 56);
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPresenterChange) UpdateTitleBarVisibility();
        };
    }

    private void UpdateTitleBarVisibility() =>
        AppTitleBar.Visibility = _customTitleBar && AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen
            ? Visibility.Visible : Visibility.Collapsed;

    // 预留右侧 144、品牌标与边距 29、两处列间距 16，窄窗口中不让文件名覆盖标题按钮。
    private void UpdateTitleFileNameWidth() =>
        TitleFileName.MaxWidth = Math.Max(0, Math.Min(600, AppTitleBar.ActualWidth - 189 - TitlePosition.ActualWidth));

    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTextPanelLayout();

    private void UpdateTextPanelLayout()
    {
        // 窄窗口保留完整图片宽度，文字停靠底部；宽窗口沿用效果图的右侧面板。
        var bottomDock = ContentGrid.ActualWidth < 800;
        WinUIControls.Grid.SetColumn(TextExtractionPanel, bottomDock ? 0 : 1);
        WinUIControls.Grid.SetRow(TextExtractionPanel, bottomDock ? 1 : 0);
        TextExtractionPanel.Width = bottomDock ? double.NaN : 320;
        TextExtractionPanel.Height = bottomDock ? Math.Min(280, ContentGrid.ActualHeight * 0.48) : double.NaN;
        TextExtractionPanel.BorderThickness = bottomDock ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
    }

    private void OnImageViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wrapHints = e.NewSize.Width < 600;
        for (var i = 0; i < ShortcutHints.Children.Count; i++)
        {
            var hint = (FrameworkElement)ShortcutHints.Children[i];
            WinUIControls.Grid.SetRow(hint, wrapHints ? i / 2 : 0);
            WinUIControls.Grid.SetColumn(hint, wrapHints ? i % 2 : i);
        }
        UpdateNavigationButtons();
    }

    // ---------- 打开与翻页 ----------

    private async Task OpenAsync(string path)
    {
        CancelPendingWork();
        ResetTextExtraction();
        try
        {
            _playlist = await FolderPlaylist.CreateAsync(path, CancellationToken.None);
            await ShowCurrentAsync();
        }
        catch (Exception ex)
        {
            // 启动参数、网络盘或目录权限异常不能让整个窗口跟着退出。
            CancelPendingWork();
            ResetTextExtraction();
            _playlist = null;
            _currentPath = null;
            _currentImage = null;
            _currentDisplayMetadata = null;
            Canvas.ClearImage();
            EmptyHint.Visibility = Visibility.Visible;
            UpdateStatusBar();
            FileStatusText.Text = $"打不开 {Path.GetFileName(path)}：{ex.Message}";
        }
    }

    private async Task ShowCurrentAsync()
    {
        var path = _playlist?.CurrentPath;
        if (path is null)
        {
            return;
        }

        // 翻页立刻取消上一张尚未完成的解码与识别
        CancelPendingWork();

        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        _currentPath = path;
        _currentImage = null;
        _currentDisplayMetadata = null;
        ResetTextExtraction();
        Canvas.ClearImage();
        UpdateNavigationButtons();
        FileStatusText.Text = $"正在打开 {Path.GetFileName(path)} …";

        try
        {
            if (await IsAnimatedGifAsync(path, token))
            {
                await ShowAnimatedGifAsync(path, token);
                return;
            }

            var image = await _loader.LoadAsync(path, token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            _currentImage = image;
            _currentDisplayMetadata = image.Metadata;
            EmptyHint.Visibility = Visibility.Collapsed;
            SetTextPanelVisible(false);
            ExtractedTextBox.Text = string.Empty;

            Canvas.SetImage(image);
            UpdateStatusBar();

            // 文字要等停下来才识别；翻页过程中不浪费 CPU
            _ocrDebounceTimer.Stop();
            _ocrDebounceTimer.Start();
            SetOcrStatus(OcrState.None);

            if (_playlist is not null)
            {
                _loader.Prefetch(_playlist.GetPrefetchCandidates(PrefetchRadius));
            }
        }
        catch (OperationCanceledException)
        {
            // 已经翻到别的图了，忽略
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            _currentImage = null;
            _currentDisplayMetadata = null;
            ResetTextExtraction();
            Canvas.ClearImage();
            EmptyHint.Visibility = Visibility.Visible;
            UpdateStatusBar();
            FileStatusText.Text = $"打不开 {Path.GetFileName(path)}：{ex.Message}";
        }
    }

    private async Task<bool> IsAnimatedGifAsync(string path, CancellationToken token)
    {
        if (!string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var metadata = await _loader.ReadMetadataAsync(path, token);
            return metadata.IsAnimated;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false; // 读不到元数据就按静态图处理，交给正常路径报错
        }
    }

    /// <summary>
    /// 动图走独立路径：不进 ImageLoader 的 LRU 缓存（多帧乘起来内存太大，不划算预读），
    /// 也不跑 OCR（对着一张不停变化的图做文字识别没有意义）。
    /// </summary>
    private async Task ShowAnimatedGifAsync(string path, CancellationToken token)
    {
        var animation = await GifAnimationDecoder.DecodeAsync(path, token);

        if (token.IsCancellationRequested)
        {
            animation.Dispose();
            return;
        }

        var firstFrame = animation.Frames[0].Bitmap;
        _currentDisplayMetadata = new ImageMetadata(
            (uint)firstFrame.PixelWidth,
            (uint)firstFrame.PixelHeight,
            ExifOrientation.Normal,
            (uint)animation.Frames.Count,
            "GIF (动图)",
            nameof(GifAnimationDecoder));

        EmptyHint.Visibility = Visibility.Collapsed;
        SetTextPanelVisible(false);
        ExtractedTextBox.Text = string.Empty;
        Canvas.SetAnimatedImage(animation);
        UpdateStatusBar();

        SetOcrStatus(OcrState.AnimatedUnsupported);
    }

    private async Task MoveAsync(Func<FolderPlaylist, bool> move)
    {
        if (_playlist is null || !move(_playlist))
        {
            return;
        }

        await ShowCurrentAsync();
    }

    private void CancelPendingWork()
    {
        _ocrDebounceTimer.Stop();

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        _ocrCts?.Cancel();
        _ocrCts?.Dispose();
        _ocrCts = null;
    }

    // ---------- OCR ----------

    private enum OcrState { None, Running, Ready, Empty, Unavailable, Failed, AnimatedUnsupported }

    private void SetOcrStatus(OcrState state, string? detail = null)
    {
        OcrStatusText.Text = state switch
        {
            OcrState.Running => "本地提取中…",
            OcrState.Ready => "文字已识别",
            OcrState.Empty => "未发现可提取文字",
            OcrState.Unavailable => "系统未安装本地文字识别语言包",
            OcrState.Failed => $"文字提取失败：{detail}",
            OcrState.AnimatedUnsupported => "动图不支持文字识别",
            _ => string.Empty,
        };
        OcrStatusIcon.Visibility = state == OcrState.Ready ? Visibility.Visible : Visibility.Collapsed;
        OcrReadyChip.Visibility = OcrStatusIcon.Visibility;
    }

    private void OnOcrDebounceTick(object? sender, object e)
    {
        _ocrDebounceTimer.Stop();
        _ = RunOcrAsync();
    }

    private async Task RunOcrAsync(bool showExtractionPanel = false)
    {
        if (showExtractionPanel)
        {
            SetTextPanelVisible(true);
            ExtractedTextBox.Text = "正在提取文字…";
        }

        if (_currentImage is null || _currentPath is null)
        {
            if (showExtractionPanel)
            {
                ExtractedTextBox.Text = "请先打开一张图片。";
            }

            return;
        }

        if (!_ocr.IsAvailable)
        {
            SetOcrStatus(OcrState.Unavailable);
            if (showExtractionPanel)
            {
                ExtractedTextBox.Text = "当前系统没有可用的本地文字提取语言包。\n\n安装方法：右键菜单 →「识别语言」→「如何添加更多语言…」，或重新运行安装程序勾选语言包。";
            }

            return;
        }

        _ocrCts?.Cancel();
        _ocrCts?.Dispose();
        _ocrCts = new CancellationTokenSource();

        var token = _ocrCts.Token;
        var image = _currentImage;
        var path = _currentPath;

        SetOcrStatus(OcrState.Running);

        try
        {
            var document = await Task.Run(
                () => _ocr.RecognizeAsync(path, image, token), token);

            if (token.IsCancellationRequested || !ReferenceEquals(_currentImage, image))
            {
                return;
            }

            _dispatcher.TryEnqueue(() =>
            {
                if (token.IsCancellationRequested || !ReferenceEquals(_currentImage, image)) return;
                Canvas.SetOcrDocument(document);
                SetOcrStatus(document.IsEmpty ? OcrState.Empty : OcrState.Ready);

                if (showExtractionPanel || TextExtractionPanel.Visibility == Visibility.Visible)
                {
                    ExtractedTextBox.Text = document.IsEmpty
                        ? "未提取到文字。"
                        : Canvas.GetAllText();
                }
            });
        }
        catch (OperationCanceledException)
        {
            // 翻页导致的取消，属正常
        }
        catch (Exception ex)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (token.IsCancellationRequested || !ReferenceEquals(_currentImage, image)) return;
                SetOcrStatus(OcrState.Failed, ex.Message);
                if (showExtractionPanel)
                {
                    ExtractedTextBox.Text = $"文字提取失败：{ex.Message}";
                }
            });
        }
    }

    // ---------- 剪贴板 ----------

    private async void OnOpenButtonClick(object sender, RoutedEventArgs e) => await PickFileAsync();

    private async void OnPreviousButtonClick(object sender, RoutedEventArgs e) =>
        await MoveAsync(p => p.MovePrevious());

    private async void OnNextButtonClick(object sender, RoutedEventArgs e) =>
        await MoveAsync(p => p.MoveNext());

    private void OnZoomOutButtonClick(object sender, RoutedEventArgs e) => Canvas.ZoomOut();

    private void OnActualSizeButtonClick(object sender, RoutedEventArgs e) => Canvas.ZoomToActualSize();

    private void OnZoomInButtonClick(object sender, RoutedEventArgs e) => Canvas.ZoomIn();

    private void OnFitButtonClick(object sender, RoutedEventArgs e) => Canvas.FitToWindow();

    private void OnRotateCounterClockwiseButtonClick(object sender, RoutedEventArgs e) =>
        Canvas.RotateCounterClockwise();

    private void OnRotateClockwiseButtonClick(object sender, RoutedEventArgs e) => Canvas.RotateClockwise();

    private void SetTextPanelVisible(bool visible)
    {
        UpdateTextPanelLayout();
        TextExtractionPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ExtractTextButton.Style = (Style)Application.Current.Resources[visible ? "ToolbarToggleOnStyle" : "ToolbarButtonStyle"];
    }

    private void ResetTextExtraction()
    {
        SetTextPanelVisible(false);
        ExtractedTextBox.Text = string.Empty;
        SetOcrStatus(OcrState.None);
    }

    private async void OnExtractTextButtonClick(object sender, RoutedEventArgs e)
    {
        if (TextExtractionPanel.Visibility == Visibility.Visible)
        {
            SetTextPanelVisible(false);
            Canvas.Focus(FocusState.Programmatic);
            return;
        }
        await RunOcrAsync(showExtractionPanel: true);
    }

    private void OnCloseTextExtractionClick(object sender, RoutedEventArgs e)
    {
        SetTextPanelVisible(false);
        // 面板关闭不影响已识别的文字，也不取消正在后台运行的 OCR。
        Canvas.Focus(FocusState.Programmatic);
    }

    private void OnCopyExtractedTextButtonClick(object sender, RoutedEventArgs e)
    {
        var text = Canvas.GetAllText();
        if (string.IsNullOrWhiteSpace(text))
        {
            FileStatusText.Text = "还没有可复制的图片文字";
            return;
        }

        ClipboardService.SetText(text);
        SetTextPanelVisible(true);
        ExtractedTextBox.Text = text;
        FileStatusText.Text = $"已复制 {text.Length} 个字符";
    }

    /// <summary>
    /// Ctrl+C 与「复制…」菜单项共用的优先级：选中文字 &gt; 框选区域 &gt; 整张图。
    /// 三种情况互斥（ImageCanvas 保证同一时刻只有一种选择在生效），所以按顺序判断即可。
    /// </summary>
    private async Task CopyAsync()
    {
        if (Canvas.HasTextSelection)
        {
            var text = Canvas.GetSelectedText();
            ClipboardService.SetText(text);
            FileStatusText.Text = $"已复制 {text.Length} 个字符";
            return;
        }

        if (Canvas.HasRegionSelection)
        {
            await CopyStreamAsync(Canvas.GetRegionSelectionPngStreamAsync(), attachSourceFile: false);
            return;
        }

        if (_currentImage is null)
        {
            return;
        }

        // 只有未旋转时，剪贴板里的像素才和磁盘上的原始文件完全一致，才附带原文件
        await CopyStreamAsync(Canvas.GetDisplayPngStreamAsync(), attachSourceFile: Canvas.DisplayRotation == RotationQuarterTurn.None);
    }

    private async Task CopyStreamAsync(Task<IRandomAccessStream?> streamTask, bool attachSourceFile)
    {
        try
        {
            using var stream = await streamTask;
            if (stream is null)
            {
                return;
            }

            await ClipboardService.SetImageAsync(stream, _currentPath, attachSourceFile);
            FileStatusText.Text = "已复制图片";
        }
        catch (Exception ex)
        {
            FileStatusText.Text = $"复制失败：{ex.Message}";
        }
    }

    // ---------- 旋转 / 全屏 ----------

    private void ToggleFullScreen()
    {
        var isFullScreen = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        AppWindow.SetPresenter(isFullScreen ? AppWindowPresenterKind.Default : AppWindowPresenterKind.FullScreen);
        UpdateTitleBarVisibility();
    }

    // ---------- 文件操作：删除、定位、关联 ----------

    private void RevealCurrentInExplorer()
    {
        if (_currentPath is not null)
        {
            FileSystemService.RevealInExplorer(_currentPath);
        }
    }

    /// <summary>删除到回收站（可恢复），删除后自动翻到下一张。</summary>
    private async Task DeleteCurrentToRecycleBinAsync()
    {
        if (_currentPath is null)
        {
            return;
        }

        var path = _currentPath;
        CancelPendingWork();

        try
        {
            await FileSystemService.DeleteToRecycleBinAsync(path);
        }
        catch (Exception ex)
        {
            FileStatusText.Text = $"删除失败：{ex.Message}";
            return;
        }

        _loader.InvalidateCache(path);
        _playlist?.Remove(path);

        if (_playlist is { IsEmpty: false })
        {
            await ShowCurrentAsync();
        }
        else
        {
            ResetTextExtraction();
            _currentImage = null;
            _currentPath = null;
            _currentDisplayMetadata = null;
            Canvas.ClearImage();
            EmptyHint.Visibility = Visibility.Visible;
            UpdateStatusBar();
        }
    }

    private void RegisterFileAssociations()
    {
        FileAssociationService.RegisterAll();
        FileStatusText.Text = $"已添加右键「用{FileAssociationRegistration.ApplicationName}打开」和系统「打开方式」";
    }

    private void UnregisterFileAssociations()
    {
        FileAssociationService.UnregisterAll();
        FileStatusText.Text = $"已移除{FileAssociationRegistration.ApplicationName}文件关联";
    }

    private async Task SetDefaultViewerAsync()
    {
        FileAssociationService.RegisterAll();
        var opened = await FileAssociationService.OpenDefaultAppsSettingsAsync();
        FileStatusText.Text = opened
            ? $"请在 Windows 默认应用设置中选择{FileAssociationRegistration.ApplicationName}，或右键图片→打开方式→始终使用"
            : $"请打开 Windows 设置→应用→默认应用，选择{FileAssociationRegistration.ApplicationName}";
    }

    // ---------- 右键菜单 ----------

    private void OnCanvasContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        if (!Canvas.HasImage)
        {
            return;
        }

        var hasPosition = e.TryGetPosition(Canvas, out var position);

        if (hasPosition)
        {
            // 右键点在还没被选中的文字上时，先把选区收敛到那一个词，
            // 和大多数文本编辑器右键菜单前的行为一致
            Canvas.SelectWordAtControlPointIfOutsideSelection(position);
        }

        var menu = BuildContextMenu();

        if (hasPosition)
        {
            menu.ShowAt(Canvas, new WinUIPrimitives.FlyoutShowOptions { Position = position });
        }
        else
        {
            menu.ShowAt(Canvas);
        }

        e.Handled = true;
    }

    private WinUIControls.MenuFlyout BuildContextMenu()
    {
        var menu = new WinUIControls.MenuFlyout();
        var items = menu.Items;

        if (Canvas.HasTextSelection)
        {
            items.Add(BuildMenuItem("复制文字", CopyAsync));
            items.Add(BuildMenuItem("用 Bing 搜索", () => WebLookupService.SearchAsync(Canvas.GetSelectedText())));
            items.Add(BuildMenuItem("翻译所选文字", () => WebLookupService.TranslateAsync(Canvas.GetSelectedText())));
            items.Add(new WinUIControls.MenuFlyoutSeparator());
        }
        else if (Canvas.HasRegionSelection)
        {
            items.Add(BuildMenuItem("复制选区为图片", CopyAsync));
            items.Add(BuildMenuItem("取消选区", () => Canvas.ClearRegionSelection()));
            items.Add(new WinUIControls.MenuFlyoutSeparator());
        }
        else
        {
            items.Add(BuildMenuItem("复制图片", CopyAsync));

            if (Canvas.HasRecognizedText)
            {
                items.Add(BuildMenuItem("全选文字", () => Canvas.SelectAllText()));
            }

            items.Add(new WinUIControls.MenuFlyoutSeparator());
        }

        items.Add(BuildMenuItem("顺时针旋转 90°", () => Canvas.RotateClockwise()));
        items.Add(BuildMenuItem("逆时针旋转 90°", () => Canvas.RotateCounterClockwise()));
        items.Add(BuildMenuItem("适应窗口", () => Canvas.FitToWindow()));
        items.Add(BuildMenuItem("实际大小 (100%)", () => Canvas.ZoomToActualSize()));
        items.Add(new WinUIControls.MenuFlyoutSeparator());

        items.Add(BuildMenuItem("在文件资源管理器中显示", RevealCurrentInExplorer));
        items.Add(BuildMenuItem("删除到回收站", DeleteCurrentToRecycleBinAsync));
        items.Add(new WinUIControls.MenuFlyoutSeparator());

        items.Add(BuildOcrLanguageMenu());
        items.Add(new WinUIControls.MenuFlyoutSeparator());

        items.Add(BuildMenuItem("设为默认图片浏览器…", SetDefaultViewerAsync));
        items.Add(BuildMenuItem("添加右键打开 / 打开方式", RegisterFileAssociations));
        if (FileAssociationService.IsRegistered())
        {
            items.Add(BuildMenuItem($"移除{FileAssociationRegistration.ApplicationName}文件关联", UnregisterFileAssociations));
        }

        return menu;
    }

    private WinUIControls.MenuFlyoutItem BuildMenuItem(string text, Action action)
    {
        var item = new WinUIControls.MenuFlyoutItem { Text = text };
        item.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                FileStatusText.Text = $"操作失败：{ex.Message}";
            }
        };
        return item;
    }

    private WinUIControls.MenuFlyoutItem BuildMenuItem(string text, Func<Task> asyncAction)
    {
        var item = new WinUIControls.MenuFlyoutItem { Text = text };
        item.Click += async (_, _) =>
        {
            try
            {
                await asyncAction();
            }
            catch (Exception ex)
            {
                FileStatusText.Text = $"操作失败：{ex.Message}";
            }
        };
        return item;
    }

    // ---------- 输入 ----------

    private async void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSourceHelper.IsCtrlDown();
        var shift = InputKeyboardSourceHelper.IsShiftDown();

        // 只读文本框也有选字/复制/光标操作；不让冒泡的 Delete、R、方向键作用于图片。
        if (IsTextInput(FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject)
            || IsTextInput(e.OriginalSource as DependencyObject))
        {
            if (!(ctrl && e.Key == VirtualKey.O) && e.Key != VirtualKey.F11) return;
        }

        switch (e.Key)
        {
            case VirtualKey.Left:
                await MoveAsync(p => p.MovePrevious());
                e.Handled = true;
                break;

            case VirtualKey.Right:
                await MoveAsync(p => p.MoveNext());
                e.Handled = true;
                break;

            case VirtualKey.Home:
                await MoveAsync(p => p.MoveFirst());
                e.Handled = true;
                break;

            case VirtualKey.End:
                await MoveAsync(p => p.MoveLast());
                e.Handled = true;
                break;

            case VirtualKey.Escape:
                Canvas.ClearSelection();
                Canvas.ClearRegionSelection();
                e.Handled = true;
                break;

            case VirtualKey.C when ctrl:
                await CopyAsync();
                e.Handled = true;
                break;

            case VirtualKey.A when ctrl:
                Canvas.SelectAllText();
                e.Handled = true;
                break;

            case VirtualKey.O when ctrl:
                await PickFileAsync();
                e.Handled = true;
                break;

            case VirtualKey.Number0 when ctrl:
                Canvas.ZoomToActualSize();
                e.Handled = true;
                break;

            case VirtualKey.Number1 when ctrl:
                Canvas.FitToWindow();
                e.Handled = true;
                break;

            case (VirtualKey)187 when ctrl: // Ctrl + =
            case VirtualKey.Add when ctrl:
                Canvas.ZoomIn();
                e.Handled = true;
                break;

            case (VirtualKey)189 when ctrl: // Ctrl + -
            case VirtualKey.Subtract when ctrl:
                Canvas.ZoomOut();
                e.Handled = true;
                break;

            case VirtualKey.R when shift:
                Canvas.RotateCounterClockwise();
                e.Handled = true;
                break;

            case VirtualKey.R:
                Canvas.RotateClockwise();
                e.Handled = true;
                break;

            case VirtualKey.F11:
                ToggleFullScreen();
                e.Handled = true;
                break;

            case VirtualKey.Delete:
                await DeleteCurrentToRecycleBinAsync();
                e.Handled = true;
                break;
        }
    }

    private static bool IsTextInput(DependencyObject? element)
    {
        for (; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is WinUIControls.TextBox or WinUIControls.RichEditBox or WinUIControls.PasswordBox)
                return true;
        }
        return false;
    }

    private async Task PickFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };

        foreach (var extension in ImageFormatRegistry.AllExtensions.OrderBy(x => x))
        {
            picker.FileTypeFilter.Add(extension);
        }

        // unpackaged 应用必须显式绑定窗口句柄，否则 Picker 无法显示
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);

        var file = await picker.PickSingleFileAsync();

        if (file is not null)
        {
            await OpenAsync(file.Path);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "打开图片";
        e.DragUIOverride.IsGlyphVisible = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.OfType<StorageFile>()
            .FirstOrDefault(f => ImageFormatRegistry.IsSupported(f.Path));

        if (file is not null)
        {
            await OpenAsync(file.Path);
        }
    }

    // ---------- 状态栏 ----------

    private void UpdateStatusBar()
    {
        TitleFileName.Text = _currentPath is null ? FileAssociationRegistration.ApplicationName : Path.GetFileName(_currentPath);
        TitlePosition.Text = _playlist is { Count: > 1 } ? $"{_playlist.CurrentIndex + 1} / {_playlist.Count}" : string.Empty;
        Title = _currentPath is null ? FileAssociationRegistration.ApplicationName
            : $"{TitleFileName.Text}{(TitlePosition.Text.Length > 0 ? $"  ({TitlePosition.Text})" : string.Empty)} — {FileAssociationRegistration.ApplicationName}";
        UpdateNavigationButtons();
        UpdateZoomText();
        UpdateToolbarState();

        if (_currentPath is null || _currentDisplayMetadata is not { } metadata)
        {
            FileStatusText.Text = "就绪";
            ImageInfoText.Text = string.Empty;
            return;
        }

        FileStatusText.Text = string.Empty;

        var frameInfo = metadata.IsAnimated ? $"  {metadata.FrameCount} 帧" : string.Empty;
        ImageInfoText.Text = $"{metadata.DisplayWidth} × {metadata.DisplayHeight}  {ImageFormatLabel.From(metadata.FormatName)}{frameInfo}";

    }

    private void UpdateToolbarState()
    {
        var hasImage = Canvas.HasImage;
        ZoomOutButton.IsEnabled = ZoomInButton.IsEnabled = ActualSizeButton.IsEnabled = FitButton.IsEnabled = hasImage;
        RotateCounterClockwiseButton.IsEnabled = RotateClockwiseButton.IsEnabled = hasImage;
        ExtractTextButton.IsEnabled = CopyTextButton.IsEnabled = hasImage && !Canvas.IsAnimating;
    }

    private void UpdateNavigationButtons()
    {
        PreviousButton.IsEnabled = _playlist is { IsEmpty: false, IsFirst: false };
        NextButton.IsEnabled = _playlist is { IsEmpty: false, IsLast: false };
        PreviousArrowButton.IsEnabled = PreviousButton.IsEnabled;
        NextArrowButton.IsEnabled = NextButton.IsEnabled;
        var visibility = _playlist is { Count: > 1 } && ImageViewport.ActualWidth >= 160 ? Visibility.Visible : Visibility.Collapsed;
        PreviousArrowButton.Visibility = visibility;
        NextArrowButton.Visibility = visibility;
    }

    private void UpdateZoomText()
    {
        var percentage = $"{Canvas.ZoomScale * 100:0}%";
        ActualSizeButton.Content = Canvas.HasImage ? percentage : "100%";
        AutomationProperties.SetName(ActualSizeButton, Canvas.HasImage
            ? $"当前缩放 {percentage}，实际大小（100%）" : "实际大小（100%），未打开图片");
        ActualSizeButton.IsEnabled = Canvas.HasImage;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        CancelPendingWork();
        _loader.Dispose();
    }
}

/// <summary>读取修饰键状态的小工具。</summary>
internal static class InputKeyboardSourceHelper
{
    public static bool IsCtrlDown() => IsDown(VirtualKey.Control);

    public static bool IsShiftDown() => IsDown(VirtualKey.Shift);

    private static bool IsDown(VirtualKey key) =>
        (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
}

using JianYiImage.Core.Ocr;
using JianYiImage.Core.Settings;
using Microsoft.UI.Xaml;
using Windows.System;
using WinUIControls = Microsoft.UI.Xaml.Controls;

namespace JianYiImage.App;

/// <summary>
/// OCR 识别语言的选择。安装程序负责装哪些语言包，这里决定识别时用其中哪一个。
/// </summary>
public sealed partial class MainWindow
{
    private const string OcrLanguageHelpUrl = "https://github.com/loonslo/JianYiImage#ocr-语言包";

    private readonly AppSettingsStore _settingsStore = new(AppSettingsStore.DefaultPath);
    private AppSettings _settings = new();

    /// <summary>按保存的偏好创建引擎；偏好指向的语言包已不在时退回自动。</summary>
    private static IOcrEngine? CreateOcrEngine(string? saved) =>
        string.Equals(saved, TesseractOcrEngine.LanguageTag, StringComparison.OrdinalIgnoreCase) && TesseractOcrEngine.TryCreate() is { } precise
            ? precise
            : WindowsOcrEngine.TryCreate(OcrLanguageSelection.ResolveRequestedTag(saved, WindowsOcrEngine.GetAvailableLanguages()));

    private static IReadOnlyList<OcrLanguageInfo> GetOcrLanguages()
    {
        var languages = WindowsOcrEngine.GetAvailableLanguages().ToList();
        if (TesseractOcrEngine.TryCreate() is { } precise) languages.Add(precise.Language);
        return languages;
    }

    /// <summary>
    /// 「识别语言」子菜单。右键菜单和「⋯」菜单每次打开都会重建，
    /// 所以刚装上的语言包和当前选择不会过期。
    /// </summary>
    private WinUIControls.MenuFlyoutSubItem BuildOcrLanguageMenu()
    {
        var available = GetOcrLanguages();
        var effective = OcrLanguageSelection.ResolveRequestedTag(_settings.OcrLanguage, available)
            ?? OcrLanguageSelection.Auto;

        var submenu = new WinUIControls.MenuFlyoutSubItem { Text = "识别语言" };
        submenu.Items.Add(BuildOcrLanguageItem("自动（跟随系统）", OcrLanguageSelection.Auto, effective));
        foreach (var language in available)
        {
            submenu.Items.Add(BuildOcrLanguageItem(OcrLanguageCatalog.DisplayName(language), language.LanguageTag, effective));
        }

        submenu.Items.Add(new WinUIControls.MenuFlyoutSeparator());
        submenu.Items.Add(BuildMenuItem("如何添加更多语言…", async () =>
            await Launcher.LaunchUriAsync(new Uri(OcrLanguageHelpUrl))));
        return submenu;
    }

    private WinUIControls.RadioMenuFlyoutItem BuildOcrLanguageItem(string text, string tag, string effective)
    {
        var item = new WinUIControls.RadioMenuFlyoutItem
        {
            Text = text,
            GroupName = "OcrLanguage",
            IsChecked = OcrLanguageSelection.IsSelected(effective, tag),
        };
        item.Click += async (_, _) =>
        {
            try
            {
                await ChangeOcrLanguageAsync(tag);
            }
            catch (Exception ex)
            {
                FileStatusText.Text = $"操作失败：{ex.Message}";
            }
        };
        return item;
    }

    private async Task ChangeOcrLanguageAsync(string tag)
    {
        var current = OcrLanguageSelection.ResolveRequestedTag(_settings.OcrLanguage, GetOcrLanguages())
            ?? OcrLanguageSelection.Auto;
        if (OcrLanguageSelection.IsSelected(current, tag))
        {
            return;
        }

        _settings = _settings with { OcrLanguage = tag };
        string? saveWarning = null;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            saveWarning = $"，但设置没能保存（下次启动会恢复）：{ex.Message}";
        }

        _ocr.UseEngine(CreateOcrEngine(tag));

        var name = _ocr.Language is { } language ? OcrLanguageCatalog.DisplayName(language) : "不可用";
        var notice = tag == OcrLanguageSelection.Auto
            ? $"识别语言：自动（当前为 {name}）{saveWarning}"
            : $"识别语言：{name}{saveWarning}";
        FileStatusText.Text = notice;

        // 只有静态图才会有 _currentImage（动图不做 OCR）；换了语言就用新语言重新识别当前这张。
        if (_currentImage is not null)
        {
            await RunOcrAsync(showExtractionPanel: TextExtractionPanel.Visibility == Visibility.Visible);

            // 识别结果经 _dispatcher 排队写回，写回时选区被重置会清空状态栏文字；
            // 把提示排在它后面再写一遍，用户才看得到（同一个队列，先进先出）。
            _dispatcher.TryEnqueue(() => FileStatusText.Text = notice);
        }
    }
}

using Windows.System;

namespace ImageViewer.App.Services;

/// <summary>
/// 右键菜单的「搜索」「翻译」：把选中文字丢进默认浏览器。
/// 不调用任何搜索/翻译 API，就是拼一个 URL 用系统默认浏览器打开——
/// 和点一个链接是同一件事。
/// </summary>
public static class WebLookupService
{
    /// <summary>查询串过长时截断——超长文本本来也不适合塞进一个 GET 请求。</summary>
    private const int MaxQueryLength = 1500;

    public static Task SearchAsync(string text) =>
        LaunchAsync($"https://www.bing.com/search?q={Uri.EscapeDataString(Truncate(text))}");

    public static Task TranslateAsync(string text) =>
        LaunchAsync($"https://www.bing.com/translator?text={Uri.EscapeDataString(Truncate(text))}");

    private static Task LaunchAsync(string url) => Launcher.LaunchUriAsync(new Uri(url)).AsTask();

    private static string Truncate(string text) =>
        text.Length <= MaxQueryLength ? text : text[..MaxQueryLength];
}

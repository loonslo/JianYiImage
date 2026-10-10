namespace JianYiImage.Core.Ocr;

/// <summary>
/// OCR 识别语言的用户偏好。
///
/// 只装了语言包并不会让应用用上它：未指定时 Windows 只会选"用户语言列表里第一个有 OCR 的语言"，
/// 装了英文包的中文系统仍然用中文引擎去读英文。所以应用提供一个明确的选择，保存在用户设置里。
/// </summary>
public static class OcrLanguageSelection
{
    /// <summary>不指定语言，沿用 Windows 的选择。</summary>
    public const string Auto = "auto";

    /// <summary>
    /// 把保存的偏好换算成要向引擎请求的语言标记。返回 null 表示自动。
    /// 偏好里的语言包已被卸载时也退回自动，而不是让引擎创建失败。
    /// </summary>
    public static string? ResolveRequestedTag(string? saved, IReadOnlyList<OcrLanguageInfo> available)
    {
        if (string.IsNullOrWhiteSpace(saved) || string.Equals(saved, Auto, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var language in available)
        {
            if (string.Equals(language.LanguageTag, saved, StringComparison.OrdinalIgnoreCase))
            {
                return language.LanguageTag;
            }
        }

        return null;
    }

    /// <summary>某个语言是不是当前偏好所指的（偏好为自动时只有 <see cref="Auto"/> 本身算）。</summary>
    public static bool IsSelected(string? saved, string languageTag) =>
        !string.IsNullOrWhiteSpace(saved)
        && string.Equals(saved, languageTag, StringComparison.OrdinalIgnoreCase);
}

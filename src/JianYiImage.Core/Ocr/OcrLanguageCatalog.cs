using System.Globalization;

namespace JianYiImage.Core.Ocr;

/// <summary>
/// 安装程序里可勾选的 OCR 语言包，以及应用菜单里语言的显示名。
///
/// Windows 的 OCR 语言包按"能力名"安装（Language.OCR~~~zh-CN~0.0.1.0），而引擎报告的语言标记带文字系统
/// （zh-Hans-CN）；两者写法不同的只有中文，所以每项同时记下两种标记。安装脚本里的勾选列表手写在
/// installer/JianYiImage.iss，与这张表是否一致由测试把关。
/// </summary>
public static class OcrLanguageCatalog
{
    /// <param name="CapabilityTag">用于 Add-WindowsCapability 的标记，如 zh-CN。</param>
    /// <param name="EngineTag">OcrEngine 报告的语言标记，如 zh-Hans-CN。</param>
    /// <param name="Label">界面显示名：本语言写法加中文名。</param>
    /// <param name="Common">安装程序里放在"常用语言"一组。</param>
    public sealed record Entry(string CapabilityTag, string EngineTag, string Label, bool Common);

    public static IReadOnlyList<Entry> Entries { get; } =
    [
        new("zh-CN", "zh-Hans-CN", "简体中文", true),
        new("en-US", "en-US", "English（美国英语）", true),
        new("zh-TW", "zh-Hant-TW", "繁體中文（台灣）", true),
        new("ja-JP", "ja-JP", "日本語（日语）", true),
        new("ko-KR", "ko-KR", "한국어（韩语）", true),

        new("en-GB", "en-GB", "English（英国英语）", false),
        new("zh-HK", "zh-Hant-HK", "繁體中文（香港）", false),
        new("fr-FR", "fr-FR", "Français（法语）", false),
        new("de-DE", "de-DE", "Deutsch（德语）", false),
        new("es-ES", "es-ES", "Español（西班牙语·西班牙）", false),
        new("es-MX", "es-MX", "Español（西班牙语·墨西哥）", false),
        new("it-IT", "it-IT", "Italiano（意大利语）", false),
        new("pt-BR", "pt-BR", "Português（葡萄牙语·巴西）", false),
        new("pt-PT", "pt-PT", "Português（葡萄牙语·葡萄牙）", false),
        new("ru-RU", "ru-RU", "Русский（俄语）", false),
        new("tr-TR", "tr-TR", "Türkçe（土耳其语）", false),
        new("nl-NL", "nl-NL", "Nederlands（荷兰语）", false),
        new("pl-PL", "pl-PL", "Polski（波兰语）", false),
        new("sv-SE", "sv-SE", "Svenska（瑞典语）", false),
        new("da-DK", "da-DK", "Dansk（丹麦语）", false),
        new("nb-NO", "nb-NO", "Norsk bokmål（挪威语）", false),
        new("fi-FI", "fi-FI", "Suomi（芬兰语）", false),
        new("cs-CZ", "cs-CZ", "Čeština（捷克语）", false),
        new("hu-HU", "hu-HU", "Magyar（匈牙利语）", false),
        new("el-GR", "el-GR", "Ελληνικά（希腊语）", false),
        new("bg-BG", "bg-BG", "Български（保加利亚语）", false),
        new("ar-SA", "ar-SA", "العربية（阿拉伯语）", false),
    ];

    /// <summary>Windows 功能按需（Features on Demand）里 OCR 语言包的能力名。</summary>
    public static string CapabilityName(string capabilityTag) => $"Language.OCR~~~{capabilityTag}~0.0.1.0";

    /// <summary>
    /// 菜单里显示的语言名：目录里有就用目录的写法；没有就用系统给的本语言名，再不行用引擎给的名字或标记本身。
    /// （引擎的 DisplayName 随进程的界面语言变化，在未打包应用里常常是英文，所以不优先用。）
    /// </summary>
    public static string DisplayName(OcrLanguageInfo language)
    {
        var entry = Entries.FirstOrDefault(e =>
            string.Equals(e.EngineTag, language.LanguageTag, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
        {
            return entry.Label;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language.LanguageTag).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return string.IsNullOrWhiteSpace(language.DisplayName) ? language.LanguageTag : language.DisplayName;
        }
    }
}

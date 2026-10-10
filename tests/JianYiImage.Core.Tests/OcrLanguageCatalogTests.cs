using JianYiImage.Core.Ocr;

namespace JianYiImage.Core.Tests;

public class OcrLanguageCatalogTests
{
    [Fact]
    public void 目录里的标记没有重复()
    {
        var entries = OcrLanguageCatalog.Entries;

        Assert.Equal(entries.Count, entries.Select(e => e.CapabilityTag).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(entries.Count, entries.Select(e => e.EngineTag).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(entries.Count, entries.Select(e => e.Label).Distinct().Count());
    }

    [Fact]
    public void 常用语言在前且包含简体中文和英文()
    {
        var entries = OcrLanguageCatalog.Entries;
        var commonCount = entries.Count(e => e.Common);

        Assert.All(entries.Take(commonCount), e => Assert.True(e.Common));
        Assert.All(entries.Skip(commonCount), e => Assert.False(e.Common));
        Assert.Contains(entries.Take(commonCount), e => e.CapabilityTag == "zh-CN");
        Assert.Contains(entries.Take(commonCount), e => e.CapabilityTag == "en-US");
    }

    [Fact]
    public void 只有中文的能力标记与引擎标记写法不同()
    {
        var different = OcrLanguageCatalog.Entries
            .Where(e => !string.Equals(e.CapabilityTag, e.EngineTag, StringComparison.Ordinal))
            .ToDictionary(e => e.CapabilityTag, e => e.EngineTag);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["zh-CN"] = "zh-Hans-CN",
                ["zh-TW"] = "zh-Hant-TW",
                ["zh-HK"] = "zh-Hant-HK",
            },
            different);
    }

    [Fact]
    public void 能力名符合Windows功能按需的写法()
    {
        Assert.Equal("Language.OCR~~~en-US~0.0.1.0", OcrLanguageCatalog.CapabilityName("en-US"));
        Assert.All(
            OcrLanguageCatalog.Entries,
            e => Assert.Matches(@"^Language\.OCR~~~[A-Za-z]{2,3}(-[A-Za-z0-9]+)+~0\.0\.1\.0$", OcrLanguageCatalog.CapabilityName(e.CapabilityTag)));
    }

    [Theory]
    [InlineData("zh-Hans-CN", "简体中文")]
    [InlineData("zh-hans-cn", "简体中文")]
    [InlineData("en-US", "English（美国英语）")]
    [InlineData("ja-JP", "日本語（日语）")]
    public void 目录里有的语言用目录的显示名(string tag, string expected)
    {
        // 引擎自己给的 DisplayName 随进程界面语言变化，不能影响显示名。
        Assert.Equal(expected, OcrLanguageCatalog.DisplayName(new OcrLanguageInfo(tag, "Whatever the engine says")));
    }

    [Fact]
    public void 目录里没有的语言用系统给的本语言名()
    {
        // 瑞典语（芬兰）不在目录里，但是合法的区域标记。
        var name = OcrLanguageCatalog.DisplayName(new OcrLanguageInfo("sv-FI", "Swedish (Finland)"));

        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void 标记无法识别时退回引擎名字或标记本身()
    {
        Assert.Equal("My Display", OcrLanguageCatalog.DisplayName(new OcrLanguageInfo("not a tag!", "My Display")));
        Assert.Equal("not a tag!", OcrLanguageCatalog.DisplayName(new OcrLanguageInfo("not a tag!", " ")));
    }
}

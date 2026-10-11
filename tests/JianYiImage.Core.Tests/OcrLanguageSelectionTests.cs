using JianYiImage.Core.Ocr;

namespace JianYiImage.Core.Tests;

public class OcrLanguageSelectionTests
{
    [Fact]
    public void 精细模式可保存选择且菜单显示模式名称()
    {
        var precise = new OcrLanguageInfo(TesseractOcrEngine.LanguageTag, "英文／代码（精细识别）");
        Assert.Equal(precise.LanguageTag, OcrLanguageSelection.ResolveRequestedTag(precise.LanguageTag, [precise]));
        Assert.Equal(precise.DisplayName, OcrLanguageCatalog.DisplayName(precise));
        Assert.Null(OcrLanguageSelection.ResolveRequestedTag(precise.LanguageTag, Installed));
    }
    private static readonly OcrLanguageInfo[] Installed =
    [
        new("zh-Hans-CN", "Chinese (Simplified, China)"),
        new("en-US", "English (United States)"),
    ];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void 未指定或自动时不向引擎请求具体语言(string? saved)
    {
        Assert.Null(OcrLanguageSelection.ResolveRequestedTag(saved, Installed));
    }

    [Fact]
    public void 偏好的语言已安装时返回引擎报告的标记写法()
    {
        Assert.Equal("en-US", OcrLanguageSelection.ResolveRequestedTag("EN-us", Installed));
        Assert.Equal("zh-Hans-CN", OcrLanguageSelection.ResolveRequestedTag("zh-Hans-CN", Installed));
    }

    [Fact]
    public void 偏好的语言包已被卸载时退回自动()
    {
        Assert.Null(OcrLanguageSelection.ResolveRequestedTag("ja-JP", Installed));
    }

    [Fact]
    public void 一个语言包都没有时退回自动()
    {
        Assert.Null(OcrLanguageSelection.ResolveRequestedTag("en-US", []));
    }

    [Theory]
    [InlineData("auto", "auto", true)]
    [InlineData("en-US", "en-US", true)]
    [InlineData("en-us", "en-US", true)]
    [InlineData("en-US", "zh-Hans-CN", false)]
    [InlineData("auto", "en-US", false)]
    [InlineData("en-US", "auto", false)]
    [InlineData(null, "en-US", false)]
    [InlineData("", "auto", false)]
    public void 判断菜单项是否为当前选择(string? saved, string tag, bool expected)
    {
        Assert.Equal(expected, OcrLanguageSelection.IsSelected(saved, tag));
    }
}

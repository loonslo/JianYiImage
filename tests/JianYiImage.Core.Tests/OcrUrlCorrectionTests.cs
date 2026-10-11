using JianYiImage.Core.Ocr;

namespace JianYiImage.Core.Tests;

public class OcrUrlCorrectionTests
{
    private static TextSelectionModel Model(params WordBox[] words) =>
        new(new OcrDocument([new OcrLine(words)], 800, 100));

    [Theory]
    [InlineData("@tps://fivsevn.com/", "https://fivsevn.com/")]
    [InlineData("@tps://www.example.com/a?b=1#c", "https://www.example.com/a?b=1#c")]
    [InlineData("@TPS://EXAMPLE.COM/", "https://EXAMPLE.COM/")]
    [InlineData("@tps://localhost:8080/", "https://localhost:8080/")]
    [InlineData("@tps://[::1]/", "https://[::1]/")]
    [InlineData("https：//example.com/", "https://example.com/")]
    [InlineData("http：／／example.com/", "http://example.com/")]
    [InlineData("@tps：／／example.com/", "https://example.com/")]
    public void 误识别的网址协议被纠正(string raw, string expected)
    {
        var model = Model(new WordBox(raw, 10, 10, 200, 16));

        Assert.Equal(expected, model.GetAllText());
        Assert.Equal(expected, model.GetText(model.SelectWordAt(0)));
        Assert.Equal(expected, model.Document.Lines[0].Words[0].Text);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://example.com/")]
    [InlineData("test@example.com")]
    [InlineData("@tps")]
    [InlineData("@tps: please reply")]
    [InlineData("@tps://")]
    [InlineData("@tps:// example.com")]
    [InlineData("@tps:///example.com")]
    [InlineData("@tp://example.com")]
    [InlineData("https://example.com/@tps://other.com")]
    [InlineData("https://example.com/?q=@tps://other.com")]
    [InlineData("user@tps://example.com")]
    [InlineData("https://example.com/a：b／c")]
    [InlineData("中文：／标点")]
    public void 正常协议邮箱提及和非网址文本保持原样(string text)
    {
        Assert.Equal(text, Model(new WordBox(text, 10, 10, 300, 16)).GetAllText());
    }

    [Fact]
    public void 拆词的协议纠正后仍能按原词框选择()
    {
        WordBox[] words =
        [
            new("@", 10, 10, 12, 16),
            new("tps", 23, 10, 20, 16),
            new(":", 44, 10, 2, 16),
            new("/", 47, 10, 4, 16),
            new("/", 52, 10, 4, 16),
            new("example.com/", 57, 10, 100, 16),
        ];
        var model = Model(words.Reverse().ToArray());

        Assert.Equal("https://example.com/", model.GetAllText());
        Assert.Equal("https://", model.GetText(new SelectionRange(0, 5)));
        Assert.Equal("example.com/", model.GetText(model.SelectWordAt(5)));
        Assert.Equal(words.Length, model.WordCount);
        Assert.Equal("@", words[0].Text);
        for (var i = 0; i < words.Length; i++)
        {
            Assert.Equal(words[i] with { Text = model.Words[i].Text }, model.Words[i]);
            Assert.Equal(i, model.WordIndexFromPoint(words[i].CenterX, words[i].CenterY));
        }
        Assert.Equal(new HighlightRect(10, 10, 147, 16), Assert.Single(model.GetHighlightRects(model.SelectAll())));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com/?q=")]
    [InlineData("user")]
    public void 紧邻网址路径查询或用户名的片段不被改写(string preceding)
    {
        var model = Model(
            new WordBox(preceding, 10, 10, 100, 16),
            new WordBox("@tps://other.com", 111, 10, 100, 16));

        Assert.Equal(preceding + "@tps://other.com", model.GetAllText());
    }

    [Fact]
    public void 协议片段之间有正常词距时不合并纠错()
    {
        var model = Model(
            new WordBox("@tps", 10, 10, 30, 16),
            new WordBox("://example.com", 50, 10, 110, 16));

        Assert.Equal("@tps ://example.com", model.GetAllText());
    }

    [Fact]
    public void 真实词框拆出的查询参数不额外补空格且部分选择同样生效()
    {
        var model = Model(
            new WordBox("https://example.com/a-b?x=", 35, 15, 175, 13),
            new WordBox("1", 213, 15, 4, 10),
            new WordBox("&y=", 221, 15, 25, 13),
            new WordBox("2#top", 249, 15, 36, 13));
        Assert.Equal("https://example.com/a-b?x=1&y=2#top", model.GetAllText());
        Assert.Equal("1&y=2#top", model.GetText(new SelectionRange(1, 4)));
    }

    [Fact]
    public void 网址后的说明文字保留空格()
    {
        Assert.Equal("https://example.com/ note", Model(
            new WordBox("https://example.com/", 10, 10, 100, 13),
            new WordBox("note", 125, 10, 30, 13)).GetAllText());
    }

    [Fact]
    public void 协议与全角标点拆词时仍能纠正并拼接()
    {
        Assert.Equal("https://example.com", Model(
            new WordBox("https", 10, 10, 30, 16),
            new WordBox("：", 45, 10, 2, 16),
            new WordBox("／／", 52, 10, 6, 16),
            new WordBox("example.com", 63, 10, 70, 16)).GetAllText());
    }

    [Fact]
    public void 同行两个独立网址均可纠正()
    {
        var model = Model(
            new WordBox("@tps://one.com", 10, 10, 100, 16),
            new WordBox("@tps://two.com", 130, 10, 100, 16));

        Assert.Equal("https://one.com https://two.com", model.GetAllText());
    }
}

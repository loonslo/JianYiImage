using JianYiImage.Core.Text;

namespace JianYiImage.Core.Tests;

public class TextTruncationTests
{
    [Theory]
    [InlineData("😀")]
    [InlineData("𠀀")]
    [InlineData("e\u0301")]
    [InlineData("👩‍💻")]
    [InlineData("🇨🇳")]
    public void 超长查询在边界处保留完整文字元素(string element)
    {
        var prefix = new string('a', 1499);
        var actual = TextTruncation.AtTextElementBoundary(prefix + element + "z", 1500);

        Assert.Equal(prefix, actual);
        Assert.DoesNotContain("%EF%BF%BD", Uri.EscapeDataString(actual));
    }

    [Fact]
    public void 正好容纳完整元素时不额外丢字()
    {
        var prefix = new string('a', 1498);
        Assert.Equal(prefix + "😀", TextTruncation.AtTextElementBoundary(prefix + "😀z", 1500));
    }

    [Fact]
    public void 普通文本与零长度限制行为明确()
    {
        Assert.Equal("中文 abc", TextTruncation.AtTextElementBoundary("中文 abc", 1500));
        Assert.Equal("abc", TextTruncation.AtTextElementBoundary("abcdef", 3));
        Assert.Equal("", TextTruncation.AtTextElementBoundary("😀", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextTruncation.AtTextElementBoundary("a", -1));
    }
}

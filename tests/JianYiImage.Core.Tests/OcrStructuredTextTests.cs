using JianYiImage.Core.Ocr;

namespace JianYiImage.Core.Tests;

public class OcrStructuredTextTests
{
    [Fact]
    public void 拆词但仍含下划线的文件名能拼回且普通单词不会补造下划线()
    {
        var model = new TextSelectionModel(new OcrDocument([new OcrLine([
            new WordBox("file", 10, 10, 30, 20), new WordBox("_name.py", 45, 10, 80, 20, SpaceBefore: true)
        ])], 800, 100));
        Assert.Equal("file_name.py", model.GetAllText());
        var missing = new TextSelectionModel(new OcrDocument([new OcrLine([
            new WordBox("file", 10, 10, 30, 20), new WordBox("name.py", 46, 10, 80, 20)
        ])], 800, 100));
        Assert.Equal("file name.py", missing.GetAllText());
    }
    [Fact]
    public void 真实电话字框纠正横杠并拼回被拆开的数字()
    {
        WordBox[] words = [new("Tel:", 36, 377, 42, 21), new("1", 89, 378, 8, 20),
            new("38", 104, 378, 27, 20), new("一", 134, 389, 8, 2), new("0000", 144, 378, 58, 20),
            new("一", 205, 389, 8, 2), new("1", 216, 378, 8, 20), new("234", 230, 378, 44, 20)];
        var model = new TextSelectionModel(new OcrDocument([new OcrLine(words)], 800, 500));
        Assert.Equal("Tel: 138-0000-1234", model.GetAllText());
        Assert.Equal("138-0000-1234", model.GetText(new SelectionRange(1, 8)));
        Assert.Equal("一", words[3].Text);
        Assert.Equal(words[3] with { Text = "-" }, model.Words[3]);
    }

    [Theory]
    [InlineData("中文的一、第一、星期一")]
    [InlineData("abc138一0000一1234")]
    [InlineData("0123456789 1.23 -5.0")]
    public void 不改写正常中文标识符和数字文本(string text)
    {
        Assert.Equal(text, new TextSelectionModel(new OcrDocument(
            [new OcrLine([new WordBox(text, 10, 10, 300, 20)])], 800, 100)).GetAllText());
    }

    [Fact]
    public void 不把相隔较远的数字列合并为电话号码()
    {
        var model = new TextSelectionModel(new OcrDocument([new OcrLine([
            new WordBox("138一", 10, 10, 40, 20), new WordBox("0000一1234", 100, 10, 100, 20)
        ])], 800, 100));
        Assert.Equal("138一0000一1234", string.Concat(model.Words.Select(w => w.Text)));
    }

    [Fact]
    public void 引擎明确的词间空格不受字框的窄间距影响()
    {
        var model = new TextSelectionModel(new OcrDocument([new OcrLine([
            new WordBox("hello", 10, 10, 40, 20), new WordBox("world", 51, 10, 40, 20, SpaceBefore: true)
        ])], 800, 100));
        Assert.Equal("hello world", model.GetAllText());
        Assert.Equal("world", model.GetText(new SelectionRange(1, 2)));
    }
}

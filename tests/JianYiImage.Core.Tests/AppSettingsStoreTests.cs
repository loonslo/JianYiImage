using JianYiImage.Core.Ocr;
using JianYiImage.Core.Settings;

namespace JianYiImage.Core.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jianyiimage-settings-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "nested", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 默认设置是自动识别语言()
    {
        Assert.Equal(OcrLanguageSelection.Auto, new AppSettings().OcrLanguage);
    }

    [Fact]
    public void 文件不存在时读到默认设置()
    {
        var store = new AppSettingsStore(SettingsPath);

        Assert.Equal(new AppSettings(), store.Load());
    }

    [Fact]
    public void 保存后能读回且自动创建目录()
    {
        var store = new AppSettingsStore(SettingsPath);

        store.Save(new AppSettings { OcrLanguage = "ja-JP" });

        Assert.True(File.Exists(SettingsPath));
        Assert.Equal("ja-JP", new AppSettingsStore(SettingsPath).Load().OcrLanguage);
    }

    [Fact]
    public void 重复保存覆盖旧内容且不留临时文件()
    {
        var store = new AppSettingsStore(SettingsPath);

        store.Save(new AppSettings { OcrLanguage = "en-US" });
        store.Save(new AppSettings { OcrLanguage = "ko-KR" });

        Assert.Equal("ko-KR", store.Load().OcrLanguage);
        Assert.Equal([SettingsPath], Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    public void 文件损坏时回到默认设置而不是抛出(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, content);

        Assert.Equal(new AppSettings(), new AppSettingsStore(SettingsPath).Load());
    }

    [Fact]
    public void 读取时忽略未知字段并兼容大小写()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "OCRLANGUAGE": "fr-FR", "someFutureSetting": 42 }""");

        Assert.Equal("fr-FR", new AppSettingsStore(SettingsPath).Load().OcrLanguage);
    }

    [Fact]
    public void 语言字段为null时交给偏好解析按自动处理()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "ocrLanguage": null }""");

        var loaded = new AppSettingsStore(SettingsPath).Load();

        Assert.Null(loaded.OcrLanguage);
        Assert.Null(OcrLanguageSelection.ResolveRequestedTag(loaded.OcrLanguage, [new("en-US", "English")]));
    }

    [Fact]
    public void 默认路径位于本地应用数据目录下的JianYiImage()
    {
        var expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(expectedRoot, "JianYiImage", "settings.json"), AppSettingsStore.DefaultPath);
    }
}

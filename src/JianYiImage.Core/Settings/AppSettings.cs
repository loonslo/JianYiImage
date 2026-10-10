using System.Text.Json;
using JianYiImage.Core.Ocr;

namespace JianYiImage.Core.Settings;

/// <summary>用户设置。字段只增不改名，旧版本写出的文件新版本仍能读。</summary>
public sealed record AppSettings
{
    /// <summary>OCR 识别语言：<see cref="OcrLanguageSelection.Auto"/> 或语言标记（如 en-US）。</summary>
    public string? OcrLanguage { get; init; } = OcrLanguageSelection.Auto;
}

/// <summary>把 <see cref="AppSettings"/> 存成用户目录下的一个 JSON 文件。</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    public AppSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>%LOCALAPPDATA%\JianYiImage\settings.json，与崩溃日志同一个目录。</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JianYiImage",
        "settings.json");

    /// <summary>
    /// 读取设置。文件不存在、读不了或内容损坏时一律回到默认值：
    /// 设置丢了只是退回默认，不该让程序起不来。
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize<AppSettings>(stream, Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>先写临时文件再替换，写到一半断电不会留下半个设置文件。</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, _path, overwrite: true);
    }
}

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using JianYiImage.Core.Ocr;

namespace JianYiImage.Core.Tests;

/// <summary>
/// 安装程序脚本（installer/）的一致性与辅助脚本测试。
/// 不测"真的去装语言包"：那会改系统，而且在管理员终端里跑测试时会真的执行。
/// </summary>
public sealed class InstallerScriptTests : IDisposable
{
    private static readonly string Root = FindRoot();
    private readonly string _work = Path.Combine(Path.GetTempPath(), "jianyiimage-installer-" + Guid.NewGuid().ToString("N"));

    public InstallerScriptTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录（Directory.Build.props）。");
    }

    private static string InstallerFile(string name) => Path.Combine(Root, "installer", name);

    private static string ReadScript() => File.ReadAllText(InstallerFile("JianYiImage.iss"), new UTF8Encoding(true));

    private static string ComponentName(OcrLanguageCatalog.Entry entry) =>
        (entry.Common ? @"ocr\common\" : @"ocr\other\") + entry.CapabilityTag.ToLowerInvariant().Replace('-', '_');

    private sealed record ScriptComponent(string Name, string Description, string Types);

    private sealed record ScriptLookup(string Component, string Caption, string CapabilityTag, string EngineTag);

    private static List<ScriptComponent> ScriptComponents(string iss) =>
        Regex.Matches(
                iss,
                @"^Name: ""(?<name>ocr\\(?:common|other)\\[a-z0-9_]+)""; Description: ""(?<desc>[^""]+)""; Types: (?<types>[a-z ]+)\r?$",
                RegexOptions.Multiline)
            .Select(m => new ScriptComponent(m.Groups["name"].Value, m.Groups["desc"].Value, m.Groups["types"].Value))
            .ToList();

    private static List<ScriptLookup> ScriptLookups(string iss) =>
        Regex.Matches(iss, @"AddOcrLang\('(?<c>[^']+)', '(?<d>[^']+)', '(?<cap>[^']+)', '(?<eng>[^']+)'\);")
            .Select(m => new ScriptLookup(m.Groups["c"].Value, m.Groups["d"].Value, m.Groups["cap"].Value, m.Groups["eng"].Value))
            .ToList();

    [Fact]
    public void 安装脚本的语言包勾选项与语言目录逐项一致()
    {
        var iss = ReadScript();
        var components = ScriptComponents(iss);
        var lookups = ScriptLookups(iss);
        var entries = OcrLanguageCatalog.Entries;

        Assert.Equal(entries.Count, components.Count);
        Assert.Equal(entries.Count, lookups.Count);

        foreach (var entry in entries)
        {
            var name = ComponentName(entry);
            var component = Assert.Single(components, c => c.Name == name);
            Assert.Equal(entry.Label, component.Description);

            var lookup = Assert.Single(lookups, l => l.Component == name);
            Assert.Equal(entry.Label, lookup.Caption);
            Assert.Equal(entry.CapabilityTag, lookup.CapabilityTag);
            Assert.Equal(entry.EngineTag, lookup.EngineTag);
        }
    }

    [Fact]
    public void 常用语言与其他语言分成两组且顺序与目录一致()
    {
        var iss = ReadScript();
        var names = ScriptComponents(iss).Select(c => c.Name).ToList();

        Assert.Equal(OcrLanguageCatalog.Entries.Select(ComponentName), names);
        Assert.Contains(@"Name: ""ocr\common""; Description: ""常用语言""", iss);
        Assert.Contains(@"Name: ""ocr\other""; Description: ""其他语言""", iss);
    }

    [Fact]
    public void 推荐安装类型只包含简体中文和英文()
    {
        var recommended = ScriptComponents(ReadScript())
            .Where(c => c.Types.Split(' ').Contains("recommended"))
            .Select(c => c.Name)
            .ToList();

        Assert.Equal([@"ocr\common\zh_cn", @"ocr\common\en_us"], recommended);
    }

    [Fact]
    public void 所有语言都在自定义类型里可选()
    {
        var notCustom = ScriptComponents(ReadScript())
            .Where(c => !c.Types.Split(' ').Contains("custom"))
            .Select(c => c.Name);

        Assert.Empty(notCustom);
    }

    [Fact]
    public void 安装脚本带UTF8签名因为含有中文字面量()
    {
        var bytes = File.ReadAllBytes(InstallerFile("JianYiImage.iss"));

        Assert.True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public void 语言包辅助脚本只含ASCII字符()
    {
        // Windows PowerShell 5.1 会把没有 BOM 的脚本按 ANSI 代码页读取，中文字面量可能直接让解析失败
        var bytes = File.ReadAllBytes(InstallerFile("OcrLanguages.ps1"));

        Assert.All(bytes, b => Assert.True(b < 0x80, "OcrLanguages.ps1 只能包含 ASCII"));
    }

    [Fact]
    public void 简体中文语言文件与安装脚本配套且是UTF8()
    {
        var text = File.ReadAllText(InstallerFile("ChineseSimplified.isl"), new UTF8Encoding(false, throwOnInvalidBytes: true));

        Assert.Contains("LanguageID=$0804", text);
        Assert.Contains("LanguageCodePage=936", text);
        Assert.Contains("ButtonNext=下一步(&N) >", text);
        Assert.Contains("[CustomMessages]", text);
        Assert.Contains(@"""ChineseSimplified.isl""", ReadScript());
    }

    // ---------- 辅助脚本（-DryRun：不碰系统） ----------

    private static string? PowerShellExe()
    {
        var path = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return File.Exists(path) ? path : null;
    }

    private (int ExitCode, string[] Lines) RunHelper(params string[] arguments)
    {
        var exe = PowerShellExe()!;
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", InstallerFile("OcrLanguages.ps1") }.Concat(arguments))
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "辅助脚本 60 秒内没有结束");
        _ = output.Result;
        var errorText = error.Result;
        Assert.True(string.IsNullOrWhiteSpace(errorText) || process.ExitCode != 0, errorText);

        var outFile = arguments[Array.IndexOf(arguments, "-OutFile") + 1];
        return (process.ExitCode, File.Exists(outFile) ? File.ReadAllLines(outFile) : []);
    }

    private string WriteCapabilities(params string[] lines)
    {
        var path = Path.Combine(_work, "capabilities.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void 干跑时按能力列表报告每个语言的状态()
    {
        if (PowerShellExe() is null)
        {
            return;
        }

        var capabilities = WriteCapabilities(
            "Language.OCR~~~zh-CN~0.0.1.0|Installed",
            "Language.OCR~~~en-US~0.0.1.0|NotPresent",
            "Language.OCR~~~ja-JP~0.0.1.0|NotPresent");
        var outFile = Path.Combine(_work, "result.txt");

        var (exitCode, lines) = RunHelper(
            "-Mode", "Install", "-DryRun", "-CapabilityListFile", capabilities,
            "-Languages", "zh-CN,en-US,xx-YY", "-OutFile", outFile);

        Assert.Equal(0, exitCode);
        Assert.Equal(
            [
                "zh-CN|already|",
                "en-US|dryrun|Language.OCR~~~en-US~0.0.1.0",
                "xx-YY|unavailable|not offered by this Windows",
            ],
            lines);
    }

    [Fact]
    public void 干跑时拒绝非法的语言标记并以非零退出()
    {
        if (PowerShellExe() is null)
        {
            return;
        }

        var capabilities = WriteCapabilities("Language.OCR~~~en-US~0.0.1.0|NotPresent");
        var outFile = Path.Combine(_work, "result.txt");

        var (exitCode, lines) = RunHelper(
            "-Mode", "Install", "-DryRun", "-CapabilityListFile", capabilities,
            "-Languages", "en-US,bad;tag,*", "-OutFile", outFile);

        Assert.Equal(1, exitCode);
        Assert.Contains("bad;tag|failed|invalid language tag", lines);
        Assert.Contains("*|failed|invalid language tag", lines);
        Assert.Contains("en-US|dryrun|Language.OCR~~~en-US~0.0.1.0", lines);
    }

    [Fact]
    public void 列出模式只写出引擎报告的语言标记()
    {
        if (PowerShellExe() is null)
        {
            return;
        }

        var outFile = Path.Combine(_work, "installed.txt");

        var (exitCode, lines) = RunHelper("-Mode", "List", "-OutFile", outFile);

        Assert.Equal(0, exitCode);
        Assert.All(lines, line => Assert.Matches(@"^[A-Za-z]{2,3}(-[A-Za-z0-9]+)+$", line));
    }
}

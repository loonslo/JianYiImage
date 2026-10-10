using JianYiImage.Core.Decoding;
using Microsoft.Win32;

namespace JianYiImage.Core.SystemIntegration;

/// <summary>
/// 每用户文件关联注册。调用方提供 HKCU（测试提供隔离子键）；不修改默认应用的 UserChoice。
/// 只移除本应用拥有的键和值，保留扩展名及其他程序的注册。
/// </summary>
public static class FileAssociationRegistration
{
    public const string ApplicationName = "简壹图片浏览器";
    public const string ShellApplicationName = "简壹图片";
    public const string CapabilitiesPath = @"Software\JianYiImage\Capabilities";
    public const string ApplicationsPath = @"Software\Classes\Applications\JianYiImage.exe";
    public const string RegisteredApplicationsPath = @"Software\RegisteredApplications";

    // ProgID 沿用 1.0.x 的 ImageViewer.AssocFile.*，不随项目改名：Windows 把用户选的默认应用按 ProgID
    // 记在受保护的 UserChoice 里，程序无法代写，改名会让已选的默认图片查看器全部失效。注册时只更新它指向的程序路径。
    private const string ProgIdPrefix = "ImageViewer.AssocFile.";
    private const string ContextMenuName = "JianYiImage.Open";

    // 1.0.x 用通用名 ImageViewer 注册。改名后注册和撤销都要清掉这些旧键，
    // 否则资源管理器会同时出现两条右键项和两个「打开方式」候选。
    private const string LegacyCapabilitiesPath = @"Software\ImageViewer\Capabilities";
    private const string LegacyApplicationsPath = @"Software\Classes\Applications\ImageViewer.exe";
    private const string LegacyParentPath = @"Software\ImageViewer";
    private const string LegacyContextMenuName = "ImageViewer.Open";

    // 通用名的键别的程序也可能占用；旧键里的显示名带本品牌才当作本应用的，其余不碰。
    private const string BrandKeyword = "简壹图片";

    // 历代在「默认应用」里登记过的名字；值指向本应用的 Capabilities（新旧路径皆可）才算本应用的登记。
    private static readonly string[] PreviousApplicationNames = ["ImageViewer", ApplicationName];
    private static readonly string[] OwnedCapabilitiesPaths = [CapabilitiesPath, LegacyCapabilitiesPath];

    public static string ProgId(string extension) =>
        $"{ProgIdPrefix}{extension.TrimStart('.').ToUpperInvariant()}";

    public static string ContextMenuPath(string extension) =>
        $@"Software\Classes\SystemFileAssociations\{extension}\shell\{ContextMenuName}";

    private static string LegacyContextMenuPath(string extension) =>
        $@"Software\Classes\SystemFileAssociations\{extension}\shell\{LegacyContextMenuName}";

    public static bool IsRegistered(RegistryKey userRoot)
    {
        using var registered = userRoot.OpenSubKey(RegisteredApplicationsPath);
        return HasOwnedRegistration(registered, ShellApplicationName)
            || PreviousApplicationNames.Any(name => HasOwnedRegistration(registered, name));
    }

    /// <summary>
    /// 当前的注册是否指向这个可执行文件。卸载程序用它判断"这是不是我装的那一份"：
    /// 卸载一份拷贝，不该把指向另一份拷贝的文件关联一起拆掉。1.0.x 的旧注册（指向 ImageViewer.exe）不算。
    /// </summary>
    public static bool IsRegisteredTo(RegistryKey userRoot, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(userRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        using var command = userRoot.OpenSubKey($@"{ApplicationsPath}\shell\open\command");
        var expected = $"\"{Path.GetFullPath(executablePath)}\" \"%1\"";
        return string.Equals(command?.GetValue(null) as string, expected, StringComparison.OrdinalIgnoreCase);
    }

    public static void Register(RegistryKey userRoot, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(userRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var exePath = Path.GetFullPath(executablePath);
        var command = $"\"{exePath}\" \"%1\"";
        var icon = $"\"{exePath}\",0";

        using (var capabilities = userRoot.CreateSubKey(CapabilitiesPath))
        {
            capabilities.SetValue("ApplicationName", ShellApplicationName);
            capabilities.SetValue("ApplicationDescription", "图片浏览、缩放和本地文字提取");
            capabilities.SetValue("ApplicationIcon", icon);
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var extension in ImageFormatRegistry.AllExtensions)
            {
                associations.SetValue(extension, ProgId(extension));
            }
        }

        using (var application = userRoot.CreateSubKey(ApplicationsPath))
        {
            application.SetValue("FriendlyAppName", ShellApplicationName);
            using var openCommand = application.CreateSubKey(@"shell\open\command");
            openCommand.SetValue(null, command);
            using var supportedTypes = application.CreateSubKey("SupportedTypes");
            foreach (var extension in ImageFormatRegistry.AllExtensions)
            {
                supportedTypes.SetValue(extension, string.Empty);
            }
        }

        foreach (var extension in ImageFormatRegistry.AllExtensions)
        {
            var progId = ProgId(extension);
            using (var fileClass = userRoot.CreateSubKey($@"Software\Classes\{progId}"))
            {
                fileClass.SetValue(null, $"{extension.TrimStart('.').ToUpperInvariant()} 图像 ({ShellApplicationName})");
                using var iconKey = fileClass.CreateSubKey("DefaultIcon");
                iconKey.SetValue(null, icon);
                using var openCommand = fileClass.CreateSubKey(@"shell\open\command");
                openCommand.SetValue(null, command);
            }

            using (var openWith = userRoot.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids"))
            {
                openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            using var contextMenu = userRoot.CreateSubKey(ContextMenuPath(extension));
            contextMenu.SetValue(null, $"用{ShellApplicationName}打开");
            contextMenu.SetValue("Icon", icon);
            contextMenu.SetValue("MultiSelectModel", "Single");
            using var menuCommand = contextMenu.CreateSubKey("command");
            menuCommand.SetValue(null, command);
        }

        using (var registered = userRoot.CreateSubKey(RegisteredApplicationsPath))
        {
            registered.SetValue(ShellApplicationName, CapabilitiesPath);
            foreach (var previousName in PreviousApplicationNames)
            {
                if (HasOwnedRegistration(registered, previousName))
                {
                    registered.DeleteValue(previousName, throwOnMissingValue: false);
                }
            }
        }

        // 新注册写完之后再清旧键：中途失败时宁可暂时重复，也不要一个都没有。
        RemoveLegacyRegistration(userRoot);
    }

    public static void Unregister(RegistryKey userRoot)
    {
        ArgumentNullException.ThrowIfNull(userRoot);
        foreach (var extension in ImageFormatRegistry.AllExtensions)
        {
            var progId = ProgId(extension);
            userRoot.DeleteSubKeyTree($@"Software\Classes\{progId}", throwOnMissingSubKey: false);
            userRoot.DeleteSubKeyTree(ContextMenuPath(extension), throwOnMissingSubKey: false);
            using var openWith = userRoot.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids", writable: true);
            openWith?.DeleteValue(progId, throwOnMissingValue: false);
        }

        userRoot.DeleteSubKeyTree(ApplicationsPath, throwOnMissingSubKey: false);
        // 不删除 Software\JianYiImage 本身：可能包含将来的用户设置。
        userRoot.DeleteSubKeyTree(CapabilitiesPath, throwOnMissingSubKey: false);
        RemoveLegacyRegistration(userRoot);
        using var registered = userRoot.OpenSubKey(RegisteredApplicationsPath, writable: true);
        foreach (var name in PreviousApplicationNames.Append(ShellApplicationName))
        {
            if (HasOwnedRegistration(registered, name))
            {
                registered!.DeleteValue(name, throwOnMissingValue: false);
            }
        }
    }

    /// <summary>清掉 1.0.x 在 ImageViewer 名下写的键。ProgID 沿用，不在其中；父键只在空的时候才删。</summary>
    private static void RemoveLegacyRegistration(RegistryKey userRoot)
    {
        if (IsOurLegacyKey(userRoot, LegacyApplicationsPath, "FriendlyAppName"))
        {
            userRoot.DeleteSubKeyTree(LegacyApplicationsPath, throwOnMissingSubKey: false);
        }

        if (IsOurLegacyKey(userRoot, LegacyCapabilitiesPath, "ApplicationName"))
        {
            userRoot.DeleteSubKeyTree(LegacyCapabilitiesPath, throwOnMissingSubKey: false);
        }

        foreach (var extension in ImageFormatRegistry.AllExtensions)
        {
            if (IsOurLegacyKey(userRoot, LegacyContextMenuPath(extension), valueName: null))
            {
                userRoot.DeleteSubKeyTree(LegacyContextMenuPath(extension), throwOnMissingSubKey: false);
            }
        }

        DeleteIfEmpty(userRoot, LegacyParentPath);
    }

    private static bool IsOurLegacyKey(RegistryKey userRoot, string path, string? valueName)
    {
        using var key = userRoot.OpenSubKey(path);
        return key?.GetValue(valueName) is string text && text.Contains(BrandKeyword, StringComparison.Ordinal);
    }

    private static void DeleteIfEmpty(RegistryKey userRoot, string path)
    {
        bool empty;
        using (var key = userRoot.OpenSubKey(path))
        {
            empty = key is { SubKeyCount: 0, ValueCount: 0 };
        }

        if (empty)
        {
            userRoot.DeleteSubKey(path, throwOnMissingSubKey: false);
        }
    }

    private static bool HasOwnedRegistration(RegistryKey? registered, string name) =>
        registered?.GetValue(name) is string value
        && OwnedCapabilitiesPaths.Contains(value, StringComparer.OrdinalIgnoreCase);
}

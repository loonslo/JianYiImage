using ImageViewer.Core.Decoding;
using Microsoft.Win32;

namespace ImageViewer.Core.SystemIntegration;

/// <summary>
/// 每用户文件关联注册。调用方提供 HKCU（测试提供隔离子键）；不修改默认应用的 UserChoice。
/// 只移除本应用拥有的键和值，保留扩展名及其他程序的注册。
/// </summary>
public static class FileAssociationRegistration
{
    public const string ApplicationName = "简壹图片浏览器";
    public const string ShellApplicationName = "简壹图片";
    private static readonly string[] PreviousApplicationNames = ["ImageViewer", ApplicationName];
    public const string CapabilitiesPath = @"Software\ImageViewer\Capabilities";
    public const string ApplicationsPath = @"Software\Classes\Applications\ImageViewer.exe";
    public const string RegisteredApplicationsPath = @"Software\RegisteredApplications";

    public static string ProgId(string extension) =>
        $"ImageViewer.AssocFile.{extension.TrimStart('.').ToUpperInvariant()}";

    public static string ContextMenuPath(string extension) =>
        $@"Software\Classes\SystemFileAssociations\{extension}\shell\ImageViewer.Open";

    public static bool IsRegistered(RegistryKey userRoot)
    {
        using var registered = userRoot.OpenSubKey(RegisteredApplicationsPath);
        return HasOwnedRegistration(registered, ShellApplicationName)
            || PreviousApplicationNames.Any(name => HasOwnedRegistration(registered, name));
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

        using var registered = userRoot.CreateSubKey(RegisteredApplicationsPath);
        registered.SetValue(ShellApplicationName, CapabilitiesPath);
        foreach (var previousName in PreviousApplicationNames)
        {
            if (HasOwnedRegistration(registered, previousName))
            {
                registered.DeleteValue(previousName, throwOnMissingValue: false);
            }
        }
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
        // 不删除 Software\ImageViewer 本身：可能包含将来的用户设置。
        userRoot.DeleteSubKeyTree(CapabilitiesPath, throwOnMissingSubKey: false);
        using var registered = userRoot.OpenSubKey(RegisteredApplicationsPath, writable: true);
        foreach (var name in PreviousApplicationNames.Append(ShellApplicationName))
        {
            if (HasOwnedRegistration(registered, name))
            {
                registered!.DeleteValue(name, throwOnMissingValue: false);
            }
        }
    }

    private static bool HasOwnedRegistration(RegistryKey? registered, string name) =>
        string.Equals(registered?.GetValue(name) as string, CapabilitiesPath, StringComparison.OrdinalIgnoreCase);
}

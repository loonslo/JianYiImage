using JianYiImage.Core.SystemIntegration;
using Microsoft.Win32;
using Windows.System;

namespace JianYiImage.App.Services;

/// <summary>
/// 在 HKCU 下注册/撤销文件关联，免安装 exe 的标配做法。
///
/// Windows 10+ 不允许程序静默抢占默认查看器（系统会直接忽略或提示用户确认），
/// 注册默认应用候选、打开方式与资源管理器右键菜单，再引导用户在系统设置中选择。
/// 全部写在 HKCU 下，不需要管理员权限。
/// </summary>
public static class FileAssociationService
{
    public static bool IsRegistered() => FileAssociationRegistration.IsRegistered(Registry.CurrentUser);

    /// <summary>把当前可执行文件注册为全部受支持扩展名的「打开方式」候选。</summary>
    public static void RegisterAll()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法获取当前可执行文件路径，注册失败。");

        FileAssociationRegistration.Register(Registry.CurrentUser, exePath);
        NativeMethods.NotifyShellAssociationsChanged();
    }

    /// <summary>移除本应用的文件关联、默认应用候选和右键菜单，保留共享键及其他应用。</summary>
    public static void UnregisterAll()
    {
        FileAssociationRegistration.Unregister(Registry.CurrentUser);
        NativeMethods.NotifyShellAssociationsChanged();
    }

    /// <summary>当前的文件关联是否指向正在运行的这份程序（卸载程序据此决定要不要拆关联）。</summary>
    public static bool IsRegisteredToThisExecutable() =>
        Environment.ProcessPath is { } exePath
        && FileAssociationRegistration.IsRegisteredTo(Registry.CurrentUser, exePath);

    public static async Task<bool> OpenDefaultAppsSettingsAsync()
    {
        // 定位到应用的查询参数由新版 Windows 11 支持；Windows 10 使用普通默认应用页。
        var settingsUri = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString(FileAssociationRegistration.ApplicationName)
            : "ms-settings:defaultapps";
        if (await Launcher.LaunchUriAsync(new Uri(settingsUri)))
        {
            return true;
        }

        return settingsUri != "ms-settings:defaultapps"
            && await Launcher.LaunchUriAsync(new Uri("ms-settings:defaultapps"));
    }
}

/// <summary>Shell 通知，让资源管理器立刻感知关联变化，不用重新登录。</summary>
internal static partial class NativeMethods
{
    private const uint ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    public static void NotifyShellAssociationsChanged() =>
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);

    [System.Runtime.InteropServices.LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}

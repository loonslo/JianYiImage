using ImageViewer.Core.Decoding;
using ImageViewer.Core.SystemIntegration;
using Microsoft.Win32;

namespace ImageViewer.Core.Tests;

public sealed class FileAssociationRegistrationTests : IDisposable
{
    private readonly string _testPath = $@"Software\ImageViewer.Tests\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public FileAssociationRegistrationTests() => _root = Registry.CurrentUser.CreateSubKey(_testPath);

    [Fact]
    public void 注册全部格式的默认应用候选与右键打开命令()
    {
        const string exe = @"C:\测试目录\Image Viewer\ImageViewer.exe";
        FileAssociationRegistration.Register(_root, exe);

        Assert.True(FileAssociationRegistration.IsRegistered(_root));
        AssertValue(FileAssociationRegistration.RegisteredApplicationsPath, "简壹图片",
            FileAssociationRegistration.CapabilitiesPath);
        AssertValue(FileAssociationRegistration.CapabilitiesPath, "ApplicationName", "简壹图片");
        AssertValue(FileAssociationRegistration.ApplicationsPath, "FriendlyAppName", "简壹图片");
        AssertValue(FileAssociationRegistration.ApplicationsPath + @"\shell\open\command", null,
            $"\"{exe}\" \"%1\"");

        foreach (var extension in ImageFormatRegistry.AllExtensions)
        {
            var progId = FileAssociationRegistration.ProgId(extension);
            AssertValue(FileAssociationRegistration.CapabilitiesPath + @"\FileAssociations", extension, progId);
            AssertValue(FileAssociationRegistration.ApplicationsPath + @"\SupportedTypes", extension, string.Empty);
            AssertValue($@"Software\Classes\{progId}\shell\open\command", null, $"\"{exe}\" \"%1\"");
            using var openWith = _root.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            Assert.NotNull(openWith);
            Assert.Contains(progId, openWith.GetValueNames());
            Assert.Equal(RegistryValueKind.None, openWith.GetValueKind(progId));
            AssertValue(FileAssociationRegistration.ContextMenuPath(extension), null, "用简壹图片打开");
            AssertValue(FileAssociationRegistration.ContextMenuPath(extension) + @"\command", null,
                $"\"{exe}\" \"%1\"");
        }
    }

    [Fact]
    public void 旧名称注册升级为中文名称并保持文件关联标识()
    {
        using (var registered = _root.CreateSubKey(FileAssociationRegistration.RegisteredApplicationsPath))
        {
            registered.SetValue("ImageViewer", FileAssociationRegistration.CapabilitiesPath);
        }
        Assert.True(FileAssociationRegistration.IsRegistered(_root));

        FileAssociationRegistration.Register(_root, @"C:\ImageViewer.exe");

        AssertValue(FileAssociationRegistration.RegisteredApplicationsPath, "简壹图片",
            FileAssociationRegistration.CapabilitiesPath);
        using var updated = _root.OpenSubKey(FileAssociationRegistration.RegisteredApplicationsPath);
        Assert.Null(updated!.GetValue("ImageViewer"));
        Assert.Null(updated.GetValue("简壹图片浏览器"));
        AssertValue(FileAssociationRegistration.CapabilitiesPath + @"\FileAssociations", ".png", "ImageViewer.AssocFile.PNG");

        FileAssociationRegistration.Unregister(_root);
        Assert.False(FileAssociationRegistration.IsRegistered(_root));
    }

    [Fact]
    public void 重复注册更新移动后的程序路径()
    {
        FileAssociationRegistration.Register(_root, @"C:\old\ImageViewer.exe");
        FileAssociationRegistration.Register(_root, @"C:\new folder\ImageViewer.exe");
        FileAssociationRegistration.Register(_root, @"C:\new folder\ImageViewer.exe");

        foreach (var extension in ImageFormatRegistry.AllExtensions)
        {
            AssertValue(FileAssociationRegistration.ContextMenuPath(extension) + @"\command", null,
                "\"C:\\new folder\\ImageViewer.exe\" \"%1\"");
        }
    }

    [Fact]
    public void 注册和撤销保留其他程序与已有默认选择()
    {
        using (var extension = _root.CreateSubKey(@"Software\Classes\.png"))
        {
            extension.SetValue(null, "OtherViewer.PNG");
            using var openWith = extension.CreateSubKey("OpenWithProgids");
            openWith.SetValue("OtherViewer.PNG", Array.Empty<byte>(), RegistryValueKind.None);
        }
        const string userChoicePath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.png\UserChoice";
        using (var userChoice = _root.CreateSubKey(userChoicePath))
        {
            userChoice.SetValue("ProgId", "OtherViewer.PNG");
            userChoice.SetValue("Hash", "test-placeholder");
        }
        using (var otherMenu = _root.CreateSubKey(@"Software\Classes\SystemFileAssociations\.png\shell\OtherViewer"))
        {
            otherMenu.SetValue(null, "other menu");
        }
        using (var apps = _root.CreateSubKey(FileAssociationRegistration.RegisteredApplicationsPath))
        {
            apps.SetValue("OtherViewer", @"Software\OtherViewer\Capabilities");
        }
        using (var settings = _root.CreateSubKey(@"Software\ImageViewer\Settings"))
        {
            settings.SetValue("Theme", "dark");
        }

        FileAssociationRegistration.Register(_root, @"C:\ImageViewer.exe");
        AssertValue(userChoicePath, "ProgId", "OtherViewer.PNG");
        FileAssociationRegistration.Unregister(_root);

        AssertValue(@"Software\Classes\.png", null, "OtherViewer.PNG");
        AssertValue(@"Software\Classes\.png\OpenWithProgids", "OtherViewer.PNG", Array.Empty<byte>());
        AssertValue(userChoicePath, "ProgId", "OtherViewer.PNG");
        AssertValue(userChoicePath, "Hash", "test-placeholder");
        AssertValue(@"Software\Classes\SystemFileAssociations\.png\shell\OtherViewer", null, "other menu");
        AssertValue(FileAssociationRegistration.RegisteredApplicationsPath, "OtherViewer", @"Software\OtherViewer\Capabilities");
        AssertValue(@"Software\ImageViewer\Settings", "Theme", "dark");
    }

    [Fact]
    public void 撤销删除自己的全部注册且可以重复执行()
    {
        FileAssociationRegistration.Register(_root, @"C:\ImageViewer.exe");
        FileAssociationRegistration.Unregister(_root);
        FileAssociationRegistration.Unregister(_root);

        Assert.False(FileAssociationRegistration.IsRegistered(_root));
        AssertMissing(FileAssociationRegistration.CapabilitiesPath);
        AssertMissing(FileAssociationRegistration.ApplicationsPath);
        foreach (var extension in ImageFormatRegistry.AllExtensions)
        {
            var progId = FileAssociationRegistration.ProgId(extension);
            AssertMissing($@"Software\Classes\{progId}");
            AssertMissing(FileAssociationRegistration.ContextMenuPath(extension));
            using var openWith = _root.OpenSubKey($@"Software\Classes\{extension}\OpenWithProgids");
            Assert.DoesNotContain(progId, openWith!.GetValueNames());
        }
    }

    private void AssertValue(string path, string? name, object value)
    {
        using var key = _root.OpenSubKey(path);
        Assert.NotNull(key);
        Assert.Equal(value, key.GetValue(name));
    }

    private void AssertMissing(string path)
    {
        using var key = _root.OpenSubKey(path);
        Assert.Null(key);
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_testPath, throwOnMissingSubKey: false);
    }
}

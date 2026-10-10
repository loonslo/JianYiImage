using JianYiImage.Core.Decoding;
using JianYiImage.Core.SystemIntegration;
using Microsoft.Win32;

namespace JianYiImage.Core.Tests;

public sealed class FileAssociationRegistrationTests : IDisposable
{
    private readonly string _testPath = $@"Software\JianYiImage.Tests\{Guid.NewGuid():N}";
    private readonly RegistryKey _root;

    public FileAssociationRegistrationTests() => _root = Registry.CurrentUser.CreateSubKey(_testPath);

    [Fact]
    public void 注册全部格式的默认应用候选与右键打开命令()
    {
        const string exe = @"C:\测试目录\简壹 图片\JianYiImage.exe";
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
    public void 内部名使用JianYiImage而ProgID沿用旧值()
    {
        Assert.Equal(@"Software\JianYiImage\Capabilities", FileAssociationRegistration.CapabilitiesPath);
        Assert.Equal(@"Software\Classes\Applications\JianYiImage.exe", FileAssociationRegistration.ApplicationsPath);
        Assert.EndsWith(@"\shell\JianYiImage.Open", FileAssociationRegistration.ContextMenuPath(".png"));

        // 用户在 Windows 里选的默认图片查看器按 ProgID 记在受保护的 UserChoice 里，程序无法代写；
        // ProgID 若跟着改名，升级后所有已选的默认应用都会失效。
        Assert.Equal("ImageViewer.AssocFile.PNG", FileAssociationRegistration.ProgId(".png"));
        Assert.Equal("ImageViewer.AssocFile.JPEG", FileAssociationRegistration.ProgId("jpeg"));
    }

    [Fact]
    public void 旧名称注册升级为当前名称并保持文件关联标识()
    {
        using (var registered = _root.CreateSubKey(FileAssociationRegistration.RegisteredApplicationsPath))
        {
            registered.SetValue("ImageViewer", FileAssociationRegistration.CapabilitiesPath);
        }
        Assert.True(FileAssociationRegistration.IsRegistered(_root));

        FileAssociationRegistration.Register(_root, @"C:\JianYiImage.exe");

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
    public void 从ImageViewer旧注册升级时清理旧键并让原有ProgID指向新程序()
    {
        const string newExe = @"C:\new folder\JianYiImage.exe";
        SeedLegacyRegistration(@"C:\old\ImageViewer.exe");
        Assert.True(FileAssociationRegistration.IsRegistered(_root));

        FileAssociationRegistration.Register(_root, newExe);

        AssertMissing(@"Software\ImageViewer\Capabilities");
        AssertMissing(@"Software\Classes\Applications\ImageViewer.exe");
        AssertMissing(@"Software\ImageViewer"); // 清空后的父键一并清掉
        foreach (var extension in ImageFormatRegistry.AllExtensions)
        {
            AssertMissing($@"Software\Classes\SystemFileAssociations\{extension}\shell\ImageViewer.Open");
            // 用户已选的默认应用记的是 ProgID；它的命令改指向新程序，选择就继续有效。
            AssertValue($@"Software\Classes\{FileAssociationRegistration.ProgId(extension)}\shell\open\command", null,
                $"\"{newExe}\" \"%1\"");
        }

        using var registered = _root.OpenSubKey(FileAssociationRegistration.RegisteredApplicationsPath);
        Assert.Null(registered!.GetValue("简壹图片浏览器"));
        AssertValue(FileAssociationRegistration.RegisteredApplicationsPath, "简壹图片",
            FileAssociationRegistration.CapabilitiesPath);
        AssertValue(FileAssociationRegistration.RegisteredApplicationsPath, "OtherViewer",
            @"Software\OtherViewer\Capabilities");
    }

    [Fact]
    public void 撤销同时清理ImageViewer旧注册()
    {
        SeedLegacyRegistration(@"C:\old\ImageViewer.exe");

        FileAssociationRegistration.Unregister(_root);

        Assert.False(FileAssociationRegistration.IsRegistered(_root));
        AssertMissing(@"Software\ImageViewer\Capabilities");
        AssertMissing(@"Software\Classes\Applications\ImageViewer.exe");
        foreach (var extension in ImageFormatRegistry.AllExtensions)
        {
            AssertMissing($@"Software\Classes\{FileAssociationRegistration.ProgId(extension)}");
            AssertMissing($@"Software\Classes\SystemFileAssociations\{extension}\shell\ImageViewer.Open");
        }
        AssertValue(FileAssociationRegistration.RegisteredApplicationsPath, "OtherViewer",
            @"Software\OtherViewer\Capabilities");
    }

    [Fact]
    public void 别的程序占用的通用名旧键不被清理()
    {
        using (var foreignApp = _root.CreateSubKey(@"Software\Classes\Applications\ImageViewer.exe"))
        {
            foreignApp.SetValue("FriendlyAppName", "Some Other Viewer");
        }
        using (var foreignMenu = _root.CreateSubKey(@"Software\Classes\SystemFileAssociations\.png\shell\ImageViewer.Open"))
        {
            foreignMenu.SetValue(null, "Open with Other Viewer");
        }
        using (var foreignCapabilities = _root.CreateSubKey(@"Software\ImageViewer\Capabilities"))
        {
            foreignCapabilities.SetValue("ApplicationName", "Some Other Viewer");
        }

        FileAssociationRegistration.Register(_root, @"C:\JianYiImage.exe");
        FileAssociationRegistration.Unregister(_root);

        AssertValue(@"Software\Classes\Applications\ImageViewer.exe", "FriendlyAppName", "Some Other Viewer");
        AssertValue(@"Software\Classes\SystemFileAssociations\.png\shell\ImageViewer.Open", null, "Open with Other Viewer");
        AssertValue(@"Software\ImageViewer\Capabilities", "ApplicationName", "Some Other Viewer");
    }

    [Fact]
    public void 旧父键里还有别的内容时不删除()
    {
        SeedLegacyRegistration(@"C:\old\ImageViewer.exe");
        using (var settings = _root.CreateSubKey(@"Software\ImageViewer\Settings"))
        {
            settings.SetValue("Theme", "dark");
        }

        FileAssociationRegistration.Register(_root, @"C:\JianYiImage.exe");

        AssertMissing(@"Software\ImageViewer\Capabilities");
        AssertValue(@"Software\ImageViewer\Settings", "Theme", "dark");
    }

    [Fact]
    public void 重复注册更新移动后的程序路径()
    {
        FileAssociationRegistration.Register(_root, @"C:\old\JianYiImage.exe");
        FileAssociationRegistration.Register(_root, @"C:\new folder\JianYiImage.exe");
        FileAssociationRegistration.Register(_root, @"C:\new folder\JianYiImage.exe");

        foreach (var extension in ImageFormatRegistry.AllExtensions)
        {
            AssertValue(FileAssociationRegistration.ContextMenuPath(extension) + @"\command", null,
                "\"C:\\new folder\\JianYiImage.exe\" \"%1\"");
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
        using (var settings = _root.CreateSubKey(@"Software\JianYiImage\Settings"))
        {
            settings.SetValue("Theme", "dark");
        }

        FileAssociationRegistration.Register(_root, @"C:\JianYiImage.exe");
        AssertValue(userChoicePath, "ProgId", "OtherViewer.PNG");
        FileAssociationRegistration.Unregister(_root);

        AssertValue(@"Software\Classes\.png", null, "OtherViewer.PNG");
        AssertValue(@"Software\Classes\.png\OpenWithProgids", "OtherViewer.PNG", Array.Empty<byte>());
        AssertValue(userChoicePath, "ProgId", "OtherViewer.PNG");
        AssertValue(userChoicePath, "Hash", "test-placeholder");
        AssertValue(@"Software\Classes\SystemFileAssociations\.png\shell\OtherViewer", null, "other menu");
        AssertValue(FileAssociationRegistration.RegisteredApplicationsPath, "OtherViewer", @"Software\OtherViewer\Capabilities");
        AssertValue(@"Software\JianYiImage\Settings", "Theme", "dark");
    }

    [Fact]
    public void 撤销删除自己的全部注册且可以重复执行()
    {
        FileAssociationRegistration.Register(_root, @"C:\JianYiImage.exe");
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

    [Fact]
    public void 能判断当前注册指向的是不是某个程序()
    {
        const string registered = @"C:\Program Files\简壹 图片\JianYiImage.exe";
        Assert.False(FileAssociationRegistration.IsRegisteredTo(_root, registered));

        FileAssociationRegistration.Register(_root, registered);

        Assert.True(FileAssociationRegistration.IsRegisteredTo(_root, registered));
        Assert.True(FileAssociationRegistration.IsRegisteredTo(_root, registered.ToUpperInvariant()));
        Assert.False(FileAssociationRegistration.IsRegisteredTo(_root, @"C:\other copy\JianYiImage.exe"));

        FileAssociationRegistration.Unregister(_root);
        Assert.False(FileAssociationRegistration.IsRegisteredTo(_root, registered));
    }

    [Fact]
    public void 旧版ImageViewer的注册不算指向新程序()
    {
        SeedLegacyRegistration(@"C:\old\ImageViewer.exe");

        Assert.False(FileAssociationRegistration.IsRegisteredTo(_root, @"C:\old\ImageViewer.exe"));
        Assert.False(FileAssociationRegistration.IsRegisteredTo(_root, @"C:
ew\JianYiImage.exe"));
    }

    /// <summary>还原 1.0.x（程序名 ImageViewer.exe）在注册表里留下的布局，另加一个别的程序的登记作对照。</summary>
    private void SeedLegacyRegistration(string exe)
    {
        var command = $"\"{exe}\" \"%1\"";
        using (var capabilities = _root.CreateSubKey(@"Software\ImageViewer\Capabilities"))
        {
            capabilities.SetValue("ApplicationName", "简壹图片浏览器");
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var extension in ImageFormatRegistry.AllExtensions)
            {
                associations.SetValue(extension, FileAssociationRegistration.ProgId(extension));
            }
        }

        using (var application = _root.CreateSubKey(@"Software\Classes\Applications\ImageViewer.exe"))
        {
            application.SetValue("FriendlyAppName", "简壹图片浏览器");
            using var openCommand = application.CreateSubKey(@"shell\open\command");
            openCommand.SetValue(null, command);
        }

        foreach (var extension in ImageFormatRegistry.AllExtensions)
        {
            var progId = FileAssociationRegistration.ProgId(extension);
            using (var fileClassCommand = _root.CreateSubKey($@"Software\Classes\{progId}\shell\open\command"))
            {
                fileClassCommand.SetValue(null, command);
            }

            using (var openWith = _root.CreateSubKey($@"Software\Classes\{extension}\OpenWithProgids"))
            {
                openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            using var menu = _root.CreateSubKey($@"Software\Classes\SystemFileAssociations\{extension}\shell\ImageViewer.Open");
            menu.SetValue(null, "用简壹图片浏览器打开");
            using var menuCommand = menu.CreateSubKey("command");
            menuCommand.SetValue(null, command);
        }

        using var registered = _root.CreateSubKey(FileAssociationRegistration.RegisteredApplicationsPath);
        registered.SetValue("简壹图片浏览器", @"Software\ImageViewer\Capabilities");
        registered.SetValue("OtherViewer", @"Software\OtherViewer\Capabilities");
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

using System.Diagnostics;
using Windows.Storage;

namespace ImageViewer.App.Services;

/// <summary>
/// 文件系统操作：删除到回收站、在资源管理器中定位。
/// </summary>
public static class FileSystemService
{
    /// <summary>
    /// 删除到回收站（不是永久删除）。
    /// 用 WinRT 的 StorageFile.DeleteAsync 而不是自己拼 Shell API——
    /// StorageDeleteOption.Default 本身就是「能进回收站就进，不能就报错」，
    /// 不会有静默永久删除的风险。
    /// </summary>
    public static async Task DeleteToRecycleBinAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        await file.DeleteAsync(StorageDeleteOption.Default);
    }

    /// <summary>在资源管理器中打开所在文件夹并选中该文件。</summary>
    public static void RevealInExplorer(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
    }
}

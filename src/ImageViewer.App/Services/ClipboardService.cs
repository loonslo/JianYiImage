using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ImageViewer.App.Services;

/// <summary>
/// 剪贴板操作。Ctrl+C 有三种含义：
/// 选中了文字复制文字；框选了区域复制该区域的图片；否则复制整张图片。
/// </summary>
public static class ClipboardService
{
    public static void SetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);

        Clipboard.SetContent(package);
        Clipboard.Flush(); // 不 Flush 的话本进程退出后剪贴板内容就没了
    }

    /// <summary>
    /// 把一段已编码好的 PNG 流放进剪贴板。
    /// </summary>
    /// <param name="pngStream">已定位到起始位置的 PNG 数据流。</param>
    /// <param name="sourcePath">
    /// 原始文件路径。只有 <paramref name="attachSourceFile"/> 为 true 时才会一并附带，
    /// 让粘贴到资源管理器得到原文件——但前提是流里的像素与文件内容完全一致
    /// （旋转过或裁剪过区域后就不能再附带，否则「粘贴为图片」和「粘贴为文件」会对不上）。
    /// </param>
    public static async Task SetImageAsync(IRandomAccessStream pngStream, string? sourcePath, bool attachSourceFile)
    {
        ArgumentNullException.ThrowIfNull(pngStream);

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(pngStream));

        if (attachSourceFile && !string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(sourcePath);
                package.SetStorageItems(new[] { file });
            }
            catch (Exception)
            {
                // 拿不到 StorageFile 不影响位图本身已经放进去了
            }
        }

        Clipboard.SetContent(package);
        Clipboard.Flush(); // 不 Flush 的话本进程退出后剪贴板内容就没了
    }
}

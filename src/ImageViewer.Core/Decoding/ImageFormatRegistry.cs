namespace ImageViewer.Core.Decoding;

/// <summary>
/// 应用支持的图片扩展名清单。文件关联注册、文件夹翻页枚举、打开对话框筛选都用它，
/// 保证三处口径一致。
/// </summary>
public static class ImageFormatRegistry
{
    /// <summary>系统 WIC 原生支持、无需额外扩展的格式。</summary>
    public static readonly IReadOnlySet<string> CoreExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif",
        ".bmp", ".dib",
        ".gif",
        ".tif", ".tiff",
        ".ico",
    };

    /// <summary>依赖系统可选编解码器扩展的现代格式（本机已装 HEIF / WebMedia）。</summary>
    public static readonly IReadOnlySet<string> ModernExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".webp",
        ".heic", ".heif", ".hif",
        ".avif",
        ".jxl",
    };

    /// <summary>应用能打开的全部扩展名。</summary>
    public static readonly IReadOnlySet<string> AllExtensions =
        new HashSet<string>(CoreExtensions.Concat(ModernExtensions), StringComparer.OrdinalIgnoreCase);

    /// <summary>判断路径是否为支持的图片。</summary>
    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) && AllExtensions.Contains(ext);
    }
}

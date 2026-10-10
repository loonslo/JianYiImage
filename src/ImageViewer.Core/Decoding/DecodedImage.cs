using Windows.Graphics.Imaging;

namespace ImageViewer.Core.Decoding;

/// <summary>
/// 一次解码的结果。像素统一为 BGRA8 premultiplied——Win2D 和 OCR 都直接吃这个格式，
/// 避免中途再做一次转换。
/// </summary>
public sealed class DecodedImage : IDisposable
{
    private SoftwareBitmap? _bitmap;

    public DecodedImage(SoftwareBitmap bitmap, ImageMetadata metadata, bool isPreview)
    {
        _bitmap = bitmap;
        Metadata = metadata;
        IsPreview = isPreview;
    }

    /// <summary>解码出的位图。已应用 EXIF 方向。</summary>
    public SoftwareBitmap Bitmap =>
        _bitmap ?? throw new ObjectDisposedException(nameof(DecodedImage));

    public ImageMetadata Metadata { get; }

    /// <summary>
    /// 是否为降采样的快速预览。两段式加载中，预览先上屏，全分辨率随后替换。
    /// </summary>
    public bool IsPreview { get; }

    /// <summary>该帧实际解码出的像素宽度（预览时小于原图）。</summary>
    public int PixelWidth => Bitmap.PixelWidth;

    /// <summary>该帧实际解码出的像素高度（预览时小于原图）。</summary>
    public int PixelHeight => Bitmap.PixelHeight;

    /// <summary>占用的近似字节数，供 LRU 缓存按内存量驱逐。</summary>
    public long ApproximateByteSize => (long)PixelWidth * PixelHeight * 4;

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}

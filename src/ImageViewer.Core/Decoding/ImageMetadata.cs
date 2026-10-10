using ImageViewer.Core.Imaging;

namespace ImageViewer.Core.Decoding;

/// <summary>
/// 不解码全部像素就能拿到的图片信息。用于在真正解码前确定显示尺寸、是否动图等。
/// </summary>
/// <param name="PixelWidth">文件中记录的像素宽度（未应用 EXIF 方向）。</param>
/// <param name="PixelHeight">文件中记录的像素高度（未应用 EXIF 方向）。</param>
/// <param name="Orientation">EXIF 方向。</param>
/// <param name="FrameCount">帧数，动图 &gt; 1。</param>
/// <param name="FormatName">解码出该图的格式名，用于诊断显示。</param>
/// <param name="DecoderName">实际使用的解码器名，用于诊断显示。</param>
public readonly record struct ImageMetadata(
    uint PixelWidth,
    uint PixelHeight,
    ExifOrientation Orientation,
    uint FrameCount,
    string FormatName,
    string DecoderName)
{
    /// <summary>应用 EXIF 方向后，应当显示的宽度。</summary>
    public uint DisplayWidth => Orientation.SwapsDimensions() ? PixelHeight : PixelWidth;

    /// <summary>应用 EXIF 方向后，应当显示的高度。</summary>
    public uint DisplayHeight => Orientation.SwapsDimensions() ? PixelWidth : PixelHeight;

    /// <summary>是否为多帧动图。</summary>
    public bool IsAnimated => FrameCount > 1;

    /// <summary>总像素数，用于判断是否为需要特殊处理的超大图。</summary>
    public long PixelCount => (long)PixelWidth * PixelHeight;
}

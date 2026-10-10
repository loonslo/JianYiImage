namespace ImageViewer.Core.Decoding;

/// <summary>
/// 图片解码器。实现分两类：
/// <list type="bullet">
///   <item>WIC——系统自带，覆盖常见格式，零额外依赖、硬件加速；</item>
///   <item>Magick——兜底，覆盖系统没有编解码器的格式（本机的 AVIF / JPEG XL）。</item>
/// </list>
/// </summary>
public interface IImageDecoder
{
    /// <summary>解码器名，出现在诊断信息里。</summary>
    string Name { get; }

    /// <summary>
    /// 声明支持的扩展名，小写含点（如 ".png"）。
    /// 这只是「可以试试」的声明，实际能否解码以运行时为准——
    /// 系统编解码器是否装齐无法静态判断。
    /// </summary>
    IReadOnlySet<string> SupportedExtensions { get; }

    /// <summary>读取尺寸、方向、帧数等信息，不解码全部像素。</summary>
    Task<ImageMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// 解码图片，并应用 EXIF 方向。
    /// </summary>
    /// <param name="path">图片路径。</param>
    /// <param name="maxPixelDimension">
    /// 长边像素上限：传 0 表示全分辨率；传正数则解出不超过该尺寸的降采样版本，
    /// 用于两段式加载的第一段（几十毫秒出图）。
    /// </param>
    /// <param name="cancellationToken">翻页时用它取消未完成的解码。</param>
    Task<DecodedImage> DecodeAsync(string path, int maxPixelDimension, CancellationToken cancellationToken);
}

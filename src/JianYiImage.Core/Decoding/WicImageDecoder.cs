using JianYiImage.Core.Imaging;
using Windows.Graphics.Imaging;

namespace JianYiImage.Core.Decoding;

/// <summary>
/// 主解码路径：系统 WIC（Windows Imaging Component）。
/// 零额外依赖、硬件加速，并且会自动用上系统已安装的编解码器扩展
/// （本机的 HEIF Image Extension / WebMedia Extension）。
/// </summary>
public sealed class WicImageDecoder : IImageDecoder
{
    private const string OrientationProperty = "System.Photo.Orientation";

    public string Name => "WIC";

    public IReadOnlySet<string> SupportedExtensions => ImageFormatRegistry.AllExtensions;

    public async Task<ImageMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        await using var fileStream = OpenRead(path);
        using var randomAccess = fileStream.AsRandomAccessStream();

        var decoder = await BitmapDecoder.CreateAsync(randomAccess).AsTask(cancellationToken);
        var orientation = await ReadOrientationAsync(decoder, cancellationToken);
        var frameCount = ReadFrameCount(decoder);

        return new ImageMetadata(
            decoder.PixelWidth,
            decoder.PixelHeight,
            orientation,
            frameCount,
            DescribeFormat(decoder.DecoderInformation?.FriendlyName, path),
            Name);
    }

    public async Task<DecodedImage> DecodeAsync(string path, int maxPixelDimension, CancellationToken cancellationToken)
    {
        await using var fileStream = OpenRead(path);
        using var randomAccess = fileStream.AsRandomAccessStream();

        var decoder = await BitmapDecoder.CreateAsync(randomAccess).AsTask(cancellationToken);
        var orientation = await ReadOrientationAsync(decoder, cancellationToken);
        var frameCount = ReadFrameCount(decoder);

        // ScaledWidth/ScaledHeight 作用在「应用 EXIF 方向之前」的原始像素上，
        // 所以这里必须拿 decoder.PixelWidth/Height 来算，不能用 DisplayWidth/Height。
        var transform = new BitmapTransform();
        var isPreview = false;

        if (maxPixelDimension > 0)
        {
            var longestEdge = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
            if (longestEdge > maxPixelDimension)
            {
                var scale = (double)maxPixelDimension / longestEdge;
                transform.ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
                transform.ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale));
                transform.InterpolationMode = BitmapInterpolationMode.Fant; // 高质量降采样
                isPreview = true;
            }
        }

        var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation, // 方向由 WIC 直接应用，出来就是正的
            ColorManagementMode.ColorManageToSRgb)      // ICC 色彩管理，照片不偏色
            .AsTask(cancellationToken);

        var metadata = new ImageMetadata(
            decoder.PixelWidth,
            decoder.PixelHeight,
            orientation,
            frameCount,
            DescribeFormat(decoder.DecoderInformation?.FriendlyName, path),
            Name);

        return new DecodedImage(bitmap, metadata, isPreview);
    }

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024, useAsync: true);

    private static async Task<ExifOrientation> ReadOrientationAsync(
        BitmapDecoder decoder, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await decoder.BitmapProperties
                .GetPropertiesAsync(new[] { OrientationProperty })
                .AsTask(cancellationToken);

            if (properties.TryGetValue(OrientationProperty, out var property) &&
                property?.Value is ushort raw)
            {
                return ExifOrientationExtensions.FromExifValue(raw);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 许多格式（PNG/BMP/GIF 等）根本没有这个属性，取不到是正常情况。
        }

        return ExifOrientation.Normal;
    }

    private static uint ReadFrameCount(BitmapDecoder decoder)
    {
        try
        {
            return decoder.FrameCount;
        }
        catch (Exception)
        {
            // 单帧格式的解码器不一定实现该属性。
            return 1;
        }
    }

    private static string DescribeFormat(string? friendlyName, string path)
    {
        if (!string.IsNullOrWhiteSpace(friendlyName))
        {
            return friendlyName;
        }

        var ext = Path.GetExtension(path);
        return string.IsNullOrEmpty(ext) ? "未知" : ext.TrimStart('.').ToUpperInvariant();
    }
}

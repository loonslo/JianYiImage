using JianYiImage.Core.Decoding;

namespace JianYiImage.Core.Tests;

public class ImageFormatLabelTests
{
    [Theory]
    [InlineData("PNG Decoder", "PNG")]
    [InlineData("JPEG Decoder", "JPEG")]
    [InlineData("GIF Decoder", "GIF")]
    [InlineData("BMP Decoder", "BMP")]
    [InlineData("TIFF Decoder", "TIFF")]
    [InlineData("GIF (动图)", "GIF (动图)")]
    [InlineData("Microsoft Raw Image Decoder", "RAW")]
    [InlineData(null, "未知")]
    [InlineData("  ", "未知")]
    [InlineData("WebP", "WebP")]
    [InlineData("Unknown Codec", "Unknown Codec")]
    [InlineData("WMPhoto Decoder", "JPEG XR")]
    [InlineData("HD Photo", "JPEG XR")]
    [InlineData("Windows Media Photo", "JPEG XR")]
    [InlineData("Camera Raw Decoder", "RAW")]
    [InlineData("  microsoft PNG decoder  ", "PNG")]
    public void 显示格式名不暴露解码器诊断信息(string? source, string expected) =>
        Assert.Equal(expected, ImageFormatLabel.From(source));
}

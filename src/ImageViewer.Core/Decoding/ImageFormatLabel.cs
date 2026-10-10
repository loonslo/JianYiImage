namespace ImageViewer.Core.Decoding;

/// <summary>将解码器诊断名称转换为用户可读的图片格式名称。</summary>
public static class ImageFormatLabel
{
    public static string From(string? formatName)
    {
        var name = formatName?.Trim();
        if (string.IsNullOrEmpty(name)) return "未知";
        if (name.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase)) name = name["Microsoft ".Length..];
        if (name.EndsWith(" Decoder", StringComparison.OrdinalIgnoreCase)) name = name[..^" Decoder".Length];
        return name switch
        {
            "WMPhoto" or "HD Photo" or "Windows Media Photo" => "JPEG XR",
            "Raw Image" or "Camera Raw" => "RAW",
            _ => name,
        };
    }
}

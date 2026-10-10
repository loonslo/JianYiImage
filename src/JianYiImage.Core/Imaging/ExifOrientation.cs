namespace JianYiImage.Core.Imaging;

/// <summary>
/// EXIF 方向标记（TIFF Tag 274）。手机竖拍的照片依赖它才能正确显示。
/// </summary>
public enum ExifOrientation
{
    /// <summary>未指定，按 Normal 处理。</summary>
    Unspecified = 0,

    /// <summary>0°，无变换。</summary>
    Normal = 1,

    /// <summary>水平镜像。</summary>
    FlipHorizontal = 2,

    /// <summary>旋转 180°。</summary>
    Rotate180 = 3,

    /// <summary>垂直镜像。</summary>
    FlipVertical = 4,

    /// <summary>沿主对角线转置（水平镜像 + 逆时针 90°）。</summary>
    Transpose = 5,

    /// <summary>顺时针旋转 90°。</summary>
    Rotate90 = 6,

    /// <summary>沿副对角线转置（水平镜像 + 顺时针 90°）。</summary>
    Transverse = 7,

    /// <summary>逆时针旋转 90°（等价顺时针 270°）。</summary>
    Rotate270 = 8,
}

/// <summary>
/// EXIF 方向的解析与几何换算。纯函数，无外部依赖。
/// </summary>
public static class ExifOrientationExtensions
{
    /// <summary>该方向是否会交换宽高（即含 90°/270° 旋转）。</summary>
    public static bool SwapsDimensions(this ExifOrientation orientation) => orientation switch
    {
        ExifOrientation.Transpose or
        ExifOrientation.Rotate90 or
        ExifOrientation.Transverse or
        ExifOrientation.Rotate270 => true,
        _ => false,
    };

    /// <summary>该方向是否包含镜像翻转。</summary>
    public static bool IsMirrored(this ExifOrientation orientation) => orientation switch
    {
        ExifOrientation.FlipHorizontal or
        ExifOrientation.FlipVertical or
        ExifOrientation.Transpose or
        ExifOrientation.Transverse => true,
        _ => false,
    };

    /// <summary>该方向对应的顺时针旋转角度（0 / 90 / 180 / 270）。</summary>
    public static int RotationDegrees(this ExifOrientation orientation) => orientation switch
    {
        ExifOrientation.Normal or ExifOrientation.FlipHorizontal => 0,
        ExifOrientation.Rotate180 or ExifOrientation.FlipVertical => 180,
        ExifOrientation.Transpose or ExifOrientation.Rotate270 => 270,
        ExifOrientation.Rotate90 or ExifOrientation.Transverse => 90,
        _ => 0,
    };

    /// <summary>把解码后的原始像素尺寸换算成应当显示的尺寸。</summary>
    public static (uint Width, uint Height) ApplyToSize(this ExifOrientation orientation, uint width, uint height)
        => orientation.SwapsDimensions() ? (height, width) : (width, height);

    /// <summary>把 EXIF 整数值转成枚举，越界值一律按 Normal 处理。</summary>
    public static ExifOrientation FromExifValue(ushort value)
        => value is >= 1 and <= 8 ? (ExifOrientation)value : ExifOrientation.Normal;

    /// <summary>
    /// 在当前方向上叠加一次用户旋转（顺时针 90° 的整数倍），返回叠加后的方向。
    /// 用于「按 R 键旋转」——不必重新解码，改方向即可。
    /// </summary>
    public static ExifOrientation RotateClockwise(this ExifOrientation orientation, int quarterTurns)
    {
        var normalized = ((quarterTurns % 4) + 4) % 4;
        var result = orientation == ExifOrientation.Unspecified ? ExifOrientation.Normal : orientation;

        for (var i = 0; i < normalized; i++)
        {
            result = result switch
            {
                ExifOrientation.Normal => ExifOrientation.Rotate90,
                ExifOrientation.Rotate90 => ExifOrientation.Rotate180,
                ExifOrientation.Rotate180 => ExifOrientation.Rotate270,
                ExifOrientation.Rotate270 => ExifOrientation.Normal,
                ExifOrientation.FlipHorizontal => ExifOrientation.Transverse,
                ExifOrientation.Transverse => ExifOrientation.FlipVertical,
                ExifOrientation.FlipVertical => ExifOrientation.Transpose,
                ExifOrientation.Transpose => ExifOrientation.FlipHorizontal,
                _ => ExifOrientation.Normal,
            };
        }

        return result;
    }
}

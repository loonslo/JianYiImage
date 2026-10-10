namespace ImageViewer.Core.Rendering;

/// <summary>
/// 用户按 R 键产生的显示旋转，只有四种状态。
///
/// 刻意与 <see cref="ImageViewer.Core.Imaging.ExifOrientation"/> 分开：
/// 后者在解码时烘焙进像素（EXIF 转正），这个是纯展示层的旋转，
/// 换图时重置，不改变缓存里的解码结果。
/// </summary>
public enum RotationQuarterTurn
{
    None = 0,
    Clockwise90 = 1,
    Clockwise180 = 2,
    Clockwise270 = 3,
}

public static class RotationQuarterTurnExtensions
{
    public static RotationQuarterTurn Next(this RotationQuarterTurn rotation) =>
        (RotationQuarterTurn)(((int)rotation + 1) % 4);

    public static RotationQuarterTurn Previous(this RotationQuarterTurn rotation) =>
        (RotationQuarterTurn)(((int)rotation + 3) % 4);

    /// <summary>90°/270° 会交换宽高。</summary>
    public static bool SwapsDimensions(this RotationQuarterTurn rotation) =>
        rotation is RotationQuarterTurn.Clockwise90 or RotationQuarterTurn.Clockwise270;

    public static (double Width, double Height) ApplyToSize(
        this RotationQuarterTurn rotation, double width, double height) =>
        rotation.SwapsDimensions() ? (height, width) : (width, height);

    /// <summary>顺时针角度（弧度），供 Matrix3x2.CreateRotation 使用。</summary>
    public static float ToRadians(this RotationQuarterTurn rotation) =>
        (float)(Math.PI / 2 * (int)rotation);
}

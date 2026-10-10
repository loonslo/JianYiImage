using System.Numerics;

namespace ImageViewer.Core.Rendering;

/// <summary>
/// 位图坐标 ↔ 视口坐标的变换矩阵构造。纯数学，用 System.Numerics（无 UI 依赖），
/// 结果可以直接赋给 Win2D 的 CanvasDrawingSession.Transform——两者用的是同一个 Matrix3x2 类型。
///
/// 设计要点：旋转只在这里处理，<see cref="ZoomPanController"/> 完全不知道旋转的存在——
/// 它始终工作在「旋转之后的逻辑尺寸」上，缩放、平移、边界钳制全部复用不变。
/// </summary>
public static class RotationTransform
{
    /// <summary>
    /// 构造「位图空间 → 视口空间」的完整变换：先把位图按 rotation 摆进它旋转后的
    /// 包围盒（尺寸与 ZoomPanController.SetImage 所用的逻辑尺寸一致），再叠加缩放与平移。
    /// </summary>
    /// <param name="bitmapWidth">解码出的原始位图宽度（未旋转）。</param>
    /// <param name="bitmapHeight">解码出的原始位图高度（未旋转）。</param>
    /// <param name="rotation">展示层旋转。</param>
    /// <param name="scale">ZoomPanController.Scale。</param>
    /// <param name="offsetX">ZoomPanController.OffsetX。</param>
    /// <param name="offsetY">ZoomPanController.OffsetY。</param>
    public static Matrix3x2 BuildBitmapToViewport(
        double bitmapWidth,
        double bitmapHeight,
        RotationQuarterTurn rotation,
        double scale,
        double offsetX,
        double offsetY)
    {
        var (logicalWidth, logicalHeight) = rotation.ApplyToSize(bitmapWidth, bitmapHeight);

        // 绕位图中心旋转——该点在旋转前后都不动，旋转后的包围盒仍以它为中心。
        var center = new Vector2((float)(bitmapWidth / 2), (float)(bitmapHeight / 2));
        var rotate = Matrix3x2.CreateRotation(rotation.ToRadians(), center);

        // 把旋转后包围盒的中心（还在 center 位置）平移到逻辑尺寸包围盒的中心，
        // 这样旋转后的内容正好落在 (0,0)-(logicalWidth,logicalHeight) 里。
        var recenter = Matrix3x2.CreateTranslation(
            (float)(logicalWidth / 2 - bitmapWidth / 2),
            (float)(logicalHeight / 2 - bitmapHeight / 2));

        var zoom = Matrix3x2.CreateScale((float)scale) *
                   Matrix3x2.CreateTranslation((float)offsetX, (float)offsetY);

        return rotate * recenter * zoom;
    }

    /// <summary>
    /// 视口坐标 → 位图坐标，命中测试用。<paramref name="transform"/> 不可逆时
    /// （scale 为 0 等退化情形）返回 false。
    /// </summary>
    public static bool TryGetInverse(Matrix3x2 transform, out Matrix3x2 inverse) =>
        Matrix3x2.Invert(transform, out inverse);
}

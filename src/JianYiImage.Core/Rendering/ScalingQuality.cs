namespace JianYiImage.Core.Rendering;

/// <summary>
/// 缩放时应采用的插值方式。Core 层不引用 Win2D，由 App 层映射到
/// CanvasImageInterpolation。放大到像素级时切最近邻是「清晰」的关键——
/// 继续用双线性会把像素边缘糊掉。
/// </summary>
public enum ScalingQuality
{
    /// <summary>缩小：高质量三次插值，抗锯齿。</summary>
    HighQualityCubic,

    /// <summary>接近原始尺寸：线性插值，平滑过渡。</summary>
    Linear,

    /// <summary>放大看像素：最近邻，保住锐利的像素边缘。</summary>
    NearestNeighbor,
}

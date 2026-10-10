using System.Numerics;
using ImageViewer.Core.Rendering;

namespace ImageViewer.Core.Tests;

/// <summary>
/// 旋转矩阵测试。期望值全部先用物理直觉手算过（把图片想象成一张纸，
/// 顺时针转 90°/180°/270°，角点会落在哪），再和公式结果核对，
/// 避免只是「代码抄自己」式的循环验证。
/// </summary>
public class RotationTransformTests
{
    // 一张 100x50 的横向位图，模拟真实图片尺寸
    private const double W = 100;
    private const double H = 50;

    private static Vector2 Map(Matrix3x2 m, double x, double y) => Vector2.Transform(new Vector2((float)x, (float)y), m);

    // ---------- 不旋转：退化为纯缩放平移 ----------

    [Fact]
    public void 不旋转时等价于缩放平移()
    {
        var m = RotationTransform.BuildBitmapToViewport(W, H, RotationQuarterTurn.None, scale: 2, offsetX: 10, offsetY: 20);

        var p = Map(m, 30, 15);

        Assert.Equal(30 * 2 + 10, p.X, precision: 3);
        Assert.Equal(15 * 2 + 20, p.Y, precision: 3);
    }

    // ---------- 顺时针 90°：横图转竖图，物理直觉——原左上角转到新的右上角 ----------

    [Fact]
    public void 顺时针九十度_原左上角落在旋转后包围盒的右上角()
    {
        var m = RotationTransform.BuildBitmapToViewport(W, H, RotationQuarterTurn.Clockwise90, 1, 0, 0);

        var p = Map(m, 0, 0);

        // 逻辑尺寸变成 50x100（宽高互换），左上角应落在 (50, 0)
        Assert.Equal(50, p.X, precision: 3);
        Assert.Equal(0, p.Y, precision: 3);
    }

    [Fact]
    public void 顺时针九十度_原右下角落在旋转后包围盒的左下角()
    {
        var m = RotationTransform.BuildBitmapToViewport(W, H, RotationQuarterTurn.Clockwise90, 1, 0, 0);

        var p = Map(m, W, H);

        Assert.Equal(0, p.X, precision: 3);
        Assert.Equal(100, p.Y, precision: 3);
    }

    // ---------- 顺时针 180°：原点对角互换 ----------

    [Fact]
    public void 一百八十度_原左上角落在右下角()
    {
        var m = RotationTransform.BuildBitmapToViewport(W, H, RotationQuarterTurn.Clockwise180, 1, 0, 0);

        var p = Map(m, 0, 0);

        // 180° 不交换宽高，逻辑尺寸仍是 100x50
        Assert.Equal(W, p.X, precision: 3);
        Assert.Equal(H, p.Y, precision: 3);
    }

    // ---------- 顺时针 270°（= 逆时针 90°）：原左上角落在左下角 ----------

    [Fact]
    public void 二百七十度_原左上角落在旋转后包围盒的左下角()
    {
        var m = RotationTransform.BuildBitmapToViewport(W, H, RotationQuarterTurn.Clockwise270, 1, 0, 0);

        var p = Map(m, 0, 0);

        Assert.Equal(0, p.X, precision: 3);
        Assert.Equal(100, p.Y, precision: 3);
    }

    // ---------- 逆变换：命中测试要用它把鼠标位置换回位图坐标 ----------

    [Theory]
    [InlineData(RotationQuarterTurn.None)]
    [InlineData(RotationQuarterTurn.Clockwise90)]
    [InlineData(RotationQuarterTurn.Clockwise180)]
    [InlineData(RotationQuarterTurn.Clockwise270)]
    public void 逆变换能把视口坐标还原成原始位图坐标(RotationQuarterTurn rotation)
    {
        var m = RotationTransform.BuildBitmapToViewport(W, H, rotation, scale: 3.2, offsetX: 17, offsetY: -8);

        Assert.True(RotationTransform.TryGetInverse(m, out var inverse));

        var original = new Vector2(37, 12);
        var viewport = Vector2.Transform(original, m);
        var roundTrip = Vector2.Transform(viewport, inverse);

        Assert.Equal(original.X, roundTrip.X, precision: 3);
        Assert.Equal(original.Y, roundTrip.Y, precision: 3);
    }

    [Fact]
    public void 缩放为零时不可逆()
    {
        var m = RotationTransform.BuildBitmapToViewport(W, H, RotationQuarterTurn.None, scale: 0, offsetX: 0, offsetY: 0);

        Assert.False(RotationTransform.TryGetInverse(m, out _));
    }

    // ---------- 尺寸换算 ----------

    [Theory]
    [InlineData(RotationQuarterTurn.None, 100, 50)]
    [InlineData(RotationQuarterTurn.Clockwise90, 50, 100)]
    [InlineData(RotationQuarterTurn.Clockwise180, 100, 50)]
    [InlineData(RotationQuarterTurn.Clockwise270, 50, 100)]
    public void 逻辑尺寸随旋转正确换算(RotationQuarterTurn rotation, double expectedWidth, double expectedHeight)
    {
        var (width, height) = rotation.ApplyToSize(W, H);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Fact]
    public void 连续四次顺时针旋转回到原状态()
    {
        var rotation = RotationQuarterTurn.None;

        for (var i = 0; i < 4; i++)
        {
            rotation = rotation.Next();
        }

        Assert.Equal(RotationQuarterTurn.None, rotation);
    }

    [Fact]
    public void Previous与Next互为反操作()
    {
        var rotation = RotationQuarterTurn.Clockwise90;

        Assert.Equal(rotation, rotation.Next().Previous());
        Assert.Equal(rotation, rotation.Previous().Next());
    }
}

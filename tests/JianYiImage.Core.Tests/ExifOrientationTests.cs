using JianYiImage.Core.Imaging;

namespace JianYiImage.Core.Tests;

public class ExifOrientationTests
{
    [Theory]
    [InlineData(ExifOrientation.Normal, false)]
    [InlineData(ExifOrientation.FlipHorizontal, false)]
    [InlineData(ExifOrientation.Rotate180, false)]
    [InlineData(ExifOrientation.FlipVertical, false)]
    [InlineData(ExifOrientation.Transpose, true)]
    [InlineData(ExifOrientation.Rotate90, true)]
    [InlineData(ExifOrientation.Transverse, true)]
    [InlineData(ExifOrientation.Rotate270, true)]
    public void 只有含九十度旋转的方向才交换宽高(ExifOrientation orientation, bool expected)
    {
        Assert.Equal(expected, orientation.SwapsDimensions());
    }

    [Fact]
    public void 竖拍照片的显示尺寸交换宽高()
    {
        // 手机竖拍：传感器出的是 4000x3000，靠 EXIF 标记转正
        var (width, height) = ExifOrientation.Rotate90.ApplyToSize(4000, 3000);

        Assert.Equal(3000u, width);
        Assert.Equal(4000u, height);
    }

    [Fact]
    public void 普通方向不改变尺寸()
    {
        var (width, height) = ExifOrientation.Normal.ApplyToSize(4000, 3000);

        Assert.Equal(4000u, width);
        Assert.Equal(3000u, height);
    }

    [Theory]
    [InlineData(ExifOrientation.FlipHorizontal, true)]
    [InlineData(ExifOrientation.FlipVertical, true)]
    [InlineData(ExifOrientation.Transpose, true)]
    [InlineData(ExifOrientation.Transverse, true)]
    [InlineData(ExifOrientation.Normal, false)]
    [InlineData(ExifOrientation.Rotate90, false)]
    [InlineData(ExifOrientation.Rotate180, false)]
    [InlineData(ExifOrientation.Rotate270, false)]
    public void 镜像判定正确(ExifOrientation orientation, bool expected)
    {
        Assert.Equal(expected, orientation.IsMirrored());
    }

    [Theory]
    [InlineData(ExifOrientation.Normal, 0)]
    [InlineData(ExifOrientation.Rotate90, 90)]
    [InlineData(ExifOrientation.Rotate180, 180)]
    [InlineData(ExifOrientation.Rotate270, 270)]
    public void 旋转角度换算正确(ExifOrientation orientation, int expected)
    {
        Assert.Equal(expected, orientation.RotationDegrees());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(255)]
    public void 越界的EXIF值一律当作正常方向(int raw)
    {
        Assert.Equal(ExifOrientation.Normal, ExifOrientationExtensions.FromExifValue((ushort)raw));
    }

    [Theory]
    [InlineData(1, ExifOrientation.Normal)]
    [InlineData(6, ExifOrientation.Rotate90)]
    [InlineData(8, ExifOrientation.Rotate270)]
    public void 合法的EXIF值映射到对应方向(int raw, ExifOrientation expected)
    {
        Assert.Equal(expected, ExifOrientationExtensions.FromExifValue((ushort)raw));
    }

    // ---------- 用户按 R 键旋转 ----------

    [Fact]
    public void 顺时针旋转四次回到原点()
    {
        var orientation = ExifOrientation.Normal;

        for (var i = 0; i < 4; i++)
        {
            orientation = orientation.RotateClockwise(1);
        }

        Assert.Equal(ExifOrientation.Normal, orientation);
    }

    [Fact]
    public void 在已有EXIF方向上叠加旋转()
    {
        // 竖拍照片（已是 Rotate90）再顺时针转一次 → 180°
        Assert.Equal(ExifOrientation.Rotate180, ExifOrientation.Rotate90.RotateClockwise(1));
    }

    [Fact]
    public void 逆时针旋转用负数表示()
    {
        Assert.Equal(ExifOrientation.Rotate270, ExifOrientation.Normal.RotateClockwise(-1));
    }

    [Fact]
    public void 带镜像的方向旋转四次也回到原点()
    {
        var orientation = ExifOrientation.FlipHorizontal;

        for (var i = 0; i < 4; i++)
        {
            orientation = orientation.RotateClockwise(1);
        }

        Assert.Equal(ExifOrientation.FlipHorizontal, orientation);
    }

    [Fact]
    public void 未指定方向按正常处理()
    {
        Assert.Equal(ExifOrientation.Rotate90, ExifOrientation.Unspecified.RotateClockwise(1));
    }
}

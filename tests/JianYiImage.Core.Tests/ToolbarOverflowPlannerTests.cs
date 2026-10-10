using JianYiImage.Core.Layout;

namespace JianYiImage.Core.Tests;

public class ToolbarOverflowPlannerTests
{
    private const double Spacing = 6;
    private const double OverflowButtonWidth = 0; // App 先预留常驻更多按钮实测宽 32 DIP 与 6 DIP 间距。

    /// <summary>
    /// 2026-10-01 / Win10 19045 / 100% DPI，capture-window.ps1 -DumpUia 实测 DIP。
    /// 打开、分组线、上一张、下一张、分组线、缩放组、适应、分组线、逆转、顺转、分组线、提取、复制。
    /// 组宽由缩放子按钮边界计算；分组线占位由相邻组按钮边界减去两个实测间距计算。
    /// 原始测量与截图路径写入阶段 B 验收记录。
    /// </summary>
    private static readonly double[] RealToolbarWidths = [66, 9, 32, 32, 9, 126, 32, 9, 32, 32, 9, 92, 32];

    private static double TotalWidth(IReadOnlyList<double> widths) =>
        widths.Sum() + Spacing * (widths.Count - 1);

    private static double UsedWidthWithOverflowButton(IReadOnlyList<double> widths, int visibleCount) =>
        widths.Take(visibleCount).Sum() + Spacing * visibleCount + OverflowButtonWidth;

    // ---------- 放得下：全部命令放得下；常驻“⋯”已由调用方预留 ----------

    [Fact]
    public void 没有任何按钮时没有可显示的按钮()
    {
        Assert.Equal(0, ToolbarOverflowPlanner.CountVisible([], Spacing, OverflowButtonWidth, 800));
    }

    [Fact]
    public void 宽度足够时全部显示()
    {
        var count = ToolbarOverflowPlanner.CountVisible(
            RealToolbarWidths, Spacing, OverflowButtonWidth, availableWidth: 1200);

        Assert.Equal(RealToolbarWidths.Length, count);
    }

    [Fact]
    public void 宽度刚好等于全部按钮总宽时仍然全部显示()
    {
        var count = ToolbarOverflowPlanner.CountVisible(
            RealToolbarWidths, Spacing, OverflowButtonWidth, availableWidth: TotalWidth(RealToolbarWidths));

        Assert.Equal(RealToolbarWidths.Length, count);
    }

    // ---------- 放不下：从右往左收起，调用方已给常驻“⋯”留位置 ----------

    [Fact]
    public void 比总宽窄一个像素就开始收起按钮()
    {
        var count = ToolbarOverflowPlanner.CountVisible(
            RealToolbarWidths, Spacing, OverflowButtonWidth, availableWidth: TotalWidth(RealToolbarWidths) - 1);

        Assert.True(count < RealToolbarWidths.Length);
    }

    [Fact]
    public void 收起时留在栏上的按钮连同溢出按钮不超过可用宽度()
    {
        for (var available = 0; available < TotalWidth(RealToolbarWidths); available++)
        {
            var count = ToolbarOverflowPlanner.CountVisible(
                RealToolbarWidths, Spacing, OverflowButtonWidth, available);

            if (count == 0)
            {
                // 连第一个按钮都放不下：只剩“⋯”，宽度再小也没有更好的办法
                continue;
            }

            Assert.True(
                UsedWidthWithOverflowButton(RealToolbarWidths, count) <= available,
                $"可用宽度 {available} 时留下 {count} 个按钮，加上“⋯”超出了可用宽度");
        }
    }

    [Fact]
    public void 收起时尽量多地保留靠左的按钮()
    {
        for (var available = 0; available < TotalWidth(RealToolbarWidths); available++)
        {
            var count = ToolbarOverflowPlanner.CountVisible(
                RealToolbarWidths, Spacing, OverflowButtonWidth, available);

            Assert.True(count < RealToolbarWidths.Length, $"可用宽度 {available} 不足以放下全部按钮，却没有收起任何按钮");

            // 再多留一个就放不下，说明没有白白浪费空间
            Assert.True(
                UsedWidthWithOverflowButton(RealToolbarWidths, count + 1) > available,
                $"可用宽度 {available} 时只留了 {count} 个按钮，其实还能多放一个");
        }
    }

    [Fact]
    public void 窗口越窄留在栏上的按钮越少且不会变多()
    {
        var previous = int.MaxValue;

        for (var available = 1400; available >= 0; available--)
        {
            var count = ToolbarOverflowPlanner.CountVisible(
                RealToolbarWidths, Spacing, OverflowButtonWidth, available);

            Assert.True(count <= previous, $"可用宽度 {available} 时按钮数 {count} 比更宽时的 {previous} 还多");
            previous = count;
        }
    }

    [Fact]
    public void 总是保留靠左的连续按钮而不是跳着留()
    {
        // 第二个按钮特别宽：放不下它时，后面更窄的按钮也不能越过它留在栏上
        double[] widths = [50, 200, 30, 30];

        var count = ToolbarOverflowPlanner.CountVisible(widths, Spacing, OverflowButtonWidth, availableWidth: 160);

        Assert.Equal(1, count);
    }

    // ---------- 真实工具栏上的几个典型窗口宽度 ----------

    [Theory]
    [InlineData(584, 13)]
    [InlineData(530, 11)]
    [InlineData(400, 8)]
    [InlineData(340, 6)]
    [InlineData(250, 5)]
    [InlineData(150, 3)]
    [InlineData(70, 0)]
    public void 典型窗口宽度下留在栏上的按钮数(double availableWidth, int expected)
    {
        var count = ToolbarOverflowPlanner.CountVisible(
            RealToolbarWidths, Spacing, OverflowButtonWidth, availableWidth);

        Assert.Equal(expected, count);
    }

    // ---------- 异常输入 ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void 可用宽度为零或负数时不会返回负数(double availableWidth)
    {
        var count = ToolbarOverflowPlanner.CountVisible(
            RealToolbarWidths, Spacing, OverflowButtonWidth, availableWidth);

        Assert.Equal(0, count);
    }
}

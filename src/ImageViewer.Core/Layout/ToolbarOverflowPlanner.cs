namespace ImageViewer.Core.Layout;

/// <summary>
/// 顶部工具栏宽度不足时的溢出规划。
/// 只做数值计算、不碰任何 UI 类型，所以「窗口缩到多窄时哪些按钮收进菜单」能脱离窗口测试。
/// </summary>
public static class ToolbarOverflowPlanner
{
    /// <summary>
    /// 算出从左数起有几个按钮能直接留在工具栏上；其余的按从右往左的顺序收进“⋯”菜单。
    /// </summary>
    /// <param name="itemWidths">每个按钮的自然宽度，顺序与工具栏一致。</param>
    /// <param name="spacing">相邻按钮之间的间距。</param>
    /// <param name="overflowButtonWidth">“⋯”按钮的宽度；只有存在被收起的按钮时才会占位。</param>
    /// <param name="availableWidth">工具栏内容区可用的宽度。</param>
    /// <returns>
    /// 等于 <paramref name="itemWidths"/> 的个数：全部放得下，不需要“⋯”。
    /// 小于个数：留下的按钮后面要紧跟“⋯”，其余按钮收进它的菜单。
    /// 始终是靠左的连续一段，不会跳过放不下的宽按钮去留后面更窄的。
    /// </returns>
    public static int CountVisible(
        IReadOnlyList<double> itemWidths,
        double spacing,
        double overflowButtonWidth,
        double availableWidth)
    {
        ArgumentNullException.ThrowIfNull(itemWidths);

        var count = itemWidths.Count;
        if (count == 0)
        {
            return 0;
        }

        var total = spacing * (count - 1);
        foreach (var width in itemWidths)
        {
            total += width;
        }

        if (total <= availableWidth)
        {
            return count;
        }

        // 放不下：“⋯”先占住位置，再按从左到右的顺序尽量多放。
        // 每个留下的按钮都带一个间距——它与右边邻居（最后一个则是“⋯”）之间的那一个。
        var visible = 0;
        var used = overflowButtonWidth;

        foreach (var width in itemWidths)
        {
            var next = used + width + spacing;
            if (next > availableWidth)
            {
                break;
            }

            used = next;
            visible++;
        }

        return visible;
    }
}

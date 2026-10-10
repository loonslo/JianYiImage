using ImageViewer.Core.Layout;
using ImageViewer.Core.SystemIntegration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WinUIControls = Microsoft.UI.Xaml.Controls;

namespace ImageViewer.App;

/// <summary>
/// 顶部工具栏的溢出：窗口变窄时，放不下的按钮从右往左收进“⋯”菜单，而不是被裁掉。
/// 放得下几个由 Core 的 <see cref="ToolbarOverflowPlanner"/> 计算，这里只负责量宽度、显隐按钮和生成菜单。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// 参与溢出的元素，顺序就是溢出顺序：命令条里的元素按 XAML 分组排列；“更多操作”常驻，不参与收起。
    /// </summary>
    private FrameworkElement[]? _toolbarItems;

    /// <summary>
    /// 每个元素的自然宽度。被收起（Collapsed）的元素量不出宽度，沿用它上次可见时的值。
    /// </summary>
    private readonly Dictionary<FrameworkElement, double> _toolbarItemWidths = [];

    private int _toolbarVisibleCount = int.MaxValue;

    private FrameworkElement[] ToolbarItems => _toolbarItems ??=
    [
        .. CommandStrip.Children.OfType<FrameworkElement>(),
    ];

    private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateToolbarOverflow(e.NewSize.Width);

    private void UpdateToolbarOverflow(double availableWidth)
    {
        // 首次布局前宽度是 0，不能据此把按钮全收起来。
        if (availableWidth <= 0)
        {
            return;
        }

        var items = ToolbarItems;

        foreach (var item in items)
        {
            if (item.Visibility == Visibility.Visible && item.DesiredSize.Width > 0)
            {
                _toolbarItemWidths[item] = item.DesiredSize.Width;
            }
        }

        var widths = items.Select(item => _toolbarItemWidths.GetValueOrDefault(item)).ToArray();
        var visibleCount = ToolbarOverflowPlanner.CountVisible(
            widths, CommandStrip.Spacing, overflowButtonWidth: 0,
            availableWidth: Math.Max(0, availableWidth - OverflowButton.ActualWidth - ToolbarGrid.ColumnSpacing));

        // 分组线不单独留在“⋯”前面。
        if (visibleCount > 0 && visibleCount < items.Length && IsDivider(items[visibleCount - 1]))
        {
            visibleCount--;
        }

        if (visibleCount != _toolbarVisibleCount)
        {
            // 栏上的按钮变了，已经打开的菜单内容就过期了。
            OverflowMenu.Hide();
            _toolbarVisibleCount = visibleCount;
        }

        for (var i = 0; i < items.Length; i++)
        {
            items[i].Visibility = i < visibleCount ? Visibility.Visible : Visibility.Collapsed;
        }

    }

    /// <summary>每次打开前按当前状态重建，被收起按钮的可用状态（如首张时“上一张”置灰）才不会过期。</summary>
    private void OnOverflowMenuOpening(object? sender, object e)
    {
        var entries = OverflowMenu.Items;
        entries.Clear();

        foreach (var item in ToolbarItems)
        {
            if (item.Visibility == Visibility.Visible)
            {
                continue;
            }

            if (IsDivider(item))
            {
                // 分组线只放在两项之间：开头不要，连续重复的不要。
                if (entries.Count > 0 && entries[^1] is not WinUIControls.MenuFlyoutSeparator)
                {
                    entries.Add(new WinUIControls.MenuFlyoutSeparator());
                }
            }
            else
            {
                foreach (var button in ButtonsInToolbarItem(item))
                    entries.Add(BuildToolbarMenuItem(button));
            }
        }

        if (entries.Count > 0 && entries[^1] is WinUIControls.MenuFlyoutSeparator)
        {
            entries.RemoveAt(entries.Count - 1);
        }
        if (entries.Count > 0) entries.Add(new WinUIControls.MenuFlyoutSeparator());
        entries.Add(BuildMenuItem("设为默认图片浏览器…", SetDefaultViewerAsync));
        entries.Add(BuildMenuItem("添加右键打开 / 打开方式", RegisterFileAssociations));
        entries.Add(BuildMenuItem($"移除{FileAssociationRegistration.ApplicationName}文件关联", UnregisterFileAssociations));
    }

    private static bool IsDivider(FrameworkElement element) => element.Tag as string == "divider";

    private static IEnumerable<WinUIControls.Button> ButtonsInToolbarItem(FrameworkElement item)
    {
        if (item is WinUIControls.Button button) yield return button;
        else if (item is WinUIControls.Border { Child: WinUIControls.Panel panel })
        {
            foreach (var child in panel.Children.OfType<WinUIControls.Button>()) yield return child;
        }
    }

    private WinUIControls.MenuFlyoutItem BuildToolbarMenuItem(WinUIControls.Button button)
    {
        var item = BuildMenuItem(ToolbarMenuText(button), () => InvokeToolbarButton(button));
        item.IsEnabled = button.IsEnabled;
        return item;
    }

    private string ToolbarMenuText(WinUIControls.Button button)
    {
        // 栏上这个按钮显示的是当前缩放比例，菜单里要写清它的作用（与右键菜单用词一致）。
        if (button == ActualSizeButton)
        {
            return "实际大小 (100%)";
        }

        // 符号按钮（− ＋ ↶ ↷）在 XAML 里有无障碍名称，菜单直接用它；文字按钮用自身文字。
        var name = AutomationProperties.GetName(button);
        return string.IsNullOrEmpty(name) ? button.Content?.ToString() ?? string.Empty : name;
    }

    /// <summary>
    /// 触发按钮自己的 Click，让菜单里的命令和栏上的按钮永远是同一个行为，不必在这里再抄一份动作表。
    /// </summary>
    private static void InvokeToolbarButton(WinUIControls.Button button)
    {
        if (button.IsEnabled
            && FrameworkElementAutomationPeer.CreatePeerForElement(button).GetPattern(PatternInterface.Invoke)
                is IInvokeProvider invoker)
        {
            invoker.Invoke();
        }
    }

}

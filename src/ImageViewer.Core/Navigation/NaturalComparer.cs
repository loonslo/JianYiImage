using System.Runtime.InteropServices;

namespace ImageViewer.Core.Navigation;

/// <summary>
/// 文件名自然排序：img2.png 排在 img10.png 之前。
///
/// 直接调用系统的 StrCmpLogicalW——这正是资源管理器用的比较函数，
/// 自己实现的话排序结果迟早会和用户在资源管理器里看到的顺序对不上。
/// </summary>
public sealed partial class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    private NaturalComparer()
    {
    }

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        return StrCmpLogicalW(x, y);
    }
}

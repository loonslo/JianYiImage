using JianYiImage.Core.Decoding;

namespace JianYiImage.Core.Navigation;

/// <summary>
/// 当前图片所在文件夹的浏览列表。看图软件的命脉——
/// 打开一张图之后要能用左右方向键翻遍整个文件夹。
/// </summary>
public sealed class FolderPlaylist
{
    private readonly List<string> _files;

    private FolderPlaylist(string folderPath, List<string> files, int currentIndex)
    {
        FolderPath = folderPath;
        _files = files;
        CurrentIndex = currentIndex;
    }

    public string FolderPath { get; }

    public IReadOnlyList<string> Files => _files;

    public int CurrentIndex { get; private set; }

    public int Count => _files.Count;

    public bool IsEmpty => _files.Count == 0;

    public string? CurrentPath =>
        CurrentIndex >= 0 && CurrentIndex < _files.Count ? _files[CurrentIndex] : null;

    /// <summary>当前是否已经是第一张。</summary>
    public bool IsFirst => CurrentIndex <= 0;

    /// <summary>当前是否已经是最后一张。</summary>
    public bool IsLast => CurrentIndex >= _files.Count - 1;

    /// <summary>
    /// 枚举 <paramref name="filePath"/> 所在文件夹下的全部图片，并定位到该文件。
    /// 枚举本身走线程池，避免在网络盘或大目录上卡住 UI。
    /// </summary>
    public static Task<FolderPlaylist> CreateAsync(string filePath, CancellationToken cancellationToken) =>
        Task.Run(() => Create(filePath), cancellationToken);

    /// <summary>同步版本，供测试与已知小目录使用。</summary>
    public static FolderPlaylist Create(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var folder = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrEmpty(folder))
        {
            return new FolderPlaylist(string.Empty, new List<string> { fullPath }, 0);
        }

        List<string> files;

        try
        {
            files = Directory
                .EnumerateFiles(folder)
                .Where(ImageFormatRegistry.IsSupported)
                .ToList();
        }
        catch (Exception)
        {
            // 目录不可读（权限、路径失效）时退化成只有当前这一张
            return new FolderPlaylist(folder, new List<string> { fullPath }, 0);
        }

        files.Sort(NaturalComparer.Instance);

        var index = files.FindIndex(f => string.Equals(f, fullPath, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            // 打开的文件本身不在支持列表里（比如用户强行拖进来），仍让它可见
            files.Insert(0, fullPath);
            index = 0;
        }

        return new FolderPlaylist(folder, files, index);
    }

    /// <summary>从一组路径直接构造，供测试使用。</summary>
    public static FolderPlaylist FromPaths(IEnumerable<string> paths, int currentIndex = 0)
    {
        var files = paths.ToList();
        files.Sort(NaturalComparer.Instance);
        return new FolderPlaylist(string.Empty, files, Math.Clamp(currentIndex, 0, Math.Max(0, files.Count - 1)));
    }

    /// <summary>下一张。已在末尾时返回 false（不循环，避免用户以为翻页失效）。</summary>
    public bool MoveNext()
    {
        if (IsLast)
        {
            return false;
        }

        CurrentIndex++;
        return true;
    }

    /// <summary>上一张。</summary>
    public bool MovePrevious()
    {
        if (IsFirst)
        {
            return false;
        }

        CurrentIndex--;
        return true;
    }

    public bool MoveFirst() => MoveTo(0);

    public bool MoveLast() => MoveTo(_files.Count - 1);

    public bool MoveTo(int index)
    {
        if (index < 0 || index >= _files.Count || index == CurrentIndex)
        {
            return false;
        }

        CurrentIndex = index;
        return true;
    }

    public bool MoveTo(string path)
    {
        var index = _files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && MoveTo(index);
    }

    /// <summary>
    /// 预读候选，按优先级排序：先下一张再上一张，逐圈向外扩。
    /// 用户多数时候往后翻，所以下一张永远优先。
    /// </summary>
    public IEnumerable<string> GetPrefetchCandidates(int radius)
    {
        for (var offset = 1; offset <= radius; offset++)
        {
            var next = CurrentIndex + offset;
            if (next < _files.Count)
            {
                yield return _files[next];
            }

            var previous = CurrentIndex - offset;
            if (previous >= 0)
            {
                yield return _files[previous];
            }
        }
    }

    /// <summary>
    /// 从列表中移除一个文件（删除到回收站之后调用），并把当前位置落到合理的下一张上。
    /// </summary>
    public bool Remove(string path)
    {
        var index = _files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        _files.RemoveAt(index);

        if (_files.Count == 0)
        {
            CurrentIndex = -1;
        }
        else if (index < CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (CurrentIndex >= _files.Count)
        {
            // 删掉的是最后一张，退回新的末尾
            CurrentIndex = _files.Count - 1;
        }

        return true;
    }
}

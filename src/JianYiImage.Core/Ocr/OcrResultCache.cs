namespace JianYiImage.Core.Ocr;

/// <summary>
/// OCR 结果缓存。翻回看过的图时不必重新识别。
///
/// 缓存键包含文件的修改时间与大小——文件被改过就自动失效，
/// 不会拿旧结果去配新内容。
/// </summary>
public sealed class OcrResultCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();
    private readonly int _capacity;

    public OcrResultCache(int capacity = 50)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>
    /// 为文件构造缓存键。文件不可访问时返回 null，调用方应跳过缓存。
    /// </summary>
    public static string? TryBuildKey(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            return string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{path.ToLowerInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}");
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool TryGet(string key, out OcrDocument? document)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                document = node.Value.Document;
                return true;
            }
        }

        document = null;
        return false;
    }

    public void Set(string key, OcrDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }

            var node = _order.AddFirst(new Entry(key, document));
            _map[key] = node;

            while (_map.Count > _capacity && _order.Last is { } last)
            {
                _map.Remove(last.Value.Key);
                _order.RemoveLast();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
        }
    }

    private readonly record struct Entry(string Key, OcrDocument Document);
}

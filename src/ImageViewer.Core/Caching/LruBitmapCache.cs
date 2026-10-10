using ImageViewer.Core.Decoding;

namespace ImageViewer.Core.Caching;

/// <summary>
/// 按内存占用（而非条数）驱逐的 LRU 解码缓存。翻页能否「秒回」靠它。
///
/// 缓存持有 <see cref="DecodedImage"/> 的所有权，驱逐时负责 Dispose；
/// 正在显示的那张可以 Pin 住，避免预读把它挤掉。
/// </summary>
public sealed class LruBitmapCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<CacheEntry> _order = new(); // 头部最新，尾部最旧
    private readonly HashSet<string> _pinned = new(StringComparer.OrdinalIgnoreCase);

    private long _currentBytes;
    private bool _disposed;

    public LruBitmapCache(long maxBytes = 512L * 1024 * 1024)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        MaxBytes = maxBytes;
    }

    public long MaxBytes { get; }

    public long CurrentBytes
    {
        get
        {
            lock (_gate)
            {
                return _currentBytes;
            }
        }
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
    /// 取缓存。命中的条目会被提到最前。
    /// 返回的对象所有权仍属于缓存，调用方不要 Dispose。
    /// </summary>
    public bool TryGet(string key, out DecodedImage? image)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }

        image = null;
        return false;
    }

    /// <summary>
    /// 放入缓存。同 key 已存在时替换旧值并释放它。
    /// 若单张图本身就超过上限，直接不缓存（否则会把其它条目全部挤掉）。
    /// </summary>
    public void Set(string key, DecodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var size = image.ApproximateByteSize;
        DecodedImage? replaced = null;
        var evicted = new List<DecodedImage>();

        lock (_gate)
        {
            if (_disposed)
            {
                image.Dispose();
                return;
            }

            if (_map.TryGetValue(key, out var existing))
            {
                replaced = existing.Value.Image;
                _currentBytes -= existing.Value.Size;
                _order.Remove(existing);
                _map.Remove(key);
            }

            if (size > MaxBytes)
            {
                // 超大单图不进缓存，交给调用方自行持有
                replaced?.Dispose();
                return;
            }

            var node = _order.AddFirst(new CacheEntry(key, image, size));
            _map[key] = node;
            _currentBytes += size;

            EvictIfNeeded(evicted);
        }

        replaced?.Dispose();
        foreach (var item in evicted)
        {
            item.Dispose();
        }
    }

    /// <summary>保护某条目不被驱逐——当前正在显示的图应当 Pin 住。</summary>
    public void Pin(string key)
    {
        lock (_gate)
        {
            _pinned.Add(key);
        }
    }

    public void Unpin(string key)
    {
        var evicted = new List<DecodedImage>();

        lock (_gate)
        {
            _pinned.Remove(key);
            EvictIfNeeded(evicted);
        }

        foreach (var item in evicted)
        {
            item.Dispose();
        }
    }

    public void Remove(string key)
    {
        DecodedImage? removed = null;

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                removed = node.Value.Image;
                _currentBytes -= node.Value.Size;
                _order.Remove(node);
                _map.Remove(key);
                _pinned.Remove(key);
            }
        }

        removed?.Dispose();
    }

    public void Clear()
    {
        List<DecodedImage> all;

        lock (_gate)
        {
            all = _order.Select(e => e.Image).ToList();
            _order.Clear();
            _map.Clear();
            _pinned.Clear();
            _currentBytes = 0;
        }

        foreach (var item in all)
        {
            item.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        Clear();
    }

    /// <summary>从尾部（最旧）开始驱逐，跳过被 Pin 的条目。调用方持锁。</summary>
    private void EvictIfNeeded(List<DecodedImage> evicted)
    {
        var node = _order.Last;

        while (_currentBytes > MaxBytes && node is not null)
        {
            var previous = node.Previous;

            if (!_pinned.Contains(node.Value.Key))
            {
                _currentBytes -= node.Value.Size;
                _map.Remove(node.Value.Key);
                _order.Remove(node);
                evicted.Add(node.Value.Image);
            }

            node = previous;
        }
    }

    private readonly record struct CacheEntry(string Key, DecodedImage Image, long Size);
}

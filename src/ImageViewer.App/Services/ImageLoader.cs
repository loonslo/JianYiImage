using ImageViewer.Core.Caching;
using ImageViewer.Core.Decoding;

namespace ImageViewer.App.Services;

/// <summary>
/// 图片加载服务：解码 + 缓存 + 预读。翻页能否不卡全靠它。
///
/// 解码一律走线程池，主线程永远不碰解码；
/// 当前显示的那张在缓存里被 Pin 住，不会被预读挤掉。
/// </summary>
public sealed class ImageLoader : IDisposable
{
    private readonly IImageDecoder _decoder;
    private readonly LruBitmapCache _cache;
    private readonly SemaphoreSlim _prefetchLimiter = new(2, 2);

    private string? _pinnedKey;
    private CancellationTokenSource? _prefetchCts;

    public ImageLoader(IImageDecoder decoder, long cacheBytes = 512L * 1024 * 1024)
    {
        _decoder = decoder;
        _cache = new LruBitmapCache(cacheBytes);
    }

    public long CacheBytes => _cache.CurrentBytes;

    public int CacheCount => _cache.Count;

    /// <summary>
    /// 加载一张图。命中缓存时立即返回。
    /// 返回的对象所有权属于缓存，调用方不要 Dispose。
    /// </summary>
    public async Task<DecodedImage> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var key = BuildKey(path);

        if (_cache.TryGet(key, out var cached) && cached is not null)
        {
            PinCurrent(key);
            return cached;
        }

        var image = await _decoder.DecodeAsync(path, 0, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        _cache.Set(key, image);
        PinCurrent(key);

        // Set 之后可能因容量被驳回（单图超过上限），此时直接用刚解出来的对象
        return _cache.TryGet(key, out var stored) && stored is not null ? stored : image;
    }

    /// <summary>读取尺寸等信息，不解码像素。</summary>
    public Task<ImageMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken) =>
        _decoder.ReadMetadataAsync(path, cancellationToken);

    /// <summary>
    /// 后台预读接下来可能要看的图。每次调用会取消上一轮尚未完成的预读——
    /// 用户快速连续翻页时，早先的预读目标已经没有意义了。
    /// </summary>
    public void Prefetch(IEnumerable<string> paths)
    {
        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();

        var cts = new CancellationTokenSource();
        _prefetchCts = cts;

        var targets = paths.ToArray();

        _ = Task.Run(async () =>
        {
            foreach (var path in targets)
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                var key = BuildKey(path);
                if (_cache.TryGet(key, out _))
                {
                    continue;
                }

                await _prefetchLimiter.WaitAsync(cts.Token).ConfigureAwait(false);

                try
                {
                    var image = await _decoder.DecodeAsync(path, 0, cts.Token).ConfigureAwait(false);
                    _cache.Set(key, image);
                }
                catch (Exception)
                {
                    // 预读失败无所谓——真正打开时会再试一次并如实报错
                }
                finally
                {
                    _prefetchLimiter.Release();
                }
            }
        }, cts.Token);
    }

    public void InvalidateCache(string path) => _cache.Remove(BuildKey(path));

    private void PinCurrent(string key)
    {
        if (string.Equals(_pinnedKey, key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_pinnedKey is not null)
        {
            _cache.Unpin(_pinnedKey);
        }

        _cache.Pin(key);
        _pinnedKey = key;
    }

    /// <summary>缓存键带上修改时间与大小，文件被改过就自动失效。</summary>
    private static string BuildKey(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                return $"{path.ToLowerInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            }
        }
        catch (Exception)
        {
            // 取不到属性就退化成只用路径
        }

        return path.ToLowerInvariant();
    }

    public void Dispose()
    {
        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        _prefetchLimiter.Dispose();
        _cache.Dispose();
    }
}

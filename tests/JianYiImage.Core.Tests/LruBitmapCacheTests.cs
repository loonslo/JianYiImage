using JianYiImage.Core.Caching;
using JianYiImage.Core.Decoding;
using JianYiImage.Core.Imaging;
using Windows.Graphics.Imaging;

namespace JianYiImage.Core.Tests;

public class LruBitmapCacheTests
{
    /// <summary>构造一张占 width×height×4 字节的图。</summary>
    private static DecodedImage MakeImage(int width, int height)
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        var metadata = new ImageMetadata(
            (uint)width, (uint)height, ExifOrientation.Normal, 1, "TEST", "TEST");

        return new DecodedImage(bitmap, metadata, isPreview: false);
    }

    /// <summary>每张 100×100 的图占 40000 字节。</summary>
    private const long OneImageBytes = 100 * 100 * 4;

    [Fact]
    public void 放入后可以取回()
    {
        using var cache = new LruBitmapCache(OneImageBytes * 4);
        var image = MakeImage(100, 100);

        cache.Set("a", image);

        Assert.True(cache.TryGet("a", out var found));
        Assert.Same(image, found);
        Assert.Equal(1, cache.Count);
        Assert.Equal(OneImageBytes, cache.CurrentBytes);
    }

    [Fact]
    public void 未命中返回空()
    {
        using var cache = new LruBitmapCache(OneImageBytes * 4);

        Assert.False(cache.TryGet("不存在", out var found));
        Assert.Null(found);
    }

    [Fact]
    public void 超出上限时驱逐最久未用的条目()
    {
        // 上限刚好容纳 2 张
        using var cache = new LruBitmapCache(OneImageBytes * 2);

        cache.Set("a", MakeImage(100, 100));
        cache.Set("b", MakeImage(100, 100));
        cache.Set("c", MakeImage(100, 100));

        Assert.False(cache.TryGet("a", out _)); // 最旧的被挤掉
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void 命中会把条目提到最前避免被误驱逐()
    {
        using var cache = new LruBitmapCache(OneImageBytes * 2);

        cache.Set("a", MakeImage(100, 100));
        cache.Set("b", MakeImage(100, 100));

        // 访问 a，使 b 成为最旧
        Assert.True(cache.TryGet("a", out _));

        cache.Set("c", MakeImage(100, 100));

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void 被Pin住的条目不会被驱逐()
    {
        using var cache = new LruBitmapCache(OneImageBytes * 2);

        cache.Set("current", MakeImage(100, 100));
        cache.Pin("current"); // 正在显示的那张

        // 预读塞进来一堆，正常情况下 current 早该被挤掉
        cache.Set("p1", MakeImage(100, 100));
        cache.Set("p2", MakeImage(100, 100));
        cache.Set("p3", MakeImage(100, 100));

        Assert.True(cache.TryGet("current", out _));
    }

    [Fact]
    public void 解除Pin之后恢复可驱逐()
    {
        using var cache = new LruBitmapCache(OneImageBytes * 2);

        cache.Set("current", MakeImage(100, 100));
        cache.Pin("current");
        cache.Set("p1", MakeImage(100, 100));
        cache.Set("p2", MakeImage(100, 100)); // p1 被驱逐，current 因 Pin 保住

        // 此刻未超上限，不该有任何驱逐。
        // 这里刻意用 Count 而不是 TryGet 验证——TryGet 命中会把条目提到 LRU 头部，
        // 那样反而会把 current 保护起来，测不到想测的东西。
        cache.Unpin("current");
        Assert.Equal(2, cache.Count);

        // 再塞一张才会超限；此时 current 不再受保护且是最旧条目，应被驱逐
        cache.Set("p3", MakeImage(100, 100));

        Assert.False(cache.TryGet("current", out _));
        Assert.True(cache.TryGet("p3", out _));
    }

    [Fact]
    public void 同一个键再次放入会替换旧值()
    {
        using var cache = new LruBitmapCache(OneImageBytes * 4);

        cache.Set("a", MakeImage(100, 100));
        var replacement = MakeImage(100, 100);
        cache.Set("a", replacement);

        Assert.Equal(1, cache.Count);
        Assert.Equal(OneImageBytes, cache.CurrentBytes);
        Assert.True(cache.TryGet("a", out var found));
        Assert.Same(replacement, found);
    }

    [Fact]
    public void 单张超过上限的图不进缓存()
    {
        using var cache = new LruBitmapCache(OneImageBytes);

        cache.Set("huge", MakeImage(500, 500)); // 1MB，远超上限

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.CurrentBytes);
    }

    [Fact]
    public void 移除条目后字节数同步减少()
    {
        using var cache = new LruBitmapCache(OneImageBytes * 4);

        cache.Set("a", MakeImage(100, 100));
        cache.Set("b", MakeImage(100, 100));
        cache.Remove("a");

        Assert.Equal(1, cache.Count);
        Assert.Equal(OneImageBytes, cache.CurrentBytes);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void 清空后计数与字节数归零()
    {
        using var cache = new LruBitmapCache(OneImageBytes * 4);

        cache.Set("a", MakeImage(100, 100));
        cache.Set("b", MakeImage(100, 100));
        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.CurrentBytes);
    }

    [Fact]
    public void 键的比较忽略大小写以匹配文件路径语义()
    {
        using var cache = new LruBitmapCache(OneImageBytes * 4);

        cache.Set(@"C:\Photos\A.PNG", MakeImage(100, 100));

        Assert.True(cache.TryGet(@"c:\photos\a.png", out _));
    }

    [Fact]
    public void 上限为非正数时拒绝构造()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruBitmapCache(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruBitmapCache(-1));
    }
}

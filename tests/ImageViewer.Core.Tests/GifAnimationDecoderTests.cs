using System.Runtime.InteropServices.WindowsRuntime;
using ImageViewer.Core.Decoding;
using Windows.Graphics.Imaging;

namespace ImageViewer.Core.Tests;

/// <summary>
/// GIF 动图解码测试。用的是签入仓库的真实 GIF 文件（<c>tools/make-animated-test-gifs.py</c>
/// 生成，逐帧内容和 disposal 设置写在生成脚本的注释里），不是人造的内存数据结构——
/// 这样才能真正测到文件 I/O、WIC 元数据查询字符串、LZW 解码这些环节。
/// </summary>
public class GifAnimationDecoderTests
{
    private static string AssetPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", fileName);

    // ---------- 基础结构：帧数、延迟、子区域合成 ----------

    [Fact]
    public async Task 解码出正确的帧数与每帧延迟()
    {
        using var animation = await GifAnimationDecoder.DecodeAsync(
            AssetPath("anim-region-test.gif"), CancellationToken.None);

        Assert.Equal(3, animation.Frames.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(300), animation.Frames[0].Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(150), animation.Frames[1].Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(220), animation.Frames[2].Delay);
    }

    [Fact]
    public async Task 每帧都合成为完整画布尺寸的位图()
    {
        using var animation = await GifAnimationDecoder.DecodeAsync(
            AssetPath("anim-region-test.gif"), CancellationToken.None);

        // 即便某一帧在文件里只存了一小块子区域，合成结果也必须是完整的 60x40 画布——
        // 这样上层渲染代码才不用关心某一帧原始编码范围有多大
        foreach (var frame in animation.Frames)
        {
            Assert.Equal(60, frame.Bitmap.PixelWidth);
            Assert.Equal(40, frame.Bitmap.PixelHeight);
        }
    }

    [Fact]
    public async Task 循环次数固定为无限循环()
    {
        using var animation = await GifAnimationDecoder.DecodeAsync(
            AssetPath("anim-region-test.gif"), CancellationToken.None);

        // NETSCAPE2.0 循环扩展块的次数目前没有解析（见 GifAnimationDecoder 里的说明），
        // 固定按无限循环处理——这是当前版本的既定行为，不是待修的 bug
        Assert.Equal(0u, animation.LoopCount);
    }

    [Fact]
    public async Task 子区域帧正确定位到画布对应位置()
    {
        using var animation = await GifAnimationDecoder.DecodeAsync(
            AssetPath("anim-region-test.gif"), CancellationToken.None);

        // 帧1只在文件里存了 (10,5)-(29,19) 这一小块蓝色方块（见生成脚本）
        var frame1 = animation.Frames[1].Bitmap;

        AssertPixel(frame1, 15, 10, 30, 30, 220);   // 方块内部：蓝色
        AssertPixel(frame1, 5, 5, 220, 30, 30);     // 方块外部：仍是帧0留下的红色背景
        AssertPixel(frame1, 40, 30, 220, 30, 30);   // 画布另一角：同样是红色背景
    }

    [Fact]
    public async Task 第二帧的绿色方块落在正确位置且不影响其他区域()
    {
        using var animation = await GifAnimationDecoder.DecodeAsync(
            AssetPath("anim-region-test.gif"), CancellationToken.None);

        var frame2 = animation.Frames[2].Bitmap;

        AssertPixel(frame2, 40, 24, 30, 200, 30);  // 绿色方块内部 (35,20)-(49,29)
        AssertPixel(frame2, 5, 5, 220, 30, 30);    // 画布左上角仍是红色
    }

    // ---------- disposal=RestoreToBackground：最容易做错的部分 ----------

    [Fact]
    public async Task RestoreToBackground会把区域清成透明而不是留下残影()
    {
        using var animation = await GifAnimationDecoder.DecodeAsync(
            AssetPath("anim-disposal-test.gif"), CancellationToken.None);

        Assert.Equal(3, animation.Frames.Count);

        // 帧1：蓝色方块正常显示
        AssertPixel(animation.Frames[1].Bitmap, 15, 10, 30, 30, 220);

        // 帧2 本身在 (10,5)-(29,19) 这个位置是透明的（它只画了别处的一块绿色标记）。
        // disposal=2「恢复为背景」按 GIF89a 规范和主流播放器的一致实现，指的是清成
        // 透明（背景色），而不是「露出更早一帧的内容」——那是 disposal=3 的语义。
        // 所以这个位置正确的结果是完全透明，不是帧0的红色：
        // 没做 disposal 的错误实现会在这里残留蓝色，那才是要防的 bug。
        AssertTransparent(animation.Frames[2].Bitmap, 15, 10);

        // 帧2 自己画的绿色标记要正常显示
        AssertPixel(animation.Frames[2].Bitmap, 38, 23, 30, 200, 30);
    }

    [Fact]
    public async Task disposal不影响没有画东西的区域()
    {
        using var animation = await GifAnimationDecoder.DecodeAsync(
            AssetPath("anim-disposal-test.gif"), CancellationToken.None);

        // 画布上从未被任何帧覆盖的角落，三帧里应该始终是最初的红色背景
        foreach (var frame in animation.Frames)
        {
            AssertPixel(frame.Bitmap, 2, 2, 220, 30, 30);
        }
    }

    // ---------- 取消 ----------

    [Fact]
    public async Task 取消令牌能中断解码()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // WinRT 的 IAsyncOperation.AsTask(token) 抛的是 TaskCanceledException——
        // 它是 OperationCanceledException 的子类，所以用 ThrowsAny（按「是不是」匹配）
        // 而不是 Throws（要求类型完全相等）
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GifAnimationDecoder.DecodeAsync(AssetPath("anim-region-test.gif"), cts.Token));
    }

    // ---------- 像素断言 helper ----------

    /// <summary>
    /// 读取 BGRA8 premultiplied 位图在 (x,y) 处的颜色，和期望 RGB 比对。
    /// 允许 ±6 的容差——GIF 全局调色板量化偶尔会有极小的色差，不是解码器的问题。
    /// </summary>
    private static void AssertPixel(SoftwareBitmap bitmap, int x, int y, byte r, byte g, byte b)
    {
        var buffer = new Windows.Storage.Streams.Buffer((uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4));
        bitmap.CopyToBuffer(buffer);
        var bytes = buffer.ToArray();
        var stride = bitmap.PixelWidth * 4;
        var offset = y * stride + x * 4;

        var actualB = bytes[offset];
        var actualG = bytes[offset + 1];
        var actualR = bytes[offset + 2];

        const int tolerance = 6;

        Assert.True(Math.Abs(actualR - r) <= tolerance && Math.Abs(actualG - g) <= tolerance && Math.Abs(actualB - b) <= tolerance,
            $"像素 ({x},{y}) 期望 RGB≈({r},{g},{b})，实际是 ({actualR},{actualG},{actualB})");
    }

    /// <summary>断言 (x,y) 处的 alpha 通道为 0——直接检查透明度本身，而不是间接推断。</summary>
    private static void AssertTransparent(SoftwareBitmap bitmap, int x, int y)
    {
        var buffer = new Windows.Storage.Streams.Buffer((uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4));
        bitmap.CopyToBuffer(buffer);
        var bytes = buffer.ToArray();
        var stride = bitmap.PixelWidth * 4;
        var offset = y * stride + x * 4;

        Assert.Equal(0, bytes[offset + 3]);
    }
}

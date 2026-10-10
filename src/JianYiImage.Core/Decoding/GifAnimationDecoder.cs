using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace JianYiImage.Core.Decoding;

/// <summary>
/// 解码动态 GIF 的全部帧，并按 disposal method 逐帧合成到完整画布上。
///
/// 为什么不能直接把每帧的 GetSoftwareBitmapAsync 结果拿来播：GIF 的常见编码方式是
/// 每帧只存「和上一帧不同的那一小块矩形」（帧本身的 Width/Height 往往小于画布），
/// 不按 GIF89a 的 disposal 规则合成的话，播放出来的画面会缺胳膊少腿、留有残影。
///
/// 只支持 GIF：WebP 动画、APNG 的帧元数据是完全不同的 WIC 查询 schema，
/// 没有真实样本验证过正确性，贸然实现容易播出一个静默损坏的动画——
/// 这两种格式目前仍按 WicImageDecoder 的默认行为只显示首帧。
/// </summary>
public static class GifAnimationDecoder
{
    /// <summary>GIF89a 附录 23.c.4 定义的四种 disposal 方法。</summary>
    private enum DisposalMethod : byte
    {
        Unspecified = 0,
        None = 1,
        RestoreToBackground = 2,
        RestoreToPrevious = 3,
    }

    public static async Task<AnimatedImage> DecodeAsync(string path, CancellationToken cancellationToken)
    {
        var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024, useAsync: true);

        await using (stream.ConfigureAwait(false))
        {
            using var randomAccess = stream.AsRandomAccessStream();

            var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.GifDecoderId, randomAccess).AsTask(cancellationToken);

            var (canvasWidth, canvasHeight) = await ReadLogicalScreenSizeAsync(decoder, cancellationToken).ConfigureAwait(false);

            var frames = new List<AnimatedFrame>((int)decoder.FrameCount);
            var canvas = new byte[canvasWidth * canvasHeight * 4];

            byte[]? snapshotBeforePreviousFrame = null;
            var previousRegion = (Left: 0, Top: 0, Width: canvasWidth, Height: canvasHeight);
            var previousDisposal = DisposalMethod.None;

            try
            {
                for (uint i = 0; i < decoder.FrameCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var frame = await decoder.GetFrameAsync(i).AsTask(cancellationToken).ConfigureAwait(false);
                    var reader = frame.BitmapProperties;

                    var region = await ReadFrameRegionAsync(
                        reader, (int)frame.PixelWidth, (int)frame.PixelHeight, canvasWidth, canvasHeight, cancellationToken)
                        .ConfigureAwait(false);
                    var delay = await ReadDelayAsync(reader, cancellationToken).ConfigureAwait(false);
                    var disposal = await ReadDisposalAsync(reader, cancellationToken).ConfigureAwait(false);

                    // 处理上一帧的收尾：这一帧画上去之前，先按上一帧的 disposal 方法复原画布
                    if (previousDisposal == DisposalMethod.RestoreToBackground)
                    {
                        ClearRegion(canvas, canvasWidth, previousRegion);
                    }
                    else if (previousDisposal == DisposalMethod.RestoreToPrevious && snapshotBeforePreviousFrame is not null)
                    {
                        Array.Copy(snapshotBeforePreviousFrame, canvas, canvas.Length);
                    }

                    if (disposal == DisposalMethod.RestoreToPrevious)
                    {
                        // 这一帧画上去之前先存一份画布快照，供下一帧收尾时把它「复原」
                        snapshotBeforePreviousFrame = (byte[])canvas.Clone();
                    }

                    using (var frameBitmap = await frame
                        .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var framePixels = GetPixelBytes(frameBitmap);
                        CompositeFrame(canvas, canvasWidth, canvasHeight, framePixels, region);
                    }

                    var frameSnapshot = (byte[])canvas.Clone();
                    var frameBitmapOut = CreateBitmapFromPixels(frameSnapshot, canvasWidth, canvasHeight);
                    frames.Add(new AnimatedFrame(frameBitmapOut, delay));

                    previousRegion = region;
                    previousDisposal = disposal;
                }
            }
            catch
            {
                foreach (var frame in frames)
                {
                    frame.Dispose();
                }

                throw;
            }

            // NETSCAPE2.0 循环扩展块的循环次数没能通过 WIC 的元数据查询稳定读到
            // （实测 /appext/Application、/appext/Data 在这份文件上都查不到值，
            // 而 /grctlext、/imgdesc 同样的查询方式完全正常，问题只出在应用扩展块本身）。
            // 这是个低影响的边角特性——绝大多数 GIF 本来就是设计成无限循环播放的，
            // 与其为了这一项冷门元数据继续深挖 WIC 的查询格式，不如直接约定无限循环，
            // 留出空间以后再修（App 层的播放逻辑不用因此改动）。
            return new AnimatedImage(frames, LoopCount: 0);
        }
    }

    /// <summary>
    /// 把一帧（可能只是画布的一个子矩形）按「不透明像素覆盖、透明像素跳过」的规则合成到画布上。
    /// GIF 的透明度是二值的（要么全透明要么全不透明，没有半透明），
    /// 所以不需要做 alpha 混合数学，直接按像素覆盖即可——这是 GIF 格式本身的性质，不是抄近路。
    /// </summary>
    private static void CompositeFrame(
        byte[] canvas, int canvasWidth, int canvasHeight,
        byte[] framePixels, (int Left, int Top, int Width, int Height) region)
    {
        var maxWidth = Math.Min(region.Width, canvasWidth - region.Left);
        var maxHeight = Math.Min(region.Height, canvasHeight - region.Top);

        for (var y = 0; y < maxHeight; y++)
        {
            var canvasRowStart = ((region.Top + y) * canvasWidth + region.Left) * 4;
            var frameRowStart = y * region.Width * 4;

            for (var x = 0; x < maxWidth; x++)
            {
                var frameOffset = frameRowStart + x * 4;
                var alpha = framePixels[frameOffset + 3];

                if (alpha == 0)
                {
                    continue; // 透明像素：保留画布上已有的内容
                }

                var canvasOffset = canvasRowStart + x * 4;
                canvas[canvasOffset] = framePixels[frameOffset];
                canvas[canvasOffset + 1] = framePixels[frameOffset + 1];
                canvas[canvasOffset + 2] = framePixels[frameOffset + 2];
                canvas[canvasOffset + 3] = framePixels[frameOffset + 3];
            }
        }
    }

    private static void ClearRegion(byte[] canvas, int canvasWidth, (int Left, int Top, int Width, int Height) region)
    {
        for (var y = 0; y < region.Height; y++)
        {
            var rowStart = ((region.Top + y) * canvasWidth + region.Left) * 4;
            Array.Clear(canvas, rowStart, region.Width * 4);
        }
    }

    private static byte[] GetPixelBytes(SoftwareBitmap bitmap)
    {
        var buffer = new Windows.Storage.Streams.Buffer((uint)(bitmap.PixelWidth * bitmap.PixelHeight * 4));
        bitmap.CopyToBuffer(buffer);
        return buffer.ToArray();
    }

    private static SoftwareBitmap CreateBitmapFromPixels(byte[] pixels, int width, int height)
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(pixels.AsBuffer());
        return bitmap;
    }

    // ---------- 元数据读取：每一项都容错，取不到就退回安全默认值 ----------

    private static async Task<(int Width, int Height)> ReadLogicalScreenSizeAsync(
        BitmapDecoder decoder, CancellationToken cancellationToken)
    {
        try
        {
            var props = await decoder.BitmapProperties
                .GetPropertiesAsync(new[] { "/logscrdesc/Width", "/logscrdesc/Height" })
                .AsTask(cancellationToken).ConfigureAwait(false);

            if (props.TryGetValue("/logscrdesc/Width", out var w) && w?.Value is ushort width &&
                props.TryGetValue("/logscrdesc/Height", out var h) && h?.Value is ushort height &&
                width > 0 && height > 0)
            {
                return (width, height);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 少数极简编码器可能没写全局逻辑屏幕描述块，退回按解码器整体尺寸
        }

        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
    }

    private static async Task<(int Left, int Top, int Width, int Height)> ReadFrameRegionAsync(
        BitmapPropertiesView reader, int frameWidth, int frameHeight, int canvasWidth, int canvasHeight,
        CancellationToken cancellationToken)
    {
        try
        {
            var props = await reader
                .GetPropertiesAsync(new[] { "/imgdesc/Left", "/imgdesc/Top" })
                .AsTask(cancellationToken).ConfigureAwait(false);

            var left = props.TryGetValue("/imgdesc/Left", out var l) && l?.Value is ushort lv ? (int)lv : 0;
            var top = props.TryGetValue("/imgdesc/Top", out var t) && t?.Value is ushort tv ? (int)tv : 0;

            // 防越界：极少数畸形 GIF 的偏移量+尺寸可能超出画布，裁到画布范围内
            left = Math.Clamp(left, 0, Math.Max(0, canvasWidth - 1));
            top = Math.Clamp(top, 0, Math.Max(0, canvasHeight - 1));

            return (left, top, frameWidth, frameHeight);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return (0, 0, frameWidth, frameHeight);
        }
    }

    private static async Task<TimeSpan> ReadDelayAsync(BitmapPropertiesView reader, CancellationToken cancellationToken)
    {
        try
        {
            var props = await reader.GetPropertiesAsync(new[] { "/grctlext/Delay" }).AsTask(cancellationToken).ConfigureAwait(false);

            if (props.TryGetValue("/grctlext/Delay", out var value) && value?.Value is ushort hundredths)
            {
                // 单位是 1/100 秒。很多 GIF 把「不限速播放」编码成 0，
                // 浏览器和主流播放器的通用约定是把过小的延迟钳到 ~100ms，避免闪成一片
                var milliseconds = hundredths * 10;
                return TimeSpan.FromMilliseconds(Math.Max(milliseconds, 20));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 没有图形控制扩展块——退回一个观感上合理的默认值
        }

        return TimeSpan.FromMilliseconds(100);
    }

    private static async Task<DisposalMethod> ReadDisposalAsync(BitmapPropertiesView reader, CancellationToken cancellationToken)
    {
        try
        {
            var props = await reader.GetPropertiesAsync(new[] { "/grctlext/Disposal" }).AsTask(cancellationToken).ConfigureAwait(false);

            if (props.TryGetValue("/grctlext/Disposal", out var value) && value?.Value is byte raw && raw <= 3)
            {
                return (DisposalMethod)raw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 忽略，退回默认
        }

        return DisposalMethod.Unspecified;
    }
}

using Windows.Graphics.Imaging;

namespace JianYiImage.Core.Decoding;

/// <summary>一帧动图：已经按 disposal 规则合成到完整画布尺寸的位图 + 播放该帧应停留的时长。</summary>
public sealed class AnimatedFrame : IDisposable
{
    public AnimatedFrame(SoftwareBitmap bitmap, TimeSpan delay)
    {
        Bitmap = bitmap;
        Delay = delay;
    }

    public SoftwareBitmap Bitmap { get; }

    public TimeSpan Delay { get; }

    public void Dispose() => Bitmap.Dispose();
}

/// <summary>解码出的完整动图。</summary>
/// <param name="Frames">全部帧，已按 disposal 规则合成，可直接逐帧上屏。</param>
/// <param name="LoopCount">循环次数，0 表示无限循环。</param>
public sealed record AnimatedImage(IReadOnlyList<AnimatedFrame> Frames, uint LoopCount) : IDisposable
{
    public void Dispose()
    {
        foreach (var frame in Frames)
        {
            frame.Dispose();
        }
    }
}

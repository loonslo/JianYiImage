using ImageViewer.Core.Decoding;
using ImageViewer.Core.Ocr;

namespace ImageViewer.App.Services;

/// <summary>
/// OCR 协调服务：缓存查找、尺寸适配、调用引擎。
/// 防抖与取消由调用方（窗口）控制——那里才知道用户是不是还在连续翻页。
/// </summary>
public sealed class OcrService
{
    private readonly IOcrEngine? _engine;
    private readonly IImageDecoder _decoder;
    private readonly OcrResultCache _cache = new();

    public OcrService(IOcrEngine? engine, IImageDecoder decoder)
    {
        _engine = engine;
        _decoder = decoder;
    }

    public bool IsAvailable => _engine is not null;

    public OcrLanguageInfo? Language => _engine?.Language;

    public string EngineName => _engine?.Name ?? "不可用";

    /// <summary>
    /// 识别一张图。命中缓存时立即返回。
    /// 引擎不可用时返回空文档，调用方据此提示「系统未安装 OCR 语言包」。
    /// </summary>
    public async Task<OcrDocument> RecognizeAsync(
        string path,
        DecodedImage image,
        CancellationToken cancellationToken)
    {
        if (_engine is null)
        {
            return OcrDocument.Empty;
        }

        var cacheKey = OcrResultCache.TryBuildKey(path);

        if (cacheKey is not null && _cache.TryGet(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var document = await RecognizeCoreAsync(path, image, cancellationToken).ConfigureAwait(false);

        if (cacheKey is not null)
        {
            _cache.Set(cacheKey, document);
        }

        return document;
    }

    private async Task<OcrDocument> RecognizeCoreAsync(
        string path,
        DecodedImage image,
        CancellationToken cancellationToken)
    {
        var maxDimension = _engine!.MaxImageDimension;
        var longestEdge = Math.Max(image.PixelWidth, image.PixelHeight);

        // 绝大多数图片都在引擎上限（实测 10000px）之内，直接拿已解码的位图去识别
        if (longestEdge <= maxDimension)
        {
            return await _engine
                .RecognizeAsync(image.Bitmap, 1.0, cancellationToken)
                .ConfigureAwait(false);
        }

        // 超长截图之类：让解码器出一份不超过上限的版本（WIC 降采样，硬件加速），
        // 再把识别出的坐标按倍率换算回原图坐标系
        using var downscaled = await _decoder
            .DecodeAsync(path, maxDimension, cancellationToken)
            .ConfigureAwait(false);

        var downscaledLongestEdge = Math.Max(downscaled.PixelWidth, downscaled.PixelHeight);
        var scaleToOriginal = downscaledLongestEdge <= 0
            ? 1.0
            : (double)longestEdge / downscaledLongestEdge;

        return await _engine
            .RecognizeAsync(downscaled.Bitmap, scaleToOriginal, cancellationToken)
            .ConfigureAwait(false);
    }

    public void ClearCache() => _cache.Clear();
}

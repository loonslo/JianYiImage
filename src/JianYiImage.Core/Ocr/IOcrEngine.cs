using Windows.Graphics.Imaging;

namespace JianYiImage.Core.Ocr;

/// <summary>可用的识别语言。</summary>
/// <param name="LanguageTag">BCP-47 标记，如 zh-Hans-CN。</param>
/// <param name="DisplayName">用于显示的语言名。</param>
public readonly record struct OcrLanguageInfo(string LanguageTag, string DisplayName);

/// <summary>
/// OCR 引擎抽象。做成可插拔是因为系统 OCR 的语言包取决于用户装了什么——
/// 本机目前只有 zh-Hans-CN，用户之后装上英文包就会自动多出一个选项。
/// </summary>
public interface IOcrEngine
{
    /// <summary>引擎名，显示在诊断信息里。</summary>
    string Name { get; }

    /// <summary>当前使用的识别语言。</summary>
    OcrLanguageInfo Language { get; }

    /// <summary>
    /// 单次识别所能接受的最大边长。超过就必须先降采样，
    /// 否则引擎会直接报错。实测本机为 10000。
    /// </summary>
    int MaxImageDimension { get; }

    /// <summary>
    /// 识别一张已解码的位图。
    /// </summary>
    /// <param name="bitmap">BGRA8 位图。边长不得超过 <see cref="MaxImageDimension"/>。</param>
    /// <param name="scaleToOriginal">
    /// 从该位图坐标换算回原图坐标的倍率。
    /// 传入的若是为了满足尺寸上限而降采样过的图，这里就是放大倍率；
    /// 直接用原图识别时传 1。
    /// </param>
    /// <param name="cancellationToken">翻页时取消。</param>
    Task<OcrDocument> RecognizeAsync(
        SoftwareBitmap bitmap,
        double scaleToOriginal,
        CancellationToken cancellationToken);
}

"""生成 tests/ImageViewer.Core.Tests/TestAssets/ 下的两个动图测试样本。

这两个文件是签入仓库的二进制测试资产，本脚本记录它们是怎么来的，
方便日后需要改测试场景时重新生成，而不是留一个不知来历的 .gif。

依赖：pip install pillow
用法：python tools/make-animated-test-gifs.py
"""

from pathlib import Path

from PIL import Image, ImageDraw

OUTPUT_DIR = Path(__file__).resolve().parent.parent / "tests" / "ImageViewer.Core.Tests" / "TestAssets"

W, H = 60, 40
RED, BLUE, GREEN, MAGENTA = (220, 30, 30), (30, 30, 220), (30, 200, 30), (255, 0, 255)


def make_solid(size: tuple[int, int], fill: tuple[int, int, int]) -> Image.Image:
    return Image.new("RGB", size, fill)


def make_region_test() -> None:
    """验证：多帧解码、帧延迟、以及「帧只是画布一角」的子区域合成。

    帧 1 的变化区域只有 (10,5)-(29,19) 这一小块蓝色方块，
    Pillow 的差分优化会把它编码成一个真正的子区域帧（而不是整幅重绘），
    这样才能测到 GifAnimationDecoder 里 /imgdesc/Left,Top 的读取和局部合成逻辑。
    """
    frame0 = make_solid((W, H), RED)

    frame1 = make_solid((W, H), RED)
    ImageDraw.Draw(frame1).rectangle([10, 5, 29, 19], fill=BLUE)

    frame2 = make_solid((W, H), RED)
    ImageDraw.Draw(frame2).rectangle([35, 20, 49, 29], fill=GREEN)

    frame0.save(
        OUTPUT_DIR / "anim-region-test.gif",
        save_all=True,
        append_images=[frame1, frame2],
        duration=[300, 150, 220],
        loop=0,
        disposal=[1, 2, 1],
        optimize=True,
    )


def make_disposal_test() -> None:
    """验证：disposal=2（RestoreToBackground）是否真的把区域清成透明。

    调色板用「P」模式手工指定，索引 3 标记为透明色：
      帧0：整幅红色，disposal=None
      帧1：(10,5)-(29,19) 蓝色方块，disposal=RestoreToBackground
      帧2：几乎整幅透明，只在 (35,20)-(42,27) 画一块绿色标记

    帧2 本身不覆盖 (10,5)-(29,19) 这块区域（它在那里是透明的），
    所以合成结果在那个位置显示什么完全取决于「帧1的蓝色块有没有被正确清除」：
      - 清除正确 → 透出帧0的红色
      - 没实现 disposal → 残留蓝色（错误）
    """
    palette = [RED, BLUE, GREEN, MAGENTA] + [(0, 0, 0)] * 252

    def make_p(fill_index: int) -> Image.Image:
        im = Image.new("P", (W, H), fill_index)
        flat: list[int] = []
        for c in palette:
            flat.extend(c)
        im.putpalette(flat)
        return im

    frame0 = make_p(0)  # 索引 0 = 红，整幅

    frame1 = make_p(0)
    ImageDraw.Draw(frame1).rectangle([10, 5, 29, 19], fill=1)  # 索引 1 = 蓝

    frame2 = make_p(3)  # 索引 3 = 透明，先铺满整幅
    ImageDraw.Draw(frame2).rectangle([35, 20, 42, 27], fill=2)  # 索引 2 = 绿，画一块标记

    frame0.save(
        OUTPUT_DIR / "anim-disposal-test.gif",
        save_all=True,
        append_images=[frame1, frame2],
        duration=[300, 150, 220],
        loop=0,
        disposal=[1, 2, 1],
        transparency=3,
        optimize=False,  # 关掉自动差分，确保帧2按我给定的像素（而非它自己猜的 diff）编码
    )


if __name__ == "__main__":
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    make_region_test()
    make_disposal_test()
    print(f"已生成到 {OUTPUT_DIR}")

"""Check actual XAML theme colors using WCAG relative luminance."""

from pathlib import Path
import sys
import xml.etree.ElementTree as ET

XAML_KEY = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"
THEME = Path(__file__).resolve().parents[1] / "src/JianYiImage.App/Themes/AppTheme.xaml"


def rgb(value: str) -> tuple[float, float, float]:
    value = value.strip().removeprefix("#")
    if len(value) == 8:
        if value[:2] != "FF":
            raise ValueError("Contrast checks require opaque colors")
        value = value[2:]
    if len(value) != 6:
        raise ValueError("Expected RGB or ARGB color")
    return tuple(int(value[i:i + 2], 16) / 255 for i in (0, 2, 4))


def luminance(color: tuple[float, float, float]) -> float:
    values = [v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in color]
    return sum(v * weight for v, weight in zip(values, (0.2126, 0.7152, 0.0722), strict=True))


def main() -> int:
    colors = {node.attrib[XAML_KEY]: node.text for node in ET.parse(THEME).getroot()
              if node.tag.endswith("}Color")}
    checks = [
        ("AppTextPrimaryColor", "AppChromeColor", 4.5),
        ("AppTextSecondaryColor", "AppChromeColor", 4.5),
        ("AppTextTertiaryColor", "AppChromeColor", 4.5),
        ("AppTextTertiaryColor", "AppCanvasColor", 4.5),
        ("AppAccentColor", "AppChromeColor", 4.5),
        ("AppAccentColor", "AppChromeColor", 3.0),
        ("AppTextSecondaryColor", "AppChromeColor", 3.0),
        ("#FFFFFF", "AppAccentFillColor", 4.5),
    ]
    success = True
    for foreground, background, minimum in checks:
        light, dark = sorted((luminance(rgb(colors.get(foreground, foreground))),
                              luminance(rgb(colors.get(background, background)))), reverse=True)
        ratio = (light + 0.05) / (dark + 0.05)
        passed = ratio >= minimum
        success &= passed
        print(f'{"PASS" if passed else "FAIL"} {foreground} / {background}: {ratio:.2f} >= {minimum:.1f}')
    return 0 if success else 1


if __name__ == "__main__":
    sys.exit(main())

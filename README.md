# 简壹图片浏览器

带「实况文本」的 Windows 看图器：打开快、缩放清晰、格式覆盖广，**图里的文字可以像网页文字一样用鼠标划选**，也可以通过顶部的“提取文字”按钮在本机提取后复制。

应用窗口和程序产品信息显示为 **简壹图片浏览器**；系统“打开方式”及资源管理器右键菜单显示为 **简壹图片**。图标为叠放的卡通拍立得照片（蓝天、太阳、青山和一颗小爱心），包含 16–256 像素的多尺寸 Windows ICO，24 像素及以下用专门的简化版；内部可执行文件名和关联 ProgID 保持兼容。

## 下载

到 [Releases](https://github.com/loonslo/ImageViewer/releases) 下载最新的 `ImageViewer-vX.Y.Z-win-x64.zip`，**解压整个文件夹**后运行 `ImageViewer\ImageViewer.exe`。免安装、自包含，不需要另外安装 .NET 或 Windows App SDK。

程序没有代码签名，首次运行可能出现“Windows 已保护你的电脑”：点“更多信息”→“仍要运行”；也可以在解压前右键 zip → 属性 → 勾选“解除锁定”。想从源码构建，见下面的“快速开始”。

## 快速开始

需要 .NET SDK 10。在项目根目录执行；如果 `dotnet` 不在 PATH 中（例如 SDK 装在 `$LOCALAPPDATA/Microsoft/dotnet` 下），请改用 SDK 的完整路径：

```bash
dotnet build src/ImageViewer.App/ImageViewer.App.csproj
```

生成测试图片（含动图测试样本，见 `tools/make-animated-test-gifs.py`）：

```bash
powershell -ExecutionPolicy Bypass -File tools/make-test-images.ps1
```

运行（可直接把图片路径作为参数传入）：

```bash
src/ImageViewer.App/bin/Debug/net10.0-windows10.0.26100.0/win-x64/ImageViewer.exe test-images/sample-2.png
```

跑测试：

```bash
dotnet test tests/ImageViewer.Core.Tests/ImageViewer.Core.Tests.csproj
```

## 发布免安装版本

```bash
dotnet publish src/ImageViewer.App/ImageViewer.App.csproj -c Release -r win-x64 --self-contained true
```

产物在 `src/ImageViewer.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/`，把整个 `publish` 目录拷到任何地方，双击 `ImageViewer.exe` 即可运行，不需要安装 .NET 或 Windows App SDK。若旧的发布目录双击后窗口立即消失，请重新执行这条 publish 命令，不要只替换单个 exe；Windows App SDK 的原生运行时文件必须和 exe 来自同一次发布。启动耗时受机器负载和系统缓存影响，不保证固定毫秒数；可用 `tools/startup-time.ps1 -Exe <发布目录>/ImageViewer.exe` 重跑；它测量保留系统缓存的新进程到窗口可见时间，不是严格冷启动。

体积约 280MB，主要是 Windows App SDK 自带运行时（WinUI、Win2D、WebView2 等）—— 这是自包含发布的固有代价，换来的是目标机器零依赖。

**踩过的坑**：`ImageViewer.App.csproj` 里 `EnableMsixTooling` 必须是 `true`，即便本应用完全不产出 MSIX 包。这是 Windows App SDK 2.5.1 的一个已知问题——`EnableMsixTooling=false` 时 `dotnet publish`（`dotnet build` 不受影响）产出的目录里 WindowsAppRuntime 的原生引导资源会缺失，启动直接崩溃（退出码 `0xC000027B` / `STATUS_ENTRYPOINT_NOT_FOUND`，故障模块 `Microsoft.ui.xaml.dll`）。本机已实测验证两种状态的行为差异，csproj 里留了完整说明，不要"优化"回 false。

### 文件关联

打开应用后，点击右上角 **`⋯`（更多操作）** 菜单：

- **设为默认图片浏览器…**：注册简壹图片浏览器并打开 Windows 默认应用设置。在系统设置里选择简壹图片浏览器；也可以右键图片→打开方式→选择其他应用→简壹图片浏览器→始终使用。Windows 10 使用普通默认应用页，新版 Windows 11 支持定位到本应用。最终默认选择由 Windows 确认。
- **添加右键打开 / 打开方式**：为受支持的图片添加资源管理器 **“用简壹图片打开”** 菜单，同时以“简壹图片”名称加入系统“打开方式”列表，无需管理员权限。Windows 11 的传统菜单项可能在“显示更多选项”里。
- **移除简壹图片浏览器文件关联**：移除本应用注册的菜单和候选，保留其他图片软件的关联。

旧版本注册过的用户，可从新版再次注册以更新显示名称和图标；关联 ProgID 保持原值。

请从准备长期保留的发布目录进行注册；如果移动了程序目录，在新位置重新注册即可更新路径。

```bash
# 注册默认应用候选、资源管理器右键菜单与「打开方式」列表
"<发布目录>/ImageViewer.exe" --register-file-associations

# 撤销本应用注册的键和值，保留共享键与其他应用
"<发布目录>/ImageViewer.exe" --unregister-file-associations
```

同样的操作也能在图片的应用内右键菜单里找到。注册只写当前用户的应用能力、ProgID、打开方式候选及本应用的菜单，不改写已有默认选择。隔离注册表测试验证了重复注册、程序路径更新、撤销及保留其他应用条目。

## 操作方式

| 操作 | 效果 |
|---|---|
| 顶部“上一张” / “下一张”图标按钮 | 自动扫描打开图片所在目录的受支持图片并自然排序；首张/末张禁用对应按钮，不循环；不扫描子目录 |
| 图片中部左右圆形导航按钮 | 与顶部切图按钮共用列表；多图目录显示，首尾箭头变淡且禁用 |
| 顶部缩放百分比 | 随滚轮、缩放按钮、快捷键、适应窗口和切图同步更新；点击恢复 100% |
| 鼠标划过文字 | 逐词选中，支持跨行连选 |
| `Ctrl+C` | 选中文字→复制文字；框选了区域→复制该区域图片；否则→复制整张图 |
| `Ctrl+A` | 选中本图全部文字 |
| `Shift+拖动` | 框选一块矩形区域，复制为图片（文字识别不准或没有文字时的兜底） |
| `Esc` | 取消文字选择 / 取消区域选择 |
| 左键拖动（非文字处） | 平移图片 |
| 滚轮 | 以鼠标位置为锚点缩放 |
| 双击文字 / 双击空白 | 选中该词 / 在适应窗口与 100% 间切换 |
| 右键 | 上下文菜单（复制、搜索、翻译、旋转、删除、文件关联…按当前状态动态生成） |
| `←` `→` `Home` `End` | 同文件夹内翻页（自然排序，`img2` 在 `img10` 之前） |
| `Ctrl+0` / `Ctrl+1` | 100% / 适应窗口 |
| `Ctrl+±` | 放大 / 缩小 |
| `R` / `Shift+R` | 顺时针 / 逆时针旋转 90°（仅影响显示与复制，不改磁盘文件） |
| 顶部“提取文字” | 本地提取图片文字并打开右侧停靠面板；面板打开时再次点击会收起 |
| 窄窗口展开文字面板 | 内容区低于 800 DIP 时面板改为底部停靠，保留完整图片宽度；放宽窗口后恢复右侧 320px |
| 焦点在文字面板文本框 | 方向键、Home/End、Ctrl+A/C 属于文本操作，Delete/R 不触发图片删除或旋转；Ctrl+O、F11 仍为窗口命令 |
| 文字面板右上角“×” | 关闭面板并恢复图片区域宽度，保留识别结果和后台 OCR；再次点击“提取文字”可打开 |
| 顶部“复制文字”图标按钮 / 面板“复制全部” | 复制当前图片的全部可提取文字 |
| `F11` | 全屏 |
| `Delete` | 删除到回收站（可恢复，不是永久删除） |
| `Ctrl+O` | 打开文件 |

鼠标悬停在文字上时光标变 I-beam（选字），其余位置变成移动光标（拖图），按住 Shift 变十字线（框选区域）——**不需要切换模式**。

缩放低于 100% 时会保证整张图片完整显示；如果当前比例仍大于画布可容纳的比例，会自动缩到适应画布。适应后还可以继续缩小。

GIF 动图自动检测并播放；文字识别、旋转、区域选区在播放动图时不可用（对着一张不停变化的图做这些没有意义）。

## 架构

```
src/ImageViewer.Core/     无 UI 依赖的纯逻辑，全部可单测
  Decoding/               WIC 解码、GIF 动图解码（disposal 合成）、格式清单、元数据
  Ocr/                    OCR 引擎、选择模型、结果缓存
  Rendering/              缩放平移状态机、插值策略、旋转矩阵
  Navigation/             文件夹翻页、自然排序
  Caching/                按内存量驱逐的 LRU 解码缓存
  Imaging/                EXIF 方向

src/ImageViewer.App/      WinUI 3 外壳
  Controls/ImageCanvas    Win2D 渲染 + 输入处理 + 动图播放定时器
  Controls/BrandMark      标题栏与空状态共用的矢量品牌标（由 tools/build-app-icon.py 生成，标题栏用简化版）
  Themes/                 固定深色颜色令牌与工具栏/面板按钮样式
  Services/               加载、OCR 协调、剪贴板、文件系统、文件关联、网页搜索/翻译

tests/                    194 个测试（2026-10-01 实测），含缩放/布局回归、GIF 二进制样本、同目录扫描与隔离文件关联注册/名称升级测试
tools/                    OCR 探针、测试图生成脚本、动图测试样本生成脚本、图标生成脚本
  capture-window.ps1      窗口 PNG 取证（输出到 TEMP），支持 Attach、文字面板与 UIA 按钮属性转储
  final-uia.ps1           可重跑的面板/窄窗口/标题/无障碍名称/溢出/系统窗口状态回归
  input-regression.ps1    对生成图的独立副本注入真实按键，验证文本焦点保护及损坏图片失败/恢复
  startup-time.ps1        三次新进程到可见窗口计时（保留系统缓存）
  check-theme-contrast.py  解析 XAML 颜色令牌并检查 WCAG 对比度
  build-app-icon.py       由一份矢量定义生成 Jianyi.svg / .png / .ico 与 BrandMark.xaml（仅需 Pillow；--check 校验产物是否过期）
```

核心原则：**算法全部放 Core，App 只做渲染和输入**。所以「鼠标从 A 拖到 B 会复制出什么文字」「旋转 90° 后某个点落在哪」这些都能脱离 GUI 完整测试。

## 几个关键设计

**文本拼接不能用系统给的 `OcrResult.Text`**。中文是逐字分词的，系统会把它拼成「这 是 一 段」。实际用两条规则：CJK 之间从不补空格；其余按词距相对行高的比例判断——这条还能把引擎拆断的英文单词（`ove` + `r`）重新粘回 `over`。

**命中测试按行优先**。实测发现全角冒号「：」的框只有 2px 宽，被误识别成「一」的连字符只有 1px 高，按词自身的矩形做命中根本点不中。垂直方向改用整行范围，水平方向对窄词补偿最小命中宽度。

**选择是词序列上的区间，不是矩形**。这是跨行连选能像网页一样工作的原因。

**插值随缩放比切换**：缩小用 HighQualityCubic，1–2 倍用 Linear，≥2 倍切 NearestNeighbor 保住像素边缘。绘制坐标用物理像素而非 DIP，否则高 DPI 屏上「100%」会发虚。

**自动适应跟随实际画布尺寸**：默认打开时大图缩小到完整显示，小图保持 100%。图片比首次窗口布局更早加载完成时，保留适应请求，在画布尺寸就绪后补做计算；调整窗口、开关文字面板或 DPI 变化时，自动适应模式重新计算缩放，并同步百分比。用户滚轮缩放、点击缩放按钮或选择 100% 后进入固定比例模式，窗口变化保留该比例；点击“适应”恢复自动适应。

**旋转是纯展示层变换，不碰解码结果**。`RotationTransform` 算出一个「位图坐标→视口坐标」矩阵，旋转/缩放/平移一次性应用；命中测试用它的逆矩阵把鼠标位置换回原始位图坐标。`ZoomPanController` 全程不知道旋转的存在——它只看「旋转后的逻辑尺寸」。

**GIF 动图按 disposal 规则真实合成，不是简单叠加帧**。GIF 常见的编码方式是每帧只存和上一帧不同的那一小块矩形，必须按 GIF89a 的 disposal method（尤其是"恢复为背景"要清成透明，而不是简单地不清）逐帧合成到完整画布，否则播放出来的画面会缺胳膊少腿。这部分用手工构造的真实二进制 GIF（`tools/make-animated-test-gifs.py`）测试，专门设计了一个只有透过正确 disposal 才能露出正确颜色的场景。

**WebP/APNG 目前只显示首帧**，不播放动画——这两种格式的动画元数据是和 GIF 完全不同的 WIC 查询 schema，没有真实样本验证过正确性，贸然实现的风险是播出一个静默损坏的动画，不如老实先不支持。

## 环境要求

- Windows 10 1809+（已在 Windows 10 22H2 / 19045 上验证）
- .NET SDK 10
- Windows App SDK 2.5.1（NuGet 自动还原）

### OCR 语言包

识别质量取决于系统装了哪些 OCR 语言包。例如只装了 `zh-Hans-CN` 时：中文识别准确，英文可用但有瑕疵（空格丢失、字母混淆、连字符被识别成「一」）。

装英文包可明显改善英文识别（需管理员权限）：

```bash
powershell -Command "Start-Process powershell -Verb RunAs -ArgumentList '-Command Add-WindowsCapability -Online -Name Language.OCR~~~en-US~0.0.1.0'"
```

装好后应用会自动多出一个可选语言，无需改代码。

## 尚未实现

- WebP / APNG 动画播放（见上方说明；两者目前显示首帧静态图）
- AVIF / JPEG XL 的兜底解码（系统未装对应扩展时打不开，需要接 Magick.NET）
- 右键菜单里的"搜索/翻译"固定用必应，未做搜索引擎可选项
- 没有设置界面（语言选择、缓存大小等目前都是代码里的常量）

UI 改版采用固定深色、自绘标题栏、分组图标工具栏与自适应停靠文字面板。原生窗口标题同步文件名及列表位置，可用于任务栏、Alt-Tab 和系统标题栏回退；文本框有无障碍名称，缩放按钮名称同步比例。空图时缩放、适应、旋转、提取与复制置灰，动图下提取与复制文字置灰。

## 许可证

[MIT](LICENSE)

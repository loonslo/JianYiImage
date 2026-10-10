# README 配图工具

README 里的截图和动图都是**从真实的应用窗口拍的**，没有任何后期合成。界面改了之后可以用这里的脚本重拍，而不用手工操作。

| 文件 | 作用 |
|---|---|
| `make.py` | 一键流水线：OCR 词框 → 拖选计划 → 抓图 → 后期 → 输出到 `assets/` |
| `ocr-dump.ps1` | 用 Windows OCR 读一张图，把每个词的包围盒写成 JSON（拖选计划的依据） |
| `plan.py` | 按词框算出每个场景要拖选的起止点（图像像素坐标，只有数字） |
| `capture.ps1` | 启动应用，置顶窗口，用**真实鼠标和键盘**拖选、框选、点菜单，并截图 |
| `capture-installer.ps1` | 用窗口消息走一遍安装向导到“准备安装”页并逐页截图；**从不按“安装”** |
| `post.py` | 裁掉窗口外框、加投影、给动图帧画上光标、合成 GIF |
| `render-samples.py` | 用无头 Chrome 把 `samples/src/*.html` 渲染成 `samples/` 里的示例图 |

## 重拍

```bash
py -3.14 tools/readme-assets/make.py --exe <发布目录>/JianYiImage.exe --work <一个空的临时目录> --take-over-input --setup <安装包.exe>
```

- **运行期间脚本会接管真实的鼠标和键盘约两分钟**（在示例图上拖选文字、按 `Ctrl+C`）。请不要碰鼠标键盘；不接受这一点就不要加 `--take-over-input`，脚本会拒绝启动。
- 只截应用自己的窗口，不截桌面。窗口被置顶在屏幕左上角，需要屏幕至少 1500×950。
- 示例图的文字用 Windows 自带的 OCR 读一遍来算拖选位置，所以需要至少一个 OCR 语言包；示例图是照只装 `zh-Hans-CN` 时也能识别准确来设计的（中文为主，避免小号英文）。
- 做完请**逐张看一眼**：窗口边缘一圈像素是 DWM 的边框，会和身后的桌面混在一起，`post.py` 已经向内收掉一像素，但新机器、新缩放比例下仍要目检，别让别的窗口的内容漏进公开的图片。

## 踩过的坑

- **逐帧用 `PrintWindow` 截图会打断拖动**：它给窗口发 `WM_PRINT`，指针捕获丢失，选区被取消。拖动期间的帧用 `CopyFromScreen`；拖动结束后的静态图才用 `PrintWindow`。
- **`SetCursorPos` 只挪光标，不产生输入事件**，WinUI 收不到拖动；要用带 `MOVE | ABSOLUTE` 的 `mouse_event`。
- 向导是 Delphi 控件，界面自动化（UIA）看不到内容；用 `EnumChildWindows` + `BM_CLICK`。向导窗口属于安装包展开出的 `<名字>.tmp` 子进程，不是你启动的那个进程。
- Windows PowerShell 5.1 会把没有 BOM 的 UTF-8 脚本按 ANSI 读，所以 `.ps1` 里只放 ASCII，中文用码点拼。

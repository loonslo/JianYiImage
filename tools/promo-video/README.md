# 宣传片工具（30 秒竖屏）

宣传片里的软件画面都是**从真实的应用窗口录下来的**：脚本用真实的鼠标和键盘在示例图上划选、点按钮、右键、滚轮缩放、按快捷键，并逐帧记下光标的位置和形状。片中的大号光标、点击波纹、按键提示和“已复制”提示，都是按录屏时记录的真实位置和时间点叠加上去的；其中提示文字取自录制时真实复制到剪贴板的内容。配乐和音效由程序原创合成，不含任何第三方音乐或采样。为了塞进 30 秒，有几段素材以 1.15–1.25 倍速播放，录制间隙（脚本读取界面元素位置的停顿）被剪掉；画面内容本身没有任何修改。

| 文件 | 作用 |
|---|---|
| `make_demo.py` | 生成录制用的示例图文件夹：两张竖版图（虚构的群聊截图、读书笔记）、一张 GIF 动图，另复制 4 张 `samples/` 里的示例图 |
| `demo-src/*.html` | 两张竖版示例图的 HTML 源文件（店名、地址、电话、密码都是编的） |
| `record.py` | 启动应用，用**真实鼠标键盘**录 9 段素材（30 fps 无损），同时记录每帧光标和每个输入事件 |
| `uia.ps1` | 供 `record.py` 读取界面元素位置、等待 OCR 完成；只读，不发送输入 |
| `backdrop.py` | 录制时铺满屏幕的深色幕布，挡住桌面，并提示“请勿操作鼠标键盘” |
| `build_data.py` | 把录好的素材和日志整理成合成页面要读的 `data.js` |
| `composition/` | 1080×1920 的合成页面：每一帧都是时间 t 的纯函数（字幕、镜头推拉、光标、波纹、按键、提示条、片头片尾） |
| `render.py` | 用无头 Chrome 逐帧截图，交给 ffmpeg 编码成 H.264；也能出单帧预览（`--stills`）和导出时间轴（`--cues`） |
| `music.py` | 按时间轴合成 128 BPM 的配乐与音效，响度标准化到 −14 LUFS，真峰值不超过 −1 dBTP；`--stems` 另外输出纯配乐和纯音效 |
| `package.py` | 合成最终交付：带声版、无声版、字幕 SRT、配乐与音效分轨、封面（9:16 和 3:4） |

## 重新出片

需要：Python 3.14 和 Pillow / numpy / scipy / websocket-client / imageio-ffmpeg（`py -3.14 -m pip install imageio-ffmpeg` 会自带 ffmpeg），Chrome，至少装一个 Windows OCR 语言包，屏幕至少 1720×1420、缩放 100%。以下 `<W>` 是一个空的临时目录。

```bash
py -3.14 tools/promo-video/make_demo.py --out <W>/demo
# 对 4 张图各跑一次 OCR，得到拖选位置的依据（chat、notes、receipt、landscape）
powershell -NoProfile -ExecutionPolicy Bypass -File tools/readme-assets/ocr-dump.ps1 -ImagePath <W>/demo/01-群聊截图.png -OutFile <W>/ocr/chat.ocr.json
# ……notes ← 02-读书笔记.png，receipt ← 03-购物小票.png，landscape ← 04-山海之间.jpg
py -3.14 tools/promo-video/record.py --exe <JianYiImage.exe> --demo <W>/demo --ocr <W>/ocr --work <W>/rec --take-over-input
# 把每段无损素材拆成帧：<W>/rec/frames/<段名>/00001.jpg …
ffmpeg -i <W>/rec/hook.mkv -q:v 1 -pix_fmt yuvj444p <W>/rec/frames/hook/%05d.jpg
py -3.14 tools/promo-video/build_data.py --rec <W>/rec
py -3.14 tools/promo-video/render.py --data <W>/rec/data.js --style light --cues --out <W>/cues.json
py -3.14 tools/promo-video/music.py --cues <W>/cues.json --out <W>/music.wav --stems
py -3.14 tools/promo-video/render.py --data <W>/rec/data.js --style light --out <W>/video.mp4
py -3.14 tools/promo-video/render.py --data <W>/rec/data.js --style light --stills 0 --out <W>/cover
py -3.14 tools/promo-video/package.py --video <W>/video.mp4 --audio <W>/music.wav --cues <W>/cues.json --cover <W>/cover/light-00.00.png --out <输出目录>
```

- **`record.py` 运行时会接管真实鼠标和键盘约两分钟**，不加 `--take-over-input` 会拒绝启动。只要光标不在脚本放的位置（有人动了鼠标），或者键盘焦点不在应用上，就立刻中止并松开所有按下的键；不会按 Delete，不会点任何菜单项，不会改识别语言。剪贴板事先备份、事后还原；`settings.json` 前后比对，变了就还原。
- 只改字幕、镜头、配色、配乐时，不需要重录：改 `composition/promo.js` / `promo.css` / `music.py` 后，从 `render.py` 那一步往下重跑即可。`--style dark` 是深色版本。
- 合成页面也能直接在浏览器里看某一刻：`composition/index.html?data=<data.js 的 file 地址>&style=light&t=12.4`。

## 踩过的坑

- 窄窗口下“⋯”菜单会伸出窗口右边缘，录出来是截断的；“设为默认 / 添加右键打开”改用右键菜单底部那几项来展示。
- 屏幕截图（BitBlt）里没有鼠标指针，所以逐帧记录 `GetCursorInfo` 的光标句柄，与 `LoadCursor` 的系统光标比对出箭头、I 形、十字、移动四种形状，合成时重画成大号光标。
- 等待 OCR 的“文字已识别”状态对 GIF 动图永远不会出现（动图不识别），GIF 那段改为固定等待。
- 无头 Chrome 用完立刻删临时用户目录会因为子进程还没退出而报“拒绝访问”：先按 PID 结束进程树，等一秒再清理。
- 编码时只写 `-colorspace bt709` 不够，色彩原色和传递函数仍是 unknown；在滤镜末尾加 `setparams` 才完整标记为 BT.709。

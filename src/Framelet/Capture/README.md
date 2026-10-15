# Framelet 截图模块

`CaptureEngine` 使用 Windows 屏幕像素创建 `Bitmap`，调用方拥有返回图片并负责释放。区域选择是同步模态交互；取消返回 `null`，不会退出应用。调用前由主窗口隐藏工具条及编辑器，并等待桌面重绘；调用结束后由主窗口统一恢复可见状态。

## 接口

- `CaptureScreen(Rectangle bounds, bool includeCursor = false)`：按物理屏幕坐标采集，可包含系统光标；允许多显示器负坐标。
- `CaptureActiveWindow(IntPtr handle, bool includeCursor = false)`：捕捉给定可见窗口的边界，最小化/失效窗口返回 `null`。
- `SelectCapture(CaptureMode mode, Size fixedSize, bool includeCursor = false)`：完整选择流程。`LastRegion` 记录已接受区域的桌面坐标。
- `SelectScreenBounds()`：点选单个显示器并返回物理桌面边界；取消返回 `null`，用于全屏录制范围选择。
- `ImageStitcher.Stitch(IReadOnlyList<Bitmap> frames)`：对同尺寸屏幕图像进行垂直重叠匹配，返回独立图片；不释放或修改输入。无法可靠匹配或向上滚动时抛出异常。
- `ImageStitcher.FindVerticalOverlap(Bitmap previous, Bitmap next)`：返回识别到的重复行数，无可靠匹配为 0；完全相同图片返回整帧高度。
- `ImageStitcher.Analyze`：区分向下匹配、重复、向上及不匹配，并返回位移和固定顶底栏范围；`AreEquivalent` 用于等待稳定画面。
- `ScrollCaptureSession.Append`：只保留新增条带和末帧，失败不改变已有结果；`ToBitmap` 返回独立图片，调用方释放 session 与结果。

矩形支持拖拽或两次点击。Enter/Space 确认角点，Tab 切换要微调的角点，方向键移动 1 像素，Shift+方向键移动 10 像素。自由区域可点选或拖动，Space 加点，Enter/双击完成。Esc 或右键取消。覆盖层保持物理像素尺寸，不跟随 DPI 消息缩放；区域外的自由截图像素透明。

`ActiveWindow` 模式现在点选完整窗口，`WindowObject` 保留窗口/子控件点选；`FullScreen` 点选单个显示器，可用 Tab / Shift+Tab 切换。悬停高亮、显示目标名称和尺寸，单击或 Enter 确认；截图来自冻结原图，不包含选框或提示。窗口候选按冻结画面前的可见 Z 序枚举。定时截图仍自动采集整个虚拟桌面，不弹出选择层。

滚动捕捉先选择框选区域或点选屏幕。区域中心或屏幕确认点决定原生滚动目标，随后发送逐格向下滚轮输入、等待稳定帧、增量匹配，到底自动完成；初始没有响应时暂停并提示，不误报长截图完成。F8 暂停/继续，F9 完成，Esc 取消；快捷键冲突保留按钮入口。合成鼠标指针不加入长截图。目标 HWND、前台、窗口位置和鼠标指向变化会停止后续输入；显式继续可重新激活既定目标。控制窗始终保持 Visible，避免终止模态循环，通过窗口捕获排除或临时透明避免污染截图。输出最多 64000 行和 8000 万像素；缓存为条带与末帧。

## 验证

```powershell
dotnet run --project src/Framelet/Capture/Verification/CaptureChecks.csproj
```

独立项目默认执行合成图像、几何、选择和自动滚动控制流程检查，不读桌面、不发送系统输入。覆盖精确重叠、逐像素输出、固定栏、重复帧、拒绝不可靠匹配、增量所有权及暂停/继续/取消。点选回归使用合成背景短暂显示模态窗口，覆盖负坐标、屏幕空隙、窗口 Z 序、点击/取消和键盘切屏。

可选 `dotnet run --project src/Framelet/Capture/Verification/CaptureChecks.csproj -- --native-scroll` 会打开自有验证窗、移动鼠标并发送真实滚轮，检查屏幕采集与自动流程；运行期间不要同时操作桌面。

运行时混合 DPI、多显示器、全局热键冲突和第三方软件兼容性仍需对应环境验证。屏幕采集无法还原被遮挡或受保护的内容。重复捕捉保留矩形边界，不保留自由路径。拼接处理同尺寸、向下滚动的静态内容，保留一份固定顶底栏；复杂重复纹理、动画或不连续画面会暂停，不将未验证的整帧直接追加。

# Framelet 截图模块

`CaptureEngine` 使用 Windows 屏幕像素创建 `Bitmap`，调用方拥有返回图片并负责释放。区域选择是同步模态交互；取消返回 `null`，不会退出应用。调用前由主窗口隐藏工具条及编辑器，并等待桌面重绘；调用结束后由主窗口统一恢复可见状态。

## 接口

- `CaptureScreen(Rectangle bounds, bool includeCursor = false)`：按物理屏幕坐标采集，可包含系统光标；允许多显示器负坐标。
- `CaptureActiveWindow(IntPtr handle, bool includeCursor = false)`：捕捉给定可见窗口的边界，最小化/失效窗口返回 `null`。
- `SelectCapture(CaptureMode mode, Size fixedSize, bool includeCursor = false)`：完整选择流程。`LastRegion` 记录已接受区域的桌面坐标。
- `ImageStitcher.Stitch(IReadOnlyList<Bitmap> frames)`：对等宽屏幕图像进行垂直重叠匹配，返回独立图片；不释放或修改输入。
- `ImageStitcher.FindVerticalOverlap(Bitmap previous, Bitmap next)`：返回识别到的重复行数，无可靠匹配为 0；完全相同图片返回整帧高度。

矩形支持拖拽或两次点击。Enter/Space 确认角点，Tab 切换要微调的角点，方向键移动 1 像素，Shift+方向键移动 10 像素。自由区域可点选或拖动，Space 加点，Enter/双击完成。Esc 或右键取消。覆盖层保持物理像素尺寸，不跟随 DPI 消息缩放；区域外的自由截图像素透明。

滚动捕捉为手动滚动流程：选区域后按 F8 采集，F9 完成；被其他软件占用的热键明确提示，并保留按钮入口。每次至少保留约四分之一重叠，并避开固定标题栏、滚动条和动画。控制窗采集时隐藏，恢复时不抢焦点。截图和拼接上限为 8000 万像素，单次最多 50 帧，采集缓存另有累计像素保护。

## 验证

```powershell
dotnet run --project src/CaptureReplica/Capture/Verification/CaptureChecks.csproj
```

独立项目编译全部截图源码，执行 24 项图像与几何检查，不显示窗口、不读桌面、不发送输入。合成数据覆盖精确重叠、完整逐像素拼接、重复帧、不匹配保留、不等宽/空输入拒绝、输入所有权、裁剪偏移、自由选区透明性、共线空路径及单像素选区。

运行时混合 DPI、多显示器、窗口遮挡及全局热键冲突仍需实际环境验证。窗口模式读取当前可见屏幕，无法还原被其他窗口遮挡的内容。重复捕捉保留矩形边界，不保留自由路径。拼接只处理同宽、向下滚动、静态内容；复杂重复纹理、固定栏和动画可能无法准确对齐，未找到可靠重叠时保留所有行。

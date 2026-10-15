<p align="center"><strong>简体中文</strong> · <a href="README.en.md">English</a></p>

![Framelet — Capture. Annotate. Record.](docs/images/framelet-cover.webp)

<h1 align="center">帧简 Framelet</h1>
<p align="center">把重点，留在画面里。<br>一个轻巧的 Windows 截图、标注与录屏工具。</p>

<p align="center">
  <a href="https://github.com/chenzhiyong1994/Framelet/releases/latest"><img src="https://img.shields.io/github/v/release/chenzhiyong1994/Framelet?style=flat-square&amp;color=147D70&amp;label=release" alt="最新版本"></a>
  <img src="https://img.shields.io/badge/Windows-x64-1C302E?style=flat-square" alt="Windows x64">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-1C302E?style=flat-square" alt="MIT License"></a>
</p>

<p align="center">
  <a href="https://github.com/chenzhiyong1994/Framelet/releases/latest"><strong>下载 Windows 便携版 ↗</strong></a>
  &nbsp; · &nbsp; <a href="https://chenzhiyong1994.github.io/Framelet/">浏览项目主页</a>
  &nbsp; · &nbsp; <a href="docs/usage.md">使用指南</a>
</p>

## 从一个画面，到一句清楚的说明

截下需要的部分，标出想说的重点；需要演示操作时，再录一段屏幕。Framelet 把这些日常工作放进一条可贴边收起的小工具栏。

| 选取画面 | 说明重点 | 录下过程 |
| :--- | :--- | :--- |
| 框选区域，或点选窗口、显示器。先看清目标，再确认截图。 | 箭头、文字、序号、高亮与马赛克；拖动图片四边、四角即可裁切，支持撤销。 | 选好范围，倒计时后开始；边框、计时和控制条让录制状态清楚可见。 |

**解压即用，无声录屏无需配置。** 便携包包含 .NET 运行时，使用 Windows 原生媒体能力生成 H.264 MP4。录音与视频首尾剪辑是需要 FFmpeg 的可选扩展。

## v0.6.0，让每一步都有把握

- **选得准。** 窗口、显示器均可点选，录屏也能先选择显示器；`Tab` 切换屏幕，`Esc` 取消。
- **裁得快。** 拖动图片任意边缘或角点，预览保留范围，松开完成裁切；`Ctrl+Z` 撤销。
- **看得见。** 录制中显示红色边框，暂停时显示黄色边框，同时标明状态和尺寸。
- **退得出。** 结束或关闭时，可保存、放弃或返回。录得不满意可以直接放弃，无需先选择保存位置。

## 三步开始

1. 从 [Releases](https://github.com/chenzhiyong1994/Framelet/releases/latest) 下载 `Framelet-v0.6.0-win-x64.zip`。
2. 解压到本地文件夹，打开 `Framelet.exe`，无需安装。
3. 按 `Ctrl+PrtSc` 框选区域，或点击工具栏的录像图标。

截图默认以 **100% 实际大小** 显示。大图可以滚动浏览，也能切换“适合窗口”。应用界面目前为中文；README 与项目主页提供中英文版本。

## 常用快捷键

| 操作 | 快捷键 |
| :--- | :--- |
| 框选区域 | `Ctrl+PrtSc` |
| 点选完整窗口 / 显示器 | `Alt+PrtSc` / `PrtSc` |
| 展开工具栏 | `Ctrl+Shift+F12` |
| 打开录像机 | `Ctrl+Shift+F9` |
| 暂停 / 继续录制 | `Ctrl+F10` |
| 结束录制，选择保存或放弃 | `Ctrl+F11` |

截图快捷键可在设置中调整；录屏快捷键被占用时，可以使用控制条。[更多操作 →](docs/usage.md)

## 轻巧，也有明确边界

图片可导出为 PNG、JPEG、BMP、GIF、TIFF 和单页 PDF。标注提交为像素，支持会话内撤销；GIF/TIFF 编辑首帧或首页。长截图支持框选区域或点选屏幕后自动滚动、采集与拼接。

录屏需要 Windows 媒体组件。音频采集依赖可选 FFmpeg，真实录音尚未验证；目前没有专用系统声音回环、摄像头叠加或多轨剪辑。屏幕点选已有合成坐标检查，真实混合 DPI 多屏仍待验证。

[完整功能说明](docs/features.md) · [验证记录](docs/validation.md)

## 从源码构建

需要 Windows 与 .NET SDK **10.0.401**。

```powershell
# 构建包含运行时的便携版，输出目录需为空
./scripts/build.ps1 -Publish

# 运行图像、配置、原生 MP4 与截图检查
./scripts/test.ps1
```

SDK 不在 `PATH` 时，可传入 `-DotnetPath '<SDK>/dotnet.exe'`。可选 FFmpeg 验证、命令行入口及诊断方式见[开发文档](docs/development.md)。

## 一起改进

欢迎提交 [Issue](https://github.com/chenzhiyong1994/Framelet/issues) 或 Pull Request。反馈问题时，请附操作步骤、Windows 版本和显示缩放。

[使用指南](docs/usage.md) · [开发与诊断](docs/development.md) · [品牌规范](docs/brand.md)

采用 [MIT License](LICENSE)。运行时与可选组件的许可说明见[第三方组件说明](THIRD-PARTY-NOTICES.md)。

# 第三方组件说明

Framelet 源码、文档与本项目原创几何品牌素材使用根目录 [MIT 许可证](LICENSE)。

## .NET

Windows 便携包包含 .NET Runtime 与 Windows Desktop Runtime。构建脚本从本次实际解析的运行时包复制许可证与第三方声明到 `licenses/`，按包名及版本分目录保存。它们保留各自的版权与许可，不由 Framelet 的许可证替代。

- [.NET Runtime](https://github.com/dotnet/runtime)
- [Windows Forms](https://github.com/dotnet/winforms)
- [Windows Desktop](https://github.com/dotnet/windowsdesktop)

## FFmpeg

录屏与视频裁剪调用用户另行安装的 [FFmpeg](https://ffmpeg.org/)。本仓库与官方便携包不包含 FFmpeg 二进制。不同 FFmpeg 构建启用的组件及许可可能不同，分发者应查阅所用构建的 [许可说明](https://ffmpeg.org/legal.html)。

## 品牌素材与字体

应用图标由 `scripts/create-icon.ps1` 的本地几何定义生成；界面预览使用本项目的测试画板。字体由操作系统提供，仓库和发布包不分发字体文件。

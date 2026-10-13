# 开发与诊断

[返回首页](../README.md) · [使用指南](usage.md) · [验证记录](validation.md) · [品牌规范](brand.md)

Framelet 使用 C#、Windows Forms 与 .NET 10，主项目为 `src/Framelet/Framelet.csproj`。请在 Windows 上构建和运行。

## 项目官网

官网是 `docs/index.html`、`docs/site.css` 和 `docs/site.js` 组成的静态页面，复用 `docs/images/` 的产品截图，不需要安装前端依赖。使用 Python 3 在仓库根目录启动预览：

```powershell
python -m http.server 8768 --bind 127.0.0.1 --directory docs
```

访问 `http://127.0.0.1:8768/`。GitHub Pages 的发布源为 `main` 分支的 `/docs`，`.nojekyll` 保持原样发布；推送后由 GitHub 自动部署。更新应用版本时同步官网中的版本标记与真实截图，主要下载入口始终指向最新 Release。修改页面后检查桌面/窄屏布局、截图切换与放大、Esc 关闭、常见问题和下载链接。

## 从源码构建与验证

主项目为 `src/Framelet/Framelet.csproj`，`global.json` 指定 .NET SDK `10.0.401`，允许同功能带的补丁升级。在仓库根目录运行：

```powershell
# 构建并生成 dist/Framelet/Framelet.exe
./scripts/build.ps1 -DotnetPath '<SDK>/dotnet.exe' -Publish

# 已有发布文件时，选择新的空目录
./scripts/build.ps1 -DotnetPath '<SDK>/dotnet.exe' -Publish -PublishDirectory dist/Framelet-0.5.0

# 图像、配置、格式、原生 MP4 编码和捕捉/拼接检查
./scripts/test.ps1 -DotnetPath '<SDK>/dotnet.exe'

# 额外执行可选 FFmpeg 编码、暂停恢复、合并与裁剪检查
./scripts/test.ps1 -DotnetPath '<SDK>/dotnet.exe' -WithRecording
```

SDK 已在 `PATH` 中时可以省略 `-DotnetPath`，也可以设置 `DOTNET_EXE`。发布目录必须为空，脚本会复制当前 README、文档、品牌素材及运行时许可；已有文件时用 `-PublishDirectory` 选择新目录，避免旧文件混入。默认自检包含原生 MP4 编码；录屏自检使用合成画面，不采集桌面或麦克风。应用自检报告写入 `artifacts/self-tests/results.json`；这些检查不等同于所有桌面交互都已验证，人工验证记录见 [验证说明](validation.md)。

支持的命令行入口：

| 参数 | 用途 |
| --- | --- |
| `图片路径` | 在编辑器打开图片 |
| `--editor` | 打开空白图像编辑器 |
| `--recorder` | 打开录像机 |
| `--profile 配置路径` | 指定主应用配置文件；录屏选项仍使用用户级配置 |
| `--fixture` | 打开供人工捕捉验证使用的样例窗口 |
| `--self-test` | 运行应用自检后退出 |
| `--output 输出目录` | 指定自检产物目录，与 `--self-test` 配合 |
| `--with-recording` | 在应用自检中增加 FFmpeg 编码与裁剪检查 |

品牌色、标志和界面原则见 [品牌说明](brand.md)。日常操作和默认输出位置见 [使用指南](usage.md)。

## 配置与诊断

主应用配置为 `%LOCALAPPDATA%\Framelet\settings.json`，录屏配置为同目录的 `recorder.json`。首次使用此目录时采用默认设置，可在“设置”中调整快捷键与输出目录；无声录屏无需配置引擎，扩展录音与剪辑可在各自入口设置 FFmpeg。程序仅使用当前品牌的目录与环境变量，不自动导入其他位置的配置。

设置 `FRAMELET_TRACE=1` 可启用诊断日志，日志保存在主应用配置所在目录的 `diagnostics.log`。设置 `FRAMELET_FFMPEG` 可指定扩展功能使用的 FFmpeg 可执行文件；`--profile` 可为主应用指定独立配置文件。

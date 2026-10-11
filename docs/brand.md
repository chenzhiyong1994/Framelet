# 帧简 Framelet

帧简（英文名 Framelet）是本项目的产品名称。“帧”表示选取、记录与编辑的图像片段，“简”表示明确、轻便的操作。应用展示使用“帧简 Framelet”；空间有限的标题、托盘说明可使用“帧简”。

## 标志

标志由三个圆头取景角与右上方折角组成。中心保留空白，表示用户正在选择的内容；折角把“捕捉一帧”与“整理、编辑这一帧”连接起来。图形采用原创几何构造，不使用照相机、镜头或原产品五彩图标元素。

共享标志坐标为 32×32：底板位于 `(1,1)`、大小 `30×30`、圆角 `6.5`；三段取景角为 `M14 8H8V14`、`M8 18V24H14`、`M18 24H24V18`。标准描边宽度 `2.5`，圆头、圆连接。右上浅色三角为 `M18 8H24V14Z`，深色折页为 `M18 8L24 14H20Q18 14 18 12Z`。ICO 的 16/24 px 版本将描边光学校正为 `3`，其余几何保持一致。

## 调色

| 名称 | 色值 | 用途 |
| --- | --- | --- |
| WarmWhite | `#F5F4EF` | 应用主背景、标志取景角 |
| Ink | `#1C302E` | 主要文字、标志折页 |
| Accent | `#147D70` | 主要操作、品牌底板 |
| AccentPale | `#E5F2EC` | 柔和强调、标志折角 |
| Muted | `#73817B` | 次要文字 |
| Border | `#D9E1DB` | 分隔与边界 |
| Canvas | `#EAEFEB` | 图像工作区 |
| Coral | `#E57761` | 需要关注的强调元素 |

标志自身只使用 Accent、WarmWhite、AccentPale 与 Ink；不增加渐变、描边装饰或阴影。图形内保持留白，不放置缩写或中心圆点。

## 资产与重建

资产位于 `src/CaptureReplica/Assets/`：

| 文件 | 内容 |
| --- | --- |
| `Framelet.ico` | 16、24、32、48、64、128、256 px 共 7 层，32 位 alpha，PNG 载荷 |
| `Framelet.svg` | 纯矢量图标，32 单位 viewBox，可无损缩放 |
| `Framelet-wordmark.svg` | 图标与中英文字组合，透明背景 |
| `Framelet.png` | 256×256 透明背景图标 |
| `Framelet-preview.png` | 从实际 ICO 的 PNG 载荷解码生成的原尺寸对照预览 |

文字组合使用系统字体 `Microsoft YaHei UI` / `PingFang SC` 与 `Segoe UI` / `Arial`，不嵌入第三方字体文件。`Framelet.svg` 本身不依赖任何字体。

在 Windows PowerShell 7 中运行：

```powershell
pwsh -NoProfile -File scripts/create-icon.ps1
```

脚本只使用 `System.Drawing` 和本地几何定义，不下载素材。默认写入上述资产目录；也可以使用 `-AssetsDirectory <directory>` 指定输出位置。它检查 ICO 层数、目录尺寸、位深、PNG 载荷尺寸与解码、Windows 图标解码以及 SVG XML 结构，然后生成多尺寸预览。

`System.Drawing.Icon` 的尺寸选择在多层 ICO 中未选择 256 px 层，而是返回了 128 px 层；因此最大层按 ICO 标准使用尺寸字节 0，并直接解码其 PNG 内容验证为 256×256。16–128 px 的图标层已另外通过 `System.Drawing.Icon` 原尺寸解码验证。

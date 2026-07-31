---
type: report
title: 悬浮窗视觉复刻 GitHub 方案调研
tags: [research, visual-parity, wpf]
intent: 记录悬浮窗视觉复刻方案的证据、决策和现役落地边界。
---

# 悬浮窗视觉复刻 GitHub 方案调研

> 文档状态：这是 2026-07-28 的方案决策记录。推荐的 raster 静态层与 WPF 动态层路线已经接入，并已通过用户真实运行验收；现役悬浮窗的唯一视觉基线和发布边界以 `AGENTS.md`、`README.md` 为准。下文“推荐落地顺序”保留用于解释当时决策，不是待办清单。

## 结论先行

截至 2026-07-28，没有找到一个可信的高热度 GitHub 项目或 agent skill，能够把单张参考截图直接生成可维护的 C# WPF/XAML 悬浮窗，并同时保证像素级一致、透明窗行为和现有交互完整。GitHub 上高热度的 screenshot-to-code 项目主要输出 HTML/CSS、Tailwind、React 或 Vue；微软相邻的 Reactor skill 面向实验性的 WinUI 3，而且其 WPF host 仍标为 `coming soon`。两者都不能直接解决当前 WPF 窗口。

本项目不应迁移 UI 技术栈，也不应把 Web 生成器的结果再人工翻译成 WPF。更准确且风险更低的路线是：

1. 保留现有 C# WPF 宿主与交互逻辑。
2. 对不需要动态变化的背景、纸张、装饰、阴影和角色，尽可能直接使用参考图中提取的原始 RGB 位图层；WPF 只负责动态文字、动态卡片和点击区域。
3. 保留现有 SAM 2.1 + rembg/BiRefNet 的离线素材提取链。当前三张角色图已经走过这条链，再换同类分割项目无法解释或修复全窗差异。
4. 继续用 ImageMagick 产生可审计的差异图和指标；如抗锯齿或透明像素噪声妨碍判断，再在测试侧评估 pixelmatch，不放入应用运行时。
5. 如要证明“真实桌面上看到的像素”而不只是 `RenderTargetBitmap` 输出，可在取得 GUI 自动化授权后评估 FlaUI 作为测试侧最终截图器，并固定 DPI、字体、主题、桌面底色和窗口状态。

实施前证据也支持这个判断。旧版真实 WPF 基线虽然几何检查已通过，但白底展开后的全窗指标为 RMSE `0.0952465`、MAE `0.0364532`、不同像素比例 `97.0814%`、SSIM `0.1034733`。这说明主要问题不是“再找一个布局库”，而是大量表面像素、颜色、纹理、阴影、字体栅格化或合成方式不同。基线证据位于 `D:\Codex\.codex\run-evidence\clock-countdown-visual-parity\20260728T135710-final\magick-metrics.json`。

推荐路线实施后的真实 WPF 候选位于 `D:\Codex\.codex\run-evidence\clock-countdown-raster-runtime\20260728T154837\24-visual-release`：RMSE `0.0751723`、MAE `0.0179607`，较旧基线分别改善约 `21.08%` 和 `50.73%`。该结果达到任务书的采用门槛，但不同像素比例仍约 `43.68%`，不能称为数学意义上的逐像素一致。

## 调研边界和数据口径

- 调研时间：2026-07-28 14:32 CST。
- 热度数据来自 GitHub REST API 的 `stargazers_count`，是查询时快照，会继续变化；准确适配优先于 star 数。
- 活跃度优先记录最新正式 GitHub Release；没有 Release 时记录默认分支最近提交或最新 tag，并明确标注。
- 功能判断只采用候选仓库的 README、源码、Release、许可证和 GitHub 官方 API，不采用二手榜单宣传。
- 本轮只读核验候选，没有安装、运行或接入任何候选；运行效果均标为未实测。
- GitHub 仓库搜索分别覆盖 `screenshot to code + WPF`、`image to XAML`、`screenshot to XAML`、`WPF + pixel perfect` 以及 `SKILL.md`。`screenshot to XAML` 仓库搜索返回 0 项；`image to XAML` 的最高相关结果 [EasyXAML](https://github.com/queeselamor/uv-easyxaml) 只有 17 stars，README 明确是 SVG 转 XAML，不是截图复刻。搜索结果不足以证明不存在私有或未被索引的实现，但没有发现可复核的直接 WPF 方案。

## 候选总览

| 候选 | 类别 | Stars | 最近更新或最新发布 | License | 平台与核心机制 | 本项目裁决 |
| --- | --- | ---: | --- | --- | --- | --- |
| [abi/screenshot-to-code](https://github.com/abi/screenshot-to-code) | 截图转代码 | 73,586 | 默认分支提交 `b0d6ab882e61`，2026-07-27；无 GitHub Release/tag | MIT | React/Vite + FastAPI + 多模态模型，输出 HTML/CSS、Tailwind、React、Vue、Bootstrap、Ionic | 不采用 |
| [microsoft/microsoft-ui-reactor](https://github.com/microsoft/microsoft-ui-reactor) | 官方 Windows UI skill / 框架 | 599 | tag `v0.1.0-preview.12`，2026-07-15；默认分支 2026-07-27 | MIT | 实验性 WinUI 3 C# DSL、Reactor skills、截图/树检查 devtools | 不迁移；不采用 |
| [ImageMagick/ImageMagick](https://github.com/ImageMagick/ImageMagick) | 图像处理与像素差 | 17,036 | [7.1.2-29](https://github.com/ImageMagick/ImageMagick/releases/tag/7.1.2-29)，2026-07-27 | ImageMagick License | Windows/Linux/macOS CLI 与 API；裁切、缩放、合成、alpha、差异图和 RMSE 等指标 | 保留并强化测试用法 |
| [mapbox/pixelmatch](https://github.com/mapbox/pixelmatch) | 视觉回归 / 像素差 | 6,891 | [v7.2.0](https://github.com/mapbox/pixelmatch/releases/tag/v7.2.0)，2026-04-29 | ISC | Node/浏览器；感知色差、抗锯齿识别、透明 checkerboard、滑窗差异密度 | 条件采用，仅测试侧 |
| [facebookresearch/sam2](https://github.com/facebookresearch/sam2) | 图像分层 / 分割 | 19,605 | 无 GitHub Release/tag；默认分支最近提交 2024-12-16，仓库最近 push 2026-05-30 | Apache-2.0 | Python/PyTorch；点或框提示分割、自动 mask、SAM 2.1 checkpoints | 已采用，继续限于离线素材 |
| [danielgatis/rembg](https://github.com/danielgatis/rembg) | 背景移除 / alpha matting | 24,043 | [v2.0.77](https://github.com/danielgatis/rembg/releases/tag/v2.0.77)，2026-07-18 | MIT | Python/ONNX Runtime；CLI、库、服务、Docker，多模型与 alpha matting | 已采用，不作为新解法 |
| [ZhengPeng7/BiRefNet](https://github.com/ZhengPeng7/BiRefNet) | 高分辨率二分割 / matting | 3,948 | [v1](https://github.com/ZhengPeng7/BiRefNet/releases/tag/v1)，2024-05-13；默认分支最近 push 2026-07-24 | MIT | Python/PyTorch，可转 ONNX；高分辨率、动态分辨率及 matting 权重 | 已通过 rembg 采用，不作为新解法 |
| [FlaUI/FlaUI](https://github.com/FlaUI/FlaUI) | Windows 桌面 UI 自动截图 | 3,066 | [v5.0.0](https://github.com/FlaUI/FlaUI/releases/tag/v5.0.0)，2025-02-25；默认分支最近提交 2026-06-17 | MIT | .NET + Windows UI Automation；UIA3 支持 WPF，GDI `BitBlt` 捕获屏幕、元素或矩形 | 候选采用，仅测试侧 |

## 逐项判断

### 1. abi/screenshot-to-code：高热度，但只能作为 Web 原型参考

[README](https://github.com/abi/screenshot-to-code/blob/main/README.md) 明列的输出栈只有 HTML + Tailwind、HTML + CSS、React + Tailwind、Vue + Tailwind、Bootstrap 和 Ionic + Tailwind。它的本地架构是 React/Vite 前端、FastAPI 后端，并依赖 OpenAI、Anthropic、Gemini 或 Replicate 等模型；可选 Chromium 预览让生成页面在浏览器中回看。

- 能解决：从截图推测 Web 布局、颜色和资产，快速生成可比较的 Web 原型。
- 不能解决：不输出 WPF/XAML；不能复用现有 WPF 控件、透明窗口、命令和主题；模型生成也不提供逐像素保证。
- WPF 接入方式：只能把生成页面当一次性几何参考，再人工翻译为 XAML，属于间接且损失信息的路径。
- 风险：新增多模型 API、Python、Node 和 Chromium 依赖，却仍要二次重写；视觉误差会从“模型生成”和“Web 到 WPF 翻译”叠加。
- 裁决：不采用，不把其高 star 等同于 WPF 适配度。

### 2. Microsoft.UI.Reactor skill：微软官方且活跃，但技术栈不匹配

[项目 README](https://github.com/microsoft/microsoft-ui-reactor/blob/main/README.md) 将 Reactor 定义为实验性的 WinUI 3 C# 声明式框架，而不是截图转代码器。[reactor-design skill](https://github.com/microsoft/microsoft-ui-reactor/blob/main/plugins/reactor/skills/reactor-design/SKILL.md) 是 Windows 11/WinUI 设计规则；[reactor-devtools skill](https://github.com/microsoft/microsoft-ui-reactor/blob/main/plugins/reactor/skills/reactor-devtools/SKILL.md) 能截取 Reactor 窗口、检查 visual tree 和驱动控件。最关键的是，其官方 [WPF interop 文档](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/guide/wpf-interop.md) 明确写着 WPF host `coming soon`，当前只发布 WinForms wrapper，WPF 需自己嵌入 `DesktopWindowXamlSource`。

- 能解决：若新建 WinUI 3 应用，可提供 agent 友好的 C# UI DSL、运行时树检查和截图工具。
- 不能解决：不从参考图生成 UI；不是 WPF；不直接支持当前透明悬浮窗和既有主题模板。
- WPF 接入方式：只能引入 WinUI XAML Island 或整体迁移，超出本任务且增加两个 dispatcher、Windows App SDK 和互操作边界。
- 风险：项目明确是 experimental、pre-1.0；迁移会扩大行为与部署回归面，仍不产生像素一致性。
- 裁决：不采用。它证明“有 Windows UI skill”不等于“有 WPF 截图一比一复刻 skill”。

### 3. ImageMagick：保留为主差异引擎

[项目 README](https://github.com/ImageMagick/ImageMagick/blob/main/README.md) 确认其支持 Windows CLI/API、裁切、缩放、合成、透明处理等自动化操作；仓库内的 [`compare` 文档](https://github.com/ImageMagick/ImageMagick/blob/main/www/compare/index.html) 明确支持数学指标和可视差异图，包括 RMSE，并说明未启用 subimage search 时按像素直接比较。[许可证](https://github.com/ImageMagick/ImageMagick/blob/main/LICENSE) 是项目自有 ImageMagick License，GitHub API 因而显示 `NOASSERTION`，不应误写为 MIT 或 Apache。

- 能解决：对齐尺寸、生成 alpha/RGB 差异图、计算 RMSE/AE/PSNR 等指标、裁切并按区域定位误差。
- 不能解决：不会推断 XAML 布局，也不会自动修正视觉；单一全窗 RMSE 不能说明误差来自背景、角色、卡片还是文字。
- WPF 接入方式：保持测试/证据侧 CLI；若未来需要纯 .NET，可另评估 README 链接的 Magick.NET，但不必进入应用运行时。
- 风险：DPI、色彩空间、premultiplied alpha、参考图尺寸和背景展开方式不固定时，数字没有可比性。
- 裁决：保留。下一轮应把全窗指标拆成背景、纸张/阴影、三个角色、标题、两张卡片和按钮等区域，并保存每区 diff mask。

### 4. pixelmatch：可补强抗锯齿和透明像素判定

[README/API](https://github.com/mapbox/pixelmatch/blob/main/README.md) 说明它直接比较同尺寸像素数组，支持感知色差、抗锯齿识别、透明背景 checkerboard 和 diff mask；`windowSize` 可返回局部滑窗中的最大差异密度，避免少量散布的 GPU/子像素噪声掩盖真正的局部回归。

- 能解决：比“任意通道不等即计数”更稳地处理文字抗锯齿和半透明边缘，并能用局部密度找出最差区域。
- 不能解决：只检测，不生成或修正 WPF；仍要求输入尺寸一致。
- WPF 接入方式：在现有 PNG 产物之后运行 Node CLI/脚本，不引用到 WPF 应用。
- 风险：新增 Node 测试依赖；默认阈值会隐藏小差异，必须先用现有参考/实际图做可变红校准，不能照搬 `0.1`。
- 裁决：条件采用。只有 ImageMagick 的抗锯齿/alpha 噪声确实妨碍定位时再引入；否则维持单一差异工具。

### 5. SAM 2：适合分层，不适合重建 UI

[README](https://github.com/facebookresearch/sam2/blob/main/README.md) 将 SAM 2 定义为图像与视频的 promptable segmentation foundation model，静态图支持点/框提示和 automatic mask generation，并提供 SAM 2.1 checkpoints。README 同时建议 Windows 用户优先 WSL，并说明安装依赖 PyTorch，部分路径需要 CUDA 编译。

- 能解决：从参考图指定角色或对象区域，生成 mask，并保留区域内原始 RGB。
- 不能解决：不能恢复被遮挡的像素，不能可靠拆分软阴影、文字与纸张纹理，也不输出 WPF 控件。
- WPF 接入方式：只做离线资产预处理，输出透明 PNG；应用继续加载普通位图。
- 风险：重型 Python/PyTorch/CUDA 环境；mask 边缘仍需人工或 matting 精修。
- 裁决：已采用且方向正确，不需要进入应用依赖。当前角色素材已由 SAM 2.1 框/点提示提取。

### 6. rembg：易用的背景移除封装，但不是布局工具

[README](https://github.com/danielgatis/rembg/blob/main/README.md) 明确支持 CLI、Python library、HTTP server、Docker，以及 CPU/GPU/ROCm 后端；单图命令支持选择模型、只输出 mask、alpha matting 和 SAM prompt。

- 能解决：快速生成透明前景、批量处理素材、切换底层模型并对边缘做 alpha matting。
- 不能解决：不能分解窗口布局或识别控件语义；对相互遮挡、相近颜色和软阴影不能保证精确。
- WPF 接入方式：离线命令生成 PNG，不进入发布包运行时。
- 风险：不同模型和 ONNX Runtime 版本可改变 alpha；产物必须继续记录模型名、命令和 SHA-256。
- 裁决：已采用。当前 rembg 调用的是 `birefnet-general`，再单独“换 rembg”不会改善未被分层的整窗区域。

### 7. BiRefNet：高分辨率边缘精修已在现有链路中

[官方实现 README](https://github.com/ZhengPeng7/BiRefNet/blob/main/README.md) 将其定义为高分辨率 dichotomous image segmentation，并提供动态分辨率、`2048x2048` 高分辨率和 matting 权重；也给出 PyTorch、Hugging Face 和 ONNX 路线。README 还明确提醒 ONNX 输出与原始 PyTorch 输出会有轻微差异。

- 能解决：比普通前景分割更适合毛绒边缘和高分辨率 alpha，适合角色层精修。
- 不能解决：仍是前景/背景二分割，不会还原字体、纸张、阴影、布局或动态交互。
- WPF 接入方式：通过 rembg 或离线 Python/ONNX 产出透明 PNG。
- 风险：大模型的 GPU/显存和版本成本较高；ONNX/PyTorch 的轻微差异要求固定工具链并锁定产物哈希。
- 裁决：已通过 rembg 采用。当前整窗指标仍差，说明继续在同类分割模型上迭代不是最高收益项。

### 8. FlaUI：最匹配 WPF 的最终桌面截图候选

[README](https://github.com/FlaUI/FlaUI/blob/main/README.md) 明确支持 Win32、WinForms、WPF 和 Store Apps，并建议 WPF 使用 UIA3。其 [`Capture.cs`](https://github.com/FlaUI/FlaUI/blob/main/src/FlaUI.Core/Capturing/Capture.cs) 提供主屏、全屏、多屏、AutomationElement 和任意矩形截图，底层用 GDI `BitBlt`/`StretchBlt`；[`CaptureTests.cs`](https://github.com/FlaUI/FlaUI/blob/main/src/FlaUI.Core.UITests/CaptureTests.cs) 展示了窗口元素截图并写出 PNG。

- 能解决：启动/附着真实 WPF 进程、定位窗口和控件、执行交互后截取最终桌面像素，补足 `RenderTargetBitmap` 看不到的 HWND、DPI 和桌面合成差异。
- 不能解决：不生成 UI、不做图像对比；GDI 屏幕截图会把透明窗口与桌面背景合成，不能直接替代内部 alpha 审计。
- WPF 接入方式：只在独立测试/验证层引用 `FlaUI.Core` + `FlaUI.UIA3`，固定 100% DPI、字体、主题、桌面底色与窗口坐标后，将 PNG 交给 ImageMagick。
- 风险：真实桌面测试比离屏测试更易受焦点、显示器、缩放和遮挡影响；项目规则要求 GUI 自动化取得用户明确同意后才能实际运行。
- 裁决：候选采用，但只作为第二条最终合成证据；现有 `RenderTargetBitmap` 仍负责透明 alpha 和稳定离屏回归。

## 推荐落地顺序

### 第一阶段：先把“像素一致”变成可定位的失败信号

不新增应用依赖。固定一个与参考图完全一致的状态、`520 x 390` 输出、96 DPI、字体、语言、主题和示例数据。继续用 ImageMagick，但把指标拆成静态背景、纸张/阴影、角色、动态卡片、文字和图标六类区域；每类保存参考、实际、叠加图和 diff mask。全窗指标只作总览，不能再单独作为通过依据。

### 第二阶段：静态像素直接复用，动态像素才由 WPF 绘制

若目标是尽可能接近参考图，背景、纸张纹理、软阴影和装饰不能继续凭 XAML 渐变/阴影近似，应从原参考图保留为一个或多个固定 PNG 层。已经分离的三个角色继续使用原始 RGB + alpha。WPF 仅覆盖会随数据变化的文字、卡片内容和命令点击面；这样不会破坏现有业务模型，也能显著减少由 WPF 栅格化重新创造的像素。

这一步有一个不可绕过的边界：参考图只有一个扁平化状态，遮挡后的背景像素并不存在。若必须得到“空白面板”或其他动态状态，需要原始分层设计稿，或另生成/修补背景；后者不是一比一恢复，只能标为推断内容。不能用 inpainting 或另一种生成模型声称恢复了原始像素。

### 第三阶段：补真实桌面合成证据

在用户明确同意 GUI 自动化后，先做一个最小 FlaUI 试验：启动现有 EXE、等待 `window-ready`、把窗口放到固定纯色底图上、截取窗口矩形，再与同底色展开的参考图比较。若它相对 `RenderTargetBitmap` 暴露出新的 DPI、阴影、透明合成或字体差异，才把 FlaUI 固化为测试依赖；否则不引入。

### 第四阶段：只在有证据时增加 pixelmatch

如果 ImageMagick 的不同像素比例主要被文字抗锯齿和半透明边缘噪声占据，再用 pixelmatch 的 `includeAA`、透明 checkerboard 和 `windowSize` 做并行试验。通过阈值必须由“故意改变 1 px / 1 色阶后测试变红”的样本校准，不接受默认值直接成为验收线。

## 明确不采用

- 不采用 screenshot-to-code 输出 Web 页面后再翻译为 WPF。
- 不迁移到 WinUI 3 / Microsoft.UI.Reactor；这会扩大技术与部署范围，且不能改善参考图像素来源。
- 不引入 MaterialDesignInXamlToolkit、WPF UI、HandyControl 等通用控件库。它们能统一控件风格，却会把项目已有的温暖可爱主题替换为通用设计语言，与目标相反。
- 不继续盲换背景移除模型。SAM 2.1、rembg、BiRefNet 已覆盖当前角色提取链，剩余整窗差异需要按区域定位。
- 不把 RMSE 下降或几何容差通过等同于“一比一”。真正的完成标准必须同时有指定状态、固定渲染环境、逐区域差异证据和用户对不可动态复现部分的明确取舍。

## 可复核查询摘要

以下命令均为只读，未安装候选：

```powershell
gh api 'search/repositories?q=%22screenshot+to+code%22+WPF+in%3Aname%2Cdescription%2Creadme&sort=stars&order=desc&per_page=10'
gh api 'search/repositories?q=%22image+to+XAML%22+in%3Aname%2Cdescription%2Creadme&sort=stars&order=desc&per_page=10'
gh api 'search/repositories?q=%22screenshot+to+XAML%22+in%3Aname%2Cdescription%2Creadme&sort=stars&order=desc&per_page=10'
gh api 'search/code?q=screenshot+XAML+filename%3ASKILL.md&per_page=50'
gh api repos/<owner>/<repo>
gh api repos/<owner>/<repo>/releases/latest
gh api -H 'Accept: application/vnd.github.raw+json' repos/<owner>/<repo>/readme
```

## 主要一手来源

- screenshot-to-code：[README](https://github.com/abi/screenshot-to-code/blob/main/README.md)、[MIT License](https://github.com/abi/screenshot-to-code/blob/main/LICENSE)。
- Microsoft.UI.Reactor：[README](https://github.com/microsoft/microsoft-ui-reactor/blob/main/README.md)、[Reactor design skill](https://github.com/microsoft/microsoft-ui-reactor/blob/main/plugins/reactor/skills/reactor-design/SKILL.md)、[Reactor devtools skill](https://github.com/microsoft/microsoft-ui-reactor/blob/main/plugins/reactor/skills/reactor-devtools/SKILL.md)、[WPF interop](https://github.com/microsoft/microsoft-ui-reactor/blob/main/docs/guide/wpf-interop.md)、[MIT License](https://github.com/microsoft/microsoft-ui-reactor/blob/main/LICENSE)。
- ImageMagick：[README](https://github.com/ImageMagick/ImageMagick/blob/main/README.md)、[`compare` 文档](https://github.com/ImageMagick/ImageMagick/blob/main/www/compare/index.html)、[ImageMagick License](https://github.com/ImageMagick/ImageMagick/blob/main/LICENSE)、[Releases](https://github.com/ImageMagick/ImageMagick/releases)。
- pixelmatch：[README/API](https://github.com/mapbox/pixelmatch/blob/main/README.md)、[ISC License](https://github.com/mapbox/pixelmatch/blob/main/LICENSE)、[Releases](https://github.com/mapbox/pixelmatch/releases)。
- SAM 2：[README/API/模型说明](https://github.com/facebookresearch/sam2/blob/main/README.md)、[Apache-2.0 License](https://github.com/facebookresearch/sam2/blob/main/LICENSE)。
- rembg：[README/CLI/API](https://github.com/danielgatis/rembg/blob/main/README.md)、[MIT License](https://github.com/danielgatis/rembg/blob/main/LICENSE.txt)、[Releases](https://github.com/danielgatis/rembg/releases)。
- BiRefNet：[README/模型说明](https://github.com/ZhengPeng7/BiRefNet/blob/main/README.md)、[MIT License](https://github.com/ZhengPeng7/BiRefNet/blob/main/LICENSE)、[Releases](https://github.com/ZhengPeng7/BiRefNet/releases)。
- FlaUI：[README](https://github.com/FlaUI/FlaUI/blob/main/README.md)、[`Capture.cs`](https://github.com/FlaUI/FlaUI/blob/main/src/FlaUI.Core/Capturing/Capture.cs)、[`CaptureTests.cs`](https://github.com/FlaUI/FlaUI/blob/main/src/FlaUI.Core.UITests/CaptureTests.cs)、[MIT License](https://github.com/FlaUI/FlaUI/blob/main/LICENSE.txt)、[Releases](https://github.com/FlaUI/FlaUI/releases)。

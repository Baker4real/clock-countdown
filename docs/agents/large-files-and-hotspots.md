---
type: reference
title: 大文件索引与并行热点
tags: [agents, navigation, hotspots, large-files]
intent: 提供仓库源码与测试的精确搜索根、超长文件符号索引命令、并行波次合并冲突热点边界与视觉实拍证据定位。
---

# 大文件索引与并行热点

本仓库没有 `tests/` 目录：产品与独立验证 C# 源码在 `src/`，工具与自检在 `tools/`，核心与窗口套件 PowerShell 脚本在仓库根（用 `rg <模式> . --max-depth 1 -g 'Countdown*.ps*'` 检索；全仓搜索务必加 `-g '!logs/**' -g '!assets/**'` 避免扫入日志与参考图）。

本仓库有若干超长源码、XAML 与测试文件，直接多轮分段盲读会快速消耗上下文，并行分支同时改动同一热点文件也会制造重复冲突。按以下约定处理：

## 1. 超长文件先查符号索引，再按行分段读

以下文件体量较大，改动或定位断言前先用 `rg -n` 查锚点，只读相关片段：

| 文件 | 推荐索引命令 |
| --- | --- |
| `src/MinminCountdown/MainWindow.xaml` | `rg -n 'x:Key="|x:Name="' src/MinminCountdown/MainWindow.xaml` |
| `src/MinminCountdown/MainWindow.xaml.cs` | `rg -n '^\s*(public|private|internal)\s+' src/MinminCountdown/MainWindow.xaml.cs` |
| `src/MinminCountdown/App.xaml` | `rg -n 'x:Key="|TargetType=' src/MinminCountdown/App.xaml` |
| `src/MinminCountdown/UiControls.cs` | `rg -n '^\s*(public|private|internal)\s+(sealed\s+|static\s+|readonly\s+)?(class|enum|struct)\s+|^\s*(public|internal)\s+' src/MinminCountdown/UiControls.cs` |
| `src/MinminCountdown/ReminderWindow.cs` | `rg -n '^\s*(public|private|internal)\s+(sealed\s+|static\s+)?(class\s+|\S+\s+\w+\s*\()' src/MinminCountdown/ReminderWindow.cs` |
| `src/MinminCountdown/ReminderRulesEditorWindow.cs` | `rg -n '^\s*(public|private|internal)\s+(sealed\s+|static\s+)?(class\s+|\S+\s+\w+\s*\()' src/MinminCountdown/ReminderRulesEditorWindow.cs` |
| `src/MinminCountdown/FloatingWindow.Notes.cs` | `rg -n '^\s*(public|private|internal)\s+' src/MinminCountdown/FloatingWindow.Notes.cs` |
| `src/MinminCountdown/MotionBehavior.cs` | `rg -n '^\s*(public|private|internal)\s+(sealed\s+|static\s+)?class\s+|^\s*(public|internal)\s+' src/MinminCountdown/MotionBehavior.cs` |
| `src/MinminCountdown/WindowValidationScenarios.cs` | `rg -n '^\s*(private|internal)\s+.*\b(Probe|Run|Observe|Validate)' src/MinminCountdown/WindowValidationScenarios.cs` |
| `src/MinminCountdown/ValidationReport.*.cs` | `rg -n '^\s*private\s+static\s+' src/MinminCountdown -g 'ValidationReport*.cs'` |
| `Countdown.CSharp.Tests.ps1` | `rg -n '^function |Write-TestSegmentTiming|WindowVerification|#' Countdown.CSharp.Tests.ps1` |
| `tools/PopupValidationContract.cs` | `rg -n '^\s*(public|private|internal)\s+(static\s+)?' tools/PopupValidationContract.cs` |
| `Countdown.Core.Tests.ReleaseContract.ps1` | `rg -n '^#\s|Assert-' Countdown.Core.Tests.ReleaseContract.ps1` |
| `DESIGN.md` | `rg -n '^#{1,3} ' DESIGN.md`（必读 §2、§7、§9、§13 及目标界面章节） |

## 2. 并行波次串行化 4 个合并热点文件

把多张票拆给并行子代理/worktree 时，以下 4 个文件属于高频追加冲突点，不要让同一波次多个分支同时改同一段：

1. `Countdown.CSharp.Tests.ps1`（WindowVerification 事实映射表与测试分段）
2. `src/MinminCountdown.WindowVerification/Program.cs`（独立场景常量、字典与 `RunFact` 注册）
3. `Countdown.Core.Tests.ReleaseContract.ps1`（发布契约清单与静态回归）
4. `DESIGN.md`（现役基线锚点与最新演进段落）

建议做法：
- 并行票只改各自独立的功能源码与独立场景类文件（如 `src/MinminCountdown.WindowVerification/*Scenario.cs`），把上述热点注册或基线叙述统一留到集成波次一次落地；
- 若必须修改其中一个热点文件，同波次只安排单一 owner 写入该文件，其他票排在下一波或集成阶段。

## 3. 视觉参考图与实拍证据定位

审计或核对多表面画面表现时，直接按以下三处路径定位，省去多轮目录枚举（同一流列文件名用 `-Name` 或 `-ExpandProperty`，避免混排异构 `Select-Object` 列）：

- **方向参考图与现役双窗基线**：`assets/ui-references/` 根级 `*-ai-optimized-*.png`（5 张方向参考图），现役版本号由 `src/MinminCountdown/VisualReferenceBaseline.cs` 的 `Version` 唯一声明，对应 `assets/ui-references/main-window/864x1000/<Version>/`（9 帧）与 `assets/ui-references/floating-window/520x390/<Version>/`（5 帧）。
- **底层贴图资产与分带生成依赖**：悬浮窗外壳与标题栏底图在 `assets/floating-plush/component-day-*.png`；管理窗口双工作区装饰场景图在 `assets/main-decorations/main-*-scene-day.png`，由 `tools/Build-MainDayOrnamentScene.ps1` 按 `tools/MainDecorationSceneBands.psd1` 的分带配置生成并由 `tools/Test-MainDayOrnamentScene.ps1` 校验带外纯净度（改动装饰贴图时须同步检查分带配置与发布契约静态哈希）。
- **版本化次级窗口与状态实拍证据**：`docs/evidence/clock-countdown-issue-253-final-surfaces/` 与 `docs/evidence/clock-countdown-issue-250-surfaces/`（提醒中心、规则编辑器、确认框、四大弹层、错误态与系统高对比度），以及同级 `issue-247-countdown-rows`、`issue-249-note-rows`、`issue-251-density-scroll-context`、`issue-255-focus-policy`、`issue-339-sliding-pill-baseline` 目录。
- **本机最近验证实拍（未跟踪）**：`logs/ui-captures/` 下按时间戳命名的子目录（如 `*-validate-only`、`*-density`、`*-scroll-hint`）。

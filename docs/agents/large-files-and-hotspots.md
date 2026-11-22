# 大文件索引与并行热点

本仓库有少量超长脚本与单点注册文件，直接整篇读取会快速消耗上下文，并行分支同时改动同一热点文件也会制造重复冲突。按以下约定处理：

## 1. 超长文件先查符号索引，再按行分段读

以下文件体量较大，改动或定位断言前先用 `rg -n` 查锚点，只读相关片段：

| 文件 | 推荐索引命令 |
| --- | --- |
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

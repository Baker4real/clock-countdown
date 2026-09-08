# Handoff: clock-countdown #101–#110 实现 + v20 基线，套件已暂停

Date: 2026-09-06T22:51+08:00  
Scope: project `D:\agents\projects\时钟倒计时`  
GitHub remote (config file): `https://github.com/Baker4real/clock-countdown.git`  
Workspace at snapshot: `D:\agents\projects\时钟倒计时`  
User `/handoff` 无额外参数。下一会话重点：**先确认用户是否解除「暂停所有套件」**；未解除则不要跑 CSharp/Ui/Core/SourceValidation/Install。解除后再用现役 `2026-09-06-v20` 跑官方 STA 套件。不要 commit / push / 关 issue，除非用户当场授权。

本快照不执行 Git。`refs/heads/main` 由读 `.git/refs/heads/main` 得到；工作树是否相对该提交脏污 **未在本手off时刻跑 `git status`**。下一会话按 `AGENTS.md` 启动时自己跑 `git status --short`，并把既有改动当用户工作保留。

## 用户目标与约束

- 本会话原目标：实现 GitHub `clock-countdown` 全部未关闭 issue，**除** `unified-env` **#58**。
- 实现顺序曾锁定：`#102 → #104 → #105 → #108 → #103 → #110 → #106 → #107`；父 spec 为 **#101**；**#109** 的实现落在 **#110**。
- 根 Agent 本会话用 subagent-orchestration：**根不写代码、不 commit、不 push、不关 issue、不建分支/worktree、不办公室部署**。写所有者是 [Serial UI issue implement](c14eb23a-7c92-43c5-ae8d-474a21ea2cca)。
- 2026-09-06 17:35 用户指令 **「暂停所有套件」仍有效**。当时已杀掉正在跑的 `Countdown.CSharp.Tests.ps1`（终端 `707521`，pwsh 13308 / 50424 及其 `MinminCountdown.exe --validate-only` 子进程）。本手off时刻再查：无匹配套件进程。`verified-current`
- 用户已授权 **新建** 视觉基线 `2026-09-06-v20`（不覆盖 v19）。`changed-and-verified`（磁盘已有 v20 目录与钉死）
- 未授权：commit、push、关 issue、办公室部署、浏览器操控、回退 #101–#110 产品 UI、剥 PNG alpha、放宽 `IsFullyOpaque` / Seal 像素门槛。

## 已确认事实

### project

- 分支 ref：`refs/heads/main` → `ac7c4680c6961c9e830b8382881008b0f8c766f5`。`verified-current`（读 ref 文件，不是 `git log`）
- 最近一次 **提交说明文件** `.git/COMMIT_EDITMSG`：`fix: 悬浮窗内容刷新不再重锚定窗口位置`。该提交是否等于远端 `origin/main`：**pending**（本快照未 fetch / 未 `git status`）
- `origin` URL 在 `.git/config`：`https://github.com/Baker4real/clock-countdown.git`，`branch.main.merge = refs/heads/main`。用户拥有远端，普通发布目标是该 `main`。**不要**建议 `private-patch-stack`。`verified-current`（config）；远端是否与本地一致 **pending**
- 现役视觉钉死已从 `2026-09-05-v19` 改到 **`2026-09-06-v20`**：
  - `src/MinminCountdown/ValidationReport.cs`：`MainReferenceBaselineVersion` / `FloatingReferenceBaselineVersion` = `2026-09-06-v20`（约 L201 / L85）；夜间键盘焦点证据字符串含 `2026-09-06-v20`（约 L1384）
  - 测试与契约：`Countdown.CSharp.Tests.ps1`、`Countdown.Ui.Tests.ps1`、`Countdown.Core.Tests.ps1`、`Countdown.Validation.psm1`
  - 合同：`DESIGN.md` 主窗 L115、悬浮窗 L161
  - `verified-current`
- v20 资产（生成时刻均为 2026-09-06 17:31 左右）：
  - 主窗 `assets/ui-references/main-window/1165x1350/2026-09-06-v20/`：**16 PNG + manifest.json**（1165×1350，`metricDpi` 96，整幅 ROI）
  - 悬浮窗 `assets/ui-references/floating-window/520x390/2026-09-06-v20/`：**8 PNG + manifest.json**
  - `verified-current`
- v19 目录仍在（未删）：
  - `assets/ui-references/main-window/1165x1350/2026-09-05-v19/`
  - `assets/ui-references/floating-window/520x390/2026-09-05-v19/`
  - 代码/测试不再钉 v19。`verified-current`
- 主窗空帧 `main-day-countdowns-empty.png` 字节长度 v19 与 v20 均为 **300894**（与本会话「empty-day RMSE=0」叙述相容，但 **不是** 对 v20 的官方套件绿灯）。`verified-current`（文件长度）
- 捕获路径（`ValidationReport.cs` ~7940）：DIP 尺寸、**96 DPI** `RenderTargetBitmap.Render(visual)`；整幅 A=0 抛错；**禁止** PrintWindow 成功路径；`CaptureOpaqueMainSurface` 仅在源非全透明后 `CompositeOverOpaqueBackground`；filter-open 仍 `CompositePopupOverlay`。窗口 `Opacity==0` 时短暂改为 `0.01` 再 Render。`verified-current`（读源码）
- `Countdown.ReleaseContract.psm1` `IntegrityHashes` 与工作树字节一致（本快照 `Get-FileHash`）：
  - `src/MinminCountdown/ValidationReport.cs` = `F9B73E74A7080C4FFBBC35F3A249E6E3F0B207411B76B85215A82D19D239066A`
  - `Countdown.CSharp.Tests.ps1` = `2920025FC63DBC350DF1CC318F31BC65F41E9C1975DE1E733EED9580FCC69847`
  - `Countdown.Ui.Tests.ps1` = `B693C82E8DE5332063116264F3DCFB99C846364DF8DC8382684BE78B492484A3`
  - `Countdown.Core.Tests.ps1` = `46A84DB449324F325701A1E73AD92CDD74B01B93E6F54175D7C42551C0CEAD85`
  - `Countdown.Validation.psm1` = `D4A13CCC09D79A3D17939F5D998102A11B5814E60D495D94CDBCB64ED6879A12`
  - `verified-current`（对照表内条目与文件哈希）。其余 IntegrityHashes 是否全部钉住当前工作树：**pending**
- 产品改动仍在工作树（未随 `ac7c468` 提交；细节以源码为准，勿回退）：
  - #102–#110：a11y/tab、Popup 二次点击与 Esc、次级窗、夜间 Active `#B8563D`、日期戳、主/悬浮布局、#106 TagSelector/ClampPopup 等。合同：`DESIGN.md`
  - #101 leftover lows：提醒中心右对齐一行 LTR/Tab（`ReminderWindow.cs` ~350–367：知道了 → 稍后 ComboBox → 稍后提醒 → 完成便签 → 打开来源；自定义稍后在下一行）；日历模式「本地时刻」(`ReminderRulesEditorWindow.cs` `_localTimeLabel`)；CheckBox/ComboBox `IsEnabled=False` 时 Surface `Opacity` 0.45（`App.xaml`）
  - 日历选中日 token：`App.xaml` `CalendarDayButtonStyle` / `TemplateBinding Foreground` / `IsSelected` after `IsInactive`（本会话未回退）
  - `verified-current`（抽查源码存在）；issue 验收是否满足 **pending**（套件未绿）

### evidence

- 本会话 transcript：`C:\Users\WINDOWS\.cursor\projects\d-agents-projects\agent-transcripts\ed694369-244b-4dfc-8d86-573b96ce32af`
- 写所有者 transcript：`...\agent-transcripts\ed694369-244b-4dfc-8d86-573b96ce32af\subagents\c14eb23a-7c92-43c5-ae8d-474a21ea2cca.jsonl`
- 暂停前最后一次 **完整跑完** 的官方视觉门（对照 **当时仍钉死的 v19**，不是 v20）在终端 `27588` / `27589`：CSharp 与 Ui 均为失败，`MainReferenceBaseline` `falseGates=[PixelMetricsWithinThresholds, PngHashesMatch]`。空帧 RMSE=0；representative/stress/edit RMSE ~1.95–2.91；filter-open RMSE ~12–14。该结果 **不能** 用来声称 v20 绿。`verified-current`（终端文件） / 对 v20 **pending**
- 17:34 启动的 CSharp 套件（终端 `707521`）在用户暂停时被强杀，**无完成退出码**。可能已是钉 v20 之后的第一次官方跑，但被中断。`verified-current`（进程已停、终端 status failed）
- empty-day 探针（会话叙述，env `MINMIN_COUNTDOWN_EMPTY_DAY_PROBE=1`）：对 **v19** empty-day RMSE=0 / 哈希一致。官方套件禁止带该 env。对 v20 **not-applicable** 作为绿灯证据
- GitHub issue 开闭：本会话早些时候 `gh issue list` 见 clock-countdown **#101–#110 open**、unified-env **#58 open**。手off时刻 **未再查询**。`pending`
- 独立 code-review（Standards [51cf615e](51cf615e-625d-4b1b-88ec-01fed1b8e0e6)、Spec [b5bd56ee](b5bd56ee-3544-4207-a6ba-d48782d411cf)）发生在 lows / 捕获修复 / v20 **之前**。事后审查 **pending**
- 知识库：本任务未检索 `D:\agents\knowledge-base`。`not-applicable`

KB status: not-applicable  
Query: not-applicable  
Lint: not-applicable  
Manifest/log: not-applicable

### local

- 验证命令以 `README.md`「本机验证」为准；WPF 测试必须 `pwsh -STA`。本机宿主 PowerShell 7 规则见用户全局规则；README 示例里仍写着 `D:\Codex\tools\PowerShell\7.6.4` 的矩阵探针路径，以实际 7.6.x 为准。
- 生成基线 env（仅用户再次授权生成时）：`MINMIN_COUNTDOWN_MAIN_BASELINE_GENERATE=1`、`MINMIN_COUNTDOWN_FLOATING_BASELINE_GENERATE=1`。非空目录会拒绝覆盖。`GenerateMainReferenceBaseline` 已拒绝覆盖已存在目录。
- 诊断 env：`MINMIN_COUNTDOWN_CAPTURE_DIAG=1`、`MINMIN_COUNTDOWN_EMPTY_DAY_PROBE=1` 不得用于官方套件。
- 本手off时刻无 Countdown 套件 / ValidateOnly 进程。`verified-current`

### agent

- 根会话不要自己改仓库；恢复实现则 resume 或新写所有者，并遵守用户当前的套件暂停。
- 关闭 issue：脏工作树本身不是 block（`AGENTS.md`）；但 **origin/main 仍是 `ac7c468` 系提交、本批 UI 未进远端**。可关条件是 issue 验收标准 + 可复核证据，不是「本地有 diff」。用户未授权关 issue。
- #101 仅在 leftover lows **被套件/验收证明** 后才可关；#102–#110 同理；**#58 本任务 out-of-scope**。
- #100 IME 会话内视为已 CLOSED，不要重开。header-icon 3px 为 #101 Out of Scope。

### device

- 本会话捕获诊断曾记录本机 **125% DPI（AppliedDPI=120）**。v19 空帧在恢复 96-DPI Render 后对齐；不要再把 PrintWindow 当成功路径。当前 DPI **pending**（本快照未重测）

## 未决阻塞

1. **套件暂停未解除。** 无 v20 官方 CSharp/Ui 完成退出码。Core / SourceValidation / Install `-ValidateOnly` 在后半会话被跳过。
2. 工作树相对 `ac7c468` 的完整脏文件清单 **pending**（未跑 git）。预期含大量 UI 源码、测试、`DESIGN.md`、v19+v20 参考图、可能的 `app\` 构建产物。
3. 其余 `IntegrityHashes` 是否全部等于当前工作树字节 **pending**。
4. GitHub #101–#110 / #58 此刻是否仍 open **pending**。
5. v20 生成后未做独立 Standards/Spec/对抗审查。
6. CSharp adapter JSON **shape SHA** 是否仍有效 **pending**（仅当字段集合变了才重算；版本字符串一般只改值）。

## 下一步建议

1. 读 `AGENTS.md` → `CONTEXT.md` → 视觉工作读 `DESIGN.md`。`git status --short`，保留既有改动。
2. **问用户是否恢复套件。** 未恢复则只做只读/文档/用户点名的事。
3. 恢复后按 `README.md` 最窄相关组合，至少：
   - `git diff --check`
   - `pwsh -NoLogo -NoProfile -STA -File .\Countdown.CSharp.Tests.ps1`
   - `pwsh -NoLogo -NoProfile -STA -File .\Countdown.Ui.Tests.ps1`
   - 视觉钉死/哈希已变：再跑 `Countdown.Core.Tests.ps1`、`Invoke-CountdownSourceValidation.ps1`、`Install-Countdown.ps1 -ValidateOnly`
4. 红则修捕获或产品与 v20 的真实差；**不要覆盖 v20 目录**（生成器会拒）。只有用户再授权才开 v21。
5. 绿且用户明确要求后再 commit/push 到用户 `main`；issue 按 AC 关，仍不要动 #58。
6. 需要评审时再跑 code-review（基点 `ac7c468` 或用户指定）。不要把本 handoff 链进 `MEMORY.md` 或 runbook。

## suggested skills

- 恢复实现：`coding-protocol` + `tdd`（用户点名 `/implement` 时再加 implement）
- 套件仍红：`diagnosing-bugs`
- 绿之后要评审：`code-review`
- 再改 Agent 文档 / DESIGN 合同表述：`writing-for-agents`
- 知识收尾（用户点名 neat-freak 时）：`neat-freak`
- 不要：`private-patch-stack`（本仓是用户 origin/main）
- 不要：未解除暂停时再挂官方套件

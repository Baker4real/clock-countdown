# #209 Tauri 闸门切片候选

状态：`gate-run-passed` / 决定已定稿（停止全面重写；部分采用）。本目录记录 520×390 悬浮窗的隔离候选证据，候选不属于生产项目、安装包或办公室发布路径。

## 交付物

### 真实运行证据（2026-10-09，决定性证据）

- `runtime/floating-countdowns-day.png`、`runtime/long-notes-day-top.png`、`runtime/long-notes-day-bottom.png`、`runtime/notes-search-expanded-day.png`：Tauri/WebView2 真实运行时按悬浮窗四场景捕获的 520×390 帧，由 `WebView2.CapturePreview` 在隔离桌面 `MinminSuiteIso-TauriGate` 上取得。
- `runtime/floating-gate-receipt.json`：闸门回执。`schemaVersion=1`、`status=passed`、单窗 520×390、`criticalInteractionsPassed=true`；四项关键交互（工作区切换、滚动 0→1072、搜索并 Esc、快速新增打开）均经 CDP 鼠标/键盘真实注入通过；每帧带 SHA-256。运行时身份记录 WebView2 `Edg/154.0.4258.62`、切片版本 `0.3.0-n3`、fixture `visual_fixture::floating_reference`。
- `runtime/suite-proof.json`：低影响启动器侧证明。候选 EXE（SHA-256 `E7F368D0…F27665EA`）运行期间权威根 `settings.json` 未变化，EXE 字节在实验前后一致，退出码 0。
- `side-by-side-runtime.png`：左侧 WPF v32 现役基线帧（manifest 绑定），右侧同场景真实 Tauri 帧；输入输出 SHA-256 绑定在 `runtime/runtime-side-by-side.json`。
- `compose-runtime-side-by-side.py`：重渲染真实并排图的可复现脚本。
- 运行入口：TauriGate 阶段经 `tools/Invoke-CountdownSuiteLowImpact.ps1 -Stage TauriGate` 在隔离桌面运行（套件 run `20261009T134140436-800ddbc3`，exit 0）。候选 exe 由切片仓库 `baa05f1f` 以 `cargo build --release --features evidence` 重建（cargo 1.96.1 钉版）。

### 早期静态候选（保留作边界参考，非决定性）

- `candidate.html`：无外部依赖的静态 WebView 视觉切片，使用脱敏 fixture，不读写真实设置。
- `candidate-render.png`：headless Chrome 渲染的 520×390 HTML 帧；只证明 HTML 可渲染。
- `candidate-boundary.json`：候选前端可消费的版本化 typed snapshot/command 边界。
- `render-side-by-side.py` / `side-by-side.png`：v22 参考帧与静态 HTML 的旧并排图，已被真实运行证据取代。

## 对比结论（基于真实运行帧）

| 维度 | WPF 生产基线 | Tauri 候选切片 | 结论 |
|---|---|---|---|
| 第一眼识别 | v32 manifest 绑定帧：三枚窗铬按钮齐全、卡片排版与底部留白符合 DESIGN 合同 | 真实 WebView2 帧整体观感接近，但存在可见差距：头部缺关闭按钮（仅两枚窗铬）、列表出现原生滚动条、底部激励文案被外壳裁切 | 候选视觉上限不高于现行 WPF，且细节 parity 未完成 |
| 交互 | 四项关键交互在生产套件有等价探针 | 工作区切换、滚动、搜索 Esc、快速新增均经 CDP 真实注入通过 | 切片交互骨架可行，但仅覆盖浮窗四场景 |
| 迭代效率 | 修改需经 WPF/XAML、真实窗口与现有验证链 | CSS/HTML 局部迭代成本低；但闸门流程本身发现 pwsh 不等待 GUI 子系统进程的平台级陷阱（已修），构建与证据链尚未纳入发布闭包 | 保留 CSS token/typed boundary 作为研究方向，不迁移生产 |
| 离线部署 | self-contained WPF、PowerShell 安装与办公室验收链已运行 | 回执自报 `offlineWebView2Installation=not-tested`；需要 Tauri 打包、WebView2 Evergreen/离线分支、签名、回滚与新证据闭包 | WebView2 成本与故障面仍未收敛 |
| Core 复用 | C# Core、schema v10、提醒与部署合同是实证基线 | 仅消费脱敏 fixture；无 C# sidecar 或 Rust parity 运行证明 | 不允许候选取得真实设置写入权 |
| 剩余闸门缺口 | — | 回执明示 `userVisualAcceptance=not-performed`、`processRelease=requires-owner-readback`；无托盘、单实例、reduced-motion、无障碍与安装包证据 | 缺口清单化，作为任何未来重启的前置条件 |

## 决定

**停止全面重写；部分采用。** 继续以 C# / .NET 10 WPF + PowerShell 7 为唯一生产路径。真实闸门运行后该决定不变且证据更强：候选在四场景三交互维度达标，但视觉细节 parity、离线 WebView2 安装、打包签名回滚、Core 复用与无障碍证据均未建立。后续若再次推进闸门，须先补齐上述缺口清单再评审。当前候选只保留 CSS token 与显式 typed boundary 的可复用思路。

## 证据边界

- `runtime/` 内帧与回执来自隔离桌面上的真实 Tauri/WebView2 进程；`side-by-side.png` 右侧仍是 headless Chrome 静态渲染，仅 `side-by-side-runtime.png` 是真实运行对比。
- 没有 deployed、office accepted 或 `live verified` 状态；实验未写入真实设置、计划任务、托盘入口或安装目录（suite-proof 记录权威 `settings.json` 前后一致）。
- WPF 侧帧的路径与 SHA-256 绑定在 `runtime/runtime-side-by-side.json`，便于独立回读。

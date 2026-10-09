---
type: decision
title: 敏敏子倒计时技术栈决策（WPF 渐进升级优先）
tags: [architecture, stack, wpf, tauri, react, rust]
intent: "记录现行 WPF 生产栈、按 #189 执行的渐进升级路线，以及 Tauri 切片闸门的候选边界。"
---

# 敏敏子倒计时技术栈决策

## 状态

- 状态：WPF 渐进升级优先；Tauri + React + Rust 仅作为后置闸门候选，未批准全面迁移。
- 记录日期：2026-10-09。
- 父级 PRD：[GitHub #189：WPF 渐进式 UI 精致化与现代化（替代全面重写为先）](https://github.com/Baker4real/clock-countdown/issues/189)。
- 现行生产基线：C# / .NET 10 WPF + PowerShell 7。它继续拥有用户界面、Windows 生命周期、schema v10 设置和办公室发布路径。
- 当前目标栈：在不改变数据、安装和部署合同的前提下，按 #189 的批次逐步改进 WPF；当前没有已批准的替代运行时。
- 闸门条件：完成 WPF 批次后，才用一个隔离的 `520 x 390` Tauri 切片与 WPF 改版做视觉、交互、性能、可访问性和发布成本对比；切片未通过全部门禁前不得写入真实设置、替换生产入口或宣称迁移完成。

这次修订收回了 2026-09-20 文档中把替代栈写成既定路线的表述。Rust 在旧的候选比较中标为“不推荐”，表示它不适合作为当前全量重写的默认路线；保留 Tauri 闸门是为了用一个有界、可回滚的实验验证视觉上限，而不是把 Rust 直接升级为现行宿主。现行解释见 [架构与语言迁移](架构与语言迁移.md) 和 [#189](https://github.com/Baker4real/clock-countdown/issues/189)。

## 约束清单

| 约束 | 现行决定 |
| --- | --- |
| 项目类型 | Windows 桌面倒计时、便签和提醒工具；含主窗口、悬浮窗、提醒中心和系统托盘 |
| 现行生态 | C# / .NET 10 WPF + PowerShell 7；不因视觉批次改写 Windows 运维边界 |
| 闸门生态 | 仅在后置切片中评估 Tauri 2 + React + Rust；不预先创建新生产目录或写入入口 |
| 目标平台 | Windows 10/11；办公室设备可能存在离线或受管网络 |
| 视觉 | 暖粉日间主题、装饰 raster、分格倒计时、圆角卡片、高自由度动效和视觉回归 |
| Windows 能力 | 单实例、托盘、开机启动、窗口位置、拖动、焦点恢复、独立多窗口 |
| 数据 | 兼容 schema v1-v10，只写 canonical schema v10；保留两代合法设置备份 |
| 发布 | 继续使用现行 Windows 安装包、哈希清单、远端事务验收和独立回读；Tauri 离线 WebView2 只属于闸门成本评估 |
| 仓库拓扑 | WPF 与 PowerShell 是生产路径；已有 Tauri 候选目录和隔离工作树不代表生产栈已切换 |
| 供应链 | 现行依赖按精确版本和发布契约管理；闸门候选依赖必须在切片前另行锁定、审查许可证并纳入闭包 |

## 现行生产技术栈

| 槽位 | 采用 | 边界 |
| --- | --- | --- |
| 应用宿主与 UI | C# / .NET 10 WPF | 继续负责主窗口、悬浮窗、提醒中心、Windows 交互和真实渲染 |
| 运维与发布 | PowerShell 7 | 继续负责薄启动 adapter、安装事务、验证、办公室部署和传输账本 |
| 数据 | schema v1-v10，规范写出 schema v10 JSON | 设置读写、两代备份、恢复证据和原子替换合同不变 |
| 验证 | 现有低影响六阶段套件、真实 WPF 验收与发布契约 | 低影响启动器是本机验证入口；不因本票新建第二套框架 |

## Tauri 闸门候选（不构成目标栈）

以下模块属于后置切片。候选中的精确版本以其独立锁文件和验证证据为准，不升级为 WPF 的生产依赖承诺。

| 槽位 | 候选 | 切片边界 |
| --- | --- | --- |
| 桌面壳 | Tauri 2 | 只验证单个 `520 x 390` 悬浮窗、窗口边界、托盘和第二实例发现 |
| UI | React + TypeScript | 只验证一条真实 WebView2 视觉/交互路径，不替换现行 WPF 页面 |
| 构建与样式 | Vite + CSS Variables（Tailwind 可选） | 以 `DESIGN.md` token 为准；不能引入远程运行时资源 |
| 后端 | Rust stable + Tauri commands/events | 只读脱敏 fixture 或隔离设置；不得取得真实生产设置写入权 |
| 系统能力 | Tauri single-instance、tray、autostart 候选插件 | 需与现有激活、启动就绪和部署回执合同逐项对比 |
| 发布 | Tauri MSI/NSIS + offline WebView2 评估 | 只做 candidate/dry-run 成本和故障面验证，不代表 deployed/live verified |

闸门通过至少需要：与 WPF 改版的第一眼识别和关键交互对比、键盘/无障碍与 reduced-motion 证据、首帧和刷新性能、单实例/托盘/窗口边界行为、离线安装和回滚成本、以及不触碰真实设置的完整资源释放。未满足前，WPF 仍是唯一生产路径。

## 明确不采用（当前决策）

- 不进行 Tauri + React + Rust 的全面重写；它只保留为 #189 后置闸门。
- 不把 Rust 作为当前生产宿主。旧比较表中的“不推荐”结论仍适用于立即全量迁移；闸门仅用于验证是否值得继续投入。
- 不改用 Electron、WinUI 3、Avalonia 或 MAUI 作为本票目标。
- 不把 JSON 换成 SQLite；现有 schema v10 和事务边界继续有效。
- 不因闸门准备提前修改生产入口、计划任务、安装目录、办公室部署链或用户设置路径。

## 版本与升级策略

- 现行 WPF 与 PowerShell 依赖继续按各项目文件、发布契约和许可证证据管理。
- Tauri、React、TypeScript、Vite、Rust 或 WebView2 版本只在隔离候选中锁定和验证；生产采用另受视觉、数据、安装和部署门禁约束。
- `candidate`、`dry-run`、`deployed` 与 `live verified` 分开记录；本地切片、合成探针或 `ValidateOnly` 不得替代办公室验收。
- 任何改变 `Countdown.ReleaseContract.psm1` `IntegrityHashes` 覆盖文件的提交，按 README 和项目规则更新静态哈希；本票只改文档，不新增覆盖文件。

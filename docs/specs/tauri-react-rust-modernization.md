---
type: spec
title: 敏敏子倒计时 Tauri 闸门切片候选规格
tags: [spec, tauri, react, rust, migration, wpf, windows]
intent: "定义 #189 WPF 渐进升级完成后可选的 Tauri、React、TypeScript、Vite、Tailwind 和 Rust 悬浮窗对比切片，不把它当作当前生产目标。"
---

> 状态：已关闭的候选规格。#209 闸门已于 2026-10-09 执行并决定停止全面重写，Tauri 切片不进入生产；当前生产宿主仍是 C# / .NET 10 WPF + PowerShell 7。本文件仅作为候选研究的历史记录保留。

## Problem Statement

当前应用可以满足倒计时、便签、提醒、悬浮窗、托盘和办公室部署等功能，但用户界面基于 WPF/XAML。WPF 并非不能实现复杂视觉，但渐变、遮罩、滤镜、响应式布局、装饰层和高频视觉迭代需要大量控件模板、Storyboard、绘制代码和 Windows 专用实现，难以以现代前端的速度持续提升视觉表现。

当前项目还承担一组不能因换 UI 而丢失的行为：北京时间语义、schema v1-v10 数据迁移、两代合法设置备份、提醒 occurrence/cursor/disposition/snooze、单实例激活、托盘、登录启动、悬浮窗位置、提醒中心、无障碍、发布哈希、事务回滚、办公室传输账本和独立远端回读。

当前目标是先改善现有 WPF 的真实使用体验，再用有界实验判断更换运行时是否有足够收益；升级继续保留数据、Windows 能力、部署安全和可复核验收。

## Solution

如果 #189 的 WPF 批次完成后触发闸门，本规格只指导一个隔离的 `520 x 390` Tauri 2 + React + TypeScript + Vite/CSS Variables + Rust 悬浮窗切片。切片用于与 WPF 改版比较视觉、交互、性能、无障碍和发布成本；它不负责所有用户界面，也不取得真实生产设置、计划任务或部署入口的写入权。

保留 schema v10 作为跨版本数据契约，读取旧 schema v1-v10，只写 canonical schema v10。第一阶段不把 JSON 改成 SQLite，也不把文件读写交给前端。PowerShell 继续作为 Windows 安装、办公室部署和证据采集工具，直到新发布链路完成独立验收。

切片采用可回滚的隔离验证：复用现有 WPF 基线和脱敏 fixture，只验证一扇真实 WebView2 窗口、typed command/event、窗口边界、托盘恢复、第二实例发现和关键无障碍路径。切片只能作为 candidate 或 dry-run；不得以浏览器截图、合成探针或本机打包宣称 deployed 或 live verified。

## 当前闸门的验收范围

- 一个真实 Tauri/WebView2 `520 x 390` 悬浮窗，与 WPF 改版在相同场景下保留原始帧、源身份、尺寸和 SHA-256；浏览器截图不能替代桌面运行。
- 逐项验证工作区切换、滚动、搜索、键盘焦点、自动化名、reduced-motion，以及窗口边界、托盘恢复和第二实例发现。
- 对比首帧与刷新性能、近似实施工作量、离线 WebView2 成本和回滚路径；未实测维度明确记录为未知。
- Core 复用须说明实际采用 C# sidecar 还是 Rust fixture parity，并保留对应实证，不能以相同 JSON 结构宣称领域行为等价。
- 给出继续、停止或部分采用的结论，并更新技术栈决策；候选全过程只消费脱敏 fixture 或自己的隔离设置。

## 历史背景：全面迁移用户故事

以下保留旧全面重写草案的研究背景，不属于 #189 或 #209 的现行实施清单。主窗口、提醒中心、真实数据迁移和生产入口切换均在本次切片范围之外。

1. As a countdown user, I want the main window to use a modern component-based interface, so that I can read and edit countdowns without the visual limitations of WPF controls.
2. As a countdown user, I want the warm-pink daytime visual system to be implemented with reusable design tokens, so that all windows remain visually consistent.
3. As a countdown user, I want rich gradients, masks, filters, decorative layers and restrained transitions, so that the application feels expressive without harming readability.
4. As a countdown user, I want the remaining time to remain visually prominent and stable while it updates, so that the numbers never jump, wrap or become hard to scan.
5. As a countdown user, I want to manage multiple named countdowns, so that I can track independent deadlines in one application.
6. As a note user, I want to create, edit, complete and delete notes with dates and tags, so that the application remains a combined countdown and task workspace.
7. As a user with many records, I want status, tag, date and text filters to retain their current semantics, so that a visual rewrite does not change how I find records.
8. As a reminder owner, I want absolute, fixed-interval, relative and typed calendar rules to keep their existing Beijing-time semantics, so that reminders do not move during migration.
9. As a reminder owner, I want each occurrence to keep its stable rule ID, occurrence key, cursor, delivery state, disposition state and snooze state, so that a restart cannot duplicate or resurrect reminders.
10. As a reminder owner, I want a single reminder center for countdowns and notes, so that I can process due items in one predictable place.
11. As a user who rejects a reminder action because persistence failed, I want the item and my draft to remain unchanged, so that a failed save never looks successful.
12. As a Windows user, I want the floating window to remain fixed at 520 x 390 and stay within the current work area, so that it is predictable across monitors and DPI settings.
13. As a Windows user, I want the floating window to be draggable except for interactive controls, so that I can position it without accidentally activating buttons.
14. As a Windows user, I want the tray menu to restore the floating window, open management, reset its position and exit, so that the application remains usable when the main window is hidden.
15. As a Windows user, I want a second launch to activate the existing instance, so that I never get duplicate timers or conflicting settings writers.
16. As a Windows user, I want login startup to show the floating window after a real first-frame render, so that a process that merely started is not reported as ready.
17. As a Windows user, I want the application to work with keyboard navigation, visible focus, screen-reader names and live announcements, so that the modern UI remains accessible.
18. As a user with reduced-motion preferences, I want decorative movement and view transitions to be reduced or disabled, so that the application remains comfortable to use.
19. As an existing user, I want schema v1-v10 settings to load without data loss, so that upgrading the application does not require rebuilding my countdowns or notes.
20. As an existing user, I want settings backup1 and backup2 to remain valid recovery points, so that a corrupt primary file does not silently erase my data.
21. As an existing user, I want a corrupt settings file to offer only explicit recovery or exit, so that the new runtime never continues with fabricated empty state.
22. As an installer, I want the new application to validate its settings migration before committing, so that deployment cannot overwrite a device with semantically changed data.
23. As an offline office device, I want the installer to carry an offline WebView2 installation option, so that the application can be installed without relying on an internet connection.
24. As a release operator, I want application, asset, installer and runtime hashes recorded, so that a deployed candidate can be bound to the exact bytes that were tested.
25. As a release operator, I want failed installation to restore the previous application, startup configuration and evidence state, so that a candidate failure does not strand the device.
26. As a release operator, I want transfer attempts to record bytes, duration, throughput, exit code and failure category, so that deployment performance and partial transfers remain auditable.
27. As a release operator, I want two independent post-commit remote reads, so that a success receipt is not accepted without confirming the live host, user, process, settings fingerprint and startup state.
28. As a maintainer, I want the frontend, Rust backend and Tauri commands to use explicit typed boundaries, so that UI changes cannot bypass persistence or Windows security rules.
29. As a maintainer, I want the Rust domain implementation compared against language-independent fixtures derived from the existing C# implementation, so that a rewrite can prove semantic parity before the new UI is enabled.
30. As a maintainer, I want the new frontend to be tested in a real Tauri/WebView2 window, so that browser-only tests do not hide desktop window, focus or runtime issues.
31. As a maintainer, I want the old WPF application to remain a rollback candidate until the new release is live verified, so that the migration can be reversed without deleting user data.
32. As a maintainer, I want old WPF code removed only after its replacement evidence is archived, so that cleanup does not erase the only reproducible verification path.

## 历史背景：全面迁移实现设想

以下版本和方案属于旧草案，不是已批准或已锁定的生产依赖。当前切片只采用前文的单窗口与隔离边界；autostart 注册、部署改造、完整 Rust 领域移植和新增 CI 仍需独立决定。

- For the optional gate slice, the desktop shell is Tauri 2; the UI is React 19.3 with TypeScript, Vite 8 and Tailwind CSS 4. Design tokens remain CSS Variables owned by `DESIGN.md`; these are candidate dependencies, not current production dependencies.
- Inside that isolated slice, Rust may own the exercised domain seam and Tauri window commands. It does not replace the current WPF owner of real settings, reminder scheduling, deployment or startup acceptance.
- React owns rendering, drafts, local interaction state and animation. React cannot access arbitrary files, registry, shell commands, scheduled tasks or network resources.
- The gate slice uses one `520 x 390` Tauri window. Separate management and reminder-center windows are explicitly out of scope until a later decision.
- Frontend/backend communication uses versioned discriminated command and event messages. Rust validates command shape, source identity, permissions and current state before applying a mutation.
- The frontend receives a state snapshot and renders it. Rust emits the next snapshot only after the persistence transaction succeeds. Persistence failure leaves disk, in-memory state, draft and reminder entry unchanged.
- Schema v1-v10 remain readable and canonical schema v10 remains the only write format. Rule IDs, occurrence keys, cursors, delivery/disposition records and snooze records retain their existing meanings.
- The settings store continues to use same-directory temporary files, atomic replacement, a path-scoped cross-process lock, backup1/backup2 rotation and explicit recovery evidence.
- The first migration keeps JSON storage. SQLite is explicitly deferred until an independent performance or data-volume requirement exists.
- The old C# behavior is converted into language-independent fixtures. Rust and the existing C# verifier must agree on canonical JSON, time conversion, recurrence, migration, failure injection and backup outcomes before the new UI writes real settings.
- The UI must preserve the existing domain glossary: countdown, note, tag, occurrence, cursor, disposition, snooze, current Beijing time, single-instance activation, startup-ready and finalized-ready.
- The visual layer may use CSS gradients, masks, filters, blend modes, SVG, Canvas and WebGL for decoration, but critical text and controls remain semantic DOM controls.
- Animation defaults to short feedback transitions and stable number updates. `prefers-reduced-motion` disables decorative movement, scale and parallax.
- The tray provides restore floating window, open management, reset position and exit. The second instance only sends a bounded activation request and never changes filters, drafts or settings.
- Tauri single-instance and autostart plugins are used for discovery and registration; Rust owns the application protocol and startup evidence.
- WebView2 uses Evergreen distribution for security updates. The Windows installer supports an offline runtime installer for office devices; fixed runtime is a separate decision requiring an explicit compatibility need.
- The release package contains only the built Tauri application, runtime assets, installer/runtime prerequisites and required deployment evidence. Node, Rust, npm cache and source trees are not runtime assets.
- The existing PowerShell deployment and transfer ledger remain the external Windows operations boundary during migration. Their acceptance contract is adapted to Tauri artifact identity, autostart state and Rust startup receipts.
- The work is staged as the WPF batches in #189, followed only if justified by the isolated Tauri slice in #209. Each candidate step has a rollback point; production entry, real user settings and office deployment remain on WPF.
- If the gate is approved, the candidate repository layout may separate frontend, Rust application modules, runtime assets, validation fixtures and deployment tooling. Module names describe boundaries; implementation may choose different physical files.
- The CI pipeline adds Node lockfile installation, TypeScript checks, frontend tests/build, Rust formatting/lint/tests, Tauri Windows packaging and migration fixture checks to the existing Windows workflow.
- Release state is recorded separately as candidate, dry-run, deployed and live verified. A local build, synthetic probe or unit test alone cannot close the migration.

## 历史背景：全面迁移测试设想

以下是未来全面迁移才会涉及的测试面；当前闸门以“当前闸门的验收范围”为准，不能从本节推导生产切换授权或宣称这些测试已执行。

- Tests assert externally observable behavior at the highest available seam. The primary seam is the real Tauri application process with its typed command/event boundary and real Windows windows; lower-level Rust tests support deterministic domain and persistence failures.
- The existing schema and deployment fixtures are preferred over inventing a parallel test oracle. The C# verifier remains a comparison oracle only until Rust parity is established.
- Rust domain tests cover Beijing-time conversion, countdown sorting, note/tag filters, rule validation, calendar occurrence generation, stable IDs, cursor/disposition/snooze transitions and failure preservation.
- Rust persistence tests cover missing primary, valid primary precedence, invalid primary recovery choices, backup rotation, atomic replacement, lock contention, corrupted/truncated/unknown JSON, recovery evidence and canonical round trips.
- Cross-language fixture tests compare Rust and C# outputs for schema v1-v10 migration, canonical schema v10, occurrence keys, cursor state, reminder delivery/disposition and backup outcomes.
- React tests use Playwright against the built Tauri/WebView2 app for real window rendering, keyboard paths, focus, scrolling, date selection, filters, draft preservation and command failures.
- Visual tests capture the main window, floating countdown workspace, floating notes workspace and reminder center from real rendering and compare against the current manifest-bound baselines.
- Accessibility tests verify names, labels, focus order, focus visibility, live announcements, keyboard-only operation, contrast and reduced-motion behavior.
- Windows integration tests verify single-instance activation, tray restore, autostart registration, window position clamping, process identity, first-frame readiness and finalized readiness.
- Installer tests verify WebView2 missing/present branches, offline installation, upgrade, uninstall, failed candidate rollback, package hashes and runtime directory contents.
- Deployment tests use the existing SSH/SCP ledger and independent readback contracts, adding Tauri artifact identity, autostart state, Rust process identity and new readiness receipts.
- A test is good when it can fail because a user-visible behavior or release guarantee is wrong. Tests that only mirror component internals or CSS class names are not required.

## Out of Scope

- Replacing the WPF main window, reminder center, production settings owner, startup entry or deployment path as part of this gate slice.
- Shipping a web/SaaS version or adding SSR, SEO, server-side rendering or cloud synchronization.
- Supporting macOS, Linux, mobile or browser-only deployment in the first migration.
- Replacing JSON with SQLite without a separate performance or data-volume requirement.
- Reintroducing a night theme or adding a theme switcher; the current single daytime warm-pink direction remains the contract.
- Adding a remote backend, account system, telemetry service or third-party content feed.
- Loading remote HTML, JavaScript, fonts or images into the application UI at runtime.
- Rewriting the Windows deployment controller into Rust before the Tauri runtime and artifact contract are proven.
- Deleting the old WPF implementation before new production evidence, rollback evidence and data-migration evidence are archived.
- Treating a successful local build, synthetic probe, package creation or `ValidateOnly` run as office deployment or live verification.
- Adding animation libraries before CSS, Web Animations API and React View Transitions are shown to be insufficient by a concrete requirement.

## Further Notes

- The current production implementation is still C# / .NET 10 WPF. This spec defines a conditional comparison candidate; it is not an approved replacement target and does not claim that a rewrite has started or that a new application is deployed.
- The existing project design contract remains authoritative for colors, typography, hit areas, dimensions, accessibility and reference-image binding during the WPF batches and any later slice.
- The first work remains the WPF baseline and batches in #189. Only after those batches may #209 start the isolated Tauri slice; it must not delete WPF or change the live settings path.
- The migration should retain a clear evidence chain for each stage: source revision, dependency lockfile, fixture result, visual baseline hashes, package manifest, transfer ledger, deployment binding and independent live readback.
- The candidate spec is cross-referenced by the repository's modernization plan and stack decision documents; those documents record WPF as current and explain the gate's evidence and rollback boundary.


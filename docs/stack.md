---
type: decision
title: 敏敏子倒计时现代化技术栈
tags: [architecture, stack, tauri, react, rust]
intent: 记录全面重构后应用运行时、前端、桌面壳、构建和验证技术栈。
---

# 敏敏子倒计时现代化技术栈

## 状态

- 状态：已确认目标栈，尚未开始迁移。
- 记录日期：2026-09-20。
- 当前生产基线：C# / .NET 10 WPF + PowerShell 7；该基线在迁移完成前继续有效。
- 目标运行时：Tauri 2 桌面壳，React 前端，Rust 应用核心。
- 迁移原则：全面替换应用 UI 与宿主代码，但保留 schema v10 数据契约和可回滚的旧生产发布路径，直到新版本完成独立验收。

## 约束清单

| 约束 | 决定 |
| --- | --- |
| 项目类型 | Windows 桌面倒计时、便签和提醒工具；含主窗口、悬浮窗、提醒中心和系统托盘 |
| 生态 | Web UI + Rust 桌面宿主，不采用 SSR 或 SEO |
| 目标平台 | Windows 10/11；办公室设备可能存在离线或受管网络 |
| 视觉 | 暖粉日间主题、装饰 raster、分格倒计时、圆角卡片、高自由度动效和视觉回归 |
| Windows 能力 | 单实例、托盘、开机启动、窗口位置、拖动、焦点恢复、独立多窗口 |
| 数据 | 兼容 schema v1-v10，只写 canonical schema v10；保留两代合法设置备份 |
| 发布 | Windows 安装包、离线 WebView2 安装能力、哈希清单、远端事务验收和独立回读 |
| 仓库拓扑 | 单仓；前端与 `src-tauri` 同仓，PowerShell 部署工具暂时保留 |
| 包管理 | npm + `package-lock.json`；不引入 pnpm workspace |
| Node | 当前主机 Node 24.19.0 / npm 12.0.2；通过版本文件和 CI 固定构建版本 |
| CI | 现有 Windows GitHub Actions；新增 Node、Rust、Tauri 打包和迁移验证步骤 |
| 无障碍 | 键盘可达、UI Automation 可用、屏幕阅读器名称、焦点可见、非颜色单独传达状态 |
| 供应链 | 依赖版本锁定、许可证清单、构建产物哈希；不从远程页面加载应用 UI |

## 定稿技术栈

| 槽位 | 采用 | 核实版本 | 证据与边界 |
| --- | --- | ---: | --- |
| 桌面壳 | Tauri | core 2.11.5 | 官方架构使用系统 WebView，支持 Rust 后端和任意 HTML/CSS/JS 前端；目标是小体积和系统集成。<https://v2.tauri.app/concept/architecture/> |
| Tauri CLI | `@tauri-apps/cli` | 2.11.4 | npm `latest`；只作为本地开发和 CI 构建工具。<https://www.npmjs.com/package/@tauri-apps/cli> |
| UI | React | 19.3.0 | React 19.3 已稳定发布，包含稳定的 View Transitions。<https://react.dev/blog/2026/09/09/react-19-3> |
| 类型 | TypeScript | 6.0.3 | npm 稳定版本条目；实施时由 lockfile 固定 patch。<https://www.npmjs.com/package/typescript> |
| 构建 | Vite | 8.3.0 | npm `latest`；Vite 8 使用 Rolldown，支持 Node 20.19+ 或 22.12+。<https://vite.dev/blog/announcing-vite8> |
| 样式 | Tailwind CSS + CSS Variables | 4.3.3 | npm `latest`；设计 token 以 CSS Variables 为单一来源，Tailwind 只承担布局和基础状态组合。<https://www.npmjs.com/package/tailwindcss> |
| 动效 | CSS Transitions、Web Animations API、React View Transitions | 浏览器内置 | 先不引入动画运行时；所有动效必须尊重 `prefers-reduced-motion`。 |
| 后端 | Rust stable + Tauri commands/events | 版本在首个切片锁定 | 负责领域状态、持久化、窗口和 Windows 系统能力；前端不直接访问文件系统或系统 API。 |
| 数据 | `serde` / `serde_json`，schema v10 JSON | 迁移切片锁定 | 先保留 JSON 数据契约和原子备份；SQLite 不进入第一阶段。 |
| 单实例 | `tauri-plugin-single-instance` | Tauri 2 插件线 | Windows 受支持；第二实例只发送受约束的恢复悬浮窗请求。<https://v2.tauri.app/plugin/single-instance/> |
| 开机启动 | `tauri-plugin-autostart` | Tauri 2 插件线 | Windows 受支持；安装器验收注册状态和实际启动回执。<https://v2.tauri.app/plugin/autostart/> |
| 托盘 | Tauri tray API | Tauri 2 | 主窗口、悬浮窗和退出动作均由受限命令路由。<https://v2.tauri.app/learn/system-tray/> |
| 浏览器验收 | Playwright | 1.63.0 | 真实页面交互、键盘、截图和可访问性回归。<https://www.npmjs.com/package/@playwright/test> |
| 包管理 | npm | 12.0.2（当前主机） | 使用 `npm ci`；仓库提交 `package-lock.json`。 |

## 明确不采用

- 不采用 Electron：UI 能力满足，但会额外捆绑 Chromium，安装包、内存和更新面不适合本工具。
- 不采用 WinUI 3 作为目标 UI：它是优秀的原生 Windows 路线，但 XAML 仍不是本项目追求的最高视觉自由度。
- 不采用 Avalonia：跨平台收益不是当前需求，Windows-only 目标不值得承担额外平台差异。
- 不采用 MAUI：目标不是移动端或多端共享 UI。
- 不采用 SQLite 作为第一阶段存储：现有 JSON 事务和 schema v10 已是稳定数据边界，先避免无收益的数据迁移。
- 不把 Tailwind 类名当作设计系统：颜色、间距、圆角、焦点和对比度 token 必须由 CSS Variables 和 `DESIGN.md` 统一定义。

## 版本与升级策略

- `package-lock.json` 是前端依赖的唯一解析结果；禁止手工编辑 lockfile。
- `rust-toolchain.toml` 在第一个 Tauri 切片中固定 Rust stable 版本；CI 与本机使用同一 toolchain。
- Tauri、React、Vite、Tailwind 和 Playwright 只在独立升级任务中升级；升级必须通过完整视觉、数据、安装和部署门禁。
- WebView2 使用 Evergreen；离线办公室安装包选择 Tauri 的 offline installer 方案。固定 WebView2 runtime 只有在受管环境明确要求版本冻结时才启用。


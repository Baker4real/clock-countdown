# #209 Tauri 闸门切片候选

状态：`candidate` / `dry-run`。本目录只记录一个 520×390 悬浮窗的隔离候选，不属于生产项目、安装包或办公室发布路径。

## 交付物

- `candidate.html`：无外部依赖的静态 WebView 视觉切片，使用脱敏 fixture，不读写真实设置。
- `candidate-render.png`：由本地 headless Chrome 渲染的 520×390 HTML 帧；它证明 HTML 可渲染，不证明 Tauri/WebView2 生产运行。
- `candidate-boundary.json`：候选前端可消费的版本化 typed snapshot/command 边界；当前没有 Rust/Tauri 运行时实现。
- `render-side-by-side.py`：重渲染并排图、回写各输入与输出 SHA-256 的可复现脚本。
- `side-by-side.png`：左侧为当前 WPF v22 参考帧，右侧为候选 HTML 静态渲染。两侧都标注来源和证据状态。

## 对比结论

| 维度 | WPF 生产基线 | Tauri 候选切片 | 结论 |
|---|---|---|---|
| 第一眼识别 | 有 v22 manifest 绑定的真实 WPF 参考帧，当前批次继续收敛暖粉主题、分格数字和 520×390 外壳 | 能快速表达 CSS token、渐变和分格布局，但只是静态 HTML，尚未有真实 WebView2/Tauri 帧 | 候选没有足够证据证明视觉上限更高 |
| 迭代效率 | 修改需经过 WPF/XAML、真实窗口和现有验证链 | CSS/HTML 局部迭代成本低；没有依赖锁定、构建、无障碍和窗口生命周期证据 | 保留 CSS token/typed boundary 作为研究方向，不迁移生产 |
| 离线部署 | 现有 self-contained WPF、PowerShell 安装和办公室验收链已运行 | 需要 Tauri 打包、WebView2 Evergreen/离线分支、签名、回滚和新证据闭包；本候选没有安装包 | WebView2 成本和故障面仍是未收敛风险 |
| Core 复用 | C# Core、schema v10、提醒和部署合同是实证基线 | 仅消费脱敏 JSON fixture；没有 C# sidecar 或 Rust parity 运行证明 | 不允许候选取得真实设置写入权 |

## 决定

**停止全面重写；部分采用。** 继续以 C# / .NET 10 WPF + PowerShell 7 为唯一生产路径。后续若再次推进闸门，只能在隔离目录补齐真实 Tauri/WebView2 运行、Rust/C# fixture parity、键盘/无障碍、reduced-motion、首帧、单实例、托盘、离线安装和回滚证据后重新评审。当前候选只保留 CSS token 与显式 typed boundary 的可复用思路。

## 证据边界

- `side-by-side.png` 是候选对比证据；右侧来自 headless Chrome 对静态 HTML 的渲染，不能证明 Tauri/WebView2 已运行。
- 没有 deployed、office accepted 或 `live verified` 状态；没有写入真实设置、计划任务、托盘入口或安装目录。
- 左侧 WPF 参考帧的路径和 SHA 绑定记录在 `candidate-boundary.json`，便于独立回读。


# v27 主窗口参考帧

8 张 `1165 × 1350` 真实 WPF 帧由官方隔离 `BaselineGenerate` 运行 `20261008T233555510-1521f509` 生成，构建来源为集成候选 `58c38cbc8e13a70961e07d6958e8eafec824035e`。每帧的 PNG SHA-256、透明度、场景和 17 项渲染源码绑定见 [manifest.json](manifest.json)。

本版本绑定已修复的确认框 Esc 处理及浮窗两行主标题。主窗口八帧与 v26 的原始 RGBA 全部一致，保留按 v25 实拍推导的边线 canary；独立回读核验了文件、源码 aggregate、应用身份、尺寸和 alpha。

PNG 未缩放或后处理。生成与文件复核不替代另行运行的 CSharp/UI 回归或正式 Clean 构建；v23 至 v26 的历史生成与失败证据原样保留。

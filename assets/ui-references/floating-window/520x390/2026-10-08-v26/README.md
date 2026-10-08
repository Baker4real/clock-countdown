# v26 悬浮窗参考帧

4 张 `520 × 390` 真实 WPF 帧由官方隔离 `BaselineGenerate` 运行 `20261008T230433088-b4cf11a8` 生成，构建来源为集成候选 `3b7d0a5132c2fa9bac6bb7c2c3b95ba5a20e9fa1`。倒计时、长便签顶部/底部和搜索展开四场景的 PNG SHA-256、透明度及 17 项渲染源码绑定见 [manifest.json](manifest.json)。

本版本纳入日历子项模板的样式传递修复。独立回读验证文件、源码 aggregate、应用身份、尺寸与 alpha；四张原始帧的 RGBA 与 v25 全部一致。原始 alpha 和固定尺寸保留，PNG 没有缩放或后处理。

生成和文件复核不替代另行运行的 CSharp/UI 回归或正式 Clean 构建。v23、v24 和尚未完成整套验收的 v25 均保留，未覆盖历史 PNG 或 manifest。

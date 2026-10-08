# 未验收的 v23 捕获

本目录保留 2026-10-08 的首次真实 WPF 改版捕获现场，不是现役验收基线。

`BaselineGenerate` run `20261008T195235411-4c119c20` 成功写出文件，尺寸、PNG 哈希和源码绑定独立回读通过；逐图检查却发现筛选弹层合成颜色失真、分段指示条处于过渡途中。随后独立 `CSharp` run `20261008T195551088-6e67dc63` 退出码 1，`CrossInterfacePageSpecificComposition` / `CrossInterfaceIntegration` 未通过，`Ui` 按红即停规则跳过。

保留原始 PNG 和 manifest，不覆盖、不把生成成功解释为视觉验收通过。现役捕获修复和独立复验转入 `2026-10-08-v24`；旧像素阈值不放宽。

---
type: report
title: "#251 工作台密度与悬浮滚动上下文实拍"
tags: [evidence, visual, density, scrolling, dpi]
intent: 保存 #251 的首选、最小尺寸与 DPI 变化窗口和悬浮滚动提示的同版真实截图，区分视觉证据与最终组合验收。
---

# #251 工作台密度与悬浮滚动上下文实拍

本目录保留 34 张真实 WPF 截图：管理窗口三个尺寸/DPI 档各五种状态，共 15 张；悬浮窗 18 个滚动与交互状态加菜单弹层，共 19 张。PNG 从原捕获目录逐字节复制，[manifest.json](manifest.json) 记录来源、尺寸和 SHA-256，全部复制后独立回读一致。

当前记录为候选证据。行为检查和基线生成已完成，独立 CSharp/Ui 复验与 #253 集成候选一起完成；截图本身不代表该阶段已验收、已发布或已部署。

## 来源与适用范围

- 源码为 `fb1b1477` 加接手时已有的便签空态上边距修正；截图尚未包含后续 #253 展开箭头与提醒中心排版改动。没有将这些截图标为后续源码的实拍。
- 构建采用现有 worktree 的增量入口。行为候选 DLL SHA-256 为 `76AA87ED8EFED5F6255705F8C5ABD0E39D494B6EC12BCFF43B96A8AE83EB03DC`。
- 原低影响套件 run 为 `20261109T150631478-666819f1`：Core、SourceValidation、InstallValidate 退出 0，CSharp/Ui 的其余断言已通过，均因参考基线尚未重拍而以 75 退出。
- 密度捕获来源为 `logs/ui-captures/20261109T071828819-density/`，滚动提示来源为 `logs/ui-captures/20261109T071840746-scroll-hint/`。全部在官方隔离桌面生成，没有截取用户桌面。
- 后续 BaselineGenerate run `20261109T153328802-e84f216d` 退出 0，生成 v72 双 manifest。复制证据前逐项核对 v72 的主窗与悬浮窗源码绑定，和捕获界面源码一致。

## 管理窗口

每个档位都包括倒计时首屏、便签顶部、便签滚到底、搜索与多选。首选和最小档按 96 DPI 渲染；DPI 档通过真实 WM_DPICHANGED 到 216 DPI，按该 DPI 光栅化为 1944×2250 像素。

| 档位 | 倒计时 | 便签顶部 | 滚到底 | 搜索 | 多选 |
| --- | --- | --- | --- | --- | --- |
| 864×1000 DIP | [截图](density/864x1000-countdowns.png) | [截图](density/864x1000-notes.png) | [截图](density/864x1000-notes-scrolled.png) | [截图](density/864x1000-notes-search.png) | [截图](density/864x1000-notes-selected.png) |
| 720×560 DIP | [截图](density/720x560-countdowns.png) | [截图](density/720x560-notes.png) | [截图](density/720x560-notes-scrolled.png) | [截图](density/720x560-notes-search.png) | [截图](density/720x560-notes-selected.png) |
| 216 DPI | [截图](density/dpi-216-countdowns.png) | [截图](density/dpi-216-notes.png) | [截图](density/dpi-216-notes-scrolled.png) | [截图](density/dpi-216-notes-search.png) | [截图](density/dpi-216-notes-selected.png) |

最小窗口保留已采纳的整窗滚动兜底；当前图片显示窗口的一部分，操作的可达性由探针逐个滚进可视区验证，不能仅凭这一帧推断整页同时可见。密度、13 DIP 字号、命中区、固定工具条与无重叠判定以 `MainWorkbenchDensityReclaimed` 为准。

## 悬浮窗

悬浮帧按 96 DPI 渲染为 520×390 像素，菜单按自身 132×107 尺寸单独渲染。

| 场景 | 证据 |
| --- | --- |
| 空便签，无滚动提示 | [空态](scroll-hint/notes-empty.png) |
| 长便签顶部、中段与底部 | [顶部](scroll-hint/notes-long-top.png)、[中段](scroll-hint/notes-long-middle.png)、[底部](scroll-hint/notes-long-bottom.png) |
| 仅首尾固定留白被卷出 | [首部留白](scroll-hint/notes-long-leading-blank.png)、[尾部留白](scroll-hint/notes-long-trailing-blank.png) |
| 滚轮、thumb 与键盘 | [滚轮](scroll-hint/notes-long-after-wheel.png)、[thumb](scroll-hint/notes-long-after-thumb.png)、[PageDown](scroll-hint/notes-long-after-page-down.png)、[PageUp](scroll-hint/notes-long-after-page-up.png) |
| 搜索展开 | [顶部](scroll-hint/notes-search-expanded-top.png)、[底部](scroll-hint/notes-search-expanded-bottom.png) |
| 快速新增展开 | [顶部](scroll-hint/notes-quick-add-top.png)、[中段](scroll-hint/notes-quick-add-middle.png) |
| 菜单打开与关闭 | [打开](scroll-hint/notes-menu-open.png)、[菜单弹层](scroll-hint/notes-menu-open-popup.png)、[关闭](scroll-hint/notes-menu-closed.png) |
| 倒计时溢出 | [顶部](scroll-hint/countdowns-overflow-top.png)、[底部](scroll-hint/countdowns-overflow-bottom.png) |

`FloatingScrollContextHintFollowsOverflow` 负责方向、边界与滚轮/thumb/键盘结果，截图供人工核对渐隐位置、玩偶、命令区与菜单。已复核的代表帧在 manifest 内明确标记，其余图片不冒称逐张人工检查。

## 视觉基线比较

v72 与 v71 的 14 帧状态指纹全部一致。管理窗口九帧的变化来自表单和列表几何；悬浮窗两个倒计时帧逐字节一致，三个便签帧的主要差异位于对应边缘渐隐带。对比这三帧的内部区域，只有内容呈现器左界 x=59 的单列边界像素变化，内部正文像素不变。基线不会用来掩盖其他行为失败，最终结论以集成候选的完整验证为准。

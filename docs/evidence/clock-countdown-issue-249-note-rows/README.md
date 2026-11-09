---
type: report
title: "#249 便签卡关键状态同版实拍"
tags: [evidence, visual, acceptance]
intent: 将最终候选已有的专项实拍公开归档，供 issue 逐项验收和回读。
---

# 便签卡关键状态同版实拍

本批是 v75 最终候选的既有原始实拍，来自官方隔离 BaselineGenerate `20261109T203340606-a12d0d4a`，尺寸 864×1000，逐张 SHA-256 与复制核对见 [manifest.json](manifest.json)，原始元数据见 [note-rows.json](note-rows.json)。候选 DLL 为 `FC68217948ACE2FA85E30FC4ED0A3A55DD964A130F4D242B2E9DDAE534A4031C`，和本机完整回归、全量 Clean + R2R 产物相同。

发布关票前已逐张读取原图，复核文字、边界、动作和状态。卡片在滚动视口之外的自然裁切沿用既定规则；卡内内容不以截图代替几何与行为断言。正式独立 SourceValidation 中 `NoteRowContentGeometryBehavior` 为 true，同版完整回归七阶段全部退出 0；[最终验收报告](../clock-countdown-issue-253-final-surfaces/README.md)保留原始回执及其哈希。本次归档仅复制已有图片，不修改产品或捕获逻辑。

- [未完成五张，纵向滚动条在顶部：无标签短正文、140 字长正文、五枚标签与两条提醒](note-rows-top.png)
- [未完成滚到底部：每 15 分钟提醒的来源卡与单标签短卡](note-rows-bottom.png)
- [「复盘」搜索：长正文与五标签两张](note-rows-search.png)
- [多选两张：批量操作条与选中卡底色，卡内几何不变](note-rows-selected.png)
- [打开提醒来源：来源卡 2 DIP ActiveBrush 强调、编辑按钮获焦](note-rows-reminder-source.png)
- [已完成段：删除线、弱化正文与暂停的提醒摘要](note-rows-completed.png)

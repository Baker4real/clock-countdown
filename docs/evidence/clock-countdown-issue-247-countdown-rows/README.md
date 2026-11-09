---
type: report
title: "#247 倒计时行关键状态同版实拍"
tags: [evidence, visual, acceptance]
intent: 将最终候选已有的专项实拍公开归档，供 issue 逐项验收和回读。
---

# 倒计时行关键状态同版实拍

本批是 v75 最终候选的既有原始实拍，来自官方隔离 BaselineGenerate `20261109T203340606-a12d0d4a`，尺寸 864×1000，逐张 SHA-256 与复制核对见 [manifest.json](manifest.json)，原始元数据见 [countdown-rows.json](countdown-rows.json)。候选 DLL 为 `FC68217948ACE2FA85E30FC4ED0A3A55DD964A130F4D242B2E9DDAE534A4031C`，和本机完整回归、全量 Clean + R2R 产物相同。

发布关票前已逐张读取原图，复核文字、边界、动作和状态。卡片在滚动视口之外的自然裁切沿用既定规则；卡内内容不以截图代替几何与行为断言。正式独立 SourceValidation 中 `CountdownRowDeadlineAndSecondaryDeleteBehavior` 为 true，同版完整回归七阶段全部退出 0；[最终验收报告](../clock-countdown-issue-253-final-surfaces/README.md)保留原始回执及其哈希。本次归档仅复制已有图片，不修改产品或捕获逻辑。

- [七行，纵向滚动条在顶部：普通主项、30 字名称、五枚标签、999 天](countdown-rows-top.png)
- [七行滚到底部：北京时区截止行、百万天、已到期次项](countdown-rows-bottom.png)
- [「长期」筛选：百万天主项与已到期次项，无滚动条](countdown-rows-longest-primary.png)
- [「收尾」筛选：已到期主项](countdown-rows-expired-primary.png)
- [便签行：与倒计时行同一套编辑/删除](note-row-actions.png)

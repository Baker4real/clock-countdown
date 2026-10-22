---
type: reference
title: 悬浮窗玩偶素材来源
tags: [assets, plush, provenance]
intent: 记录悬浮窗玩偶素材的官方来源、派生过程和校验值。
---

# 悬浮窗玩偶素材来源

这些素材只用于本机、非商用的个人倒计时应用，不构成任何再发布或商用授权。

## 来源

- 系列页：<https://www.sanrio.com/collections/pitatto-friends>
- 美乐蒂可摆姿势玩偶：<https://www.sanrio.com/products/my-melody-posable-standing-display-plush-medium>
- 大耳狗可摆姿势玩偶：<https://www.sanrio.com/products/cinnamoroll-posable-standing-display-plush-medium>
- 库洛米可摆姿势玩偶：<https://www.sanrio.com/products/kuromi-posable-standing-display-plush-medium>

## 旧 PowerShell 回归夹具

以下六张商品素材只供 `Countdown.App.psm1` 的兼容回归使用，不属于现役悬浮窗视觉基线，不复制到正式 `app`，也不安装到运行目录。

| 文件 | 官方原图 |
| --- | --- |
| `floating-plush-mymelody-front.png` | `zz-2506411531_MM_--1_1600x.jpg` |
| `floating-plush-mymelody-pose.png` | `zz-2506411531_MM_--6_1600x.jpg` |
| `floating-plush-cinnamoroll-front.png` | `zz-2506411531_CN_--1_1600x.jpg` |
| `floating-plush-cinnamoroll-pose.png` | `zz-2506411531_CN_--6_1600x.jpg` |
| `floating-plush-kuromi-front.png` | `zz-2506411531_KU_--1_1600x.jpg` |
| `floating-plush-kuromi-pose.png` | `zz-2506411531_KU_--6_1600x.jpg` |

以上六张商品素材都由对应官方商品图做了非生成式的连通白底去除、裁切与 256 x 256 缩放；未使用生图或重绘。它们留在仓库中只为保持旧 PowerShell 回归可执行，不能作为后续视觉实现的参考目标。

## 参考图派生姿态

以下三张角色姿态是后述日间外框、标题栏、角色无文字组件（现役 `520 x 390` 运行时源资产）的派生输入。#231 起它们只作悬浮窗视觉基线 manifest 的 sourceBinding 哈希输入：只留在仓库，不进办公室发布包，也不再装进正式 `app`，运行时从不加载。悬浮窗整体视觉验收以 [DESIGN.md](../../DESIGN.md) 绑定的现役 manifest 为准；`component-day-card-blank.png` 继续随正式 `app` 发布并参与视觉回归，但方向 B 的倒计时暖纸层级和便签台历索引不再把它渲染为卡片背景。

- 参考图：`D:\Codex\.codex\run-evidence\nodesek-image-generation\20260727\floating-window-three-mascots-integration.png`
- 参考图生成方式：Nodesek `gpt-image-2` 图片编辑接口；请求元数据保存在同目录的 `floating-window-three-mascots-integration.metadata.json`。
- 参考图 SHA-256：`7B28C015B28E8FCAB3BBA6D88E61ACFECC8A9B1B0252AA56431C8C2DCF30CB95`
- 提取证据：`D:\Codex\.codex\run-evidence\clock-countdown-visual-parity\20260728-reference-extraction`
- 方法：使用 SAM 2.1 `facebook/sam2.1-hiera-small` 的框选与正负点提示取得角色区域，再用 rembg 的 `birefnet-general`（BiRefNet）精修毛绒边缘 alpha。
- 像素边界：三张 PNG 的 RGB 直接取自参考图；分割流程只生成和精修 alpha，并按角色可见边界裁切，没有生成、补绘或重绘 RGB。

| 文件 | SHA-256 |
| --- | --- |
| `floating-plush-mymelody-reference.png` | `54F7C2EB62D48783D5977CF68AA795757EC891BE96298351D88784866ED98B07` |
| `floating-plush-cinnamoroll-reference.png` | `9B4E007395F1AF3B20E2EC492C22C8A4FB1F205473A2AD16AFD71F457B44E6B9` |
| `floating-plush-kuromi-reference.png` | `52D0814F2A764EE1262F8919FE70BD535CC14BAFC399295532AAEEFA352384DC` |

## 日间无文字组件

- 组件构建证据：`D:\Codex\.codex\run-evidence\clock-countdown-raster-components\20260728T151344`
- 输入：上述参考图的 `520 x 390` 缩放图、旧版真实 WPF 截图及三张参考姿态 alpha。
- 外框组件保留参考图的非内容 RGB，并沿用真实 WPF 窗口 alpha；标题、卡片、金额和角色槽位均先清空。
- 标题与卡片组件保留参考图底板，使用横向邻域补齐文字、图标和角色遮挡区；角色组件只复制参考图 RGB，并使用三张参考姿态的最大连通 alpha。
- `audit.json` 证明禁止的标题、页签、图标、倒计时和金额区域没有参考文字像素进入静态组件，0/1 卡和便签反证图的变化也没有越出动态区。
- 正式运行证据：`D:\Codex\.codex\run-evidence\clock-countdown-raster-runtime\20260728T154837\24-visual-release`。真实 WPF 白底 RGB 指标为 RMSE `0.0751723102`、MAE `0.0179606893`；它通过本任务门槛，但不等同于逐像素完全相同。

| 文件 | SHA-256 |
| --- | --- |
| `component-day-outer-shell.png` | `A4F2F78163F8AA5217C4161F0EE2BFB924686E8BED5768C527F3B5C7D6CC4924` |
| `component-day-header-blank.png` | `857B570F1CD47C29F1C2744251DE1B1C7B4D9D921944BB79E974B0B60B26C1B1` |
| `component-day-card-blank.png` | `253EF68502B1F798E37DDF02BF17F9BEE08D818B8AEE76EA287A4403226FB1C8` |
| `component-day-mascots.png` | `D000285BFFC2CBCF3411C11407FF691E531A8602E9D3E8F3FD68EBFC4ADC0726` |

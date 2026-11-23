# #339 滑动胶囊设计合同与 v86 双窗视觉基线

本目录保存 GitHub #339 需要长期复核的视觉证据、逐帧比对和来源绑定。现役版本为 `2026-11-23-v86`，上一版为 `2026-11-22-v85`。产品视觉实现来自已合并的 #337/#338；本票不重设计产品、不改 token 色值或悬浮窗产品源码。

## 生成与来源

- `Set-CountdownBaselineVersion -WhatIf` 核对 Git 历史、全部 worktree 与现役号后确认已用最大编号为 v85，正式分配 v86。`Update-CountdownIntegrityHashes` 核对 29 项全部一致，未改 `Countdown.ReleaseContract.psm1`。
- fresh ticket worktree 的首次官方 Build：`20261123T155821807-c4774d9f`，退出 0，`CleanR2R`；候选 DLL SHA-256 为 `5267DF171B621FA17374A1FA3FA72FDC70E1B3E2F56A27B94ADABBD4B2DCFE37`。
- 首次 BaselineGenerate 在测试前被负载预检拒绝：CPU 5%、GPU 3D 100%（`kryptex_peakminer`）。未停止任何进程；按启动器支持加 `-SkipLoadPreflight` 后，单独 BaselineGenerate `20261123T155943755-e0ae9b76` 退出 0，用时 97 秒。双 manifest 和报告均为有效，生成器移除两窗共同的 v85 目录，没有手工覆盖或删除历史目录。
- 主窗源码聚合为 `B50BDC68F369FEE65E1D4604F67EBB3A3844FD4A627A6C4A97284184BCCC4967`（23 项）；悬浮窗为 `0A99A4A9215B6D4FF1C46A8B0F097FEF4B0EB08275D9AE1BAD9D2EFC5865C783`（28 项）。两窗 EXE SHA-256 均保持 `B3167E3D257CEB7CDBA57C174A2A22D5954B036C4119F2C97D86DC638F6B4DDD`。
- [capture-manifest.json](capture-manifest.json) 把六张保留图片绑定到同一 BaselineGenerate 的捕获目录 `logs/ui-captures/20261123T080044503-validate-only`、候选 DLL、双 manifest 及原始 focus/surfaces 清单哈希。复制前后逐张 SHA-256 相同；该清单已登记为 `git-blob` 发布证据根。

## 逐帧结论

[官方逐帧比对](v85-v86-comparison.txt)以 `7b173e4ad408bb1b1bc8058fa4af851344855c23` 的 Git blob 为 v85 来源。[像素分析](baseline-pixel-analysis.json)与[20 格有限覆盖矩阵](visual-coverage.json)记录每帧差异框、x/y 区间和核对状态。14/14 帧状态指纹一致。

| 帧 | 差异像素 | 差异行与结论 |
| --- | ---: | --- |
| `main-day-countdowns-empty` | 1866 | y 130–180，仅工作区页签槽与滑动胶囊 |
| `main-day-countdowns-representative` | 1866 | 同上 |
| `main-day-countdowns-stress` | 1866 | 同上 |
| `main-day-notes-empty` | 11215 | y 130–180、526–568，仅页签带与便签工具条带 |
| `main-day-notes-representative` | 11202 | 同上 |
| `main-day-notes-stress` | 11198 | 同上 |
| `main-day-notes-edit` | 10629 | y 130–180、558–599，仅页签带与便签工具条带 |
| `main-day-notes-filter-open` | 11202 | y 130–180、526–568；弹层及其他区域无像素差 |
| `main-day-notes-selected` | 11202 | y 130–180、526–568；多选与列表其他区域无像素差 |

主窗前后核对图逐帧确认：旧 `3 DIP` 直角底条消失，两处容器成为对称暖边槽，激活胶囊为全圆角，便签分段不再泄漏 `#707070` 冷灰边。装饰带、编辑卡、弹层和工具条之外的列表区域没有像素变化，外部布局零漂移。#338 已明确把搜索容器从 `VerticalAlignment=Top, Margin=0,2,0,0` 改为 `VerticalAlignment=Center`，所以同一便签工具条带内搜索标签和输入框发生约 1 px 的中线重对齐；它与新 42 DIP 对称状态槽共用中线，不属于列表或装饰带漂移。

悬浮窗 `floating-countdowns-day`、`floating-workday-complete-day`、`long-notes-day-top`、`long-notes-day-bottom`、`notes-search-expanded-day` 五帧的 PNG 字节和状态指纹全部与 v85 一致，源码绑定聚合与 EXE 哈希也不变，无意外像素变化。

## 同版常态与焦点实拍

基线参考帧捕获前清焦点，不能替代焦点证据。下列两张 96 DPI 常态图与四张 150% DPI 焦点裁图都来自上述同一 v86 BaselineGenerate；四张焦点图均为真实 Tab 路径、`keyboardIsMostRecentInputDevice=true`、`singleFocusStroke=true` 且 `focusVisualAdorner=false`，已逐张用 `read_image` 核对。

| 类型 | 证据 | 观察 |
| --- | --- | --- |
| 倒计时常态 | [main-countdowns-normal.png](main-countdowns-normal.png) | 对称暖边页签槽、全圆角胶囊，无底部横条 |
| 便签常态 | [main-notes-normal.png](main-notes-normal.png) | 对称状态槽、蜜桃胶囊、左竖轨与徽章，无冷灰边 |
| 工作区焦点 | [Countdowns-CountdownTab.png](Countdowns-CountdownTab.png) | 倒计时选中页签只有单层暖色焦点环，与粉底有间隙 |
| 工作区焦点 | [Notes-NotesTab.png](Notes-NotesTab.png) | 便签选中页签只有单层暖色焦点环，与粉底有间隙 |
| 状态分段焦点 | [Notes-ActiveNotesSegment.png](Notes-ActiveNotesSegment.png) | 选中未完成段单层焦点环，左竖轨与徽章完整 |
| 状态分段焦点 | [Notes-CompletedNotesSegment.png](Notes-CompletedNotesSegment.png) | 未选中已完成段单层焦点环，无默认冷灰框或第二描边 |

## 验证边界

独立 CSharp `20261123T161005265-4f2144af` 与 Tooling `20261123T162216319-9222224b` 均退出 0。ValidationHarness 两轮真实运行 `20261123T162026605-dd0b88f8`、`20261123T162408904-3e00df3a` 都因采样饥饿退出 1：首轮仅 `popup-flipped`（gap 193.5897 ms），第二轮为 `popup-flipped` 与 `datepicker-text-interrupted-input`（约 206 ms），均显著超过 37.0496 ms 阈值；场景重试预算已用尽，没有弱化断言或把失败记为通过。四个 `segment-pill` / `segment-pill-immediate` 场景仍通过。实际命令、退出码和 run ID 见 [runs.json](runs.json)。

最终五阶段 `-Closeout` 不在本 ticket worktree 二次执行；必须由 orchestrator 在最终合并提交的新 fresh gate worktree 执行，本目录不提前声称该阶段完成。

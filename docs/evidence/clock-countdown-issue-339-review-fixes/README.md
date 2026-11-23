# PRD #336 双轴评审修复证据（v87）

本目录记录 `review/prd-336-fixes` 的五项评审发现修复、先红后绿对照和 v86→v87 零像素漂移证明。v86 的原始视觉证据仍保留在 [历史基线证据](../clock-countdown-issue-339-sliding-pill-baseline/README.md) 与 Git 提交 `45f6ddc5bfa48a42c6e1f54e320542fc992ee25e`；本目录不重写历史结论。

## 发现与完成证据

| 发现 | 修复与证据 | 状态 |
| --- | --- | --- |
| 动态计数首帧假绿 | `ObserveFirstFrameAfterChange` 在动作前安装 `CompositionTarget.Rendering`，`SaveNote` 内层 Pump 也无法提前消费首帧。新增、完成、撤销、筛选及 0/9/113、Width、兄弟 X、徽章断言全部保留。[红绿对照](red-green.json) | done |
| reduced pointer 首帧缺失 | `PopupValidationContract` 的 off 分支先取 `ObserveFrame`，再检查 Loaded 后稳定态；真实管理窗口增加工作区与便签状态两类点击首帧断言。生产 `MoveTo` 在关闭动画时同步提交目标几何，红测实际抓到旧几何后修复。[红绿对照](red-green.json) | done |
| 四张复核图未入 Git | 实际 `read_image` 复核的四张 v85/v86 对照图已逐字节复制到历史证据目录；[review-sheets.json](../clock-countdown-issue-339-sliding-pill-baseline/review-sheets.json) 记录源/目标相同 SHA-256，并纳入发布证据检查。 | done |
| 两处探针同形重复 | 共享 `VerifyPillLayerAndRealHit`、`VerifyPillHoverStates`、`VerifyPillHighContrastRoundTrip`；层序、自动化树、真实 `InputHitTest`、hover 和日间/高对比往返断言强度不变。 | done |
| `SegmentIndicator` 动画 switch 重复 | 单一 `IAnimatable` 入口逐个属性完成清钟、写基础终值并按需启动 `SnapshotAndReplace`；Width/Translate 行为由四个 Harness pill 场景与完整 CSharp 验证。 | done |

## 基线与像素

- [baseline-manifest.json](baseline-manifest.json) 以 `git-blob` 口径绑定双 manifest、候选 DLL、14/14 帧结论、红绿对照、命令记录和原始运行日志 SHA-256。
- `Set-CountdownBaselineVersion -WhatIf` 实测最大已用号 v86，正式分配 `2026-11-23-v87`；29 项 `IntegrityHashes` 全部一致，`Countdown.ReleaseContract.psm1` 无 diff。
- 首次 Build 请求 `20261123T180041649-e9399376` 在任何清理前被 capability-root 预检拒绝。未改仓库外 policy、未删除任何产物；11 个既有增量目录被完整保留到 `D:/agents/tmp/prd-336-review-incremental-before-v87-build`，随后 `-RequireClean` 预检通过。本 worktree 首次实际 Clean + R2R Build `20261123T180304487-c92512a5` 退出 0，DLL SHA-256 `7626A4AA461DD9404C7768EBE84666F2C4000061A9926759DAB816E7F0E459C0`。
- BaselineGenerate 首次负载预检测得 CPU 83%、GPU 3D 100%，未停止进程；按官方支持使用 `-SkipLoadPreflight` 后，`20261123T180529795-8f3d1620` 退出 0。
- [v86→v87 官方逐帧比对](v86-v87-comparison.txt)：主窗 9/9、悬浮窗 5/5 的 PNG 与状态指纹全部逐字节一致。主窗 sourceBinding 仅 `SegmentIndicator.cs` 改变，聚合为 `B7B2D6062221D57CA736831026E466AEC698F74E7CC1D02863621874F8728ADC`；悬浮窗聚合保持 `0A99A4A9215B6D4FF1C46A8B0F097FEF4B0EB08275D9AE1BAD9D2EFC5865C783`。
- 独立 CSharp `20261123T180801636-85b83577`、完整 ValidationHarness `20261123T181555625-555989c1` 与 Tooling `20261123T182415487-7eecd535` 均退出 0；Harness 的四个 pill 场景全绿、零卡顿重试。早期采样饥饿和基线延后失败均保留在 [runs.json](runs.json)，没有改写为通过。

## 边界

本轮未执行五阶段 `-Closeout`，也未在同一 worktree 二次 Clean；最终 fresh gate 由 orchestrator 负责。未开 PR、未 push、未关闭 issue。高 CPU/GPU 负载只按官方开关跳过开跑前拒绝，没有放宽任何行为、像素、帧间隔或采样阈值。

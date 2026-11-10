# #256 双窗 fixture 与 #243 管理窗新条目补证

当前状态：完整 fixture、七个场景迁移及 R1 已通过正式 Build/SourceValidation。最终集成、视觉基线与组合门禁待补齐；下面的 RED 和中断记录不计为成功。

## 资源边界

[DualWindowValidationFixture](../../../src/MinminCountdown/DualWindowValidationFixture.cs) 的 `Run` 拥有唯一临时根、设置及备份、workspace、日历、诊断日志、双窗构造、离屏设置、显示与清理。场景只准备领域种子、调用 `ShowWindows`，然后执行自己的动作和断言；不逐项关闭双窗、释放日历或删除设置。未返回的 WPF 构造也通过 `Application.Current.Windows` 的构造前后差集登记，只回收该次构造新增的对应类型窗口。

失败分为 `setup` 和 `cleanup`；原场景异常保留原身份与堆栈。清理逐项尝试，某一资源失败仍继续释放其余资源；同时发生行为失败与清理失败时，异常保留二者。锁文件负例只释放其持有的锁，再由原 fixture 的 `RetryCleanup` 完成收口。

清理访问明确登记的设置/备份/tmp、日志及其轮转文件，额外测试输入由 `WriteTemporaryFile` 登记。只删除这些文件，再按深度反序删除空目录；不递归清扫。未知残留保留并明确报 `cleanup` 失败。相应负例只移除其刻意植入的未知文件，再交回 fixture 收口。

[生命周期探针](../../../src/MinminCountdown/ValidationExercises.DualWindowFixture.cs) 检查初始空状态、真实共享双窗、离屏标志、真实持久化、日历初态/释放、后继场景隔离、原始行为异常、Main/Floating 两个真实坏 PNG 构造失败，以及锁文件/未知残留的失败归因与有界重试。该探针登记为必须为真的 `DualWindowValidationFixtureLifecycle` 验证事实。

## 迁移范围与初始语义

| 场景 | 保留的场景责任与初态 | 日历等价边界 |
|---|---|---|
| `RunNoteCompletionFlipTransition` | 同一组日期/便签、完成/撤销、视口、真实帧和中断断言；首个代表场景 | 原来仅构造，未刷新，初态 `Pending` |
| `RunNoteCompletionExitTransition` | 单条未设日期便签；真实完成、淡出、撤销、隐藏/切页与动画偏好 | 原来未刷新，`Pending` |
| `RunNoteCompletionUndoFeedback` | 同一注入时钟、重复规则、写盘阻断、唯一撤销槽和到期；中途解除写盘阻断是被测动作 | 原来未刷新，`Pending` |
| `RunRemainingBlocksOnDemand` | 原时间线、分钟精度、记录与 WM_DPICHANGED；DPI 上下文仍由场景恢复 | 原来未刷新，`Pending` |
| `RunFloatingNextReminderCache` | 原三条便签、同一时钟、事件/计算计数和文字断言；真实刷新仍在场景动作中 | 原来就是独立不存在根加 synthetic offline；仍经过加载中/未就绪，不读共享缓存、不触网 |
| `VerifyFloatingWorkspaceOrderAndFilterAction` | 原稳定 ID、完整初始设置和时钟；真实排序、筛选与磁盘字节断言 | 原来未刷新，`Pending` |
| `RunFloatingScrollContextHintBehavior` | 原时钟、工作区覆盖、18 个状态、滚动输入及实拍回调；工作区覆盖作用域仍由场景持有 | 原来未刷新，`Pending` |

对应源码：[完成/撤销](../../../src/MinminCountdown/ValidationExercises.ConfirmationAndUndo.cs)、[按需渲染/提醒缓存](../../../src/MinminCountdown/ValidationExercises.MainWindow.cs)、[排序与筛选](../../../src/MinminCountdown/ValidationExercises.QualityGates.cs)、[滚动上下文](../../../src/MinminCountdown/ValidationExercises.Density.cs)。

日历构造本身不读年度数据或刷新；[HolidayCalendar](../../../src/MinminCountdown/HolidayCalendar.cs) 的初态是 `Pending`/“节假日日历正在加载”。本 fixture 使用私有根及离线下载函数，未把需要 ready、随包年度、联网重试或异步送达的场景迁入。生命周期探针另验证释放后 `RefreshAsync` 抛 `ObjectDisposedException`。

## 保留的不同生命周期

| 未迁移的场景/所有者 | 可验证的不同生命周期原因 |
|---|---|
| `RunLazyManagerLifecycleBehavior`、`RunDeleteUndoInPlaceBehavior`、`RunWindowPositionResetBehavior`、Narrator 场景 | 使用 `ManagerWindowLifecycle`，管理窗尚未创建/按需创建本身就是断言；不能提前构造双窗。参见[生命周期](../../../src/MinminCountdown/ValidationExercises.NotesAndLifecycle.cs)、[就地删除撤销](../../../src/MinminCountdown/ValidationExercises.ConfirmationAndUndo.cs)、[窗口定位](../../../src/MinminCountdown/ValidationExercises.MainWindow.cs)、[Narrator](../../../src/MinminCountdown/ValidationExercises.Narrator.cs)。 |
| `RunFloatingNoteKeyboardSubmission` | 管理窗只作宿主但始终不显示；只有浮窗获得输入。见[主窗相关探针](../../../src/MinminCountdown/ValidationExercises.MainWindow.cs)。 |
| `RunListDeleteKeyboardBehavior` | 完成管理窗焦点/删除断言之后才显示浮窗，显示顺序属于被测上下文。见[主窗相关探针](../../../src/MinminCountdown/ValidationExercises.MainWindow.cs)。 |
| `RunAlwaysConfirmExitKeepsReminderBehavior` | `FloatingWindowCloseBridge` 拦截 Close，取消后提醒协调器必须仍运行，确认后一起终止；通用 fixture 不接管退出确认协议。见[退出确认](../../../src/MinminCountdown/ValidationExercises.ConfirmationAndUndo.cs)。 |
| `RunCountdownPreferencesPopupBehavior` | 按需宿主与循环中新建浮窗，窗口数量/时序不同。见[偏好探针](../../../src/MinminCountdown/ValidationExercises.MainWindow.cs)。 |
| `OpenWindowsFollowMonitorChanges` | 五种窗口共同验证原生拓扑变化，含提醒中心 ForceClose 与真实位置操作，不是离屏双窗作用域。见[显示器几何](../../../src/MinminCountdown/MonitorGeometryValidation.cs)。 |
| 共享表面圆角、可访问名、多窗口实拍及对比度/收入遮罩重启场景 | 同一作用域还持有提醒中心、规则编辑器、确认框，或刻意销毁/重建浮窗验证实例边界。见[布局探针](../../../src/MinminCountdown/ValidationExercises.LayoutProbes.cs)、[质量探针](../../../src/MinminCountdown/ValidationExercises.QualityGates.cs)、[实拍](../../../src/MinminCountdown/ValidationReport.FocusEvidence.cs)、[对比度与控件](../../../src/MinminCountdown/ValidationReport.ContrastAndControls.cs)。 |
| `ApplicationValidation` 及既有引用主窗/浮窗的报告探针 | 共享整个 validate-only 进程的设置守恒、提醒、托盘与基线生命周期；不由单个独立场景拥有。见[应用验证](../../../src/MinminCountdown/ApplicationValidation.cs)。 |

没有将所有 `new FloatingWindow` 机械替换；单窗、提醒中心、恢复/启动和真实日历协议不在同生命周期迁移范围。

## #238 R1 / #243 补证与修复边界

旧[完成补位探针](../../../src/MinminCountdown/ValidationExercises.ConfirmationAndUndo.cs) 只在浮窗检查 Capture 后插入新卡。新增管理窗场景完成日期末条，再在下方既存日期组插入视口内新卡；同时检查该既存日期组及旧卡确实补位，避免动画没有运行造成假绿。

真实 RED 的新卡自身纵移为 0，祖先纵移为 121.32 DIP，累计为 121.32 DIP。管理窗生产调用链是 `OnNoteCompletionChanged → Capture → ApplyWithUndo → 同步列表刷新 → Start`；普通用户输入不能在同一同步调用中插入新记录，未宣称复现一个用户并发新增流程。但 #243 AC4–5 和 [DESIGN 动效合同](../../../DESIGN.md#9-motion-哲学) 明确要求两个表面的无旧身份条目不动，已有 Capture/Start WPF seam 正是该合同的验证边界。

因此只在 [NoteCompletionExit](../../../src/MinminCountdown/NoteCompletionExit.cs) 对“无旧身份但继承移动祖先”的条目添加反向抵消，仍使用同一 Storyboard、token 和缓动，沿原释放路径恢复原 Transform。既存条目的 FLIP/分组规则、领域事务、写盘和撤销语义不变。新增断言覆盖两窗的首帧、真实中间帧、自然结束和主动中断，累计纵移须始终为 0，旧卡/日期组仍须运动，终态和中断均恢复原 Transform。

## 运行账

均经 `Invoke-CountdownSuiteLowImpact.ps1`，保留负载预检、隔离桌面、BelowNormal、单实例锁与 `WaitForLockSeconds 600`。没有修改历史 manifest 或跳过基线门。

| run | 阶段与实际退出 | 结论 |
|---|---|---|
| `20261110T211409686-d706c1ad` | Build 0 / 43s；SourceValidation 1 / 112s | R1真实 RED；err第26行 `surface=MainWindow; own=0; ancestor=121.32; displacement=121.32; viewport=380.47` |
| `20261110T211911343-10f97e8d` | CSharp 1 / 308s | 同一旧产物在 adapter 路径也报相同 R1 错误；另保留 adapter diagnostic evidence |
| `20261110T213448860-53bf7521` | Build 0 / 146s；SourceValidation 1 / 114s | fixture故意漏 Close 的真实 RED：`cleanup/lifecycle-success; cleanupFailures=2; scenarioFailure=none`；负对照已撤销 |
| `20261110T215009608-d5359c4f` | Build Interrupted / 1；SourceValidation Skipped / 1 | 宿主恢复后 owner 失效；不是 GREEN；原 job 在新 runtime 中 unknown；清理事务交父代理恢复 |
| `20261110T220602793-63e2c23f` | Build Interrupted / 1；SourceValidation Skipped / 1 | 第二次宿主中断；未记 GREEN。恢复时实际核对 owner/stage/child 均已不存在，官方锁可用后才启动下一轮 |
| `20261110T231532847-abd8d4d3` | Build 0 / 209s；SourceValidation 0 / 105s | 首次真实 GREEN：`Valid`、fixture生命周期、七个迁移场景事实全部通过。开跑负载 CPU 27%、GPU 3d 5%，两阶段均隔离 |

最后一轮 err 的第 26 行确认生命周期探针完整通过；第 27–30 行记录两窗自然结束与主动中断的真实帧。新卡首帧/中间帧/终态累计位移均为 0；浮窗旧卡起始 88 DIP、中间 66.387/75.836 DIP，主窗旧组起始 121.32 DIP、中间 91.435/92.52 DIP；每项都恢复了原 Transform。提醒缓存的计数序列仍为 `3,0,1,0,3,0,3,0,3,0,6,0`，包括真实日历状态变化触发的重算。

该 GREEN 的源码版本对应本票工作树，尚未宣称集成候选完整门禁已通过。集成 SHA、基线版本及组合门禁将在完成后追加。主代理证据目录为 `D:/agents/run-evidence/clock-countdown-implement-spec/20261110T200353`，本票最终交付写入其中的 `issue-256-result.md`。

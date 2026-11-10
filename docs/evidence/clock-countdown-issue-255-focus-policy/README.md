# #255 焦点策略与真实 WPF 证据

本目录保存本票可公开回读的代表性实拍与运行摘要。它对应 `KeyboardFocusPolicy` 的规则深化，没有改变窗口几何、导航、字号或 2 DIP 焦点环。

[manifest.json](manifest.json) 使用相对图片路径，记录原始像素哈希、来源运行与焦点字段。四张图均从真实 WPF 运行直接复制，逐张用 `read_image` 复核，复制前后 SHA-256 相同。管理窗裁图按原始 150% DPI 截取并整数倍放大；另外两张表面图按 96 DPI 渲染。未改动图片像素。

| 证据 | 观察 |
| --- | --- |
| [置顶勾选框](Countdowns-TopmostCheck.png) | 单层暖金焦点边，没有附加装饰层。 |
| [日期输入](Countdowns-CountdownDate.png) | 实际焦点在打开日历按钮，宿主输入面绘制焦点；按钮没有第二层焦点环。 |
| [悬浮窗](floating-focus.png) | 快速新增入口显示单层暖金焦点边。 |
| [提醒规则编辑器](rules-editor-focus.png) | 保存修改按钮显示单层暖金焦点边。 |

保留 `templateDrawsFocus` 字段名以兼容既有证据消费者，现表示“模板或宿主负责描边”，覆盖勾选框、日期按钮和金额文字。`singleFocusStroke` 使用生产策略查询当前状态；实拍观察与真实 Tab 探针仍独立保留。

## 行为与负对照

成功的生成报告包含 21 个直接策略检查，全部通过；管理窗完整 Tab 路径、悬浮窗及次级窗口的单层焦点事实也全部通过。原始焦点清单包含 47 张裁图，策略判定失败和自绘加装饰层的冲突均为零；本目录精选其中两张并补充两张跨窗表面图。金额文字的指针获焦、键盘动作、失焦清理与拒绝第二层装饰在直接夹具中验证；现有悬浮窗真实 Tab 探针另读实际 `FloatingIncomeFocusRing`，不以本目录图片替代它。

受控旧行为在 `KeyboardFocusDrawnOnce` 中恢复了“金额文字误接受共享装饰层”这一原有分支。官方 SourceValidation 运行 `20261110T210705755-e5405238` 退出 1，原始错误为 `System.InvalidOperationException: Focus policy accepted a second stroke on income text.`。该分支随后完全删除，保留测试。没有将编译失败、负载预检失败或基线失配当作 RED。

## 基线与独立验证

官方 BaselineGenerate 运行 `20261110T214726441-37beebac` 退出 0，生成正式 `2026-11-10-v76` 双 manifest。正式比较工具确认：相对 v75 的全部 14 张参考 PNG 和状态指纹逐字节一致；只增加焦点策略源码绑定及更新已触达源码的哈希，没有手改 manifest 哈希。

生成时仅未绑定的 `RunDisclosureArrowMotionBehavior` 带受限诊断。该诊断不改变输入、断言或生产行为，之后完整移除，文件与本票基点逐字一致。移除后的正式 Build `20261110T215935835-d05630d5` 退出 0；最终候选 DLL 为 `EFAB696440AB7394849299BD82EA75D9879F2AC37B81967B3EF0319D506A4E61`，EXE 哈希与生成记录一致。生产 sourceBinding 与 producerInputs 不包含该验证方法，因此没有仅为删除日志而重拍。

运行与失败记录见 [runs.json](runs.json)。最终候选的独立运行 `20261110T232055062-e3a8a360` 中 CSharp、Ui、SourceValidation、ValidationHarness 与 Tooling 全部退出 0；CSharp/Ui 在 v76 双 manifest 已存在的源码基线模式运行。无诊断最终源码报告再次确认 21 项策略检查、两窗与次级窗口单层描边及箭头行为全部通过。另逐项核对双 manifest 的 51 个源码绑定、38 个唯一文件，工作区和 Git 暂存 blob 的 SHA 全部匹配。Core、InstallValidate 和集成 full gate 由主代理统一执行，本目录只记录本票局部验收。

## 保留的不稳定观测

首次 BaselineGenerate `20261110T211653206-d1348c45` 在既有 #253 展开箭头首次 Space 探针失败，发生在新增焦点夹具之前；这不能排除焦点改动的影响。随后独立 CSharp/Ui、绑定 DLL 哈希的 GeometryPreflight 及带受限观测的原生成场景均通过。观测显示原生 Click 恰好一次、焦点和激活正常、修饰键为空、鼠标释放且无捕获、终态角度正确且没有动画时钟；未定位首次失败原因，没有据猜测修改产品或放宽断言。临时 `DEBUG-255-DISCLOSURE` 观测已全部移除。

## 模块边界

删除新策略后，输入来源状态会重新分散到主窗与金额控件，绘制归属和装饰资格会重新分散到窗口、Tab 验证器及截图元数据，单层判定也必须重新回到验证器。这次变更不只是移动 `FocusVisualState`：它只保留模板附加状态，生产应用与验证共同使用同一策略接口。没有迁移 #256 双窗 fixture 或修改 #257 行内动作。

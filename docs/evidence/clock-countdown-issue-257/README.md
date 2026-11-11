# #257 行内动作构造契约

状态：局部七个必需阶段均已取得通过回执；保留既有 #212 稳定刷新和 #129 窗口锚定的间歇失败记录。尚待父级干净集成提交上的完整八阶段门禁。机器可读运行账见 [runs.json](runs.json)，基线与图片绑定见 [manifest.json](manifest.json)。

## 实现与删除测试

现有 `UiControls.cs` 内的 `InlineActionButton` 是目标构造边界。倒计时、便签、标签和规则调用方只提供表面、动作和记录上下文；Content、可访问 Name/HelpText、样式、危险语义、焦点资格与 ToolTip 均在构造器内完成。没有新增 adapter、平行动作框架或 #256 fixture 的误用。

- 主行保持 `SecondaryButton`、53×33、caption 字号和 4 DIP；铅笔/垃圾桶模板逐字迁入。删除继续使用 DangerBrush、透明边框和既有「删除倒计时/便签（Delete）」提示。
- 标签保持 65×39、caption 字号和 7 DIP；保存用 SecondaryButton，删除用 DangerButton。
- 规则保持 MinWidth 58 和 6 DIP；编辑/复制沿用默认 Button，删除用 DangerButton。
- 焦点通过 `KeyboardFocusPolicy.Apply` 取得，未重建 #255 规则。几何、命令、草稿、筛选、撤销、确认框和提醒中心均未改变。
- 删除该共享模块会迫使四类调用方重新分别实现内容、命名、帮助、样式映射、危险表达、焦点与提示，而不只是移动一个属性，因此它承担实质语义复杂度。

本票没有新增源码文件，发布闭包实测仍为 `SourceBuildFiles=99`、`BuildInputFiles=101`；Integrity 23 项检查无不一致。

## TDD 与真实交互

| 证据 | run ID | 结果 |
|---|---|---|
| 受控 RED | `20261111T015940714-9ee2d0b7` | Build=0，SourceValidation=1；命中 #257 规则行内动作契约，不是编译、负载或基线失败 |
| 共享构造 GREEN | `20261111T022628183-21a40b31` | Build=0，SourceValidation=0；九项直接 WPF 构造事实与规则删除通过 |
| 真实标签窗口链 | `20261111T084414363-95dd1422` | Build=0，SourceValidation=0；`TagManagerInlineActionBehavior=true` |

九项直接夹具分别覆盖倒计时编辑/删除、便签编辑/删除、标签保存/删除、规则编辑/复制/删除。真实规则窗执行既有 DeleteCommand，确认草稿行消失且接受前不提前改写 ResultRules。真实标签弹层先证明“只改文本、不点保存”不落盘，再点击保存核对按原 ID 重命名；删除先取消并核对标签、countdown、note 与关联完全不变，再确认删除并核对记录保留、关联解除且落盘。保存前/后与取消/确认就是行为敏感对照。

既有倒计时/便签删除与撤销、规则编辑/复制/删除、主窗/规则窗键盘路径和 #255 焦点 fixture 保留。标签探针只拥有 MainWindow 与模态确认框，未强套双窗 fixture 生命周期。

## v78 正式基线与实拍

正式 `Set-CountdownBaselineVersion -WhatIf` 实测下一号为 v78；Apply 后独立 Build `20261111T023114396-318079ec` 通过，BaselineGenerate `20261111T083857440-1c181a59` 退出 0（154 秒）。第一次生成尝试在创建 run 前被正常负载门拒绝，未产生 manifest；没有 SkipLoadPreflight，后续由正常放行的正式 run 完成。

- 主窗源码聚合：`1FA92C596B2262F3A9613A6496838E18E64E384E5352381098ED6758ECEE463C`（23 项）。
- 悬浮窗源码聚合：`4D574BABAC0D501E1307B982BA8D79CB41D1EC7C69E6ED3B1A2F9F0DDDC36239`（28 项）。
- [正式比较](v77-v78-comparison.txt)：14/14 PNG 与状态指纹逐字节一致；EXE 均为 `FE8A141A1F896F9CB3ACC814BB5A13628A9691830D8F4235952E28C0E4086EF5`。
- 最终候选 DLL：`DB47F8BACA54042C6186088E6CBC4DE6744B903D3B99A4F7CF36F191F9F1020B`。
- 双 manifest 共 51 项绑定、38 个唯一文件，工作区与暂存 blob SHA 全部一致；manifest JSON 经 Git EOL 归一化后也一致。

生成后只增加未绑定的真实标签探针和报告事实，生产 sourceBinding 不变，因此没有重写 v78 或另开 v79。以下公开图从 v78 原帧复制并逐字节核对，已用 read_image 复核。

![倒计时行内编辑与删除](main-countdowns.png)

![便签行内编辑与删除](main-notes.png)

## 局部验收

所有窗口/嵌套阶段均经 `tools/Invoke-CountdownSuiteLowImpact.ps1`，`WaitForLockSeconds 600`、阶段超时 1800 秒、外层受检 wrapper 不超过 3600 秒；未直跑内层 CSharp/Ui/SourceValidation，未操作挖矿进程。

| 阶段 | 成功 run | 退出码 / 秒数 |
|---|---|---|
| Build | `20261111T094947126-fa32fb8b` | 0 / 209 |
| Core | `20261111T085207274-75a05a9c` | 0 / 447 |
| CSharp（同候选恢复） | `20261111T100509242-90288c41` | 0 / 876 |
| Ui | `20261111T102712782-da34fb5e` | 0 / 422 |
| SourceValidation | `20261111T094947126-fa32fb8b` | 0 / 114 |
| ValidationHarness | `20261111T102712782-da34fb5e` | 0 / 31 |
| Tooling | `20261111T094947126-fa32fb8b` | 0 / 64 |

最终 SourceValidation 的 `Valid`、`InlineActionContract`、`TagManagerInlineActionBehavior`、`RuleEditorInlineDeleteUsesDangerButton`、`KeyboardFocusPolicyBehavior` 与 `CrossInterfaceReferenceBaselinesCurrent` 均为 true，报告 SHA-256 为 `9D8D1DA64C8F5F7AB108F9BCD418B348C88B2F34A6B4AB9353F991846443AC9B`。

并行组合 `20261111T085207274-75a05a9c` 的 Core 通过，但 CSharp 的既有 #212 FloatingStableRefresh 组失败；Ui 被取消、后续阶段跳过，均未计为通过。宿主中断的 `20261111T090140423-1c0f11f0` 不作结论；相同源码的隔离 CSharp `20261111T091945641-75e41c49` 通过。Tooling `f6ccbc2b` 发现的现役基线文档锚点和反射预算问题已据实修复：补两处 v78 锚点，标签探针使用现有强类型 XAML 字段，未抬高阈值。修复后 CSharp `95f01107`、Ui `f0fdacc6` 曾在既有 #129 窗口底部锚定项失败；对应同候选恢复 `90288c41` 和 `da34fb5e` 通过。未修改产品或放宽断言来处理间歇失败，其根因仍未定位；本票不声称所有阶段在同一轮一次通过。

原始 run、失败回执和 v78 生成目录均持久归档到 [257-runs](D:/agents/run-evidence/clock-countdown-implement-spec/20261110T200353/257-runs)；[sourceBinding 审计](D:/agents/run-evidence/clock-countdown-implement-spec/20261110T200353/issue-257-binding-audit.json) 可独立复核。InstallValidate 与父级合并后的完整八阶段集成门禁不在本票已完成范围内。

---
type: reference
title: 编码标准
tags: [code-review, wpf, focus, validation]
intent: 评审改动时逐条检查、需要判断的编码规则
---

# 编码标准

评审本仓库的改动时逐条检查下面的规则。能机械判定的规则做成 Tooling 检查或测试（例如 `tools/Test-CountdownTooling.ps1` 的 `visual-baseline:design-anchors`、`validation:control-name-lookups`），不写在这里。

## 代码后台的本地值会盖掉样式

按 WPF 依赖属性的取值优先级，代码后台赋的本地值高于样式 setter、样式触发器和模板触发器。代码里给一类控件统一赋值，会把样式和模板对同一属性的安排一并盖掉，XAML 里看起来对，运行时不生效。

- diff 在代码后台给依赖属性赋值时（尤其是 `ConfigureControls` 这类按类型批量配置的循环），到 XAML 里查同一属性有没有样式 setter、模板或触发器在管；有的话，确认代码只给本该覆盖的控件赋值，否则改在样式里表达。
- diff 改了 XAML 样式里某个属性的值时，反查代码后台有没有给同一属性赋本地值；有的话，这条样式改动对那些控件不生效，记为缺陷。

反例：`App.xaml` 的 `ComboBox` 与 `DatePicker` 样式都把 `FocusVisualStyle` 设成 `{x:Null}`，获焦时由模板把输入面边界换成 2px `ActiveBrush`；`MainWindow.xaml.cs` 的 `ConfigureControls` 却在循环里给键盘路径控件统一赋 `MainKeyboardFocusVisual`，本地值盖掉了样式，键盘进入时叠出两道描边。同一个循环也给模板自带常驻 `FocusRing` 的页签和按钮套了这层装饰。前几轮评审都没发现，先后在 `468dcb4f`（页签与按钮）和 `8b18cb0f`（输入框）修掉；现在由 `FocusVisualState.DrawsOwnKeyboardFocus` 判断哪些控件自己画焦点。

## 焦点断言看实际获焦的元素

WPF 把 `FocusVisualStyle` 装饰层画在实际拿到键盘焦点的元素上，复合控件的焦点常落在模板部件上：`DatePicker` 的 `IsTabStop` 为 False、`KeyboardNavigation.TabNavigation` 为 Once，Tab 焦点落在模板里的打开日历按钮 `PART_Button`。只读外层控件属性的焦点断言，可能对着一个永远不获焦的元素通过。

- 验证代码里关于焦点的断言（焦点可见、单层描边、焦点样式）读实际获焦的元素：Tab 进入后取 `Keyboard.FocusedElement`，或对复合控件读具体的模板部件。
- 评审时对 diff 里新增或改动的每条焦点断言，确认被检查的元素就是焦点会落到的那个；对不上就记为缺陷。

反例：`ValidationReport.KeyboardAndDialogs.cs` 的 Tab 路径焦点可见判定原先读日期框自身的 `FocusVisualStyle`，日期框挂着装饰层样式就通过了；装饰层实际画在 `PART_Button` 上，双层描边因此没被拦下。`8b18cb0f` 把这条判定改为读 `PART_TextBox` 的边框，`WindowValidationScenarios.cs` 的单层描边检查也加上了两个日期框的 `PART_Button`。

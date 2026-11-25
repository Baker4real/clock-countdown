---
type: decision
title: 窗口验证完全迁出产品程序集
tags: [adr, validation, wpf, assembly-boundary, ticket-258]
intent: 记录 US34–US41 为何把完整窗口验证闭包迁到独立 runner，以及产品 seam、预检和视觉证据的约束。
---

# 窗口验证完全迁出产品程序集

状态：已采纳（2026-11-25，#258 US34–US41）。

## 背景

#273/#274 只迁出首批独立事实，产品仍编译完整 `ApplicationValidation`、`ValidationReport`、`ValidationExercises` 和窗口场景。终态验收要求正常产品只保留正常启动、最小安装/测试启动支持及两个设置 CLI，同时保留既有 59 条独立事实、完整旧报告和视觉证据，因此需要一次真正的程序集边界迁移，而不是继续在产品窗口里增加验证 partial。

## 决策

- 36 个验证源文件、窗口内联场景、验证 options、日志与设置恢复、report JSON、资源释放和退出行为全部归 `src/MinminCountdown.WindowVerification`。默认模式仍精确产出 59 条独立事实，`--validate-only` 仍产出完整兼容报告；正常产品 EXE 明确拒绝验证参数。
- 产品只对 `MinminCountdown.WindowVerification` 开放 internal，不增加公共 API。场景优先使用 WPF 路由事件、视觉树/UIA、真实对话框和既有窄 internal probe；新增 `FloatingDragProbe` 只承载原生拖拽计数快照和受保护的位置恢复，不暴露报告或场景状态。
- `MainCountdownBlockRenderer` 是产品与 runner 共用的 internal 渲染实现，避免为像素比较维护第二份排版算法。v89 的 14 帧 PNG 与状态指纹同 v87 逐字节一致，证明这次共享没有视觉漂移。
- GeometryPreflight 和 Tray harness 显式分离候选产品程序集与 runner 程序集，并证明 runner 的 `MainWindow` 类型来自候选 `$AppPath`，禁止误测 runner 的内嵌产品副本。
- runner 不进入办公室载荷，runner 哈希不混入产品 payload aggregate。视觉 manifest 记录真实生成 runner 的 executable 身份；换版、重拍、严格自检、源码/EXE/sourceBinding 门和既有像素阈值不变。

## 考虑过的方案

- **保留产品验证入口，只让 runner 调用产品**：无法满足最终产品程序集不含验证类型的要求，也会让正常 EXE 继续背负完整验证闭包。
- **把所有 private 窗口成员改成 internal/public**：迁移最快，但会把场景状态变成产品契约；改用 WPF 行为和极窄 probe 保持封装。
- **复用旧视觉 manifest**：生成 EXE 与报告源码身份已变，旧证据不能冒充当前证据；必须分配 v91 并重拍。

## 后果

- CSharp、SourceValidation、安装验证 module、Core fake host、诊断夹具、GeometryPreflight 和 Tray 工具都以 runner 为完整报告生产者；正常启动与两个设置 CLI 仍以产品 EXE 为被测对象。
- `tools/Test-CountdownValidationExtraction.ps1` 和 CSharp 程序集反射审计共同防止验证源码/类型回流；Tooling 的按名控件查找上限继续扫描 runner，不能因移动文件而清零。
- 视觉基线生成器与 source/EXE 身份随 runner 改变；历史 v87 证据保留在 Git 历史，本票 v91 及后续版本必须重新经过完整生成与比较流程，现役版本由 DESIGN.md 指向。

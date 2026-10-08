---
type: reference
title: Issue 验收契约
tags: [agents, issues, acceptance, verification]
intent: 按 issue 类型定义必需检查、可选检查和明确的免测理由，避免把文档、运行、视觉、发布和办公室验收混成一个门。
---

# Issue 验收契约

机器可读的类型和检查定义在 [`issue-acceptance.json`](issue-acceptance.json)。每条 issue 回写记录必须包含 `number`、`taskType`、`requiredChecks`、`evidence`、`notApplicable`、`status`、`summary`、`missing` 和 `next`。

`requiredChecks` 是关闭前必须为 `processed` 的检查；`optionalChecks` 可以因范围明确跳过；`notApplicable` 必须逐项给出理由，不能用空数组或“未涉及”代替证据。`candidate`、`open` 和 `closed` 记录的是 issue 状态，不等价于实现、部署或 live verified。

任务类型的边界如下：

- `docs` 只要求文档一致性、链接和 diff 检查；只有文档触达运行合同，才把低影响套件列为必需检查。
- `behavior` 要求低影响套件和真实行为场景；没有渲染表面变化时，视觉检查可以有明确理由地标为不适用。
- `visual` 还必须有当前源码可复现的 WPF 帧和 `manifest/sourceBinding`。
- `release` 要求发布契约和依赖闭包；`ValidateOnly` 不代替办公室验收。
- `office` 要求传输账本和两次独立远端回读。
- `gate` 要求候选运行时的实际运行、离线路径和回滚证据；静态 HTML 或合成探针只能记录为 candidate/dry-run。

关闭时只依据本票的必需检查逐项对账。父 issue 的汇总只能引用子票记录，不能把父票状态或本地构建结果提升为子票证据。

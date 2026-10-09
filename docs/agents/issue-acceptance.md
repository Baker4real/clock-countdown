---
type: reference
title: Issue 验收契约
tags: [agents, issues, acceptance, verification]
intent: 按 issue 类型定义必需检查、可选检查和明确的免测理由，避免把文档、运行、视觉、发布和办公室验收混成一个门。
---

# Issue 验收契约

机器可读的类型和检查定义在 [`issue-acceptance.json`](issue-acceptance.json)。每条 issue 回写记录必须包含 `number`、`taskType`、`requiredChecks`、`evidence`、`notApplicable`、`status`、`summary`、`missing` 和 `next`。

`requiredChecks` 是关闭前必须为 `processed` 的检查，包含记录自行提升为必需的可选检查；`optionalChecks` 未提升时可以因范围明确跳过或带具体理由标为不适用；`notApplicable` 必须逐项给出理由，不能用空数组或“未涉及”代替证据。任何必需检查都不能免测。`candidate`、`open` 和 `closed` 记录的是 issue 状态，不等价于实现、部署或 live verified。

任务类型的边界如下：

- `docs` 只要求文档一致性、链接和 diff 检查；只有文档触达运行合同，才把低影响套件列为必需检查。
- `behavior` 要求低影响套件和真实行为场景；没有渲染表面变化时，视觉检查可以有明确理由地标为不适用。
- `visual` 还必须有当前源码可复现的 WPF 帧和 `manifest/sourceBinding`。
- `release` 要求发布契约和依赖闭包；`ValidateOnly` 不代替办公室验收。
- `office` 要求传输账本和两次独立远端回读。
- `gate` 要求候选运行时的实际运行、离线路径和回滚证据；静态 HTML 或合成探针只能记录为 candidate/dry-run。

关闭时只依据本票的必需检查逐项对账。父 issue 的汇总只能引用子票记录，不能把父票状态或本地构建结果提升为子票证据。

## 回写与恢复

`tools/Invoke-IssueWriteback.ps1` 默认生成正文和计划；只有 `-Apply` 才发请求，关闭票还须 `-CloseSatisfied`。每票在 POST 前保存意图、取得评论 ID 后立即保存，再独立回读正文和状态。父票在所有子票回读通过后处理。

GET 默认最多尝试 3 次，可用 `-GetAttempts`（1–5）和 `-GetRetryDelaySeconds`（0–30）调整；POST 和 PATCH 不自动重试。中断后使用原记录、原仓库及关闭选项，指定 `-ResumeFrom <旧输出目录>` 和新的 `-OutputDirectory <新输出目录>`。恢复会核对整批正文 SHA、票号、父票和目标状态，重新回读已有评论 ID，仅对未发送票创建评论。旧目录保持原样。

若 POST 结果未知且没有评论 ID，恢复会停止，须先独立核对 GitHub，不能直接重发。恢复不是重新生成已发评论；记录内容改变时应作为新的验收回写处理。

无网络回归 `tools/Test-IssueWriteback.ps1` 已接入 Tooling，覆盖首行格式与旧插值负控制、GET 有限重试、子票和父票恢复、结果未知时阻止重复 POST，以及旧证据保护。

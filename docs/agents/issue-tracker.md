---
type: reference
title: Issue tracker：GitHub
tags: [agents, issues, github]
intent: 规定本仓库问题、PRD 和 ticket 的 GitHub Issues 操作方式。
---

# Issue tracker：GitHub

本仓库的问题和 PRD 使用 GitHub Issues 管理，通过 `gh` CLI 操作。

## 约定

- 创建：`gh issue create`
- 读取：`gh issue view <number> --comments`
- JSON 读取（含评论）：`gh issue view <number> --json 'number,title,body,comments'`；PowerShell 下逗号分隔的 `--json '<fields>'` 必须加单引号（否则被拆成数组参数），且 `--comments` 与 `--json` 互斥，不叠加。
- 列出：`gh issue list`
- 评论：`gh issue comment <number>`
- 标签：`gh issue edit <number> --add-label/--remove-label`
- 关闭：`gh issue close <number>`
- PR 不作为默认 triage 请求入口
- skill 要求发布问题时，创建 GitHub Issue
- skill 要求读取 ticket 时，读取对应 GitHub Issue
- `/to-spec` 与 `/to-tickets` 直接发布为 GitHub Issue（PRD 父票与挂原生 `parent` + `blocked-by` 边的子票，默认标签 `ready-for-agent`），不在仓库 `docs/specs/` 新建重复副本；正文统一使用 `CONTEXT.md` 正名与纯散文契约描述（省去代码围栏、盘符路径、源码扩展名、两位 `#NN` 引用及待决/可选字面词）以一次通过术语、待决与隐私预检；本地草稿、tickets 清单与 `.created.json` 状态回执归位 `D:\agents\run-evidence\<task-slug>\<YYYYMMDD>\`，发布核验通过后回收 `.bodies/` 临时切片目录
- `/wayfinder` 使用一个 map issue 及其子 issue；优先使用 GitHub 原生子任务和依赖关系

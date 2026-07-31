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
- 列出：`gh issue list`
- 评论：`gh issue comment <number>`
- 标签：`gh issue edit <number> --add-label/--remove-label`
- 关闭：`gh issue close <number>`
- PR 不作为默认 triage 请求入口
- skill 要求发布问题时，创建 GitHub Issue
- skill 要求读取 ticket 时，读取对应 GitHub Issue
- `/wayfinder` 使用一个 map issue 及其子 issue；优先使用 GitHub 原生子任务和依赖关系

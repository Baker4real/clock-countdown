---
type: decision
title: 登录直启 EXE 并收敛为单一安装器
tags: [adr, installer, startup, deployment]
intent: 记录 #291 删除旧安装事务、adapter、桌面启动器和机器级时间服务处理的理由与边界。
---

# 登录直启 EXE 并收敛为单一安装器

状态：已采纳（2026-11-24，#291）。

## 背景

旧登录链依赖 PowerShell、adapter、WMI 和安装器写出的状态文件，桌面另有一份原生启动器。旧安装事务还维护 24 字段 journal、三文件验收握手、两套计划任务、桌面启动器状态以及 Windows 时间服务快照与校时。对一个办公室用户的单设备应用来说，这些故障点和维护成本超过了它们提供的保护。

#259 已确定更小的保证：升级不丢也不悄悄改写用户设置；新版本起不来时旧版本自动回来；维护者能读取办公室当前运行的版本。#287–#290 已完成布局切换、直接 EXE 启动、设置迁移演练、回滚、新部署入口和实时状态读取，因此旧实现可以退役。

## 决策

- 登录任务直接执行安装根下的 `app\MinminCountdown.exe`，不带安装相关参数；桌面入口是指向同一 EXE 的快捷方式。删除 adapter、WMI 启动、`CountdownLauncher.cs/.exe` 与其构建脚本、延迟探针和对应旧测试。
- `Install-CountdownProgram.ps1` 与 `Countdown.Installer.psm1` 是唯一安装入口和事务实现。安装只替换程序目录，保留设置及两代备份保护、迁移演练、启动就绪提交、失败回滚和中断恢复；不再处理遗留任务、遗留启动器或旧布局清理。
- 删除旧安装事务/生命周期模块、旧安装入口和一次性布局切换脚本。新安装器不运行测试套件，不配置或快照 Windows 时间服务，也不把校时失败作为安装失败。
- 删除多阶段 journal 和三文件验收握手的旧安装实现。安装结果是一份 JSON，事务标记只服务本次程序替换和恢复。`InstallValidate` 使用新安装器 `-CheckOnly`；失败 JSON 必须同时以非零进程退出闭锁，并有负控制防止假绿。
- 真实安装与 ZIP 打包都保留节假日年度覆盖守卫，`-CheckOnly` 按现行契约豁免。发布契约的 `PackageFiles` 与实际 ZIP 精确一致：程序目录载荷、载荷清单和两个安装器文件。
- 诊断日志只由 C# `DiagnosticLogger` 写入，保留其滚动 JSONL、路径散列互斥体和跨进程并发测试，不再保留 PowerShell 写入方。

## 后果

- 登录和双击不再依赖 PowerShell 安装路径、adapter、WMI 服务或启动请求文件。应用是否存活由已安装 EXE 的进程、会话和哈希判断，不再用计划任务 `Ready` / `LastTaskResult` 作为替代指标。
- 安装器不再修复机器级时间配置；现有系统校时配置保持不变，是否持续校时由 Windows 管理。
- `RetiredRuntimeFiles` 只作为历史元数据保留，新安装器不据此执行一次性清理。历史 `docs/evidence` 与既有备份不改不删。
- #292 的启动验收协议已由下方补充决策退役；#293 的 PowerShell 设置实现、#294 的静态哈希钉值与更新工具不在 #291 中提前删除。
- 本机最窄分段、Build、Core、Tooling、InstallValidate 可以覆盖退役面；完整五阶段 fresh gate 由合并后的 orchestrator 验收，单实施 worktree 不以阶段组合冒称 fresh 全门。

## 规模与包计数

按实施地图的同一文件集合统计非空行，#290 后的本票基线是生产 3,602 行、测试 12,145 行；本票完成后分别为 **3,383** 和 **8,828** 行。PRD 的 2,000 / 1,500 预期没有达成，但没有为凑数删除保护或负路径测试：

| 组成 | 完成后非空行 | 仍需的保证 |
| --- | ---: | --- |
| 新安装入口与事务 | 1,759 | 包逐项哈希、唯一交互会话、全机锁、中断恢复、设置快照与迁移演练、直接 EXE 任务/快捷方式、启动就绪、回滚、年度覆盖和 CheckOnly 非零闭锁 |
| 部署入口与编排 | 954 | 同一提交 CleanR2R 证据、测过字节一致性、年度覆盖、受管传输/远端执行、结果未知语义和安装后一次实时状态读取 |
| 实时状态命令 | 179 | 清单、EXE 哈希、启动就绪进程、任务漂移、设置指纹和安装进行中的只读证明 |
| 过渡运行时协议 | 491 | #292 尚未执行的启动验收协议退役 |
| Core 入口、安装/部署/状态分段 | 2,975 | 新安装正反向路径、负控制、真实端口只读检查、部署拒绝与异常路径、发布 ZIP 精确闭包及设置互斥 |
| 过渡启动协议与设置主域测试 | 281 | #292 协议行为和 #293 设置实现的现役回归 |
| CSharp 与外部关闭测试 | 5,572 | 产品验证、迁移、直接 EXE 激活、#292 启动协议和 C# 诊断并发；共享 helper 已改名保留 |

真实构建载荷为 429 个清单文件，加 `app\published-payload.json` 与两个安装器文件，发布 ZIP 和 `PackageFiles` 均为 **432** 个文件；旧契约的 448 个文件包含已退役的安装链、根目录重复资产和本地信任根。精确闭包由真实 ZIP entries 对契约逐项相等的测试守住，额外文件拒绝仍属于 #294 的范围。

## #292 启动验收协议退役补充

状态：已采纳（2026-11-25，#292）。

- 正常启动只执行现有设置加载、确认恢复、迁移保存、单实例激活和窗口生命周期，不读取安装器留下的文件。删除只读验收、`startup-request.json` 状态推进、`acceptance-status.json`、`--require-startup-request`、PowerShell 过渡运行时 module 及 C# 启动验收类型和 helper。
- `runtime-status.json` 升为 schema v5，精确字段只有 `schemaVersion`、`stage`、`processId`、`sessionId`、`readyAtUtc`、`calendarState`、`timeZoneId`。删除启动令牌和 adapter `applicationPath`；安装器仍以回执 PID 核对存活已安装 EXE 的映像路径、交互会话与载荷清单哈希，首帧与日历刷新继续原子替换并保留占用重试。
- 两个设置 CLI 保持在色板、日志、单实例和任何窗口之前返回。普通第二实例恢复既有悬浮窗；外部关闭、提醒 tick 和设置保存不经过验收抑制分支。
- 验证事实去向：`StartupAcceptanceProtocolModuleDeep` 与 `StartupAcceptanceProtocolInterfaceBehavior` 随被测握手删除；仍有价值的 tokenless 精确字段、进程身份、非法日历状态、原子替换和持续占用反例由新 `RuntimeReceiptInterfaceBehavior` 覆盖，真实 EXE 的直接启动矩阵另证明缺失、损坏、active、finalized 和未知 schema 旧文件不影响迁移保存，安装器首安装用 tokenless 回执达到启动就绪。
- 上方规模与包计数是 #291 完成时的快照；本补充删除过渡运行时 module 与旧协议测试，不沿用其中“过渡协议仍现役”的含义。#293 与 #294 的范围不因本补充提前实施。

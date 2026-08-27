---
type: report
title: 回读长命令 SCP 化方案
tags: [deployment, office, ssh, scp]
intent: 记录部署后独立回读 SCP 化（issue #78）的设计决策、目标协议、不变量与验收建议。
---

# 回读长命令 SCP 化方案

## 背景与问题

issue #78（2026-08-27 已按本方案验收并关闭，实现提交 df0a454，端到端验收 runId 20260827214221918-811d1ca7；父项 #74 已全部收口关闭）。2026-08-26 正式部署中，Git 自带 MSYS2 ssh（OpenSSH_10.5p1）把约 8KB 以上的远程命令确定性截断：22644 字符的部署后独立回读命令被截断，远端 `pwsh` 报 `-EncodedCommand` 编码错误。同一二进制在 08-24 对同长度命令还正常，行为漂移无任何预警。

身份层防线（#75 候选优先级解析 + #76 约 32KB 只读 echo 探针）只能识别并闭锁截断型实现，不能消除对 ssh 远程命令长度行为的依赖本身。#78 要求把部署后独立回读（及任何超长远程命令）SCP 化：脚本经 SCP 上传到远端临时路径，再用短 ssh 命令执行并回读结果，使命令长度退出威胁模型。

issue 验收草案：回读 SCP 化；身份门探针语义不变；传输账本 schema 与严格 SSH 参数集不变；全套件与 `-ValidateOnly` 全绿且 `git diff --check` 无错误；一次真实办公室正式部署端到端验收。Out of scope：MSYS2 截断根因修复、办公室 sshd/服务端改动。

## 第一性原理分析

**目标**：让部署后独立回读不再依赖 ssh 远程命令长度行为；命令长度退出威胁模型。

**可观察事实**：

- 远程命令长度行为不可信且无预警漂移（08-24 正常、08-26 截断，同一二进制）。
- 回读命令之所以达 22644 字符，是因为把 7 个函数定义和期望值 JSON 内嵌进了命令行。
- SCP 文件传输 + 哈希复核链路已被 remote-install 先例证明可靠。

**可控制行动**：我们控制命令行承载的内容（令其短小且长度有界）、批量内容的传输通道（SCP + SHA-256）、本地预算（哈希、静态合同）。

**核心原理**：命令行只承载控制面，不承载数据面。数据面（脚本本体）走文件通道并带字节级完整性证明（SHA-256）；控制面（路径、哈希、执行动作）是 O(1) 短命令，与脚本大小无关。于是「命令长度」从威胁模型中消去，而不是被探伤。

**需证明的保证**：

- 回读通道的任意 ssh 远程命令长度有固定小上界（与脚本大小无关）。
- 回读语义逐字段不变。
- 既有六项合同不变量全部保持（见「不变量清单」）。

**威胁模型与失败模式**：

- SCP 上传失败 → 账本四态既有语义（`Invoke-CountdownRecordedScpTransfer` 已实现）。
- 短命令非零退出 → 既有 ssh 回读失败语义。
- 远端哈希不匹配 → 执行前中止（照抄 remote-install 的哈希复核前置）。
- 截断无法静默篡改脚本本体（脚本变成文件 + 哈希）；残余风险只剩固定短命令本身，由身份门探针继续兜底。

复合失败形态在账本中的精确呈现见「目标协议设计」一节的失败语义映射。

## 现状机制与候选改动点

### 超长命令唯一构造点

`Countdown.RemoteDeployment.psm1:1157` `Invoke-CountdownRemoteDeploymentIndependentRead`：

- 期望值参数 JSON → Base64（1193-1194）。
- 内嵌 7 个回读函数定义文本（1195-1208）。
- reader 脚本文本模板（1209-1222）。
- reader 脚本 gzip + Base64 压缩段（1224-1241）。
- 解压 bootstrap（1242-1259）。
- UTF-16LE Base64 → `pwsh.exe -NoLogo -NoProfile -EncodedCommand`（1260-1261）。
- `>30000` 字符安全门（1262）。

### 执行通道

`Countdown.TransferLedger.psm1:435` `Invoke-CountdownSshBase64CommandRead`：456-482 校验 16 元素严格参数集；450-455 对 `RemoteCommand > 30000` 判失败（exitCode=-1）；540-544 对 ssh 无法启动判失败（exitCode=-1）；512-518 对远端非零退出透传退出码；520-534 校验 stdout 的 Base64 可解码性与解码后大小（1..MaximumDecodedBytes）。UTF-8 严格解码不在该层，在编排层（`Countdown.RemoteDeployment.psm1:1432-1433`，`UTF8Encoding($false, $true).GetString`）。

### 编排与账本

`Invoke-CountdownRemoteDeploymentIndependentReadback`（`Countdown.RemoteDeployment.psm1:1350`），账本 Id 钉为 `post-deployment-independent-readback`（1452），证据写 `post-deployment-independent-readback.json`；回读失败统一报告「已提交但独立验证不完整，未回滚」（1465-1467）。

`Invoke-CountdownRecordedSshRead`（`Countdown.TransferLedger.psm1:547`）的记账时序是：`:557` 先把账本读入内存、`:572` 才执行 Reader、`:672-674` 用内存副本整本覆盖写盘，Stopwatch 跨度覆盖整个 Reader（570-583）。因此 Reader 内不得再藏独立记账步骤：内层已落盘的账本条目会被过期副本覆盖丢失，且 ssh 条目 metrics 会静默包含内层耗时。这是上传步骤必须移出 Reader 的直接依据。

跨模块绑定现状：`Countdown.RemoteDeployment.psm1:67-98` 只经 `NewBoundScriptBlock` 绑定 `Invoke-CountdownRecordedSshRead` 与 `Invoke-CountdownSshBase64CommandRead` 两个转发块（71-74 做导出存在性检查），未绑定 `Invoke-CountdownRecordedScpTransfer`——列入改动点。

### 调用方与注入点

`Deploy-OfficeCountdown.ps1:1553-1570`；adapter 工厂 `New-CountdownRemoteDeploymentReadbackSshAdapter`（`Countdown.RemoteDeployment.psm1:1281-1348`）目前只注入 ssh Options/Destination/CommandRunner，无 scp 注入点。编排层（1350-1364）与 adapter（1283-1287）当前都没有 `$remoteRoot` 来源，也没有 scp destination 形态（`user@host:path`）参数。

### 既有 SCP 化先例（remote-install）

`Deploy-OfficeCountdown.ps1:1421-1475`：长脚本 UTF-8 无 BOM 落盘 run-evidence 目录 `remote-install.ps1`（1422-1427）→ SHA-256（1428）→ 静态合同 `Assert-CountdownRemoteInstallScript`（1421）→ `Invoke-CountdownRecordedScpTransfer`（1439-1447；`Countdown.TransferLedger.psm1:252-387`，账本 Id/method=scp、计时、非零退出码四态语义：`failed` / `failed-before-transfer`（RemoteBytesProbe 确认远端零字节）/ `failed-bytes-unverified`）上传 → 远端普通文件/reparse point 校验 + SHA-256 复核（远端以 `FileShare.Read` 打开，1454-1458）→ 短命令 `pwsh.exe -NoLogo -NoProfile -NonInteractive -File` 执行、非零退出码透传（1448-1475）。其失败走本地 throw 路径（1476-1545）：`Invoke-OfficeEncodedCommand` 抛出后进入证据回读与汇总 throw，不经 ssh 账本条目。远端暂存 `$remoteRoot = "D:\Codex\deploy\clock-countdown-$shortCommit-$runId"`（1334），目录建立用短 `Invoke-OfficeEncodedCommand`（1339）。

### 身份门（全部在首次 SCP 之前，1316-1333）

`Resolve-OfficeSshClientIdentity`（862-948，System32\OpenSSH 成对 ssh/scp 优先、PATH 兜底且同目录配对、无候选 throw）→ `Resolve-OfficeSshEchoProbeCharacterCount`（950-995，target=32768、minimum=22644）→ 写 `ssh-client-identity.json`（1324-1327）→ `Invoke-OfficeSshEchoBehaviorProbe`（997-1037，约 32KB 只读 echo、BEGIN/END 标记、截断/失败 throw 且错误含二进制路径/版本/实测截断长度）。选定绝对路径钉进 `$script:sshExecutablePath`/`$script:scpExecutablePath`（1317-1318）。

### 严格 SSH 参数集

`Deploy-OfficeCountdown.ps1:1306-1315` `$script:sshOptions`，16 元素（`-i` + 7 个 `-o`：BatchMode=yes、IdentitiesOnly=yes、ConnectTimeout、ConnectionAttempts、StrictHostKeyChecking=yes、UserKnownHostsFile、KexAlgorithms）；SCP 复用同数组。

### 账本 schema

`Countdown.TransferLedger.psm1:6-180`：`transferAttemptsVersion='1.0'`、`generatedAtUtc`、`attempts[]`（id/method（`'scp'`|`'ssh'`）/status 四态/exitCode/transferStarted/bytesVerified/metrics（bytes、elapsed、MiB/s、Mbps））。账本不记录命令行、私钥、known-hosts 或其他 SSH 参数。

### 不在本期范围

第一次回读（binding 读取，`Invoke-CountdownRemoteDeploymentBindingRead`，810-848）也是 EncodedCommand 但很短，不在截断范围，本期不动。

### 测试与验证现状

回读契约测试 `Countdown.Core.Tests.ps1:2945-3079`（含 3022-3024 公共 adapter 账本 id/顺序断言）；SCP 化静态断言 3790-3846（3790-3801 部署脚本 SCP 调用计数与显式传参断言，3816-3824 AST 禁裸 ssh/scp 只扫部署脚本，3837-3846 身份解析先于首次 SCP 的顺序断言）；身份/探针单测 3872-4020。`Countdown.TransferLedger.Tests.ps1` 的唯一断言原语是 `Assert-Equal`（:9 定义），全文件静态出现 138 行（含定义行），另有 7 个 drift 循环用例（848-889）；该文件未被 README 本机验证清单、12 步 runner（`tools/Invoke-CountdownVerificationRun.ps1`）或 CI 引用，需手动运行。README.md「本机验证」共 10 条命令（8 条套件 + 97/98 两个 `-ValidateOnly`）；`-ValidateOnly`（`Deploy-OfficeCountdown.ps1:1076-1100`）纯本地、不解析 SSH 身份、不触网、不跑探针。

## 目标协议设计

1. **脚本构造与上传（编排层新增独立记账步骤）**。reader 脚本本体构造不变（`Countdown.RemoteDeployment.psm1:1195-1222` 内容不变：7 个函数定义 + 期望值 JSON Base64 内嵌脚本文本）；1224-1241 的 gzip+Base64 压缩段与 1242-1259 的解压 bootstrap 一并移除——SCP 上传的是纯文本脚本，远端 `pwsh -File` 直接执行。上传步骤移入编排层 `Invoke-CountdownRemoteDeploymentIndependentReadback`（1350）：UTF-8 无 BOM 落盘本次 run-evidence 目录 → SHA-256 → 在调用 `Invoke-CountdownRecordedSshRead` **之前**，以 `Invoke-CountdownRecordedScpTransfer` 作为独立记账步骤上传到 `$remoteRoot` 下固定文件名（如 `post-deployment-independent-readback.ps1`），新增账本 attempt（method=scp，新 Id 如 `post-deployment-independent-readback-script`）。账本条目顺序：…→`remote-deployment-binding`→新增 scp 条目→`post-deployment-independent-readback`。上传不得留在 Reader 内：`Invoke-CountdownRecordedSshRead` 在 `:557` 读账本入内存、`:572` 执行 Reader、`:672-674` 整本覆盖写盘，内层 SCP 条目会被过期副本覆盖丢失，且 ssh 条目 metrics 会静默包含上传耗时；`Invoke-CountdownRecordedSshRead` 本体不改，ssh 条目 metrics 因此只含短命令执行耗时。
2. **短命令执行（Reader 只做短命令回读）**。照抄 `Deploy-OfficeCountdown.ps1:1448-1475` 的远端「普通文件/reparse point 校验 → SHA-256 复核 → `pwsh.exe -File` 执行 → 非零退出码透传」模式，但需捕获 stdout（回读 JSON），仍走 `Invoke-CountdownSshBase64CommandRead`（Base64 可解码性与 1..MaximumDecodedBytes 校验 520-534、16 元素校验 456-482 原样复用；UTF-8 严格解码留在编排层 1432-1433）。既有账本条目 `post-deployment-independent-readback`（method=ssh）保留，只是 RemoteCommand 变为固定短命令。
3. **注入点扩展与 plumbing**。`New-CountdownRemoteDeploymentReadbackSshAdapter`（1281-1348）扩展捕获 scp 绝对路径/选项/RemoteBytesProbe，或新增 SCP 版 adapter 工厂；编排层（1350）与 adapter 当前无 `$remoteRoot` 来源、无 scp destination 形态（`user@host:path`，参照 `Deploy-OfficeCountdown.ps1:1345/1443`）——需新增参数；短命令包装需内嵌远端脚本路径与本地预算的期望 SHA-256。调用现场（1553-1570）已有 `$script:scpExecutablePath` 与 `$script:sshOptions` 可用。编排层需新增 scp 相关可选注入参数（runner 级）以便测试夹具替换，否则测试会真实启动 `scp.exe`。
4. **跨模块绑定补齐**。`Countdown.RemoteDeployment.psm1:67-98` 按既有同形 `NewBoundScriptBlock` 转发块新增 `Invoke-CountdownRecordedScpTransfer` 绑定（参数签名对齐 `Countdown.TransferLedger.psm1:252-266`；其依赖 `Read/Write-CountdownTransferLedger` 与 `Invoke-CountdownScpProcess` 同模块内解析，无需额外闭包），并把 71-74 的导出存在性检查扩展到该命令。
5. **远端残留决策**。脚本落在按 runId 唯一的 `$remoteRoot` 内，与 remote-install.ps1 同等卫生等级；当前 `$remoteRoot` 本无清理约定，本期不新增清理义务，在此明示。
6. **连带点处置**。1262 的 30000 字符安全门与 TransferLedger 450-455 的门保留，转为保护固定短命令的纵深防御；探针 32KB 档位不缩（无授权）。
7. **账本条目变化**。attempts 由 4 条变 5 条（新增 scp 条目，顺序见第 1 条）——schema 不变但验收基线要更新，列为验收注意项。

失败语义映射（复合形态）：

- SCP 上传失败 → scp 条目按既有四态语义记录（345-356），无 ssh 条目；编排层 catch（1465-1467）按「已提交但独立验证不完整，未回滚」报告。
- 上传成功但短命令未执行（ssh 无法启动或长度门拦截）→ scp=`succeeded` + ssh=`failed-bytes-unverified`，exitCode=-1（450-455、540-544；status 映射 655-663）。
- 短命令远端非零退出（含远端哈希不匹配在执行前中止）→ scp=`succeeded` + ssh=`failed-bytes-unverified`，exitCode 为透传的非零码（512-518）。其账本呈现与 remote-install 的本地 throw 路径（`Deploy-OfficeCountdown.ps1:1476-1545`）不同：后者由 `Invoke-OfficeEncodedCommand` 抛出后进入证据回读与汇总 throw，不产生 ssh 账本条目。
- 截断无法静默篡改脚本本体（文件 + SHA-256）；残余风险只剩固定短命令本身，由身份门探针继续兜底。

## 不变量清单

1. 账本 schema 冻结：不增删改字段；账本不记录 SSH 参数/凭据；ssh 二进制路径/版本只进 run-evidence，不进 `transfer-attempts.json`。
2. 16 元素严格参数集不增删；#75 可选可执行路径参数接口签名与默认裸名兼容语义不破。
3. 探针语义不变：约 32KB 只读 echo、完整往返放行、截断/无候选闭锁、错误三要素（路径/版本/截断长度）、负向夹具可红；无缩档授权。
4. `-ValidateOnly` 边界不变。
5. AST 禁裸 ssh/scp 合同持续生效：新增 SCP 上传调用点位于 `Countdown.RemoteDeployment.psm1` 模块内，必须经注入拿到候选解析出的绝对路径，并在调用点显式传 `ScpExecutablePath`/`ScpOptions`，不得依赖 `Countdown.TransferLedger.psm1:219-224` 的裸名 `scp.exe` 默认。
6. 部署验收形态不变：成功回执、deployment binding、两次独立回读字段级匹配（Host/User/Token/ProcessId/ExecutableHash/SettingsHash）、账本全 Valid；既有账本条目 `post-deployment-independent-readback` 的记录结构不变。
7. `docs/办公室部署.md` 第 50、56、58 行以「回读 = ssh 远程命令」为合同前提，实现时需同步修订；`README.md:102` 只述「两次独立远端回读」与探针描述、通道无关，降级为可选同步点。

## 测试与验证计划

- 更新 `Countdown.Core.Tests.ps1:2945-3079`：回读契约改为「上传 + 短命令」双通道，注入假 scp/ssh runner；3022-3024 公共 adapter 账本 id/顺序断言纳入新 scp 条目（期望 id 序列变为 `remote-deployment-binding,post-deployment-independent-readback-script,post-deployment-independent-readback` 或等效）。
- 3790-3846 调整口径：新 SCP 调用点在模块内而非部署脚本，3790-3801 对部署脚本源码的 SCP 调用计数保持 2 不变；3837-3846 顺序断言保持。新增模块级静态断言：模块内 SCP 调用点必须显式传 `ScpExecutablePath`/`ScpOptions`（防静默退回裸名 `scp.exe`，`Countdown.TransferLedger.psm1:219-224` 默认行为）；AST 禁裸 ssh/scp 合同（3816-3824 目前只扫部署脚本）需扩展覆盖该模块调用点或新增等效断言。
- `Countdown.TransferLedger.Tests.ps1` 列入必改：成功 twin（810-843）断言最后两条 attempt id 恰为 `remote-deployment-binding,post-deployment-independent-readback`（836-837），失败切片（776-784）用 `[-2..-1]` 取末两条——新增 scp 条目后必然 mismatch，需更新为含新 scp 条目的 id/顺序（切片相应改为 `[-3..-1]` 或等效），并新增 scp 假 runner 注入；adapter 无 scp 注入点时测试会真实启动 `scp.exe`。
- 新增负向夹具：远端哈希不匹配 → 执行前中止（scp=`succeeded` + ssh=`failed-bytes-unverified`）；SCP 上传失败 → 账本四态映射正确（仅 scp 条目、无 ssh 条目）。
- 探针尺寸语义测试（3872-4020）保持。
- README 本机验证 10 条（8 条套件 + 97/98 两个 `-ValidateOnly`）全绿 + `git diff --check` 无错误；本改动属共享逻辑/发布边界变化，按 AGENTS.md 跑完整组合。`Countdown.TransferLedger.Tests.ps1` 不在 runner 内，实现后须手动运行。
- 文档同步点：`docs/办公室部署.md` 第 50、56、58 行（必改）；`README.md:102`（可选）。
- 端到端：一次真实办公室正式部署验收（issue 验收草案第 5 条）。

## 验收标准建议稿

以下为草案，实现时按原文定稿；五条已于 2026-08-27 全部验收通过（含账本条目 4→5 的基线确认）：

1. 部署后独立回读 SCP 化：reader 脚本经 SCP 上传 `$remoteRoot`，短 ssh 命令执行并回读结果；回读通道任意 ssh 远程命令长度有固定小上界，与脚本大小无关。账本条目顺序固定为 …→`remote-deployment-binding`→新增 scp 条目→`post-deployment-independent-readback`。
2. 身份门探针语义不变：约 32KB 只读 echo、完整往返放行、截断/无候选闭锁、错误三要素齐全。
3. 传输账本 schema 与 16 元素严格 SSH 参数集不变；新增 scp 条目复用既有四态语义；上传独立记账，ssh 条目 metrics 不含上传耗时。
4. README 本机验证 10 条（8 条套件 + 两个 `-ValidateOnly`）全绿，`git diff --check` 无错误；`Countdown.TransferLedger.Tests.ps1` 手动运行通过。
5. 一次真实办公室正式部署端到端验收通过：成功回执、deployment binding、两次独立回读字段级匹配、账本全 Valid。

## 风险与未决项

- 账本 attempts 由 4 条变 5 条，schema 不变但验收基线（条目计数、id 与顺序）需经 issue 验收确认。
- #74/#77 存在与关闭状态矛盾的 2026-08-27 补充评论，以 CLOSED 状态与 17:02 收口证据为准。
- 远端暂存 `$remoteRoot` 清理属在案卫生事项，本期未执行、不新增义务。
- 实现排期前提「#74 已收口」当前满足；若 #74 状态回摆，本方案的合同引用需重新核对。
- 回读脚本在部署后新落盘，被杀软/索引扫描锁定的窗口比 remote-install 更现实（远端以 `FileShare.Read` 打开，`Deploy-OfficeCountdown.ps1:1454-1458`）；其失败表现为回读失败（「已提交但独立验证不完整，未回滚」）而非部署失败。
- stdout 纯度：新形态下 wrapper 或 `pwsh -File` 的任何额外 stdout（进度流/警告）会使 Base64 解码失败（`Countdown.TransferLedger.psm1:521-527`），wrapper 必须零额外输出；切勿把 remote-install 经 `Invoke-OfficeEncodedCommand` 的 `2>&1` 合并流（`Deploy-OfficeCountdown.ps1:21`）带进回读通道。

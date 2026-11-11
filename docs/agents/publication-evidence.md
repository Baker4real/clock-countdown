# 发布证据检查

提交或发布版本化文档、JSON 回执、截图与源码绑定时，先运行 [Test-CountdownPublicationEvidence.ps1](../../tools/Test-CountdownPublicationEvidence.ps1)。本机默认检查暂存区；CI 检查实际提交。两者使用同一个 Git 树中的 blob，不读取工作树文件来补齐遗漏。

```powershell
pwsh -NoLogo -NoProfile -File ./tools/Test-CountdownPublicationEvidence.ps1 -Source Index -Json
pwsh -NoLogo -NoProfile -File ./tools/Test-CountdownPublicationEvidence.ps1 -Source Commit -Commit HEAD -Json
```

`Index` 以 `git write-tree` 固定快照；未解决的索引冲突会失败。`Commit` 解析指定 tree，之后的暂存或工作树变化不能掩盖该提交的错误。失败报告包含引用者、缺失目标或不匹配哈希和被核验的树身份。

## 链接与哈希口径

所有受跟踪 Markdown 的仓库内链接目标必须存在于该树，目录链接也按树内目录判断；外部 URL 和页内锚点不作本地文件校验。磁盘上被忽略或未跟踪的同名文件不满足目标存在性。路径区分大小写，不能依赖 Windows 文件系统掩盖 CI 的路径问题。

需要机器核验的当前证据根放在 `docs/evidence/`，声明 `"hash_scope": "git-blob"`。检查器验证根内及其可达 JSON 中的文件引用：`path`/`sha256`、`path`/`pngSha256`，以及同一对象中的 `manifest`/`manifest_sha256`、`report`/`report_sha256` 等成对字段。被引用 JSON 继续递归检查，循环引用只读取一次。[现役证据根登记](publication-evidence.json)固定必需根；删除登记中的文件或移除其 `hash_scope` 都会失败。没有声明的历史证据不被冒称已验证。

一般引用相对当前 JSON；现役视觉合同的 `sourceBinding`、`current_source_bindings` 和 `captures` 内路径相对仓库根。需要新增引用形状时，同时添加公开接口的正反例，不靠猜测字段名扩展其含义。

版本化哈希以 Git blob 的实际字节为准。写新 JSON 时正常导入 [Countdown.PublicationEvidence.psm1](../../tools/Countdown.PublicationEvidence.psm1)，使用 `Write-CountdownEvidenceJson -Path <file> -Value <object>`：它写 UTF-8 无 BOM 和 LF，再由调用方计算引用哈希。该写入器不会重算或修复已有哈希；修改被引用内容后，旧引用应当报红。基线生成入口已使用此写法。

`run_artifact_*` 明确保留原始运行文件的哈希，不用于替代发布字节哈希。绝对路径、URL，以及未版本化的 `logs/`、`app/`、`verification/` 运行产物只计为 `ExternalReferences`；检查器不读取本机产物来声称其可从 Git 复核。那些产物的实际验证仍由原运行阶段负责。

## 验证入口

[Tooling](../../tools/Test-CountdownTooling.ps1)运行同一个检查和[回归夹具](../../tools/Test-CountdownPublicationEvidence.Tests.ps1)，覆盖忽略目标、暂存/提交隔离、PNG 篡改、缺失引用、CRLF 与 LF 的不同哈希。发布前须先暂存拟提交文件；运行 Tooling 不会自动替用户添加文件。

[正式套件入口](../../tools/Invoke-CountdownSuiteLowImpact.ps1)的 `Build` 表示 Clean + R2R；`IncrementalBuild` 明确表示开发迭代。`-Closeout` 必须包含六个正式阶段，在长阶段之前检查当前 checkout 的清理能力，并要求构建成功后实际输出的模式回执。阶段执行请求和退出0本身都不能代替回执；中断、缺阶段、增量构建和无 R2R 不能得到成功收尾结果。

[SuiteRun](../../tools/Countdown.SuiteRun.psm1)只对有明确合同的 Tooling stdout 解析阶段级 JSON，优先投影最终失败检查并保留 stderr 补充诊断；截断、无效 JSON 或其他阶段仍使用现有日志摘要。负例见[摘要回归](../../tools/Test-SuiteStageFailureExcerpt.ps1)和[构建完成回归](../../tools/Test-SuiteCompletion.ps1)。

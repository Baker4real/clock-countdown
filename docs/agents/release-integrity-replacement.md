---
type: reference
title: 发布完整性接替门与静态钉值退役对照
tags: [release, integrity, deployment, installer]
intent: 记录 #294 删除手工静态 SHA-256 前逐项核对的失败模式与接替门。
---

# 发布完整性接替门与静态钉值退役对照

## 结论

#290–#293 后的真实树只剩 **16 个手工静态 SHA-256 钉值**：两个安装器文件和 14 个 `BuildAssets` 资产。旧探索中的 29 个钉值包含已经退役的旧部署、旧安装、adapter、启动协议和 PowerShell 设置实现，不属于本次现行基线。

**无保留。** 下表每一类都有接替门，没有因为找不到接替而留下的静态钉值。`BuildInputFiles` 与 `SourceBuildFiles` 在本基线已无生产消费者或公开属性；本票只清理残留说明，不新建另一份源码清单。`BuildAssets` 的源到目标映射继续保留，因为它是构建复制与禁入未声明资产的实际输入，不是 C# 源码清单。

## 现行信任链

1. **提交 blob 身份：** 正式部署预检对两个安装器文件和全部 `BuildAssets.SourcePath` 同时读取提交 `ls-tree` 与工作树 `git hash-object --no-filters`。原字节不同即以 `source-blob-mismatch` 拒绝；干净树、`core.autocrlf=true` 或重算生成清单都不能替代这道门。
2. **已测构建输出：** Build 保留源资产与目标资产逐字节一致检查，生成 `app\published-payload.json`；CleanR2R 回执的 `PayloadAggregateSha256` 与部署时构建输出必须一致。部署不重新导出、不重建。
3. **全包生成清单：** `New-CountdownDeployPackage` 在真实 ZIP 内生成根级 `package-manifest.json`，列出除自身以外的每个包文件及 SHA-256，并有清单聚合哈希。应用载荷清单继续单独记录提交、应用文件和聚合身份。
4. **清单自身绑定：** 整包 ZIP SHA-256 经传输和远端入口核对；部署端另把生成清单 SHA-256 注入远端安装脚本，解包后先核对清单，再允许安装器启动。只改包内清单并同时重算外层 ZIP 哈希仍会在安装器启动前失败。
5. **安装精确闭包：** 真实安装与 `CheckOnly` 都在安装锁、中断恢复、候选创建和任何 Windows port 之前核对根级清单、实际文件集合、逐文件哈希以及两份清单的一致性。改动、多出、缺少均拒绝；候选复制后再次核对程序目录精确闭包。
6. **安装后状态：** 现有启动就绪、已安装 EXE 与载荷清单核对、提交与聚合哈希状态读取保持不变。

## 逐项对照

| 原静态钉值类别 | 原挡住的失败 | 接替门 | 处置 |
| --- | --- | --- | --- |
| `Install-CountdownProgram.ps1` | 安装入口被改、CRLF 转换后的字节被当作原版本、传输或解包后漂移 | 提交 blob 身份；根级生成清单；ZIP SHA-256；部署端注入的清单 SHA-256；安装前全包精确闭包 | 删除静态钉值 |
| `Countdown.Installer.psm1` | 安装实现被改、包与审核版本不一致、传输或解包后漂移 | 同上；包内副本与仓库副本分别经受来源门和解包门 | 删除静态钉值 |
| 三张 `sanrio-official-{mymelody,cinnamoroll,kuromi}.png` | 官方角色资产源被改、发布副本与源不同、套件后被换 | 提交 blob 身份；Build 源到目标字节一致；应用载荷清单；CleanR2R 聚合；根级清单与 ZIP；安装精确闭包 | 删除三个静态钉值 |
| 四张 `assets\floating-plush\component-day-*.png` | 现役悬浮组件被改、旧资产混入、发布副本漂移 | 同上；BuildAssets 精确映射和未声明资产拒绝继续保留 | 删除四个静态钉值 |
| 两张 `assets\main-decorations\main-*-scene-day.png` | 管理窗口装饰源或发布副本被改 | 同资产接替链；视觉基线自己的现役 manifest 与聚合哈希不变 | 删除两个静态钉值 |
| `holiday-data\2026.json`、`2027.json` | 年度数据被改、换行漂移、旧构建输出进入部署 | 提交 blob 身份；Build 字节一致；应用载荷与 CleanR2R 聚合；根级清单与 ZIP；安装闭包；年度覆盖守卫独立保留 | 删除两个静态钉值 |
| `holiday-data\README.md`、`LICENSE.txt` | 数据说明或许可证文本被改、CRLF 被重算哈希合法化 | 提交 blob 身份与 `.gitattributes` LF；Build 字节一致；应用载荷；根级清单与 ZIP；安装闭包 | 删除两个静态钉值 |
| `countdown-icon.ico` | 图标源或程序目录副本被改 | 提交 blob 身份；Build 字节一致；应用载荷与 CleanR2R 聚合；根级清单与 ZIP；安装闭包 | 删除静态钉值 |

## 合法变化与篡改分开验收

- **合法源文件变化：** 修改安装器或资产源后，正常 Build / Deployer 直接生成新清单，不要求 `git add` 后运行哈希更新工具。正式部署仍要求变化已提交、推送、通过同一提交套件并满足 blob 身份。
- **源码文件增删：** C# 项目由 SDK 项目输入编译；发布契约不登记 `BuildInputFiles` / `SourceBuildFiles`，新增或删除源码不需要同步发布清单。
- **产物或包被篡改：** 不改载荷清单而改构建输出会被部署打包拒绝；改包内文件、增加文件、删除文件会被安装器拒绝；同时改清单和外层 ZIP 哈希仍被部署端注入的清单哈希拒绝。自动化没有放宽这些失败路径。

行为覆盖位于 `Countdown.Core.Tests.Installer.ps1` 的 `reject-*` 包闭包用例，以及 `Countdown.Core.Tests.DeployEntry.ps1` 的真实 ZIP、合法源变化、清单绑定、真实 `core.autocrlf=true` 克隆和强制 CRLF 反例。`tools/Invoke-CountdownInstallValidation.ps1` 由真实 Deployer 生成并解压测试包，再运行包内安装器 `CheckOnly`；仓库源码树不再冒充发布包。

## 不在本次退役范围的哈希

第三方 NuGet / 上游许可证的固定来源身份、视觉基线 manifest、应用载荷生成哈希、CleanR2R 聚合哈希、运行状态与部署记录的 SHA-256 都是现役独立契约，不是需要手工同步的 `IntegrityHashes`。这些保护继续保留。

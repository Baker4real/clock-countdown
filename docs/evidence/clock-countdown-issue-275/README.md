# #275 程序目录资源后的 v79 基线重拍

#275 让构建把随包节假日数据与应用图标复制进 `app\`，`HolidayCalendar` 改从程序目录读取随包年份。改到的 `src/MinminCountdown/MinminCountdown.csproj` 在主窗与悬浮窗的源码绑定内，`src/MinminCountdown/HolidayCalendar.cs` 在悬浮窗的源码绑定内。集成 tip `3994cd21ecafe127716d26761050faea40eb2141` 上 CSharp 以 75 退出，只延后 `SourceHashesMatch`，14 帧实拍与 v78 零像素差；视觉源码确实变了，所以按 README「本机验证」重拍，不属于“视觉源码没改却失配”的情形。

## 重拍过程

`Set-CountdownBaselineVersion -WhatIf` 确认已用最大编号为 v78 后应用 `2026-11-14-v79`，钉值核对 29 项全部一致、未改契约。之后在本 worktree 依次经低影响启动器运行，均未加 `-SkipLoadPreflight`，负载预检正常放行：

- Build `20261114T094833876-dabd4814`：退出 0，`CleanR2R` 回执，候选 DLL SHA-256 `AD86CD9433B590251120615F28D099C52274590A919E46A79ACA16D56B609A6D`。
- BaselineGenerate `20261114T095041355-350b5f3b`：单独运行，退出 0；生成报告的 `Valid`、两窗 `ReferenceBaselineValidated` 与 `CrossInterfaceReferenceBaselinesCurrent` 均为 true。生成器按 #268 移除了两窗共同的上一版 v78 目录。
- CSharp `20261114T095237972-0e664134`：单独复验，退出 0，无延后项；日志中悬浮窗 DPI 为 144，工作区 2560×1600。

## 结果

[跨版本比对](v78-v79-comparison.txt)以 `3994cd2` 的 Git blob 为旧版：主窗 9 帧与悬浮窗 5 帧的 PNG 和状态指纹全部逐字节一致，两窗 EXE 哈希都仍是 `FE8A141A1F896F9CB3ACC814BB5A13628A9691830D8F4235952E28C0E4086EF5`。绑定文件变化：主窗只有 `MinminCountdown.csproj`，悬浮窗为 `HolidayCalendar.cs` 与 `MinminCountdown.csproj`。

- 主窗源码绑定聚合：`1FA92C596B2262F3A9613A6496838E18E64E384E5352381098ED6758ECEE463C` → `B7974DA410A3260DCA5B43BBA2B74C8525F6FD3B5F655FF6421B2D004C36E151`（23 项）。
- 悬浮窗源码绑定聚合：`4D574BABAC0D501E1307B982BA8D79CB41D1EC7C69E6ED3B1A2F9F0DDDC36239` → `0C4B72CBC5D8B79C904717AF66669461B1B858C6856F45BD7F50361543DE2283`（28 项）。

[生成事实](v79-evidence.json) 记录运行编号、负载、显示 DPI、双 manifest 的聚合与文件哈希。现役源码绑定指针 [final-source-binding.json](../clock-countdown-issue-253-final-surfaces/final-source-binding.json) 的 `current_source_bindings` 已改指 v79，[验收状态](../clock-countdown-issue-253-final-surfaces/acceptance-status.json) 中对它的哈希随之更新；该目录的 v75 捕获身份保持不变。[DESIGN 现役记录](../../../DESIGN.md) 已改到 v79，保留 v78 历史事实。

本次重拍只证明参考帧与当前源码、EXE 重新绑定；#275 的行为验收与正式组合门禁另行执行。

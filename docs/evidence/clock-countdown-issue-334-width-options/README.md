# #334 悬浮窗便签正文列加宽：v1 对比稿

> 仅设计对比稿与证据，不改产品代码、不实施 #335、不进视觉基线。推荐方案 B；用户尚未在 #334 评论选定，选择项保持 **unverified**，由 orchestrator 发布对比稿并征求选择。

Method: design review only (detector not applicable, native WPF)

## 推荐

**选择方案 B（上缘单行日期）作为用户选择时的推荐。** 它把正文可用宽从 189 DIP 增至 237 DIP，增量 48 DIP / 25.4%，全角汉字估算从约 14 字/行增至约 18 字/行；勾选与 `...` 的 36 DIP 命中槽、319 DIP 卡宽、`x=396` 正文右界均不变。代价是每张卡增加 22 DIP 日期带，并需把现有三行印记的可见条带内粘性行为改写为单行上缘粘性。

方案 A 是保守退路：结构与现有三行印记最接近，但只增加 6 DIP，估算只多 1 个全角字，收益有限。

## 现役基线与几何口径

- 分支基点：`73ad5f46440eb8b2bbe3118c141cf0e0792f8d29`，当时与 `integration/prd-296` 相同。生成时源码身份及逐文件绑定见 [preview-manifest.json](preview-manifest.json)。
- 现役真实帧：[v79 manifest](../../../assets/ui-references/floating-window/520x390/2026-11-14-v79/manifest.json) 中的 [long-notes-day-top.png](../../../assets/ui-references/floating-window/520x390/2026-11-14-v79/long-notes-day-top.png)。PNG SHA-256：`39A148822F95D1FE53C2F0EEB59956BAB1B0ACE58BE330E1A789F4C61B3533B8`。
- 悬浮窗 `sourceBinding` 聚合 SHA-256：`0C4B72CBC5D8B79C904717AF66669461B1B858C6856F45BD7F50361543DE2283`；校验器对 manifest 列出的 28 个文件逐一重算当前工作树 SHA-256。
- 几何来自 [DESIGN §10](../../../DESIGN.md#L513-L530)、[FloatingWindow.Notes.cs](../../../src/MinminCountdown/FloatingWindow.Notes.cs#L15-L21) 与 [FloatingWindow.xaml](../../../src/MinminCountdown/FloatingWindow.xaml#L219-L340)：319 DIP 卡宽内含 2 DIP 左右边、36 DIP 勾选槽、197 DIP 正文的 Grid 列、48 DIP 日期列和 36 DIP `...` 列；正文 StackPanel 右侧再有 8 DIP 间距，因此现役真正可排文字宽为 `197 - 8 = 189 DIP`。
- 每行字数是**设计估算而非 WPF 实测**：全角口径为 `floor(可用 DIP / 13)`。票面所写约 12 字是经验口径；本稿统一用 13 DIP 全角估算，并用本机 Microsoft YaHei 13 px 对 v79 同一条长便签做换行复核。混合中英文、标点悬挂与真实 WPF 栅格化仍须由选定后的实现验证。

## 方案 A：三行窄印记

![方案 A 与现役帧并排](v1-option-a-narrow-stamp.png)

- **重新分配**：日期列 `48 -> 42 DIP`，正文 Grid 列 `197 -> 203 DIP`，可用文字宽 `189 -> 195 DIP`，约 15 个全角字/行。印记左右内边距 `4 -> 0`，保留 2 DIP 左边线，文字可用宽由 38 增至 40 DIP。
- **日期印记可读性**：`7 月 / 15 / 周一` 与 `无日期 / --` 文案、13/20 DIP 字号、pastel 轮换都不变；不为加宽正文而缩小日期字号。三行文字仍不可点击、不可聚焦。
- **长便签与滚动**：保留现有可见条带内居中粘性。v79 同一长便签在本机字体估算下仍为 7 行，首卡高度差估为 0 DIP；继续使用唯一 `FloatingBodyScroller`，不加卡内滚动容器。
- **勾选与 `...` 命中**：两列都保持 36 DIP，菜单位置与触发器尺寸不变；正文右侧仍留 8 DIP，不借加宽侵占命中区。
- **取舍**：实现面最小，但宽度只增 3.2%，对长便签行数没有实质改善。

## 方案 B：上缘单行日期（推荐）

![方案 B 与现役帧并排](v1-option-b-top-date.png)

- **重新分配**：移除 48 DIP 右侧文字印记列，正文 Grid 列 `197 -> 245 DIP`，可用文字宽 `189 -> 237 DIP`，约 18 个全角字/行。原正文右 8 DIP 间距与右侧 `...` 列保留。
- **日期印记可读性**：改为正文上缘 13 DIP 单行 `7 月 15 日 · 周一`，无日期时完整显示 `无日期`，继续使用原 pastel 底色与文字色；一行即可保留月、日、星期，不需要三行窄排。
- **长便签与滚动**：日期带高 18 DIP、与正文间隔 4 DIP。v79 同一长便签估算从 7 行降至 6 行，抵消部分日期带成本，首卡高度净差估为 **+4 DIP**；更长正文才有更明显的高度收益。短卡仍由现有 72 DIP 最小高度吸收。长卡被视口裁切时，单行日期应在卡片可见条带内粘性置顶，而不是滚出后留下空白；仍只用外层列表滚动与现有上下滚动提示，不新增内部滚动。
- **勾选与 `...` 命中**：36 DIP 勾选槽和 36 x 36 DIP 菜单触发器不动；正文不进入菜单列。搜索展开导致视口变矮时，日期带按可见条带可达范围停靠，不能覆盖搜索槽、底部命令区或菜单弹层。
- **取舍**：宽度收益明确，日期扫描从右侧三行改为正文上缘一行；需要调整印记粘性方向，并由 #335 实测短卡、长卡、搜索展开和无日期状态。

## 数字对比

| 口径 | 现役 | A 三行窄印记 | B 上缘单行日期 |
| --- | ---: | ---: | ---: |
| 卡宽 | 319 DIP | 319 DIP | 319 DIP |
| 勾选 / `...` 槽 | 36 / 36 DIP | 36 / 36 DIP | 36 / 36 DIP |
| 日期文字列 | 48 DIP | 42 DIP | 0 DIP，移入正文上缘 |
| 正文 Grid 列 | 197 DIP | 203 DIP | 245 DIP |
| 正文可用文字宽 | 189 DIP | 195 DIP | 237 DIP |
| 全角字/行估算 | 约 14 | 约 15 | 约 18 |
| v79 同条长便签行数估算 | 7 | 7 | 6 |
| 首卡高度差估算 | 基线 | 0 DIP | +4 DIP |

## 生成与最窄检查

生成器 [generate_previews.py](generate_previews.py) 直接读取上述 v79 PNG，以 Pillow 和本机 Microsoft YaHei / Bahnschrift 字体合成卡片区域；不使用生成式生图。左帧是原图逐像素副本，右帧只改 `x=60..378, y=147..277` 的卡片可见区域。预览清单 [preview-manifest.json](preview-manifest.json) 固定版本号、源提交、源帧哈希、几何、换行估算和每张预览 SHA-256。

从仓库根运行：

```powershell
& 'C:/Users/WINDOWS/.dsh/dsh-runtimes/dsh-primary-runtime/dependencies/python/python.exe' 'docs/evidence/clock-countdown-issue-334-width-options/generate_previews.py'
& 'C:/Users/WINDOWS/.dsh/dsh-runtimes/dsh-primary-runtime/dependencies/python/python.exe' 'docs/evidence/clock-countdown-issue-334-width-options/validate_previews.py'
```

[validate_previews.py](validate_previews.py) 失败即非零退出，覆盖：PNG 解码与 1112 x 650 尺寸、预览 SHA-256、v79 原帧 SHA-256、28 个现役绑定源文件哈希、左帧像素完全一致、右帧差异限制在卡片可见区域、版本化文件名、非视觉基线目录，以及本 README 本地链接和影响说明存在性。

## 验收状态与交接边界

| #334 acceptance | 状态 | 证据 / 未决项 |
| --- | --- | --- |
| 2-3 个方案各与现役帧并排，标正文宽度与每行汉字数 | verified | 两张 v1 PNG；189/195/237 DIP 与约 14/15/18 字口径 |
| 每方案说明日期、长便签滚动、勾选与 `...` 命中影响 | verified | 上文逐项说明；长便签行数与高度为估算，非 WPF 实测 |
| 给出推荐，并请用户在评论里选定；选定前不改产品代码 | **unverified** | 已推荐 B 且未改产品代码；orchestrator 尚未发布评论，用户尚未选定 |
| 对比稿进入设计预览目录，不进视觉基线 | verified | 全部产物仅在本目录；清单 `visualBaseline=false`，差异检查保护现役帧 |

本票不声称完成 #335 的真实 WPF 几何、五标签、搜索、滚动、焦点或命中回归；这些只在用户选定后进入实施与验收。

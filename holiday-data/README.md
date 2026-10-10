# 离线节假日日历数据

`2026.json` 是随包离线兜底数据。它覆盖 2026 年 1 月 1 日至 12 月 31 日的每一天，`is_workday=true` 表示按中国大陆法定节假日与调休安排应工作的日期；周末补班和法定休假均已展开为完整日历。

## 来源与许可证

- 上游项目：[cg-zhou/holiday-calendar](https://github.com/cg-zhou/holiday-calendar)
- 固定来源提交：[`5e6ca2f0d8d50495b850697a60b2dc3d5dff588b`](https://github.com/cg-zhou/holiday-calendar/tree/5e6ca2f0d8d50495b850697a60b2dc3d5dff588b)
- 上游年度源：[data/CN/2026.json](https://github.com/cg-zhou/holiday-calendar/blob/5e6ca2f0d8d50495b850697a60b2dc3d5dff588b/data/CN/2026.json)
- 许可证：MIT；许可证正文见同目录 `LICENSE.txt`。

仓库只保存转换后的 `days` 数组，不复制上游名称字段。转换规则是：上游 `public_holiday` 映射为 `is_workday=false`，`transfer_workday` 映射为 `true`，其余周一至周五为 `true`、周六周日为 `false`。

## 覆盖和更新

当前随包覆盖年份为 **2026**。上游发布新的国务院安排后，维护者应固定新的上游提交，重新生成对应年份的 365/366 天 `days` 数组，更新本文件中的来源提交、年份和 `IntegrityHashes`，再运行发布契约与验证套件。未随包覆盖的年份不会按工作日规则推断；当本地缓存和联网源都没有该年份时，日历保持未就绪，工资和距离下班均不显示估算，悬浮窗底部状态显示「节假日安排未就绪：距离下班和今日已赚暂不可用，联网后自动恢复」（#218）。

年度覆盖守卫（#218）：当前北京时间进入 12 月后，若本目录尚未包含下一年的随包数据，发布契约定义校验与依赖它的构建、验证和部署都会明确失败，失败信息指明缺失年份并指向本节的补数据流程；11 月 30 日之前或数据齐全时守卫通过。

运行时优先级固定为：应用目录 `holiday-cache\\{year}.json` 的本地缓存，其次是联网结果，最后是本目录随包数据。联网成功且内容有效时会写入本地缓存；联网结果与随包数据冲突时采用联网结果。

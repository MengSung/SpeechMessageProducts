# 遺留 L3 移至好牧人 9.1 上線前作業

## Goal

依 goal-objective.md 修訂 1，L3 不執行 Live 切換、模組檢查或回復；只記錄移至好牧人 9.1 上線前作業的範圍與理由，registration tier/evidence 保持不變。

## Requirements

- 本階段不執行 L3 Live 切換、逐模組 live-check、rollback 或 registration 清冊更新。
- 將 L3 實機補驗與七個 dormant Workflow 活動列入好牧人 D365 9.1 上線前作業。
- 保持 `plugins.registration.json` 的 `tier`、`evidence`、`reason` 原值，不宣稱 Live 通過或本單元組織狀態已驗收。
- 記錄依據：部署工具阻止沒有 Live evidence 的項目切入正式組織，故延後補驗不造成正式部署風險。
- 不執行組織寫入；本單元不推定 `off`、`verify=0` 或殘留 0。

## Acceptance Criteria

- [x] 一頁 L3 回報區分範圍處置完成與 Live 實機未驗收，並列出上線前處置。
- [x] 父任務、決策紀錄、進度頁 DATA 與回報一致，且 registration 清冊未改。
- [x] 提交只包含 L3 紀錄與任務封存資料，訊息以 `P8-L3` 開頭，推送後核對遠端 SHA。

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.
# 遺留 L3：本階段範圍處置

## 需求

依使用者核定的 `goal-objective.md` 修訂 1，P8 階段不執行遺留 L3 的 Live 切換、模組 live-check、rollback 或清冊分級更新。只記錄移至好牧人 D365 9.1 上線前作業的範圍與理由。

## 限制

- 不連線或寫入任何組織。
- 不修改 `plugins.registration.json` 的 `tier`、`evidence` 或 `reason`。
- 不宣稱任何 Live 模組通過，也不宣稱本單元起訖 `off`／`verify=0`／殘留 0。
- 明確說明：部署工具阻止沒有 Live evidence 的項目切入正式組織，因此延後不造成正式部署風險。

## 驗收條件

- 有一頁 `wp-reports/P8-L3-live-recheck.md` 記錄處置結果與上線前工作。
- 父任務、決策紀錄、進度頁 DATA 與本報告一致，清楚區分範圍處置完成與實機未驗收。
- 只提交 L3 紀錄與任務資料；以 `P8-L3` 開頭，推送後核對遠端 SHA。

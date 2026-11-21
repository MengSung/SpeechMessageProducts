# P8-L1 補齊 Live 失敗原因與唯讀調查

## Goal

依 08A §5 遺留 L1：補齊六個 Live preparation/observe 的安全失敗診斷，並唯讀結案 R7、R9、church-defaults 三項調查；不執行組織寫入。

## Requirements

- 僅修改 `SpeechMessage.Dynamics.Deployment`、其測試及直接相關回報／階段紀錄。
- 新原因只追加子代碼或診斷欄位，不改既有原因碼數字。
- 診斷不得輸出 token、設定值、trace message 或其他敏感內容。
- R7、R9、church-defaults 三項調查各限時 30 分鐘且唯讀；組織對象限定 goodshepherdback。不得使用 `artifacts/p8` 的舊一次性 D34／殘留探針，不建立新的 PowerShell probe。
- 查無充分證據時明確記為「去向無法確認」或「目前證據不足」，不推定成功、不延長調查。
- 不執行 apply、migrate、switch、step/workflow 啟停、LINE 發送或任何組織寫入。

## 修訂 1 結案方式（2026-11-21 使用者核定）

本任務改依使用者核定的 `goal-objective.md` 修訂 1 縮減結案。原因碼程式成果沿用已提交 `c819da1e`。原完整技術契約中未完成的項目不在本階段補做，並須照實列為好牧人 9.1 上線前作業。

## Acceptance Criteria

- [x] 已盤點六項測試與實際專案／行為；`RelayPrecheck_Failure_ReportsReasonCode` 位於 `SpeechMessageProducts.LineWebhook.Tests/RelayLiveAcceptanceTests.cs`，只測固定原因碼 formatter 與訊息遮蔽，未覆蓋 Deployment 前置失敗路徑。
- [x] 六項先紅輸出未找到，明確保留為證據缺口，未偽造紅綠紀錄。
- [x] Deployment Release 有保存的 `1036/1036` 通過輸出；較晚 12:03 執行 exit 0，但測試數字未保存。
- [x] R7 結論為「目前證據不足」；R9 結論為「去向無法確認」；church-defaults 結論為「目前證據不足」。不推測、不標成技術通過，並列上線前作業。
- [x] 父任務 `implement.md`、`wp-reports/P8-decisions.md`、L1 回報及進度頁 DATA 已同步為「已驗收（縮減）」並保留未解項目。
- [x] 歷史提交順序偏差已記錄；不改寫既有提交／推送歷史。

縮減驗收只表示依修訂 1 完成 L1 本階段核定範圍，不表示原 08A §5 完整測試、紅綠證據或調查均已通過。

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.

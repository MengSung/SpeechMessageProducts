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

## Acceptance Criteria

- [ ] 六個指定 `SpeechMessage.Dynamics.Deployment.Tests` 測試以原名存在，先紅後綠並通過：
  `LivePrep_RejectedActivity_DoesNotBlockSameModuleSteps`、
  `LivePrep_SafetyFailure_ReportsSubCheckId`、
  `LiveCheck_DataverseFault_ReportsFaultCodeStageAndEntity`、
  `ChurchDefaults_ApplyFault_ReportsRowStateAndIsValidForUpdate`、
  `RelayPrecheck_Failure_ReportsReasonCode`、
  `Observe_TraceExceptions_ReportsTypeNameCountsWithoutMessages`。
- [ ] `dotnet test SpeechMessage.Dynamics.Deployment.Tests -c Release` 全綠。
- [ ] R7 的兩筆 trace exception、R9 診斷 trace 2→0、church-defaults `0x80040224` 各有一段證據化結論；無證據部分依 08A 原文結案，不繼續追查。
- [ ] 父任務 `implement.md`、`wp-reports/P8-decisions.md`、L1 一頁回報及進度頁 DATA 一致更新。
- [ ] 完成單元提交、輕量紀錄檢查、唯一 `C:\smv\guard-p8` 完整守門、推送 P8 分支及 `git ls-remote` SHA 核對。

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.

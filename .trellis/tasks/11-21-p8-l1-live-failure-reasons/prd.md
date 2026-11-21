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

- [ ] 六個指定測試均須保留先紅後綠證據並通過；正式專案歸屬以 08A §5 為準：
  `LivePrep_RejectedActivity_DoesNotBlockSameModuleSteps`、
  `LivePrep_SafetyFailure_ReportsSubCheckId`、
  `LiveCheck_DataverseFault_ReportsFaultCodeStageAndEntity`、
  `ChurchDefaults_ApplyFault_ReportsRowStateAndIsValidForUpdate`、
  `Observe_TraceExceptions_ReportsTypeNameCountsWithoutMessages`
  位於 `SpeechMessage.Dynamics.Deployment.Tests`。
- [ ] `RelayPrecheck_Failure_ReportsReasonCode` 依 08A §5 應位於
  `SpeechMessage.Dynamics.Deployment.Tests`；目前同名測試實際位於
  `SpeechMessageProducts.LineWebhook.Tests/RelayLiveAcceptanceTests.cs`，且只驗證測試fixture的固定原因碼格式化與訊息本文遮蔽，
  沒有覆蓋 Deployment 前置失敗路徑。此專案歸屬與覆蓋差異尚未補測，不能以同名測試通過代替 08A 驗收。
- [ ] 六項測試的先紅輸出未在既有證據中找到；不得事後重建或宣稱曾有紅燈。需在允許範圍補足可驗證的測試先紅證據，或依 D8-26 保留未驗收狀態並續行。
- [x] `dotnet test SpeechMessage.Dynamics.Deployment.Tests -c Release` 有完整 Release 輸出 1036/1036 通過（`C:\smv\guard-p8-evidence\p8-l1-deployment-tests-20261121-resume.log`）；較晚的 12:03 執行 exit 0，但該次數字未保存。
- [ ] R7 有兩次相同 since 的唯讀 observe 結果：09:56 為兩筆 `(unknown-exception)`，12:18 為 trace 可用、0 筆；原兩筆已無法分類為預期失敗或缺陷，結論為「目前證據不足」。
- [ ] R9 由兩筆變零的原因無法從唯讀 observe、repo 設定或既有證據確認；正式 Deployment CLI 沒有查 retention／清理作業歷史的子命令，依 08A 結案為「去向無法確認」。
- [ ] church-defaults 唯讀 plan 未重現 `0x80040224`，亦未提供目標列狀態或四欄 `IsValidForUpdate`；結論為「目前證據不足」。
- [ ] 父任務 `implement.md`、`wp-reports/P8-decisions.md`、L1 一頁回報及進度頁 DATA 一致更新。
- [ ] 單元仍未達驗收；依 D8-26 記錄必做證據缺口、維持進行中並續下一單元。L1 狀態文件提交與守門／推送完成後，只能記為缺口狀態同步，不代表驗收。

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.

# P7.1 App-named 名單目錄型別化讀取審查

## 範圍

審查 `ORG-CALL-00014`／`list.catalog.retrieve.app.named` 的 registry、Phase 0 matrix、Data8 固定
`QueryExpression`、封閉 response branch、ProductClient DTO 與 DI、測試及權威差距矩陣更新。
不包含 ChurchReport consumer、feature gate、CE request／mutation、traffic cutover、ToolUtility removal、P7.5
或 P8。

## 本機審查結論

- Critical：無。固定 query 僅接受零參數，欄位、篩選、排序與 page size 均由 connector 擁有；CRM `Entity`、
  `EntityCollection`、paging cookie、profile、credential、transport 與可變來源集合沒有穿越 wire／DTO 邊界。
- Warning：無。ProductClient 會在 executor I/O 前驗證 profile／workload，原樣轉送 cancellation token，並對
  operation ID、response kind、branch、null row 與 empty ID fail closed；沒有 cache、retry、fallback、Entity
  rehydration、timer、subscription 或 background work。
- Info：`statuscode = 0` 是 list entity 的 factory-specific 合約，不能以其他 entity 的 `statecode = 0`
  機械替換。新增與既有相鄰 query contract tests 已覆蓋此回歸風險；規則已回饋至 Gateway spec。

## 證據

- 受影響 focused tests：98 passed、0 failed。
- 完整 Dynamics Release tests：786 passed、7 個受控 live-SQL skips、0 failed。
- 完整 solution Release tests：命令 exit code 0；受控 live-environment skips 沒有被升格為 CE evidence。
- 完整 solution Release build：0 warnings、0 errors。
- rebaseline Python tests：13 passed；authoritative matrix validator：`outcome=valid`。
- 全部變更 C# 檔：UTF-8 without BOM、CRLF-only、final CRLF；`git diff --check` 通過。

## 雙模型審查降級

已透過 `Start-CcgDualModelRun.ps1` 嘗試 final reviewer，並依授權最多等待 45 秒。Gemini wrapper 結束但沒有
usable reviewer output；Claude 未在期限內完成且已終止其本次 process tree。此結果記錄為「雙模型未完成」，
不是成功的雙模型或單模型審查；本 task 依上述本機證據繼續。

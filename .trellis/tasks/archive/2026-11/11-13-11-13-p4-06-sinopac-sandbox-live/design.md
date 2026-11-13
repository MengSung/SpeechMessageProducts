# P4-06 設計

## 邊界

本包只新增 LineWebhook 的測試秘密識別、預設排除設定與實機驗收測試，不改付款產品流程或 ChurchReport。實機只連 `goodshepherdback` 測試組織及永豐沙箱；正式資料、正式商店、正式 LINE Channel 與真實付款均禁止。

## 設定與信任界線

`PaymentsLiveAcceptanceTests` 從 LineWebhook User Secrets 讀取 CRM、付款設定檔與測試教會識別。測試在任何寫入前驗證 `CrmConnection:ServerUrl` 主機包含 `goodshepherdback`，且付款 profile 的環境為 `Sandbox`；任一條件不成立即失敗，不連線、不建立資料。秘密轉移只經同一程序的標準輸入，不輸出設定值，也不修改 ChurchReport `appsettings*.json`。

## 測試資料與資源所有權

單一 fixture 擁有本次建立的 Contact、Fee、Outbound Work Item 與 Organization Service scope。每筆資料以本次正式建立回傳的 ID 綁定清理，不用顯示名稱或廣域查詢刪除。先記錄教會 A 原本的付款 profile 名稱，將其暫設為沙箱 profile；在 `finally` 依待辦、收費單、會友順序刪除本次 ID，最後還原教會設定。服務 scope 與 SDK 資源由 fixture 確定性 Dispose；取消與錯誤路徑仍執行清理，清理結果逐項核對。

## 三個驗收案例

- `Live_AllotQuery_SandboxRoundTrip` 使用正式 outbound registration 與 Worker，真的向永豐沙箱查詢；60 秒內驗簽成功並完成待辦。若查無撥款，確認四欄不變並只在本機紀錄保存實際狀態碼。
- `Live_AllotWriteBack_SyntheticBatch_MatchesLegacy` 使用合成 `IPaymentAllotGateway` 回應測試 D365 寫回及舊公式對照；回報必須明確說明回應為合成，不能當成永豐實回。
- `Live_SameFeeEnqueuedTwice_ExecutesOnce` 對同一收費單重建待辦，確認穩定待辦 ID、attempts 與收費單修改時間保持不變。

## D365 結構變更

`apply` 前保留 goodshepherdback metadata before 快照。先檢查 plan 僅有 `account.new_sinopacprofile` 的 CreateAttribute 與 IncludeAttribute，無其他差異才套用。套用後執行 `verify` 並匯出 metadata；欄位永久保留，不在測試結束時移除。

## 預設測試隔離

三個 live 測試以 `Category=PaymentsLive` 標記；`PaymentsLive.runsettings` 依既有 runsettings 慣例排除該類別，避免一般測試意外連接 D365 或永豐。只在明確指定 filter 時執行。


# 階段 5 整合設計

## 邊界

Plugin 外殼與沙箱專用基礎型別留在 `SpeechMessage.Dynamics.Plugins` 且維持 internal；
規則留在 `SpeechMessageProducts.GoodShepherd.Core`，只接受純值、Entity、
`IOrganizationService` 與通知介面，不依賴沙箱輸入型別。Workflow 活動集中在
`SpeechMessage.Dynamics.Workflows`。部署清單升級為 schema version 2，支援多組件、
多步驟、映像與活動。

## 資料與影子流程

同一份合成 `scenarios.json` 產生舊程式 `legacy.json`、人工審核的 `expected.json`，
供規則測試、外殼測試與 goodshepherdback 影子比對共用。影子模式只記錄計畫中的
寫入與推播，不解密 token、不連外、不寫入正式資料；差異只可採 05A D5-6 既定類別。
所有共享結果與記錄均為操作區域資料，具上限、取消與 Dispose 路徑，禁止跨請求或跨教會
保留可變狀態。

## 相容與隔離

防遞迴政策依各舊入口的實際行為宣告；例外不可吞掉。教會、會友、Channel、Retry Key
與待辦來源均由伺服器端關聯或固定 GUID 驗證，不信任呼叫端提供的隔離欄位。每個
通知與待辦操作使用明確 scope，完成、取消、逾時與失敗時釋放所有連線、stream、timer、
task 與 cancellation registration。

## 風險處理

舊程式若在兩小時內無法由 harness 編譯或執行，依 D5-3 改為逐檔閱讀對照並標記
`basis=reading`；活動若無法在 P5-03b 時間盒完成測試流程，依 D5-9 標記
`basis=deferred-to-P7`。舊步驟／舊流程僅能在 `switch --to legacy` 或 `shadow` 的第一輪期間啟用，且先通過 D5-18；第一輪結束（含失敗、取消）必須停用。新步驟只用 Shadow，不以 Live 啟用；舊、新步驟不得同時啟用。任何需要放寬檢查、正式組織或變更 D5-2 的情況均停止該路徑並記錄，不以替代實作掩蓋。

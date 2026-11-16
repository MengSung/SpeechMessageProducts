# P6-03 技術設計

## 邊界

ChurchReport 是現役網站。本包只把 P2-06 留下的送訊相容殼替換成產品內部通知器，不改送訊後端、token 來源、內容、診斷來源代號或呼叫端的等待與失敗行為。四個被刪除型別為 ChurchReport 的 `PushUtility`、`ReplyUtility`、`LineUtilityClass`，以及 ToolUtility 的 `PushUtility`。

## 資料與資源流程

- 一般與多則推播：呼叫端 → `ChurchReportLineNotifier` → `ILineNotificationWorkflow` → 原 `LineLegacySendWorkflow` 路徑。
- 可靠推播：呼叫端 → 通知器 → 原 `SendOrThrowAsync`；保留原 RetryKey、驗證與例外傳遞。
- 多播：通知器先以既有 `ToolUtilityClass` 寫統計，再呼叫既有 `LineLegacySendWorkflow.MultiCastAsync`；失敗先經共用 `ExceptionReporting` 回報，再保留原例外。
- `ForClient` 建立的工作流程借用呼叫端提供的 `LineMessagingClient`；通知器不 Dispose、不保存該 client，不建立 static、cache、queue 或背景工作。client 的建立與釋放責任完全留在原呼叫端。
- 每次呼叫的收件人、訊息、例外與 RetryKey 只在該次操作使用；不跨請求、使用者、教會或頻道保存狀態。

## 相容與故障處理

通知器只接收實際仍有正式呼叫者的入口。文字與多則推播維持 best-effort：結果錯誤或流程例外透過共用診斷回報一次，不往外丟。可靠推播維持參數拒絕與錯誤往外傳。多播維持空收件人零副作用及統計先於傳送。來源代號逐字保留。

P2-06 黃金案例採用明確重播／退役兩份清單，兩清單的聯集必須與原檔案例集合完全相同且互斥；退役只適用於沒有正式呼叫者、隨相容殼刪除的入口。黃金來源檔本身不修改。

## 驗證

先依 06A 暫時改名四個型別，以 ChurchReport 與指定測試專案的建置錯誤擷取呼叫位置，之後立即還原探針。新增測試依 06A 十個固定名稱先紅後綠。程式提交後在 `C:\smv\guard` 執行完整 00 §8.1；不部署、不連線 D365。

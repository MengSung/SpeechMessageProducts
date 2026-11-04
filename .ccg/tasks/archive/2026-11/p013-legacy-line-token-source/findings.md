# P0-13 舊 LINE token 來源調查

日期：2026-11-04。僅查本機舊原始碼；未連線 D365／LINE，未修改產品程式、舊專案或其他 worktree，未讀取 User Secrets、授權檔或金鑰內容。

## 可確認的來源

舊專案位於上層工作區的 `.其他解決方案`，只記錄檔名、結構與欄位名稱：

- `LineApiWebhook/Crm2011Processor/Dynamics365ProcessorClass/Dynamics365ProcessorClass.MessageStore.cs:73,81-85,231`：建立 `letter`，把 token 寫入 `new_channel_access_token`。
- `Dynamics365Line/LineMessagePlugIn/LineMessagePlugInClass.cs:49,207,233`：從 `letter` 記錄讀取 `new_channel_access_token`。
- `LineApiWebhook/LineMessagePlugIn/Core/LineMessagePlugInCore.cs:43,76-79`：讀取相同資料欄位。
- `Dynamics365Line/WeeklyReportLinePlugIn/WeeklyReportLinePlugInClass.cs:46,207,232`：從 `new_group_present_weekly_report` 讀取同名欄位。
- `Dynamics365Line/LineUtility/LineUtilityClass.cs:39-44,100-143`：存在非空預設 token 常數；傳入空字串時會走依組織選擇預設值的回退邏輯。未輸出、複製或記錄常數值。

## 邊界與結論

- 網站設定分離不會清除過去寫入 Dataverse 記錄的 token。
- 清空資料欄位不能證明舊 DLL 不會發送；已停用的舊 Plugin／流程保持停用。
- 本機原始碼不能證明目前部署版本、憑證效力或資料庫欄位值；未宣稱資料已清理。
- 使用者 2026-11-04 追加核定：「不需要清理資料中的舊 LINE token 與會友 LINE ID」。本次 P0-13 trace 免除這兩項清理前置，記錄為核定保留資料，停止資料清理引導；未變更後續階段規則。
- 已將來源與使用者回報的離線建置結果補入 P0-13 驗收文件，同步 00／01 的本次例外；下一步由使用者開啟 goodshepherdback「設定 → 系統管理」，續行 Plugin 追蹤設定與最小驗收。
- P0-13 與階段 0 均未完成；主 Trellis 任務維持原狀，不建立新 Trellis 任務。

# P0-ENV　Development LINE 設定隔離阻擋

## 已核對的本機事實
- 工作分支為 1.0.0.11.Integration.P0；最近完成提交 ba565a55（P0-05）。
- appsettings.Development.json 目前只有 DiagnosticsTrace／Dataverse，沒有 CrmConnection 或 LineMessaging 覆寫。
- appsettings.json 的 CRM 組織目前是 sunnyvalechback；LINE 的預設別名是 Jesus，Jesus 與 JesusBack 的 token 欄位非空。只記錄鍵名、別名與是否存在，沒有複製任何值。
- LineUtilityClass 自建 static IConfigurationBuilder：SetBasePath(Directory.GetCurrentDirectory()) → AddJsonFile("appsettings.json", optional: true, reloadOnChange: false) → Build()。
- 它的 GetChannelAccessToken 與相容建構式從這份靜態設定讀取 token，缺少組織設定時退回 DefaultOrganization，再退回 Jesus；不使用 Host 注入的 Development 設定或 User Secrets。
- 目前有效程式碼有 16 處 `?? "Jesus"`，分布於 14 個檔案，位置保存在 local-findings.json；這是對原提示「9 處」的實際更新。

## 為何只改 Development 設定不足
即使 Host 合併後的組織改成 goodshepherdback，這份獨立的靜態設定仍然只讀基底 appsettings.json。Development 中的 DefaultOrganization 或空 token 覆寫無法改變該路徑的選擇。
因此不能只憑合併設定測試通過，就宣稱 Development 不會取用基底 token。直接移除共用基底 token 也會影響既有正式執行路徑，不能當成本包可採用的繞過方式。

## 停止與後續
依使用者已核定 P0-ENV(c)：「若必須改產品程式才能做到，停下來回報。這屬於 P2-01 啟動檢查，階段 0 不改產品程式。」
本次已停止；沒有啟動網站、沒有連線任何 D365／LINE、沒有讀取 User Secrets 或實際 token、沒有修改產品程式或設定，也沒有進入 P0-06。
需由使用者決定 P2-01 安全啟動／token 來源防護如何安排；在取得新指示前維持停止。goodshepherdback 的清理前置條件及禁止連線仍有效。

## 使用者後續補核與核定
後續由使用者交付的 Claude 單模型人工核對，補列 LineMessagingProcessorClass 的一處退回邏輯，總計更新為 17 處／15 檔；初始 local-findings.json 的 16 處／14 檔保留為當時調查快照，不改寫原證據。使用者已核定獨立 P0-SEC 先處理 11＋1 個讀取點、UserSecretsId 與 Development 啟動防護，原安全範圍待決已解決；目前新停止原因是環境優先順序矛盾，詳見 P0-SEC 回報。

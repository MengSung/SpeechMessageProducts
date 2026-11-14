# P5-03b 畫面元件與活動測試流程

## 範圍與授權
依05A P5-03b完成離線工具；P5-03a實機追蹤待釐清時，只做不依賴實機的部分，最多兩包並行。沒有使用者介面元件待搬，本包不在goodshepherdback匯出或匯入。活動第一次實機留到P5-05，失敗兩次依D5-9。不得為本包啟用新Live或同時啟用舊新。

## 唯一驗收清單
- [ ] ui-export匯出未受管理SpeechMessageUi，解開至d365/solutions/SpeechMessageUi；共用實體表單不打包。
- [ ] ui-import重壓縮、匯入並發布，只允許goodshepherdback，離線假服務驗證。
- [ ] `UiExport_SharedEntityForm_NotPackaged`
- [ ] `UiImport_ZipEntryOutsideRoot_Rejected`
- [ ] `UiImport_OversizedArchive_Rejected`
- [ ] `UiImport_NonWhitelistedOrganization_Rejected`
- [ ] 活動新輪從實際舊流程定義只替換核定活動參照、補ExecutionMode=Shadow，建立SMV-TEST-手動流程，執行讀追蹤後停用刪除；第一輪仍受D5-18與finally停用限制。
- [ ] `ActivityProbe_RewritesOnlyActivityReference`
- [ ] `ActivityProbe_FailureMidRun_RemovesTestProcess`
- [ ] 同名測試與Deployment.Tests全綠，固定guard完整通過、一頁回報、進度頁DATA更新。P5-03a未驗收前不宣稱P5-03完成或推送。

## 時間盒
半天。2026-11-15 03:47 Asia/Taipei (UTC+08:00)：規劃已備妥，實作尚未開始；半天實作時間盒自啟動實作時計算，不把規劃時間冒充完成時間。

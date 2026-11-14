# P5-03b 畫面元件與活動測試流程

## 範圍與授權
依05A P5-03b完成離線工具；P5-03a實機追蹤待釐清時，只做不依賴實機的部分，最多兩包並行。沒有使用者介面元件待搬，本包不在goodshepherdback匯出或匯入。活動第一次實機留到P5-05，失敗兩次依D5-9。不得為本包啟用新Live或同時啟用舊新。

## 唯一驗收清單
- [x] ui-export離線匯出未受管理SpeechMessageUi，解開至d365/solutions/SpeechMessageUi；共用實體表單不打包。
- [x] ui-import重壓縮、匯入並發布，只允許goodshepherdback，離線假服務驗證。
- [x] `UiExport_SharedEntityForm_NotPackaged`
- [x] `UiImport_ZipEntryOutsideRoot_Rejected`
- [x] `UiImport_OversizedArchive_Rejected`
- [x] `UiImport_NonWhitelistedOrganization_Rejected`
- [x] 活動新輪的離線定義改寫與清理 owner 已完成；實際舊流程讀取、追蹤及實機執行保留至P5-05。
- [x] `ActivityProbe_RewritesOnlyActivityReference`
- [x] `ActivityProbe_FailureMidRun_RemovesTestProcess`
- [x] 同名測試與Deployment.Tests全綠，固定guard完整通過；一頁回報已建立，進度頁保留P5-03a未驗收邊界，不宣稱P5-03完成或推送。

## 時間盒
半天。2026-11-15 03:47 Asia/Taipei (UTC+08:00)：規劃已備妥，實作尚未開始；半天實作時間盒自啟動實作時計算，不把規劃時間冒充完成時間。

## 2026-11-15 04:16 Asia/Taipei (UTC+08:00) 離線完成

P5-03b 的離線驗收清單已完成；16／16 UI、2／2 activity、Deployment 766／766及固定 guard 八項全通過。P5-03a 尚未驗收，因此 P5-03 整包仍 active。

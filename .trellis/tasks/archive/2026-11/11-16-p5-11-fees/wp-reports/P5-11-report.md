# P5-11 一頁回報

2026-11-16 13:37 Asia/Taipei (UTC+08:00)：完成四個退役入口的閱讀盤點、每日撥款待辦與繳費通知外殼；不執行舊 DLL。離線雙目標、真 WF／CAS、兩 DLL 打包及停用部署均有證據，乾淨固定 guard 八項全數通過。封存提交後僅跑紀錄型輕量 guard，通過才推送並核對遠端，接續 P5-12；不需使用者操作。

| 需求 | 證據 | 狀態 |
| --- | --- | --- |
| fee＋教會當地查詢日的穩定 sourceId、精確兩欄 payload、未來付款日拒絕 | Fees 七項具名測試及固定 GUID 向量；Core 完整 net10／net48 各 348 個測試，Payments 101 個測試 | 通過；唯一鍵實機效果不由離線測試推論 |
| 六案 reading 與真外殼、收件者隔離、穩定 Retry Key | Core／真 WF 共用 scenarios／expected；Plugins 完整 594 個測試含 CAS；收尾黃金／Fees 雙目標各 57 個測試 | 通過；未產生 legacy.json、未經舊程式實跑 |
| 正式 net462 兩 DLL 與 goodshepherdback 全停用 | p5-11-final-package-check.log：兩 DLL ok=true；plan 20261116-051134；apply 成功；p5-11-closure-verify.log=0；legacy-20261116-051321 的舊啟用步驟／流程皆 0 | 通過；20 個新步驟停用；FileVersion=1.0.0.16 |
| 乾淨固定 guard 八項 | guard-952a7c43-prepare.log、guard-952a7c43-full.log；Release 全方案 7,043 個測試、Debug tracer 7 個測試；編碼、重複碼、I18n、文件與進度頁全通過 | 通過；無新專案，覆蓋率門檻不適用 |

基準 reading／simulation；live=deferred、differences=null，不宣稱實機影子差異零。P5-PROP-006 已核定缺口：通知活動 Live 查詢前拒絕；P5-11 僅處理既有 new_fee，不補課程建費單／付款 fallback；Host 架構須 P7 前完成，併階段審查。黃金檔曾違反根欄位／case／JSONPath／來源證據契約，已修正且未放寬驗證器。全包含收尾預計 9 筆提交，未改寫歷史；外部 .ccg/tasks/p5-03b-ui-activity-probe/.turns.json 保留、不提交。

本機證據 SHA-256：guard-952a7c43-full.log `BC0F6A6568EAB258305401FA9D83D7E4783EE0ABF2BF1758A2604530E2E616B2`；原始輸出留本機不提交。遠端 SHA 以收尾 push 後 git ls-remote 的對話回報為準，不預先宣稱推送成功。

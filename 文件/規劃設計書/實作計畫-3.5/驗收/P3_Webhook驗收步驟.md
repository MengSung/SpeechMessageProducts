# P3 Webhook 驗收步驟

> 狀態：LINE 平台連線與簽章模擬已完成；VM 日誌／A.8 清理及 P3-06 三項實機 Warning 證據待收尾。不得標示 P3-09 done。更新時間：2026-11-12 01:13 Asia/Taipei (UTC+08:00)。

## 固定邊界

- 只使用 goodshepherdback、測試 VM D365-9.1（D365APP01）及測試 LINE Channel `2006058491`（@556vpxof）。舊 Channel `1646814154` 禁止使用。
- 沿用使用者已建立的測試教會A；不建立或修改測試教會A/B/C 的既有欄位。Channel 指派保留於測試教會A。完全不碰聖谷行道會、CRM 既有網站、既有 HTTPS binding、`CRMAppPool`、憑證及 CRM 既有資料。
- 獨立使用 `LineWebhookP309` site／AppPool、專用測試資料庫與 A.8 schema。既有資料庫、空表、AppPool、部署目錄沿用，不重建。
- `metadata.json` 是本機唯讀快照，不搬移或提交；需要引用時只記 SHA-256。
- Channel secret、access token、TokenKey、CRM 密碼、reply token、TestLineUserId 及 DPAPI credential 內容不得進 repo、evidence、命令列、log 或回報。秘密只從 User Secrets／核准 credential store 在程序記憶體使用。

## 已有證據

- 測試教會A 的正式 GUID、OrganizationId 與測試資料 GUID 留存在既有 CRM audit evidence；本清單不複製敏感 userId。
- 測試 Contact、Channel `2006058491` 的 `protect-token`／`assign-channel` 已完成，不重做。mapping 固定為 Organization `goodshepherdback`、ChurchId 測試教會A、OrganizationId 以 WhoAmI 取得、TimeZone `Asia/Taipei`。
- 新 Channel 憑證重發後，`protect-token` 以 stdin 執行成功，LINE Bot Info 回 HTTP 200。離線 Webhook 測試本輪 104/104 通過；這不代表主機實機事件已通過。
- 部署腳本現版 SHA-256 `BC420A9943C73559C230F7D0EC3153D16EF44D08F035014C130E1AE3B9BA9F30`，PowerShell Parser 0 錯誤；最近一次 `-Apply` exit code 1，錯誤細節未留存，不能宣稱部署完成。
- 最近外部 HTTPS 探測：goodshepherdback 回 302 且 TLS 驗證成功；Webhook URL 的 GET 與空事件 POST 也回 302 且 TLS 驗證成功，尚未證明請求由新 site 處理。部署後 VM 狀態待唯讀檢查。

## 驗收清單

1. **部署與共存**：依 2026-11-11 19:35 核定，VM 由專用 HTTPS `*:8443` 接收；本機 Node 轉接只將 `linewebhook-test.speechmessage.com.tw` 導至 `192.168.50.20:8443` 並保留 Host。不得新增 443 SNI binding、改 CRM 或還原 IIS 備份 `P3-09-pre-202611101130`（該備份會移除 ANCM v2 註冊）。使用者 21:50 確認部署成功、公開無簽章 POST 回 401、goodshepherdback 回 302 且憑證指紋不變。
2. **LINE 平台連線**：以程序內 token 先讀回並記錄原網址 `https://jesus.speechmessage.com.tw:808/api/callback`，再呼叫 Messaging API `PUT /v2/bot/channel/webhook/endpoint` 設定 `https://linewebhook-test.speechmessage.com.tw/api/line/webhook/2006058491`，最後呼叫 `POST /v2/bot/channel/webhook/test`。本輪更新成功且官方測試回 `success=true`（2026-11-11 22:12:07Z）。空事件成功只證明平台連線，不代表業務事件驗收。
3. **中文關鍵字**：使用 Channel secret 在程序記憶體計算 HMAC-SHA256，依 P3-01 黃金路由送出 `主日出席`、`代禱`、`報名`、`我的報名`、`第一課簽到`、`簽到`、`簽核`、`裝備`、`靈修`、`點名`。本輪十筆模擬請求均回 HTTP 200；業務 disposition、letter 與 reply 結果仍以 VM 日誌及 CRM 記錄核對，不以 HTTP 200 單獨宣稱業務完成。
4. **偽語系**：對 `qps-ploc` 測試 Contact（其專用合成 LINE ID）送「主日出席」與「Report」，兩筆有效簽章請求均回 HTTP 200。第一輪誤用中文 Contact ID 的兩筆請求不作偽語系通過證據。偽語系回覆括號格式須由安全回覆日誌核實。
5. **重送與驗簽**：同一 `webhookEventId` 以 `isRedelivery=true` 首次及重送各送一次，兩次 HTTP 均回 200；一筆 A.8 row 及重送旗標仍待 VM 查詢確認。錯誤簽章已回 HTTP 401；其未入 A.8 row 的證據待 VM 查詢確認。
6. **回覆送出紀錄**：測試站已開啟 `LineWebhook:ReplyLog:Enabled`。收尾工具預定讀取固定 EventId `30610`，僅輸出事件 ID、訊息類型、文字存在與否、語系、API 狀態碼及未遮罩 userId 布林檢查；不保留原始 log line。工具兩次均回 `RemoteException`，安全化回覆欄位尚未取得；不得宣稱遮罩與回覆欄位已核實。
7. **P3-06 Warning**：逐項留下決定性實機證據：(a) 每事件 30 秒期限及逾時後資源回到基線；(b) appointment 教會範圍欄位及查詢 scope；(c) annotation 同一穩定事件重送 Upsert 不產生重複紀錄。現有程式與離線測試只證明實作契約；本輪三項實機 Warning 均尚未取得決定性證據，必須明標「未確認」。
8. **清理**：既有 P309 TestDataTool 已清除本次兩個及兩個歷史標記 Contact，確認目前與歷史標記 Contact 均為 0；測試教會A 與 Channel 指派保留。VM 收尾尚未生效；唯讀核對為本批 A.8 16 列（redelivery 一列、錯誤簽章零列）、stdout 開啟且有 1 個檔案。專用空 schema、site、AppPool、部署目錄保留。

模擬事件使用無效 replyToken 時，LINE 拒絕回覆屬預期；判定依據是正確收件、驗簽、分派、去重、嘗試 Reply 並留下安全紀錄，不宣稱真人已收到訊息。真人手機補測可省略並另行註明。

## 本輪結果與待收尾

- 2026-11-11 21:50 使用者執行 8443 部署腳本成功；沒有修改 CRM `*:443`、CRM site／AppPool 或憑證，也不再部署、不還原 IIS 備份。
- LINE 原 Webhook URL 已記錄，設定新 endpoint 成功，官方 Webhook 測試 `success=true`。
- HMAC 共送 18 個 HTTP 呼叫：10 個中文關鍵字、誤用中文 Contact ID 的偽語系初次嘗試兩個、正確 `qps-ploc` 中文／英文／「我的報名」三個、同一 redelivery event 兩次、錯誤簽章一筆；17 個有效簽章回 200，錯簽回 401。所有事件均為模擬，非真人手機。偽語系結論只依正確 Contact 的三個案例。
- HMAC 事件前綴為 `P309-96fd5451e33d-`。VM 收尾腳本 `C:\Users\Administrator\.speechmessage\P3-09-finalize-vm.ps1` 已核對 Parser 通過，第一次因 `<location>` XPath 不符而在變更前失敗；只修正 XPath 後第二次仍回 `RemoteException`。唯讀核對顯示未部分清理：stdout 開啟、1 個 stdout 檔、A.8 16 列（redelivery 1、錯誤簽章 0）、AppPool Started。腳本現 SHA-256 `88A86C0D93E91F69577258B6919F39F14213038B8502A79F2A9376AD27A64322`；不得再部署或還原 IIS 備份。
- P3-06 三項 runtime Warning 均尚未確認；本輪不將離線測試或程式碼檢閱冒稱為實機證據。真人手機補測省略。

## 收尾門檻

以上 1～8 都要有對應證據，測試資料殘留為 0，且三項 Warning 均有結論，才可標示 P3-09 done。階段審查採使用者核定的自審例外，明寫「使用者核定略過外部雙模型審查，僅自審，非雙模型結果」，Critical／High 為 0。完成後才跑固定 guard §8.1、推送 P3 並用 `git ls-remote` 核對 SHA、更新進度頁與交階段 3 總回報；不得開始階段 4。

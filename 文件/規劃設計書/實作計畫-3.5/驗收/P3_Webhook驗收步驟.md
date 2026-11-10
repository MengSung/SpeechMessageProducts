# P3 Webhook 驗收步驟

> 狀態：待使用者收尾。本文是續作清單，不是實機通過證據。2026-11-10 21:58 Asia/Taipei (UTC+08:00) 已再次以 PowerShell Parser 核對部署基礎腳本，錯誤 0；仍未建立 CRM 測試資料、未新增 IIS site／SNI binding、未設定 LINE Webhook URL、未送出事件，也未改 CRM／IIS／SQL／LINE。

## 固定邊界

- 只使用 `goodshepherdback`、`D365-9.1`（`D365APP01`）及測試 Channel `1646814154`。
- 不修改 CRM site、既有 `*:443:` binding、`CRMAppPool`、既有憑證或 CRM 資料庫。
- `metadata.json` 是本機唯讀快照，不搬移、不提交；回報只引用既有 SHA-256。
- Channel secret、token、TokenKey、reply token、TestLineUserId 不輸出、不進命令列、log、evidence 或回報。
- Mapping 來源已由使用者核定：OrganizationId 以唯讀 WhoAmI 取得、ChurchId 使用測試教會 A 的正式 GUID、TimeZone 為 `Asia/Taipei`、組織為 `goodshepherdback`。目前只缺 CRM UI 建立測試教會 A／B 與各自 Contact；不以猜測、SQL 或自訂 CRM probe 代替。
- Hosting Bundle／ANCM、SQL 測試 DB/A.8 空表、專用 AppPool 及發布目錄已有前一輪證據；實際建立狀態沿用，不宣稱本輪重查。

## 依序執行

1. **建立 CRM 測試資料。** 只在 `goodshepherdback` 建立帶唯一 P3-09 標記的測試教會 A、B 及各自測試 Contact。A 的 Contact 綁定 User Secrets 內的 `TestLineUserId`，語系為 `zh-TW`；B 的 Contact 設為 `qps-ploc`。保存 Account／Contact 正式 GUID，不保存 userId。以既有唯讀 `WhoAmI` 取得 OrganizationId；時區固定 `Asia/Taipei`。不得用 SQL、名稱推測或自訂 CRM probe 建立／查詢資料。
2. **指派測試 Channel。** 沿用專案 `SpeechMessage.Dynamics.Deployment` 的 `protect-token` 與 `assign-channel`，並用既有 `LineRecipientQueries` 驗證正式 lookup。token、Channel secret 從 User Secrets 於記憶體／有限 stdin 傳遞；不要把秘密放命令列。Channel 指派給測試教會 A。
3. **部署獨立 IIS 站台。** 執行前先用 PowerShell Parser 驗證 `.trellis/tasks/11-10-p3-09-webhook-live-acceptance/evidence/deploy-p309-infrastructure.ps1`，再執行其可重複部署流程；已存在的 DB／A.8 表、AppPool、部署目錄須沿用並核對。該腳本只建置基礎資源，不建立 site、binding 或 mapping。其後使用獨立 `LineWebhookP309` site／AppPool，依核定 mapping 將設定放在專用 AppPool 環境變數，不落地到 repo。
4. **新增 SNI 並立即驗證。** 只新增 hostname `linewebhook-test.speechmessage.com.tw`、SNI、既有萬用憑證。立刻確認 `goodshepherdback` 維持 HTTP 302 且憑證指紋不變、CRM site 與 `CRMAppPool` 為 Started、測試 hostname 由新 site 回應。任一項不符，立即移除新 binding；CRM 仍異常才依既有備份 `P3-09-pre-202611101130` 還原並重新檢查。不得改寫 CRM 原 binding 或更換憑證。
5. **測試 LINE 平台連線。** 以 User Secrets 測試 token 在程序記憶體中呼叫 Messaging API `PUT /v2/bot/channel/webhook/endpoint`，設定 `https://linewebhook-test.speechmessage.com.tw/api/line/webhook/1646814154`；接著呼叫官方 Webhook 測試 API `POST /v2/bot/channel/webhook/test`，記錄不含秘密的成功狀態。官方連線測試送空事件陣列，不能當作事件處理驗收。
6. **送出簽章模擬事件。** 從 User Secrets 在記憶體取 Channel secret，以 HMAC-SHA256 計算 `X-Line-Signature`，不要輸出簽章或秘密。中文會友依 P3-01 黃金資料挑 10 個常用關鍵字；偽語系會友送「主日出席」及 `Report`，回覆文字應有括號。確認 A.8、回覆事件紀錄與去重結果。模擬 replyToken 無效時 LINE 拒絕回覆是預期；判定為正確收件、驗簽、分派、去重、嘗試 Reply 並記錄 API 狀態，不冒稱真人訊息已送達。
7. **重送與錯誤簽章。** 同一 `webhookEventId` 以 `isRedelivery=true` 再送兩次，只能處理一次；資料庫同一 Channel/event 僅一列，回覆處理不重複。錯誤簽章應回 401，且不得入列或執行業務處理。
8. **核實回覆紀錄。** 僅測試站開啟 `LineWebhook:ReplyLog:Enabled`。固定 EventId 為 `30610`；核對 event ID、訊息類型、回覆文字、語系及 LINE API HTTP 狀態碼。確認 `TestLineUserId` 已遮罩；Channel token、reply token、Channel secret 與完整 userId 均不得出現。重啟設定快照前，不以環境變數變更宣稱生效。
9. **逐項核實 P3-06 三項 Warning。** (a) 每事件 30 秒期限：以可觀察的逾時事件確認取消與資源回到基線；(b) appointment 教會範圍欄位：以實際 CRM metadata／查詢結果確認欄位與 scope；(c) annotation 重送 Upsert：以相同穩定事件識別驗證不產生重複紀錄。沒有決定性實機證據就標「未確認」，不以離線測試代替。
10. **清理並驗證。** 先按核准流程備份，再依正式 GUID 清除 A／B 教會、Contacts 及 Channel 指派；刪除專用測試 DB 中本次 A.8 測試列，驗證列數為 0。測試 site／AppPool／空 A.8 schema／部署目錄留給 P4-05 共用，不列為測試資料殘留。只清本次建立的 TEMP 發布檔。若清理工具被拒，記下精確目標，不繞過；最終清理腳本須有前置條件檢查。

## 驗收界線

本輪尚無任何上述 live 步驟的通過結果。LINE 官方連線測試與 HMAC 模擬事件是不同證據；兩者都不能冒稱真人手機補測。真人手機操作不是 04 完成條件，可選做並另列補測。所有步驟完成、測試資料清零、P3-06 Warning 均有結論前，P3-09 與階段 3 不得標示完成。

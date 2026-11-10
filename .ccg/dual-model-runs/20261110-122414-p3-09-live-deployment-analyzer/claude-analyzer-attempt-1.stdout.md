## 分析:P3-09 安裝後續作(LineWebhook 實機部署)

**依據與限制:** 我讀了 P3-09 的 PRD、設計、執行受阻回報,04 的 P3-09 第 1~7 步,00 §11,以及 `Program.cs`、`appsettings.json`、`WebhookServiceRegistration.cs`、`WebhookDeduplicationRegistration.cs`。我沒有連線 VM,也沒有讀取任何秘密。P3-06 三項 Warning 的原始審查文字我沒有找到,下面對它們的做法是依你給的描述推導,不是引用原文。

### 現況
- Hosting Bundle 已由使用者安裝,Codex 另以 PowerShell Direct 獨立複核 ANCM v2、.NET 10.0.14、CRM site 與 CRMAppPool、goodshepherdback 302 以及兩份安裝檔已刪除。
- IIS 備份 `P3-09-pre-202611101130` 與操作前 `applicationHost.config` 雜湊已有紀錄。
- 但那份雜湊與備份是在安裝前建立的,Hosting Bundle 安裝已經改過 IIS 組態(新增 ANCM 模組)。安裝後的基線必須重新取得。
- 目前還沒有獨立 site、app pool、平台測試 DB、Webhook URL 設定,也沒有 P3-09 第 1~7 步的任何實機結果。
- LineWebhook 是 net10 的 Web SDK 專案。`appsettings.json` 只有 `EnableLegacyCallback:false`。
- 啟動時必須有 `ConnectionStrings:Platform`,以及 `GoodShepherd` ToolUtility 設定和測試 Channel 驗簽設定。缺任何一項主機都拒絕啟動,這個設計是正確的。

### 風險分級

**Critical**
1. **新 SNI binding 與既有 `*:443:` 的共存沒有被證明。**
   - 若新 binding 用 `*:443:linewebhook-test...`,HTTP.sys 對具名 hostname 優先比對,對不到才落到 `*:443:` 的 CRM binding。這個預期行為還沒在實機驗過。
   - 常見陷阱是新增時誤帶「Require SNI」旗標,或把憑證綁到 IP:port 層。這可能動到 `netsh http show sslcert` 中既有的 `0.0.0.0:443` 條目。
   - 必須先留存 `netsh http show sslcert` 與 `show urlacl` 的前後差異。
   - 新增後必須同時驗證:(a) 測試 hostname 回 Webhook;(b) `goodshepherdback` 仍回 302 且憑證指紋不變;(c) 以 IP 或不帶 SNI 連線仍落到 CRM。
   - 設計書已規定:證明不了就停止 IIS 寫入。請維持這個規則。
2. **平台 DB 連線字串若指到 CRM 實例上的 CRM 資料庫,等於汙染 CRM。**
   - 同一個 `MSSQLSERVER` 承載 CRM 的 `*_MSCRM` 與 `MSCRM_CONFIG`。
   - 只能新建專用名稱的 DB(例如 `SpeechMessagePlatform_P3Test`),表由 `001_LineWebhookEvent.sql` 建立。
   - 建庫前後都要列出 `sys.databases` 並比對。
   - 應用程式不要用 sysadmin 身分連線。建議用 app pool 虛擬帳號 `IIS APPPOOL\<新池>` 建立 login,只授予該庫的 db_datareader、db_datawriter,必要時加 EXECUTE。

**High**
3. **共用 app pool 或共用目錄。**
   - 新 site 必須用新的 app pool(No Managed Code、獨立身分),實體路徑在獨立目錄。
   - 不得掛在 CRM site 下當子應用程式,CRM 的 web.config 繼承會干擾。
4. **CRM 影響的觀察點。**
   - 新增 binding 和 site 都會觸發 IIS 動態重載,但不應重啟 CRMAppPool。操作前後記錄 CRMAppPool 的 start time 或 PID,證明沒有被回收。
   - 不使用 `iisreset`。
5. **Hosting Bundle 安裝後的基線。**
   - 重新取 `applicationHost.config` 雜湊,與備份中的版本比對。
   - 預期差異只有 ANCM v2 模組與全域模組新增,其他差異都要解釋。
   - 以這份新基線作為之後「CRM 設定未變」的比較對象,不要拿安裝前的雜湊比。
   - 還原備份會連 ANCM 一起回退,不能當成輕鬆的回復方式。
6. **LINE API 範圍。**
   - 第 2 步的 `PUT /v2/bot/channel/webhook/endpoint` 會改變測試 Channel 的設定。
   - 先用 GET 讀取並記錄原值,再改。
   - 確認 Channel ID 是測試 Channel(`1646814154`),只用 User Secrets 的測試 secret 與 token。
   - 驗收結束後 URL 保持指向測試主機,這是第 7 步的完成定義。
   - 在 URL 確認為 HTTPS 且 Webhook 可達之前,不要送測試 API。
7. **秘密落地。**
   - 以 `ASPNETCORE_` 環境變數放進 app pool 或 web.config 會落到 `applicationHost.config` 或磁碟明文。
   - 建議:
     - 用 appsettings 之外、ACL 僅限 app pool 身分的檔案,或沿用專案既有的 `protect-token`/DPAPI 機制。
     - 由 VM 內程序在記憶體中產生。
   - 部署檔案清單要掃描,確認沒有 `appsettings.Development.json`、User Secrets 副本,也沒有連線字串明文留在 `Logs`。
   - 回報與證據只寫布林或長度。
8. **模擬事件的處理範圍。**
   - 第 3 步會讓主機實際呼叫 `api.line.me` reply,reply token 無效是預期的,LINE 回 400 要記為「預期錯誤」。
   - 但會友出席寫入若指向 goodshepherdback,是在寫真實的 Dynamics 資料,需要:
     - 事先備份,
     - 事件的 LINE userId 對應到測試聯絡人,
     - 清理腳本。
   - 先確認這條路徑的組織與資料標記後再送事件。

**Warning**(P3-06 三項,需要實機決定性證據)
9. **30 秒事件 timeout。**
   - 在實機製造一個會超過 30 秒的下游(例如對測試聯絡人的 Dynamics 呼叫被人為延遲,或暫時讓去重 SQL 鎖住)。
   - 取得三項證據:
     - 該事件的 `LineWebhookEvent` 列狀態(是否留在處理中、是否被視為失敗),
     - 診斷日誌或例外通知,
     - 同一 `webhookEventId` 重送時的行為。
   - 以及 HTTP 回應時間:Webhook 應早已回 200,timeout 只影響背景處理。
   - 若不能安全製造延遲,就記為「未確認」,不得虛稱通過。
10. **appointment schema 額外教會欄位。**
    - 唯讀查 goodshepherdback 的 appointment 實體 metadata(只用 `export-metadata` 或 `verify`),比對程式寫入的欄位名與型別是否存在、必要欄位是否齊全。
    - 再用第 3 步的出席事件,實際建立一筆 appointment 看是否成功,並讀回欄位值。
    - 失敗訊息應記錄在案,不要刪欄位或改 schema。
11. **annotation retry Upsert。**
    - 對同一個含附件的事件重送兩次(`isRedelivery=true`),查 annotation 與 letter 的筆數。
    - 要看到恰好 1 份,或可解釋的重複。
    - 只送圖片或音訊型事件,下載需要 LINE content API,模擬事件的 messageId 在 LINE 端不存在,下載會失敗。
    - 這項在模擬環境下可能無法取得決定性證據,應明列「未確認」,而不是以離線測試取代。

**Info**
12. 公網 DNS 與 TLS 已驗過;測試 hostname 目前回 302,這是尚未部署的證據,不是缺陷。
13. Gemini 後端無輸出,所有分析都只是單模型,不得記為雙模型完成。
14. 清理後 DB 可保留空結構,但回報要寫明「資料庫仍存在、列數 0」。
15. 回復清單依序為:
    - 移除新 site、binding、app pool,
    - 還原目錄,
    - 刪除測試 DB,
    - 移除 SQL login,
    - 以 `netsh` 比對 sslcert 與 urlacl,
    - 重新確認 CRM 健康。

### 不應執行或必須先確認的步驟
- 不要用 `appcmd restore backup` 作為常規回復,它會連 ANCM 模組一起回退。
- 不要執行 `iisreset`、`netsh http delete sslcert`、`add sslcert` 去碰既有 `0.0.0.0:443`。
- 不要修改 CRM site 的 web.config、CRM binding、CRMAppPool 或憑證。
- 不要把連線字串指向任何 CRM 資料庫。
- 不要用 sysadmin 或 sa 作為應用程式連線身分。
- 不要用正式 Channel 或正式 token。
- 不要送真實使用者的 LINE userId 事件。
- 不要把離線測試結果折算成實機結果。
- 以下必須先確認再動手:新 binding 是否帶 SNI 旗標、`netsh sslcert` 前後差異、安裝後的新基線雜湊、LINE 端原 Webhook URL 的 GET 讀取值。

### 建議順序
1. 取得安裝後基線:IIS 設定雜湊、`netsh sslcert`/`urlacl`、CRMAppPool PID、goodshepherdback 回應、`sys.databases`。
2. 建立測試 DB 與表,再建立限權 login。
3. 建立新的 app pool 與 site(先只綁 HTTP 的 localhost 或內部埠自測 `/health`),再加 SNI HTTPS binding。
4. 立刻重做第 1 步的比對,證明 CRM 不受影響。
5. 把秘密放進受限來源,啟動站台,驗證能正常啟動。
6. 依 04 第 2~6 步送出,再逐項處理三項 Warning。
7. 清理,再次比對基線。回報只寫「可確認」與「未確認」,不寫推測的通過。

---
SESSION_ID: c312ec9d-aefc-4ce7-9e47-a83c99f56598

## 分析：P3-09 Hosting Bundle 安裝後續作

**限制聲明**：本次只讀了 repo 內的 PRD、設計、受阻回報、04 P3-09 與 00 §11，以及 `SpeechMessageProducts.LineWebhook` 與 `WebhookServiceRegistration` 的原始碼，沒有連 VM。Hosting Bundle、ANCM v2、CRM 與 302 的狀態，都沿用你的紀錄與 Codex 的唯讀複核，我沒有獨立重驗。Gemini 先前 403/503，所以這份是單模型分析。

### 目前狀態
- 前置已完成：.NET 10.0.14 Hosting Bundle 與 ANCM v2 已裝，CRM site 與 `CRMAppPool` Started，兩份暫存安裝檔已清除。
- 尚未做（對應 `implement.md` 第 6～10 項）：平台測試 DB、獨立 site 與 app pool、部署、LINE 端點設定、04 的第 1～7 步、三項 Warning、清理、審查與推送。
- `appsettings.json` 只有 `LineWebhook:EnableLegacyCallback=false`。
- 程式還需要這些設定才起得來，否則刻意啟動失敗：
  - `ConnectionStrings:Platform`
  - `LineWebhook:Channels:<channelId>` 對應
  - ToolUtility 的 `GoodShepherd` 區段
  - Localization 設定
  - 簽章、去重、佇列三項依賴

### Critical
1. **IIS 備份 `P3-09-pre-202611101130` 不能當主要回復點。**
   - 它是在 Hosting Bundle 安裝前做的。整份還原會移除安裝器寫進 `applicationHost.config` 的 ANCM v2 註冊，但 runtime 仍在磁碟上。
   - 還原也會讓 W3SVC 重載全部 app pool，包含 CRMAppPool，並覆蓋期間任何人對 IIS 做的其他變更。
   - 建議：建站前先建新備份（例如 `P3-09-post-bundle`），並把「`appcmd delete site` / `delete apppool` 只刪自己建的項目」列為主要回復。整份 `restore backup` 只當最後手段，且要先經你確認。
   - `P3-09-執行受阻回報.md` 目前寫的回復步驟要改。
2. **SNI binding 與 `*:443:` 並存必須先證明，不能假設。**
   - 做法：新站只加 `https *:443:linewebhook-test.speechmessage.com.tw`，`sslFlags=1`（要求 SNI）。
   - 憑證用 `netsh http add sslcert hostnameport=…`（或 IIS 的 `AddSslCertificate`）掛上。CRM 的 HTTP.sys 項目（`0.0.0.0:443` 之類）不能動。
   - 前後都要記錄 `netsh http show sslcert`、`appcmd list site/apppool`、CRM 的 bindings XML 與憑證指紋，並逐字比對。
   - 前後對 `https://goodshepherdback…` 與 `https://linewebhook-test…` 各做一次 TLS 加 Host 探測，確認前者仍是同樣的 302 與 Location。
   - 任何不一致，立刻只刪新 binding，不碰 CRM。
3. **整份 `applicationHost.config` 的雜湊不能再當「CRM 未變」證據。** 安裝 bundle 與新增 site 都會改它。
   - 要比對的是 CRM 的 site、binding、`CRMAppPool` 與 HTTP.sys 憑證項目的逐段內容。
   - 另外要把「安裝器造成的差異」和「我們造成的差異」分開。分法是在建站前先取一份 post-bundle 快照。

### High
4. **秘密怎麼進 IIS 站台，目前沒有已核定的方案。**
   - User Secrets 只在 Development 環境載入，IIS 站台是 Production，所以讀不到。
   - 直接用環境變數會明文寫進 `applicationHost.config`，並被後續任何備份帶走。
   - 做法（任選一種後，須先確認再做）：
     - 把只有 app pool 身分可讀的 `appsettings.Production.json` 放在站台目錄，或放網站根目錄之外。
     - 內容在 VM 上由記憶體中的秘密直接產生，不經命令列，也不複製本機檔案。
     - 內容 ACL 只給 app pool 身分，清理時刪除。
   - 之後每次 `appcmd` 備份都要確認沒帶到秘密。
   - 稽核證據只記「鍵存在」，不記值。
5. **SQL 隔離的邊界比「另建 DB」大。**
   - 平台測試 DB 與 CRM 的 `*_MSCRM` 在同一個 `MSSQLSERVER` 執行個體，而 Codex 帳號是 sysadmin。
   - 為 `IIS APPPOOL\<新池>` 建立的是伺服器層級 login，清理時必須 `DROP LOGIN`。
   - 該 user 只對測試 DB 有權限：不加 server role，不 mapping 到任何 CRM DB，不給 `db_owner`。附錄 A.8 的資料表由部署者建立，app 只需要資料讀寫與刪除舊列。
   - `CREATE` 與 `DROP DATABASE` 一律用完整、已驗證名稱，不用萬用字元，也不在 CRM 檔案目錄建檔。
   - 建庫前後都要比對資料庫清單，而且 CRM DB 清單不得變動。
   - 同機也要限制測試 DB 的大小成長。
6. **VM 作業系統是否支援 ICU 要先確認。**
   - 程式有 `StartupGuards.ValidateIcuMode()`，.NET 在 Windows 依賴 OS 的 ICU（Windows 10 1903／Server 2019 以上才有）。
   - 若 VM 是較舊的 Windows Server，站台會啟動失敗。
   - 先唯讀查 OS 版本。這是低成本的先決檢查，要在部署前做。
7. **出席紀錄會寫入 `goodshepherdback`，而它是正式組織的複製。**
   - 第 3 步的「出席紀錄寫入」實際寫的是這個 D365 組織。
   - 只能用事先指定的測試聯絡人與測試 `lineId`，並做標記。
   - 事件前先備份，並寫好清理查詢條件。
   - 不得用任何真實會友的 LINE ID。
8. **LINE Webhook URL 要先讀後改。**
   - 先 `GET /v2/bot/channel/webhook/endpoint`，記下原值（只記在回報，不含秘密），再 `PUT`。
   - 回復是改回原值。
   - 只用測試 Channel，且 token 只從記憶體使用。
9. **「清理後為 0」與「Webhook URL 保持指向測試站」互相衝突。**
   - 04 第 7 步與 P4-05／P4-06 都要求測試站持續運作。
   - 所以 site、app pool、DB 結構、login、`hostnameport` 憑證項目會刻意保留，只有測試資料列和暫存檔清到 0。
   - 回報必須分成「刻意保留清單」與「清到 0 清單」，不能寫成全部為 0。請你決定要不要保留站台。

### Warning
10. **P3-06 三項 Warning 的實機核實方式不同，不要都報通過。**

| Warning | 可行的決定性證據 | 限制 |
|---|---|---|
| ① 30 秒事件 timeout | 只讀確認程式常數 `EventTimeout=30s`。實測要讓下游延遲，例如暫時阻擋本站程式到 `api.line.me` 的出站，量測約 30 秒後取消，且後續事件仍處理。 | 阻擋出站屬防火牆變更，且 VM 與 CRM 共用。必須限定在這個程式路徑、限時、逐項回復，並先經你同意。否則標「未確認」。 |
| ② appointment 額外教會欄位 | 對 `goodshepherdback` 做唯讀 `EntityDefinitions(appointment)` 屬性查詢，核對程式依賴的欄位在實際 schema 的存在與型別。 | 這是唯讀、最可行的一項，可做決定性確認。 |
| ③ annotation 重送 Upsert | 模擬事件的 `messageId` 在 LINE content API 查不到內容（404），音訊處理路徑可能走不到。 | 要確認只能靠手機實際傳一則音訊給測試 Channel（04 視為選做）。沒做就明列「未確認」。 |

11. **第 3 步的「主機送出紀錄」依賴出站與日誌。**
   - VM 必須能連 `api.line.me`；需要防火牆出站例外時，只可對該目的地開，並限定該程式。
   - 日誌不能含 token 或簽章。
   - LINE 回「Invalid reply token」要與連線失敗、TLS 失敗分開記錄。
12. **IIS 設定細節。**
   - App pool 用 No Managed Code，並使用獨立 app pool、獨立資料夾。
   - `Logs` 目錄需要 app pool 身分的寫入權，其他位置不給。
   - 不要用 `iisreset`。
   - 站台只建 HTTPS 具名 binding，不加 `*:80`。
   - 預設 20 分鐘閒置回收可能讓 LINE 驗證的首個請求逾時。可評估關閉閒置回收，這只影響新池。
13. **外部可達性。** 公網已查到 `220.134.52.83` 並回 302，入站路徑存在。部署前不能把這個 302 當作 Webhook 已通。

### Info
- 二進位建議 framework-dependent 發佈，runtime 用 10.0.14。ASP.NET Core 8 runtime 保留，CRM 不受影響。
- 04 第 5 步的重送驗證，要連同去重表的列數一起核對；第 6 步的 401 要確認沒有寫入去重列。
- 不建議的做法：整份 `restore backup`、`iisreset`、改 `*:443:`、與 CRM 共用 app pool 或 DB、在命令列帶秘密、用離線測試代替實機結果。

### 必須先確認或不得執行
1. 先確認：秘密注入方式（#4）、VM 的 OS／ICU（#6）、是否保留測試站（#9）、是否同意為 Warning ① 做出站阻擋（#10）。
2. 不得執行：任何修改 CRM site、`*:443:`、CRMAppPool 或憑證的動作，以及整份 `restore backup`。
3. 建議順序：
   1. 唯讀查 OS 與 ICU。
   2. 做 post-bundle 的備份與快照，含 `netsh http show sslcert`。
   3. 建測試 DB 與限權 login。
   4. 建 app pool、site 與 SNI binding，立即比對 CRM 不變。
   5. 部署並放入設定檔。
   6. 設 LINE URL（先記原值），做驗證。
   7. 做 04 第 1～7 步與三項 Warning。
   8. 清理並再次比對。
   9. 寫回報與審查，推送時以 `git ls-remote` 核對 SHA。

### 行動項目
1. [ ] 修正受阻回報裡的回復說明（#1）。
2. [ ] 取得你對 #4、#9、#10 的決定。
3. [ ] 唯讀查 VM 的 OS／ICU 與 `netsh http show sslcert` 基線。
4. [ ] 建 post-bundle 備份，再開始 `implement.md` 第 6 項。

---
SESSION_ID: 7bd8846f-522a-4b35-b996-970ee6828708

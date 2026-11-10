## 分析：P3-09 Hosting Bundle 安裝後的續作風險

本次只讀了 P3-09 PRD、設計、04 P3-09 第 1～7 步、00 §11、安裝紀錄，以及 LineWebhook 的 `Program.cs`、`LineWebhookRegistration.cs` 和 `appsettings.json` 的鍵名。我沒有讀任何秘密值，也沒有對 VM 做任何操作。下面標「待實機確認」的項目，是我沒在現場驗證的推論。

### 現況
- 已核實（使用者紀錄加 Codex 唯讀複核）：
  - ANCM v2 和 ASP.NET Core／.NET 10.0.14 已裝好。
  - CRM site 與 `CRMAppPool` 為 Started，CRM 的 `*:443:` binding 沒變。
  - goodshepherdback 回 HTTP 302，兩份暫存安裝檔已刪。
  - IIS 備份 `P3-09-pre-202611101130` 還在。
- 尚未建立：
  - 獨立測試 DB。
  - Webhook 專用 app pool 和 site。
  - SNI binding。
  - 任何 LINE 呼叫。
- LineWebhook 是 net10 的 `Microsoft.NET.Sdk.Web` 專案。
  - 啟動時要求 Hosting、Localization、ToolUtility 和 `AddLineWebhook` 都已註冊，少一個就拒絕啟動。
  - repo 的 `appsettings.json` 只有 `LineWebhook:EnableLegacyCallback`。
  - 從程式碼掃到的必要設定鍵有：
    - `ConnectionStrings:Platform`
    - `LineWebhook:Channels`
    - `LineWebhook:Queue`
    - `LineMessaging:DefaultOrganization`
    - `Localization:ReleasedCultures`
    - `Localization:AllowPseudoLocale`
    - `Dataverse:Pool`
    - `Deployment:Organizations`
  - 掃描只做了關鍵字搜尋，完整清單要以啟動驗證為準。

### Critical

1. **SNI 與既有 `*:443:` 共存的行為沒有證據。**
   - 設計要求「無法證明 HTTP.sys 同時處理具名 SNI 與 `*:443:` 就停止 IIS 寫入」。
   - 新增 `host:443` binding 時，若 `sslFlags` 或憑證綁定處理不當，HTTP.sys 會多出一筆 SNI 條目；萬用 binding 的憑證綁定若被覆寫，CRM 會中斷。
   - 這台沒有現成的 SNI 前例，而且同 IP 同埠，CRM 就是爆炸半徑。
   - 建議：
     - 先用 `netsh http show sslcert` 和 `appcmd list site /config` 存下完整 baseline。
     - 新 binding 用 `/bindings:https/<hostname>:443`、`sslFlags=1`，並選用同一張現有萬用憑證（指紋相同，只讀取，不更動）。
     - 建好後立即檢查：CRM 的 `*:443:` 仍在、`goodshepherdback` 仍回 302、測試 hostname 路由到新站。
     - 任一項失敗，立即移除新 binding。不要用 `restore backup` 當第一步，因為它會回復整份 `applicationHost.config`，可能蓋掉使用者之後的合法變更。先逐項撤銷，確有必要才還原備份。
   - 另一個要先確認的點：目前測試 hostname 回的 302 來自 CRM 的 `*:443:` 兜底。新 SNI 生效後，要用兩個 hostname 分別驗證路由，不能只看狀態碼是 302。

2. **平台 DB 與 CRM 隔離只靠命名，沒有權限邊界。**
   - SQL 登入是 sysadmin，PRD 也承認這點。
   - `ConnectionStrings:Platform` 若指向預設資料庫、或沿用 sysadmin 登入，應用程式有權限觸碰 CRM 的 `*_MSCRM` 資料庫。
   - 建議：
     - 建庫前先列出現有資料庫並留存。
     - 新庫用專用名稱，連線字串必須明寫 `Initial Catalog=<新庫>`。
     - 優先用 Windows 整合驗證，授權給新 app pool 身分 `IIS AppPool\<新池名>`，且只在新庫授予 `db_datareader`、`db_datawriter`，加上 `001_LineWebhookEvent.sql` 需要的建表權限（建好後可收回）。
     - 不要把 sysadmin 帳號寫進站台設定。
   - 注意：授權該 app pool 登入到 SQL Server 會建立 server-level login，這是 instance 層級變更，要記入清理清單。

3. **秘密進入 IIS 站台的方式沒有設計。**
   - repo 的 `appsettings.json` 沒有 Channel secret、token、DB 連線字串。
   - DPAPI Clixml 和 User Secrets 是 Codex 本機的機制，IIS 上的 app pool 身分無法讀取。
   - 常見的錯誤做法：把值寫進 `web.config` 的 `environmentVariables`，或寫進 `appsettings.Production.json`。這兩者都會把秘密落地在 VM 上。
   - 建議：
     - 先決定載入方式再部署。
     - 候選：僅 app pool 身分可讀的 ACL 限制檔，或機器層級環境變數；`appcmd` 的 `environmentVariables` 會寫進 `applicationHost.config`，而該檔已被備份並可能被比對、複製，不建議用。
     - 無論用哪種，要在步驟 1 前給使用者確認（00 §9-1 的秘密規則），並在清理時一併移除。
     - 命令列、標準輸出、證據檔都不得出現值。

### High

4. **`ASPNETCORE_ENVIRONMENT` 與偽語系需求可能衝突（待實機確認）。**
   - `AddSpeechMessageLocalization` 會把 `IsProduction()` 傳給語系驗證，且有 `Localization:AllowPseudoLocale` 設定。
   - 步驟 4（偽語系會友）預期在 Production 下很可能被政策拒絕，除非明確開啟。
   - 建議：
     - 先讀 `StartupGuards.ValidateLocalizationConfiguration` 確認 Production 是否禁止偽語系。
     - 不要為了讓步驟 4 通過就把環境改成 Development，那會連帶開啟開發者例外頁等行為。
     - 若 Production 禁止，在回報中如實記錄「步驟 4 的前置條件與環境衝突」，由使用者決定。

5. **站台與 app pool 的部署形態沒有檢查。**
   - 需 `dotnet publish` 產物，並確認 `web.config` 由發佈產生、`hostingModel` 為 inprocess，且 `aspnetcorev2_inprocess.dll` 存在。
   - 要新建獨立 app pool：`managedRuntimeVersion=""`（No Managed Code），不與 `CRMAppPool` 共用，也不沿用其身分。
   - 站台實體路徑放新目錄（例如 `C:\inetpub\linewebhook-test`），不要放在 CRM 目錄下，避免被 CRM 的 web.config 繼承影響。
   - 專案下 `Logs` 目錄由 `AddSpeechMessageHosting(Path.Combine(ContentRootPath, "Logs"))` 建立，app pool 身分需要寫入權限。
   - 這是單向的權限設定，只給新目錄，不要放寬到 CRM 或 `C:\Windows\Temp`。

6. **LINE Webhook 測試步驟的可觀察性不足。**
   - 步驟 3 要求檢查「主機送出的回覆請求內容」。
   - `Logging.ClearProviders()` 加 `ExceptionLoggerProvider` 表示一般 log 不會輸出，而回覆請求並不一定有紀錄。
   - 要在步驟 3 前確認用什麼證據：
     - 候選：`Line.Messaging` 的送出紀錄或 `LineWebhookReplySender` 的測試鉤子。
     - 若沒有，就只能以 SQL 的出席紀錄加 LINE 回「Invalid reply token」的錯誤作為間接證據，並在報告中如實標示。
   - 不要為了取得證據而臨時加入會記錄 token 或內文的 logging。

7. **`PUT /v2/bot/channel/webhook/endpoint` 會改變測試 Channel 的實際設定。**
   - 這是 LINE 端的持久變更，且若 Channel 其實也被其他環境使用，會把那邊的 Webhook 導走。
   - 建議：
     - 呼叫前先用 `GET /v2/bot/channel/webhook/endpoint` 讀取並記錄現值（URL 不是秘密）。
     - 回復步驟就是 PUT 回原值。
     - 先確認這個 token 的 Channel 確實是測試 Channel，而非正式 Channel（00 §11.3）。
     - 驗收結果要保留指向測試主機（步驟 7），所以結尾不要回復原值。

### Warning

8. **P3-06 三項 Warning 的可驗證範圍各不相同。**

   | Warning | 實機可取得的決定性證據 | 不可宣稱 |
   |---|---|---|
   | 30 秒事件 timeout（P3-05 的 `SqlWebhookEventDeduplicator` 以 30 秒預算，HTTP timeout 被它包住） | 用測試用延遲回應（例如對語音下載點注入延遲）觀察事件在 30 秒左右被取消；SQL lease 狀態。需要可控的慢速目標，LINE 真實端點做不到 | 不能用離線單元測試冒充；若沒有可控延遲來源，列為「未確認」 |
   | appointment schema 多出的教會範圍欄位 | 對 goodshepherdback 做唯讀 metadata 查詢（`export-metadata` 或 `verify`），比對該欄位是否存在於 `new_appointment` 實體 | 不寫入 CRM 資料，不為了驗證建立資料 |
   | annotation 重送 Upsert | 在 Dataverse 層需要寫入 annotation，這會碰 CRM 資料庫。步驟 5 只驗證事件去重，並不涉及 annotation 的 Upsert | 除非使用者明確允許對測試聯絡人寫入，否則列為「未確認」，不可用步驟 5 的結果替代 |

   這三項最可能的結果就是「兩項未確認」。這是可接受的，PRD 也寫明「無法確認者明列未確認」。不要為了湊成通過而擴大實機寫入。

9. **步驟 3 的偽造事件會觸發真實的 Dataverse 寫入。** 「出席紀錄寫入」代表寫進 goodshepherdback 的 CRM 資料。
   - 必須限定在測試聯絡人，並先備份、記錄寫入前後的筆數（00 §11.3）。
   - 清理要刪除這些出席紀錄，而「CRM 資料庫不變」的驗證標準與此有張力：嚴格說，寫入 Dataverse 就是在改 CRM。
   - 建議在 PRD 的措辭上區分「CRM 站台、設定、DB 結構不變」和「測試聯絡人的測試資料可建立並清除」，並讓使用者確認。

10. **憑證與逾期。** 憑證 2027-05-06 到期，目前沒問題；SNI binding 只是引用，沒有複製憑證檔或私鑰，也不要匯出。

### Info

11. 兩份暫存安裝檔已移除，安裝階段的殘留為 0。
12. 安裝期間 302 的取樣不足以推斷中斷時長，紀錄中的說法是準確的。
13. Gemini 後端持續 403/503，本次分析是單模型（Claude），報告中不得稱雙模型完成。

### 不應執行或須先確認的步驟
- 不得：
  - 修改 CRM site、`*:443:` binding、`CRMAppPool` 或憑證。
  - 執行 `iisreset`。
  - 以 `restore backup` 作為第一個回復手段。
  - 用 sysadmin 連線字串部署。
  - 把秘密寫進 `web.config`、`applicationHost.config` 或命令列。
  - 使用正式 Channel。
- 動手前須先確認：
  - 秘密載入方式（Critical 3）。
  - 偽語系與 Production 的關係（High 4）。
  - 步驟 3 的證據來源（High 6）。
  - 對 CRM 測試聯絡人寫入的範圍（Warning 9）。

### 建議順序
1. 唯讀存下 baseline：`netsh http show sslcert`、`appcmd list site/apppool/config`、SQL 資料庫清單、`goodshepherdback` 回應。
2. 建立隔離 DB，建專用最小權限登入，執行 `001_LineWebhookEvent.sql`。
3. `dotnet publish` 到新目錄，建立 app pool、site，先只綁 HTTP 內部埠（例如 8080）做本機 `curl` 煙霧測試，這一步完全不碰 443。
4. 煙霧測試通過後再加 SNI binding，立刻驗證 CRM 與新站。
5. 步驟 2 以 `GET` 記錄現值，再 `PUT`，並呼叫 Webhook 測試 API。
6. 依序做步驟 3～6，最後清理並比較 baseline。

先加內部 HTTP binding 做煙霧測試，能在不碰 443 的情況下抓出大部分啟動錯誤（缺設定鍵、權限、ANCM），這是我最建議的一步。

### 行動項目
1. [ ] 與使用者確認 Critical 3 的秘密載入方式。
2. [ ] 讀 `StartupGuards.ValidateLocalizationConfiguration`，確認 Production 對偽語系的限制。
3. [ ] 決定步驟 3 的回覆內容證據來源。
4. [ ] 把 P3-06 三項 Warning 的「未確認」預期寫進驗收步驟。
5. [ ] 修訂 PRD 中「CRM 資料庫不變」與「Dataverse 測試資料寫入」的措辭。

---
SESSION_ID: 6552d795-9ec1-45e2-9ebf-6adba150f169

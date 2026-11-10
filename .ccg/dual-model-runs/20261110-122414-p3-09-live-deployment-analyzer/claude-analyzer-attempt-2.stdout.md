我已讀完 PRD、設計、04 P3-09、00 §11、安裝紀錄，也對照了 LineWebhook 的 Program、端點、設定來源和三項 Warning 的程式位置。以下是分析，沒有寫入任何東西，也沒有讀取秘密值。

**限制**：本次沒有重新連 VM。IIS、SQL 的現況都引自 `hosting-bundle-install.md` 與 PRD，必須在動手前重新唯讀驗證。

# 分析：P3-09 安裝後續作

## 現況
- Hosting Bundle 10.0.14、ANCM v2、CRM site 和 CRMAppPool 都已核對。
- 還沒做的是 implement.md 的第 6～10 步：隔離 DB、獨立 site/pool、部署、第 1～7 步驗收、清理。
- LineWebhook 是 net10 的 `Sdk.Web` 專案，沒有 `UserSecretsId`，`Program` 也沒有呼叫 `AddDevelopmentConfigurationOverrides`。

## Critical

**C1. 秘密進不了 IIS 程序，而且「秘密不落地」會和部署衝突。**
- 站台需要的設定有 `Deployment:Organizations:goodshepherdback:TestChannelSecret`、`ConnectionStrings:Platform`、`LineWebhook:Channels:*`，以及 `AddToolUtility` 連 D365 用的連線設定。
- 本機的 User Secrets 在 VM 上不存在。
- 在 IIS 下要提供秘密，只剩這幾條路，各有代價：
  - app pool 的 `environmentVariables` 會明文寫進 `applicationHost.config`，也會進之後的 appcmd 備份。
  - `web.config` 的 `<environmentVariable>` 同樣是明文落地。
  - 把 `secrets.json` 放進 pool 身分的 profile 也是落地。
- 這和 00 §11.3「不寫檔」互相衝突，我找不到能完全不落地的現成做法。
- 動手前必須先決定用哪一種，並寫進手冊。
- 建議方向：用 pool 專用的 ACL 保護檔案，或建好 pool 後用 DPAPI 保護的設定。不要放進 applicationHost.config，以免被備份複製。
- 不要把這個問題當成部署細節；這是 P3-09 最先要拍板的事。

**C2. 沒有證據證明 binding 共存方式。**
- 設計自己寫了「若無法證明 HTTP.sys 能在保留 `*:443:` 時處理 SNI，就停止 IIS 寫入」。
- 要先用唯讀方式取得三樣證據：
  - `netsh http show sslcert` 的現況，確認 `0.0.0.0:443` 項目存在。
  - 新 binding 只會新增 `hostnameport` 項目，不會碰 `ipport`。
  - CRM binding 的 `sslFlags`。
- 新 binding 的形式只能是 `https *:443:linewebhook-test.speechmessage.com.tw`，`sslFlags=1`，並選用既有萬用憑證。
- 不匯入、不替換憑證，也不用 `netsh http delete/add sslcert` 處理 `ipport`。
- 驗證要做「兩個 hostname 同時」的對照：`linewebhook-test` 到新站，`goodshepherdback` 仍是 302。

## High

**H1. 回復備份不乾淨。**
- 備份 `P3-09-pre-202611101130` 是在 11:30 建立，Hosting Bundle 是在 11:39 安裝。
- Bundle 安裝很可能改了 `applicationHost.config`（加入 ANCM 的 globalModules/modules）。
- 若日後用 `appcmd restore backup` 還原，ANCM 登錄會一起被還原，bundle 還裝著卻失效。
- 還原也可能觸發所有 pool 回收，包含 CRM。
- 做法：
  - 在任何 IIS 寫入前，另建一份安裝後備份（例如 `P3-09-post-bundle-…`）。
  - 基線比較改以這份為準，要用唯讀文字 diff 驗證 CRM site、pool、bindings 沒變。
  - 回復的主要手段是只刪自己建的 site、pool、DB 和 login。整份 restore 只當最後手段，並先告知影響。
- 必須先確認「安裝前備份是否真的在安裝前」，再確認 pre 與 post 備份的差異只有 ANCM 相關項目。

**H2. 新站的隔離細節。**
- 新站必須是獨立 site，不能做成 CRM site 底下的虛擬應用程式，否則會繼承 CRM 的 web.config。
- app pool：
  - 新建，不能共用 CRMAppPool。
  - 設為 No Managed Code。
  - 使用 ApplicationPoolIdentity。
  - 不能用 CRM 的服務帳號。
- 實體路徑放在 CRM 目錄之外。
- site 名稱、ID、路徑都要先確認沒有衝突。
- 不要用 `iisreset`。
- `w3wp.exe` 是 CRM 與新站共用的執行檔名，防火牆規則或程序層級的故障注入不能以程式路徑為目標（見 W1）。

**H3. SQL 權限和授權範圍。**
- 平台 DB 要用專屬名稱，在同一個 MSSQLSERVER 執行個體上新建。
- 建議用 Integrated 驗證，讓 `IIS APPPOOL\<新pool>` 做一個 login 加一個 DB user，只授權在 `LineWebhookEvent` 上的 SELECT、INSERT、DELETE。
- 不給 db_owner 或 sysadmin，也不授權任何 MSCRM 資料庫。
- 這個 login 是 SQL 內建主體，不是網域或 D365 帳號，應不違反 §11.3「不碰安全邊界」。但這是共用執行個體上的新 server principal，我建議在紀錄中明寫理由。
- 若改用 SQL 驗證，會多出一個密碼要保管，所以不建議。
- 建庫前記錄 `sys.databases` 清單，建庫後再比對，確認只多了這一個。
- 結構只用 `Database/001_LineWebhookEvent.sql`，不自動建表。
- 該 SQL 檔不含「存在就略過」，重跑會失敗，所以建庫和建表必須一次做對。

**H4. 第 3 步「檢查主機送出的回覆請求內容」目前沒有證據通道。**
- 主機只對 LINE 發出回覆，記錄管道是 `ExceptionLoggerProvider` 只寫 Error，而且設計明確不輸出 body。
- 因此「回覆內容」和「偽語系全部有括號」在實機上沒有現成的觀察方法。
- 動手前要先找出可用的證據通道，例如測試環境才有的出站內容紀錄。
- 若沒有，第 3、4 步只能核實「出席紀錄寫入」和「HTTP 回應」，回覆內容要明列「未確認」，不能拿離線測試代替。
- 同時確認 D34 清理已完成：goodshepherdback 是正式機複本，要先確認會友 `new_lineid` 已清空，只剩測試 Contact。
- 模擬事件的 userId 只能對應測試 Contact。

## Warning

**W1. 30 秒事件逾時。**
- 它是靜態常數，`WebhookQueueOptions.EventTimeout`，不能用設定調整。
- 實機上沒有安全的故障注入手段：
  - 防火牆規則若以 `w3wp.exe` 為目標會波及 CRM。
  - 改程式或常數則已不是被驗收的版本。
- 較誠實的做法：記錄正常事件從收到到完成都遠小於 30 秒（用 D365 紀錄時間戳與請求時間比對），其餘標示「30 秒逾時路徑：實機未確認」。
- 注意：HTTP 200 在入列後就回傳，處理是非同步的，所以請求回應時間不能代表處理時間。

**W2. appointment 額外教會範圍欄位。**
- 可以用 `d365/snapshots/goodshepherdback/…/metadata.json`（13 MB，來自實際組織匯出）核對。我只做了粗略搜尋，看到 `appointment` 實體存在，教會相關欄位多數屬 contact。
- 這份快照應該重新匯出一份新的，並用唯讀 `RetrieveEntity` 確認 appointment 上的欄位，再與程式實際讀寫的欄位逐一對照。
- 這是唯讀、決定性的證據，成本低，列為可以通過的項目。
- 注意：`d365/snapshots/` 目前是 untracked，不要提交。

**W3. annotation 重送的 Upsert。**
- `AnnotationUpsertService` 以確定性主鍵 Upsert，同事件重送理論上只留一筆。
- 但音訊內容需要從 LINE 取回，模擬事件的 messageId 取不到內容，會失敗在下載階段，根本到不了 Upsert。
- 因此要實機驗證，必須有人用測試帳號實際傳一則語音（這是計畫標為選做的手機操作）。
- 沒有這一步，就誠實標「未確認」。
- 若有做，驗證方法是用同一個 messageId、不同 `webhookEventId` 各送一次，查 annotation 筆數為 1。

## LINE API 與步驟順序的風險

- **記下舊 endpoint。**
  - 在 `PUT /v2/bot/channel/webhook/endpoint` 之前，先 `GET` 取得現行 endpoint 並記錄，才有回復點。
  - 這會把測試 Channel 的所有流量導向新站，若有其他人也在用同一個測試 Channel（例如本機開發通道），他們會被切走。
- **確認 token 存在。**
  - PUT 需要 channel access token，目前文件只確認了 `TestChannelSecret` 存在，要另外確認 access token 的鍵存在（只確認存在，不讀值）。
- **「Use webhook」開關。**
  - 開關在 LINE 後台，Messaging API 不能設，可能需要使用者協助。
- **Webhook 測試 API 的涵蓋範圍。**
  - 它送的是空 `events` 陣列，只能證明驗簽和 200，不證明事件處理，也不會產生去重資料列。不要把它的成功寫成第 3 步的證據。
- **LINE 只用測試 Channel。** 這一條沿用 §11.3，沒有額外風險。

## 清理與驗收定義的衝突

- 04 第 7 步要求 Webhook URL 保持指向測試主機，04 還說明 P4-05 要用這個測試站，所以站台、pool、binding 完成後應該留著。
- 但 PRD 又寫「本次擁有的資源清理後為 0」。
- 建議在動手前定義：
  - 測試資料（資料列、D365 的測試出席紀錄與 annotation）清到 0。
  - 站台、pool、DB 結構留存，並如實寫成「保留」，不要寫成「資源 0」。
  - 若要全部移除，同時 Webhook URL 也必須回復或指向可用端點，否則 LINE 會持續打到失敗的 endpoint。
- 秘密殘留也要查：`applicationHost.config`、備份檔、web.config、日誌、證據檔，這是 C1 的收尾檢查。

## 不應執行、或必須先確認的步驟

| 項目 | 結論 |
|---|---|
| 修改 CRM site、`*:443:` binding、CRMAppPool、憑證 | 不得執行 |
| `iisreset`、整份 `appcmd restore backup` | 不作為一般流程；restore 僅為最後手段，先告知影響 |
| `netsh http` 操作 `ipport` 項目 | 不得執行 |
| 以 `w3wp.exe` 為目標的防火牆故障注入 | 不得執行（會波及 CRM） |
| 授權 CRM 資料庫或 sysadmin 給新 login | 不得執行 |
| 把秘密放進 `applicationHost.config`、命令列、repo、證據 | 不得執行；要先決定 C1 的方式 |
| PUT 新 endpoint | 先 GET 記錄舊值，並確認 token 鍵存在 |
| 提交 `d365/snapshots/`、`.ccg/dual-model-runs/` | 不要提交（體積大，非交付物） |
| 用離線測試或 Webhook 測試 API 冒充第 3～5 步 | 不得執行 |

## 建議順序（每步先記錄前置、動作、結果、回復）
1. 唯讀重驗：CRM site、pool、binding、`netsh http show sslcert`、`sys.databases`、`goodshepherdback` 的 302。
2. 另建安裝後的 appcmd 備份，並與安裝前備份做 diff。
3. 決定並記錄秘密提供方式（C1）。
4. 建 DB、最小權限的 login/user 與結構。
5. 建獨立 pool 與 site，只加 SNI binding，部署後以 hostname 對照測 CRM 與新站。
6. 取得舊 Webhook endpoint，PUT 新值，跑 Webhook 測試 API。
7. 依序做第 3～6 步，並如實標記無法觀察的項目。
8. 清理、比對 CRM 基線、掃描秘密殘留。

## 結論
- 目前沒有 Critical 級的「已發生」問題，但 C1 和 C2 是動手前必須先解決的阻擋項。
- H1、H4 會直接影響驗收結論的可信度。
- Warning 三項的實機可確認程度：
  - W2 可以用唯讀 metadata 核實。
  - W1 和 W3 在沒有人工語音與故障注入的情況下，只能誠實標「未確認」。

(本次是單模型分析：Gemini 沒有輸出，不可記為雙模型完成。)

---
SESSION_ID: cd6b03c4-5e93-4eef-98be-c583147376d2

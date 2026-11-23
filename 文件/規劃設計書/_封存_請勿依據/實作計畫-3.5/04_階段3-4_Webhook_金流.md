# 04　階段 3～4：LINE Webhook 主機、金流與幣別（實作計畫 3.5）

> 兩個階段各一個分支：`1.0.0.11.Integration.P3.Worktree`、`.P4.Worktree`。每個階段驗收後才開下一個。階段 2 已驗收，摘要見 `03_階段2_完成摘要.md`。
>
> **閱讀方式：** 每個工作包的規格沿用 3.4（3.3 起已核對）；每個階段開頭有「工作包總表」（依賴、實機、時間盒）。實機步驟由 **Codex 依 00 §11 自行執行**。每包的完成條件＝00 §7.1 的五項＋該包列出的測試，不得加碼（D29）。
>
> **3.5 新增：** 每個階段開工先做一次「前置缺項檢查」（00 §7.3，D37），P3-09、P4-06 需要的秘密、主機與權限在階段開頭就一次列給使用者，不再等到實機那一步才停。

---

# 階段 3　LINE Webhook 主機（`SpeechMessageProducts.LineWebhook`）

- 估時：原估 12～17 天；依實際速度約 3～5 個工作天（P0～P2 實際速度見 00 §15）。
- 目標：LINE 入站全部在 .NET 10。
  - 驗簽、去重、先回 200、有界佇列、事件分派；
  - 關鍵字依語系路由；
  - 回覆依會友語言。
- 不再使用 `.其他解決方案\LineApiWebhook` 的主機。
- **起點：** 3.5 切換提交（00 §14 的 P3-00）；P3 分支從該提交開出，該提交即編碼檢查的 `-Base`。

## 階段 3 前置缺項檢查（00 §7.3；P3-00 完成後、P3-01 開工時一起做）

Codex 唯讀核對下列項目，缺的一次寫進一頁「前置缺項清單」交使用者，同時先做 P3-01～P3-08 的離線工作：

| 項目 | 給哪個 WP | 誰提供 | 核對方式（不讀值、不輸出秘密） |
|---|---|---|---|
| 測試 Channel 的 Channel secret 已在 User Secrets `Deployment:Organizations:goodshepherdback:TestChannelSecret`（2026-11-09 已存；使用者決定沿用、不重新發行） | P3-03、P3-09 | 已備妥；若被 LINE 判定無效才請使用者處理（00 §9-1） | 鍵存在、格式為 32 碼十六進位 |
| 測試 Channel 的 Channel ID（非秘密） | P3-09 | repo 或既有紀錄；查不到才問使用者 | 數字字串 |
| DMZ 測試主機：IIS、可用的 HTTPS 憑證、對外可連的網址 | P3-09 | Codex 自查；需要購買或對外 DNS／防火牆入站變更時依 00 §9-1 | 唯讀列出站台、繫結、憑證到期日 |
| Codex 對測試主機的實際操作權限（遠端連入、部署 IIS、重啟服務） | P3-04、P3-09 | Codex 自查；權限不足依 00 §9-1 請使用者協助或代為執行 | 實際執行一次唯讀命令；失敗記錄錯誤碼（例如 1312、權限不足） |
| 測試主機 SQL Server 可建立測試資料庫 | P3-04、P3-09 | Codex 自查 | 唯讀連線與權限查詢 |

## 階段 3 工作包總表（3.5，範圍與時間盒同 3.4）

| WP | 名稱 | 依賴 | 實機（Codex） | 時間盒 |
|---|---|---|---|---|
| P3-01 | 舊行為的黃金資料：103 個關鍵字、事件樣本、回覆內容 | — | 無 | 1 天 |
| P3-02 | 主機專案與端點（只組裝） | P2-01 | 無 | 半天 |
| P3-03 | 驗簽 | P3-02 | 無 | 半天 |
| P3-04 | 去重（平台 SQL，D16） | P3-02 | 測試主機 SQL（Codex 建表） | 1 天 |
| P3-05 | 有界佇列與背景處理（依 userId 分區保序） | P3-02 | 無 | 1 天 |
| P3-06 | 事件分派與處理器移植 | P3-01、P3-05 | 無 | 1 天（超過依 00 §12 拆 a／b） |
| P3-07 | 關鍵字路由改用 `IKeywordCatalog` | P3-01、P3-06 | 無 | 半天 |
| P3-08 | 回覆、letter 紀錄（D19）、例外處理 | P3-06 | 無 | 1 天 |
| P3-09 | 實機驗收：Webhook | P3-02～P3-08 | **有** | 1 天 |

新增 `SpeechMessageProducts.LineWebhook` 與 `.Tests` 兩個 csproj 時，同一步加入 `SpeechMessageProducts.sln`（00 §3.2）。

## P3-01　舊行為的黃金資料（先做，其他 WP 都依賴它）

- **新增** `tools\LegacyGolden\`（net10 console，不進 .sln）。
  - 先用 P0-05 的轉換工具把舊檔轉成 UTF-8 到暫存區，不修改 `.其他解決方案` 原檔；
  - 讀取 `.其他解決方案\LineApiWebhook` 的 `MessageRoutingKeywords.cs`（以 Glob 尋找確切路徑，該檔是 CP950，約 587 行）；
  - 編譯一個只含關鍵字表與比對函式的 harness，輸出 `Golden\Keywords\routing.json`：每個關鍵字與幾個變形（全形、前後空白、大小寫）→ 意圖。
- **事件樣本：**
  - 依 LINE 官方 Webhook 文件格式建立 12 種事件的樣本 JSON：message（text、image、audio、location、sticker）、follow、unfollow、join、leave、postback、accountLink、memberJoined；
  - 放在 `SpeechMessageProducts.LineWebhook.Tests\Golden\Events\`；
  - goodshepherdback 的 letter 或追蹤中若有真實事件，Codex 可自行匯出並遮罩個資後使用；不向使用者索取。
- **回覆內容：** 對每個關鍵字，記錄舊程式「回覆什麼樣的訊息」：類型與文字，個資遮罩。
  - 能從程式靜態得知的直接記錄；
  - 需要 D365 資料才能決定的，記錄為「呼叫哪個處理器＋參數」。

## P3-02　主機專案與端點

- **`SpeechMessageProducts.LineWebhook`**（net10）：
  - `Program.cs` 只組裝：`AddSpeechMessageHosting`、`AddSpeechMessageLocalization`、`AddToolUtility(..., "GoodShepherd")`、`AddLineWebhook`、`AddSpeechMessagePayments`（階段 4）；
  - 符合 R2。
- **端點：** `POST /api/line/webhook/{channelId}`。舊路徑 `/api/callback` 保留為相容路由，以設定開關控制，預設關閉。
- **處理順序：** 讀取 body，上限 1 MB → 驗簽 → 解析 → 每個事件做去重 → 放入佇列 → 回 200。
  - 驗簽失敗回 401；
  - 佇列滿回 503，LINE 會重送，去重保證不重複處理；
  - **不再有「任何錯誤都 return Ok()」的吞錯行為。**

## P3-03　驗簽

- 由 Tenancy 依 `channelId` 取 Channel secret，`LineSignature.Verify`（P1-03）。
  - secret 為空時拒絕啟動該 Channel，不允許略過驗簽。舊設定的 `ChannelSecret` 是空字串，必須補上。
- **測試：**
  - `Webhook_InvalidSignature_401`
  - `Webhook_EmptySecretChannel_RejectedAtStartup`
  - `Webhook_BodyTooLarge_413`

## P3-04　去重（平台 SQL）

- **資料表：** 見附錄 A.8，`LineWebhookEvent(ChannelId, WebhookEventId, ReceivedUtc, IsRedelivery)`，主鍵為 `(ChannelId, WebhookEventId)`。
- **`IWebhookEventDeduplicator.TryRegisterAsync`：**
  - 以 INSERT 搭配主鍵衝突判斷，衝突代表重複；
  - 使用 `Microsoft.Data.SqlClient`，連線字串 `ConnectionStrings:Platform`；
  - SQL 逾時 5 秒；逾時視為失敗：回 503，讓 LINE 重送，**不可以當成「不重複」繼續處理**。
- **清除：** 每日清除 7 天前的資料，由 Hosting 的排程背景服務執行。
- **平台 SQL：** 測試環境由 Codex 在測試主機的 SQL Server 建立資料庫與附錄 A.8 的資料表；正式環境的連線字串若 Codex 無法自行取得，依 00 §9-1 請使用者提供。不得改用記憶體去重上正式環境，記憶體實作只給測試用。
- **測試：**
  - `Dedupe_SameEvent_SecondIsDuplicate`
  - `Dedupe_SqlTimeout_Returns503`
  - `Dedupe_SameEventIdDifferentChannels_NotDuplicate`
  - `Cleanup_DeletesOnlyOlderThan7Days`
  - SQL 實作的整合測試：以 LocalDB 或測試主機的 SQL Server 執行；整合測試不列入一般 `dotnet test`（以 Trait 區分），由 Codex 在本包與 P3-09 各執行一次並記錄。

## P3-05　有界佇列與背景處理

- **佇列：** `Channel<WebhookEnvelope>`，容量 1,000（可設定），`BoundedChannelFullMode.Wait`，寫入逾時 2 秒，逾時回 503。
- **處理：** N 個處理迴圈，預設 4，以 `hash(userId) % N` 分區，確保同一會友的事件依序處理。
- **每個事件的處理：**
  - 有自己的 `CancellationTokenSource`，逾時 30 秒；
  - `HttpContext` 不得進入佇列，只放已解析的事件與 Channel 識別。
- **停止：** 停止接收 → 等佇列清空，最多 30 秒 → 取消剩餘 → Dispose。
- **測試：**
  - `Queue_Full_Returns503`
  - `SameUser_EventsProcessedInOrder`
  - `DifferentUsers_ProcessedConcurrently`
  - `Shutdown_DrainsWithinTimeout`
  - `Envelope_ContainsNoHttpContext`：以反射檢查型別成員。
  - `ResourceCounts_ReturnToBaseline`

## P3-06　事件分派與處理器移植

- **來源：** `.其他解決方案\LineApiWebhook\LineApiWebhook\` 的 `LineBotApp*.cs`（6 個 partial）、`Handlers\LineBotMessageHandler.cs`、`Processors\WebhookMessageProcessor.cs`、`Crm2011Processor\`。
- **規則位置：**
  - 業務規則進 `GoodShepherd.Core` 對應模組，例如出席進 Attendance、簽到進 Appointments、課程進 Lessons；
  - 主機只有轉接（adapter）。
- **一次一個事件類型：**
  1. 先依 P3-01 的黃金資料寫測試；
  2. 再搬；
  3. 每個處理器標明來源檔與行範圍。
- **語音訊息：**
  - 舊程式把音訊存成 D365 `annotation`，且 `Authorization` 少了 `Bearer ` 前綴；
  - 搬移時修正，並在測試中鎖住正確標頭。
- **不移植：** 舊 `LineMessagePlugIn` 以「姓名＋手機」做身分綁定的流程，設計判定不安全。綁定沿用 ChurchReport 1.5 現行流程。
- **碰到兩份副本邏輯不同時：** 依 00 第 9 節停止條件 4 處理。

## P3-07　關鍵字路由

- 103 個中文關鍵字搬進 `GoodShepherdText.resx`：
  - key 為 `Keyword.{意圖}`，值以 `|` 分隔多個說法；
  - `en` 放既有英文關鍵字 Pray、Report、QR、RPG、ID、Notify。
- 比對方式（完全相符或前綴）沿用舊規則。
- **黃金測試：** `Routing_AllLegacyKeywords_SameIntent`，資料來自 P3-01，必須 100% 相同。
- **其他測試：**
  - `Routing_EnglishKeywordsCaseInsensitive`
  - `Routing_PseudoMember_ChineseKeywordStillWorks`

## P3-08　回覆、letter 紀錄、例外處理

- **回覆：**
  - 用 Reply API；
  - sender 依會友語言與教會；
  - 文字全部經字典，搬入的中文字串抽成 key（設計 12.5 節）。
- **letter 紀錄**（D19）：
  - 入站訊息寫一筆 letter 作為紀錄，**不觸發任何 Plugin**；
  - 階段 5 的 `OutboundLetterPlugin` 以方向欄位只處理外送。
- **例外：**
  - 處理器失敗交給 Hosting 的通知入口，必須傳遞真實的 `Exception`；
  - 不得把錯誤推給開發者個人的 LINE userId，舊程式約 25 處，全部移除。

## P3-09　實機驗收：Webhook（Codex 執行）

Codex 寫 `驗收\P3_Webhook驗收步驟.md`，並依 00 §11 自行執行。**開始前**：本階段前置缺項清單（見本檔開頭）全部到位。秘密只從 User Secrets 讀取，經標準輸入或程序內傳遞，不出現在命令列、檔案、log 或回報（00 §11.3）。

1. 部署到測試 IIS（DMZ 測試主機），設定 HTTPS 憑證（使用既有可用的憑證；需要購買時依 00 §9-1）。
2. 以 Messaging API 把測試 Channel 的 Webhook URL 指向測試主機（`PUT /v2/bot/channel/webhook/endpoint`），再呼叫 Webhook 測試 API，結果須為成功。
3. **中文會友：** 以正確簽章送出 10 個常用關鍵字的模擬事件，檢查主機送出的回覆請求內容（主機送出紀錄）與出席紀錄寫入。模擬事件的 reply token 無效，LINE 回「Invalid reply token」屬預期，不算失敗。
4. **偽語系會友：** 送「主日出席」與「Report」，回覆內容全部有括號。
5. **重送：** 同一 `webhookEventId` 以 `isRedelivery=true` 再送兩次，只處理一次。
6. **驗簽：** 錯誤簽章回 401。
7. **收尾：** 測試資料清除，殘留 0；Webhook URL 保持指向測試主機（測試 channel）。

**完成定義：** 1～7 全部符合。使用者用手機實際打一次關鍵字為選做，不是通過條件。時間盒：1 個工作天。

---

# 階段 4　金流＋幣別

- 估時：原估 6～8 天；依實際速度約 2～3 個工作天。
- 目標：
  - 金流只剩 `SpeechMessage.Payments` 一份；
  - 撥款查詢改由對外待辦執行；
  - 金額帶幣別；
  - 永豐金鑰不再寫死。

## 階段 4 前置缺項檢查（00 §7.3；P4-01 開工時做）

| 項目 | 給哪個 WP | 誰提供 | 核對方式 |
|---|---|---|---|
| 永豐**測試**商店金鑰放進 User Secrets（鍵名由 P4-04 的具名設定檔決定，Codex 在清單中寫明完整鍵名） | P4-04、P4-06 | 使用者（永豐商店後台，00 §9-1） | 只核對鍵存在 |
| 測試主機到永豐測試端點的出站連線 | P4-06 | Codex 自查；需要防火牆出站例外時依 00 §11.2 自行設定，權限不足依 00 §9-1 | 唯讀連線測試，不送交易 |
| P3-09 的 LineWebhook 測試站仍在運作（P4-05 回呼端點掛在這裡） | P4-05、P4-06 | Codex 自查 | 健康檢查端點 |

## 階段 4 工作包總表（3.5，範圍與時間盒同 3.4）

| WP | 名稱 | 依賴 | 實機（Codex） | 時間盒 |
|---|---|---|---|---|
| P4-01 | 盤點金流副本並鎖行為 | — | 無 | 1 天 |
| P4-02 | 金額改用 `Money` | P4-01 | 無 | 半天 |
| P4-03 | 永豐撥款查詢處理器（對外待辦） | P4-01、P2-07 | 無 | 1 天 |
| P4-04 | 金鑰改為具名設定檔（named profile） | P4-01 | 無 | 半天 |
| P4-05 | 回呼端點由 LineWebhook 承載 | P3-02、P4-04 | 無 | 半天 |
| P4-06 | 實機驗收：永豐測試環境 | P4-02～P4-05 | **有** | 半天 |

## P4-01　盤點金流副本

- **盤點對象：**
  - 主方案的 `SpeechMessage.Payments*` 與 ChurchReport `Payments\`；
  - `.其他解決方案\LineApiWebhook\Payment.Infrastructure`；
  - `.其他解決方案\Dynamics365Line` 的 `AllotPlugIn`、`AllotWorkflow`、`AllotTlsWorkflow`（其中 `AllotTlsProcessor.cs:53-58` 有寫死的正式環境金鑰）。
- 列出每個能力（建單、查詢、撥款、回呼驗證、HashCode 計算）在各處的實作與差異。
- **特性測試：**
  - HashCode 計算以固定輸入對照舊程式的輸出，使用測試用假金鑰；
  - 請求與回應的序列化做黃金檔。

## P4-02　金額改用 `Money`

- Payments 的公開模型由 `decimal` 改為 `Money`。
- 舊呼叫點以轉接方法維持相容，標 `[Obsolete]`。
- **測試：**
  - `Money_TwdRoundTrip_NoDecimalLoss`
  - `CreateOrder_UnsupportedCurrency_Rejected`

## P4-03　永豐撥款查詢處理器

- **基底：** 以 `AllotTlsWorkflow` 的邏輯為基底，寫成 `AllotQueryHandler : IOutboundTaskHandler`，放在 Payments（net10）。
- **流程：**
  1. 由 Plugin 或流程建立 `AllotQuery` 待辦；
  2. 處理器查詢永豐；
  3. 回寫收費單。
- 回寫欄位與舊程式一致，以特性測試證明。
- **重試：** 網路錯誤與 5xx 可重試；永豐回覆「查無資料」等業務結果不重試，照實回寫。

## P4-04　金鑰改為具名設定檔

- **移除：**
  - `XKey.config` 的 15 組 `*_XKeyID`；
  - 程式內的 HashCode 金鑰常值；
  - `D:\XKeyConfig\` 檔案讀取。
- **新設定：** `Payments:Sinopac:Profiles:{profileName}`，每間教會在 Tenancy 設定 `SinopacProfile`。
  - 金鑰值由部署環境提供（環境變數或 Windows 認證保管），不進 repo；
  - 測試用假金鑰。
- **測試：**
  - `Profile_Missing_FailsClosed`
  - `Profile_TenantA_CannotUseTenantBProfile`
  - `Source_NoHardcodedKeys`：以 `rg` 規則掃描舊金鑰樣式，結果必須為 0。

## P4-05　回呼端點

- `SpeechMessage.Payments.AspNetCore` 的 ATM 與信用卡回呼端點在 LineWebhook 主機註冊。
- ChurchReport 既有回呼維持運作，直到階段 7 切換完成。
- **測試：**
  - 回呼驗簽；
  - 重複回呼冪等；
  - 金額與幣別一致性檢查。

## P4-06　實機驗收：永豐測試環境（Codex 執行）

在永豐**測試環境**（測試金鑰放 User Secrets；沒有時依 00 §9-1 請使用者提供）：

1. 建立一筆測試收費單並觸發撥款查詢待辦；
2. 回寫結果與舊程式一致；
3. 同一筆再觸發一次，只執行一次；
4. 收尾：測試收費單與待辦刪除，殘留 0。

**完成定義：** 1～4 全部符合；不做任何真實付款。時間盒：半個工作天。

**階段 3～4 共同的完成判準（階段 2 已於 2026-11-09 達成，見 03 檔）：**
- 各 WP 依 00 §7.1 驗收；P3-09、P4-06 實機驗收由 Codex 完成並留紀錄；
- 每個階段結束各做一次外部審查（00 §6.6），沒有未處理的 Critical／High；
- `Find-DuplicateCode` 基準中，與送訊、金流有關的重複項目已移除。

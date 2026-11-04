# 03　階段 2～4：共用送訊與對外待辦、LINE Webhook 主機、金流與幣別

> 三個階段各開一個分支：`1.0.0.11.Integration.P2`、`.P3`、`.P4`。每個階段驗收後才開下一個。

---

# 階段 2　共用送訊（雙目標）＋對外待辦

- 估時：10～13 天（國際化＋2～3 天）。
- 目標：
  - LINE 送訊只剩一份。單人推播放在 `LineMessagingProcessor.Core`（`net462;net10.0`），Plugin 可以直接推播；net462 沙箱的 TLS／推播實機驗收仍須依下方 P2-04 前置完成；
  - `new_linechannel` 加密存放 Token；
  - 對外待辦只處理宮格圖、QR 圖片、撥款查詢三種，可由 D365 建立、由 .NET 10 執行。
- 前提（使用者 2026-11-04 核定）：沙箱對 `api.line.me` 的推播驗收已移出 P0-13，改在本檔「P2-04 前置驗收」完成；未通過不得開始 P2-04 以後的推播工作。
- D365 端的註冊與結構一律由佈署專案執行（D22）；推播相關的步驟加進 `d365\registration\plugins.registration.json`，在 goodshepherdback 以 `smoke` 驗證。

## 階段 2 的 WP 一覽

| WP | 名稱 |
|---|---|
| P2-01 | `SpeechMessage.Hosting`：共用例外通知、啟動檢查、架構測試搬家（R2、R3、R9） |
| P2-02 | `SpeechMessageProducts.GoodShepherd.Core` 骨架（雙目標） |
| P2-03 | Channel Token 加密封包 `ChannelTokenProtector`（雙目標）與寫入工具 |
| P2-04 | `LineMessagingProcessor.Core` 完整版：單人推播、sender、教會檢查、宮格綁定 API |
| P2-05 | Flex 範本引擎與 sender 範本 |
| P2-06 | 合併既有送訊副本（PushUtility、ReplyUtility、LineUtilityClass） |
| P2-07 | 對外待辦：`OutboundTaskWriter`（雙目標）與 `OutboundTaskWorker`（Hosting） |
| P2-08 | 宮格多語：alias 帶語系、只為啟用語系產生（對外待辦處理器） |
| P2-09 | QR 圖片產生（對外待辦處理器） |
| P2-10 | 改字時間輪詢背景服務 |
| P2-11 | 推播整條鏈的骨架驗證（升級 `DiagnosticsPingPlugin`） |
| P2-12 | 使用者驗收 |

## P2-01　`SpeechMessage.Hosting`

- **新增專案：** `SpeechMessage.Hosting`（net10）與 `SpeechMessage.Hosting.Tests`。
- **共用例外通知**（AGENTS.md「永久例外 LINE 通知規則」）：
  - **來源：** 把 ChurchReport 的 `Services\LineExceptionSender.cs` 與 `ToolUtility\Diagnostics\ExceptionOutputOptions.cs` 搬進 Hosting，成為唯一的通知入口。
  - **先鎖行為：** 既有測試 `ChurchReport.MemberInfo.Tests\LineSharedWorkflow\ExceptionOutputOptionsTests.cs` 必須全綠，作為特性測試。
  - **必須補的測試：**
    - `WriteExceptionLog` × `SendLine` 四種組合；
    - 「先寫並 flush `Exception.log` 成功才排入 LINE」；
    - 「寫檔失敗不得送 LINE」；
    - 「LINE 失敗不遞迴通知」；
    - 佇列有上限、滿載時走 stderr；
    - 停止時 drain 並 Dispose；
    - 正常取消（呼叫端 token）不通知；
    - 內部逾時視為失敗。
- **啟動檢查** `StartupGuards`：
  - Development 子集已在 P0-SEC 完成（只能連 goodshepherdback、取不到正式 LINE token）。本 WP 把它搬進 Hosting，讓所有主機共用，並延伸成統一的 Channel 解析器（取代 17 處 `?? "Jesus"`）；搬移時 P0-SEC 的測試必須全綠；
  - .NET 10 不在 ICU 模式就拒絕啟動；
  - 正式環境出現 `qps-ploc` 或 `AllowPseudoLocale=true` 就拒絕啟動；
  - `ReleasedCultures` 含未完成的語系時拒絕啟動。完成度由 I18nCheck 產生的 `CultureCompleteness.g.cs` 提供。
- **架構測試：** 從 `SpeechMessage.Localization.Tests` 搬到 `SpeechMessage.Hosting.Tests\Architecture\`，並補三類規則：
  - **R2：** 主機只有 `Program` 與 `*Registration`、`*Endpoints`；Plugin 組件的公開類別只能是 `IPlugin` 或 `CodeActivity`；
  - **R3：** 原始碼不得出現組織代碼字面值，例如 `"Jesus"`、`"Sunny"`、`"Ymllc"`，白名單只有遷移相容程式並附理由；
  - **R9：** `HttpClient` 只能出現在允許的專案；Plugin 不得參考 `System.Drawing`、`SpeechMessage.Payments`。
- **ChurchReport：** 改呼叫 Hosting 的通知入口。行為不變，以既有測試證明。

## P2-02　`SpeechMessageProducts.GoodShepherd.Core` 骨架

- **專案：** 雙目標，`SpeechMessageStrictCode=true`。
  - 參考 `ToolUtility.Core`、`SpeechMessage.Localization`、`LineMessagingProcessor.Core`、`Line.Messaging`；
  - 不參考任何 net10 專案。
- **模組資料夾**（依設計 4.4 節）：
  - `Attendance`、`WeeklyReports`、`Lessons`、`Appointments`、`QrCodes`、`Care`、`Fees`、`Members`、`Messaging`、`Notifications`；
  - 另有 `Resources\GoodShepherdText.resx` 與產生檔。
- **本 WP 只建立：**
  - `Notifications\MemberNotificationService`：組合「讀會友與 Channel → 教會檢查 → 決定語言 → 組訊息 → 推播」，詳見 P2-04；
  - 各模組的空資料夾與 README.md，說明「階段 5 由哪些舊程式搬入」。
- **測試專案：** `GoodShepherd.Core.Tests`（雙目標）建立，含一個 smoke 測試。

## P2-03　Channel Token 加密封包

**`ChannelTokenProtector`**（雙目標，放在 `LineMessagingProcessor.Core\Security\`），規格見附錄 A.5：
- **演算法：** AES-256-CBC＋HMAC-SHA256，先加密再驗證（Encrypt-then-MAC）；
- **封包格式：** `v1:{base64(iv)}:{base64(ciphertext)}:{base64(mac)}`；
- **金鑰：** 64 bytes，前 32 bytes 加密、後 32 bytes 做 MAC，以 base64 提供；
- **驗證失敗：** 一律丟 `ChannelTokenProtectionException`，訊息為固定文字，**不含任何密文或明文片段**。

**金鑰來源：**
- Plugin 端：推播步驟的 secure configuration，JSON `{"tokenKey":"..."}`；
- 主機端：設定 `LineChannels:TokenKey`。正式環境由部署機器的環境變數或 Windows 認證保管提供，不寫入 `appsettings.json`。

**寫入工具：**
- 佈署專案 `SpeechMessage.Dynamics.Deployment protect-token --channel-id <id>`（v3.2 的 `tools\D365Provisioning` 已併入佈署專案，D22）：
  - Token 與 Channel secret 從標準輸入讀取，不從命令列參數讀，避免進入 shell 歷史；
  - 加密後以 Upsert（替代索引鍵 `new_channelid`）寫入 `new_linechannel`。
- `assign-channel --account <教會ID> --channel-id <id>`：把教會指向 Channel。
- 兩個指令都：
- 只能對白名單組織執行。

**先寫的測試：**
- `RoundTrip_Net462AndNet10_Compatible`：net462 加密、net10 解密，反之亦然；兩個目標共用固定測試向量檔。
- `Tampered_Ciphertext_Throws`、`Tampered_Mac_Throws`
- `WrongKey_Throws`、`UnknownVersion_Throws`
- `Exception_NeverContainsSecret`
- `Mac_CompareIsConstantTime`：以程式碼審查加測試確認使用固定時間比較。

## P2-04 前置驗收：沙箱對 LINE 的測試推播

**使用者 2026-11-04 核定：** 從 P0-13 原第 6 步移至此處。在 P2-04 開工前由使用者操作，Codex 只準備步驟；P0-13 不需要 LINE channel 或以下兩個 secrets。

1. 確認 goodshepherdback 已完成原安全清理，骨架 trace 驗收通過。準備**測試 channel** 與**使用者自己的測試帳號**，在 Deployment 同組織 User Secrets 加入 `TestChannelToken`、`TestLineUserId`；不使用正式 channel，不把值寫入 repo／命令列／回報。
2. 開追蹤「全部」，清單保持同一個 DiagnosticsPingPlugin、同一個步驟身份、enabled=true；將 unsecureConfig 設為 `{"mode":"push","culture":"zh-TW","lineUserId":"${secret:TestLineUserId}"}`，secureConfig 為 `${secret:TestChannelToken}`。只保存佔位名稱。
3. 執行 `dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- plan --org goodshepherdback`，審核只有預期的設定／啟用變更；以同一入口 `apply --org goodshepherdback --plan <剛審核的plan.json>` 套用，再 `verify --org goodshepherdback`。含秘密的 verify=3 表示 metadata 相同但秘密未比對，不能稱完整成功；verify=1 則停下處理差異。工具不偵測同模板的秘密輪替，只改 User Secrets 不保證已更新部署秘密，需輪替時另行核定。
4. 同一入口執行 `smoke --org goodshepherdback --scenario p0-ping-push`。手機實收一則「P0 推播測試 …」，smoke=0，追蹤有 Outcome=Accepted、Status=200、有效 RequestId（x-line-request-id），沒有 token、內文或 lineUserId；確認測試 task 已刪除。至此才成立沙箱可直接推播的前提。
5. 成功或失敗都改回 trace、secureConfig 空白、enabled=false，重新 plan → 審核 → apply → verify=0；追蹤改回「例外狀況」，確認資料清理。不能連 `api.line.me` 時請使用者確認防火牆／Proxy，未通過前不得開始 P2-04 推播工作。

**重送依據（使用者 2026-11-04 核定）：** 不另做 P0 實機重送；以「Retry Key＝來源記錄 ID」單元測試及 LINE 官方 24 小時有效期內同 Retry Key 抑制重複受理為據。重試內容、收件者維持一致；此契約不保證送達，也不涵蓋超過有效期的重送。來源：[LINE 官方重試說明](https://developers.line.biz/en/docs/messaging-api/retrying-api-request/)。

## P2-04　`LineMessagingProcessor.Core` 完整版

**分工：**
- `LineMessagingProcessor.Core` 只做傳輸、組 sender、Retry Key、教會一致性檢查；
- 讀 D365 資料（會友、教會、Channel）由 `ToolUtility.Core` 的查詢負責；
- 兩者由 `GoodShepherd.Core` 的 `MemberNotificationService` 組合。

**新增或擴充：**
- `PushToMemberAsync(MemberRecipient recipient, ChurchChannel channel, Guid sourceChurchId, IReadOnlyList<ISendMessage> messages, Guid retryKey, CancellationToken ct)`：
  - `MemberRecipient`：ContactId、LINE userId、所屬教會 ID、語言；
  - `ChurchChannel`：教會 ID、該教會被指派的 ChannelId、已解密的憑證、sender 名稱；
  - **一個 Channel 可以由多間教會共用**（設計 6.5 節的 Channel 池，例如一個 Channel 約 125 間教會），所以是教會指向 Channel：`Account.new_linechannelid`。Channel 本身不屬於某一間教會；
  - **教會檢查：** 兩個條件都要成立，任一不符就回傳 `Rejected(ChurchMismatch)`，**不呼叫 LINE**：
    - `recipient.ChurchId == sourceChurchId`；
    - `channel` 是 `sourceChurchId` 這間教會的 `new_linechannelid`。
  - 這點修正了 Word 08 圖「new_linechannel 所屬教會」的寫法，以附錄 A.3 為準。
- 訊息型別改用 `Line.Messaging`（已雙目標）。
- `LinkRichMenuToUserAsync(...)`、`UnlinkRichMenuAsync(...)`：會友改語言時直接換綁宮格用。
  - 只呼叫 LINE 的綁定 API，不產生圖片；
  - alias 不存在時回傳 `Permanent(AliasNotFound)`。
- **`ToolUtility.Core\LineRecipients\LineRecipientQueries`：**
  - 以 ContactId 讀 `new_lineid`、所屬教會、`new_preferredlanguage`；
  - 以教會 ID 讀 `Account.new_linechannelid`，再讀該 `new_linechannel`。
  - 只取需要的欄位，用 `ColumnSet`，不用 AllColumns。
- **`MemberNotificationService.NotifyAsync(...)`：**
  1. 讀資料；
  2. 會友沒有 LINE userId → 回傳 `Rejected(NotBound)`；
  3. 決定語言（P1-05）；
  4. 以字典組訊息；
  5. 推播。

**先寫的測試：**
- `Push_ChurchMismatch_RejectedWithoutHttp`：會友教會不符、Channel 不是該教會指派的，各一個。
- `Push_SharedChannel_TwoChurches_EachUsesOwnSender`：兩間教會共用同一 Channel，sender 名稱各自正確。
- `Notify_MemberNotBound_ReturnsNotBound`
- `Notify_UsesMemberLanguage_ZhTwAndPseudo`
- `Notify_RetryKeyIsSourceRecordId`
- `Notify_ColumnSetIsMinimal`：假 `IOrganizationService` 斷言查詢的欄位清單。
- `LinkRichMenu_AliasNotFound_Permanent`
- `Notify_ParallelDifferentChurches_NoCrossTalk`

## P2-05　Flex 範本引擎與 sender 範本

- **Flex 範本：** JSON 檔放在各產品 `Resources\Flex\*.json`，以嵌入資源編進組件。
  - 文字位置寫 `{{t:Key}}`，資料位置寫 `{{v:name}}`（設計 6.4 節）；
  - 填入時做 JSON 字串跳脫；
  - 每個範本都必須有 `altText` 的 key。
- **sender 範本：** `Line.Sender.Template`，例如 `{churchName}小幫手`。
  - 由送訊端依「發送教會」產生，呼叫端不能指定；
  - 超過 20 字時依序改用 `Brand.ShortName`；仍超過就丟例外，不送出。
- **引擎位置：** 放在 `LineMessagingProcessor.Core\Templates\`，雙目標。
  - 依賴 `SpeechMessage.Localization` 的 `ITextCatalog`；
  - 共用核心層內部，`Localization` 是最底層，其他共用核心專案可以參考它。

**先寫的測試：**
- `Fill_TextAndValuePlaceholders`
- `Fill_ValueWithQuotesAndNewlines_EscapedValidJson`
- `Template_MissingAltText_Throws`
- `Sender_LongChurchName_UsesShortName`、`Sender_StillTooLong_Throws`
- `Fill_PseudoLocale_AllTextWrapped`
- 每個範本一個黃金檔測試。

## P2-06　合併既有送訊副本

1. **盤點：** 以 `rg "class PushUtility|class ReplyUtility|class LineUtilityClass"` 在主方案與兩個舊方案中盤點。設計記錄為 PushUtility 5 份、ReplyUtility 4 份、LineUtilityClass 3 份。每一份的公開方法列成對照表。
2. **以最完整的一份為基底（R5）：**
   - 推播類方法改呼叫 `LineMessagingProcessor.Core`；
   - Reply、多則訊息、內容下載保留在 `LineMessagingProcessor`（net10）。
3. **主方案內的呼叫點**（ChurchReport 的 `Tools\LineUtilityClass.cs`、`ToolUtility\PushUtility.cs` 等）改用新入口。舊類別：
   - 先保留為「轉呼叫新入口」的薄殼，標 `[Obsolete]`；
   - 階段 6 再刪除。

**先寫的測試：**
- 搬移前先寫特性測試：每個公開方法以假 handler 記錄送出的 HTTP 請求，包括 URL、標頭（Token 以遮罩比對）、body，存成黃金檔。
- 改為新入口後，黃金檔必須相同。例外是**刻意**加上的 `X-Line-Retry-Key` 與 `sender`，在回報中逐項說明。

## P2-07　對外待辦（`new_outboundtask`）

- **欄位：** 見附錄 A.4。
- **類型只有三種：** `RichMenuGenerate`、`QrImageGenerate`、`AllotQuery`（設計 10.6 節）。
- **`OutboundTaskWriter`**（雙目標，放在 `GoodShepherd.Core\Outbound\`）：
  - `Enqueue(type, churchId, contactId?, idempotencyKey, payload)`；
  - payload 只能放 ID 與數值，序列化前以白名單型別檢查；
  - 以替代索引鍵 Upsert，同一個冪等鍵只有一筆。
- **`OutboundTaskWorker`**（Hosting，`BackgroundService`）：

| 項目 | 規格 |
|---|---|
| 輪詢 | 每 5 秒，每批最多 50 筆，`statuscode=待處理`，且 `new_nextattempton <= UtcNow` 或空值 |
| 認領 | `UpdateRequest` 設 `ConcurrencyBehavior = IfRowVersionMatches`，把狀態改「處理中」並寫 `new_claimedon`；失敗（版本不符）代表別人已認領，略過 |
| 執行 | 依類型交給 `IOutboundTaskHandler`；每筆以教會 ID 取設定，不在批次之間共用教會資料 |
| 重試 | 逾時、429、5xx 以指數退避 1、2、4、8、16 分鐘，最多 5 次；超過改「失敗」，寫 `Exception.log` 後通知 |
| 卡住 | 「處理中」超過 10 分鐘視為卡住，可重新認領 |
| 停止 | 主機停止時取消輪詢，等目前這一批完成，最多 30 秒，之後放棄並保持「處理中」，交給下一次重新認領 |
| 保留 | 完成 30 天、失敗 90 天。以 D365 大量刪除工作清除，設定步驟寫進驗收文件 |

**先寫的測試：**
- `Enqueue_SameIdempotencyKey_SingleRecord`
- `Enqueue_PayloadWithText_Rejected`
- `Claim_TwoWorkersParallel_OnlyOneExecutes`：假 `IOrganizationService` 模擬 RowVersion 衝突。
- `Retry_429_BackoffSchedule`
- `Retry_ExceedsMax_FailedAndNotified`
- `StaleProcessing_Reclaimed`
- `Shutdown_DrainsCurrentBatch_WithinTimeout`
- `Worker_ResourceCountsReturnToBaseline`

## P2-08　宮格多語（對外待辦處理器）

- **alias 格式：** `{教會代碼}-{組合}-{頁籤}-{語系}`，只用英數、底線、連字號，≤ 32 字（設計 6.5 節）。
  - `qps-ploc` 縮寫為 `qps`。
- **`RichMenuGenerateHandler`：**
  - 只為「教會已啟用」的語系產生 8 張；
  - 圖面文字從字典取，含教會覆寫；
  - 用 ImageSharp 合成，這部分在 `LineMessagingProcessor.RichMenus`（net10）；
  - `chatBarText` ≤ 14 字。
- **容量：** 每個 Channel 1,000 張；產生前檢查剩餘容量，不足時失敗並通知。
- **現有 RichMenus 專案：** 以既有 `LineMessagingProcessor.RichMenus.Tests` 為特性測試基礎，擴充多語。

**先寫的測試：**
- `Alias_Format_MaxLength32`
- `Generate_OnlyEnabledCultures`
- `Generate_CapacityInsufficient_FailsBeforeUpload`
- `ChatBarText_TooLong_Rejected`
- `Generate_PseudoLocale_AliasUsesQps`

## P2-09　QR 圖片產生（對外待辦處理器）

- **QR 內容編碼：** 放在 `GoodShepherd.Core\QrCodes`（雙目標）。產生端與 ChurchReport 掃碼端共用同一份，階段 6 讓 ChurchReport 改用。
- **圖片：** 由 net10 的處理器以 ZXing.Net＋ImageSharp 產生。
- **測試：**
  - 內容編碼與解碼黃金測試；
  - 產生的圖片能被 ZXing 解回同一內容。

## P2-10　改字時間輪詢背景服務

- **`TextChangePollingService`**（Hosting）：
  - 每 30 秒查詢 `new_textchangedon > 上次查詢時間 − 2 分鐘` 的教會；
  - 只讓那幾間教會的快照失效（P1-08）。
- **測試：**
  - `Poll_OnlyChangedChurchesInvalidated`
  - `Poll_D365Unavailable_KeepsSnapshotsAndRetries`
  - `Poll_OverlapWindow_NoMissedChange`

## P2-11　推播整條鏈的骨架驗證

**升級 `DiagnosticsPingPlugin`**，新增 `mode=member`：
1. 觸發記錄為 `task`，以其「關於」欄位指向一位 Contact；
2. 呼叫 `MemberNotificationService` 走完整條鏈：讀 Channel 並解密、教會檢查、決定語言、範本、推播；
3. Retry Key＝task ID。

**測試：** 在 `Plugins.Tests` 以假服務跑完整條鏈。
- 正常；
- 教會不符；
- 未綁定；
- 偽語系。

> 設計 15 章的完成判準寫「建立一筆 LINE 活動」。這裡改用 `task` 觸發診斷 Plugin 驗證同一條鏈；真正的 LINE 活動 Plugin（`OutboundLetterPlugin`）在階段 5 搬移。

## P2-12　使用者驗收

**Codex 先寫好** `驗收\P2_推播鏈驗收步驟.md`，內容如下：
1. 以 `protect-token` 寫入測試 Channel。
2. 測試會友 A 的偏好語言設為 `qps-ploc`。
3. 建立 task 並關聯會友 A，60 秒內手機收到偽語系訊息。
4. 改關聯到「別間教會的會友」，不會收到，追蹤記錄寫明教會不符。
5. 系統作業重跑，不會收到第二則。
6. 手動建立一筆宮格產生待辦，兩台主機（或兩個程序）同時執行，只產生一次。

---

# 階段 3　LINE Webhook 主機（`SpeechMessageProducts.LineWebhook`）

- 估時：12～17 天。
- 目標：LINE 入站全部在 .NET 10。
  - 驗簽、去重、先回 200、有界佇列、事件分派；
  - 關鍵字依語系路由；
  - 回覆依會友語言。
- 不再使用 `.其他解決方案\LineApiWebhook` 的主機。

## 階段 3 的 WP 一覽

| WP | 名稱 |
|---|---|
| P3-01 | 舊行為的黃金資料：103 個關鍵字、事件樣本、回覆內容 |
| P3-02 | 主機專案與端點（只組裝） |
| P3-03 | 驗簽 |
| P3-04 | 去重（平台 SQL，決定 D16） |
| P3-05 | 有界佇列與背景處理（依 userId 分區保序） |
| P3-06 | 事件分派與處理器移植 |
| P3-07 | 關鍵字路由改用 `IKeywordCatalog` |
| P3-08 | 回覆、letter 紀錄（D19）、例外處理 |
| P3-09 | 使用者驗收 |

## P3-01　舊行為的黃金資料（先做，其他 WP 都依賴它）

- **新增** `tools\LegacyGolden\`（net10 console，不進 .sln）。
  - 先用 P0-05 的轉換工具把舊檔轉成 UTF-8 到暫存區，不修改 `.其他解決方案` 原檔；
  - 讀取 `.其他解決方案\LineApiWebhook` 的 `MessageRoutingKeywords.cs`（以 Glob 尋找確切路徑，該檔是 CP950，約 587 行）；
  - 編譯一個只含關鍵字表與比對函式的 harness，輸出 `Golden\Keywords\routing.json`：每個關鍵字與幾個變形（全形、前後空白、大小寫）→ 意圖。
- **事件樣本：**
  - 依 LINE 官方 Webhook 文件格式建立 12 種事件的樣本 JSON：message（text、image、audio、location、sticker）、follow、unfollow、join、leave、postback、accountLink、memberJoined；
  - 放在 `SpeechMessageProducts.LineWebhook.Tests\Golden\Events\`；
  - 若使用者能提供遮罩過的真實錄製資料，優先使用。
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
- **停止條件：** 使用者沒有提供平台 SQL。不得改用記憶體去重上正式環境。記憶體實作只給測試用。
- **測試：**
  - `Dedupe_SameEvent_SecondIsDuplicate`
  - `Dedupe_SqlTimeout_Returns503`
  - `Dedupe_SameEventIdDifferentChannels_NotDuplicate`
  - `Cleanup_DeletesOnlyOlderThan7Days`
  - SQL 實作的整合測試：以 LocalDB 或 Docker 的 SQL Server 執行；沒有環境時標記 Skip，並在回報說明。

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

## P3-09　使用者驗收

**Codex 寫好** `驗收\P3_Webhook驗收步驟.md`：
1. 部署到測試 IIS（DMZ），設定憑證。
2. 在 LINE Developers 後台把測試 Channel 的 Webhook URL 指向測試主機，按 Verify。
3. **中文會友：** 逐一打 10 個常用關鍵字，回覆正確、出席紀錄寫入。
4. **偽語系會友：** 打「主日出席」與「Report」，回覆全部有括號。
5. **重送：** 在後台開啟 redelivery，暫停主機 1 分鐘再恢復；重送的事件只處理一次。
6. **驗簽：** 用錯誤簽章送一次，主機回 401。

---

# 階段 4　金流＋幣別

- 估時：6～8 天。
- 目標：
  - 金流只剩 `SpeechMessage.Payments` 一份；
  - 撥款查詢改由對外待辦執行；
  - 金額帶幣別；
  - 永豐金鑰不再寫死。

## 階段 4 的 WP 一覽

| WP | 名稱 |
|---|---|
| P4-01 | 盤點金流副本並鎖行為 |
| P4-02 | 金額改用 `Money` |
| P4-03 | 永豐撥款查詢處理器（對外待辦） |
| P4-04 | 金鑰改為具名設定檔（named profile） |
| P4-05 | 回呼端點由 LineWebhook 承載 |
| P4-06 | 使用者驗收 |

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

## P4-06　使用者驗收

在永豐**測試環境**：
1. 建立一筆收費單並觸發撥款查詢待辦；
2. 回寫結果正確；
3. 同一筆再觸發一次，只執行一次。

**階段 2～4 共同的完成判準：**
- 各 WP 通過驗證並提交，使用者驗收成功；
- 外部審查沒有未處理的 Critical／High；
- `Find-DuplicateCode` 基準中，與送訊、金流有關的重複項目已移除。

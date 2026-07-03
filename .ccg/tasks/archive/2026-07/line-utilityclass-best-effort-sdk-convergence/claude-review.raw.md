已完成程式碼比對與架構調查（含 `ILineNotificationWorkflow` / `LineNotificationWorkflow` 實作、DI 註冊狀況、既有呼叫端），以下為審查報告。

---

# LINE Workflow 收斂審查報告（LineUtilityClass.cs）

> **附註**：題目附上的 diff 文字與目前 worktree 中的實際 `git diff` 略有出入（實際版本另外多了 `m_CreatePushLineMessage` 委派欄位注入、第 4 個建構子多了 `createPushLineMessage` 參數、以及 BOM 移除)。以下審查以**目前 worktree 的真實 diff**為準，以確保結論可對應實際程式碼。

## 🔴 Critical（無）
未發現會導致崩潰或訊息誤送的嚴重錯誤。`SendBestEffortSdkMessagesAsync` 在 `m_LineNotificationWorkflow` 為 `null` 時完整 fallback 回原本的 `PushMessageAsync` 呼叫，行為與重構前一致；有注入時才走新路徑，屬安全的漸進式收斂。

## 🟡 Warning

1. **多組織 Channel Token 切換（`SetupChannelAccessToken`）在未來接上 workflow 後會被靜默架空**
   `SetupChannelAccessToken` 依 `m_OrganizationName`（"jesus"/"jesusback"）重新產生 `m_ChannelAccessToken` 並重建 `m_LineMessagingClient`。但一旦 `m_LineNotificationWorkflow != null`，所有 `SendBestEffortSdkMessagesAsync` 呼叫都改走 `m_LineNotificationWorkflow.SendAsync(...)`，完全不經過 `m_LineMessagingClient`。而共用 workflow 背後的 `LineNotificationWorkflow`／`LineMessagingProcessorClass` 是在別處（如 `PaymentNotificationService.CreateDefaultLineNotificationWorkflow`）用**單一、建構時決定**的 token 建立的，與 `LineUtilityClass` 執行期動態切換的 `m_OrganizationName` 完全脫鉤。
   目前因為**沒有任何生產程式碼會把 workflow 注入 `LineUtilityClass`**（見下方 Info-2），這暫時是死路徑，不影響現況；但這是留給下一步收斂工作的地雷——若之後有人直接把 `ILineNotificationWorkflow` 接進使用 `SetupChannelAccessToken` 的呼叫端，多組織 token 切換會悄悄失效且不會有任何錯誤提示。建議在正式接線前，於 `design.md` 或程式碼註解中明確記錄這個邊界限制。

2. **測試覆蓋率偏薄，僅涵蓋 `SendMessage(List<ISendMessage>)` 一個代表案例**
   這次改動把 10 個以上的公開方法（`SendMessageAsync`、`SendImage`、`SendVideo`、`SendAudeo`、`SendLocation`、`SendSticker`、兩個 `PostSerializedTemplate`、`PostSerializedFlex`、`PostSerializedConfirm`、`PostSerializedImageMap`）全部導向同一個 `SendBestEffortSdkMessagesAsync`，但 `LineUtilityClassWorkflowTests.cs` 只測了 `SendMessage`。雖然邏輯集中在單一 private helper、風險有限，但每個呼叫點各自傳入不同的 `source` 字串（用於追蹤／觀測），這部分完全沒有測試斷言，任何複製貼上打錯字（例如下一點提到的 `SendAudeo`/`SendAudio` 不一致）都不會被 CI 抓到。

3. **`SendAudeo` 方法名稱與新加入的 `source` 標籤不一致**
   既有方法名維持拼字錯誤 `SendAudeo`（非本次改動引入），但新加入的追蹤字串卻寫成 `"ChurchReport.LineUtilityClass.SendAudio"`。功能上無影響，但日後若要用這個 `source` 做故障排查／統計聚合，容易讓人誤以為程式碼裡有對應的 `SendAudio` 方法，建議統一（例如改用 `nameof(SendAudeo)` 或至少讓字串與現有拼字一致）。

## 🔵 Info

1. **產品端 CRM／統計語意確實留在 ChurchReport，邊界守得住**
   所有改動點都維持先呼叫 `m_CreatePushLineMessage(UserId, "Line推播統計:...", ...)`（即 `ToolUtilityClass.CreatePushLineMessage`，寫入 CRM 統計）再呼叫 `SendBestEffortSdkMessagesAsync`。共用的 `LineMessagingProcessor.Workflows` 專案（`LineNotificationWorkflow.cs` 註解也明講「CRM 更新、付款語意與會員資料不允許放進這裡」）完全不知道 ChurchReport 的統計/CRM 概念，只負責訊息驗證、組裝、送出。符合題目要求的依賴邊界。

2. **目前對生產環境是零行為風險——沒有任何呼叫端真的注入 workflow**
   全庫搜尋 `new LineUtilityClass(` 只有 `LineUtilityClassWorkflowTests.cs` 這一處會傳入非 null 的 `ILineNotificationWorkflow`；`Startup.cs` 也沒有把 `ILineNotificationWorkflow` 註冊進 DI 容器供 `LineUtilityClass` 使用。也就是說，這次改動在正式站台上目前完全等效於重構前（永遠走 fallback 分支），是安全的純新增基礎設施（符合 requirements.md 明說的「Do not remove the legacy API in this slice」）。

3. **`SendAsync` 是 best-effort（吞例外），與舊行為有語意差異，但屬設計內**
   `LineNotificationWorkflow.SendAsync` 會把所有例外（`LineResponseException`/`HttpRequestException`/`TaskCanceledException`/其他）包成 `LineNotificationResult.Failure` 回傳、不拋出；而重構前多數 `Send*` 方法（如 `SendImage`、`SendSticker` 等）本身沒有 try/catch，例外會直接往外拋給呼叫端。一旦 workflow 被接上，這些方法的失敗會變成「靜默吞掉」而非讓呼叫端感知。這符合 `ILineNotificationWorkflow` 文件註解裡「SendAsync 給可容忍失敗的流程，SendOrThrowAsync 給付款指示等必須浮出錯誤的流程」的設計原則，`LineUtilityClass` 這批方法本來就屬於「可容忍失敗」的類別（frontend/會友通知），所以目前的選擇是合理的；只是提醒未來若有任何呼叫端依賴「例外會往外拋」做重試或告警，需要意識到這個行為轉變。

4. **`Dispose()` 未處理 `m_LineNotificationWorkflow`——正確**
   `Dispose(bool)` 只釋放 `m_ToolUtilityClass`、`m_LineMessagingClient`、`m_ReplyUtility`，未釋放注入的 workflow。這是對的，因為 workflow 是外部（DI／呼叫端）擁有的相依，`LineUtilityClass` 不應該替它管生命週期。

5. **建構子新增 `ArgumentNullException` 屬相容性改善**
   兩個新建構子都對 `aToolUtilityClass`（及新增的 `lineMessagingClient`）做 null 檢查並提早拋錯，原本的參數化建構子沒有這道防線。這是把潛在的延遲 NRE 提前成清楚的建構期例外，屬正向相容性改善，不影響既有合法呼叫。

---

**總結**：這次收斂改動整體是安全、漸進、邊界清楚的基礎設施擴充，目前對生產行為零影響。主要需要留意的是「多組織 token 切換」與「workflow 注入」這兩條路徑尚未互相考慮過（Warning 1），建議在後續把 `ILineNotificationWorkflow` 真正接進任何仍依賴 `SetupChannelAccessToken` 的呼叫端之前，先解決或明確記錄這個限制；測試覆蓋也建議至少再補 1–2 個非文字類訊息（如 `SendImage`）的 `source` 標籤斷言，降低複製貼上打錯字的風險。

---
SESSION_ID: e15509f8-9b21-4315-8420-96d4fcb40b2b

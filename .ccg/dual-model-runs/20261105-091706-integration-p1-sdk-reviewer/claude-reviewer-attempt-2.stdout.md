# P1-03 LINE SDK 唯讀 Code Review 報告

**審查範圍**：Line.Messaging / Line.Messaging.Tests 全部來源（已讀完整檔案，非僅 diff），基準固定於 cb932c01，涵蓋已 commit 與未 commit 變更。未做任何檔案修改、未呼叫額外模型。

---

## Critical 🔴

### 1. 新增的「原始 bytes 固定時間 HMAC」驗簽從未接入實際 Webhook 進入點——production 仍在用舊版、以 string 往返的驗簽

- **檔案**：`Line.Messaging/Webhooks/WebhookRequestMessageHelper.cs:40-89`、對照 `Line.Messaging/LineSignature.cs`（全檔）
- **可重現原因**：
  - 全репо `grep LineSignature\.` 只命中 `Line.Messaging.Tests/LineSdkFoundationTests.cs`（測試本身）與 `LineSignature.cs`（定義本身）。沒有任何 production 程式碼呼叫 `LineSignature.Verify`。
  - 真正公開且會被消費端呼叫的 Webhook 進入點是 `WebhookRequestMessageHelper.GetWebhookEventsAsync(this HttpRequestMessage request, ...)`（第 35 行），這是 SDK 對外唯一的簽章驗證 extension method，repo 內外都沒有其他 production 呼叫點被改寫成呼叫新類別。
  - 該方法內部仍是舊邏輯：第 40 行 `var content = await request.Content.ReadAsStringAsync();` 先把 body 解碼成 .NET `string`，再在 `VerifySignature`（第 71-89 行）用 `Encoding.UTF8.GetBytes(requestBody)` **重新編碼**成 bytes 才算 HMAC，而不是對 LINE 實際送上來的原始 bytes 做 HMAC。
  - 這正是本次任務明確要求檢查的項目（「原始 bytes HMAC 固定時間」）。新寫的 `LineSignature.Verify(byte[] body, ...)` 設計上就是為了避免 string 往返造成的位元組失真與格式攻擊面，但它是**死碼**——完全沒有被接上這條唯一的生產路徑。只要 Content-Type 的 charset 被中介層改寫、body 內含 BOM 或任何無法在 UTF-8 decode→encode 往返中保持位元組一致的情況，`VerifySignature` 算出的 HMAC 就會跟 LINE 原始簽章不符，造成合法 webhook 被拒（可用性問題），而這條新增的、理論上更穩健的驗簽實作完全沒有機會發揮作用。
  - `VerifySignature` 本身的 `SlowEquals`（第 96-102 行）雖然也是固定時間比較，邏輯跟新 `LineSignature` 類似，但輸入源頭（string 而非 raw bytes）這個根本問題沒有被这次 P1-03 的工作修正到實際呼叫點。
- **最小修正**：讓 `WebhookRequestMessageHelper.GetWebhookEventsAsync` 改讀 `await request.Content.ReadAsByteArrayAsync()` 取得原始 bytes，呼叫 `LineSignature.Verify(bodyBytes, xLineSignature, channelSecret)` 做驗簽，驗簽通過後才用同一份 bytes（或由其解碼出的字串）做 JSON 反序列化；同時可以刪除或標記 `VerifySignature`/`SlowEquals` 為 obsolete，避免兩套驗簽邏輯並存造成未來再次分岔。

---

## Warning 🟡

### 2. 測試專案目標框架與產品框架不一致，無法驗證「net462 相容」的宣稱

- **檔案**：`Line.Messaging.Tests/Line.Messaging.Tests.csproj:3`（`<TargetFrameworks>net48;net10.0</TargetFrameworks>`）對照 `Line.Messaging/Line.Messaging.csproj:4`（`<TargetFrameworks>net462;net10.0</TargetFrameworks>`）
- **可重現原因**：產品 SDK 的 .NET Framework 目標是 **net462**，但測試專案的 .NET Framework 目標是 **net48**。任務證據所稱「net48 82/82」其實是用 net48 測試宿主載入 net462 建置出來的 `Line.Messaging.dll` 去跑測試，並非在 net462 執行環境下實跑。`LineSdkFoundationTests.Assembly_TargetFramework_MatchesProduct` 也只驗證組件的 `TargetFrameworkAttribute` metadata 字串等於 `.NETFramework,Version=v4.6.2`，這只是讀 DLL 的 metadata，並不代表測試「在」net462 runtime 上執行。net48 對 net462 一般是前向相容，但兩者在 BCL 預設值（例如 TLS 預設、`ServicePointManager`、某些 Reflection/Culture 行為）上仍有差異，任何僅在 net462 runtime 才會出現的相容性問題，這套測試矩陣不會被觸發/捕捉到，與任務要求「雙目標 net462/net10 相容」的驗證意圖有落差。
- **最小修正**：把 `Line.Messaging.Tests.csproj` 的 `<TargetFrameworks>` 改成 `net462;net10.0`，與產品專案一致；如果因為測試 SDK/工具鏈在 net462 上有相容性問題才改成 net48，應在 csproj 加註解說明原因並記錄此已知落差。

### 3. `BorrowedStreamContent` 在 RichMenu 圖片上傳時被重複 Dispose 兩次

- **檔案**：`Line.Messaging/LineMessagingClient.cs:2165-2184`（`UploadRichMenuJpegImageAsync` / `UploadRichMenuPngImageAsync`）
- **可重現原因**：
  ```csharp
  using var content = new BorrowedStreamContent(stream);
  content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
  using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, ...) { Content = content }).ConfigureAwait(false);
  ```
  `SendAsync`（`LineMessagingClient.cs:2846-2855`）內部是 `using (request) { ... return await _client.SendAsync(request, completion)...; }`，當 `SendAsync` 回傳前，`request.Dispose()` 會被呼叫；`HttpRequestMessage.Dispose()` 的標準實作會連帶呼叫 `request.Content.Dispose()`，也就是把 `content`（同一個 `BorrowedStreamContent` 實例）disposed 一次。等呼叫端方法結束時，外層 `using var content` 又會對同一個物件呼叫一次 `Dispose()`。
  目前 `BorrowedStreamContent.Dispose(bool)`（`BorrowedStreamContent.cs:45-49`）沒有自己的已釋放旗標、每次只是把 `_stream = null`，所以這次重複呼叫目前是良性的 no-op，不會丟例外或造成外部可觀察的錯誤；但這是脆弱的資源所有權設計——一旦未來這個類別或其 `base.Dispose` 行為變得非冪等（例如加計數器、丟例外、通知事件等），就會在這兩個上傳方法上出事。這跟任務要求檢查的「所有 request/response/content/stream 所有權」「Dispose races」直接相關。
- **最小修正**：拿掉外層 `using var content`（改成普通 `var content = new BorrowedStreamContent(stream);`），把 content 的生命週期完全交給 `request`/`SendAsync` 管理，避免雙重 Dispose；或者讓 `SendAsync` 不要用 `using(request)` 自行 Dispose，而由呼叫端統一管理（但這會改動所有呼叫點，風險較大，建議採前一個較小改法）。

---

## Info 🟢

### 4. `Sender` 例外訊息語言與專案繁中慣例不一致

- **檔案**：`Line.Messaging/Messages/Sender/Sender.cs:16-19`
- **說明**：目前原始碼中 `ArgumentException` 訊息為英文（"Sender name must contain 1 to 20 UTF-16 code units."、"Sender icon must be an absolute HTTPS URL."），而本次任務提供的固定基準 diff 顯示同一段建構子原本是繁體中文訊息（「發送者名稱必須為一至二十字。」等），且本檔案與其他新增檔案的 XML 文件註解全部維持繁體中文慣例。純屬訊息語言一致性問題，非功能缺陷，但與任務「所有修改區繁中」的慣例要求有落差，建議之後統一。

---

## 測試覆蓋說明（非缺陷，供脈絡參考）
- 六個 Golden JSON 檔（`Line.Messaging.Tests/Golden/*.json`）經位元組級檢查，工作目錄內容（供 `EmbeddedResource` 編譯使用）六個檔案結尾皆為 `\r\n`，與 `LineSdkGoldenTests` 的 `Assert.EndsWith("\r\n", expected)` 前提一致；git 物件本身雖因 `.gitattributes`/`core.autocrlf` 正規化而不含結尾換行，但那只影響 repo 儲存層，不影響測試實際讀到的 bytes——此點**不是缺陷**，特此排除。
- `ContentStream` 的 responseOwner 一次性轉交與 `_disposeGate` 鎖、`LineMessagingClient.SendAsync`/`Dispose` 以 `Interlocked.Exchange`/`Volatile.Read` 做的 credential 隔離與冪等釋放、`LineSignature.Verify` 本身（孤立看）的格式檢查順序與固定時間比較、`WebhookEvent`/`DeliveryContext` 對缺欄位事件的預設值處理，均逐行覆核未發現功能缺陷。

## 總結
1 項 Critical（新硬化驗簽邏輯未接上真正的 webhook 入口，形同死碼，現行生產路徑仍用舊的 string 往返驗簽）、2 項 Warning（測試/產品 TFM 不一致、上傳路徑的 content 重複 Dispose）、1 項 Info（例外訊息語言不一致）。建議在 P1-03 關卡前至少解決 Critical 項目，否則本次驗簽硬化工作對實際 webhook 流量沒有任何效果。

---
SESSION_ID: ab4e8ce2-a66e-43c4-b5d6-b678d80e1918

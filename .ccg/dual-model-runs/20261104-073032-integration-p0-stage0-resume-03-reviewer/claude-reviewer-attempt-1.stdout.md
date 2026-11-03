# 審查報告：LineMessagingProcessor.Core（階段0 第3批）

**審查範圍**：`73b6e6cf4..ae29cb40a` 內 `LineMessagingProcessor.Core/*`、`LineMessagingProcessor.Tests/LinePushClientTests.cs` 與相關 csproj／spec 變更。
**審查方式**：純靜態閱讀＋本地檢索（`git log`、`grep`、`Read`），未執行任何建置／測試／網路連線，未讀取遮罩欄位之外的機密內容。本批獨立完成，不代表同批其他分份（Plugin／net10 host／其他）之結論。

## Critical 🔴
無。針對專項逐一核對結果：

- **每次呼叫獨立 credential/request/result**：`PushAsync`（LinePushClient.cs:60-113）每次建立自己的 `HttpRequestMessage`／`CancellationTokenSource`，未寫入任何 `static`／快取欄位，確認成立。
- **回應與取消資源釋放**：`timeout`、`linked`、`message`、`response` 皆以 `using`/`using var` 宣告（LinePushClient.cs:71-73, 86），正常與例外路徑皆會釋放；`HttpCompletionOption.ResponseHeadersRead` 搭配不讀取 body，避免殘留串流。
- **caller 取消只在收到回應前生效，收到回應後依 HTTP 分類**：成功路徑（LinePushClient.cs:86-97）完全不檢查 `ct`，回應一到即按狀態碼分類；取消／例外只可能發生在尚未取得 `response` 時。
- **非 caller 取消 Retryable 且保留原始例外**：`catch (OperationCanceledException)`／`catch (HttpRequestException)`（LinePushClient.cs:99-112）皆先以 `ct.ThrowIfCancellationRequested()` 判斷是否為 caller 取消，否則回傳 `Retryable` 並原樣保留 `exception` 實例（非包裝、非改寫）。
- **不通知、不寫檔、不把 Token/body/userId 放進自產輸出**：`LineChannelCredential.ToString()`、`LinePushRequest.ToString()`、`LinePushResult.ToString()` 均回傳固定遮罩字串；`TransportException` 標註 `[JsonIgnore]`，序列化 `LinePushResult` 不會外露例外內容。
- **net48/net10 行為一致**：兩個目標共用同一份 `PushAsync` 邏輯與逾時排程器 `StartTimeout`；`Newtonsoft.Json` 版本經 `Directory.Packages.props` 集中管理（13.0.4），兩平台一致。

## Warning 🟡

1. **net48 唯一生產進入點完全無測試覆蓋** — `LinePushClient.cs:27-36, 120-128`
   `LinePushClient()`（無參數建構子）與 `CreateSharedClient()` 所建立的靜態 `SharedClient`（`UseCookies=false`、`AllowAutoRedirect=false`、`UseDefaultCredentials=false`、`Timeout=InfiniteTimeSpan`）是 net48 Plugin 沙箱唯一會呼叫的路徑。檢查整份 `LinePushClientTests.cs`，所有案例皆透過 `Fixture.Client => new(Http)`（即三參數/二參數建構子＋假 handler）繞過這條路徑，檔案中沒有任何 `new LinePushClient()` 或對 `SharedClient` 的引用。
   可重現缺口：若日後不慎把 `AllowAutoRedirect` 或 `UseCookies` 其中之一改成預設值（`true`），33 個以上案例仍會全部通過，不會觸發任何測試失敗 — 對這段生產路徑而言是守門假綠燈。
   最小修法：新增一個不送出真實 HTTP 的測試，透過 `internal` 可視範圍直接斷言 `LinePushClient` 的靜態 `SharedClient`（或其底層 `HttpClientHandler`）之 `UseCookies`/`AllowAutoRedirect`/`UseDefaultCredentials`/`Timeout` 四個值，或至少對無參數建構子做一次建構成功的煙霧測試。

2. **請求序列化依賴行程全域可變靜態狀態** — `LinePushClient.cs:76`
   `JsonConvert.SerializeObject(new {...})` 未傳入專屬 `JsonSerializerSettings`，隱含依賴 Newtonsoft.Json 的靜態 `JsonConvert.DefaultSettings`。目前 `LineMessagingProcessor`／`SpeechMessage.Dynamics.Plugins` 均未設定此靜態屬性（已檢索確認），故現狀不會觸發問題；但 net48 Plugin 沙箱是同一 AppDomain 內可能與其他外掛共用的行程範圍狀態，若日後任何同 AppDomain 的程式碼設定 `JsonConvert.DefaultSettings`（例如換用 camelCase 解析器或全域轉換器），此處的推播本文會被悄悄改變，且不會有任何編譯期或既有測試警示 — 與文件註解宣稱的「每次呼叫獨立」隔離保證有落差。
   最小修法：改用類別內部 `static readonly` 且不受外部影響的 `JsonSerializerSettings` 實例顯式傳入 `SerializeObject`。

3. **`PushAsync` 的 null 防呆沒有對應測試** — `LinePushClient.cs:62-68`
   `credential`／`request` 為 `null` 時擲出 `ArgumentNullException` 的分支，在目前測試檔案中找不到直接呼叫 `PushAsync(null, ...)` 或 `PushAsync(credential, null, ...)` 的案例。影響面小（邏輯簡單且雙目標寫法一致），但屬於可重現、非猜測的覆蓋率缺口。

## Info 🟢

1. `LinePushClient.cs:38-43`：net10 的 `public LinePushClient(HttpClient httpClient)` 要求呼叫端提供「無 Cookie、無預設憑證、不自動轉址」的管線，此要求僅以 XML 文件註解聲明為契約，Core 本身未在建構或送出請求時做執行期檢查。屬已核定的信任邊界責任劃分（host 自行確保），非 Core 程式錯誤，僅供留意：若 host 未來誤注入允許自動轉址的 `HttpClient`，在 CA／DNS 遭滲透的極端情境下，`Authorization: Bearer` 標頭理論上可能隨跨網域轉址外洩。
2. `LinePushClient.cs:41` vs `:43`：net48 的對應建構子是 `internal`，net10 是 `public`，為刻意的平台差異（net48 只能走沙箱共用的無參數建構子），非缺陷，僅記錄以防日後誤解兩平台 API 面對稱。
3. 另曾懷疑 Core csproj 未設 `<LangVersion>` 會導致 net48 編譯失敗（檔案全面使用檔案範圍 namespace／關係模式等 C#9/10 語法）；經檢索既有 `SpeechMessage.Dynamics.Plugins`（net48-only、同樣無 `LangVersion`、`global.json` 固定 SDK 10.0.403）已存在相同語法並視為既有可編譯基準，故排除此項，不列為發現。

## Summary

本批（Core 傳輸層）在跨呼叫隔離、資源釋放、取消/例外分類、機密不外洩、net48/net10 行為一致性等核心專項上未發現 Critical 缺陷，實作與既核定的設計規則（caller 取消語意、Retryable 保留原始例外、Core 不通知不寫檔）吻合。主要建議是補強兩個測試缺口（net48 生產進入點的 `SharedClient` 設定、null 防呆）與移除對 `JsonConvert.DefaultSettings` 全域狀態的隱性依賴，三者均屬 Warning 等級、不阻擋本批次結論，但建議在下一輪補上，以避免未來重構時出現無測試攔截的回歸。本份結論僅代表 Core 分份，仍需與同批其他分份（Plugin／net10 host／規格文件）合併審查後才能視為本批完成。

---
SESSION_ID: 0a433d8a-14ba-4ee9-a928-e730a5311dc9

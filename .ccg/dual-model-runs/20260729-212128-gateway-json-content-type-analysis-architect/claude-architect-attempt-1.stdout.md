# Gateway JSON-only Content-Type 邊界分析

## 0. 現況確認

已讀取 `Program.cs`、`GatewayOperationRequestBodyReader.cs`、`GatewayRequestBodyBoundaryTests.cs` 與 spec 契約文件。確認現況：

- `Program.cs` 的 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}` 目前順序為：`UseAuthentication` → `UseAuthorization` → `operationAuthorizer.Authorize`（401/403）→ `bodyReader.ReadAsync`（body byte/JSON 邊界）→ executor。
- `GatewayOperationRequestBodyReader.ReadAsync` 完全不檢查 `HttpRequest.ContentType`，只要 wire bytes 是合法且 shape 正確的 JSON 就會成功，無論 caller 宣告什麼 Content-Type（含缺失）。
- Spec 契約（`dynamics-gateway-hosting-version-routing.md`）已明示 `Content-Type: application/json` 是唯一 API 契約，但未強制。
- 測試 fixture (`CreateHttpRequest`/`SendThroughTestServerAsync`) 目前固定寫死 `application/json`，尚無反例覆蓋。

結論：這是一個真實的邊界缺口——JSON-only 契約目前只由「body 內容剛好是合法 JSON」隱性保證，而非由媒體型別協商強制。

---

## 1. 建議的媒體型別與 charset 契約

| 項目 | 決策 |
|---|---|
| 缺少 `Content-Type` | 拒絕（415） |
| Media type 比對 | 對 `application/json` 做**大小寫不敏感**完整比對（`APPLICATION/JSON` 可接受，`application/Json` 可接受） |
| `charset` 參數 | 允許缺省（視為 UTF-8）；若提供，僅接受大小寫不敏感的 `utf-8`；其他 charset（如 `iso-8859-1`、`utf-16`）一律拒絕 |
| 其他/未知參數 | 拒絕（fail-closed）。JSON-only 契約不需要 `boundary`、`q` 等參數；出現即視為契約外行為 |
| `application/*+json`（RFC 6839 structured suffix，如 `application/vnd.api+json`、`application/merge-patch+json`） | **不接受**於本次最小修正。理由：Gateway 契約是特定 envelope shape（`idempotencyKey`/`parameters`），不是通用內容協商資源；貿然放行 `+json` 等於重新打開任意 subtype 白名單，卻沒有對應語意處理，弱化「JSON-only」的意圖且擴大解析面。若未來確有需求，應以明確 allowlist（例如僅 `application/vnd.speechmessage.gateway+json`）方式追加，而非泛用萬用字元 |
| Method 範圍 | 僅套用於有 body 的 `POST /v1/organizations/{alias}/operations/{capabilityOperationId}`；`GET /health`、`GET /ready`、`GET /v1/operations` 無 body，不受影響 |
| 空 body（Content-Length: 0）但宣告非 JSON Content-Type | 仍以 415 拒絕，不特殊處理，維持契約單一路徑 |

---

## 2. 最小實作位置與理由

**建議新增一個獨立、無狀態的 pure 驗證器**，而不是把邏輯塞進 `GatewayOperationRequestBodyReader` 或改動其 enum：

- 新檔：`SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationContentTypeValidator.cs`
  - `public static bool IsSupportedJsonContentType(string? contentType)`（或回傳一個輕量 enum，若要與 reader 風格一致也可以，但布林已足夠，因為呼叫端只有一個 415 分支）。
  - 內部使用 **`Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse`**（ASP.NET Core 共享框架已內建，無需新增套件依賴）解析 `MediaType`（不含參數的 subtype）與 `Parameters`（含 `Charset`）。不要手刻字串 split/正規表達式——手刻解析容易在大小寫、空白、quoted-string 上出錯，等於自建一個新的攻擊面。
  - 方法本身：純函式、無 I/O、無 stream/buffer/timer/cache，符合「不得新增未界定 owner 的資源」限制。

- `Program.cs` 呼叫點：放在

  ```
  if (!authorization.Succeeded) { return Results.Forbid(); }
  // ← 新增 Content-Type 檢查於此
  var bodyRead = await bodyReader.ReadAsync(...)
  ```

  也就是**授權成功之後、`bodyReader.ReadAsync` 之前**。理由見下一節的 ordering 風險分析。

**為什麼不修改 `GatewayOperationRequestBodyReader`**：
- Reader 的職責、doc 註解、測試已經明確鎖定在「wire bytes → JSON shape」邊界，混入 media-type 決策會擴大它的職責與回填測試成本。
- Content-Type 檢查只讀 header（Kestrel/IIS 已解析好的字串），完全不需要碰 request stream，因此獨立於 reader 之外反而更符合「一次只做一件事」與最小 diff 原則。
- 不需要新增 `GatewayOperationRequestBodyReadStatus` 列舉值，415 直接在 endpoint 層回傳，reader 契約與既有測試 0 改動。

---

## 3. 必須先建立的 RED 測試案例與 assertion

建議新增於既有 `SpeechMessage.Dynamics.Tests/GatewayRequestBodyBoundaryTests.cs`（沿用其 `CreateFactory`/`TrackingReadStream`/`RecordingExecutor` fixture），或獨立新檔 `GatewayOperationContentTypeBoundaryTests.cs` 以維持單一職責與可讀性。至少需要：

1. **`Authorized_missing_content_type_is_rejected_with_415`**
   - 已授權 caller，未帶 `Content-Type`。
   - Assert：`StatusCode == 415`；`body.ReadCount == 0`（stream 完全未被讀取）；`executor.CallCount == 0`。

2. **`Authorized_non_json_content_type_with_valid_json_body_is_rejected_with_415`**
   - `Content-Type: text/plain`，body 內容是合法 JSON。
   - Assert：415；`ReadCount == 0`（證明 media type 判斷先於任何 body 消耗）；`executor.CallCount == 0`。

3. **`Authorized_json_content_type_is_case_insensitive_and_accepted`**
   - `Content-Type: APPLICATION/JSON`。
   - Assert：進入 body read（非 415），最終行為與現有 success 測試一致。

4. **`Authorized_json_with_utf8_charset_case_insensitive_is_accepted`**
   - `Content-Type: application/json; charset=UTF-8` 與 `charset=utf-8` 兩種大小寫。
   - Assert：接受。

5. **`Authorized_json_with_unsupported_charset_is_rejected_with_415`**
   - `Content-Type: application/json; charset=iso-8859-1`。
   - Assert：415；`ReadCount == 0`。

6. **`Authorized_json_with_unknown_parameter_is_rejected_with_415`**
   - `Content-Type: application/json; boundary=xyz`。
   - Assert：415（驗證未知參數 fail-closed 決策）。

7. **`Authorized_structured_suffix_json_is_rejected_with_415`**
   - `Content-Type: application/vnd.api+json`。
   - Assert：415（鎖定「暫不接受 `+json` 萬用字元」的架構決策，避免未來不小心放寬）。

8. **`Unauthorized_or_unmapped_caller_gets_403_regardless_of_content_type`** ← **關鍵回歸測試**
   - `mapped: false`（沿用現有 `Authenticated_unmapped_request_is_forbidden_before_any_body_read` 的 fixture 手法），搭配**故意錯誤**的 `Content-Type: text/plain`。
   - Assert：`StatusCode == 403`（不是 415！）；`body.ReadCount == 0`；`executor.CallCount == 0`。
   - 目的：鎖死「authorization 必須先於 content-type 判斷」的順序，防止未來重構把檢查移到 authorize 之前造成新的 401/403 vs 415 探測 oracle。

9. **Kestrel end-to-end 版本**（比照現有 `Kestrel_http11_rejects_declared_and_chunked_limit_plus_one`）
   - 用真實 HttpClient 送 `Content-Type: text/plain` 的合法 JSON body，斷言 wire-level `HttpStatusCode.UnsupportedMediaType (415)`，確認不是只有單元測試層級成立。

以上第 1、2、5、6、8 項在目前程式碼下應該全部 **RED**（現況會回 200/400 而非 415，或在第 8 項現況雖然是 403 但缺乏顯式回歸鎖定）。

---

## 4. 風險檢查

**Authentication/Authorization ordering**
- 風險：若把 Content-Type 檢查放在 `operationAuthorizer.Authorize` 之前，等於在 authentication/authorization 完成前就對 caller 揭露「body-parsing 相關的契約差異」，形成新的 response-code oracle（測試 8 正是為了鎖住這點）。
- 結論：**必須**放在 `authorization.Succeeded` 判斷之後、`bodyReader.ReadAsync` 之前。這與現有 413/400 的既定順序完全一致，不需要新的 middleware 或提前介入。

**資源 owner**
- Content-Type 驗證只讀 `HttpRequest.ContentType`（Kestrel/IIS 已解析的字串屬性），不觸碰 `Request.Body`。不新增 stream、buffer、pool、cache、timer、subscription 或背景工作，符合限制。
- 新驗證器為 stateless static class，不需要 DI 生命週期管理，不影響 `GatewayOperationRequestBodyReader` 既有 singleton/ArrayPool 所有權模型。

**取消（cancellation）**
- 無 I/O，因此無取消語意需要處理；不需要 `CancellationToken` 參數。

**記憶體/效能**
- `MediaTypeHeaderValue.TryParse` 成本與 header 長度成正比，Header 長度已受 Kestrel `MaxRequestHeadersTotalSize`/`MaxRequestHeaderCount` 既有邊界保護，不需要額外長度上限。
- 不影響既有 `ArrayPool<byte>` 單一 rent 邊界；此檢查在 rent 之前執行，等於進一步減少「不必要 rent」的情況（非 JSON 請求直接 415，連 buffer 都不會租用）——對現有記憶體邊界是淨改善而非新增風險。

**日誌/回顯**
- 415 回應不應帶出 caller 宣告的原始 Content-Type 字串、body 內容、principal 或 token；直接 `Results.StatusCode(415)`（比照現有 `Results.StatusCode(413)` 模式）即可，不需要 body 或訊息內容，天然滿足「不得回顯」要求。

---

## 5. 分級結論

**Critical**
- 現況：JSON-only 契約在 HTTP 層完全未被強制，`Content-Type: text/plain`（或缺失）搭配合法 JSON body 會被接受並進入 executor——這是本次任務要修補的核心邊界缺口，建議依第 1～2 節方案修正。
- Ordering 風險：Content-Type 檢查邏輯若未來被移到 `operationAuthorizer.Authorize` 之前，將重新引入 401/403 vs 415 的 pre-auth oracle。必須以測試 8 鎖定順序，任何實作 PR 都需通過此測試才能合併。

**Warning**
- 手刻字串解析 Content-Type（而非使用 `MediaTypeHeaderValue`）容易在大小寫、空白、quoted charset 上出現邊界 bug，等同新增一個小型攻擊面；務必使用框架既有型別。
- 若貿然接受未知參數（如 `boundary=`）或 `application/*+json` 萬用字元，會削弱「JSON-only、契約明確」的架構意圖，且日後很難收緊（會被既有 caller 依賴）。建議一開始就 fail-closed，之後如需放寬用明確 allowlist 逐項加入。

**Info**
- 建議以獨立 `GatewayOperationContentTypeValidator` 靜態類別實作，不修改 `GatewayOperationRequestBodyReader` 或其 enum，維持既有測試與職責邊界零改動。
- 新測試建議掛在既有 `GatewayRequestBodyBoundaryTests.cs` fixture 之上（或平行新檔），沿用 `TrackingReadStream`/`RecordingExecutor` 以繼續驗證「拒絕發生在 stream read 與 executor 呼叫之前」的既定證明模式。
- 效能與記憶體：此檢查對現有邊界是淨改善（提前排除非 JSON 請求，減少不必要的 buffer rent），無新增資源疑慮。

---

**任務狀態**：分析已完成，未修改任何程式檔案（依指示）。若要進入實作階段，建議順序為：先落地上述 RED 測試（確認在目前程式碼下失敗於預期位置），再新增 `GatewayOperationContentTypeValidator` 與 `Program.cs` 的單一呼叫點修正，使測試轉綠。

---
SESSION_ID: 99641259-8ccd-4cce-91ec-61a2032160ee

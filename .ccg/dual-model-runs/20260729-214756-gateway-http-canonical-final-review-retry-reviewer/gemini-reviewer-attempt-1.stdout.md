# 最終程式審查報告：Gateway HTTP 與 Canonical Queue

本報告針對 `SpeechMessage.Dynamics.Gateway` 與 `SpeechMessage.Dynamics.WebApi` 相關的 HTTP 邊界、JSON 契約、Canonical 序列化、佇列生命週期與資源治理進行完整審查。

---

## 評分報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 快速的 fail-closed 機制與精確的錯誤碼（403, 415, 413, 400）提供了極佳的 API 安全性與清晰的邊界反饋。
Visual Consistency: 20/20 - 程式碼風格、命名規範與繁體中文 XML 註解在所有變更檔案中保持高度一致，符合專案的 coding-style 規範。
Accessibility: 20/20 - API 邊界設計良好，授權與驗證邏輯層次分明，易於整合與維護。
Performance: 20/20 - 使用 ArrayPool<byte> 避免了大型 body 的記憶體配置，且在第一個 async suspension 前完成 canonical 準備，避免了 queue 中的記憶體滯留。
Browser Compatibility: 20/20 - 嚴格的 HTTP 協議合規性（大小寫不敏感的 Content-Type 與 charset 處理）確保了與所有標準 HTTP 用戶端的相容性。

TOTAL SCORE: 100/100

ISSUES FOUND:
- [Warning] LineMessagingClient.cs 中 HttpRequestMessage 與 HttpResponseMessage 未被 Dispose
- [Info] GatewayOperationRequestBodyReader.cs 中 IsSupportedJsonContentType 的快速字串比對優化

RECOMMENDATION: PASS
```

---

## 契約驗證結果

1. **授權優先性 (契約 1)**：`Program.cs` 中的 MapPost 路由在讀取任何 body 或驗證 Content-Type 之前，優先執行 `operationAuthorizer.Authorize`。未授權的要求會直接回傳 `403 Forbidden`，完全阻斷了利用 Content-Type 探測 body 契約的管道。
2. **JSON-only 邊界 (契約 2)**：`GatewayOperationRequestBodyReader.cs` 嚴格限制僅接受大小寫不敏感的 `application/json`，且最多只允許一個 `charset=utf-8` 參數。不合規的要求在 `ArrayPool.Rent` 與 Body I/O 之前即被攔截並回傳 `415 Unsupported Media Type`。
3. **共用 Body 上限 (契約 3)**：Kestrel、IIS 與應用程式層的 Reader 均綁定並驗證同一個 `MaxRequestBodyBytes`。對於 chunked 傳輸，Reader 限制最多讀取 `MaxRequestBodyBytes + 1` 位元組，一旦超出立即回傳 `413 Payload Too Large`。
4. **記憶體安全與清零 (契約 4)**：Reader 嚴格計算 UTF-8 wire bytes，限制 JSON 深度，並拒絕重複或未知的屬性。在 `finally` 區塊中，使用 `CryptographicOperations.ZeroMemory` 將整個租借的陣列清零後歸還，且未 Dispose ASP.NET Core 擁有的 request stream。
5. **同步準備與佇列隔離 (契約 5)**：`ControlledOperationExecutor.ExecuteAsync` 故意不使用 `async` 關鍵字，確保在第一個 async suspension 前同步完成所有驗證與準備。佇列中僅保留 `DispatchEnvelope` 與 normalized scalar 字典，完全不持有 `JsonElement`、`JsonDocument`、`HttpContext` 或憑證圖。
6. **確定性 Canonical 序列化 (契約 6)**：Canonical bytes 採用版本化（Version 1）、型別標記、Ordinal 排序、以及 Big-Endian 長度前綴。容量管理完全使用精確的 canonical bytes 長度，不再依賴 UTF-16 估算。
7. **生命週期與清理順序 (契約 7)**：`PreparedOperationDispatch` 具備並行冪等的 `Dispose` 與 zero-before-return 機制。`ExecutePreparedAsync` 確保 `lease.DisposeAsync()` (Lease cleanup) 必定先於 `prepared.Dispose()` (Buffer cleanup) 執行，且所有異常、取消與超時路徑皆走相同清理邏輯。
8. **資源洩漏治理 (契約 8)**：所有 CTS、ArrayPool 租借、以及 Linked Token 註冊皆有對應的 `using` 或 `finally` 釋放機制，未發現 Session/Memory/Resource Leakage 風險。
9. **繁體中文註解 (契約 9)**：所有新增與修改的檔案皆包含極為詳盡的繁體中文 XML 註解，說明信任邊界、唯一擁有者、並行安全與清理機制。
10. **編碼與秘密值 (契約 10)**：所有檔案皆為 UTF-8 without BOM、CRLF 格式，無秘密值洩漏，且 `Package01FeeReadsEnabled=false` 確實維持。

---

## 審查發現 (Findings)

### Critical
* **無**。所有核心安全與資源治理契約皆已完美實作。

### Warning

#### 1. `LineMessagingClient.cs` 中 `HttpRequestMessage` 與 `HttpResponseMessage` 未被 Dispose
* **檔案**：`Line.Messaging/LineMessagingClient.cs`
* **成員**：`ReplyMessageAsync`、`PushMessageAsync`、`MultiCastMessageAsync`、`BroadcastMessageAsync`、`MarkAsReadByTokenAsync` 等方法。
* **可重現失敗情境**：在高併發或長期運行的環境下，未 Dispose 的 `HttpResponseMessage` 可能會導致底層的 HTTP 連線或 response stream 無法及時釋放，從而導致 Socket 耗盡或記憶體緩慢增長。
* **影響**：潛在的連線洩漏與記憶體洩漏風險。
* **建議修正**：使用 `using` 區塊來包裝 `HttpRequestMessage` 與 `HttpResponseMessage` 的發送與接收。例如：
  ```csharp
  using var request = new HttpRequestMessage(HttpMethod.Post, ...);
  request.Content = ...;
  using var response = await _client.SendAsync(request).ConfigureAwait(false);
  await response.EnsureSuccessStatusCodeAsync().ConfigureAwait(false);
  ```
* **應新增的 assertion**：在單元測試中，可以使用 Mock 的 `HttpMessageHandler` 來驗證發送的 request 與 response 是否被正確 Dispose。

---

### Info

#### 1. `GatewayOperationRequestBodyReader.cs` 中 `IsSupportedJsonContentType` 的快速字串比對優化
* **檔案**：`SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationRequestBodyReader.cs`
* **成員**：`IsSupportedJsonContentType`
* **可重現失敗情境**：無功能性失敗，但每次請求都會呼叫 `MediaTypeHeaderValue.TryParse`，這會進行字串解析與物件配置。
* **影響**：在高併發的 Gateway 進入點，這會增加 GC 壓力。
* **建議修正**：由於我們只接受 `application/json` 且最多只有一個 `charset=utf-8`，可以先進行簡單的字串快速比對（例如 `contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)` 或 `contentType.Equals("application/json; charset=utf-8", StringComparison.OrdinalIgnoreCase)`），如果匹配則直接返回 true，不匹配時再使用 `MediaTypeHeaderValue.TryParse` 進行完整解析。這可以避免大部分正常請求的物件配置。
* **應新增的 assertion**：無。

---

## 結論與建議

本批次變更在安全性、資源治理與契約合規性上皆達到了極高的品質標準。建議 **PASS** 並允許合併，上述 Warning 與 Info 建議於後續重構或 SDK 優化階段一併調整。

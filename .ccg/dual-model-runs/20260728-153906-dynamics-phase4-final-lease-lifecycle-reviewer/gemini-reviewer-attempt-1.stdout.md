# Dynamics Phase 4 最終租約生命週期與隔離強化審查報告

本審查針對當前 Phase 4 隔離強化變更集（包含 `RuntimeHostSlotLease` 的同步釋放機制、ADFS 權杖解析優化及 HTTP 傳輸層隔離）進行唯讀事實核對與程式碼品質評估。

## 總體結論：**PASS**
（僅限本次本機隔離強化增量；不構成 Package01 或多 Gateway 正式上線放行）

---

## 關鍵發現與分類 (Findings & Classifications)

### Critical 🔴
* **無**。本次變更在單機範疇內未發現嚴重的程式碼缺陷、資源洩漏或 race condition。

### Warning 🟡

1. **`RuntimeHostSlotLease.Dispose()` 同步等待非同步操作可能導致死鎖**
   * **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Capacity/IRuntimeHostSlotCoordinator.cs` (第 53-56 行)
   * **原因說明**：在 `Dispose()` 中，呼叫了 `_coordinator.ReleaseAsync(this, CancellationToken.None).AsTask().GetAwaiter().GetResult()`。如果 `IRuntimeHostSlotCoordinator` 的實作（例如未來可能導入的 Redis 協調器）在 `ReleaseAsync` 中執行了真正的非同步等待，且呼叫端是在一個有 `SynchronizationContext` 的執行緒（如 UI 執行緒或舊版 ASP.NET 執行緒）上同步呼叫 `Dispose()`，這將會導致死鎖。
   * **建議**：雖然目前程式碼庫中主要使用 `await using` / `DisposeAsync()`，但為了確保同步 `Dispose()` 的安全性，建議在 `ReleaseAsync` 的實作中確保所有 await 都加上 `ConfigureAwait(false)`，或者在 `Dispose()` 中使用 `Task.Run` 來避開當前的 `SynchronizationContext`。

2. **`ReadBoundedResponseAsync` 回傳 `byte[]` 造成額外的記憶體配置與殘留風險**
   * **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs` (第 386 行)
   * **原因說明**：`ReadBoundedResponseAsync` 雖然在 `finally` 區塊中將租用的 `ArrayPool<byte>` 緩衝區以 `clearArray: true` 歸零，但第 386 行的 `buffer.AsSpan(0, totalRead).ToArray()` 會在受管理堆積（Managed Heap）上配置一份新的 `byte[]` 複本。雖然在 `RequestNewTokenAsync` 的 `finally` 區塊中呼叫了 `CryptographicOperations.ZeroMemory(body)` 來清空這份複本，但 `ParseTokenResponse` 解析出來的 `AccessToken` 和 `RefreshToken` 字串（不可變字串）仍會殘留在記憶體中，直到 GC 回收。
   * **建議**：後續可考慮直接將 `ReadOnlySpan<byte>` 傳給 `ParseTokenResponse`，避免在堆積上配置承載明文 token 的字串或陣列複本，或者使用 `IMemoryOwner<byte>` 來管理這段記憶體的生命週期。

### Info 🟢

1. **測試盲點：缺乏例外與死鎖驗證**
   * **檔案與行號**：`SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs` (第 478-497 行)
   * **原因說明**：新增的 `Synchronous_host_slot_lease_dispose_waits_for_release_completion` 測試成功驗證了同步 `Dispose()` 會等待釋放完成，但未驗證當 `ReleaseAsync` 拋出例外時的行為，也未驗證在有 `SynchronizationContext` 的環境下是否會發生死鎖。

2. **手寫 JSON 解析器正確性與效能提升**
   * **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs` (第 400-459 行)
   * **原因說明**：改用 `Utf8JsonReader` 手動解析 token 回應，避免了 `JsonDocument` 的 DOM 配置，且正確處理了 `expires_in` 的數字與字串型別，並使用 `reader.Skip()` 安全跳過未知屬性，效能與安全性均有提升。

3. **HTTP 隔離設定與功能旗標確認**
   * **檔案與行號**：
     * `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs` (第 80-89 行)
     * `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsHttpTransport.cs` (第 92-103 行)
     * `SpeechMessageProducts.ChurchReport/appsettings.json` (第 559 行)
   * **原因說明**：`SocketsHttpHandler` 已正確停用 cookies、redirects、proxies、decompression 與 pre-authentication。`Package01FeeReadsEnabled` 確實保持為 `false`，確保未經驗證的 Web API 路徑不會在生產環境中被意外啟用。

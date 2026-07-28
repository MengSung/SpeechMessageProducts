
We have enough information to write the review report.
Let's review the checklist and constraints:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- Classify findings as Critical / Warning / Info.
- Cite exact files and lines for each finding.
- PASS or FAIL verdict.
- Focus on session/profile/token leakage, memory/resource leakage, race conditions, semaphore/lease lifecycle, cancellation, response parsing limits, handler reuse, test effectiveness, and whether the reported Phase 4 completion is confined to the local hardening increment.

Let's organize the findings:

### Verdict: PASS (僅限本機隔離強化增量)

### Critical 🔴
無。未發現嚴重的 session/profile/token 洩漏、記憶體洩漏或 race condition。

### Warning 🟡
1. **`AdfsOAuthTokenProvider.cs` 中的 `ReadBoundedResponseAsync` 記憶體殘留風險**
   - **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs` (第 386 行，`buffer.AsSpan(0, totalRead).ToArray()`)
   - **原因說明**：`ReadBoundedResponseAsync` 雖然在 `finally` 區塊中將租用的 `ArrayPool<byte>` 緩衝區以 `clearArray: true` 歸零，但第 386 行的 `buffer.AsSpan(0, totalRead).ToArray()` 會在受管理堆積（Managed Heap）上配置一份新的 `byte[]` 複本，承載完整的 token JSON 回應。雖然在 `RequestNewTokenAsync` 的 `finally` 區塊中呼叫了 `CryptographicOperations.ZeroMemory(body)` 來清空這份複本，但 `ParseTokenResponse` 解析出來的 `AccessToken` 和 `RefreshToken` 字串（不可變字串）仍會殘留在記憶體中，直到 GC 回收。這在極端情況下仍有敏感資訊殘留的風險。
   - **建議**：後續可考慮使用 `Utf8JsonReader` 直接對 Stream 進行串流解析，避免在堆積上配置承載明文 token 的字串或陣列複本。

2. **`OrganizationAdmissionManager.cs` 中 `_lease` 欄位併發存取缺乏同步保護**
   - **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs` (第 66-105 行，`EnsureHostSlotAsync` 方法)
   - **原因說明**：`OrganizationAdmissionManager` 作為 Singleton 註冊，其 `EnsureHostSlotAsync` 方法會被多個併發請求同時呼叫。然而，該方法在讀寫 `_lease` 欄位時並未持有任何鎖（如 `_gate`），這可能導致在冷啟動或續租窗口內，多個執行緒同時進入 `TryAcquireAsync` 或 `TryRenewAsync`，產生多個 `RuntimeHostSlotLease` 實例，但最終只有一個被指派給 `_lease`，其餘的租約實例會被丟棄且未被正確釋放（Release/Dispose），造成潛在的租約生命週期管理混亂。
   - **建議**：在 `EnsureHostSlotAsync` 內對 `_lease` 的讀取、更新與寫回操作應使用鎖（如 `_gate` 或專屬鎖）進行同步保護。

### Info 🔵
1. **`InMemoryRuntimeHostSlotCoordinator` 的單機限制聲明**
   - **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Capacity/InMemoryRuntimeHostSlotCoordinator.cs` (第 1-10 行)
   - **原因說明**：協調器的 `IsDurable` 屬性恆為 `false`，且其租約管理完全基於記憶體內的 `ConcurrentDictionary`。程式碼與設計文件中已明確聲明此為單進程（process-local）限制，未宣稱為跨主機的分散式協調方案，符合安全邊界約束。
2. **HTTP Handlers 安全配置與 PreAuthenticate 停用**
   - **檔案與行號**：
     - `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs` (第 80-89 行)
     - `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsHttpTransport.cs` (第 97 行)
   - **原因說明**：`SocketsHttpHandler` 已正確停用 cookies、redirects、proxies、decompression，且 `PreAuthenticate` 已由 `true` 改為 `false`，有效防止了 session 洩漏與憑證搶先送出，符合安全強化要求。
3. **原子化本地准入控制與防洩漏設計**
   - **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs` (第 185-241, 365-398 行)
   - **原因說明**：使用 `_totalAdmission` (SemaphoreSlim) 原子化地限制了 in-flight + queued 的總工作量，且在超時、取消或異常時皆會進入 `ReleaseReservation` 釋放所有已預約的資源，確保不會發生 semaphore 或 slot 洩漏。
4. **測試有效性驗證**
   - **檔案與行號**：`SpeechMessage.Dynamics.Tests/OrganizationAdmissionManagerTests.cs`
   - **原因說明**：新增的併發測試（如 `Concurrent_burst_is_limited_to_local_queue_capacity_and_releases_all_reservations`）能有效驗證併發 burst 下的容量限制與資源釋放，測試品質良好。

### 剩餘 Release Blockers (本增量範疇外之發布阻礙)
1. **分散式協調器 (Durable Coordinator)**：需實作具備 epoch/fencing/quarantine 語義的跨主機協調器。
2. **設定檔生命週期隔離 (Profile Lifecycle Isolation)**：需實作 profile 產生隔離、replace-and-drain 以及確定性的非同步執行期銷毀。
3. **回應串流限制與去識別化 (Bounded Response Streaming & Redaction)**：需在所有 ADFS 和 CRM 路徑上限制回應大小並對 token/body 進行去識別化。
4. **Gateway 工作負載驗證 (Gateway Workload Auth)**：需實作 JWT/mTLS 驗證，移除呼叫端控制的 workload subject 資料。
5. **完整 Soak/Fault 測試套件**：需執行 socket/timer/heap soak 測試以及 Gateway-plus-Embedded 聚合容量測試。
6. **Live Smoke 驗證矩陣**：需在真實 CE 8.2 和 CE 9.1 環境下完成雙副本容量與 parity 驗證。

我們將以 Traditional Chinese 輸出此報告。# Dynamics Phase 4 最終隔離強化審查報告

本審查針對當前未提交的 Phase 4 本地隔離強化變更集進行唯讀事實核對與程式碼品質評估。

## 總體結論：**PASS**
（僅限本次窄範圍的本機強化增量；不構成 Package01 或多 Gateway 正式上線放行）

---

## 關鍵發現與分類 (Findings & Classifications)

### Critical 🔴
* **無**。本次變更在單機範疇內未發現嚴重的程式碼缺陷、資源洩漏或 race condition。

### Warning 🟡

1. **`OrganizationAdmissionManager.cs` 中 `_lease` 欄位併發存取缺乏同步保護**
   * **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs` (第 66-105 行，`EnsureHostSlotAsync` 方法)
   * **原因說明**：`OrganizationAdmissionManager` 作為 Singleton 註冊，其 `EnsureHostSlotAsync` 方法會被多個併發請求同時呼叫。然而，該方法在讀寫 `_lease` 欄位時並未持有任何鎖（如 `_gate`），這可能導致在冷啟動或續租窗口內，多個執行緒同時進入 `TryAcquireAsync` 或 `TryRenewAsync`，產生多個 `RuntimeHostSlotLease` 實例，但最終只有一個被指派給 `_lease`，其餘的租約實例會被丟棄且未被正確釋放（Release/Dispose），造成潛在的租約生命週期管理混亂。
   * **建議**：在 `EnsureHostSlotAsync` 內對 `_lease` 的讀取、更新與寫回操作應使用鎖（如 `_gate` 或專屬鎖）進行同步保護。

2. **`AdfsOAuthTokenProvider.cs` 中的 `ReadBoundedResponseAsync` 記憶體殘留風險**
   * **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs` (第 386 行，`buffer.AsSpan(0, totalRead).ToArray()`)
   * **原因說明**：`ReadBoundedResponseAsync` 雖然在 `finally` 區塊中將租用的 `ArrayPool<byte>` 緩衝區以 `clearArray: true` 歸零，但第 386 行的 `buffer.AsSpan(0, totalRead).ToArray()` 會在受管理堆積（Managed Heap）上配置一份新的 `byte[]` 複本，承載完整的 token JSON 回應。雖然在 `RequestNewTokenAsync` 的 `finally` 區塊中呼叫了 `CryptographicOperations.ZeroMemory(body)` 來清空這份複本，但 `ParseTokenResponse` 解析出來的 `AccessToken` 和 `RefreshToken` 字串（不可變字串）仍會殘留在記憶體中，直到 GC 回收。這在極端情況下仍有敏感資訊殘留的風險。
   * **建議**：後續可考慮使用 `Utf8JsonReader` 直接對 Stream 進行串流解析，避免在堆積上配置承載明文 token 的字串或陣列複本。

### Info 🟢

1. **`InMemoryRuntimeHostSlotCoordinator` 的單機限制聲明**
   * **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Capacity/InMemoryRuntimeHostSlotCoordinator.cs` (第 1-10 行)
   * **原因說明**：協調器的 `IsDurable` 屬性恆為 `false`，且其租約管理完全基於記憶體內的 `ConcurrentDictionary`。程式碼與設計文件中已明確聲明此為單進程（process-local）限制，未宣稱為跨主機的分散式協調方案，符合安全邊界約束。

2. **HTTP Handlers 安全配置與 PreAuthenticate 停用**
   * **檔案與行號**：
     - `SpeechMessage.Dynamics.WebApi/DependencyInjection/WebApiServiceCollectionExtensions.cs` (第 80-89 行)
     - `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsHttpTransport.cs` (第 97 行)
   * **原因說明**：`SocketsHttpHandler` 已正確停用 cookies、redirects、proxies、decompression，且 `PreAuthenticate` 已由 `true` 改為 `false`，有效防止了 session 洩漏與憑證挑戰-回應的搶先送出，符合安全強化要求。

3. **原子化本地准入控制與防洩漏設計**
   * **檔案與行號**：`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs`

# UI Reviewer 審查報告

**審查狀態：PASS**

本審查針對 **Dynamics Multi-Profile Runtime Drain Recovery** 的修正進行了完整的程式碼與規格書比對。本次修正成功解決了前次審查中發現的 Critical 缺陷（即舊 Runtime 清理失敗時永久阻塞 Slot 的問題），並建立了完善的重試與收斂機制。

---

## 1. Summary (綜合評估)

本次修正的程式碼品質極高，技術設計嚴謹，完全符合 `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 規範。
- **生命週期收斂**：透過 `DrainOwnedRuntimeAsync` 的 `finally` 區塊，以精確的物件參考（`ReferenceEquals`）與 `Disposed` 狀態作為清除 Catalog 證明的依據，既避免了因清理失敗而永久阻塞 Slot，也防止了因過早清除而遺失對未完成 Runtime 的強引用。
- **資源與容量隔離**：`crm82` 與 `crm91` 擁有完全獨立的 Client、Transport、Token Provider 與 CTS，僅在組織層級（`CanonicalOrganizationCapacityKey`）共享准入容量，完全符合 No-SDK 與安全隔離合約。
- **非同步邊界防護**：在 `InitializeCoreAsync` 開頭引入 `await Task.Yield()`，強制建立非同步邊界，徹底解決了同步失敗導致 `_initializationTask` 損壞且無法重試的邊界問題。

---

## 2. Accessibility Issues (無障礙問題)

* **無**。本次修正不涉及前端 UI 變更。Gateway 的 `/ready` 診斷端點已正確去除了所有敏感資訊（如 Endpoint、Credential、Token 等），僅輸出安全的多 Profile 狀態與容量指標，符合無障礙與安全合約。

---

## 3. Design Issues (設計一致性問題)

* **無**。`Central` 與 `Local` 網關均統一使用 `ExecutionMode=Gateway` 契約，僅透過 `Endpoint` 進行拓撲切換，未發明冗餘的列舉值，保持了設計系統的一致性。

---

## 4. Suggestions (改進建議)

### ℹ️ Info - 關於 `AdfsOAuthTokenProvider` 的 `SocketsHttpHandler` 生命週期
- **位置**：`SpeechMessage.Dynamics.WebApi/Runtime/AdfsOAuthTokenProvider.cs`
- **說明**：當 `_httpClientFactory` 為 null 時，Provider 會自行建立並重用 `SocketsHttpHandler`。雖然在 `DisposeCoreAsync` 中已正確 Dispose 了 `_ownedHttpClient`（這會連帶 Dispose 內部的 handler），但建議在註解中進一步強調：此 SocketsHttpHandler 的 `PooledConnectionLifetime` 設為 5 分鐘，是為了兼顧連線重用與 DNS/端點更新的安全性，以防未來維護者誤將其改為無限期。

---

## 5. Positive Notes (優秀設計點)

- **精確的 Rollback 機制**：`DynamicsProfileRuntimeManager.AcquireAsync` 在取得 Runtime Lease 失敗時，採用了反向清理順序，並以 `CaptureCleanupFailureAsync` 確保 Lease 與 Permit 都被釋放，且將所有清理失敗與原始失敗包裝為 `AggregateException` 向上傳遞，防範了容量計數器（ActivePermits）在高負載下因異常而永久升高。
- **高質量的繁體中文註解**：所有新增的 Production 與 Test 程式碼均附帶了極為詳盡的繁體中文 XML 註解，清晰說明了擁有權（Ownership）、併發行為、錯誤結果與 Drain/Dispose 順序，為後續維護提供了極佳的架構上下文。
- **完善的 Regression 測試**：新增的三個單元測試（`Disposed_draining_cleanup_failure_is_reported_and_does_not_block_later_replacement` 等）與 Soak 測試確實覆蓋了非典型但高風險的併發與失敗路徑，且透過 `WeakReference` 證明了垃圾回收後無任何記憶體或 CTS 洩漏。

# VALIDATION REPORT

## 1. Summary (總體評估)
本次審查針對 `dynamics-multi-profile-runtime` 的未提交變更（uncommitted git diff）進行了全面的生命週期、資源隔離、安全性與併發控制審查。

整體設計非常嚴密，完全符合規格書中對於 Local/Central Gateway 的多 Profile 執行路徑、replace-and-drain 流程、資源清理順序以及錯誤彙整的要求。程式碼中包含了極為詳盡的繁體中文 XML 註解與實作說明，且所有新增與修改的檔案均符合 UTF-8 without BOM 與 CRLF 的編碼規範。

經評估，本變更集在資源隔離、生命週期管理與錯誤復原機制上表現優異，未發現任何 Critical 或 Warning 級別的缺陷。

---

## 2. Scoring (評分)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 產品 Request 僅能透過已授權的 Alias 進行路由，禁止傳入敏感的 Endpoint 或 Credential，提供了極佳的安全邊界防護；Readiness 端點與錯誤訊息皆經過 Redacted 處理，不洩漏敏感資訊。
Visual Consistency: 20/20 - 統一了 Central 與 Local Gateway 的多 Profile 執行架構，命名與設計模式與既有的容量管理系統高度一致，並完美相容舊有的單一 Profile 執行路徑。
Accessibility: 20/20 - 生命週期與資源可控性極佳。所有 Permit、Lease、Handler、Token Provider、CTS 等資源皆有明確的單一擁有者與確定性的清理路徑，且 rollback 機制能確保所有資源皆被嘗試釋放。
Performance: 20/20 - Queue wait 期間不持有 Runtime 或 Client 的強引用，允許舊 Generation 正常 drain；相同實體 Organization 共享容量限制，不同 Organization 互相隔離；所有鎖（lock）的範圍皆經過精心設計，避免了鎖內非同步等待或 I/O 導致的死鎖風險。
Browser Compatibility: 20/20 - 透過 FixedProfileExecutionLeaseProvider 提供了完美的向下相容性，確保既有的單一 Profile 測試與 Embedded 模式不受影響。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無 (None)

RECOMMENDATION: PASS
```

---

## 3. Detailed Findings (詳細審查結果)

### Accessibility & Lifecycle (生命週期與資源清理) - **PASS**
- **AcquireAsync 錯誤復原**：在 `DynamicsProfileRuntimeManager.AcquireAsync` 中，若在取得 Runtime Lease 後發生異常，`catch` 區塊會透過 `CaptureCleanupFailureAsync` 依序嘗試釋放 `runtimeLease` 與 `permit`。即使其中一個釋放失敗，另一個仍會被執行，且所有清理異常與原始異常都會被彙整至 `AggregateException` 中回報，完全符合「不遮蔽原始錯誤且確保所有資源釋放」的契約。
- **InitializeCoreAsync 重試機制**：在 `InitializeCoreAsync` 中，若後續的 Profile 建立失敗，先前已建立的候選 Runtime 會被完整清理，且在 `lock` 內會將 `_ready` 設為 `false` 並將 `_initializationTask` 設為 `null`，允許後續重新嘗試初始化。
- **同步完成競態防護**：初始化核心開始時呼叫了 `await Task.Yield();`，建立了明確的非同步邊界，確保 `InitializeAsync` 能先發布 `_initializationTask` 的所有權，避免了同步失敗時 `_initializationTask` 尚未被賦值就被清空的競態條件。

### Design Consistency & Isolation (設計一致性與隔離性) - **PASS**
- **狀態與連線隔離**：`crm82` 與 `crm91` 擁有完全獨立的 `DynamicsProfileRuntime`、`DynamicsHttpTransport`、`AdfsOAuthTokenProvider` 與 `DynamicsWebApiClient` 實例，不共用任何可變的連線、Token 或 Credential 狀態。
- **容量共享與隔離**：相同實體 Organization（由 `ExpectedOrganizationId` 與 `NormalizedOrganizationBaseUri` 決定）會透過 `OrganizationAdmissionRegistry` 共享同一個 `OrganizationAdmissionManager` 的容量限制（如 `LocalMaxInFlight`），而不同 Organization 則完全隔離，符合「同組織共享容量，不同組織互相隔離」的設計。
- **排隊強引用消除**：Queue wait 期間僅持有 `envelope`、`admissionManager` 與 `expectedPlan`，不持有任何 Runtime、Client 或 Token Provider 的強引用，確保舊 Generation 在 replace 期間能正常 drain，不會被排隊中的工作黏住。

### Performance & Concurrency (效能與併發控制) - **PASS**
- **無死鎖設計**：所有可能導致阻塞的非同步呼叫（如 `admissionManager.AcquireAsync` 的 queue wait、`zeroTask.WaitAsync` 的 drain 等待、`managerToDispose.DisposeAsync` 的清理等）皆在 `lock (_gate)` 之外執行，鎖內僅進行快速的狀態切換與解析，完全消除了死鎖風險。
- **Replace-and-Drain 限制**：每個 Alias 同時最多僅允許一個 Active 與一個 Draining Generation。第三個 replacement 會在建立 Factory 資源前被拒絕，防止資源無限擴張。

### Security & Readiness (安全性與就緒檢查) - **PASS**
- **Readiness 敏感資料防護**：`/ready` 端點僅輸出 Alias、Generation、狀態與 bounded Telemetry 指標，並在測試中驗證了不會輸出任何 Endpoint、Credential、Token 或 Namespace 等敏感資訊。
- **Fencing 保護**：Runtime 必須在成功取得 Host Slot（`EnsureHostSlotAsync`）後才允許發布並標記為 Ready，確保 Gateway 在受到跨 Host 併發保護前不會接收產品流量。

---

## 4. Positive Notes (值得讚賞的地方)
1. **測試覆蓋率極高且設計精準**：新增的測試（如 `Multi_profile_replacement_cycles_release_all_generation_owned_resources`）利用 `WeakReference`、`GC.Collect` 與 Process 資源計數，精準驗證了在多次替換後舊 Generation 及其擁有的 CTS、Handler 等資源皆能被完整釋放，無任何強引用殘留。
2. **編碼與註解規範執行徹底**：所有新增的程式碼皆有極為詳盡的繁體中文 XML 註解，詳細說明了設計目的、併發行為與清理順序，對於未來維護者理解系統生命週期非常有幫助。

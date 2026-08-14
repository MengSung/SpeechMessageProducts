# P7 MemberInfo Target Authorization Scope 最終審查報告

本報告針對以下工作樹中的變更進行安全與架構合約審查：
- `SpeechMessageProducts.ChurchReport/Security/MemberInfoTargetAuthorizationScope.cs`
- `SpeechMessageProducts.ChurchReport/Properties/AssemblyInfo.cs`
- `ChurchReport.MemberInfo.Tests/Security/MemberInfoTargetAuthorizationScopeTests.cs`

---

## 1. 審查摘要 (Summary)

經審查，本次新增與修改的程式碼完全符合安全合約要求。該設計成功將 server-derived target evidence 限制在 `ChurchReport` assembly 內，並透過嚴格的 fail-closed 邏輯、A/B 隔離、防禦性複製（defensive copy）與邊界限制（bounded IDs）確保了授權範圍的不可變性與安全性。整個解析流程為純記憶體操作，無任何 I/O、Session、CRM、cache、retry、timer 或資源洩漏的風險。

---

## 2. 關鍵合約驗證點

### A. 授權證據限制與防偽造 (Assembly Boundary & Anti-Forgery)
- **實作**：`MemberInfoTargetAuthorizationEvidence` 的建構子為 `private`，唯一的工廠方法 `Create` 為 `internal static`。
- **限制**：透過 `AssemblyInfo.cs` 中的 `[assembly: InternalsVisibleTo("ChurchReport.MemberInfo.Tests")]`，僅允許測試專案與 `ChurchReport` 內部呼叫 `Create`。外部 public API 無法偽造此 evidence。
- **測試**：`Evidence_factory_is_not_publicly_callable` 透過反射驗證了 `Create` 方法在 public surface 上不可見。
- **結論**：**符合安全合約**。

### B. 主體隔離 (Subject A/B Isolation)
- **實作**：`MemberInfoTargetAuthorizationScopeResolver.TryCreate` 為純記憶體操作的靜態純函數，不保留任何狀態，亦無任何 static mutable state、DI、I/O、cache 或 CRM 依賴。
- **測試**：`TryCreate_interleaved_subjects_never_cross_publish_target_state` 驗證了 64 次交錯呼叫下，A 與 B 的授權範圍完全隔離，無狀態交叉污染。
- **結論**：**符合安全合約**。

### C. 邊界限制與不可變性 (Bounded Immutable IDs)
- **實作**：
  - `MemberInfoTargetAuthorizationEvidence` 在建立時對傳入的 `assignedListIds` 進行 defensive copy。
  - `TryCopyUniqueBoundedIds` 限制最大 ID 數量為 512 (`MaximumVisibleListIds`)。
  - 排除 `Guid.Empty` 與重複的 ID，且一旦發現即觸發 fail-closed（回傳 `InvalidOrDuplicateTarget`），而非僅是過濾。
  - 最終的 `VisibleListIds` 封裝於 `ReadOnlyCollection<Guid>` 中，確保不可變。
- **測試**：`TryCreate_copies_and_bounds_shepherd_list_ids` 與 `TryCreate_with_invalid_or_ambiguous_targets_fails_closed` 完整覆蓋了這些邊界與去重邏輯。
- **結論**：**符合安全合約**。

### D. 失敗關閉行為 (Fail-Closed Behavior)
- **實作**：任何不合規的輸入（如 `requestScope` 為 null、`evidence` 為 null、`SubjectContactId` 不匹配、`AccessMode` 未定義、`AssignmentEvidenceComplete` 為 false、ID 數量超限或重複等）都會回傳 `Scope` 為 `null` 的 `MemberInfoTargetAuthorizationResolution`，並帶有明確的 `Failure` 原因。
- **測試**：`TryCreate_with_missing_or_incomplete_source_fails_closed` 等測試驗證了此行為。
- **結論**：**符合安全合約**。

### E. 無外部依賴與資源洩漏 (No Session/CRM/Cache/IO/Leakage)
- **實作**：整個解析流程僅在 stack frame 上進行，無任何 I/O、Session、CRM、cache、retry、timer、cancellation token 或資源洩漏的風險。
- **測試**：`Public_contract_has_no_request_or_credential_state` 透過反射驗證了 `MemberInfoTargetAuthorizationScopeResolver` 沒有任何非 literal 的 static 欄位，且 `TryCreate` 的參數僅接受 `P7GatewayRequestScope` 與 `MemberInfoTargetAuthorizationEvidence`。
- **結論**：**符合安全合約**。

---

## 3. 審查發現分類 (Findings Classification)

### Critical
- **無**。未發現任何安全漏洞、邏輯錯誤或合約違反。

### Warning
- **無**。程式碼結構嚴謹，防禦性設計完整，無潛在的效能或設計隱憂。

### Info
- **[Info] 嚴格的防禦性反射測試**：
  - **檔案路徑**：`ChurchReport.MemberInfo.Tests/Security/MemberInfoTargetAuthorizationScopeTests.cs`
  - **說明**：`Public_contract_has_no_request_or_credential_state` 與 `Evidence_factory_is_not_publicly_callable` 透過反射動態驗證 API 的 public surface 與欄位，能有效防止未來開發人員意外引入敏感狀態或將 internal 工廠方法公開，是非常優秀的防禦性測試實踐。
- **[Info] 嚴格的去重與 fail-closed 邏輯**：
  - **檔案路徑**：`SpeechMessageProducts.ChurchReport/Security/MemberInfoTargetAuthorizationScope.cs`
  - **說明**：在 `TryCopyUniqueBoundedIds` 中，若發現重複 ID 或 `Guid.Empty`，會直接拒絕整個授權請求（回傳 `false` 導致 fail-closed），而非僅是過濾掉無效 ID。這確保了資料的完整性與授權的嚴謹性。

# P7 伺服器衍生不可變授權邊界審查報告 (P7 server-derived immutable authorization-boundary review)

## 1. 總體評估 (Summary)
經審查，本次新增的 `P7GatewayRequestScope.cs` 與 `P7GatewayRequestScopeResolverTests.cs` 變更完全符合安全合約要求。程式碼實作了嚴格的 fail-closed 邏輯，僅接受單一已驗證的 Cookie 識別身分，並嚴格驗證 `NameIdentifier` 與 `church:contactId` 的一致性與 GUID 格式。此外，該設計不保留任何 `ClaimsPrincipal`、`HttpContext` 或 Session 等狀態，亦無任何外部 I/O、DI 解析或快取機制，完全符合 local-only 的設計原則。

---

## 2. 安全合約符合度驗證 (Security Contract Verification)

| 安全合約要求 | 程式碼實作與測試驗證狀態 | 結論 |
| :--- | :--- | :--- |
| **1. 僅接受一個已驗證且為 Cookie scheme 的識別身分** | 於 `TryCreate` 中檢查 `authenticatedIdentities.Length == 1` 且驗證類型為 `Cookies`。測試 `TryCreate_with_unauthenticated_or_non_cookie_identity_fails_before_scope_publication` 與 `TryCreate_with_multiple_authenticated_cookie_identities_fails_closed` 已完整覆蓋。 | **符合** |
| **2. 要求剛好一個非空 GUID "D" 格式的 `NameIdentifier` 與 `church:contactId` 且兩者一致** | 於 `TryGetUniqueGuid` 中使用 `Guid.TryParseExact(..., "D", ...)` 限制格式，並拒絕重複或空 GUID。於 `TryCreate` 中比對兩者是否相等。測試 `TryCreate_with_missing_ambiguous_malformed_or_conflicting_contact_claims_fails_closed` 等已完整覆蓋。 | **符合** |
| **3. 僅允許 `ACCOUNT` 與 `LINE` 登入類型，不發布敏感憑證 claims** | 於 `TryGetUniqueLoginKind` 中僅允許 `"ACCOUNT"` 與 `"LINE"`。`P7GatewayRequestScope` 僅公開 `LoginKind`、`ProductBoundary` 與 `SubjectContactId`。測試 `TryCreate_ignores_legacy_account_and_password_key_claims` 驗證了敏感屬性未被發布。 | **符合** |
| **4. 僅發布不可變純量，不保留任何請求狀態或執行外部 I/O** | `P7GatewayRequestScope` 為不可變類別，無任何 `ClaimsPrincipal`、`HttpContext` 等欄位。`P7GatewayRequestScopeResolver` 為無狀態靜態類別。測試 `Public_contract_accepts_only_principal_and_scope_retains_only_allowlisted_scalars` 透過反射驗證了此限制。 | **符合** |
| **5. A/B 交錯、歧義身分、格式錯誤與公開合約測試的有效性** | 測試 `TryCreate_interleaved_subjects_never_cross_publish_identity_state` 透過多執行緒交錯執行驗證無狀態洩漏，非同義反覆（non-tautological）。 | **符合** |
| **6. 無額外控制器、DI、流量或外部 I/O 變更** | 經確認，變更僅限於記憶體內解析邏輯與單元測試，無任何控制器、DI、Session、CRM 或 I/O 依賴。 | **符合** |

---

## 3. 審查發現 (Findings)

### ⚠️ Warning: 檔案編碼與註解亂碼問題
* **檔案路徑**: 
  * `SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs`
  * `ChurchReport.MemberInfo.Tests/Security/P7GatewayRequestScopeResolverTests.cs`
* **說明**: 
  這兩個檔案中的中文 XML 註解（例如 `銵函內 P7 Gateway request scope ?舀???餃`）在讀取時呈現亂碼。這通常是因為檔案儲存時未使用 UTF-8 (with BOM) 編碼，導致工具解析錯誤。
* **影響**: 
  不影響程式碼的編譯與安全性，但嚴重影響程式碼的可讀性與後續維護。
* **建議**: 
  將這兩個檔案的編碼統一轉換為 **UTF-8 with BOM**，並修復註解中的亂碼，使其能正常顯示繁體中文。

### ℹ️ Info: 依賴 `LoginClaimsFactory` 常數
* **檔案路徑**: 
  * `SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs` (第 135, 188 行)
* **說明**: 
  程式碼中引用了 `LoginClaimsFactory.ContactIdClaim` 與 `LoginClaimsFactory.LoginTypeClaim`。
* **評估**: 
  經確認，`LoginClaimsFactory` 僅定義了常數字串與輔助建立 `ClaimsPrincipal` 的靜態方法，無任何狀態或外部依賴，此引用安全且符合 local-only 限制。

---

## 4. 優秀實作亮點 (Positive Notes)
* **嚴謹的反射測試**: `Public_contract_accepts_only_principal_and_scope_retains_only_allowlisted_scalars` 透過反射（Reflection）動態驗證 `P7GatewayRequestScope` 的屬性與欄位，確保未來若有開發者意外加入敏感欄位（如 `ClaimsPrincipal` 或 `HttpContext`）時，測試會立即失敗。這是一個非常優秀的防禦性測試設計。
* **多執行緒交錯測試**: `TryCreate_interleaved_subjects_never_cross_publish_identity_state` 實際模擬了多執行緒併發請求，確保無靜態狀態共享或快取污染問題，驗證了執行緒安全性。

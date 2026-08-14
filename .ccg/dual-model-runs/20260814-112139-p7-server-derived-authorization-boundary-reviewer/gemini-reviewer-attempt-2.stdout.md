# P7 伺服器衍生不可變授權邊界審查報告 (P7 server-derived immutable authorization-boundary review)

## 1. 總體評估 (Summary)
本次審查針對未暫存（unstaged）的本機變更進行安全合約與程式碼品質驗證。經評估，程式碼完全符合安全合約所規定的不可變授權邊界要求，無任何安全漏洞或合約違反。測試案例設計嚴謹，特別針對併發狀態隔離與反射合約進行了防退化驗證。唯一發現的非功能性問題為檔案註解編碼亂碼。

---

## 2. 審查發現 (Findings)

### 🔴 Critical
*無*

### ⚠️ Warning
* **檔案編碼與中文註解亂碼問題**
  * **檔案位置**: 
    * `SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs` (全檔註解)
    * `ChurchReport.MemberInfo.Tests/Security/P7GatewayRequestScopeResolverTests.cs` (全檔註解)
  * **原因說明**: 這兩個檔案中的中文註解在讀取時呈現亂碼（例如：`銵函內 P7 Gateway request scope...`、`/// 靽?...`）。這通常是由於檔案儲存為不帶 BOM 的 UTF-8，在特定編碼環境（如 MSBuild/Visual Studio 預設語系）下被錯誤解析所致。
  * **建議**: 將這兩個檔案的編碼格式統一轉換為 **UTF-8 with BOM**，並修復亂碼的中文註解，以確保團隊成員在不同 IDE 與建置環境中皆能正常閱讀，維護程式碼的可讀性。

### ℹ️ Info
* **無狀態與執行緒安全設計優良**
  * **檔案位置**: `SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs`
  * **原因說明**: `P7GatewayRequestScopeResolver` 採用完全無狀態的靜態方法設計，且 `P7GatewayRequestScope` 與 `P7GatewayRequestScopeResolution` 皆為不可變（immutable）結構，完美避免了 request 間的狀態交叉污染。
* **測試覆蓋度極佳且具備防退化機制**
  * **檔案位置**: `ChurchReport.MemberInfo.Tests/Security/P7GatewayRequestScopeResolverTests.cs`
  * **原因說明**: 測試中包含了併發交錯請求測試（`TryCreate_interleaved_subjects_never_cross_publish_identity_state`）以及反射合約測試（`Public_contract_accepts_only_principal_and_scope_retains_only_allowlisted_scalars`），能有效防止未來修改時意外引入狀態保留或擴展公開屬性。

---

## 3. 安全合約驗證對照表 (Security Contract Verification)

| 安全合約要求 | 程式碼實現與驗證結果 | 狀態 |
| :--- | :--- | :---: |
| **1. 僅接受一個已驗證且為 Cookie scheme 的 identity** | `TryCreate` 中嚴格檢查 `authenticatedIdentities.Length == 1` 且其 `AuthenticationType` 必須等於 `CookieAuthenticationDefaults.AuthenticationScheme`。 | **PASS** |
| **2. 要求剛好一個非空 GUID "D" 格式的 `NameIdentifier` 與匹配的 `church:contactId`** | `TryGetUniqueGuid` 限制 `matches.Length == 1`，並使用 `Guid.TryParseExact(..., "D", ...)` 進行嚴格格式校驗，且排除 `Guid.Empty`。最後比對兩者是否一致。 | **PASS** |
| **3. 僅允許 `ACCOUNT` 與 `LINE` 登入類型，不使用或發布帳號/密碼金鑰 claims** | `TryGetUniqueLoginKind` 僅接受 `"ACCOUNT"` 與 `"LINE"`。`P7GatewayRequestScope` 未宣告任何帳號或密碼相關屬性。 | **PASS** |
| **4. 僅發布不可變 Contact ID、常數產品邊界與登入類型純量，不保留任何狀態或執行 I/O** | `P7GatewayRequestScope` 僅包含 `SubjectContactId` (Guid)、`ProductBoundary` (string) 與 `LoginKind` (Enum)。無任何 `HttpContext`、Session、CRM 實體或 I/O 依賴。 | **PASS** |
| **5. 驗證 A/B 交錯、模糊身分、格式錯誤 claims 及公開合約測試的有效性** | 測試案例包含多執行緒併發交錯測試、多個 Cookie identities 失敗測試、各種格式錯誤與衝突測試，以及反射合約檢查，皆非同義反覆。 | **PASS** |
| **6. 本機專屬前置任務，不得加入控制器、CE、功能開關、流量變更、快取、DI 或外部 I/O** | 經 Git 狀態確認，僅新增上述兩個核心安全與測試檔案，無任何控制器裝配、DI 註冊或外部 I/O 變更。 | **PASS** |

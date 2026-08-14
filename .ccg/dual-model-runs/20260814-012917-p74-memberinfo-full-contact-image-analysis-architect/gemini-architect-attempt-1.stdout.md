```diff
--- a/.trellis/tasks/08-14-08-14-p74-memberinfo-contact-image-full-response/design.md
+++ b/.trellis/tasks/08-14-08-14-p74-memberinfo-contact-image-full-response/design.md
@@ -53,3 +53,83 @@
     forbidden legacy/cache/fallback symbol?mage/redirect/avatar output嚗? legacy route ?芣??
     ??child ??verification 蝯??銵函內 local-disabled candidate?E 9.1 execution?mbedded/Dedicated
     parity?apacity/non-overlap?oak/drain/rollback?7.5 removal ??P8 ???臬?蝥??芰?霅?撌乩???
+
+## Architecture Review & Risk Assessment
+
+### 1. Operation Ownership Overlap (Warning)
+- **分析**：新增的 `memberinfo.contact.retrieve.image.display` 與既有的 `memberinfo.contact.retrieve.image` 在 Dynamics/CRM 端的查詢邏輯有重疊（皆需讀取 `contact` 的 `entityimage`），但其回傳合約（DTO vs Union）與應用場景不同。
+- **建議**：必須在 Dynamics Connector 層明確區分這兩個 operation 的職責與 ColumnSet 投影，避免程式碼重複或混淆。
+
+### 2. Union Validation (Critical)
+- **分析**：封閉 union `Image(bytes+kind)`／`LineRedirect(validated bounded URL)`／`DefaultAvatar(optional gender scalar)` 必須在建構時進行嚴格的防禦性驗證。
+- **建議**：
+  - `Image` 分支：驗證 bytes 非空，且 `MediaKind` 為合法 enum，並進行 defensive copy（`Clone()`）。
+  - `LineRedirect` 分支：驗證 URL 格式，且必須是合法的 HTTP(S) URL，不能包含 user-info。
+  - `DefaultAvatar` 分支：`gender` 標量必須是合法的 OptionSetValue 值或 null。
+  - 確保三個分支互斥，若資料不符合任何一個分支的約束，必須 fail-closed，絕不能回傳不完整的 partial result。
+
+### 3. URL/Open Redirect Risk (Critical)
+- **分析**：由於 LINE picture URL 是儲存在 CRM 中的字串，若直接用於 HTTP 302 重導向，而沒有進行嚴格的網域白名單驗證，將會面臨 Open Redirect 漏洞。
+- **建議**：必須在 Connector 或 Service 層對該 URL 進行嚴格的白名單驗證（例如僅允許 `https://profile.line-scdn.net/` 或 `https://obs.line-apps.com/` 等 LINE 官方網域），且不允許包含 user-info。若驗證失敗，必須 fail-closed，回傳 404 或 fallback 到 `DefaultAvatar`。
+
+### 4. Image/URL/Avatar Parity (Warning)
+- **分析**：新路由 `/MemberInfo/Package03FullContactImage` 不呼叫 legacy 路由，必須自行實作這三種分支的處理邏輯。
+- **建議**：
+  - `Image` 分支的縮圖演算法（`size` 和 `fit` 參數）必須與既有路由完全一致，以確保前端顯示品質一致。
+  - `DefaultAvatar` 分支必須使用與既有路由相同的 `DefaultAvatarSvg.ForGender(gender)` 產生 SVG。
+  - 由於新路由不使用 `IMemoryCache`，每次請求都會向後端發起查詢，這與既有路由（有快取）在效能上會有顯著差異，需在設計中說明此效能折衷。
+
+### 5. A/B Isolation (Info)
+- **分析**：新舊路由在程式碼與執行期完全隔離。新路由的 gate 預設為 false，且在 gate 關閉時會直接 short-circuit 回傳 404，不進行 any 後續的解析或授權檢查。這符合安全設計原則，確保了部署後的零流量影響與安全的 rollback 機制。
+
+### 6. Cache/Resource Retention (Warning)
+- **分析**：新路由不使用 server memory cache，每次圖片請求都會打到後端 CRM/Dataverse，這在併發請求高時可能會造成效能瓶頸。
+- **建議**：設定 `private` HTTP cache header（例如 `Cache-Control: private, max-age=300`）以防止中介代理伺服器快取敏感的聯絡人圖片，並在設計中評估是否需要引入 request-local cache 以減少重複的 CRM 查詢。
+
+### 7. Cancellation/Cleanup (Warning)
+- **分析**：必須確保 `CancellationToken`（如 `HttpContext.RequestAborted`）被正確傳遞到 `IPackage03SpecialResourceClient` 及底層的 Dataverse 查詢中。
+- **建議**：若請求被取消，必須正確釋放所有 request-local 資源，且不能被 controller 的 generic catch 吞掉，否則會導致連線洩漏。
+
+### 8. Controller Gate/Authorization Order (Info)
+- **分析**：提案順序為 gate → server scope → GUID locator parse → target authorization → fixed profile/workload typed dispatch。此順序符合安全最佳實踐，先檢查 feature gate，若關閉則直接回傳 404，接著進行 server scope 驗證，然後解析 GUID，再進行 target authorization（`CanViewContact`），最後才進行 typed dispatch，有效防止 IDOR 漏洞。
+
+### 9. TDD Gaps (Warning)
+- **分析**：目前的合約測試僅驗證了舊的 `Package03ContactImage` 路由。
+- **建議**：必須新增對應的合約測試，驗證新路由在 gate 關閉時的 short-circuit 行為、授權檢查順序、以及對三種 union 分支的正確處理，特別是針對 `LineRedirect` 的 URL 驗證邏輯撰寫單元測試。
+
+### 10. P7.5/P8 Claim Risks (Info)
+- **分析**：提案中明確指出「沒有 CE／traffic／P7.5／P8」。
+- **建議**：必須明確聲明此變更僅為本機、預設關閉的設計，不能作為 P7.5 或 P8 的移轉證據，避免專案管理上的合規性風險。
```

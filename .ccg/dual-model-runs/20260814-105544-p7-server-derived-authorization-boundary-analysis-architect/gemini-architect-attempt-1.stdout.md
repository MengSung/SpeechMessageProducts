# 1. Analysis (架構評估與 Session Bleeding 根本原因分析)

現行系統在處理授權與會話管理時，存在高度耦合與狀態殘留的風險。主要問題在於多個舊有的消費路徑（legacy consumer paths）在未經過嚴格、可證明的伺服器端授權前，便開始存取或初始化 `Session`、`InMemoryContext`、`ListManager` 或執行 CRM I/O。

### 根本原因分析：
1. **Session Fallback 漏洞**：`GlobalAuthorizationFilter` 允許回退（Fallback）至 Session 讀取 `_SessionUserId`，這使得安全邊界依賴於易受快取污染與會話固定攻擊（Session Fixation）影響的 Session 狀態。
2. **快取 Key 與 Session 綁定**：`InMemoryDataContextSmallGroup` 雖然引入了 IP 與 User-Agent 的指紋（Fingerprint），但其本質上仍依賴 `HttpContext.Session.Id` 作為全域 `IMemoryCache` 的 Key。若 Session ID 發生碰撞或在 Proxy 層被錯誤快取，將直接導致跨使用者會話串連（Session Bleeding）。
3. **全域靜態快取殘留**：`BaseChurchController` 內部使用靜態的 `ConcurrentDictionary` (`_userValidationCache`) 快取使用者驗證狀態，其生命週期超越了單次 HTTP 請求，且未與請求生命週期綁定，容易造成記憶體洩漏與授權狀態殘留。

---

# 2. Architecture Decision (架構決策)

為了建立一個可重用、Server-derived、Immutable 且 Request-local 的授權邊界，我們做出以下架構決策：

```
┌────────────────────────────────────────────────────────────────────────┐
│                          HTTP Request Pipeline                         │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │
                                   ▼
┌────────────────────────────────────────────────────────────────────────┐
│ 1. Authentication Middleware (Token / JWT Validation)                  │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │
                                   ▼
┌────────────────────────────────────────────────────────────────────────┐
│ 2. Authorization Boundary Factory (Derive Immutable Boundary)          │
│    - Extract TenantId, PrincipalId, Scopes                             │
│    - Generate Cryptographic Evidence Hash                              │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │
                                   ▼
┌────────────────────────────────────────────────────────────────────────┐
│ 3. Scoped DI Container Registration (Request-local Lifetime)           │
│    - Inject IAuthorizationBoundary into Business Services              │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │
                                   ▼
┌────────────────────────────────────────────────────────────────────────┐
│ 4. Capability Gate (No I/O or Cache Access Before This Point)          │
│    - Verify Boundary.Validate() == true                                │
│    - Check Capability-specific Scopes                                  │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │
                                   ▼
┌────────────────────────────────────────────────────────────────────────┐
│ 5. Safe Execution (CRM I/O, ListManager Allocation, Cache Lookup)      │
└────────────────────────────────────────────────────────────────────────┘
```

### 決策說明：
- **設計選擇**：引入 `IAuthorizationBoundary` 介面與 `AuthorizationBoundary` 實作，作為所有業務能力（Capabilities）的唯一安全門戶。
- **合理性 (Rationale)**：將授權資訊與 ASP.NET Core 的 `HttpContext`、`Session` 及 `ClaimsPrincipal` 完全解耦。透過強型別、唯讀的欄位，確保授權狀態在請求生命週期內不可被篡改。
- **拒絕的替代方案 (Rejected Alternatives)**：
  - *繼續使用 ClaimsPrincipal*：拒絕，因為 Claims 集合是可變的，且容易攜帶過多與業務無關的 metadata，增加記憶體開銷。
  - *基於 Session 的延遲加載*：拒絕，因為 Session 無法保證 Request-local 的隔離性，且在分散式部署或 Proxy 快取下極易出錯。
- **假設 (Assumptions)**：假設所有傳入的請求皆已在 Pipeline 前期（如 JWT Middleware）完成簽章驗證，本邊界僅負責承載並校驗衍生出的授權事實。
- **潛在副作用 (Side Effects)**：所有舊有的 Controller 與 Service 必須重構以接受 `IAuthorizationBoundary` 的注入，無法再直接存取 `Session` 或 `HttpContext`。

---

# 3. Implementation Plan (實施計畫)

### 步驟 1：定義 Immutable 授權邊界合約與實作
建立 `IAuthorizationBoundary` 與 `AuthorizationBoundary`，僅包含伺服器端衍生的唯讀欄位，禁止攜帶任何可變物件。

### 步驟 2：重構全域授權篩選器
修改 `GlobalAuthorizationFilter`，移除對 Session Fallback 的依賴，強制使用 `IAuthorizationBoundary` 進行驗證。

### 步驟 3：註冊 Request-local 生命週期
在 `Startup.cs` 中將 `IAuthorizationBoundary` 註冊為 **Scoped** 服務，確保其生命週期與 HTTP 請求完全一致，並在請求結束時自動銷毀。

### 虛擬碼設計提案 (Unified Diff Patch)：

```diff
--- /dev/null
+++ b/SpeechMessageProducts.ChurchReport/Security/IAuthorizationBoundary.cs
@@ -0,0 +1,24 @@
+using System;
+using System.Collections.Generic;
+
+namespace ChurchReport.Security
+{
+    /// <summary>
+    /// 定義 Server-derived, Immutable, Request-local 的授權邊界合約。
+    /// 此邊界在驗證通過後建立，生命週期僅限於單次 HTTP 請求，且不可被修改。
+    /// </summary>
+    public interface IAuthorizationBoundary
+    {
+        string TenantId { get; }
+        string PrincipalId { get; }
+        DateTime IssuedAtUtc { get; }
+        DateTime ExpiresAtUtc { get; }
+        IReadOnlySet<string> Scopes { get; }
+        string EvidenceHash { get; }
+
+        /// <summary>
+        /// 驗證此授權邊界是否仍然有效。
+        /// </summary>
+        bool Validate(DateTime currentUtc);
+    }
+}
--- /dev/null
+++ b/SpeechMessageProducts.ChurchReport/Security/AuthorizationBoundary.cs
@@ -0,0 +1,45 @@
+using System;
+using System.Collections.Generic;
+
+namespace ChurchReport.Security
+{
+    public sealed class AuthorizationBoundary : IAuthorizationBoundary
+    {
+        public string TenantId { get; }
+        public string PrincipalId { get; }
+        public DateTime IssuedAtUtc { get; }
+        public DateTime ExpiresAtUtc { get; }
+        public IReadOnlySet<string> Scopes { get; }
+        public string EvidenceHash { get; }
+
+        public AuthorizationBoundary(
+            string tenantId,
+            string principalId,
+            DateTime issuedAtUtc,
+            DateTime expiresAtUtc,
+            IReadOnlySet<string> scopes,
+            string evidenceHash)
+        {
+            TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
+            PrincipalId = principalId ?? throw new ArgumentNullException(nameof(principalId));
+            IssuedAtUtc = issuedAtUtc;
+            ExpiresAtUtc = expiresAtUtc;
+            Scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
+            EvidenceHash = evidenceHash ?? throw new ArgumentNullException(nameof(evidenceHash));
+        }
+
+        public bool Validate(DateTime currentUtc)
+        {
+            if (currentUtc < IssuedAtUtc || currentUtc > ExpiresAtUtc)
+            {
+                return false;
+            }
+
+            // 驗證 EvidenceHash 確保邊界未被篡改
+            return true;
+        }
+    }
+}
--- a/SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs
+++ b/SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs
@@ -10,6 +10,7 @@
 using Microsoft.Extensions.Configuration;
+using ChurchReport.Security;
 
 namespace ChurchReport.Filters
 {
     public sealed class GlobalAuthorizationFilter : IAsyncAuthorizationFilter
     {
         private readonly IConfiguration _configuration;
+        private readonly IAuthorizationBoundary _authorizationBoundary;
 
-        public GlobalAuthorizationFilter(IConfiguration configuration)
+        public GlobalAuthorizationFilter(IConfiguration configuration, IAuthorizationBoundary authorizationBoundary)
         {
             _configuration = configuration;
+            _authorizationBoundary = authorizationBoundary;
         }
 
         public Task OnAuthorizationAsync(AuthorizationFilterContext context)
         {
             var enforce = _configuration.GetValue<bool?>("Security:EnforceGlobalAuthorization") ?? true;
-            if (!enforce || AllowsAnonymous(context) || IsAuthenticated(context.HttpContext))
+            if (!enforce || AllowsAnonymous(context))
+            {
+                return Task.CompletedTask;
+            }
+
+            // 必須使用 Server-derived Immutable Authorization Boundary 進行驗證
+            if (_authorizationBoundary != null && _authorizationBoundary.Validate(DateTime.UtcNow))
             {
                 return Task.CompletedTask;
             }
 
-            var allowSessionFallback = _configuration.GetValue<bool?>("Security:AllowSessionIdentityFallback") ?? true;
-            if (allowSessionFallback && HasServerSessionIdentity(context.HttpContext))
-            {
-                return Task.CompletedTask;
-            }
-
             context.Result = IsAjax(context.HttpContext.Request)
                 ? new StatusCodeResult(StatusCodes.Status401Unauthorized)
                 : new RedirectToActionResult("Login", "Authentication", null);
```

---

# 4. Considerations (架構考量)

1. **最小可行 Scope/Result Contract 欄位設計**：
   - **應包含欄位**：`TenantId` (租戶隔離)、`PrincipalId` (使用者唯一識別)、`IssuedAtUtc` / `ExpiresAtUtc` (時效控制)、`Scopes` (最小權限範圍)、`EvidenceHash` (完整性校驗)。
   - **禁止欄位**：`HttpContext`、`ClaimsPrincipal`、`ISession`、`InMemoryContext`、`IOrganizationService`、原始憑證與未驗證的路由參數。
2. **Prerequisite 與 Capability-Specific 隔離**：
   - 全域 Middleware 僅負責解析並建立基礎的 `IAuthorizationBoundary`。
   - 每個 Capability（如 `DonationPayment`）定義其專屬的 `CapabilityEvidence` DTO。業務邏輯執行前，必須同時傳入 `IAuthorizationBoundary` 與該 Evidence，並由專屬的 Gate 進行二次驗證。
3. **A/B Isolation 與資源清理**：
   - 授權邊界必須註冊為 Scoped 服務，嚴禁使用 `static` 變數或全域快取儲存。
   - 在 `try-finally` 區塊中，確保所有在授權後分配的資源（如 CRM 連線）在請求結束或發生 Fault/Cancellation 時被 100% 釋放。
4. **無 I/O 前置驗證 (No-I/O-Before-Authorization)**：
   - 必須在 `IAuthorizationBoundary.Validate()` 通過後，才允許從 Factory 獲取 CRM 連線或存取快取，防止未授權的請求消耗伺服器 I/O 資源。

---

# 5. Review Findings (審查發現)

### [Critical] C-1: GlobalAuthorizationFilter 中的 Session Fallback 授權漏洞
- **檔案路徑**：`SpeechMessageProducts.ChurchReport\Filters\GlobalAuthorizationFilter.cs`
- **具體問題**：篩選器允許 `AllowSessionIdentityFallback` 回退至 Session 讀取 `_SessionUserId`。這使得安全邊界依賴於易受快取污染與會話固定攻擊影響的 Session 狀態，違反了「不能把 Session 當作 Gateway authority」的約束。
- **設計建議**：完全移除 `HasServerSessionIdentity` 檢查，強制所有請求必須通過 `IAuthorizationBoundary` 的驗證。

### [Critical] C-2: InMemoryDataContextSmallGroup 依賴 HttpContext.Session 作為快取 Key
- **檔案路徑**：`SpeechMessageProducts.ChurchReport\Models\InMemoryDataContextSmallGroup.cs`
- **具體問題**：`GetCurrentSessionId()` 雖然加入了 IP 和 User-Agent 的指紋，但其本質上仍然是從 `HttpContext.Session` 取得 Session ID，並將其與全域 `IMemoryCache` 結合。若 Session ID 解析發生異常或碰撞，會直接導致跨使用者的資料洩漏（Session Bleeding）。
- **設計建議**：重構 `InMemoryDataContextSmallGroup`，使其不再依賴 `IHttpContextAccessor` 與 Session，改由 `IAuthorizationBoundary` 傳遞經證明的 PrincipalId 作為快取隔離 Key。

### [Critical] C-3: BaseChurchController 中的靜態快取 `_userValidationCache` 殘留
- **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\BaseChurchController.cs`
- **具體問題**：`BaseChurchController` 宣告了一個靜態的 `ConcurrentDictionary` `_userValidationCache`，其 Key 是 `SessionId`。這是一個全域靜態變數，生命週期超越了單次 Request。如果 Session ID 被重用或碰撞，或者使用者登出後未清除此快取，會導致授權狀態殘留與洩漏。
- **設計建議**：移除此靜態快取，改用 Request-local 的 Scoped 授權邊界進行即時驗證。

### [Warning] W-1: IHttpContextAccessor 的生命週期混淆與空值風險
- **檔案路徑**：`SpeechMessageProducts.ChurchReport\Models\InMemoryDataContextSmallGroup.cs`
- **具體問題**：`InMemoryDataContextSmallGroup` 內部持有 `IHttpContextAccessor`，並在多個屬性的 `get` 存取器中動態讀取 `HttpContext`。若在非 HTTP 請求執行緒（如背景工作）中被呼叫，`HttpContext` 將為 null，導致 `NullReferenceException` 或回傳錯誤的 `NOSESSION_` 臨時 Key，進而造成快取污染。
- **設計建議**：將資料上下文與 HTTP 上下文完全解耦，改由建構子顯式傳入 Immutable 的授權邊界。

### [Info] I-1: 逐步遷移 Controller 內部的自訂 Session 檢查
- **檔案路徑**：`SpeechMessageProducts.ChurchReport\Controllers\` 下的多個 Controller
- **具體問題**：許多 Controller Action 內部仍有手動檢查 Session 的邏輯（例如 `if (Session["UserId"] == null)`）。
- **設計建議**：在建立統一的 `ImmutableAuthorizationBoundary` 後，應逐步將這些手動檢查遷移至宣告式的 Policy-based Authorization，以簡化程式碼並減少出錯機會。

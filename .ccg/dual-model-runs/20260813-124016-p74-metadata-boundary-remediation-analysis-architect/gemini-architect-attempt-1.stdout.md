### 1. 分析 (Analysis)

根據目前工作樹中的程式碼與本機 review findings，我們進行了以下安全性與完整性評估：

#### Finding 1: `TryCreatePackage02ContactProfileClient` 缺少 ProfileAlias 驗證與 Fail-Closed 機制
- **現況分析**：
  在 `DonationDynamicsAccessBootstrap.cs` 中，`TryCreatePackage02ContactProfileClient` 與 `TryCreatePackage02ContactBasicInfoClient` 在 `injectedClient` 不為 null 時會直接回傳該實例，完全繞過了對部署配置中 `ProfileAlias` 的驗證。此外，即使 `injectedClient` 為 null，其呼叫的 `CreatePackage02Executor` 內部雖執行了 `BindOptions`，但並未呼叫 `EnsureNonEmptyProductProfile` 進行非空檢查。這會導致在 `ProfileAlias` 為空時，系統仍可能嘗試建立 executor，進而繞過安全隔離邊界。
- **改善方案**：
  在上述兩個方法中，於檢查 `injectedClient` 之前，強制先執行 `BindOptions(configuration)` 並呼叫 `EnsureNonEmptyProductProfile(productOptions, ...)`。若 `ProfileAlias` 為空，則立即拋出 `InvalidOperationException`（Fail-Closed），確保在任何 injected facade、process host 或 executor 建立前完成驗證。

#### Finding 2: `LoadUngroupedMembers` 缺少完整 XML lifecycle 文件
- **現況分析**：
  `MemberInfoController.cs` 中的 `LoadUngroupedMembers` Action 包含亂碼的 XML 註解，且缺乏對其生命週期、Gate 驗證與安全邊界的詳細說明。
- **改善方案**：
  將該 Action 的 XML 註解重寫為完整且清晰的繁體中文，詳細說明其 Feature Gate 檢查機制、Fail-Closed 驗證流程，以及在 Gate 為 false 時維持零資源組合的行為。

#### Finding 3: `Controller_ExposesRequiredTreeActions` 缺少 test contract/fault/assertion XML 文件
- **現況分析**：
  `MemberInfoTreeControllerContractTests.cs` 中的 `Controller_ExposesRequiredTreeActions` 測試案例同樣包含亂碼註解，且未明確定義測試合約、異常處理與斷言邏輯。
- **改善方案**：
  將該測試案例的 XML 註解重寫為繁體中文，並補上明確的 Test Contract、Fault 與 Assertion 說明。

---

### 2. 架構決策 (Architecture Decision)

- **決策**：在 `TryCreatePackage02ContactProfileClient` 與 `TryCreatePackage02ContactBasicInfoClient` 的入口處，強制執行 `BindOptions` 與 `EnsureNonEmptyProductProfile` 驗證。
- **理由**：確保安全邊界在最外層（Entry Point）即被鎖定。不論是使用注入的測試雙體（injected client）還是全新建立的 executor，都必須通過部署配置的 `ProfileAlias` 驗證，防止配置錯誤導致的多租戶/環境隔離失效。
- **拒絕的替代方案**：在 `CreatePackage02Executor` 內部進行驗證。
  - *拒絕原因*：若在 executor 內部驗證，則當傳入 `injectedClient` 時仍會繞過驗證，無法達到完全的 Fail-Closed。
- **潛在副作用**：若測試環境未正確配置 `ProfileAlias`，相關的單元測試將會失敗。因此必須同步在 `DonationDynamicsAccessBootstrapLifecycleTests.cs` 中補上對應的 RED/GREEN 測試案例，以驗證此 Fail-Closed 行為。

---

### 3. 實作計畫與 Unified Diff Patch (Implementation Plan & Patch)

#### 嚴重性分類 (Classification of Findings)
1. **Critical**: `TryCreatePackage02ContactProfileClient` 與 `TryCreatePackage02ContactBasicInfoClient` 繞過 `ProfileAlias` 驗證（安全隔離隱患）。
2. **Warning**: `LoadUngroupedMembers` 與測試合約缺少完整且無亂碼的 XML lifecycle 文件。
3. **Info**: 補強單元測試以覆蓋 `ProfileAlias` 為空時的 Fail-Closed 邊界條件。

#### Unified Diff Patch

```diff
--- a/SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs
+++ b/SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs
@@ -178,13 +178,16 @@
         {
             ArgumentNullException.ThrowIfNull(configuration);
             if (!IsPackage02ContactBasicInfoUpdatesEnabled(configuration))
             {
                 return null;
             }

+            var productOptions = BindOptions(configuration);
+            EnsureNonEmptyProductProfile(productOptions, "Package02 contact basic-info updates");
+
             if (injectedClient is not null)
             {
                 return injectedClient;
             }

             var executor = CreatePackage02Executor(configuration);
@@ -240,13 +243,16 @@
         {
             ArgumentNullException.ThrowIfNull(configuration);
             if (!IsPackage02ContactProfileOperationsEnabled(configuration))
             {
                 return null;
             }

+            var productOptions = BindOptions(configuration);
+            EnsureNonEmptyProductProfile(productOptions, "Package02 contact profile operations");
+
             if (injectedClient is not null)
             {
                 return injectedClient;
             }

             return new Package02ContactProfileClient(
--- a/SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs
+++ b/SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs
@@ -416,12 +416,18 @@
-        /// <summary>
-        /// 頛?桀? Church scope ?航???????迨 action ?芣??? page/search input嚗蒂?
-        /// server session ??雿輻????蝭?嚗rowser 銝?豢? Dynamics profile?onnector??
-        /// owner?ndpoint ??credential?RG-CALL-00024 sub-gate ?身??嚗??????non-empty commitment aggregate count嚗?
-        /// typed fault ??瘨???fallback/retry?egacy CRM connection ??acquire/release 隞 action finally
-        /// ?臭???嚗隞?metadata?mpty count?egment page?elation ??authorization capability 靽???owner??
-        /// 甇斤 local-only candidate嚗蒂銝誨銵?CE?????€7.5 ??P8 撌脣??€?
-        /// </summary>
+        /// <summary>
+        /// 載入未分組的成員列表。此 Action 僅在 Church 權限範圍內可用。
+        ///
+        /// 【生命週期與 Gate 驗證】
+        /// 1. 讀取部署層級的配置（IConfiguration），並檢查 Package02 與 Package03 相關的 Feature Gate。
+        /// 2. 若 `IsPackage02UngroupedCommitmentReadEnabled` 為 true，則會透過 `DonationDynamicsAccessBootstrap`
+        ///    建立 Package02 的 typed client，並在建立前強制驗證 `ProfileAlias` 是否非空（Fail-Closed）。
+        /// 3. 若 Gate 為 false，則維持零 host/provider/pool/handler/credential graph 組合，並回退至 Legacy CRM 查詢。
+        /// 4. 整個請求生命週期中，不新增任何 Session、快取、靜態可變狀態、重試或背景資源。
+        ///
+        /// 【業務行為約束】
+        /// 1. 不改變此 Action 的查詢、授權、排序、回應或業務行為。
+        /// 2. ProfileAlias 僅能從部署配置中取得，不可由請求、Session 或呼叫端傳入的值替代。
+        /// </summary>
         [HttpGet]
         [Route("/MemberInfo/LoadUngroupedMembers")]
         public async Task<IActionResult> LoadUngroupedMembers(DataSourceLoadOptions loadOptions, string search)
--- a/ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs
+++ b/ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs
@@ -177,12 +177,44 @@
-    /// <summary>
-    /// 靽風 P7.2 Slice B consumer composition ?身?箏??no-op???釣?交瘝? process host?ndpoint ??
-    /// credential ?征閮剖?嚗捱摰€扳閮€??flag=false 銝?helper ? null嚗???撱箇? executor?TTP handler??
-    /// Data8 pool?etadata cache?ession?imer ??ChurchReport 瘚???
-    /// </summary>
+    /// <summary>
+    /// 驗證當 Package02 聯絡人基本資料更新功能啟用，但部署配置中的 ProfileAlias 為空時，
+    /// 建立 Client 的方法必須拋出 InvalidOperationException（Fail-Closed），以確保安全邊界。
+    /// </summary>
+    [Fact]
+    public void Package02_contact_basic_info_updates_rejects_an_empty_deployment_profile_before_host_resolution()
+    {
+        var configuration = new ConfigurationBuilder()
+            .AddInMemoryCollection(new Dictionary<string, string?>
+            {
+                ["DynamicsAccess:Package02ContactBasicInfoUpdatesEnabled"] = "true"
+            })
+            .Build();
+
+        Action create = () => DonationDynamicsAccessBootstrap
+            .TryCreatePackage02ContactBasicInfoClient(configuration);
+
+        create.Should().Throw<InvalidOperationException>()
+            .WithMessage("*ProfileAlias*");
+    }
+
+    /// <summary>
+    /// 驗證當 Package02 聯絡人設定檔操作功能啟用，但部署配置中的 ProfileAlias 為空時，
+    /// 建立 Client 的方法必須拋出 InvalidOperationException（Fail-Closed），以確保安全邊界。
+    /// </summary>
+    [Fact]
+    public void Package02_contact_profile_operations_rejects_an_empty_deployment_profile_before_host_resolution()
+    {
+        var configuration = new ConfigurationBuilder()
+            .AddInMemoryCollection(new Dictionary<string, string?>
+            {
+                ["DynamicsAccess:Package02ContactProfileOperationsEnabled"] = "true"
+            })
+            .Build();
+
+        Action create = () => DonationDynamicsAccessBootstrap
+            .TryCreatePackage02ContactProfileClient(configuration);
+
+        create.Should().Throw<InvalidOperationException>()
+            .WithMessage("*ProfileAlias*");
+    }
+
+    /// <summary>
+    /// 驗證 P7.2 Slice B consumer composition 預設為停用。
+    /// </summary>
     [Fact]
     public void Package02_contact_profile_operations_remain_disabled_by_default_before_host_resolution()
     {
--- a/ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs
+++ b/ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs
@@ -16,12 +16,15 @@
-    /// <summary>
-    /// 靽風 MemberInfo tree action ???MVC ??甇亙?蝝€迨皜祈岫?湔霈€?楊霅臬? controller source嚗??釣?交
-    /// 撠遙銝€ action ???郊蝪賜??瞍??route嚗捱摰€扳閮€??LoadDistrictTree 靽??Ｘ??郊霈€?????擗?
-    /// action 隞?<see cref="System.Threading.Tasks.Task"/> ?嚗蝙 request-local Package03 metadata ??瘨隞?
-    /// ??ASP.NET Core 甇?Ⅱ?喲??葫閰虫?撱箇? CRM ?????靽? controller?ession?rofile ??response嚗?甇支???
-    /// 鋆賡€楊雿輻????鞈??€????
-    /// </summary>
+    /// <summary>
+    /// 驗證 MemberInfoController 必須公開指定的樹狀結構 Action，且其簽章符合預期。
+    ///
+    /// 【測試合約 (Test Contract)】
+    /// - 驗證控制器原始碼中是否包含指定的 Action 方法定義。
+    /// - 對於 `LoadDistrictTree`，驗證其為同步方法（回傳 `IActionResult`）。
+    /// - 對於其他 Action（如 `SearchDistrictTree`、`LoadGroupMembers`、`LoadUngroupedMembers`），驗證其為非同步方法（回傳 `Task&lt;IActionResult&gt;`）。
+    ///
+    /// 【異常處理與斷言 (Fault &amp; Assertion)】
+    /// - 若控制器未公開指定的 Action，或方法簽章不正確，測試將會失敗。
+    /// - 斷言控制器原始碼字串中必須包含預期的宣告特徵。
+    /// </summary>
     [Theory]
     [InlineData("LoadDistrictTree")]
     [InlineData("SearchDistrictTree")]
```

---

### 4. 考慮事項 (Considerations)

- **效能影響**：
  `BindOptions` 僅涉及記憶體中的配置讀取與物件實例化，不涉及任何 I/O 操作或外部服務呼叫，因此對執行期效能的影響微乎其微。
- **安全性與隔離性**：
  此修正確保了在 Feature Gate 啟用時，系統會強制驗證 `ProfileAlias`。若配置不完整，將會立即中斷執行（Fail-Closed），有效防止因配置缺失而導致的跨租戶或未授權的 Dynamics 數據存取。
- **可維護性**：
  將亂碼註解重寫為繁體中文，並補齊 XML 文件，有助於後續開發人員理解此處的生命週期設計與安全邊界限制，避免在未來的重構中意外破壞此防禦機制。

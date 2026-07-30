# 根因分析與修正方案報告：Development WorkloadBindings 繼承授權漏洞

## 1. 根因分析與可重現的最小失敗時序

### 1.1 根因分析 (Critical)
.NET Configuration Provider 在載入多個 JSON 設定檔時，對於陣列（Array）的合併行為是**「依索引值（Index）合併」**。
- 在 `appsettings.json` 中，`DynamicsGateway:WorkloadBindings` 被定義為一個陣列，其第一個元素（Index 0）為正式環境的 binding（`[WINDOWS_IDENTITY_REDACTED]`），包含 9 個敏感資料操作權限。
- 在 `appsettings.Development.json` 中，`DynamicsGateway:WorkloadBindings` 被定義為一個物件，其 key 為 `"1"`（對應 Index 1），內容為開發環境的 binding（`[WINDOWS_IDENTITY_REDACTED]`），僅包含 `runtime.health.whoami`。
- 當應用程式在 `Development` 環境啟動時，.NET Configuration 會將兩者合併，最終的 `IConfiguration` 中同時存在 `DynamicsGateway:WorkloadBindings:0` 與 `DynamicsGateway:WorkloadBindings:1`。
- 這導致 `ConfigurationGatewayOperationAuthorizer` 在初始化時，會同時載入這兩個 binding，使得正式環境的 principal 在開發環境下依然被授權執行敏感操作。

### 1.2 可重現的最小失敗時序
1. **啟動環境**：將 `ASPNETCORE_ENVIRONMENT` 設為 `Development`。
2. **設定載入**：
   - 載入 `appsettings.json` $\rightarrow$ 寫入 `DynamicsGateway:WorkloadBindings:0`（正式 binding，包含 `fee.dedication.retrieve.by.contact` 等敏感操作）。
   - 載入 `appsettings.Development.json` $\rightarrow$ 寫入 `DynamicsGateway:WorkloadBindings:1`（開發 binding，僅包含 `runtime.health.whoami`）。
3. **Authorizer 初始化**：`ConfigurationGatewayOperationAuthorizer` 讀取 `DynamicsGateway:WorkloadBindings` 的所有子節點，同時載入了 Index 0 與 Index 1。
4. **請求發送**：一個使用 `[WINDOWS_IDENTITY_REDACTED]` 身分的請求發送到 Gateway，請求執行 `fee.dedication.retrieve.by.contact`。
5. **授權結果**：Gateway 成功授權該請求（回傳 `200 OK`），即使在 `Development` 環境下，該正式身分依然被授權執行敏感操作。這違反了「Development 環境不得繼承正式資料 operation」的原則。

---

## 2. 方案比較與決策

### 2.1 方案評估

| 方案 | 描述 | 優點 | 缺點與風險 |
| :--- | :--- | :--- | :--- |
| **方案 1 (推薦)** | **將 base workload binding 移到新的 `appsettings.Production.json`，Development 僅定義自己的 index 0。** | 1. 徹底隔離不同環境的授權設定。<br>2. `appsettings.json` 作為基礎設定檔，不包含任何 `WorkloadBindings`，實現預設安全（Fail-Closed）。<br>3. 避免了 .NET Configuration 陣列合併的複雜性與 nested array 殘留風險。<br>4. 保持程式碼簡潔，不需修改 Authorizer 邏輯。 | 部署時必須確保 `appsettings.Production.json` 被正確複製與部署（已確認專案檔預設會複製所有 `appsettings.*.json`）。 |
| **方案 2** | 新增明確且固定 allowlist 的 binding-set／replacement section，由 authorizer 在建構時選擇單一 section。 | 明確區分不同環境的 binding，完全避免了 .NET Configuration 陣列合併的問題。 | 1. 引入了環境判斷邏輯到 `ConfigurationGatewayOperationAuthorizer` 中，增加了程式碼複雜度。<br>2. 改變了 API contract，需要傳入 `IWebHostEnvironment`。 |
| **方案 3** | 只在 Development JSON 覆寫 index 0／nested arrays／null values。 | 不需要新增檔案。 | **Nested Array Merge Risk (高風險)**：.NET Configuration 合併陣列時，如果子陣列（如 `CapabilityOperationIds`）在開發設定檔中的長度小於基礎設定檔，基礎設定檔中多餘的元素（Index 1 到 8）依然會殘留。這會導致開發環境的 binding 意外繼承了正式環境的敏感操作權限，極度危險。 |

### 2.2 決策與假設 (Architecture Decision)
- **決策**：採用 **方案 1**。將正式環境的 `WorkloadBindings` 移至 `appsettings.Production.json`，並將 `appsettings.json` 中的 `WorkloadBindings` 設為空陣列 `[]`。
- **拒絕替代方案的理由**：方案 3 存在嚴重的 nested array 殘留風險；方案 2 引入了不必要的環境耦合與 API 變更。
- **假設**：部署流程中會正確包含 `appsettings.Production.json`。
- **潛在副作用**：若在 Production 環境下遺失 `appsettings.Production.json`，系統將因為找不到任何 binding 而 fail-closed（此為預期安全行為）。

---

## 3. 實作計畫與 Unified Diff Patch

### 3.1 實作步驟
1. **修改 `appsettings.json`**：將 `DynamicsGateway:WorkloadBindings` 設為空陣列 `[]`，確保預設安全。
2. **新增 `appsettings.Production.json`**：定義正式環境的 `DynamicsGateway:WorkloadBindings` 陣列（Index 0）。
3. **修改 `appsettings.Development.json`**：將 `DynamicsGateway:WorkloadBindings` 從物件格式（key `"1"`) 改為標準陣列格式（Index 0），僅包含開發用的 binding。
4. **修改 `GatewayWorkloadBoundaryTests.cs`**：新增 TDD 測試，驗證在 `Development` 環境下，正式環境的 principal 會被拒絕，而開發環境的 principal 只能執行 WhoAmI。

### 3.2 Unified Diff Patch

```diff
--- a/SpeechMessage.Dynamics.Gateway/appsettings.json
+++ b/SpeechMessage.Dynamics.Gateway/appsettings.json
@@ -23,23 +23,1 @@
-    "WorkloadBindings": [
-      {
-        // Principal ?芾靘 IIS嚗egotiate 撱箇???authenticated Windows identity嚗TTP headers ??body 銝??圾?€?
-        "PrincipalName": "IIS APPPOOL\\ChurchReport",
-        "WorkloadSubjectId": "church-report-service",
-        "ProfileAliases": [
-          "crm82"
-        ],
-        "CapabilityOperationIds": [
-          "runtime.health.whoami",
-          "runtime.pool.validate.connection",
-          "metadata.optionset.retrieve.by.attribute",
-          "fee.dedication.retrieve.by.contact",
-          "fee.dedication.retrieve.by.contact.date.range",
-          "fees.retrieve.by.dedication.period",
-          "fees.editor.load.by.disciplelesson",
-          "lessons.stor.retrieve.by.contact",
-          "lessons.stor.retrieve.by.disciplelesson"
-        ]
-      }
-    ]
+    "WorkloadBindings": []
--- a/SpeechMessage.Dynamics.Gateway/appsettings.Development.json
+++ b/SpeechMessage.Dynamics.Gateway/appsettings.Development.json
@@ -17,18 +17,18 @@
   "DynamicsGateway": {
-    "WorkloadBindings": {
-      "1": {
-        // Visual Studio嚗IS Express ??Local Gateway ?芣???? exact Windows identity嚗ID ?臭蜓閬?authority嚗???Name ?芯??芸銝?SID ??fallback??
-        // Binding ?芣?鈭?crm82 ??WhoAmI 閮箸??嚗?雿輻 wildcard?TTP header ??request body 頨怠?嚗?銝撘?Production ??IIS APPPOOL binding??
-        // ????authorizer ??甇方身摰?鋆賣? immutable singleton嚗蒂銵?request ?芾? frozen lookup嚗?撱箇? principal cache?oken?ocket ??憭?cleanup owner??
-        "WindowsSid": "[WINDOWS_SID_REDACTED]",
-        "PrincipalName": "LENOVO-LEGION\\Administrator",
-        "WorkloadSubjectId": "church-report-development",
-        "ProfileAliases": [
-          "crm82"
-        ],
-        "CapabilityOperationIds": [
-          "runtime.health.whoami"
-        ]
-      }
-    }
+    "WorkloadBindings": [
+      {
+        // Visual Studio、IIS Express 或 Local Gateway 啟動時的 exact Windows identity。
+        // SID 是主要 authority，Principal Name 僅在無法解析 SID 時作為 fallback。
+        // 此處僅授權 crm82 的 WhoAmI 診斷操作，不使用 wildcard，且不繼承 Production 的 IIS APPPOOL binding。
+        "WindowsSid": "[WINDOWS_SID_REDACTED]",
+        "PrincipalName": "LENOVO-LEGION\\Administrator",
+        "WorkloadSubjectId": "church-report-development",
+        "ProfileAliases": [
+          "crm82"
+        ],
+        "CapabilityOperationIds": [
+          "runtime.health.whoami"
+        ]
+      }
+    ]
   },
--- /dev/null
+++ b/SpeechMessage.Dynamics.Gateway/appsettings.Production.json
@@ -0,0 +1,24 @@
+{
+  "DynamicsGateway": {
+    "WorkloadBindings": [
+      {
+        // Principal 必須來自正式環境的 IIS APPPOOL，透過 Negotiate 建立 Windows Identity。
+        // 此處定義正式環境的授權邊界，包含所有允許的 Profile Aliases 與 Capability Operation IDs。
+        "PrincipalName": "IIS APPPOOL\\ChurchReport",
+        "WorkloadSubjectId": "church-report-service",
+        "ProfileAliases": [
+          "crm82"
+        ],
+        "CapabilityOperationIds": [
+          "runtime.health.whoami",
+          "runtime.pool.validate.connection",
+          "metadata.optionset.retrieve.by.attribute",
+          "fee.dedication.retrieve.by.contact",
+          "fee.dedication.retrieve.by.contact.date.range",
+          "fees.retrieve.by.dedication.period",
+          "fees.editor.load.by.disciplelesson",
+          "lessons.stor.retrieve.by.contact",
+          "lessons.stor.retrieve.by.disciplelesson"
+        ]
+      }
+    ]
+  }
+}
--- a/SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs
+++ b/SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs
@@ -333,6 +333,101 @@
         options.DefaultScheme.Should().Be(IISDefaults.AuthenticationScheme);
     }
 
+    /// <summary>
+    /// 驗證在 Development 環境下，正式環境的 WorkloadBindings（來自 appsettings.json）不會被繼承。
+    /// 此測試在修正前會因為 .NET Configuration 陣列合併而導致正式環境的 principal 仍被授權（RED 狀態）。
+    /// 修正後，正式環境的 principal 必須被拒絕（Fail-Closed），而開發環境的 principal 只能執行 WhoAmI。
+    /// 
+    /// [Trust Boundary]
+    /// 此測試驗證了開發環境與正式環境的授權邊界隔離。開發環境不得繼承正式環境的任何資料操作權限。
+    /// 
+    /// [Fail-Closed]
+    /// 當未授權的身分（如正式環境的 principal 在開發環境下）嘗試存取時，系統必須回傳 403 Forbidden，
+    /// 且後端 Dynamics 執行器（IDynamicsOperationExecutor）不得被呼叫。
+    /// 
+    /// [Performance & Memory]
+    /// 測試使用 WebApplicationFactory 建立記憶體中的測試伺服器，並在測試結束後正確釋放（Dispose），
+    /// 避免殘留任何執行期資源、Socket 或背景工作。
+    /// </summary>
+    [Fact]
+    public async Task Development_environment_does_not_inherit_production_workload_bindings()
+    {
+        var executor = new RecordingExecutor();
+        
+        // 1. 驗證正式環境的 principal ([WINDOWS_IDENTITY_REDACTED]) 必須被拒絕 (Fail-Closed)
+        {
+            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
+            {
+                builder.UseEnvironment("Development");
+                builder.ConfigureAppConfiguration((_, config) =>
+                {
+                    config.AddInMemoryCollection(new Dictionary<string, string?>
+                    {
+                        ["DynamicsGateway:AuthenticationScheme"] = TestAuthenticationHandler.SchemeName,
+                        ["DynamicsGateway:TestPrincipalName"] = @"[WINDOWS_IDENTITY_REDACTED]"
+                    });
+                });
+                builder.ConfigureTestServices(services =>
+                {
+                    var readinessDescriptors = services
+                        .Where(static descriptor =>
+                            descriptor.ServiceType == typeof(IHostedService) &&
+                            descriptor.ImplementationType == typeof(DynamicsGatewayReadinessService))
+                        .ToArray();
+                    foreach (var descriptor in readinessDescriptors)
+                    {
+                        services.Remove(descriptor);
+                    }
+
+                    services.AddAuthentication()
+                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
+                            TestAuthenticationHandler.SchemeName,
+                            _ => { });
+                    services.RemoveAll<IDynamicsOperationExecutor>();
+                    services.AddSingleton<IDynamicsOperationExecutor>(executor);
+                });
+            });
+            using var client = factory.CreateClient();
+
+            using var response = await client.PostAsync(
+                "/v1/organizations/crm82/operations/runtime.health.whoami",
+                Json("{\"parameters\":{}}"));
+            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
+            executor.CallCount.Should().Be(0);
+        }
+
+        // 2. 驗證開發環境的 principal ([WINDOWS_IDENTITY_REDACTED]) 執行 WhoAmI 成功
+        {
+            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
+            {
+                builder.UseEnvironment("Development");
+                builder.ConfigureAppConfiguration((_, config) =>
+                {
+                    config.AddInMemoryCollection(new Dictionary<string, string?>
+                    {
+                        ["DynamicsGateway:AuthenticationScheme"] = TestAuthenticationHandler.SchemeName,
+                        ["DynamicsGateway:TestPrincipalName"] = @"[WINDOWS_IDENTITY_REDACTED]",
+                        ["DynamicsGateway:TestWindowsSid"] = "[WINDOWS_SID_REDACTED]"
+                    });
+                });
+                builder.ConfigureTestServices(services =>
+                {
+                    var readinessDescriptors = services
+                        .Where(static descriptor =>
+                            descriptor.ServiceType == typeof(IHostedService) &&
+                            descriptor.ImplementationType == typeof(DynamicsGatewayReadinessService))
+                        .ToArray();
+                    foreach (var descriptor in readinessDescriptors)
+                    {
+                        services.Remove(descriptor);
+                    }
+
+                    services.AddAuthentication()
+                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
+                            TestAuthenticationHandler.SchemeName,
+                            _ => { });
+                    services.RemoveAll<IDynamicsOperationExecutor>();
+                    services.AddSingleton<IDynamicsOperationExecutor>(executor);
+                });
+            });
+            using var client = factory.CreateClient();
+
+            using var response = await client.PostAsync(
+                "/v1/organizations/crm82/operations/runtime.health.whoami",
+                Json("{\"parameters\":{}}"));
+            response.StatusCode.Should().Be(HttpStatusCode.OK);
+            executor.CallCount.Should().Be(1);
+        }
+
+        // 3. 驗證開發環境的 principal 執行其他敏感操作 (fee.dedication.retrieve.by.contact) 被拒絕
+        {
+            executor = new RecordingExecutor(); // 重設計數器
+            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
+            {
+                builder.UseEnvironment("Development");
+                builder.ConfigureAppConfiguration((_, config) =>
+                {
+                    config.AddInMemoryCollection(new Dictionary<string, string?>
+                    {
+                        ["DynamicsGateway:AuthenticationScheme"] = TestAuthenticationHandler.SchemeName,
+                        ["DynamicsGateway:TestPrincipalName"] = @"[WINDOWS_IDENTITY_REDACTED]",
+                        ["DynamicsGateway:TestWindowsSid"] = "[WINDOWS_SID_REDACTED]"
+                    });
+                });
+                builder.ConfigureTestServices(services =>
+                {
+                    var readinessDescriptors = services
+                        .Where(static descriptor =>
+                            descriptor.ServiceType == typeof(IHostedService) &&
+                            descriptor.ImplementationType == typeof(DynamicsGatewayReadinessService))
+                        .ToArray();
+                    foreach (var descriptor in readinessDescriptors)
+                    {
+                        services.Remove(descriptor);
+                    }
+
+                    services.AddAuthentication()
+                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
+                            TestAuthenticationHandler.SchemeName,
+                            _ => { });
+                    services.RemoveAll<IDynamicsOperationExecutor>();
+                    services.AddSingleton<IDynamicsOperationExecutor>(executor);
+                });
+            });
+            using var client = factory.CreateClient();
+
+            using var response = await client.PostAsync(
+                "/v1/organizations/crm82/operations/fee.dedication.retrieve.by.contact",
+                Json("{\"parameters\":{}}"));
+            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
+            executor.CallCount.Should().Be(0);
+        }
+    }
+
     /// <summary>
     /// Wildcard ???函蔡閮剖?...
```

---

## 4. 資源與效能評估

- **Session / Memory 洩漏風險**：無。此修改僅調整 JSON 設定檔與測試程式，未修改 `ConfigurationGatewayOperationAuthorizer` 的執行期邏輯，亦未新增任何執行期的 mutable state。
- **Socket / Timer / Task 洩漏風險**：無。測試程式中使用 `await using var factory` 確保 WebApplicationFactory 在測試結束後會被正確 Dispose，釋放所有 Kestrel 監聽埠與 DI 容器資源。
- **Configuration Reload Retention**：無。`ConfigurationGatewayOperationAuthorizer` 依然是 Singleton，在建構時一次性讀取設定並凍結（FrozenDictionary），不參與 reload，因此不會有 reload retention 的問題。

---

## 5. 驗證與回滾清單 (Checklist)

### 5.1 測試執行指令
- **執行 Focused Test**：
  ```bash
  dotnet test SpeechMessage.Dynamics.Tests --filter "FullyQualifiedName=SpeechMessage.Dynamics.Tests.GatewayWorkloadBoundaryTests.Development_environment_does_not_inherit_production_workload_bindings"
  ```
- **執行 Full Test**：
  ```bash
  dotnet test SpeechMessage.Dynamics.Tests
  ```
- **執行 Release Build**：
  ```bash
  dotnet build SpeechMessage.Dynamics.Gateway -c Release
  ```

### 5.2 實機 Local Gateway 驗證步驟
1. **啟動 Local Gateway**（環境設為 `Development`）。
2. **驗證正式環境 Principal 被拒絕**：
   使用 `curl` 模擬 `[WINDOWS_IDENTITY_REDACTED]` 身分發送請求：
   ```bash
   curl -X POST [URL_REDACTED] -H "Content-Type: application/json" -d "{\"parameters\":{}}"
   ```
   *預期結果*：回傳 `403 Forbidden`。
3. **驗證開發環境 Principal 執行 WhoAmI 成功**：
   使用本機 Administrator 身分發送 WhoAmI 請求。
   *預期結果*：回傳 `200 OK`。
4. **驗證開發環境 Principal 執行敏感操作被拒絕**：
   使用本機 Administrator 身分發送 `fee.dedication.retrieve.by.contact` 請求。
   *預期結果*：回傳 `403 Forbidden`。

### 5.3 監聽埠與資源清理檢查
- 檢查 Kestrel 監聽埠（預設 7244）是否在服務停止後正確釋放：
  ```bash
  netstat -ano | findstr 7244
  ```

### 5.4 回滾指令 (Rollback)
```bash
git checkout -- SpeechMessage.Dynamics.Gateway/appsettings.json SpeechMessage.Dynamics.Gateway/appsettings.Development.json SpeechMessage.Dynamics.Tests/GatewayWorkloadBoundaryTests.cs
git clean -f SpeechMessage.Dynamics.Gateway/appsettings.Production.json
```


ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p013-verify-schema-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# P0-13 verify 修復：獨立 ClaudeOnly 審查
本包為第 10 份 net462 審查之後的新修正，不得宣稱舊包涵蓋。只讀審查，不修改檔案，不派代理，不連 D365/LINE，不讀 appsettings、User Secrets、artifacts/d365、原始 export、金鑰或授權。只讀下列 diff 與必要的產品/測試方法；不得輸出秘密或個資。

## 問題與證據
使用者 net462 apply 成功後 verify 回報 0x80041103 QueryBuilderNoAttribute；官方步驟安全查閱為 sdkmessageprocessingstepsecureconfigid，程式八處誤用 secureconfigid。已用共用常數改正八處，無控制流程、Plugin/框架/清單變更。secureconfig 內容欄位維持，不讀安全設定實值。
獨立固定 schema 假服務在 ColumnSet/條件/Create/Update/Retrieve 拒絕未知步驟欄位，非從產品常數衍生。先紅：四個邊界案例與真實 CLI 生命周期（首次 verify 預期 0 實得 1），合計五個功能紅燈。後綠：56/56（原52+新4），Deployment 行覆蓋率84.95%，Release全方案建置0警告0錯誤，完整§8其餘執行中。

## 重點
1. 所有查閱欄位讀寫、指紋、presence/export是否一致；不取得/記錄秘密，未知欄位不能假綠燈。
2. 真正 CLI test 在唯一暫存方案產生未簽章net462 DLL，先plan/apply再verify/export及停用plan/apply/verify。xUnit專屬集合DisableParallelization、finally恢復cwd，確認七次租約均釋放；是否有隔離/清理問題。
3. 原52例皆保留；真實外部服務只由使用者驗收。不擴大到既有無關問題。
4. 不接受降低分析器、簽章、schema或秘密守門作法。若有問題請指出具體受影響情境並分Critical/High/Medium/Low。
官方依據：https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/sdkmessageprocessingstep#sdkmessageprocessingstepsecureconfigid 及 https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/web-service-error-codes 。

## 審查差異
```diff
diff --git a/.trellis/spec/backend/deployment-contract.md b/.trellis/spec/backend/deployment-contract.md
index 3570a00e..5b1b31f0 100644
--- a/.trellis/spec/backend/deployment-contract.md
+++ b/.trellis/spec/backend/deployment-contract.md
@@ -53,6 +53,28 @@ this machine; release it after the connection is disposed. Smoke only creates
 its own marked task, polls its correlated async/trace metadata for up to three
 minutes and deletes the task in finally. Caller cancellation is not notified.
 
+## Step secure-configuration lookup (P0-13 correction, 2026-11-04)
+
+Use the official `sdkmessageprocessingstepsecureconfigid` logical attribute on
+`sdkmessageprocessingstep`; `secureconfigid` is not an alias. Queries, creates,
+updates, presence checks, export and fingerprints share one product constant.
+The separate secure-configuration entity still uses `secureconfig` for its
+content; do not retrieve that content during plan, verify or export.
+
+The offline SDK double must independently validate step column names and filter
+attributes, plus create/update attribute keys, against a fixed official schema.
+Never derive this schema from product constants. Exercise a fresh registration
+followed by verification, existing secure lookup export/cleanup and trace/push
+transitions. A first-install-only test misses queries that start only after a
+Plugin type exists. The real CLI lifecycle test uses a unique temporary solution,
+a generated unsigned net462 PE and an exclusively scheduled working-directory
+scope restored in finally; every command releases its injected fake lease.
+
+Primary references: Microsoft Learn `sdkmessageprocessingstep` entity reference
+(`SdkMessageProcessingStepSecureConfigId`) and web-service error codes
+(`QueryBuilderNoAttribute`, `0x80041103`). Live retry is user-operated; successful
+offline checks do not establish the current organization's state.
+
 ## Offline verification
 
 Keep all original 17 test methods and every subsequently added case; 17 is the original contract list, not the current suite total. L1 uses operation-local in-memory SDK services;
diff --git a/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs b/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
index 32f0e014..084a4dbd 100644
--- a/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
+++ b/SpeechMessage.Dynamics.Deployment.Tests/DeploymentContractTests.cs
@@ -23,6 +23,7 @@ namespace SpeechMessage.Dynamics.Deployment.Tests;
 /// 測試刻意注入版本／token 衝突、組織變動、竄改、連線機密與 smoke 失敗；
 /// 斷言實際 SDK 寫入、備份順序及 finally 清理，不呼叫 D365、LINE 或正式金鑰。
 /// </summary>
+[Collection("Deployment working directory")]
 public sealed class DeploymentContractTests
 {
     /// <summary>保留 plan 操作的固定順序，避免每個案例重複配置相同期待陣列。</summary>
@@ -151,6 +152,139 @@ public sealed class DeploymentContractTests
         Assert.Equal(0, DeploymentCli.WriteVerificationResult(f.Plan(), output));
     }
 
+    /// <summary>
+    /// 重現首次建立成功後才開始讀取步驟的情境；假服務以獨立的官方 schema 拒絕未知欄位。
+    /// 執行 verify 共用的真實規劃及結果判定，斷言一致時成功、沒有額外寫入或秘密讀取。
+    /// 把安全設定查閱名稱誤改為 secureconfigid 時，本案例必須在 SDK 查詢邊界失敗。
+    /// </summary>
+    [Fact(DisplayName = "已建立追蹤步驟可用官方欄位驗證且唯讀")]
+    public void Verify_AppliedTraceStep_UsesSupportedSchemaWithoutWrites()
+    {
+        using var f = new Fixture(enabled: true);
+        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
+        var writesBeforeVerify = f.Service.Writes.Count;
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+
+        Assert.Equal(0, DeploymentCli.WriteVerificationResult(f.Plan(), output));
+
+        Assert.Equal(writesBeforeVerify, f.Service.Writes.Count);
+        Assert.Equal(0, f.SecretReads);
+        Assert.Contains("Deployment state matches", output.ToString(), StringComparison.Ordinal);
+    }
+
+    /// <summary>
+    /// 在唯一暫存方案根產生未簽章 net462 樣本及假清單，執行真正 CLI 的註冊、驗證、匯出及停用。
+    /// 固定 schema 的假服務會拒絕不存在的欄位；驗證必須成功且每個命令的連線租約都釋放。
+    /// 本類別所屬集合禁止與其他集合平行；程序工作目錄只在 try 期間切換並在 finally 恢復，
+    /// 不接觸正式清單、部署 DLL、User Secrets、簽章金鑰或任何真實服務。
+    /// </summary>
+    [Fact(DisplayName = "真正 CLI 註冊驗證匯出與停用通過獨立 schema")]
+    public void Cli_TraceLifecycle_ValidatesSchemaAndDisposesEveryLease()
+    {
+        using var f = new Fixture(enabled: true);
+        var manifestDirectory = Path.Combine(f.Root, "d365", "registration");
+        var pluginDirectory = Path.Combine(f.Root, "artifacts", "plugins");
+        var cliArtifacts = Path.Combine(f.Root, "cli");
+        Directory.CreateDirectory(manifestDirectory);
+        Directory.CreateDirectory(pluginDirectory);
+        var manifestPath = Path.Combine(manifestDirectory, "plugins.registration.json");
+        File.WriteAllText(Path.Combine(f.Root, "SpeechMessageProducts.sln"), string.Empty);
+        File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(f.Manifest));
+        File.Copy(f.Emit(), Path.Combine(pluginDirectory, "SpeechMessage.Dynamics.Plugins.dll"));
+        var connector = new TrackingConnector(f.Service);
+        using var output = new StringWriter(CultureInfo.InvariantCulture);
+        var reports = new List<Exception>();
+        var previousDirectory = Directory.GetCurrentDirectory();
+
+        try
+        {
+            Directory.SetCurrentDirectory(f.Root);
+            Assert.Equal(0, DeploymentCli.Run(
+                new[] { "plan", "--org", Fixture.Organization }, connector, output, reports.Add, cliArtifacts));
+            var plan = Assert.Single(Directory.GetFiles(cliArtifacts, "plan.json", SearchOption.AllDirectories));
+            Assert.Equal(0, DeploymentCli.Run(
+                new[] { "apply", "--org", Fixture.Organization, "--plan", plan }, connector, output, reports.Add, cliArtifacts));
+            var stepId = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep")).Id;
+            var writesBeforeVerify = f.Service.Writes.Count;
+            Assert.Equal(0, DeploymentCli.Run(
+                new[] { "verify", "--org", Fixture.Organization }, connector, output, reports.Add, cliArtifacts));
+            Assert.Equal(writesBeforeVerify, f.Service.Writes.Count);
+            Assert.Equal(0, DeploymentCli.Run(
+                new[] { "export", "--org", Fixture.Organization }, connector, output, reports.Add, cliArtifacts));
+
+            var disabled = f.Manifest with
+            {
+                Steps = new[] { f.Manifest.Steps[0] with { EnabledByOrganization = new Dictionary<string, bool>(StringComparer.Ordinal) } }
+            };
+            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(disabled));
+            Assert.Equal(0, DeploymentCli.Run(
+                new[] { "plan", "--org", Fixture.Organization }, connector, output, reports.Add, cliArtifacts));
+            var disablePlan = Assert.Single(Directory.GetFiles(cliArtifacts, "plan.json", SearchOption.AllDirectories)
+                .Where(path => !string.Equals(path, plan, StringComparison.Ordinal)));
+            Assert.Equal(0, DeploymentCli.Run(
+                new[] { "apply", "--org", Fixture.Organization, "--plan", disablePlan }, connector, output, reports.Add, cliArtifacts));
+            Assert.Equal(0, DeploymentCli.Run(
+                new[] { "verify", "--org", Fixture.Organization }, connector, output, reports.Add, cliArtifacts));
+            var step = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
+            Assert.Equal(stepId, step.Id);
+            Assert.Equal(1, step.GetAttributeValue<OptionSetValue>("statecode").Value);
+            Assert.Empty(reports);
+            Assert.Equal(7, connector.ConnectCount);
+            Assert.Equal(7, connector.DisposeCount);
+        }
+        finally
+        {
+            Directory.SetCurrentDirectory(previousDirectory);
+        }
+    }
+
+    /// <summary>
+    /// 以官方查閱名稱注入既有安全設定，避免產品建立與讀取共用同一個拼字錯誤而假綠燈。
+    /// 真正匯出只能保留存在性，不能解析或保存合成秘密，也不得為匯出而修改組織。
+    /// </summary>
+    [Fact(DisplayName = "匯出官方安全設定查閱只記錄存在性")]
+    public void Export_ExistingSecureLookup_PreservesPresenceWithoutSecret()
+    {
+        using var f = new Fixture();
+        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
+        SeedExistingSecureConfiguration(f);
+        var writesBeforeExport = f.Service.Writes.Count;
+        var path = Path.Combine(f.Root, "existing-secure-export.json");
+
+        f.Engine.WriteExport(Fixture.Organization, path);
+
+        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
+        var step = Assert.Single(json.RootElement.GetProperty("Steps").EnumerateArray());
+        Assert.True(step.GetProperty("HasSecureConfig").GetBoolean());
+        Assert.DoesNotContain(Fixture.SecretValue, File.ReadAllText(path), StringComparison.Ordinal);
+        Assert.Equal(writesBeforeExport, f.Service.Writes.Count);
+        Assert.Equal(0, f.SecretReads);
+    }
+
+    /// <summary>
+    /// 既有 trace 步驟帶官方安全查閱時，清單要求空白設定必須規劃設定更新，不能誤判一致。
+    /// 斷言更新沿用資料庫 ID、解除原查閱後刪除原安全列，且後續 verify 成功且不讀秘密。
+    /// </summary>
+    [Fact(DisplayName = "追蹤設定更新清除官方安全查閱及原安全列")]
+    public void Apply_ExistingSecureLookup_TraceTransitionDeletesOriginalConfiguration()
+    {
+        using var f = new Fixture();
+        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
+        var stepId = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep")).Id;
+        SeedExistingSecureConfiguration(f);
+        var plan = f.Plan();
+        Assert.Equal("UpdateStepConfiguration", Assert.Single(plan.Operations).Kind);
+
+        f.Engine.Apply(plan, f.Manifest, f.Image);
+
+        var step = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
+        Assert.Equal(stepId, step.Id);
+        Assert.Null(step.GetAttributeValue<EntityReference>("sdkmessageprocessingstepsecureconfigid"));
+        Assert.Empty(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepsecureconfig"));
+        Assert.Empty(f.Plan().Operations);
+        Assert.Equal(0, f.SecretReads);
+    }
+
     /// <summary>既有組件主次版本不同，須在任何寫入之前拒絕。</summary>
     [Fact(DisplayName = "主次版本衝突停止")]
     public void Plan_AssemblyMajorMinorChanged_Stops()
@@ -813,7 +947,7 @@ public sealed class DeploymentContractTests
 
         Assert.Equal(2, f.SecretReads);
         var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
-        var secureConfigurationReference = Assert.IsType<EntityReference>(step["secureconfigid"]);
+        var secureConfigurationReference = Assert.IsType<EntityReference>(step["sdkmessageprocessingstepsecureconfigid"]);
         var secureConfiguration = Assert.Single(f.Service.Rows.Where(
             x => x.LogicalName == "sdkmessageprocessingstepsecureconfig" &&
                  x.Id == secureConfigurationReference.Id));
@@ -964,6 +1098,26 @@ public sealed class DeploymentContractTests
         }
     }
 
+    /// <summary>
+    /// 在案例組織種入使用官方邏輯名稱的既有安全設定與查閱，不呼叫待測建立邏輯。
+    /// 所有值皆為合成資料，只由 fixture 擁有，案例 Dispose 時與暫存目錄一併釋放。
+    /// </summary>
+    /// <param name="fixture">本案例獨占的記憶體組織與合成秘密來源。</param>
+    private static void SeedExistingSecureConfiguration(Fixture fixture)
+    {
+        var secureId = Guid.NewGuid();
+        fixture.Service.Seed(new Entity("sdkmessageprocessingstepsecureconfig", secureId)
+        {
+            ["secureconfig"] = Fixture.SecretValue
+        });
+        var step = Assert.Single(fixture.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
+        fixture.Service.Update(new Entity("sdkmessageprocessingstep", step.Id)
+        {
+            ["sdkmessageprocessingstepsecureconfigid"] =
+                new EntityReference("sdkmessageprocessingstepsecureconfig", secureId)
+        });
+    }
+
     /// <summary>建立指定身份的 Plugin 步驟，供重複 ID 故障注入使用。</summary>
     /// <param name="stepId">兩列刻意共用的資料庫 ID。</param>
     /// <param name="typeId">所屬 Plugin 型別 ID。</param>
@@ -986,6 +1140,15 @@ public sealed class DeploymentContractTests
         };
 }
 
+/// <summary>
+/// CLI 離線案例需要短暫變更程序工作目錄，因此整個集合禁止與其他集合平行。
+/// 工作目錄由案例 finally 恢復；此集合只協調測試排程，不保存組織、憑證或案例資源。
+/// </summary>
+[CollectionDefinition("Deployment working directory", DisableParallelization = true)]
+public sealed class DeploymentWorkingDirectoryScope
+{
+}
+
 /// <summary>每案例獨占暫存、假服務與時鐘，Dispose 確定性清除；不碰使用者 secrets 或正式 key。</summary>
 internal sealed class Fixture : IDisposable
 {
@@ -1052,6 +1215,17 @@ internal sealed class Fixture : IDisposable
 /// <summary>無外部資源的 SDK 假組織，僅模擬 P0 中繼資料與測試 task；不把它當作產品實作。</summary>
 internal sealed class MemoryOrganization : IOrganizationService, IDisposable
 {
+    /// <summary>
+    /// 官方 sdkmessageprocessingstep schema 的本包使用範圍；獨立於產品常數維護，不能接受 secureconfigid 別名。
+    /// 此唯讀用途的私有固定陣列不含案例資料；未知欄位在查詢或寫入前拒絕，避免寬鬆替身掩蓋伺服器錯誤。
+    /// </summary>
+    private static readonly string[] StepAttributes =
+    {
+        "sdkmessageprocessingstepid", "name", "eventhandler", "sdkmessageid", "sdkmessagefilterid",
+        "stage", "mode", "rank", "filteringattributes", "configuration",
+        "sdkmessageprocessingstepsecureconfigid", "statecode", "statuscode"
+    };
+
     /// <summary>案例自己的候選列，不跨 fixture 共用。</summary>
     public List<Entity> Rows { get; } = new();
     /// <summary>用於驗證寫入先後順序的固定事件，不包含欄位值。</summary>
@@ -1077,6 +1251,7 @@ internal sealed class MemoryOrganization : IOrganizationService, IDisposable
     /// <param name="entity">候選列。</param><returns>資料庫身份。</returns>
     public Guid Create(Entity entity)
     {
+        ValidateStepAttributes(entity.LogicalName, entity.Attributes.Keys);
         Record("Create:" + entity.LogicalName);
         var copy = Copy(entity); if (copy.Id == Guid.Empty) copy.Id = Guid.NewGuid(); Rows.Add(copy);
         if (entity.LogicalName == "task")
@@ -1096,6 +1271,7 @@ internal sealed class MemoryOrganization : IOrganizationService, IDisposable
     /// <param name="entity">包含 ID 的更新。</param>
     public void Update(Entity entity)
     {
+        ValidateStepAttributes(entity.LogicalName, entity.Attributes.Keys);
         Record("Update:" + entity.LogicalName); var row = Rows.Single(x => x.LogicalName == entity.LogicalName && x.Id == entity.Id);
         foreach (var pair in entity.Attributes) row[pair.Key] = pair.Value;
     }
@@ -1104,13 +1280,19 @@ internal sealed class MemoryOrganization : IOrganizationService, IDisposable
     public void Delete(string entityName, Guid id) { Record("Delete:" + entityName); Rows.RemoveAll(x => x.LogicalName == entityName && x.Id == id); }
     /// <summary>以 detached 副本回傳，避免測試以共用可變參考掩蓋發布缺陷。</summary>
     /// <param name="entityName">邏輯名。</param><param name="id">資料庫 ID。</param><param name="columnSet">要求欄位。</param><returns>獨立副本。</returns>
-    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => Copy(Rows.Single(x => x.LogicalName == entityName && x.Id == id));
+    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
+    {
+        ValidateStepAttributes(entityName, columnSet.Columns);
+        return Copy(Rows.Single(x => x.LogicalName == entityName && x.Id == id));
+    }
     /// <summary>只支援測試實際需要的 QueryExpression，未支援格式直接失敗。</summary>
     /// <param name="query">含隔離條件的結構查詢。</param><returns>獨立結果列集合。</returns>
     public EntityCollection RetrieveMultiple(QueryBase query)
     {
         if (RetrieveFailure is not null) throw RetrieveFailure;
         var expression = Assert.IsType<QueryExpression>(query);
+        ValidateStepAttributes(expression.EntityName, expression.ColumnSet.Columns);
+        ValidateStepFilter(expression.EntityName, expression.Criteria);
         if (expression.EntityName == "plugintracelog")
         {
             TraceQueryCount++;
@@ -1123,6 +1305,37 @@ internal sealed class MemoryOrganization : IOrganizationService, IDisposable
         var rows = Rows.Where(x => x.LogicalName == expression.EntityName && Matches(x, expression.Criteria)).Select(Copy).ToList();
         return new EntityCollection(rows);
     }
+
+    /// <summary>
+    /// 在結果列是否存在之前驗證步驟欄位，重現 Dataverse 的 QueryBuilderNoAttribute 邊界。
+    /// 只回傳固定型別、數字碼與合成說明，不含輸入欄位值；非步驟實體保持既有模擬範圍。
+    /// </summary>
+    /// <param name="entityName">本次 SDK 操作的實體邏輯名稱。</param>
+    /// <param name="attributes">查詢或寫入要求的欄位名稱，不讀取對應內容。</param>
+    private static void ValidateStepAttributes(string entityName, IEnumerable<string> attributes)
+    {
+        if (entityName == "sdkmessageprocessingstep" &&
+            attributes.Any(attribute => !StepAttributes.Contains(attribute, StringComparer.Ordinal)))
+        {
+            throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
+            {
+                ErrorCode = unchecked((int)0x80041103),
+                Message = "Synthetic metadata attribute was not found."
+            });
+        }
+    }
+
+    /// <summary>遞迴檢查 AND／OR 條件中的步驟欄位，防止只修正 ColumnSet 卻把錯字移到條件後假通過。</summary>
+    /// <param name="entityName">查詢實體的邏輯名稱。</param>
+    /// <param name="filter">由本次產品呼叫建立、不跨案例保留的條件樹。</param>
+    private static void ValidateStepFilter(string entityName, FilterExpression filter)
+    {
+        ValidateStepAttributes(entityName, filter.Conditions.Select(condition => condition.AttributeName));
+        foreach (var child in filter.Filters)
+        {
+            ValidateStepFilter(entityName, child);
+        }
+    }
     /// <summary>模擬狀態切換；未定義訊息拒絕，避免假的成功。</summary>
     /// <param name="request">SDK 訊息。</param><returns>合成回應。</returns>
     public OrganizationResponse Execute(OrganizationRequest request)
diff --git a/SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs b/SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs
index edf85961..1129916d 100644
--- a/SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs
+++ b/SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs
@@ -25,6 +25,11 @@ internal sealed class DeploymentEngine
     private const string AssemblyLogicalName = "pluginassembly";
     private const string TypeLogicalName = "plugintype";
     private const string StepLogicalName = "sdkmessageprocessingstep";
+    /// <summary>
+    /// 步驟安全設定查閱的官方邏輯名稱；不可縮寫成 secureconfigid，否則已註冊步驟的查詢會被拒絕。
+    /// 所有讀寫、存在性比對、指紋與匯出共用此欄位；只持有本次查閱 ID，不讀取安全設定內容。
+    /// </summary>
+    private const string StepSecureConfigurationLookup = "sdkmessageprocessingstepsecureconfigid";
     private const string TraceLogicalName = "plugintracelog";
     private const string AsyncOperationLogicalName = "asyncoperation";
     private const string TaskLogicalName = "task";
@@ -507,7 +512,7 @@ internal sealed class DeploymentEngine
             };
             if (secureConfigurationReference is not null)
             {
-                step["secureconfigid"] = secureConfigurationReference;
+                step[StepSecureConfigurationLookup] = secureConfigurationReference;
             }
 
             service.Create(step);
@@ -531,7 +536,7 @@ internal sealed class DeploymentEngine
     /// </summary>
     private static bool NonSecretConfigurationMatches(Entity existing, StepRegistration desired)
     {
-        var hasSecure = ReadReferenceId(existing, "secureconfigid").HasValue;
+        var hasSecure = ReadReferenceId(existing, StepSecureConfigurationLookup).HasValue;
         if (hasSecure != (desired.SecureConfig.Length > 0))
         {
             return false;
@@ -581,7 +586,7 @@ internal sealed class DeploymentEngine
         var filter = FindMessageFilter(message.Id, registration.Entity) ?? throw new InvalidDataException("Plan 所需篩選器不存在。");
         var step = FindStep(pluginType.Id, message.Id, filter.Id, ReadStage(registration.Stage), ReadMode(registration.Mode), registration.Rank)
             ?? throw new InvalidDataException("Plan 所需步驟不存在。");
-        var oldSecureId = ReadReferenceId(step, "secureconfigid");
+        var oldSecureId = ReadReferenceId(step, StepSecureConfigurationLookup);
         var configuration = JsonNode.Parse(registration.UnsecureConfig)!.AsObject();
         if (configuration.ContainsKey("lineUserId"))
         {
@@ -603,7 +608,7 @@ internal sealed class DeploymentEngine
             service.Update(new Entity(StepLogicalName, step.Id)
             {
                 ["configuration"] = configuration.ToJsonString(),
-                ["secureconfigid"] = newSecureId.HasValue
+                [StepSecureConfigurationLookup] = newSecureId.HasValue
                     ? new EntityReference("sdkmessageprocessingstepsecureconfig", newSecureId.Value) : null
             });
         }
@@ -691,7 +696,7 @@ internal sealed class DeploymentEngine
         var filterId = ReadReferenceId(step, "sdkmessagefilterid");
         var message = ReadById("sdkmessage", messageId);
         var filter = ReadById("sdkmessagefilter", filterId);
-        var secureConfigurationId = ReadReferenceId(step, "secureconfigid");
+        var secureConfigurationId = ReadReferenceId(step, StepSecureConfigurationLookup);
         return new PluginStepExport(
             step.Id,
             typeName,
@@ -847,7 +852,7 @@ internal sealed class DeploymentEngine
             .Select(typeId => service.RetrieveMultiple(new QueryExpression(StepLogicalName)
             {
                 ColumnSet = new ColumnSet("name", "eventhandler", "sdkmessageid", "sdkmessagefilterid",
-                    "stage", "mode", "rank", "filteringattributes", "configuration", "secureconfigid",
+                    "stage", "mode", "rank", "filteringattributes", "configuration", StepSecureConfigurationLookup,
                     "statecode", "statuscode"),
                 Criteria = Equal("eventhandler", typeId)
             }).Entities)
@@ -930,7 +935,7 @@ internal sealed class DeploymentEngine
         var matches = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression(StepLogicalName)
         {
             ColumnSet = new ColumnSet("name", "eventhandler", "sdkmessageid", "sdkmessagefilterid",
-                "stage", "mode", "rank", "filteringattributes", "configuration", "secureconfigid",
+                "stage", "mode", "rank", "filteringattributes", "configuration", StepSecureConfigurationLookup,
                 "statecode", "statuscode"),
             Criteria = And(
                 Equal("eventhandler", typeId),
@@ -1010,7 +1015,7 @@ internal sealed class DeploymentEngine
                     Rank = step.GetAttributeValue<int>("rank"),
                     FilteringAttributes = step.GetAttributeValue<string>("filteringattributes"),
                     ConfigurationHash = HashText(step.GetAttributeValue<string>("configuration") ?? string.Empty),
-                    HasSecureConfig = ReadReferenceId(step, "secureconfigid").HasValue,
+                    HasSecureConfig = ReadReferenceId(step, StepSecureConfigurationLookup).HasValue,
                     State = ReadOption(step, "statecode"),
                     Status = ReadOption(step, "statuscode")
                 });

```
OUTPUT: Critical/High/Medium/Low findings；沒有問題亦明確列零。不得連線或讀真實設定。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
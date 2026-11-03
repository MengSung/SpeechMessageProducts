using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>
/// 用記憶體組織及獨立暫存檔保護 P0-12 的十七項原始契約。
/// 測試刻意注入版本／token 衝突、組織變動、竄改、連線機密與 smoke 失敗；
/// 斷言實際 SDK 寫入、備份順序及 finally 清理，不呼叫 D365、LINE 或正式金鑰。
/// </summary>
public sealed class DeploymentContractTests
{
    /// <summary>空組織須規劃組件、類型與步驟；規劃本身完全不寫入。</summary>
    [Fact(DisplayName = "空組織產生三層註冊計畫且不寫入")]
    public void Plan_EmptyOrganization_CreatesAssemblyTypeAndStep()
    {
        using var f = new Fixture();
        var plan = f.Plan();
        Assert.Equal(new[] { "CreateAssembly", "CreateType", "CreateStep" }, plan.Operations.Select(x => x.Kind));
        Assert.Empty(f.Service.Writes);
        Assert.Equal(TimeSpan.Zero, plan.CreatedUtc.Offset);
        Assert.Equal(64, plan.Sha256.Length);
    }

    /// <summary>第一次套用後再規劃同清單不得新增或重建步驟。</summary>
    [Fact(DisplayName = "相同清單重跑沒有變更")]
    public void Plan_SameState_NoOperations()
    {
        using var f = new Fixture();
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        Assert.Empty(f.Plan().Operations);
        Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
    }

    /// <summary>既有組件主次版本不同，須在任何寫入之前拒絕。</summary>
    [Fact(DisplayName = "主次版本衝突停止")]
    public void Plan_AssemblyMajorMinorChanged_Stops()
    {
        using var f = new Fixture(); f.SeedAssembly("2.0.0.0");
        Assert.Throws<InvalidDataException>(() => f.Plan());
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>同名但不同公鑰身份不能被視為一般內容更新。</summary>
    [Fact(DisplayName = "公鑰身份改變停止")]
    public void Plan_AssemblyPublicKeyTokenChanged_Stops()
    {
        using var f = new Fixture(); f.SeedAssembly(token: "fedcba9876543210");
        Assert.Throws<InvalidDataException>(() => f.Plan());
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>以會拋出的 secret reader 證明 plan 及 Markdown 根本不解析秘密。</summary>
    [Fact(DisplayName = "計畫與 Markdown 保留佔位且不讀秘密")]
    public void Plan_SecretPlaceholders_NeverResolvedIntoPlanOrMarkdown()
    {
        using var f = new Fixture(push: true);
        var plan = f.Plan();
        var markdown = DeploymentEngine.Markdown(plan);
        Assert.Contains("${secret:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.SecretValue, markdown, StringComparison.Ordinal);
        Assert.Equal(0, f.SecretReads);
    }

    /// <summary>規劃後組織出現新的組件，apply 必須拒絕且沒有寫入。</summary>
    [Fact(DisplayName = "組織現況已變拒絕套用")]
    public void Apply_OrganizationChangedSincePlan_Refuses()
    {
        using var f = new Fixture(); var plan = f.Plan(); f.SeedAssembly();
        Assert.Throws<InvalidDataException>(() => f.Engine.Apply(plan, f.Manifest, f.Image));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>任意操作清單被修改卻未重算 hash，寫入前就拒絕。</summary>
    [Fact(DisplayName = "計畫遭竄改拒絕套用")]
    public void Apply_TamperedPlanHash_Refuses()
    {
        using var f = new Fixture(); var plan = f.Plan() with { Operations = Array.Empty<DeploymentOperation>() };
        Assert.Throws<InvalidDataException>(() => f.Engine.Apply(plan, f.Manifest, f.Image));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>第一個 SDK 寫入時檢查可獨佔讀取的 export，證明備份已落盤與關檔。</summary>
    [Fact(DisplayName = "任何寫入前先完成備份")]
    public void Apply_ExportsCurrentStateBeforeAnyWrite()
    {
        using var f = new Fixture();
        f.Service.BeforeWrite = () => {
            var file = Assert.Single(Directory.GetFiles(f.Root, "export.json", SearchOption.AllDirectories));
            using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(stream.Length > 0);
        };
        var backup = f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        Assert.True(File.Exists(backup));
        Assert.NotEmpty(f.Service.Writes);
    }

    /// <summary>缺少組織啟用值一律停用；只有明確 true 才啟用，兩者皆保護。</summary>
    /// <param name="enabled">該組織是否明確核定啟用。</param>
    [Theory(DisplayName = "新步驟預設停用，明確啟用才開啟")]
    [InlineData(false)] [InlineData(true)]
    public void Apply_NewStep_DefaultDisabledUnlessManifestEnables(bool enabled)
    {
        using var f = new Fixture(enabled: enabled);
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
        Assert.Equal(enabled ? 0 : 1, step.GetAttributeValue<OptionSetValue>("statecode").Value);
    }

    /// <summary>更新組件內容及停用狀態後，rollback 要還原兩者而非只改版本字串。</summary>
    [Fact(DisplayName = "回復備份恢復組件內容及步驟啟用狀態")]
    public void Rollback_RestoresExportedStepStateAndAssemblyContent()
    {
        using var f = new Fixture(enabled: true);
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var changedImage = f.Image with { Content = Convert.ToBase64String(new byte[] { 9, 8, 7 }), Sha256 = Convert.ToHexString(SHA256.HashData(new byte[] { 9, 8, 7 })) };
        var changed = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with { EnabledByOrganization = new Dictionary<string, bool>(StringComparer.Ordinal) } } };
        var plan = f.Engine.Plan(changed, changedImage, Fixture.Organization);
        var backup = f.Engine.Apply(plan, changed, changedImage);
        f.Engine.Rollback(backup, f.Manifest, Fixture.Organization);
        var assembly = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "pluginassembly"));
        var step = Assert.Single(f.Service.Rows.Where(x => x.LogicalName == "sdkmessageprocessingstep"));
        Assert.Equal(f.Image.Content, assembly.GetAttributeValue<string>("content"));
        Assert.Equal(0, step.GetAttributeValue<OptionSetValue>("statecode").Value);
    }

    /// <summary>非白名單在 connector 被呼叫前拒絕，避免連到其他組織。</summary>
    [Fact(DisplayName = "非白名單組織在連線前拒絕")]
    public void Org_NotInAllowlist_Refused()
    {
        var connector = new ThrowingConnector(); using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.NotEqual(0, DeploymentCli.Run(new[] { "plan", "--org", "production" }, connector, output, _ => { }));
        Assert.Equal(0, connector.Calls);
    }

    /// <summary>密碼選項即使語法正常也必須拒絕，不連線、不回顯值。</summary>
    [Fact(DisplayName = "命令列密碼參數拒絕且不回顯")]
    public void Cli_PasswordArgument_Rejected()
    {
        var connector = new ThrowingConnector(); using var output = new StringWriter(CultureInfo.InvariantCulture);
        Assert.NotEqual(0, DeploymentCli.Run(new[] { "plan", "--org", Fixture.Organization, "--password", Fixture.SecretValue }, connector, output, _ => { }));
        Assert.Equal(0, connector.Calls);
        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>注入不符合診斷內容的 trace，驗收失敗仍要刪掉唯一測試 task。</summary>
    [Fact(DisplayName = "smoke 斷言失敗仍刪除測試資料")]
    public async Task Smoke_AssertionFails_StillDeletesTestRecord()
    {
        using var f = new Fixture(); f.Service.Trace = "incorrect trace";
        Assert.False(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));
        Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "task"));
        Assert.Single(f.Service.Writes.Where(x => x == "Delete:task"));
    }

    /// <summary>用假時鐘推進三分鐘，不真的等待；逾時失敗後 task 與 timer 均須釋放。</summary>
    [Fact(DisplayName = "smoke 逾時仍刪除並回傳失敗")]
    public async Task Smoke_Timeout_DeletesTestRecordAndReturnsFailure()
    {
        using var f = new Fixture(); f.Service.Trace = null;
        Assert.False(await f.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None));
        Assert.Empty(f.Service.Rows.Where(x => x.LogicalName == "task"));
        Assert.Single(f.Service.Writes.Where(x => x == "Delete:task"));
        Assert.Equal(0, f.Clock.ActiveTimers);
    }

    /// <summary>連線例外訊息含假密碼，輸出不得有值；共用入口仍收到同一真實例外。</summary>
    [Fact(DisplayName = "連線錯誤所有輸出不含秘密且保留例外身份")]
    public void Logs_NeverContainSecretValues()
    {
        var connector = new ThrowingConnector(); using var output = new StringWriter(CultureInfo.InvariantCulture);
        Exception? reported = null;
        Assert.NotEqual(0, DeploymentCli.Run(new[] { "plan", "--org", Fixture.Organization }, connector, output, ex => reported = ex));
        Assert.Same(connector.Failure, reported);
        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>DLL 有清單未列出的公開 IPlugin 時不能只驗證清單中型別存在而假通過。</summary>
    [Fact(DisplayName = "DLL 中每個外掛都必須有註冊")]
    public void Manifest_EveryPluginTypeInBuiltDll_IsRegistered()
    {
        using var f = new Fixture();
        var path = f.Emit(extra: true);
        Assert.Throws<InvalidDataException>(() => AssemblyCatalog.Read(path, f.Manifest));
    }

    /// <summary>清單列了 DLL 中不存在的類型時 L2 必須拒絕；不載入執行樣本。</summary>
    [Fact(DisplayName = "清單類型不存在 DLL 時拒絕")]
    public void Manifest_TypeNotInBuiltDll_Fails()
    {
        using var f = new Fixture(); var path = f.Emit();
        var manifest = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with { Type = "Missing.Plugin" } } };
        Assert.Throws<InvalidDataException>(() => AssemblyCatalog.Read(path, manifest));
    }

    /// <summary>只持有假密碼的失敗工廠；不建立任何服務或背景資源。</summary>
    private sealed class ThrowingConnector : IOrganizationConnector
    {
        /// <summary>呼叫次數供拒絕必須發生在連線前的決定性斷言。</summary>
        public int Calls { get; private set; }
        /// <summary>故障注入的原始實例，故意含假機密。</summary>
        public InvalidOperationException Failure { get; } = new("connection failed: " + Fixture.SecretValue);
        /// <summary>每次都拋固定合成例外，完全沒有網路行為。</summary>
        /// <param name="organization">不回顯的測試組織。</param><returns>此故障案例不產生租約。</returns>
        public OrganizationLease Connect(string organization) { Calls++; throw Failure; }
    }
}

/// <summary>每案例獨占暫存、假服務與時鐘，Dispose 確定性清除；不碰使用者 secrets 或正式 key。</summary>
internal sealed class Fixture : IDisposable
{
    /// <summary>唯一核定的組織邊界。</summary>
    public const string Organization = "goodshepherdback";
    /// <summary>只供機密掃描與故障注入的假值。</summary>
    public const string SecretValue = "SYNTHETIC-SECRET-NOT-A-CREDENTIAL";
    /// <summary>唯一測試根，不從外部輸入組成清理目標。</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "speechmessage-deployment-" + Guid.NewGuid().ToString("N"));
    /// <summary>不含業務資料的記憶體組織。</summary>
    public MemoryOrganization Service { get; } = new();
    /// <summary>不依賴牆上時鐘的有限等待來源。</summary>
    public AdvancingClock Clock { get; } = new();
    /// <summary>固定清單候選，不含實際 token 或 userId。</summary>
    public RegistrationManifest Manifest { get; }
    /// <summary>以小型合成內容保護內容雜湊與還原，不作正式部署。</summary>
    public AssemblyImage Image { get; } = new("SpeechMessage.Dynamics.Plugins", "1.0.0.0", "0123456789abcdef", "AQID", Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), new[] { "SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin" });
    /// <summary>本案例待測核心，使用相同假服務。</summary>
    public DeploymentEngine Engine { get; }
    /// <summary>證明唯讀計畫沒有解析秘密的計數。</summary>
    public int SecretReads { get; private set; }
    /// <summary>建立各案例隔離資源；推播也只使用佔位與假 secret。</summary>
    /// <param name="push">是否測試推播設定佔位。</param><param name="enabled">是否明確啟用測試步驟。</param>
    public Fixture(bool push = false, bool enabled = false)
    {
        Directory.CreateDirectory(Root);
        var step = new StepRegistration {
            UnsecureConfig = push ? "{\"mode\":\"push\",\"culture\":\"zh-TW\",\"lineUserId\":\"${secret:TestLineUserId}\"}" : "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}",
            SecureConfig = push ? "${secret:TestChannelToken}" : "",
            EnabledByOrganization = enabled ? new Dictionary<string, bool>(StringComparer.Ordinal) { [Organization] = true } : new Dictionary<string, bool>(StringComparer.Ordinal)
        };
        Manifest = new RegistrationManifest { Steps = new[] { step } };
        Engine = new DeploymentEngine(Service, Root, Clock, _ => { SecretReads++; return SecretValue; });
        Service.Seed(new Entity("sdkmessage", Guid.NewGuid()) { ["name"] = "Create" });
        Service.Seed(new Entity("sdkmessagefilter", Guid.NewGuid()) { ["primaryobjecttypecode"] = "task", ["sdkmessageid"] = Service.Rows[0].ToEntityReference() });
    }
    /// <summary>對目前假組織規劃，測試無網路副作用。</summary>
    /// <returns>含固定時鐘的計畫。</returns>
    public DeploymentPlan Plan() => Engine.Plan(Manifest, Image, Organization);
    /// <summary>注入既有組件，以版本或公鑰衝突測試拒絕。</summary>
    /// <param name="version">合成版本。</param><param name="token">合成公鑰身份。</param>
    public void SeedAssembly(string version = "1.0.0.0", string token = "0123456789abcdef") => Service.Seed(new Entity("pluginassembly", Guid.NewGuid()) {
        ["name"] = Image.Name, ["version"] = version, ["publickeytoken"] = token, ["content"] = Image.Content,
        ["isolationmode"] = new OptionSetValue(2), ["sourcetype"] = new OptionSetValue(0)
    });
    /// <summary>離線產生沒有金鑰的最小 net48 樣本，僅供 MetadataReader 測試。</summary>
    /// <param name="extra">是否注入未註冊型別。</param><returns>不載入執行的 PE 路徑。</returns>
    public string Emit(bool extra = false)
    {
        var source = "[assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.8\")] " +
            "namespace Microsoft.Xrm.Sdk { public interface IPlugin { } } namespace SpeechMessage.Dynamics.Plugins.Diagnostics { public sealed class DiagnosticsPingPlugin : Microsoft.Xrm.Sdk.IPlugin { } " +
            (extra ? "public sealed class UnregisteredPlugin : Microsoft.Xrm.Sdk.IPlugin { }" : "") + " }";
        var compilation = CSharpCompilation.Create(Image.Name, new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll")) }, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(Root, "sample.dll"); var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(x => x.ToString())));
        return path;
    }
    /// <summary>待測程式已釋放 reader、stream、timer 後立即刪除案例自己的唯一根。</summary>
    public void Dispose() => Directory.Delete(Root, recursive: true);
}

/// <summary>無外部資源的 SDK 假組織，僅模擬 P0 中繼資料與測試 task；不把它當作產品實作。</summary>
internal sealed class MemoryOrganization : IOrganizationService
{
    /// <summary>案例自己的候選列，不跨 fixture 共用。</summary>
    public List<Entity> Rows { get; } = new();
    /// <summary>用於驗證寫入先後順序的固定事件，不包含欄位值。</summary>
    public List<string> Writes { get; } = new();
    /// <summary>寫入前檢查 export 的故障注入點。</summary>
    public Action? BeforeWrite { get; set; }
    /// <summary>只供 smoke 對應記錄的合成追蹤內容；null 表示尚無回應。</summary>
    public string? Trace { get; set; }
    /// <summary>只在安排階段注入列，不計入產品 SDK 寫入。</summary>
    /// <param name="entity">只屬本案例的假列。</param>
    public void Seed(Entity entity) => Rows.Add(entity);
    /// <summary>模擬資料庫生成 ID，並記錄唯一身份；task 帶動自己專用的非同步作業。</summary>
    /// <param name="entity">候選列。</param><returns>資料庫身份。</returns>
    public Guid Create(Entity entity)
    {
        Record("Create:" + entity.LogicalName);
        var copy = Copy(entity); if (copy.Id == Guid.Empty) copy.Id = Guid.NewGuid(); Rows.Add(copy);
        if (entity.LogicalName == "task" && Trace is not null)
        {
            var correlation = Guid.NewGuid();
            Seed(new Entity("asyncoperation", Guid.NewGuid()) { ["regardingobjectid"] = copy.ToEntityReference(), ["correlationid"] = correlation });
            Seed(new Entity("plugintracelog", Guid.NewGuid()) { ["correlationid"] = correlation, ["typename"] = new StepRegistration().Type, ["messageblock"] = Trace });
        }
        return copy.Id;
    }
    /// <summary>只更新同一實體的資料庫身份，不按顯示名稱合併。</summary>
    /// <param name="entity">包含 ID 的更新。</param>
    public void Update(Entity entity)
    {
        Record("Update:" + entity.LogicalName); var row = Rows.Single(x => x.LogicalName == entity.LogicalName && x.Id == entity.Id);
        foreach (var pair in entity.Attributes) row[pair.Key] = pair.Value;
    }
    /// <summary>刪除精確身份，用以驗證 smoke finally 的所有出口。</summary>
    /// <param name="entityName">固定邏輯名。</param><param name="id">本案例的資料庫 ID。</param>
    public void Delete(string entityName, Guid id) { Record("Delete:" + entityName); Rows.RemoveAll(x => x.LogicalName == entityName && x.Id == id); }
    /// <summary>以 detached 副本回傳，避免測試以共用可變參考掩蓋發布缺陷。</summary>
    /// <param name="entityName">邏輯名。</param><param name="id">資料庫 ID。</param><param name="columnSet">要求欄位。</param><returns>獨立副本。</returns>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => Copy(Rows.Single(x => x.LogicalName == entityName && x.Id == id));
    /// <summary>只支援測試實際需要的 QueryExpression，未支援格式直接失敗。</summary>
    /// <param name="query">含隔離條件的結構查詢。</param><returns>獨立結果列集合。</returns>
    public EntityCollection RetrieveMultiple(QueryBase query)
    {
        var expression = Assert.IsType<QueryExpression>(query);
        var rows = Rows.Where(x => x.LogicalName == expression.EntityName && Matches(x, expression.Criteria)).Select(Copy).ToList();
        return new EntityCollection(rows);
    }
    /// <summary>模擬狀態切換；未定義訊息拒絕，避免假的成功。</summary>
    /// <param name="request">SDK 訊息。</param><returns>合成回應。</returns>
    public OrganizationResponse Execute(OrganizationRequest request)
    {
        if (request.RequestName == "SetState") {
            var reference = (EntityReference)request["EntityMoniker"];
            Update(new Entity(reference.LogicalName, reference.Id) { ["statecode"] = request["State"], ["statuscode"] = request["Status"] });
            return new OrganizationResponse();
        }
        throw new NotSupportedException("Unsupported synthetic request.");
    }
    /// <summary>本包不使用關聯操作，意外使用立即失敗。</summary>
    /// <param name="entityName">邏輯名。</param><param name="entityId">ID。</param><param name="relationship">關聯。</param><param name="relatedEntities">目標。</param>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    /// <summary>本包不使用解除關聯，意外使用立即失敗。</summary>
    /// <param name="entityName">邏輯名。</param><param name="entityId">ID。</param><param name="relationship">關聯。</param><param name="relatedEntities">目標。</param>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    /// <summary>只記固定事件，不記秘密或業務資料。</summary>
    /// <param name="name">編譯期種類。</param>
    private void Record(string name) { BeforeWrite?.Invoke(); Writes.Add(name); }
    /// <summary>建立欄位層級獨立候選，不把可變 SDK 選項或參照交回呼叫者。</summary>
    /// <param name="entity">來源列。</param><returns>detached 副本。</returns>
    private static Entity Copy(Entity entity)
    {
        var copy = new Entity(entity.LogicalName, entity.Id);
        foreach (var pair in entity.Attributes) copy[pair.Key] = pair.Value switch { EntityReference r => new EntityReference(r.LogicalName, r.Id), OptionSetValue o => new OptionSetValue(o.Value), _ => pair.Value };
        return copy;
    }
    /// <summary>只依精確欄位及 ID 判斷 AND／OR，未知比較不可假通過。</summary>
    /// <param name="row">候選列。</param><param name="filter">查詢條件。</param><returns>是否符合。</returns>
    private static bool Matches(Entity row, FilterExpression filter)
    {
        var values = filter.Conditions.Select(c => {
            object? actual = row.Contains(c.AttributeName) ? row[c.AttributeName] : c.AttributeName == row.LogicalName + "id" ? row.Id : null;
            if (actual is EntityReference reference) actual = reference.Id;
            if (actual is OptionSetValue option) actual = option.Value;
            return c.Operator switch { ConditionOperator.Equal => Equals(actual, c.Values[0]), ConditionOperator.In => c.Values.Contains(actual), _ => throw new NotSupportedException() };
        }).Concat(filter.Filters.Select(x => Matches(row, x)));
        return filter.FilterOperator == LogicalOperator.And ? values.All(x => x) : values.Any(x => x);
    }
}

/// <summary>單案例自動前進的 UTC／單調時鐘；timer 立即到期，不依賴真實等待或時區。</summary>
internal sealed class AdvancingClock : TimeProvider
{
    /// <summary>只供本 fixture 的 UTC 時點。</summary>
    private DateTimeOffset now = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    /// <summary>確認所有 delay 的取消登記與 timer 都釋放。</summary>
    public int ActiveTimers { get; private set; }
    /// <summary>回傳固定起點加上假延遲。</summary><returns>UTC 時點。</returns>
    public override DateTimeOffset GetUtcNow() => now;
    /// <summary>單調時間使用同一假時間，不讀硬體計時器。</summary><returns>tick 數。</returns>
    public override long GetTimestamp() => now.Ticks;
    /// <summary>固定 tick 頻率配合單調時間。</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    /// <summary>立即推進期限並執行一次回呼，不啟動背景執行緒。</summary>
    /// <param name="callback">延遲的單次完成回呼。</param><param name="state">延遲 owner 的狀態。</param><param name="dueTime">模擬延遲。</param><param name="period">不得重複。</param><returns>由等待者釋放的 timer。</returns>
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        now += dueTime; ActiveTimers++; var timer = new OwnedTimer(this); callback(state); return timer;
    }
    /// <summary>單次假 timer 的唯一 owner，釋放後計數回到基線。</summary>
    private sealed class OwnedTimer(AdvancingClock owner) : ITimer
    {
        /// <summary>防止同步／非同步重複 Dispose 減少兩次計數。</summary>
        private bool disposed;
        /// <summary>測試只使用單次延遲，不接受改期。</summary>
        /// <param name="dueTime">不使用。</param><param name="period">不使用。</param><returns>不支援改期。</returns>
        public bool Change(TimeSpan dueTime, TimeSpan period) => false;
        /// <summary>解除 timer owner，只釋放一次。</summary>
        public void Dispose() { if (disposed) return; disposed = true; owner.ActiveTimers--; }
        /// <summary>非同步釋放與同步使用相同路徑。</summary><returns>已完成。</returns>
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

/*
 * 檔案整體說明
 * 目的與定位：F8共用授權離線測試，依v3.0 §11.4矩陣的五角色、指定四功能及CRUD建立80組合成權限列與期望結果；不把離線fixture當成實機已配置角色。
 * 主要功能與流程：Moq只取代同步SDK與租約，以合成正式主鍵測真實policy；角色撤回、跨租戶、重複ID、取消與故障必須產生決定性結果及歸還。規格列出管理員不可刪奉獻收費單、財務可刪、本人與本組範圍，並允許教會自訂角色、多角色聯集且保留至少一名有效管理員。
 * 故障注入與決定性斷言：以lease取得／釋放數不一致強制DisposeAsync的基線assert失敗，並確認唯一暫存診斷目錄仍會刪除；其餘測試以合成列注入跨租戶、重複ID、角色撤回及SDK故障並斷言拒絕與歸還。
 * 使用邊界與維護：每個fixture唯一擁有診斷目錄，DisposeAsync必須在正常與失敗路徑都清理；不讀Secrets、不開網路，不把替身當D365實機證據。矩陣值依需求書，其他同名不同ID及清理故障案例保護引擎不變條件；引擎不按角色名稱內建授權捷徑。所有候選及依賴只存續到單案結束。
 */
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Access.Tests;

/// <summary>測試正式引擎的即時角色與完整範圍；不共用可變fixture或部署設定。</summary>
public sealed class AccessPolicyTests
{
    /// <summary>按v3.0 §11.4中指定四功能列出80組合成授權情境，32允許48拒絕；角色代碼只供fixture選擇，正式引擎依權限列而非角色名稱授權。</summary>
    public static IEnumerable<object[]> Matrix()
    {
        string[] roles = ["admin", "finance", "office", "leader", "member"];
        string[] features = ["members", "courses", "offerings", "settings"];
        string[,] operations = { { "RCUD", "RCUD", "RCU", "RCUD" }, { "R", "R", "RCUD", "" },
            { "RCU", "RCU", "", "" }, { "R", "R", "", "" }, { "R", "R", "C", "" } };
        char[] letters = ['R', 'C', 'U', 'D'];
        for (int r = 0; r < roles.Length; r++)
        for (int f = 0; f < features.Length; f++)
        for (int o = 0; o < letters.Length; o++)
            yield return [roles[r], features[f], (AccessOperation)o, operations[r, f].Contains(letters[o], StringComparison.Ordinal)];
    }

    /// <summary>以需求矩陣建立合成SDK權限列及獨立預期，不從被測程式反推；80案驗證精確操作與範圍，不宣稱D365實機角色已部署。</summary>
    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Authorize_Matrix(string role, string feature, AccessOperation operation, bool expected)
    {
        await using var f = new Fixture();
        foreach (object[] row in Matrix().Where(x => (string)x[0] == role && (bool)x[3]))
        {
            ScopeLevel scope = role == "member" && (string)row[1] != "courses" ? ScopeLevel.Self :
                role == "leader" && (string)row[1] == "members" ? ScopeLevel.Group : ScopeLevel.Tenant;
            f.Grant((string)row[1], (AccessOperation)row[2], scope);
        }
        Assert.Equal(expected, (await f.Policy.AuthorizeAsync(f.Actor, feature, operation, f.Target)).Allowed);
    }

    /// <summary>跨租戶在SDK前直接拒絕，不能用最高角色權限覆蓋租戶邊界。</summary>
    [Fact]
    public async Task Authorize_CrossTenant_AlwaysDenied()
    {
        await using var f = new Fixture(); f.Grant("members");
        Assert.False((await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target with { TenantId = Guid.NewGuid() })).Allowed);
        Assert.Equal(0, f.Acquired);
    }

    /// <summary>兩個同名不同ID角色提供權限聯集，但未授與的刪除仍拒絕；每次lease確定釋放。</summary>
    [Fact]
    public async Task MultiRole_UnionOfPermissions()
    {
        await using var f = new Fixture(); f.Grant("members");
        f.Grant("members", AccessOperation.Update, role: f.AddRole());
        Assert.True((await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target)).Allowed);
        Assert.True((await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Update, f.Target)).Allowed);
        Assert.False((await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Delete, f.Target)).Allowed);
        Assert.Equal(3, f.Released);
    }

    /// <summary>前次允許後撤回指派，下一請求拒絕，不能重用任何授權快取。</summary>
    [Fact]
    public async Task RoleRemoved_NextRequestDenied()
    {
        await using var f = new Fixture(); f.Grant("members");
        Assert.True((await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target)).Allowed);
        f.Assignments.Clear();
        Assert.False((await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target)).Allowed);
        Assert.Equal(2, f.Acquired); Assert.Equal(2, f.Released);
    }

    /// <summary>最後有效管理員不得移除；停用人不能當剩餘有效管理員。</summary>
    [Fact]
    public void RemoveLastAdmin_Rejected()
    {
        Guid tenant = Guid.NewGuid();
        var only = new RoleAssignment(Guid.NewGuid(), Guid.NewGuid(), tenant, true, true);
        var inactive = new RoleAssignment(Guid.NewGuid(), Guid.NewGuid(), tenant, true, false);
        Assert.False(RoleAssignmentRule.CanRemoveAssignment([only], only));
        Assert.False(RoleAssignmentRule.CanRemoveAssignment([only, inactive], only));
        Assert.True(RoleAssignmentRule.CanRemoveAssignment([only, inactive with { IsActive = true }], only));
    }

    /// <summary>lease基線assert失敗時仍須清理fixture唯一擁有的暫存目錄；finally避免測試失敗時把diagnostics檔案留在主機。</summary>
    [Fact]
    public async Task Fixture_Dispose_DeletesDirectory_WhenLeaseBaselineAssertionFails()
    {
        var f = new Fixture();
        string directory = f.DirectoryPath;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "synthetic-canary.log"), "synthetic");
        f.Acquired = 1;
        try
        {
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(() => f.DisposeAsync().AsTask());
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>合成SDK及診斷唯一owner，回傳獨立Entity候選，沒有跨案例主機或client。</summary>
    internal sealed class Fixture : IAsyncDisposable, IAccessGroupSource
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "f8-access-" + Guid.NewGuid().ToString("N"));
        private readonly Mock<IOrganizationService> _service = new(MockBehavior.Strict);
        private readonly ExceptionDiagnostics _diagnostics;
        internal readonly Guid Tenant = Guid.NewGuid();
        internal readonly Guid Contact = Guid.NewGuid();
        internal readonly Guid Root = Guid.NewGuid();
        internal readonly Guid Child = Guid.NewGuid();
        internal readonly List<Entity> Assignments = [];
        internal readonly List<Entity> Roles = [];
        internal readonly List<Entity> Permissions = [];
        internal int Acquired;
        internal int Released;
        internal int Faulted;
        internal Action? BeforeQuery { get; set; }
        internal Func<QueryExpression, EntityCollection>? QueryOverride { get; set; }
        internal Func<string, Guid, Entity>? RetrieveOverride { get; set; }
        internal AccessGroupSnapshot? GroupOverride { get; set; }
        internal bool NoGroups { get; set; }
        internal Exception? ReleaseError { get; set; }
        internal Exception? FaultError { get; set; }
        /// <summary>只供合成canary驗證，正式診斷不應包含Message或輸入本文。</summary>
        internal string DiagnosticsText => File.Exists(Path.Combine(_directory, "Exception.log")) ? File.ReadAllText(Path.Combine(_directory, "Exception.log")) : "";
        /// <summary>此fixture唯一擁有的隨機暫存目錄，只供清理契約測試檢查其生命週期。</summary>
        internal string DirectoryPath => _directory;

        /// <summary>固定產品、純合成active Contact與choice租戶；所有lease都有可觀測的唯一歸還。</summary>
        internal Fixture()
        {
            _diagnostics = new ExceptionDiagnostics(_directory, outputOptions: new ExceptionOutputOptions(true, false));
            _service.Setup(s => s.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>())).Returns((string name, Guid id, ColumnSet _) =>
                RetrieveOverride?.Invoke(name, id) ?? (name == "contact" ? ContactRow(id, Tenant) : TenantRow(id)));
            _service.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                BeforeQuery?.Invoke();
                var q = Assert.IsType<QueryExpression>(query);
                if (QueryOverride is not null) return QueryOverride(q);
                var rows = q.EntityName switch { "new_roleassignment" => Assignments, "new_role" => Roles, "new_rolepermission" => Permissions, _ => throw new InvalidOperationException("Unexpected query.") };
                return new EntityCollection(rows.Select(Clone).ToList());
            });
            AddRole();
            Policy = new DataverseAccessPolicy(Acquire, _diagnostics, "GoodShepherd2", this);
        }

        /// <summary>當次伺服器解析身分，不含LINE使用者識別或部署秘密。</summary>
        internal Actor Actor => new(Contact, Tenant, "GoodShepherd2");
        /// <summary>伺服器目標候選，案例可獨立變更以測試拒絕。</summary>
        internal DataScope Target => new(Tenant, Contact, Child);
        /// <summary>被測正式policy，只存fixture依賴，不共用其他案例的來源。</summary>
        internal DataverseAccessPolicy Policy { get; }

        /// <summary>每次Acquire建立全新租約，不讓可變服務狀態跨請求遺留。</summary>
        private IAccessDataverseLease Acquire(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Interlocked.Increment(ref Acquired);
            var lease = new Mock<IAccessDataverseLease>(MockBehavior.Strict);
            lease.SetupGet(x => x.Service).Returns(_service.Object);
            lease.Setup(x => x.MarkFaulted()).Callback(() => { Interlocked.Increment(ref Faulted); if (FaultError is not null) throw FaultError; });
            lease.Setup(x => x.Dispose()).Callback(() => { Interlocked.Increment(ref Released); if (ReleaseError is not null) throw ReleaseError; });
            return lease.Object;
        }

        /// <summary>角色顯示名相同但正式ID不同必須都保留，指派有自己的正式ID。</summary>
        internal Guid AddRole()
        {
            Guid id = Guid.NewGuid();
            var role = new Entity("new_role", id) { ["new_name"] = "same-name", ["new_tenantid"] = new EntityReference("account", Tenant), ["statecode"] = new OptionSetValue(0) };
            Roles.Add(role);
            Assignments.Add(new Entity("new_roleassignment", Guid.NewGuid()) { ["new_contactid"] = new EntityReference("contact", Contact),
                ["new_roleid"] = new EntityReference("new_role", id), ["statecode"] = new OptionSetValue(0) });
            return id;
        }

        /// <summary>明確權限fixture使用平台choice值，不使用bool或權限名稱捷徑。</summary>
        internal void Grant(string feature, AccessOperation operation = AccessOperation.Read, ScopeLevel scope = ScopeLevel.Tenant, Guid? role = null)
        {
            Permissions.Add(new Entity("new_rolepermission", Guid.NewGuid()) {
                ["new_roleid"] = new EntityReference("new_role", role ?? Roles[0].Id), ["new_feature"] = feature,
                ["new_operation"] = new OptionSetValue(100000000 + (int)operation), ["new_scope"] = new OptionSetValue(100000000 + (int)scope), ["statecode"] = new OptionSetValue(0) });
        }

        /// <summary>產品群組候選可注入缺父、循環或跨租戶；正式policy負責驗證而非信任此來源。</summary>
        public AccessGroupSnapshot? Read(IOrganizationService service, Actor actor, CancellationToken ct) => NoGroups ? null : GroupOverride ??
            new AccessGroupSnapshot(Contact, Tenant, "GoodShepherd2", Root, [new(Root, Tenant, null), new(Child, Tenant, Root)]);

        /// <summary>合成active Contact與精確Account lookup，與平台正式欄位種類一致。</summary>
        internal static Entity ContactRow(Guid id, Guid tenant) => new("contact", id) { ["statecode"] = new OptionSetValue(0), ["parentcustomerid"] = new EntityReference("account", tenant) };
        /// <summary>只以OptionSetValue100000001表示平台租戶旗標，避免舊錯誤fixture重現。</summary>
        internal static Entity TenantRow(Guid id) => new("account", id) { ["statecode"] = new OptionSetValue(0), ["new_istenant"] = new OptionSetValue(100000001) };

        /// <summary>對可變lookup與choice建立新實例，不把來源候選分享給另一請求。</summary>
        internal static Entity Clone(Entity e)
        {
            var c = new Entity(e.LogicalName, e.Id);
            foreach (var kv in e.Attributes) c[kv.Key] = kv.Value switch { OptionSetValue o => new OptionSetValue(o.Value), EntityReference r => new EntityReference(r.LogicalName, r.Id), _ => kv.Value };
            return c;
        }

        /// <summary>先排空diagnostics並驗證租約基線；不論排空或斷言是否失敗，都在finally清理本案唯一暫存目錄。</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await _diagnostics.DisposeAsync();
                Assert.Equal(Acquired, Released);
            }
            finally
            {
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
        }
    }
}

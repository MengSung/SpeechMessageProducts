/*
檔案整體說明
目的與定位：以離線合成 SDK 執行 F3 實機 helper 的同一協調流程，保護寫前清冊、完整排空、正式身分與精確清理契約。
主要功能與流程：每案使用自己的 catalog、正式 schema 投影、記憶體資料庫及 repo 外 artifacts；替身只允許 account、
名額申請單與有版本條件的名額回復，未知訊息與秘密欄位直接失敗。合成配發交易只是外部 D365 的替身，不是平台 Plugin 測試。
故障與斷言：注入組織錯配、索引未就緒、步驟／Channel 漂移、落檔失敗、已提交後故障及清理故障；斷言零越界寫入、
二十個正式不同名額、清冊已關閉讀取且完整、所有並行呼叫先排空、租戶／申請單／名額回到原基線與所有 lease 已釋放。
資源與隔離：Fixture 唯一擁有單案暫存目錄與合成連線計數，不讀 User Secrets、不建立 connector 的真網路實作、不連任何組織。
替身 clone 每次回應，拒絕共用可變 Entity；所有執行緒由被測協調者 join，fixture Dispose 才精確刪除自己建立的暫存目錄。
驗證邊界：分類固定 Offline／F3，命令仍須明確 Category!=Live3P；通過不能宣稱實機鎖、交易、schema 或 LINE 驗收完成。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Live;

/// <summary>只呼叫注入合成依賴的 helper 流程；所有故障由本案 owner 隔離，不使用任何實機資源。</summary>
[Trait("Category", "Offline")]
[Trait("WP", "F3")]
public sealed class F3ChannelPoolLiveSupportContractTests
{
    /// <summary>兩個核定案例均走寫入、讀回、釋放及回復；保留不同 ID 的同名既有租戶，不以顯示名去重。</summary>
    [Theory]
    [InlineData("goodshepherd20", 20)]
    [InlineData("construction", 1)]
    public async Task Round_AllocatesDistinctFormalSlots_ThenRestoresBaseline(string organization, int count)
    {
        using var fixture = new Fixture(organization, count);
        await fixture.RunAsync();
        Assert.Equal(count, fixture.Store.Allocations.Count);
        Assert.Equal(count, fixture.Store.Allocations.Distinct().Count());
        Assert.Equal(count, fixture.Store.ReleaseCount);
        Assert.Equal(count, fixture.Store.RestoreCount);
        Assert.Equal(0, fixture.Store.ActiveCalls);
        Assert.Equal(0, fixture.Store.OpenLeases);
        Assert.Equal(fixture.Store.CreatedLeases, fixture.Store.DisposedLeases);
        Assert.Equal(fixture.Baseline, fixture.Store.Fingerprint());
        Assert.Equal(count == 20 ? 20 : 1, fixture.Store.EnteredAllocations);
        Assert.True(fixture.Store.DurableOwnershipObserved);
    }

    /// <summary>音訊科技好牧人只需登錄身分與登錄中狀態；不要求它有產品池容量、排序或名額，且絕不能參與配發。</summary>
    [Fact]
    public async Task RegisteredAudioChannel_WithoutPoolFieldsOrSlots_IsNeverAllocated()
    {
        using var fixture = new Fixture("goodshepherd20", 20);
        var registered = fixture.Store.Rows.Last(row => row.LogicalName == "new_linechannel");
        registered.Attributes.Remove("new_capacity"); registered.Attributes.Remove("new_poolorder");
        registered.Attributes.Remove("new_reservedrichmenus"); registered.Attributes.Remove("new_tenantrichmenuquota");
        fixture.Store.Rows.RemoveAll(row => row.LogicalName == "new_channelslot" && row.GetAttributeValue<EntityReference>("new_channel").Id == registered.Id);
        var baseline = fixture.Store.Fingerprint();
        await fixture.RunAsync();
        Assert.Equal(20, fixture.Store.Allocations.Count);
        Assert.All(fixture.Store.Allocations, id => Assert.NotEqual(registered.Id,
            fixture.Store.Rows.Single(row => row.Id == id).GetAttributeValue<EntityReference>("new_channel").Id));
        Assert.Equal(baseline, fixture.Store.Fingerprint());
        Assert.Equal(0, fixture.Store.OpenLeases);
    }

    /// <summary>每項前置獨立破壞；缺鎖、錯組織、Pending key、錯步驟與他產品池都必須在第一筆 SDK 寫入前拒絕。</summary>
    [Theory]
    [InlineData("lock-missing")]
    [InlineData("lock-lane")]
    [InlineData("lock-package")]
    [InlineData("lock-extra")]
    [InlineData("server-name")]
    [InlineData("server-id")]
    [InlineData("key-pending")]
    [InlineData("key-missing")]
    [InlineData("step-stage")]
    [InlineData("step-disabled")]
    [InlineData("step-config")]
    [InlineData("step-duplicate")]
    [InlineData("image-missing")]
    [InlineData("channel-product")]
    [InlineData("channel-capacity")]
    [InlineData("channel-order")]
    [InlineData("channel-key")]
    [InlineData("registered-type")]
    [InlineData("registered-status")]
    [InlineData("slot-concurrency")]
    [InlineData("request-concurrency")]
    [InlineData("slot-duplicate")]
    [InlineData("slot-shortage")]
    [InlineData("journal-failure")]
    public async Task Preconditions_FailClosedBeforeAnyWrite(string fault)
    {
        using var fixture = new Fixture("goodshepherd20", 20);
        fixture.Inject(fault);
        await Assert.ThrowsAnyAsync<Exception>(fixture.RunAsync);
        Assert.Equal(0, fixture.Store.WriteCount);
        Assert.Equal(0, fixture.Store.OpenLeases);
        Assert.Equal(fixture.Store.CreatedLeases, fixture.Store.DisposedLeases);
    }

    /// <summary>SDK 已提交但回應遺失時，仍按寫前正式候選 ID 及 marker 找回；其他十九呼叫必須先完成才清理。</summary>
    [Fact]
    public async Task DeliveredThenFault_DrainsAllTwenty_AndCleansOnlyOwnedRows()
    {
        using var fixture = new Fixture("goodshepherd20", 20);
        fixture.Store.DeliveredFault = true;
        var exception = await Assert.ThrowsAnyAsync<Exception>(fixture.RunAsync);
        Assert.DoesNotContain("synthetic-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(20, fixture.Store.EnteredAllocations);
        Assert.Equal(20, fixture.Store.ReleaseCount);
        Assert.Equal(fixture.Baseline, fixture.Store.Fingerprint());
        Assert.Equal(0, fixture.Store.OpenLeases);
        Assert.Equal(0, fixture.Store.ActiveCalls);
        Assert.Contains(fixture.Reported, failure => ReferenceEquals(failure, fixture.Store.DeliveredException));
        var diagnostic = File.ReadAllText(Path.Combine(fixture.Root, "Logs", "Exception.log"));
        Assert.Contains("System.IO.IOException", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-secret", diagnostic, StringComparison.Ordinal);
    }

    /// <summary>其中一筆刪除失敗不可提早退出其他 owner 清理；保留失敗證據並報失敗，不能以已清大部分假稱成功。</summary>
    [Fact]
    public async Task CleanupFault_ContinuesOtherOwners_AndReportsFailure()
    {
        using var fixture = new Fixture("goodshepherd20", 20);
        fixture.Store.DeleteFault = true;
        var exception = await Assert.ThrowsAnyAsync<Exception>(fixture.RunAsync);
        Assert.DoesNotContain("synthetic-secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(20, fixture.Store.ReleaseCount);
        Assert.Equal(0, fixture.Store.ActiveCalls);
        Assert.Equal(0, fixture.Store.OpenLeases);
        Assert.Equal(1, fixture.Store.Rows.Count(row => row.LogicalName == "account" && row.Contains("new_tenantcode")));
        Assert.NotEmpty(Directory.GetFiles(fixture.Artifacts));
    }

    /// <summary>清理前有人竄改 marker 時，該正式帳戶必須保留；不能因本機 ID 清冊存在就擴權刪除。</summary>
    [Fact]
    public async Task OwnershipChanged_RefusesForeignCleanup_AndStillDisposesAllLeases()
    {
        using var fixture = new Fixture("construction", 1);
        fixture.Store.ChangeOwnership = true;
        await Assert.ThrowsAnyAsync<Exception>(fixture.RunAsync);
        Assert.Equal(0, fixture.Store.ReleaseCount);
        Assert.Equal(0, fixture.Store.OpenLeases);
        Assert.Contains(fixture.Store.Rows, row => row.LogicalName == "account" && row.GetAttributeValue<string>("name") == "foreign-owner");
    }

    /// <summary>不得接受任意前綴、空 GUID 或只含前綴 marker；所有權文法是固定線 A 代碼加完整本輪 GUID。</summary>
    [Theory]
    [InlineData("c201", false)]
    [InlineData("c100", false)]
    [InlineData("c121", false)]
    [InlineData("b102", false)]
    [InlineData("c101", true)]
    public void Ownership_InvalidCodeOrEmptyId_Refused(string prefix, bool emptyId)
    {
        var marker = Guid.NewGuid().ToString("N");
        var id = emptyId ? Guid.Empty : Guid.NewGuid();
        var code = prefix + "-" + marker;
        var row = new Entity("account", id) { ["name"] = "F3-" + marker + "-" + prefix,
            ["new_tenantcode"] = code, ["new_istenant"] = new OptionSetValue(100000001), ["new_istesttenant"] = new OptionSetValue(100000001) };
        Assert.Throws<InvalidDataException>(() => F3ChannelPoolLiveSupport.RequireOwnedAccount(row, id, code, marker));
    }

    /// <summary>每案 owner 只建立自己的暫存鎖／清冊，載入 repo 正式清單但完全不建立真 connector。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>單案唯一暫存根，僅 Fixture 擁有並在全部工作排空後遞迴移除。</summary>
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "f3-helper-" + Guid.NewGuid().ToString("N"));
        /// <summary>本案 repo 外清冊目錄；落檔故障案例可替換為同根一般檔，驗證零 SDK 寫入。</summary>
        internal string Artifacts { get; private set; }
        /// <summary>本案合成 lane A／F3 鎖檔，不碰真正測試環境鎖。</summary>
        internal string LockPath { get; }
        /// <summary>單案記憶體 SDK owner，所有獨占 client 都只借用此案資料。</summary>
        internal Store Store { get; }
        /// <summary>初始化時的安全資料指紋，回復必須完整相等；不包含秘密或 RowVersion。</summary>
        internal string Baseline { get; }
        /// <summary>僅本案暫存真正例外供參照相等斷言；Dispose 清除，不讓例外跨測試保留。</summary>
        internal List<Exception> Reported { get; } = new();
        private readonly string organization;
        private readonly int count;
        private readonly OrganizationCatalog catalog;
        private readonly SchemaDefinition schema;
        private readonly RegistrationManifest manifest;
        private SchemaSnapshot snapshot;
        private readonly string[] keys;

        /// <summary>以真 SSOT 定義建立合成完整 metadata；快照只供離線 schema 比對，不冒稱 SDK 部署結果。</summary>
        internal Fixture(string organization, int count)
        {
            this.organization = organization; this.count = count;
            Artifacts = Path.Combine(Root, "artifacts"); LockPath = Path.Combine(Root, "test-env.lock");
            Directory.CreateDirectory(Artifacts);
            File.WriteAllText(LockPath, "lane=A wp=F3 start=" + DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture), new UTF8Encoding(false));
            var repository = FindRepository();
            catalog = OrganizationCatalog.Load(repository);
            schema = SchemaFiles.LoadForOrganization(repository, organization);
            manifest = RegistrationManifestLoader.LoadDocument(repository).Assemblies.Single(value => value.AssemblyName == "SpeechMessage.Dynamics.Platform.Plugins");
            keys = organization == "goodshepherd20" ? new[] { "100000001", "100000002", "100000003" } : new[] { "100000004" };
            Store = new Store(organization, keys, Artifacts);
            snapshot = CompleteSnapshot(schema, organization, Store.OrganizationId);
            var plan = SchemaPlanner.Create(schema, snapshot, DateTimeOffset.UtcNow, catalog);
            Assert.Empty(plan.Operations);
            Assert.Empty(plan.PendingKeys);
            Baseline = Store.Fingerprint();
        }

        /// <summary>僅在固定故障點改合成依賴；不同案例不共享快照、檔案、SDK Entity 或故障計數。</summary>
        internal void Inject(string fault)
        {
            if (fault == "lock-missing") File.Delete(LockPath);
            if (fault == "lock-lane") File.WriteAllText(LockPath, "lane=B wp=F3 start=2026-11-27T00:00:00+00:00");
            if (fault == "lock-package") File.WriteAllText(LockPath, "lane=A wp=FS start=2026-11-27T00:00:00+00:00");
            if (fault == "lock-extra") File.WriteAllText(LockPath, "lane=A wp=F3 lane=B start=2026-11-27T00:00:00+00:00");
            if (fault is "server-name" or "server-id") Store.IdentityFault = fault;
            if (fault is "key-pending" or "key-missing") snapshot = snapshot with { Entities = snapshot.Entities.Select(entity => entity.LogicalName != "new_slotrequest" ? entity : entity with {
                Keys = fault == "key-missing" ? Array.Empty<SchemaKeyState>() : entity.Keys.Select(key => key with { Status = "Pending" }).ToArray() }).ToArray() };
            var step = Store.Rows.First(row => row.LogicalName == "sdkmessageprocessingstep");
            if (fault == "step-stage") step["stage"] = new OptionSetValue(40);
            if (fault == "step-disabled") step["statecode"] = new OptionSetValue(1);
            if (fault == "step-config") step["configuration"] = "{}";
            if (fault == "step-duplicate") { var duplicate = Clone(step, new ColumnSet(true)); duplicate.Id = Guid.NewGuid(); Store.Rows.Add(duplicate); }
            if (fault == "image-missing") Store.Rows.RemoveAll(row => row.LogicalName == "sdkmessageprocessingstepimage");
            var channel = Store.Rows.First(row => row.LogicalName == "new_linechannel");
            if (fault == "channel-product") channel["new_product"] = "GoodButler";
            if (fault == "channel-capacity") channel["new_capacity"] = 19;
            if (fault == "channel-order") channel["new_poolorder"] = 2;
            if (fault == "channel-key") channel["new_channelid"] = "999999999";
            if (fault == "registered-type") Store.Rows.Last(row => row.LogicalName == "new_linechannel")["new_channeltype"] = new OptionSetValue(100000001);
            if (fault == "registered-status") Store.Rows.Last(row => row.LogicalName == "new_linechannel")["new_channelstatus"] = new OptionSetValue(100000001);
            if (fault is "slot-concurrency" or "request-concurrency") snapshot = snapshot with { Entities = snapshot.Entities.Select(entity => entity.LogicalName ==
                (fault == "slot-concurrency" ? "new_channelslot" : "new_slotrequest") ? entity with { OptimisticConcurrency = false } : entity).ToArray() };
            if (fault == "slot-duplicate") { var duplicate = Clone(Store.Rows.First(row => row.LogicalName == "new_channelslot"), new ColumnSet(true)); Store.Rows.Add(duplicate); }
            if (fault == "slot-shortage") Store.Rows.Remove(Store.Rows.First(row => row.LogicalName == "new_channelslot"));
            if (fault == "journal-failure") { Artifacts = Path.Combine(Root, "not-a-directory"); File.WriteAllText(Artifacts, "occupied"); }
        }

        /// <summary>執行真協調流程，只有 metadata 讀取與傳輸以本案替身取代；分類不會委派至真正 Live 入口。</summary>
        internal async Task RunAsync()
        {
            await using var diagnostics = new ExceptionDiagnostics(Path.Combine(Root, "Logs"),
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false));
            await F3ChannelPoolLiveSupport.RunCoreAsync(organization, count, catalog, schema, manifest,
                Store, keys, Artifacts, LockPath, _ => snapshot, (exception, operation) =>
                {
                    Reported.Add(exception);
                    _ = diagnostics.Report(exception, operation, notify: false);
                });
        }

        /// <summary>流程已 join 才刪本案根；若 owner 未釋放，測試斷言仍失敗，不會另開背景工作掩蓋問題。</summary>
        public void Dispose() { Reported.Clear(); Directory.Delete(Root, true); }
    }

    /// <summary>
    /// 以正式分層定義建立完整 active phase 的純合成 metadata；沿用 F0 核定的兩個不可變 ID／owner，
    /// 其餘新表依清單 owner。此處只產生測試值，不建立表、不變更正式 schema，也不借 legacy P1 快照漏欄。
    /// </summary>
    private static SchemaSnapshot CompleteSnapshot(SchemaDefinition definition, string organization, Guid organizationId)
    {
        var retained = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["new_localizedtext"] = Guid.Parse("f417a3e7-d3d9-f111-8cca-00155d007916"),
            ["new_offeringcategory"] = Guid.Parse("8407cdf5-d3d9-f111-8cca-00155d007916")
        };
        var entities = definition.Entities.Select(entity =>
        {
            var keep = organization == "goodshepherd20" && retained.ContainsKey(entity.LogicalName);
            return new SchemaEntityState(keep ? retained[entity.LogicalName] : Guid.NewGuid(), entity.LogicalName,
                entity.LogicalName + "id", entity.IsNew ? entity.PrimaryName : "name",
                keep ? "OrganizationOwned" : entity.OwnershipType ?? "OrganizationOwned", entity.Audit,
                entity.OptimisticConcurrency ?? true, false,
                entity.Fields.Where(field => field.Phase <= definition.ActivePhase).Select(field => new SchemaFieldState(Guid.NewGuid(),
                    field.LogicalName, field.Type, field.MaxLength == 0 ? null : field.MaxLength,
                    field.Required ? "ApplicationRequired" : "None", field.Secured,
                    field.Target.Length == 0 ? Array.Empty<string>() : new[] { field.Target }, field.Behavior, field.Format,
                    field.Options.Select(option => option.Value).ToArray(), false, entity.Audit) { ChoiceIsGlobal = false }).ToArray(),
                entity.Keys.Select(key => new SchemaKeyState(Guid.NewGuid(), key.LogicalName, key.Attributes.ToArray(), "Active")).ToArray());
        }).ToArray();
        var publisher = new SchemaPublisherState(Guid.NewGuid(), definition.Publisher, definition.Prefix);
        return new SchemaSnapshot { Organization = organization, OrganizationId = organizationId, Version = "9.1.0.0", LanguageCode = 1028,
            Entities = entities, Publishers = new[] { publisher },
            Solutions = new[] { new SchemaSolutionState(Guid.NewGuid(), definition.Solution, false, publisher.Id) },
            Components = entities.Select(entity => new SchemaComponentState(Guid.NewGuid(), 1, entity.Id) { RootBehavior = 0 }).ToArray() };
    }

    /// <summary>只沿編譯輸出祖先找這個 repo；不受工作目錄或外部環境設定改寫。</summary>
    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(System.AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new InvalidDataException("Synthetic repository unavailable.");
    }

    /// <summary>合成回應深複製 SDK 可變 choice／lookup，RowVersion 原樣傳回供條件回復，不讓被測程式修改資料庫參照。</summary>
    private static Entity Clone(Entity source, ColumnSet columns)
    {
        var result = new Entity(source.LogicalName, source.Id) { RowVersion = source.RowVersion };
        foreach (var pair in source.Attributes.Where(pair => columns.AllColumns || columns.Columns.Contains(pair.Key)))
            result[pair.Key] = pair.Value switch { OptionSetValue choice => new OptionSetValue(choice.Value),
                EntityReference reference => new EntityReference(reference.LogicalName, reference.Id), _ => pair.Value };
        return result;
    }

    /// <summary>有界單案記憶體 SDK；外部交易在這裡合成，不載入真 Plugin、credential、HTTP 或使用者資料。</summary>
    private sealed class Store : IOrganizationConnector
    {
        /// <summary>與本案完整 metadata 一致的合成組織 ID；好牧人使用核定 F0 身分供 planner 驗證。</summary>
        internal Guid OrganizationId { get; }
        /// <summary>單案合成資料庫，並行讀寫只在 Store 的私有鎖內發生，不跨案例保存。</summary>
        internal List<Entity> Rows { get; } = new();
        /// <summary>實際合成配發的正式名額 ID，完整保留供互異及產品來源斷言。</summary>
        internal List<Guid> Allocations { get; } = new();
        /// <summary>已完成釋放交易數，故障案例仍須清完其他擁有者。</summary>
        internal int ReleaseCount { get; private set; }
        /// <summary>有 RowVersion 條件的名額回復次數，不能以跳過更新假稱回復。</summary>
        internal int RestoreCount { get; private set; }
        /// <summary>通過獨立清冊讀回的 SDK 寫入數，前置失敗必須為零。</summary>
        internal int WriteCount { get; private set; }
        /// <summary>本案已建立的獨占租約數，與已釋放數精確配對。</summary>
        internal int CreatedLeases { get; private set; }
        /// <summary>已完成排空檢查並關閉的租約數，不允許帶工作提前釋放。</summary>
        internal int DisposedLeases { get; private set; }
        /// <summary>仍存活的本案租約數；成功及故障測試結束皆須歸零。</summary>
        internal int OpenLeases => CreatedLeases - DisposedLeases;
        /// <summary>已入場合成配發呼叫數，以原子操作增減；任何清理與 Dispose 前必須為零。</summary>
        internal int ActiveCalls;
        /// <summary>配發累計入場數，用來證明故障後二十個工作全部返回再清理。</summary>
        internal int EnteredAllocations;
        /// <summary>獨立 handle 已讀得封閉且完整 JSONL 的觀測值，不把檔案存在當持久化證明。</summary>
        internal bool DurableOwnershipObserved { get; private set; }
        /// <summary>僅本案的服務端身分故障模式，不能影響其他租戶或連線。</summary>
        internal string IdentityFault { get; set; } = "";
        /// <summary>單次已提交後回應遺失注入，原始例外仍須交共用診斷。</summary>
        internal bool DeliveredFault { get; set; }
        /// <summary>故障物件只供本案驗通知入口確實接收原參照，不可用安全包裝冒充。</summary>
        internal Exception? DeliveredException { get; private set; }
        /// <summary>單次帳戶刪除失敗注入，驗其他 owner 仍完成清理並回報整案失敗。</summary>
        internal bool DeleteFault { get; set; }
        /// <summary>交易後改寫 marker 名稱的故障開關，精確清理必須拒絕外來所有權。</summary>
        internal bool ChangeOwnership { get; set; }
        private readonly string organization;
        private readonly string artifacts;
        private readonly object sync = new();
        private int deletedFaults;

        /// <summary>建立明確兩產品池、三個精確平台步驟及兩筆同名異 ID 既有資料；不從 helper 輸出計算預期。</summary>
        internal Store(string organization, IReadOnlyList<string> keys, string artifacts)
        {
            this.organization = organization; this.artifacts = artifacts;
            OrganizationId = organization == "goodshepherd20" ? Guid.Parse("ec4d281b-bbe7-f111-8cce-00155d007916") : Guid.NewGuid();
            var product = organization == "goodshepherd20" ? "GoodShepherd2" : "GoodButler";
            for (var index = 0; index < keys.Count; index++)
            {
                var channel = new Entity("new_linechannel", Guid.NewGuid()) { ["new_channelid"] = keys[index], ["new_product"] = product,
                    ["new_capacity"] = 20, ["new_poolorder"] = index + 1, ["new_channeltype"] = new OptionSetValue(index == 2 ? 100000000 : 100000001),
                    ["new_channelstatus"] = new OptionSetValue(index == 2 ? 100000000 : 100000001),
                    ["new_reservedrichmenus"] = 0, ["new_tenantrichmenuquota"] = 45 };
                Rows.Add(channel);
                for (var number = 1; number <= 20; number++) Rows.Add(new Entity("new_channelslot", Guid.NewGuid()) { RowVersion = "1",
                    ["new_channel"] = channel.ToEntityReference(), ["new_slotnumber"] = number, ["new_slotstatus"] = new OptionSetValue(100000000),
                    ["new_richmenuquota"] = 45, ["new_usedrichmenus"] = 0, ["new_usecount"] = 0 });
            }
            Rows.Add(new Entity("account", Guid.NewGuid()) { ["name"] = "same-name" });
            Rows.Add(new Entity("account", Guid.NewGuid()) { ["name"] = "same-name" });
            Rows.Add(new Entity("new_slotallocationlock", Guid.NewGuid()) { ["new_name"] = "allocation", ["new_lockvalue"] = 0 });
            var assembly = new Entity("pluginassembly", Guid.NewGuid()) { ["name"] = "SpeechMessage.Dynamics.Platform.Plugins", ["isolationmode"] = new OptionSetValue(2), ["sourcetype"] = new OptionSetValue(0) };
            Rows.Add(assembly);
            var slotType = AddType("SlotRequestPlugin", assembly.Id); var channelType = AddType("ChannelCreatedPlugin", assembly.Id);
            AddStep(slotType, "Create", "new_slotrequest", 20, "");
            AddStep(channelType, "Create", "new_linechannel", 40, "");
            var update = AddStep(channelType, "Update", "new_linechannel", 20, "new_product");
            Rows.Add(new Entity("sdkmessageprocessingstepimage", Guid.NewGuid()) { ["sdkmessageprocessingstepid"] = update.ToEntityReference(),
                ["name"] = "ProductBefore", ["entityalias"] = "ProductBefore", ["imagetype"] = new OptionSetValue(0),
                ["attributes"] = "new_product", ["messagepropertyname"] = "Target" });
        }

        /// <summary>明列正式型別與組件關聯；禁止只有相同顯示名就匹配步驟。</summary>
        private Guid AddType(string name, Guid assembly)
        {
            var row = new Entity("plugintype", Guid.NewGuid()) { ["typename"] = "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool." + name,
                ["pluginassemblyid"] = new EntityReference("pluginassembly", assembly) };
            Rows.Add(row); return row.Id;
        }

        /// <summary>建立真實 message/filter/handler 外鍵鏈與固定映射；rank、同步與 stage 均可被各案獨立破壞。</summary>
        private Entity AddStep(Guid handler, string messageName, string entity, int stage, string filtering)
        {
            var message = Rows.SingleOrDefault(row => row.LogicalName == "sdkmessage" && row.GetAttributeValue<string>("name") == messageName);
            if (message is null) { message = new Entity("sdkmessage", Guid.NewGuid()) { ["name"] = messageName }; Rows.Add(message); }
            var filter = new Entity("sdkmessagefilter", Guid.NewGuid()) { ["sdkmessageid"] = message.ToEntityReference(), ["primaryobjecttypecode"] = entity, ["secondaryobjecttypecode"] = "none" };
            Rows.Add(filter);
            var step = new Entity("sdkmessageprocessingstep", Guid.NewGuid()) { ["sdkmessageid"] = message.ToEntityReference(), ["sdkmessagefilterid"] = filter.ToEntityReference(),
                ["eventhandler"] = new EntityReference("plugintype", handler), ["stage"] = new OptionSetValue(stage), ["mode"] = new OptionSetValue(0), ["rank"] = 1,
                ["statecode"] = new OptionSetValue(0), ["supporteddeployment"] = new OptionSetValue(0), ["filteringattributes"] = filtering,
                ["configuration"] = "{\"products\":{\"goodshepherd20\":\"GoodShepherd2\",\"construction\":\"GoodButler\",\"association\":\"GoodGuild\"}}" };
            Rows.Add(step); return step;
        }

        /// <summary>每次連線都是獨占 wrapper；Dispose 時必須沒有仍在入場的申請，關閉後所有操作拒絕。</summary>
        public OrganizationLease Connect(string name)
        {
            Assert.Equal(organization, name);
            var client = new Client(this);
            CreatedLeases++;
            return new OrganizationLease(client, () => { Assert.Equal(0, ActiveCalls); client.Closed = true; DisposedLeases++; });
        }

        /// <summary>只比較既有資料與名額可變欄位；忽略系統 RowVersion，才能驗證精確回復內容而非版本重設。</summary>
        internal string Fingerprint()
        {
            lock (sync) return string.Join("|", Rows.Where(row => row.LogicalName is "account" or "new_slotrequest" or "new_channelslot" or "new_linechannel")
                .OrderBy(row => row.Id).Select(row => row.LogicalName + row.Id.ToString("N") + string.Join(";", row.Attributes.Where(pair => pair.Value is not null)
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + (pair.Value switch {
                        OptionSetValue option => option.Value.ToString(CultureInfo.InvariantCulture), EntityReference reference => reference.Id.ToString("N"),
                        DateTime date => date.ToString("O", CultureInfo.InvariantCulture), _ => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) })))));
        }

        /// <summary>在每次 SDK 寫入前從獨立讀取 handle 讀已落檔清冊；必須找到此次正式候選 ID，不能只憑檔案存在放行。</summary>
        private void BeforeWrite(Guid id)
        {
            var evidence = Directory.GetFiles(artifacts, "*.jsonl");
            Assert.Single(evidence);
            var text = File.ReadAllText(evidence[0]);
            Assert.Contains(id.ToString("D"), text, StringComparison.Ordinal);
            foreach (var line in text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)) { using var parsed = JsonDocument.Parse(line); }
            DurableOwnershipObserved = true;
            WriteCount++;
        }

        /// <summary>每個 client 只保存此案 Store；沒有網路或 unmanaged 資源，租約關閉會禁止後續讀寫。</summary>
        private sealed class Client(Store owner) : IOrganizationService
        {
            /// <summary>租約釋放後標記，所有 SDK 操作都必須拒絕繼續借用。</summary>
            internal bool Closed { get; set; }

            /// <summary>所有輸入只能是三欄申請單或完整本輪帳戶；合成交易在單一鎖內提交，回應遺失仍保留資料供精確回收。</summary>
            public Guid Create(Entity entity)
            {
                ObjectDisposedException.ThrowIf(Closed, this);
                var allocation = entity.LogicalName == "new_slotrequest" && entity.GetAttributeValue<OptionSetValue>("new_operation").Value == 100000000;
                if (allocation) { Interlocked.Increment(ref owner.ActiveCalls); Interlocked.Increment(ref owner.EnteredAllocations); }
                try
                {
                    lock (owner.sync)
                    {
                        owner.BeforeWrite(entity.Id);
                        Assert.DoesNotContain(owner.Rows, row => row.Id == entity.Id);
                        var row = Clone(entity, new ColumnSet(true)); row.RowVersion = "1";
                        Assert.True(row.LogicalName is "account" or "new_slotrequest");
                        if (row.LogicalName == "new_slotrequest")
                        {
                            Assert.Equal(3, row.Attributes.Count);
                            Assert.True(row.Contains("new_correlationid") && row.Contains("new_operation") && row.Contains("new_tenant"));
                            var account = owner.Rows.Single(value => value.Id == row.GetAttributeValue<EntityReference>("new_tenant").Id);
                            var operation = row.GetAttributeValue<OptionSetValue>("new_operation").Value;
                            if (operation == 100000000)
                            {
                                var slot = owner.Rows.Where(value => value.LogicalName == "new_channelslot" && value.GetAttributeValue<OptionSetValue>("new_slotstatus").Value == 100000000)
                                    .OrderBy(value => owner.Rows.Single(channel => channel.Id == value.GetAttributeValue<EntityReference>("new_channel").Id).GetAttributeValue<int>("new_poolorder"))
                                    .ThenBy(value => value.GetAttributeValue<int>("new_slotnumber")).First();
                                slot["new_tenant"] = account.ToEntityReference(); slot["new_slotstatus"] = new OptionSetValue(100000001);
                                slot["new_usecount"] = slot.GetAttributeValue<int>("new_usecount") + 1; slot["new_allocatedon"] = DateTime.UtcNow;
                                account["new_channelslot"] = slot.ToEntityReference(); row["new_result"] = new OptionSetValue(100000000);
                                row["new_slot"] = slot.ToEntityReference(); row["new_channel"] = slot["new_channel"]; owner.Allocations.Add(slot.Id);
                                var channel = owner.Rows.Single(value => value.Id == slot.GetAttributeValue<EntityReference>("new_channel").Id);
                                if (!owner.Rows.Any(value => value.LogicalName == "new_channelslot" && value.GetAttributeValue<EntityReference>("new_channel").Id == channel.Id && value.GetAttributeValue<OptionSetValue>("new_slotstatus").Value == 100000000))
                                    channel["new_channelstatus"] = new OptionSetValue(100000002);
                                if (owner.ChangeOwnership) account["name"] = "foreign-owner";
                            }
                            else
                            {
                                Assert.Equal(100000001, operation); Assert.Equal(0, owner.ActiveCalls);
                                var slot = owner.Rows.Single(value => value.Id == account.GetAttributeValue<EntityReference>("new_channelslot").Id);
                                row["new_slot"] = slot.ToEntityReference(); row["new_channel"] = slot["new_channel"];
                                slot["new_tenant"] = null; slot["new_slotstatus"] = new OptionSetValue(100000000); slot["new_releasedon"] = DateTime.UtcNow;
                                account["new_channelslot"] = null; row["new_result"] = new OptionSetValue(100000004); owner.ReleaseCount++;
                                owner.Rows.Single(value => value.Id == slot.GetAttributeValue<EntityReference>("new_channel").Id)["new_channelstatus"] = new OptionSetValue(100000001);
                            }
                            row["new_name"] = row["new_correlationid"];
                        }
                        owner.Rows.Add(row);
                        if (allocation && owner.DeliveredFault) { owner.DeliveredFault = false; owner.DeliveredException = new IOException("synthetic-secret-delivered"); throw owner.DeliveredException; }
                        return row.Id;
                    }
                }
                finally { if (allocation) Interlocked.Decrement(ref owner.ActiveCalls); }
            }

            /// <summary>只支援正式身分與有 RowVersion 的條件名額回復；任何部署／註冊／無條件 Update 直接失敗。</summary>
            public OrganizationResponse Execute(OrganizationRequest request)
            {
                ObjectDisposedException.ThrowIf(Closed, this);
                if (request is WhoAmIRequest) return new WhoAmIResponse { Results = { ["OrganizationId"] = owner.IdentityFault == "server-id" ? Guid.NewGuid() : owner.OrganizationId,
                    ["UserId"] = Guid.Parse("11111111-1111-1111-1111-111111111111"), ["BusinessUnitId"] = Guid.NewGuid() } };
                if (request is RetrieveCurrentOrganizationRequest) return new RetrieveCurrentOrganizationResponse { Results = { ["Detail"] = new OrganizationDetail {
                    OrganizationId = owner.OrganizationId, UniqueName = owner.IdentityFault == "server-name" ? "goodshepherdback" : owner.organization, OrganizationVersion = "9.1.0.0" } } };
                if (request is UpdateRequest update)
                {
                    lock (owner.sync)
                    {
                        owner.BeforeWrite(update.Target.Id); Assert.Equal(0, owner.ActiveCalls);
                        Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior); Assert.Equal("new_channelslot", update.Target.LogicalName);
                        var slot = owner.Rows.Single(row => row.Id == update.Target.Id); Assert.Equal(slot.RowVersion, update.Target.RowVersion);
                        foreach (var pair in update.Target.Attributes) slot[pair.Key] = pair.Value;
                        owner.RestoreCount++; return new UpdateResponse();
                    }
                }
                throw new NotSupportedException("Synthetic message outside contract.");
            }

            /// <summary>讀回單一正式 ID 的 detached 投影；不存在不以空列掩蓋傳輸失敗。</summary>
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
            {
                ObjectDisposedException.ThrowIf(Closed, this);
                lock (owner.sync) return Clone(owner.Rows.Single(row => row.LogicalName == entityName && row.Id == id), columnSet);
            }

            /// <summary>只實作 helper 使用的精確 Equal／NotNull 查詢；敏感欄位、未知運算及 AllColumns 一律拒絕。</summary>
            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                ObjectDisposedException.ThrowIf(Closed, this);
                var expression = Assert.IsType<QueryExpression>(query); Assert.False(expression.ColumnSet.AllColumns);
                Assert.DoesNotContain(expression.ColumnSet.Columns, column => column.Contains("token", StringComparison.OrdinalIgnoreCase) || column.Contains("secret", StringComparison.OrdinalIgnoreCase) && column != "sdkmessageprocessingstepsecureconfigid");
                lock (owner.sync)
                {
                    var source = owner.Rows.Where(row => row.LogicalName == expression.EntityName && Match(row, expression.Criteria)).ToList();
                    return new EntityCollection(source.Select(row => Clone(row, expression.ColumnSet)).ToList());
                }
            }

            /// <summary>合成篩選器以主鍵及 lookup 正式 ID 比較，不按顯示字串或列表位置決定資料所有權。</summary>
            private static bool Match(Entity row, FilterExpression filter)
            {
                bool Condition(ConditionExpression condition)
                {
                    object? value = condition.AttributeName == row.LogicalName + "id" ? row.Id : row.Attributes.TryGetValue(condition.AttributeName, out var raw) ? raw : null;
                    if (value is EntityReference reference) value = reference.Id;
                    if (value is OptionSetValue option) value = option.Value;
                    return condition.Operator switch { ConditionOperator.Equal => Equals(value, condition.Values[0]), ConditionOperator.NotNull => value is not null,
                        ConditionOperator.In => condition.Values.Contains(value), _ => throw new NotSupportedException("Synthetic filter outside contract.") };
                }
                var results = filter.Conditions.Select(Condition).Concat(filter.Filters.Select(nested => Match(row, nested))).ToArray();
                return filter.FilterOperator == LogicalOperator.And ? results.All(value => value) : results.Any(value => value);
            }

            /// <summary>只刪本輪正式帳戶／申請單；清理開始時全部申請已排空，故障不會被轉成假成功。</summary>
            public void Delete(string entityName, Guid id)
            {
                ObjectDisposedException.ThrowIf(Closed, this);
                lock (owner.sync)
                {
                    owner.BeforeWrite(id); Assert.Equal(0, owner.ActiveCalls); Assert.True(entityName is "account" or "new_slotrequest");
                    if (entityName == "account" && owner.DeleteFault && owner.deletedFaults++ == 0) throw new IOException("synthetic-secret-delete");
                    var row = owner.Rows.Single(value => value.LogicalName == entityName && value.Id == id); owner.Rows.Remove(row);
                }
            }

            /// <summary>回復必須使用官方條件訊息；無條件 Update 視為安全退化。</summary>
            public void Update(Entity entity) => throw new NotSupportedException();
            /// <summary>本測試不允許新增未列關聯，避免替身接受越界寫入。</summary>
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
            /// <summary>本測試不允許解除未列關聯，清理只能按本輪正式 ID。</summary>
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        }
    }
}

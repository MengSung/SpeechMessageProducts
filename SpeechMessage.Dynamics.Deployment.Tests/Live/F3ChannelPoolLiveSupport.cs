/*
檔案整體說明
目的與定位：集中 F3 Live3P 的固定組織、正式資料所有權與先寫後送清冊契約，以及不會連線的精確離線守門。
主要功能與流程：精確比對 catalog／服務端組織、已發布 schema／Active keys／兩表樂觀並行與平台三步驟後，核對產品池。
T01／T02／GoodButler 是營運產品池；音訊科技好牧人僅登錄，不要求先有池欄位或名額，更不能參與配發。
輸入與信任邊界：正式入口只讀 Deployment 認證與 Handoff 公開 Channel ID；不讀 Channel secret／token，不建立 LINE client。
離線案例只用區域合成 Entity 或注入合成 connector，不讀 User Secrets，不可用其成功宣稱 Live 通過。
資料與生命週期：每輪 GUID／marker 與 account／申請／釋放候選 ID 必須在 repo 外固定 artifacts 先落清冊、Flush(true) 並關檔。
清冊只保留安全欄位與正式 ID 供故障復原；每檔上限 4 MiB，人工核銷前不自動刪證據，不把 marker／ID 放入斷言輸出。
本輪 owner 持有一條控制連線及最多二十條獨占 worker 連線、五分鐘內部期限；單次真 SDK 最多三十秒，已送工作全排空後才清理。
清理按精確 ID／marker／旗標重驗，釋放後以 RowVersion 條件回復本輪名額時間與計數，完整核對 baseline，再逆序 Dispose 租約。
失敗與驗證：每筆真正 Exception 交共用 file-only ExceptionDiagnostics，再拋不含遠端本文的安全 fault；診斷 owner 最後排空釋放。
測試發現無 I/O、共享資料快取或背景工作；純邊界與同流程故障注入另驗前置零寫入、並行排空、精確清理及釋放，不代為部署或註冊。
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
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Deployment.Schema;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Live;

/// <summary>F3 專用區域安全邊界，實機依賴只允許從 RunAsync 的明確入口取得。</summary>
internal static class F3ChannelPoolLiveSupport
{
    /// <summary>只接受兩個固定名稱及各自核定數量；不借 catalog 的 legacy 相容範圍擴權。</summary>
    internal static string Product(string organization, int count) => (organization, count) switch
    {
        ("goodshepherd20", 20) => "GoodShepherd2",
        ("construction", 1) => "GoodButler",
        _ => throw Invalid("target")
    };

    /// <summary>只有正式 ID、完整本輪名稱／代碼及兩個明確「是」旗標都吻合才可清理。</summary>
    internal static void RequireOwnedAccount(Entity row, Guid id, string code, string marker)
    {
        if (id == Guid.Empty || !Guid.TryParseExact(marker, "N", out var parsed) || parsed == Guid.Empty || marker != parsed.ToString("N") ||
            code.Length != 37 || code[4] != '-' || code[5..] != marker ||
            !(code[..4] == "b101" || int.TryParse(code.AsSpan(1, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
                code[0] == 'c' && number is >= 101 and <= 120)) throw Invalid("ownership");
        Identity(row, "account", id);
        if (Text(row, "name") != "F3-" + marker + "-" + code[..4] || Text(row, "new_tenantcode") != code ||
            Choice(row, "new_istenant") != 100000001 || Choice(row, "new_istesttenant") != 100000001) throw Invalid("ownership");
    }

    /// <summary>
    /// 僅兩個明確 Live3P 入口呼叫；從固定來源建立獨占連線與公開 Channel ID，委派同流程核心。
    /// 本入口擁有 configuration 與共用 file-only 診斷，核心完成 drain／清理／Dispose 之後才釋放；不發 LINE。
    /// 前置與清理原始故障先進診斷，對 xUnit 僅顯示安全型別與固定階段，不能用成功包裝掩蓋失敗。
    /// </summary>
    internal static async Task RunAsync(string organization, int count)
    {
        var artifacts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpeechMessage", "artifacts", "f3-live");
        await using var diagnostics = new ExceptionDiagnostics(Path.Combine(artifacts, "Logs"),
            outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false));
        // 測試工具沿用 FS 的明確 file-only 共用診斷，不建立 LINE client；真正例外先報告，再輸出安全 fault。
        try
        {
            _ = Product(organization, count);
            var repository = Repository();
            var lockPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpeechMessage", "locks", "test-env.lock");
            CheckLock(lockPath);
            var catalog = OrganizationCatalog.Load(repository);
            var target = catalog.GetTest(organization);
            RequireTarget(target, organization);
            var schema = SchemaFiles.LoadForOrganization(repository, organization);
            var manifest = RegistrationManifestLoader.LoadDocument(repository).Assemblies.Single(row => row.AssemblyName == PlatformAssembly);
            using var configuration = (ConfigurationRoot)DeploymentConfiguration.Create(typeof(OrganizationCatalog).Assembly);
            // 僅讀公開 Channel ID 以驗資料庫正式關聯，不讀 access token、secret 或解密金鑰，亦沒有 LINE client。
            using var handoff = (ConfigurationRoot)new ConfigurationBuilder().AddUserSecrets("SpeechMessage.ThreeProducts.Handoff", reloadOnChange: false).Build();
            var aliases = organization == "goodshepherd20" ? new[] { "T01", "T02", "GoodShepherd2" } : new[] { "GoodButler" };
            var keys = aliases.Select(alias => handoff["ThreeProducts:Line:" + alias + ":ChannelId"] ?? "").ToArray();
            var connector = new UserSecretsOrganizationConnector(configuration, static (url, user, password) =>
                new OnPremiseClient(url, user, password) { Timeout = TimeSpan.FromSeconds(30) }, catalog);
            await RunCoreAsync(organization, count, catalog, schema, manifest, connector, keys, artifacts, lockPath,
                service => SchemaMetadata.Read(service, target, CancellationToken.None, retrieveAsIfPublished: false),
                (exception, operation) => { _ = diagnostics.Report(exception, operation, notify: false); }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            throw SafeFailure("entry", exception, (failure, operation) => { _ = diagnostics.Report(failure, operation, notify: false); });
        }
    }

    /// <summary>
    /// 離線可注入合成 SDK、metadata 與共用診斷的同一協調流程；正式入口固定來源，不對產品公開測試接縫。
    /// 五分鐘期限僅阻止尚未送出的工作，已入場同步 SDK 必須實際返回後才清理，不能以 WaitAsync 提早釋放仍在用的 client。
    /// 寫前完整候選清冊由本輪 owner 持久化；所有工作 join 後按所有權回復基線，任一失敗仍嘗試其他 owner 並逆序釋放。
    /// </summary>
    internal static async Task RunCoreAsync(string organization, int count, OrganizationCatalog catalog,
        SchemaDefinition schema, RegistrationManifest manifest, IOrganizationConnector connector,
        IReadOnlyList<string> channelKeys, string artifacts, string lockPath,
        Func<IOrganizationService, SchemaSnapshot> readSchema, Action<Exception, string> report)
    {
        var product = Product(organization, count);
        var target = catalog.GetTest(organization);
        RequireTarget(target, organization);
        CheckLock(lockPath);
        if (channelKeys.Count != (count == 20 ? 3 : 1) || channelKeys.Any(key => key.Length is < 1 or > 20 || !key.All(char.IsAsciiDigit)) ||
            channelKeys.Distinct(StringComparer.Ordinal).Count() != channelKeys.Count) throw Invalid("channel-input");
        RequireExternal(artifacts);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var leases = new List<OrganizationLease>(count + 1);
        var errors = new List<Exception>();
        var owners = Array.Empty<Owned>();
        Snapshot? baseline = null;
        string? journal = null;
        IOrganizationService? service = null;
        var phase = "identity";
        try
        {
            var control = connector.Connect(organization); leases.Add(control); service = control.Service;
            var organizationId = RequireServer(service, organization);
            var metadata = readSchema(service);
            if (metadata.Organization != organization || metadata.OrganizationId != organizationId) throw Invalid("metadata-scope");
            phase = "schema";
            var plan = SchemaPlanner.Create(schema, metadata, DateTimeOffset.UtcNow, catalog);
            if (plan.Operations.Count != 0 || plan.PendingKeys.Count != 0) throw Invalid("schema");
            if (metadata.Entities.Single(row => row.LogicalName == "new_channelslot").OptimisticConcurrency != true ||
                metadata.Entities.Single(row => row.LogicalName == "new_slotrequest").OptimisticConcurrency != true) throw Invalid("concurrency");
            phase = "plugins";
            RequirePlugins(service, manifest, organization);
            phase = "pool";
            baseline = ReadSnapshot(service, deadline.Token);
            RequirePool(baseline, product, channelKeys, count);
            var workers = new IOrganizationService[count];
            for (var index = 0; index < count; index++)
            {
                deadline.Token.ThrowIfCancellationRequested();
                var worker = connector.Connect(organization); leases.Add(worker);
                if (RequireServer(worker.Service, organization) != organizationId) throw Invalid("worker-scope");
                workers[index] = worker.Service;
            }
            var marker = Guid.NewGuid().ToString("N");
            owners = Enumerable.Range(101, count).Select(number => new Owned((count == 20 ? "c" : "b") + number.ToString(CultureInfo.InvariantCulture), marker)).ToArray();
            CheckLock(lockPath);
            phase = "journal";
            Directory.CreateDirectory(artifacts);
            SchemaDeploymentCli.EnsureNoLinks(artifacts);
            journal = Path.Combine(artifacts, "f3-" + marker + ".jsonl");
            Journal(journal, new { kind = "ownership-before-send", organization, organizationId, marker,
                owners = owners.Select(owner => new { owner.Id, owner.Code, owner.RequestId, owner.Correlation, owner.ReleaseId, owner.ReleaseCorrelation }),
                baseline = baseline.Slots.Select(slot => new { slot.Id, slot.RowVersion, values = slot.Attributes.ToDictionary(pair => pair.Key, pair => JournalValue(pair.Value)) }) }, true);
            phase = "accounts";
            foreach (var owner in owners)
            {
                deadline.Token.ThrowIfCancellationRequested(); CheckLock(lockPath);
                var account = new Entity("account", owner.Id) { ["name"] = "F3-" + marker + "-" + owner.Code[..4], ["new_tenantcode"] = owner.Code,
                    ["new_istenant"] = new OptionSetValue(100000001), ["new_istesttenant"] = new OptionSetValue(100000001) };
                if (service.Create(account) != owner.Id) throw Invalid("create-identity");
            }
            // 每個工作擁有不同 client。閘門只控制共同開始，不包住 SDK 呼叫；WhenAll 即使部分失敗仍等待全部返回。
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            phase = "allocations";
            var tasks = new List<Task<Guid>>(count);
            Exception? schedulingFailure = null;
            try
            {
                for (var index = 0; index < owners.Length; index++)
                {
                    var owner = owners[index]; var worker = workers[index];
                    tasks.Add(Task.Run(async () =>
                    {
                        await start.Task.ConfigureAwait(false);
                        deadline.Token.ThrowIfCancellationRequested(); CheckLock(lockPath);
                        if (worker.Create(Request(owner, false)) != owner.RequestId) throw Invalid("request-identity");
                        var result = Find(worker, "new_slotrequest", owner.RequestId, RequestColumns) ?? throw Invalid("request-missing");
                        RequireRequest(result, owner, false);
                        if (Choice(result, "new_result") != 100000000) throw Invalid("allocation-result");
                        return RequireAllocation(worker, result, owner, baseline, product, channelKeys, count);
                    }));
                }
            }
            catch (Exception exception) { schedulingFailure = exception; }
            // 即使排程中途失敗，也必須開閘並 join 已建立的全部工作，才能清理或 Dispose client。
            start.TrySetResult();
            var all = Task.WhenAll(tasks);
            Guid[] results;
            try { results = await all.ConfigureAwait(false); }
            catch (Exception exception)
            {
                var failure = (Exception?)all.Exception ?? exception;
                throw schedulingFailure is null ? failure : new AggregateException(schedulingFailure, failure);
            }
            if (schedulingFailure is not null) throw new AggregateException(schedulingFailure);
            if (results.Distinct().Count() != count) throw Invalid("duplicate-allocation");
            Journal(journal, new { kind = "allocated-confirmed", count });
        }
        catch (Exception exception) { errors.Add(SafeFailure(phase, exception, report)); }
        finally
        {
            // 沒有 fire-and-forget：只有完整 WhenAll 返回（成功或故障）才能進入這裡；所有連線仍在 owner 手中。
            if (service is not null && baseline is not null && journal is not null)
            {
                foreach (var owner in owners)
                {
                    try { Cleanup(service, owner, baseline, journal, lockPath); }
                    catch (Exception exception) { errors.Add(SafeFailure("cleanup", exception, report)); }
                }
                try
                {
                    var after = ReadSnapshot(service, CancellationToken.None);
                    RequireRestored(baseline, after);
                    Journal(journal, new { kind = "baseline-restored", accounts = after.Accounts.Count, requests = after.Requests.Count, slots = after.Slots.Count });
                }
                catch (Exception exception) { errors.Add(SafeFailure("baseline", exception, report)); }
            }
            for (var index = leases.Count - 1; index >= 0; index--)
            {
                try { leases[index].Dispose(); }
                catch (Exception exception) { errors.Add(SafeFailure("dispose", exception, report)); }
            }
        }
        if (errors.Count != 0) throw new AggregateException("F3 live round failed; inspect local ownership evidence.", errors);
    }

    /// <summary>只讀平台組件、正式 handler／message／filter 外鍵與三個精確步驟；缺任何映像或重複步驟都拒絕，不代為註冊。</summary>
    private static void RequirePlugins(IOrganizationService service, RegistrationManifest manifest, string organization)
    {
        if (manifest.AssemblyName != PlatformAssembly || manifest.Steps.Count != 3) throw Invalid("manifest");
        var assembly = Read(service, "pluginassembly", AssemblyColumns,
            filter: criteria => criteria.AddCondition("name", ConditionOperator.Equal, PlatformAssembly)).SingleOrDefault() ?? throw Invalid("assembly");
        if (Text(assembly, "name") != PlatformAssembly || Choice(assembly, "isolationmode") != 2 || Choice(assembly, "sourcetype") != 0) throw Invalid("assembly");
        var types = Read(service, "plugintype", TypeColumns,
            filter: criteria => criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, assembly.Id));
        if (types.Count != 2 || types.Any(type => Reference(type, "pluginassemblyid", "pluginassembly") != assembly.Id) ||
            types.Select(type => Text(type, "typename")).Distinct(StringComparer.Ordinal).Count() != 2) throw Invalid("plugin-types");
        var steps = new List<Entity>();
        foreach (var type in types)
            steps.AddRange(Read(service, "sdkmessageprocessingstep", StepColumns,
                filter: criteria => criteria.AddCondition("eventhandler", ConditionOperator.Equal, type.Id)));
        if (steps.Count != 3 || steps.Select(step => step.Id).Distinct().Count() != 3) throw Invalid("plugin-steps");
        var messages = Read(service, "sdkmessage", MessageColumns, filter: criteria => criteria.AddCondition("name", ConditionOperator.In, "Create", "Update"));
        var filters = Read(service, "sdkmessagefilter", FilterColumns,
            filter: criteria => criteria.AddCondition("primaryobjecttypecode", ConditionOperator.In, "new_slotrequest", "new_linechannel"));
        var matched = new HashSet<Guid>();
        foreach (var expected in manifest.Steps)
        {
            if (!expected.EnabledByOrganization.TryGetValue(organization, out var enabled) || !enabled || expected.UnsecureConfig != ProductMap ||
                expected.SecureConfig.Length != 0 || expected.Mode != "Synchronous" || expected.Rank != 1) throw Invalid("manifest-step");
            var expectedStage = expected.Stage switch { "PreOperation" => 20, "PostOperation" => 40, _ => throw Invalid("manifest-stage") };
            var type = types.SingleOrDefault(row => Text(row, "typename") == expected.Type) ?? throw Invalid("plugin-type");
            var message = messages.SingleOrDefault(row => Text(row, "name") == expected.Message) ?? throw Invalid("message");
            var filter = filters.SingleOrDefault(row => Reference(row, "sdkmessageid", "sdkmessage") == message.Id && Text(row, "primaryobjecttypecode") == expected.Entity &&
                (!row.Contains("secondaryobjecttypecode") || row["secondaryobjecttypecode"] is null or "" or "none")) ?? throw Invalid("filter");
            var step = steps.SingleOrDefault(row => Reference(row, "eventhandler", "plugintype") == type.Id && Reference(row, "sdkmessageid", "sdkmessage") == message.Id &&
                Reference(row, "sdkmessagefilterid", "sdkmessagefilter") == filter.Id && Choice(row, "stage") == expectedStage) ?? throw Invalid("step");
            if (!matched.Add(step.Id) || Choice(step, "mode") != 0 || Number(step, "rank") != 1 || Choice(step, "statecode") != 0 ||
                Choice(step, "supporteddeployment") != 0 || Text(step, "configuration") != ProductMap ||
                Reference(step, "sdkmessageprocessingstepsecureconfigid", "sdkmessageprocessingstepsecureconfig", true) is not null ||
                Csv(step, "filteringattributes") != string.Join(',', expected.FilteringAttributes.Order(StringComparer.Ordinal))) throw Invalid("step-settings");
            var images = Read(service, "sdkmessageprocessingstepimage", ImageColumns,
                filter: criteria => criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.Equal, step.Id));
            if (images.Count != expected.Images.Count) throw Invalid("images");
            foreach (var expectedImage in expected.Images)
            {
                var image = images.SingleOrDefault(row => Text(row, "name") == expectedImage.Name) ?? throw Invalid("image");
                if (Reference(image, "sdkmessageprocessingstepid", "sdkmessageprocessingstep") != step.Id || Text(image, "entityalias") != expectedImage.Name ||
                    Text(image, "messagepropertyname") != "Target" || Choice(image, "imagetype") != (expectedImage.Type == "PreImage" ? 0 : 1) ||
                    Csv(image, "attributes") != string.Join(',', expectedImage.Attributes.Order(StringComparer.Ordinal))) throw Invalid("image-settings");
            }
        }
    }

    /// <summary>只將真正 null／缺值視為空清單；重複欄位仍保留並在精確比較失敗，不靜默去重。</summary>
    private static string Csv(Entity row, string field) => !row.Contains(field) || row[field] is null ? "" :
        string.Join(',', Text(row, field).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()).Order(StringComparer.Ordinal));

    /// <summary>
    /// 核對固定公開 Channel key、正式 ID、產品及型別／狀態；只有 T01、T02、GoodButler 驗產品池容量與完整名額。
    /// 音訊科技好牧人只登錄、不配發，無須先建池欄位／名額；若已有名額，必須仍可用且無租戶，不能掩蓋誤配發。
    /// 不讀舊空產品密文，不補池、不建立或更動 schema。
    /// </summary>
    private static void RequirePool(Snapshot baseline, string product, IReadOnlyList<string> keys, int count)
    {
        var channels = baseline.Channels.Where(channel => !string.IsNullOrWhiteSpace(Text(channel, "new_product"))).ToArray();
        if (channels.Length != keys.Count || channels.Any(channel => Text(channel, "new_product") != product)) throw Invalid("pool-product");
        var channelIds = new HashSet<Guid>(); var available = 0; var tenants = new HashSet<Guid>();
        for (var index = 0; index < keys.Count; index++)
        {
            var channel = channels.SingleOrDefault(row => Text(row, "new_channelid") == keys[index]) ?? throw Invalid("channel-key");
            var registered = count == 20 && index == 2;
            if (!channelIds.Add(channel.Id) || Choice(channel, "new_channeltype") != (registered ? 100000000 : 100000001) ||
                Choice(channel, "new_channelstatus") != (registered ? 100000000 : 100000001)) throw Invalid("channel-settings");
            var slots = baseline.Slots.Where(row => Reference(row, "new_channel", "new_linechannel") == channel.Id).ToArray();
            if (registered)
            {
                if (slots.Any(slot => Reference(slot, "new_tenant", "account", true) is not null ||
                    Choice(slot, "new_slotstatus") != 100000000 || Number(slot, "new_usedrichmenus") != 0)) throw Invalid("registered-allocation");
                continue;
            }
            if (Number(channel, "new_capacity") != 20 || Number(channel, "new_poolorder") != index + 1) throw Invalid("channel-settings");
            var quota = Number(channel, "new_tenantrichmenuquota"); var reserved = Number(channel, "new_reservedrichmenus");
            if (quota is < 1 or > 45 || reserved < 0 || reserved + 21L * quota > 1000) throw Invalid("menu-budget");
            if (slots.Length != 20 || slots.Select(row => Number(row, "new_slotnumber")).Distinct().Count() != 20) throw Invalid("slot-count");
            foreach (var slot in slots)
            {
                var status = Choice(slot, "new_slotstatus"); var tenant = Reference(slot, "new_tenant", "account", true);
                if (Number(slot, "new_slotnumber") is < 1 or > 20 || Number(slot, "new_richmenuquota") != quota ||
                    Number(slot, "new_usedrichmenus") < 0 || Number(slot, "new_usedrichmenus") > quota || Number(slot, "new_usecount") < 0 ||
                    status is < 100000000 or > 100000004 || status == 100000000 && (tenant is not null || Number(slot, "new_usedrichmenus") != 0) ||
                    status == 100000001 && tenant is null || tenant.HasValue && !tenants.Add(tenant.Value) || string.IsNullOrEmpty(slot.RowVersion)) throw Invalid("slot-state");
                if (status == 100000000) available++;
            }
        }
        if (baseline.Slots.Any(slot => !channelIds.Contains(Reference(slot, "new_channel", "new_linechannel")!.Value)) || available < count) throw Invalid("pool-capacity");
    }

    /// <summary>配發的正式 request/account/slot/channel 四者必須互相指向，名額在本輪可用基線內且只來自核定營運池。</summary>
    private static Guid RequireAllocation(IOrganizationService service, Entity result, Owned owner, Snapshot baseline, string product, IReadOnlyList<string> keys, int count)
    {
        var slotId = Reference(result, "new_slot", "new_channelslot")!.Value; var channelId = Reference(result, "new_channel", "new_linechannel")!.Value;
        var before = baseline.Slots.SingleOrDefault(slot => slot.Id == slotId) ?? throw Invalid("allocation-baseline");
        if (Choice(before, "new_slotstatus") != 100000000 || Reference(before, "new_tenant", "account", true) is not null ||
            Reference(before, "new_channel", "new_linechannel") != channelId) throw Invalid("allocation-baseline");
        var account = Find(service, "account", owner.Id, AccountColumns) ?? throw Invalid("account-missing");
        RequireOwnedAccount(account, owner.Id, owner.Code, owner.Marker);
        var slot = Find(service, "new_channelslot", slotId, SlotColumns) ?? throw Invalid("slot-missing");
        var channel = Find(service, "new_linechannel", channelId, ChannelColumns) ?? throw Invalid("channel-missing");
        if (Reference(account, "new_channelslot", "new_channelslot") != slotId || Reference(slot, "new_tenant", "account") != owner.Id ||
            Choice(slot, "new_slotstatus") != 100000001 || Reference(slot, "new_channel", "new_linechannel") != channelId ||
            Text(channel, "new_product") != product || !keys.Take(count == 20 ? 2 : 1).Contains(Text(channel, "new_channelid"), StringComparer.Ordinal) ||
            Choice(channel, "new_channelstatus") is not (100000001 or 100000002)) throw Invalid("allocation-links");
        return slotId;
    }

    /// <summary>全排空後逐 owner 清理；每次寫入先刷新清冊並重驗鎖，拒絕竄改所有權，不以名稱查詢刪除其他輪次資料。</summary>
    private static void Cleanup(IOrganizationService service, Owned owner, Snapshot baseline, string journal, string lockPath)
    {
        var account = Find(service, "account", owner.Id, AccountColumns);
        if (account is null)
        {
            if (Find(service, "new_slotrequest", owner.RequestId, RequestColumns) is not null || Find(service, "new_slotrequest", owner.ReleaseId, RequestColumns) is not null) throw Invalid("orphan-request");
            return;
        }
        RequireOwnedAccount(account, owner.Id, owner.Code, owner.Marker);
        var allocation = Find(service, "new_slotrequest", owner.RequestId, RequestColumns);
        if (allocation is not null) RequireRequest(allocation, owner, false);
        var slotId = Reference(account, "new_channelslot", "new_channelslot", true);
        if (slotId.HasValue)
        {
            var before = baseline.Slots.SingleOrDefault(row => row.Id == slotId.Value) ?? throw Invalid("cleanup-slot");
            var current = Find(service, "new_channelslot", slotId.Value, SlotColumns) ?? throw Invalid("cleanup-slot");
            if (Choice(before, "new_slotstatus") != 100000000 || Reference(before, "new_tenant", "account", true) is not null ||
                Reference(current, "new_tenant", "account") != owner.Id || Choice(current, "new_slotstatus") != 100000001 ||
                Number(current, "new_usedrichmenus") != 0 || Reference(current, "new_channel", "new_linechannel") != Reference(before, "new_channel", "new_linechannel") ||
                allocation is not null && (Choice(allocation, "new_result") != 100000000 || Reference(allocation, "new_slot", "new_channelslot") != current.Id)) throw Invalid("cleanup-slot-owner");
            CheckLock(lockPath);
            Journal(journal, new { kind = "release-before-send", owner.Id, owner.ReleaseId, slotId });
            if (service.Create(Request(owner, true)) != owner.ReleaseId) throw Invalid("release-id");
            var released = Find(service, "new_slotrequest", owner.ReleaseId, RequestColumns) ?? throw Invalid("release-missing");
            RequireRequest(released, owner, true);
            if (Choice(released, "new_result") != 100000004 || Reference(released, "new_slot", "new_channelslot") != slotId ||
                Reference(released, "new_channel", "new_linechannel") != Reference(before, "new_channel", "new_linechannel")) throw Invalid("release-result");
            current = Find(service, "new_channelslot", slotId.Value, SlotColumns) ?? throw Invalid("released-slot");
            if (Reference(current, "new_tenant", "account", true) is not null || Choice(current, "new_slotstatus") != 100000000 || Number(current, "new_usedrichmenus") != 0 ||
                Reference(current, "new_channel", "new_linechannel") != Reference(before, "new_channel", "new_linechannel") || string.IsNullOrEmpty(current.RowVersion)) throw Invalid("released-state");
            // 使用官方樂觀並行只回復本輪造成的計數／時間變動；不回寫租戶、Channel、容量或任何既有擁有權。
            var restore = new Entity("new_channelslot", slotId.Value) { RowVersion = current.RowVersion };
            foreach (var field in RestoreColumns) restore[field] = before.Attributes.TryGetValue(field, out var value) ? value : null;
            CheckLock(lockPath); Journal(journal, new { kind = "restore-before-send", current.Id, current.RowVersion });
            service.Execute(new UpdateRequest { Target = restore, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
        }
        foreach (var release in new[] { false, true })
        {
            var row = Find(service, "new_slotrequest", release ? owner.ReleaseId : owner.RequestId, RequestColumns);
            if (row is null) continue;
            RequireRequest(row, owner, release); CheckLock(lockPath); Journal(journal, new { kind = "delete-request-before-send", row.Id });
            service.Delete("new_slotrequest", row.Id);
            if (Find(service, "new_slotrequest", row.Id, RequestColumns) is not null) throw Invalid("request-remains");
        }
        account = Find(service, "account", owner.Id, AccountColumns) ?? throw Invalid("cleanup-account");
        RequireOwnedAccount(account, owner.Id, owner.Code, owner.Marker);
        if (Reference(account, "new_channelslot", "new_channelslot", true) is not null ||
            Read(service, "new_channelslot", SlotColumns, filter: criteria => criteria.AddCondition("new_tenant", ConditionOperator.Equal, owner.Id)).Count != 0) throw Invalid("retained-allocation");
        CheckLock(lockPath); Journal(journal, new { kind = "delete-account-before-send", owner.Id }); service.Delete("account", owner.Id);
        if (Find(service, "account", owner.Id, AccountColumns) is not null) throw Invalid("account-remains");
    }

    private static readonly string[] RestoreColumns = { "new_usecount", "new_allocatedon", "new_releasedon", "new_reserveduntil" };

    /// <summary>完整 ID 集合與每筆名額／Channel 明列值都須回復；不同 ID 同名合法保留，重複 ID 已在 Read 階段拒絕。</summary>
    private static void RequireRestored(Snapshot before, Snapshot after)
    {
        if (!before.Accounts.Select(row => row.Id).Order().SequenceEqual(after.Accounts.Select(row => row.Id).Order()) ||
            !before.Requests.Select(row => row.Id).Order().SequenceEqual(after.Requests.Select(row => row.Id).Order()) ||
            State(before.Slots) != State(after.Slots) || State(before.Channels) != State(after.Channels)) throw Invalid("baseline-restoration");
    }

    /// <summary>只比較明列安全欄位及正式主鍵；系統 RowVersion 不應回復，null 與缺欄依 SDK 語意同為無值。</summary>
    private static string State(IEnumerable<Entity> rows) => JsonSerializer.Serialize(rows.OrderBy(row => row.Id).Select(row => new {
        row.Id, values = row.Attributes.Where(pair => pair.Value is not null).OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => JournalValue(pair.Value)) }));

    private const string PlatformAssembly = "SpeechMessage.Dynamics.Platform.Plugins";
    private const string ProductMap = "{\"products\":{\"goodshepherd20\":\"GoodShepherd2\",\"construction\":\"GoodButler\",\"association\":\"GoodGuild\"}}";
    private static readonly string[] AccountColumns = { "name", "new_tenantcode", "new_istenant", "new_istesttenant", "new_channelslot" };
    private static readonly string[] RequestColumns = { "new_name", "new_tenant", "new_operation", "new_correlationid", "new_result", "new_channel", "new_slot" };
    private static readonly string[] SlotColumns = { "new_channel", "new_slotnumber", "new_slotstatus", "new_tenant", "new_richmenuquota", "new_usedrichmenus", "new_usecount", "new_allocatedon", "new_releasedon", "new_reserveduntil" };
    private static readonly string[] ChannelColumns = { "new_channelid", "new_product", "new_channeltype", "new_capacity", "new_poolorder", "new_channelstatus", "new_reservedrichmenus", "new_tenantrichmenuquota" };
    private static readonly string[] AssemblyColumns = { "name", "isolationmode", "sourcetype" };
    private static readonly string[] TypeColumns = { "typename", "pluginassemblyid" };
    private static readonly string[] StepColumns = { "eventhandler", "sdkmessageid", "sdkmessagefilterid", "stage", "mode", "rank", "statecode", "supporteddeployment", "filteringattributes", "configuration", "sdkmessageprocessingstepsecureconfigid" };
    private static readonly string[] MessageColumns = { "name" };
    private static readonly string[] FilterColumns = { "sdkmessageid", "primaryobjecttypecode", "secondaryobjecttypecode" };
    private static readonly string[] ImageColumns = { "sdkmessageprocessingstepid", "name", "entityalias", "imagetype", "attributes", "messagepropertyname" };

    /// <summary>本輪所有候選主鍵在送出前持久化；只有 SDK 回應及精確讀回才能確認它已是正式資料庫列。</summary>
    private sealed class Owned(string prefix, string marker)
    {
        /// <summary>寫前記錄的 account 正式候選主鍵；送出後須精確讀回確認，不能只憑 GUID 授權清理。</summary>
        internal Guid Id { get; } = Guid.NewGuid();
        /// <summary>固定 c101–c120 或 b101 加完整本輪 marker，避免其他輪次／合法同名資料混用。</summary>
        internal string Code { get; } = prefix + "-" + marker;
        /// <summary>只在本輪與外部清冊存活的不可變所有權標記，不含 credential 或使用者資料。</summary>
        internal string Marker { get; } = marker;
        /// <summary>配發申請正式候選 ID；回應遺失時仍以此 ID／correlation 精確找回，不作廣域刪除。</summary>
        internal Guid RequestId { get; } = Guid.NewGuid();
        /// <summary>配發專屬 correlation，不取代正式 ID；送出前與 ID 一起持久化。</summary>
        internal string Correlation { get; } = Guid.NewGuid().ToString("D");
        /// <summary>獨立釋放申請候選 ID，於首次 SDK 寫入之前已在完整所有權清冊。</summary>
        internal Guid ReleaseId { get; } = Guid.NewGuid();
        /// <summary>釋放專屬 correlation，避免配發單重試鍵混用；不跨租戶共用。</summary>
        internal string ReleaseCorrelation { get; } = Guid.NewGuid().ToString("D");
    }

    /// <summary>一次完整且有界的資料候選；沒有 credential、SDK client、HttpContext 或跨輪次快取。</summary>
    private sealed record Snapshot(List<Entity> Accounts, List<Entity> Requests, List<Entity> Slots, List<Entity> Channels);

    /// <summary>只接受 catalog 的精確 canonical 測試端點；不可借 legacy 相容清單或 caller URL 擴權。</summary>
    private static void RequireTarget(OrganizationDefinition target, string organization)
    {
        if (target.Name != organization || target.Kind != "test" || target.Host != "https://" + organization + ".speechmessage.com.tw") throw Invalid("catalog");
    }

    /// <summary>WhoAmI 與組織定義必須同時返回相同非空正式 ID 與精確 uniqueName；每個 worker 都重新核對。</summary>
    private static Guid RequireServer(IOrganizationService service, string organization)
    {
        var current = (RetrieveCurrentOrganizationResponse)service.Execute(new RetrieveCurrentOrganizationRequest());
        var who = (WhoAmIResponse)service.Execute(new WhoAmIRequest());
        if (current.Detail is null || current.Detail.OrganizationId == Guid.Empty || current.Detail.UniqueName != organization ||
            who.OrganizationId != current.Detail.OrganizationId || who.UserId == Guid.Empty ||
            !Version.TryParse(current.Detail.OrganizationVersion, out var version) || version.Major != 9) throw Invalid("server");
        return who.OrganizationId;
    }

    /// <summary>只讀固定 lane=A、wp=F3 三欄鎖，持有讀取時禁止覆寫；不建立、刪除或借用其他工作包的鎖。</summary>
    private static void CheckLock(string path)
    {
        SchemaDeploymentCli.EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > 1024) throw Invalid("lock");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var parts = reader.ReadToEnd().Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] != "lane=A" || parts[1] != "wp=F3" || !parts[2].StartsWith("start=", StringComparison.Ordinal) ||
            !DateTimeOffset.TryParseExact(parts[2][6..], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw Invalid("lock");
    }

    /// <summary>外部證據只允許 repo 外實體路徑；逐層拒絕 link／junction，避免正式主鍵或 marker 被意外提交。</summary>
    private static void RequireExternal(string path)
    {
        var full = Path.GetFullPath(path); var repository = Repository();
        if (full.Equals(repository, StringComparison.OrdinalIgnoreCase) || full.StartsWith(repository + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw Invalid("journal-path");
        SchemaDeploymentCli.EnsureNoLinks(full);
    }

    /// <summary>沿編譯輸出祖先找本工作樹，避免目前工作目錄或環境變數換掉 catalog／schema 授權來源。</summary>
    private static string Repository()
    {
        for (var directory = new DirectoryInfo(System.AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw Invalid("repository");
    }

    /// <summary>逐筆 UTF-8 無 BOM／CRLF 清冊，CreateNew 或有界 append 後 Flush(true) 並關閉；任何失敗阻擋下一個 SDK 寫入。</summary>
    private static void Journal(string path, object value, bool create = false)
    {
        SchemaDeploymentCli.EnsureNoLinks(path);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\r\n");
        using var stream = new FileStream(path, create ? FileMode.CreateNew : FileMode.Append, FileAccess.Write, FileShare.Read);
        if (stream.Length + bytes.Length > 4 * 1024 * 1024) throw Invalid("journal-budget");
        stream.Write(bytes); stream.Flush(true);
    }

    /// <summary>只把明列 baseline 欄位投影為可保留純值；不讓 SDK Entity 的擴充參照或秘密進 JSON。</summary>
    private static object? JournalValue(object? value) => value switch { null => null, OptionSetValue option => option.Value,
        EntityReference reference => new { reference.LogicalName, reference.Id }, int number => number, string text => text,
        DateTime time => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), _ => throw Invalid("journal-type") };

    /// <summary>只有三欄核定輸入及預先持久化主鍵；名稱與結果由服務端 Plugin 填入，caller 不指定 Channel 或產品。</summary>
    private static Entity Request(Owned owner, bool release) => new("new_slotrequest", release ? owner.ReleaseId : owner.RequestId)
    {
        ["new_tenant"] = new EntityReference("account", owner.Id), ["new_operation"] = new OptionSetValue(release ? 100000001 : 100000000),
        ["new_correlationid"] = release ? owner.ReleaseCorrelation : owner.Correlation
    };

    /// <summary>每張單都核對正式 ID／租戶／操作與完整 correlation；只有相同前綴或同名不能授權刪除。</summary>
    private static void RequireRequest(Entity row, Owned owner, bool release)
    {
        Identity(row, "new_slotrequest", release ? owner.ReleaseId : owner.RequestId);
        var correlation = release ? owner.ReleaseCorrelation : owner.Correlation;
        if (Reference(row, "new_tenant", "account") != owner.Id || Choice(row, "new_operation") != (release ? 100000001 : 100000000) ||
            Text(row, "new_correlationid") != correlation || Text(row, "new_name") != correlation) throw Invalid("request-owner");
    }

    /// <summary>完整查詢採穩定主鍵排序與有界分頁；重複 ID、缺 cookie、空續頁、錯表及超量全拒絕，不做去重。</summary>
    private static List<Entity> Read(IOrganizationService service, string entity, string[] columns,
        Action<FilterExpression>? filter = null, CancellationToken cancellationToken = default)
    {
        var query = new QueryExpression(entity) { ColumnSet = new ColumnSet(columns), PageInfo = new PagingInfo { Count = 250, PageNumber = 1 } };
        query.AddOrder(entity + "id", OrderType.Ascending); filter?.Invoke(query.Criteria);
        var result = new List<Entity>(); var ids = new HashSet<Guid>(); var cookies = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = service.RetrieveMultiple(query) ?? throw Invalid("query");
            foreach (var row in page.Entities)
            {
                Identity(row, entity, row.Id);
                if (!ids.Add(row.Id) || result.Count >= 10000) throw Invalid("query-identity");
                result.Add(row);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!page.MoreRecords) return result;
            if (page.Entities.Count == 0 || string.IsNullOrEmpty(page.PagingCookie) || page.PagingCookie.Length > 16384 ||
                !cookies.Add(page.PagingCookie) || query.PageInfo.PageNumber >= 40) throw Invalid("query-page");
            query.PageInfo.PageNumber++; query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    /// <summary>按正式主鍵讀零或一筆；不存在與 SDK 故障保持區別，不能 catch 任意例外就視為已刪除。</summary>
    private static Entity? Find(IOrganizationService service, string entity, Guid id, string[] columns) =>
        Read(service, entity, columns, filter: criteria => criteria.AddCondition(entity + "id", ConditionOperator.Equal, id)).SingleOrDefault();

    /// <summary>baseline 只取 ID 的租戶／申請單及無秘密池欄位；未發布半份結果不會交給後續清理。</summary>
    private static Snapshot ReadSnapshot(IOrganizationService service, CancellationToken token) => new(
        Read(service, "account", Array.Empty<string>(), cancellationToken: token), Read(service, "new_slotrequest", Array.Empty<string>(), cancellationToken: token),
        Read(service, "new_channelslot", SlotColumns, cancellationToken: token), Read(service, "new_linechannel", ChannelColumns,
            criteria => criteria.AddCondition("new_product", ConditionOperator.NotNull), token));

    /// <summary>正式 ID 與邏輯名稱精確一致且非空；不得用操作 token 或顯示名稱取代資料庫身分。</summary>
    private static void Identity(Entity row, string entity, Guid id)
    { if (row is null || id == Guid.Empty || row.Id != id || row.LogicalName != entity) throw Invalid("identity"); }
    /// <summary>必要字串不做隱含轉型或 trim，缺值及型別錯誤固定拒絕。</summary>
    private static string Text(Entity row, string field) => row.Attributes.TryGetValue(field, out var raw) && raw is string value ? value : throw Invalid("text");
    /// <summary>Choice 必須保持 SDK 型別，Boolean 與整數不能冒充 FS 是否旗標。</summary>
    private static int Choice(Entity row, string field) => row.Attributes.TryGetValue(field, out var raw) && raw is OptionSetValue value ? value.Value : throw Invalid("choice");
    /// <summary>數量只接受明確 int，缺欄不以零掩蓋不完整資料。</summary>
    private static int Number(Entity row, string field) => row.Attributes.TryGetValue(field, out var raw) && raw is int value ? value : throw Invalid("number");
    /// <summary>lookup 必須是指定表的非空正式 ID；optional 只允許真正 null，不接受錯型別。</summary>
    private static Guid? Reference(Entity row, string field, string entity, bool optional = false)
    {
        if ((!row.Attributes.TryGetValue(field, out var raw) || raw is null) && optional) return null;
        return raw is EntityReference value && value.Id != Guid.Empty && value.LogicalName == entity ? value.Id : throw Invalid("lookup");
    }
    /// <summary>固定錯誤碼不含組織資料、marker、正式 ID、credential 或原始 SDK fault 本文。</summary>
    private static InvalidDataException Invalid(string code) => new("F3 contract: " + code + ".");
    /// <summary>
    /// 每個真正例外先交本輪注入的共用診斷，只輸出固定階段與型別；WhenAll 的每個原物件皆逐一報告。
    /// 診斷自身失敗只走固定 stderr，不阻斷其他 owner 清理；不把 inner exception／遠端本文帶入 xUnit。
    /// </summary>
    private static InvalidOperationException SafeFailure(string phase, Exception exception, Action<Exception, string> report)
    {
        var failures = exception is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : new[] { exception }.AsEnumerable();
        foreach (var failure in failures)
        {
            try { report(failure, "F3ChannelPoolLiveSupport." + phase); }
            catch { Console.Error.WriteLine("[Live3P.F3] DiagnosticReportFailed"); }
        }
        return new InvalidOperationException("F3 " + phase + " failed (" + exception.GetType().Name + ").");
    }
}

/// <summary>精確離線編譯與純資料契約；本類不呼叫 Live 入口，也沒有連線、秘密或資料寫入能力。</summary>
[Trait("Category", "Offline")]
[Trait("WP", "F3")]
public sealed class ChannelPoolLiveOfflineContractTests
{
    /// <summary>編譯後反射核對兩個入口名稱與分類；不建立測試類別或執行其方法。</summary>
    [Fact]
    public void LiveEntrypoints_HaveExactClassificationAndNoSkip()
    {
        var type = typeof(ChannelPoolLiveTests);
        var traits = type.CustomAttributes.Where(attribute => attribute.AttributeType == typeof(TraitAttribute))
            .Select(attribute => (Name: (string?)attribute.ConstructorArguments[0].Value,
                Value: (string?)attribute.ConstructorArguments[1].Value)).ToArray();
        Assert.Contains(traits, trait => trait.Name == "Category" && trait.Value == "Live3P");
        Assert.Contains(traits, trait => trait.Name == "WP" && trait.Value == "F3");
        var methods = type.GetMethods().Where(method => method.DeclaringType == type).ToArray();
        Assert.Equal(2, methods.Length);
        Assert.Contains(methods, method => method.Name == "SlotRequest_TwentyConcurrent_AllDistinct_Live");
        Assert.Contains(methods, method => method.Name == "Construction_AllocatesOnlyGoodButlerChannel_Live");
        foreach (var method in methods)
        {
            var fact = Assert.Single(method.GetCustomAttributes(typeof(FactAttribute), false).Cast<FactAttribute>());
            Assert.Null(fact.Skip);
            Assert.Equal(typeof(Task), method.ReturnType);
        }
    }

    /// <summary>拒絕 legacy、第三產品、大小寫及錯誤數量，防止讀秘密之前的組織邊界退化。</summary>
    [Theory]
    [InlineData("goodshepherdback", 20)]
    [InlineData("association", 1)]
    [InlineData("Goodshepherd20", 20)]
    [InlineData("goodshepherd20", 1)]
    [InlineData("construction", 20)]
    public void Target_UnknownLegacyOrWrongCount_Refused(string organization, int count) =>
        Assert.Throws<InvalidDataException>(() => F3ChannelPoolLiveSupport.Product(organization, count));

    /// <summary>兩個核定案例解析成固定產品；沒有 caller 傳產品或 Channel 的後備路徑。</summary>
    [Fact]
    public void Target_ApprovedCases_MapExactly()
    {
        Assert.Equal("GoodShepherd2", F3ChannelPoolLiveSupport.Product("goodshepherd20", 20));
        Assert.Equal("GoodButler", F3ChannelPoolLiveSupport.Product("construction", 1));
    }

    /// <summary>以錯誤 ID、marker、旗標及實體注入故障；任何一項不符皆不能授權刪除。</summary>
    [Theory]
    [InlineData("id")]
    [InlineData("marker")]
    [InlineData("flag")]
    [InlineData("boolean")]
    [InlineData("entity")]
    public void Cleanup_OtherOwnershipOrNonTestTenant_Refused(string fault)
    {
        var id = Guid.NewGuid();
        var marker = Guid.NewGuid().ToString("N");
        var code = "c101-" + marker;
        var row = Account(id, code, marker);
        if (fault == "id") row.Id = Guid.NewGuid();
        if (fault == "marker") row["name"] = "F3-other-c101";
        if (fault == "flag") row["new_istesttenant"] = new OptionSetValue(100000000);
        if (fault == "boolean") row["new_istesttenant"] = true;
        if (fault == "entity") row.LogicalName = "contact";
        Assert.Throws<InvalidDataException>(() => F3ChannelPoolLiveSupport.RequireOwnedAccount(row, id, code, marker));
    }

    /// <summary>合成正式 ID 及完整 marker 控制組可驗證；不以只有前綴的顯示名作所有權。</summary>
    [Fact]
    public void Cleanup_ExactIdMarkerAndChoiceFlags_Accepted()
    {
        var id = Guid.NewGuid();
        var marker = Guid.NewGuid().ToString("N");
        var code = "c101-" + marker;
        F3ChannelPoolLiveSupport.RequireOwnedAccount(Account(id, code, marker), id, code, marker);
    }

    /// <summary>只建立單次合成 account；無連線或 Dispose 資源，正式 Choice 型別刻意不能改成 Boolean。</summary>
    private static Entity Account(Guid id, string code, string marker) => new("account", id)
    {
        ["name"] = "F3-" + marker + "-" + code[..4], ["new_tenantcode"] = code,
        ["new_istenant"] = new OptionSetValue(100000001), ["new_istesttenant"] = new OptionSetValue(100000001)
    };
}

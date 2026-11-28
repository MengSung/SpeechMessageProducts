/*
檔案整體說明
目的與定位：F12 測試租戶建立與精確清理的 SDK 作業邊界，僅接受三個核定測試組織及對應產品。
主要功能與流程：建立時以 SDK 交易建立測試 account、角色、合成會友及配發申請，先保存只含穩定資料庫 ID 與提交結果的本地復原清單，未知結果不得自動清理；清理時重新驗證遠端父子 lookup 後才呼叫受限 LINE 清理委派及釋放名額。
資源擁有權與隔離：SDK 及 LINE 委派由外層命令借入，命令必須持有整台電腦共用組織鎖；不建立背景工作、不保存 token 或使用者資料到 manifest。同步 SDK 入場後必須排空，原始例外由外層共用告警入口處理。
失敗與驗證：任何範圍、穩定身分、容量或名額讀回衝突都拒絕；逾時、回應遺失或未知提交保留 manifest 至明確清理。account 尚不可見時，未知提交一律保留；已確定未送出、正式回滾或完整成功回應排除晚到建立後，仍須每個正式 ID 均不存在才接受空清理。合成 SDK 測試驗證正式拒絕、錯配釋放及跨租戶拒絕，不能當作實機結果。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessage.Dynamics.Deployment.TestTenants;

/// <summary>命令範圍內借用 SDK 的測試租戶協調器，不擁有連線或全域狀態。</summary>
internal sealed class TestTenantService
{
    private const int MaximumRows = 128;
    private readonly IOrganizationService service;
    private readonly string organization;
    private readonly string product;
    private readonly string artifactRoot;

    /// <summary>只借用已經由共同關卡核驗目標身分的單命令服務及固定 artifact 根。</summary>
    internal TestTenantService(IOrganizationService service, string organization, string product, string artifactRoot)
    {
        if ((organization, product) is not ("goodshepherd20", "GoodShepherd2") and not
            ("construction", "GoodButler") and not ("association", "GoodGuild"))
            throw new ArgumentException("Test tenant scope is not approved.");
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.organization = organization;
        this.product = product;
        this.artifactRoot = Path.GetFullPath(artifactRoot);
    }

    /// <summary>建立隔離測試租戶並保存精確復原清單；不輸出任何 LINE 使用者識別資料。</summary>
    /// <remarks>
    /// 呼叫端的期望值只做配發後比對；白名單委派不能改寫 F3 選擇。寫前清單一律是未知結果，
    /// SDK 入場前取消、正式 ExecuteTransactionFault 及逐筆驗證成功回應才可持久化確定狀態。
    /// 傳輸逾時、回應遺失或錯誤型別的回應不立即清理，即使當下可讀到資料也保留原例外與清單；
    /// 明確 cleanup 命令須重新驗證 account 及所有父子範圍。已確認結果的復原失敗則聚合原始例外與清理例外。
    /// </remarks>
    internal TestTenantManifest Create(string code, string name, string? expectedChannel,
        IReadOnlyList<string> admins, IReadOnlyList<string> members, CancellationToken ct, Action<string>? validateChannel = null)
    {
        ValidateCode(code);
        ArgumentNullException.ThrowIfNull(admins);
        ArgumentNullException.ThrowIfNull(members);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150 || admins.Count > 8 || members.Count > 24 ||
            admins.Concat(members).Any(user => !ValidUserId(user)) ||
            admins.Distinct(StringComparer.Ordinal).Count() != admins.Count || members.Distinct(StringComparer.Ordinal).Count() != members.Count ||
            expectedChannel is not null && !ValidChannel(expectedChannel))
            throw new ArgumentException("Test tenant input is invalid.");
        ct.ThrowIfCancellationRequested();
        if (File.Exists(ManifestPath(code))) throw new InvalidOperationException("A test tenant manifest already exists.");
        if (Read("account", "new_tenantcode", code, "new_tenantcode").Count != 0)
            throw new InvalidOperationException("The test tenant code is already in use.");
        var account = New("account");
        account["name"] = name.StartsWith("測試", StringComparison.Ordinal) ? name : "測試" + name;
        account["new_tenantcode"] = code;
        account["new_istenant"] = new OptionSetValue(100000001);
        account["new_istesttenant"] = new OptionSetValue(100000001);
        if (product == "GoodShepherd2") account["new_lifecyclestatus"] = new OptionSetValue(ReadTrialOption(ct));
        var rows = new List<Entity> { account };
        var roleIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var roleCode in new[] { "member", "admin" })
        {
            var role = New("new_role");
            role["new_name"] = "測試" + roleCode;
            role["new_tenantid"] = account.ToEntityReference();
            role["new_code"] = roleCode;
            role["new_isadmin"] = new OptionSetValue(roleCode == "admin" ? 100000001 : 100000000);
            rows.Add(role);
            roleIds.Add(roleCode, role.Id);
        }
        // 同一使用者同時是 admin 與 member 只建立一個 Contact，兩個不同角色指派則完整保留。
        foreach (var user in admins.Concat(members).Distinct(StringComparer.Ordinal))
        {
            var contact = New("contact");
            contact["lastname"] = "測試會友";
            contact["parentcustomerid"] = account.ToEntityReference();
            contact["new_lineid"] = user;
            rows.Add(contact);
            foreach (var roleCode in admins.Contains(user, StringComparer.Ordinal) ? new[] { "member", "admin" } : new[] { "member" })
            {
                var assignment = New("new_roleassignment");
                assignment["new_name"] = "測試角色指派";
                assignment["new_contactid"] = contact.ToEntityReference();
                assignment["new_roleid"] = new EntityReference("new_role", roleIds[roleCode]);
                rows.Add(assignment);
            }
        }
        var allocation = SlotRequest(account.Id, release: false);
        rows.Add(allocation);
        var manifest = TestTenantManifest.Create(organization, product, code,
            rows.Select(row => new TestTenantRecordId(row.LogicalName, row.Id)));
        // 這些 GUID 是本命令指定給 CreateRequest.Target.Id 的正式主鍵，不是顯示／重試／draft key。
        // 寫前落盤使傳輸結果不確定時仍能精確讀回，不因取消遺失唯一清理範圍。
        manifest.Save(artifactRoot, ct);
        var submitted = false;
        try
        {
            ExecuteCreates(rows, ct, beforeSubmit: () => submitted = true,
                afterConfirmedResponse: () => manifest = manifest.RecordCreationOutcome(artifactRoot, TestTenantCreationOutcome.Confirmed));
            var allocated = GetRequired("new_slotrequest", allocation.Id, "new_tenant", "new_result", "new_channel", "new_slot");
            if (allocated.GetAttributeValue<OptionSetValue>("new_result")?.Value != 100000000)
                throw new InvalidOperationException("Test tenant slot allocation was refused.");
            var channel = ResolveChannel(manifest, allocated);
            if (expectedChannel is not null && !string.Equals(expectedChannel, channel, StringComparison.Ordinal))
                throw new InvalidOperationException("Allocated test Channel does not match the expected Channel.");
            validateChannel?.Invoke(channel);
            ct.ThrowIfCancellationRequested();
            return manifest;
        }
        catch (Exception failure)
        {
            // SDK 一旦入場，查無資料不能證明尚未提交；未知結果只保留，禁止立即清理延遲交易的唯一正式 ID。
            if (submitted && manifest.CreationOutcome == TestTenantCreationOutcome.Unknown && !IsConfirmedRollback(failure)) throw;
            try
            {
                if (manifest.CreationOutcome == TestTenantCreationOutcome.Unknown)
                    manifest = manifest.RecordCreationOutcome(artifactRoot, submitted
                        ? TestTenantCreationOutcome.RolledBack : TestTenantCreationOutcome.NotSubmitted);
                // 建立尚未簽發邀請，沒有本命令創建的 LINE 宮格；只走精確 SDK 回復，不呼叫外部 LINE。
                CleanupAsync(code, static (_, _, _) => Task.CompletedTask, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Test tenant creation and scoped recovery failed; manifest is retained.", failure, cleanupFailure);
            }
            throw;
        }
    }

    /// <summary>從本地正式 ID 清單與遠端測試租戶／slot lookup 讀取配發 Channel，不按設定的第一筆猜測。</summary>
    /// <remarks>僅供邀請在載入 LINE token 前核對白名單；完整角色及配發鏈仍由簽發服務重新驗證。</remarks>
    internal string ReadChannelKey(string code, CancellationToken ct)
    {
        ValidateCode(code);
        ct.ThrowIfCancellationRequested();
        var manifest = TestTenantManifest.Load(artifactRoot, organization, code);
        if (manifest.Product != product) throw ScopeFailure();
        var account = GetRequired("account", manifest.TenantId, "new_tenantcode", "new_istenant", "new_istesttenant", "new_channelslot");
        ValidateAccount(account, code);
        ct.ThrowIfCancellationRequested();
        var result = ResolveFromSlot(account);
        ct.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>依本地清單重驗遠端範圍，LINE 清理成功後才刪除本租戶資料並釋放名額。</summary>
    internal Task CleanupAsync(string code, Func<string, IReadOnlyList<string>, CancellationToken, Task> cleanLine,
        CancellationToken ct) => CleanupCoreAsync(code, cleanLine, refreshAllowed: true, ct);

    /// <summary>最多兩次盤點：撤銷未用邀請後只重讀一次；持續變動一律停止，不能形成無界遞迴或清理掃描。</summary>
    private async Task CleanupCoreAsync(string code, Func<string, IReadOnlyList<string>, CancellationToken, Task> cleanLine,
        bool refreshAllowed, CancellationToken ct)
    {
        ValidateCode(code);
        ArgumentNullException.ThrowIfNull(cleanLine);
        ct.ThrowIfCancellationRequested();
        var manifest = TestTenantManifest.Load(artifactRoot, organization, code);
        if (manifest.Product != product) throw ScopeFailure();
        var account = Get("account", manifest.TenantId, "new_tenantcode", "new_istenant", "new_istesttenant", "new_channelslot");
        if (account is null)
        {
            // 未知建立可能仍在遠端執行，任何空讀都不能刪唯一清單。已確定的三種結果均排除晚到建立；
            // 包含完整成功回應之後，遠端精確刪除已完成但本地刪檔失敗的續跑，仍逐一讀回全部正式 ID。
            if (manifest.CreationOutcome == TestTenantCreationOutcome.Unknown)
                throw new InvalidOperationException("Test tenant creation cannot be resolved while the account is absent; manifest is retained.");
            foreach (var record in manifest.Records)
            {
                ct.ThrowIfCancellationRequested();
                if (Get(record.EntityLogicalName, record.Id) is not null) throw ScopeFailure();
            }
            ct.ThrowIfCancellationRequested();
            DeleteManifest(manifest);
            return;
        }
        ValidateAccount(account, code);
        var contacts = ReadScoped("contact", "parentcustomerid", account.Id, "parentcustomerid", "new_lineid");
        var roles = ReadScoped("new_role", "new_tenantid", account.Id, "new_tenantid");
        var invites = ReadScoped("new_testinvite", "new_tenant", account.Id, "new_tenant", "new_usedon");
        var requests = ReadScoped("new_slotrequest", "new_tenant", account.Id, "new_tenant", "new_channel", "new_slot", "new_result", "new_operation");
        ct.ThrowIfCancellationRequested();
        var assignments = ReadAssignments(roles, contacts);
        ct.ThrowIfCancellationRequested();
        var permissions = ReadByRoles("new_rolepermission", roles, "new_roleid");
        ct.ThrowIfCancellationRequested();
        foreach (var row in contacts)
            if (Reference(row, "parentcustomerid", "account") != account.Id) throw ScopeFailure();
        foreach (var row in roles)
            if (Reference(row, "new_tenantid", "account") != account.Id) throw ScopeFailure();
        foreach (var row in invites.Concat(requests))
            if (Reference(row, "new_tenant", "account") != account.Id) throw ScopeFailure();
        var contactIds = contacts.Select(row => row.Id).ToHashSet();
        var roleIdsSet = roles.Select(row => row.Id).ToHashSet();
        if (assignments.Any(row => !contactIds.Contains(Reference(row, "new_contactid", "contact")))) throw ScopeFailure();
        foreach (var row in assignments.Concat(permissions))
            if (!roleIdsSet.Contains(Reference(row, "new_roleid", "new_role"))) throw ScopeFailure();
        var snapshot = contacts.Concat(roles).Concat(invites).Concat(requests).Concat(assignments).Concat(permissions)
            .Append(account).ToDictionary(row => (row.LogicalName, row.Id));
        foreach (var tracked in manifest.Records)
        {
            ct.ThrowIfCancellationRequested();
            var remote = Get(tracked.EntityLogicalName, tracked.Id);
            if (remote is not null && !snapshot.ContainsKey((tracked.EntityLogicalName, tracked.Id))) throw ScopeFailure();
        }
        var allocationId = manifest.Records.Single(record => record.EntityLogicalName == "new_slotrequest").Id;
        var allocation = requests.SingleOrDefault(row => row.Id == allocationId);
        // 正式同步配發已明確拒絕且從未掛 slot 的建立失敗，不存在可操作的 LINE Channel。
        // 僅在零邀請、零任何 tenant slot、原配發單無兩種 lookup 時接受；未知結果仍保留清單。
        var unallocated = allocation is not null && invites.Count == 0 &&
            allocation.GetAttributeValue<OptionSetValue>("new_operation")?.Value == 100000000 &&
            allocation.GetAttributeValue<OptionSetValue>("new_result")?.Value is 100000001 or 100000002 or 100000003 or 100000005 &&
            allocation.GetAttributeValue<EntityReference>("new_channel") is null &&
            allocation.GetAttributeValue<EntityReference>("new_slot") is null &&
            account.GetAttributeValue<EntityReference>("new_channelslot") is null &&
            ReadScoped("new_channelslot", "new_tenant", account.Id, "new_tenant").Count == 0;
        var channelKey = unallocated ? null : allocation is null ? ResolveFromSlot(account) : ResolveChannel(manifest, allocation);
        var users = contacts.Select(row => row.GetAttributeValue<string>("new_lineid"))
            .Where(user => !string.IsNullOrWhiteSpace(user)).ToArray();
        if (users.Any(user => !ValidUserId(user))) throw ScopeFailure();
        ct.ThrowIfCancellationRequested();
        // 先以每筆邀請的 RowVersion CAS 封鎖尚未完成的加入；並行 consume 要嘛先完成，要嘛整筆回滾。
        var invalidate = invites.Where(row => !row.Attributes.TryGetValue("new_usedon", out var used) || used is null).Select(row =>
        {
            if (string.IsNullOrWhiteSpace(row.RowVersion)) throw ScopeFailure();
            return (OrganizationRequest)new UpdateRequest
            {
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
                Target = new Entity("new_testinvite", row.Id) { RowVersion = row.RowVersion, ["new_usedon"] = DateTime.UtcNow }
            };
        }).ToArray();
        if (invalidate.Length != 0)
        {
            if (!refreshAllowed) throw new InvalidOperationException("Test tenant invitations changed during cleanup; no unbounded retry is allowed.");
            ExecuteBatch(invalidate, ct);
            // CAS 返回前另一個 consume 可能先完成；重新盤點其 Contact，才可開始 LINE 與資料刪除。
            await CleanupCoreAsync(code, cleanLine, refreshAllowed: false, ct).ConfigureAwait(false);
            return;
        }
        if (channelKey is not null)
            await cleanLine(channelKey, Array.AsReadOnly(users.Distinct(StringComparer.Ordinal).ToArray()), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var children = invites.Concat(assignments).Concat(permissions).Concat(contacts).Concat(roles).ToArray();
        if (children.Length != 0) ExecuteDeletes(children, ct);
        // 釋放由 F3 唯一配發器完成；若宮格計數尚未歸零，保留 manifest／account 等正式清理器回報。
        if (!unallocated)
        {
            var release = SlotRequest(account.Id, release: true);
            ExecuteCreates([release], ct);
            var released = GetRequired("new_slotrequest", release.Id, "new_result", "new_tenant");
            if (released.GetAttributeValue<OptionSetValue>("new_result")?.Value != 100000004 ||
                ReadScoped("new_channelslot", "new_tenant", account.Id, "new_tenant").Count != 0 ||
                GetRequired("account", account.Id, "new_channelslot").GetAttributeValue<EntityReference>("new_channelslot") is not null)
                throw new InvalidOperationException("Test tenant slot release is awaiting authoritative cleanup.");
        }
        requests = ReadScoped("new_slotrequest", "new_tenant", account.Id, "new_tenant");
        ExecuteDeletes(requests.Append(account).ToArray(), ct);
        DeleteManifest(manifest);

        // token 僅由本 operation-local closure 借用；每次同步 I/O 真正排空後才決定是否停止下一次查詢。
        List<Entity> ReadScoped(string entity, string attribute, object value, params string[] columns)
        {
            ct.ThrowIfCancellationRequested();
            var result = Read(entity, attribute, value, columns);
            ct.ThrowIfCancellationRequested();
            return result;
        }
    }

    /// <summary>從已發布的正式 choice metadata 找唯一繁中試用標籤，禁止猜選項數字或讀未發布候選。</summary>
    private int ReadTrialOption(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = service.Execute(new RetrieveAttributeRequest
        { EntityLogicalName = "account", LogicalName = "new_lifecyclestatus", RetrieveAsIfPublished = false });
        if (response is not RetrieveAttributeResponse { AttributeMetadata: PicklistAttributeMetadata { OptionSet: { } options } }) throw ScopeFailure();
        var matches = options.Options.Where(option => option.Value.HasValue && option.Label?.LocalizedLabels.Any(label =>
            label.LanguageCode == 1028 && string.Equals(label.Label, "試用中", StringComparison.Ordinal)) == true).ToArray();
        return matches.Length == 1 ? matches[0].Value!.Value : throw ScopeFailure();
    }

    /// <summary>分配申請的權威 Channel 必須屬固定產品；只能輸出公開數字 ID，不能以名稱或 token 代替。</summary>
    private string ResolveChannel(TestTenantManifest manifest, Entity allocation)
    {
        if (Reference(allocation, "new_tenant", "account") != manifest.TenantId) throw ScopeFailure();
        var channel = GetRequired("new_linechannel", Reference(allocation, "new_channel", "new_linechannel"), "new_product", "new_channelid");
        return ChannelKey(channel);
    }

    /// <summary>只用現存 account 的正式 slot lookup 解析 Channel；缺少原申請又無 slot 時拒絕猜測。</summary>
    private string ResolveFromSlot(Entity account)
    {
        var slot = GetRequired("new_channelslot", Reference(account, "new_channelslot", "new_channelslot"), "new_tenant", "new_channel");
        if (Reference(slot, "new_tenant", "account") != account.Id) throw ScopeFailure();
        return ChannelKey(GetRequired("new_linechannel", Reference(slot, "new_channel", "new_linechannel"), "new_product", "new_channelid"));
    }

    /// <summary>明確驗產品及公開 Channel ID 格式；不接受其他產品或以外部識別值反推授權。</summary>
    private string ChannelKey(Entity channel)
    {
        var key = channel.GetAttributeValue<string>("new_channelid");
        if (channel.GetAttributeValue<string>("new_product") != product || !ValidChannel(key)) throw ScopeFailure();
        return key;
    }

    /// <summary>父租戶須同時是租戶、測試租戶且代碼相同；不是測試 marker 的資料完全不清理。</summary>
    private static void ValidateAccount(Entity account, string code)
    {
        if (account.GetAttributeValue<string>("new_tenantcode") != code ||
            account.GetAttributeValue<OptionSetValue>("new_istenant")?.Value != 100000001 ||
            account.GetAttributeValue<OptionSetValue>("new_istesttenant")?.Value != 100000001) throw ScopeFailure();
    }

    /// <summary>驗證產品的固定短代碼，避免任意路徑及用他產品代碼擴大 scope。</summary>
    private void ValidateCode(string code)
    {
        var prefix = product == "GoodShepherd2" ? 'c' : product == "GoodButler" ? 'b' : 'g';
        if (code is null || code.Length != 4 || code[0] != prefix ||
            code.Skip(1).Any(character => character is < '0' or > '9') ||
            !int.TryParse(code.AsSpan(1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number) || number is < 1 or > 299)
            throw new ArgumentException("Test tenant code is invalid.");
    }

    /// <summary>只接受 LINE 固定格式；驗證失敗不把原始使用者值放到例外。</summary>
    private static bool ValidUserId(string? user) => user is { Length: 33 } && user[0] == 'U' &&
        user.Skip(1).All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    /// <summary>公開 Channel ID 限制為十至二十個 ASCII 數字，與私人 LINE userId 分離。</summary>
    private static bool ValidChannel(string? key) => key is { Length: >= 10 and <= 20 } && key.All(character => character is >= '0' and <= '9');

    /// <summary>由本命令唯一產生並交 SDK Create 使用的正式資料庫 GUID，不接收 caller 指定 ID。</summary>
    private static Entity New(string logicalName) => new(logicalName, Guid.NewGuid());

    /// <summary>配發與釋放僅傳 F3 同步外殼允許的三個欄位；不自行改 slot 狀態或覆寫結果。</summary>
    private static Entity SlotRequest(Guid tenant, bool release) => new("new_slotrequest", Guid.NewGuid())
    {
        ["new_tenant"] = new EntityReference("account", tenant),
        ["new_operation"] = new OptionSetValue(release ? 100000001 : 100000000),
        ["new_correlationid"] = Guid.NewGuid().ToString("D")
    };

    /// <summary>有界父子查詢一次完整發布；MoreRecords、重複或空資料庫 ID 一律拒絕，不靜默去重。</summary>
    private List<Entity> Read(string logicalName, string attribute, object value, params string[] columns)
    {
        var query = new QueryExpression(logicalName) { ColumnSet = new ColumnSet(columns), TopCount = MaximumRows + 1 };
        query.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
        query.Orders.Add(new OrderExpression(logicalName + "id", OrderType.Ascending));
        return ValidateCollection(service.RetrieveMultiple(query), logicalName);
    }

    /// <summary>僅查已驗證測試角色集合的下層記錄；空父集合不發無限制查詢。</summary>
    private List<Entity> ReadByRoles(string logicalName, List<Entity> roles, params string[] columns)
    {
        if (roles.Count == 0) return [];
        var query = new QueryExpression(logicalName) { ColumnSet = new ColumnSet(columns), TopCount = MaximumRows + 1 };
        query.Criteria.AddCondition("new_roleid", ConditionOperator.In, roles.Select(role => (object)role.Id).ToArray());
        return ValidateCollection(service.RetrieveMultiple(query), logicalName);
    }

    /// <summary>同時盤點本租戶的角色及 Contact 兩個方向，否則新增的跨租戶 role 指派可能不在 manifest 而漏查。</summary>
    private List<Entity> ReadAssignments(List<Entity> roles, List<Entity> contacts)
    {
        if (roles.Count == 0 && contacts.Count == 0) return [];
        var query = new QueryExpression("new_roleassignment")
        {
            ColumnSet = new ColumnSet("new_roleid", "new_contactid"), TopCount = MaximumRows + 1,
            Criteria = new FilterExpression(LogicalOperator.Or)
        };
        if (roles.Count != 0) query.Criteria.AddCondition("new_roleid", ConditionOperator.In, roles.Select(role => (object)role.Id).ToArray());
        if (contacts.Count != 0) query.Criteria.AddCondition("new_contactid", ConditionOperator.In, contacts.Select(contact => (object)contact.Id).ToArray());
        return ValidateCollection(service.RetrieveMultiple(query), "new_roleassignment");
    }

    /// <summary>驗證回傳完整集合，不按顯示內容刪掉合法同名列。</summary>
    private static List<Entity> ValidateCollection(EntityCollection collection, string logicalName)
    {
        if (collection is null || collection.MoreRecords || collection.Entities.Count > MaximumRows ||
            collection.Entities.Any(row => row.LogicalName != logicalName || row.Id == Guid.Empty) ||
            collection.Entities.Select(row => row.Id).Distinct().Count() != collection.Entities.Count) throw ScopeFailure();
        return collection.Entities.ToList();
    }

    /// <summary>查缺只接受精確 ObjectDoesNotExist fault，其他服務與授權錯誤原樣交外層處理。</summary>
    private Entity? Get(string logicalName, Guid id, params string[] columns)
    {
        try
        {
            var row = service.Retrieve(logicalName, id, new ColumnSet(columns));
            if (row.LogicalName != logicalName || row.Id != id || id == Guid.Empty) throw ScopeFailure();
            return row;
        }
        catch (FaultException<OrganizationServiceFault> exception) when (exception.Detail.ErrorCode == -2147220969) { return null; }
    }

    /// <summary>必要資料缺席時拒絕，不對正式父子範圍做缺省推測。</summary>
    private Entity GetRequired(string logicalName, Guid id, params string[] columns) => Get(logicalName, id, columns) ?? throw ScopeFailure();

    /// <summary>正式 lookup 的邏輯實體及非空資料庫 ID 必須吻合。</summary>
    private static Guid Reference(Entity row, string attribute, string target) =>
        row.GetAttributeValue<EntityReference>(attribute) is { Id: var id, LogicalName: var logicalName } &&
        id != Guid.Empty && logicalName == target ? id : throw ScopeFailure();

    /// <summary>同一 SDK 交易創建整批列；入場前標記是否已送出，逐筆核對正式主鍵後才記錄成功，均早於返回時的 caller 取消檢查。</summary>
    /// <remarks>回呼只由本次 Create closure 擁有，既不跨操作保存也不產生背景工作；配發釋放交易沿用相同逐筆回應驗證。</remarks>
    private void ExecuteCreates(List<Entity> rows, CancellationToken ct, Action? beforeSubmit = null, Action? afterConfirmedResponse = null)
    {
        ExecuteBatch(rows.Select(row => (OrganizationRequest)new CreateRequest { Target = row }).ToArray(), ct, beforeSubmit, response =>
        {
            if (response.Responses.Count != rows.Count || response.Responses.Where((item, index) => item is not CreateResponse create || create.id != rows[index].Id).Any())
                throw ScopeFailure();
            afterConfirmedResponse?.Invoke();
        });
    }

    /// <summary>只有正式交易 fault detail 證明整批回滾；一般 OrganizationServiceFault、逾時、取消及 IOException 都保留未知結果。</summary>
    private static bool IsConfirmedRollback(Exception failure) =>
        failure is FaultException<OrganizationServiceFault> { Detail: ExecuteTransactionFault } or FaultException<ExecuteTransactionFault>;

    /// <summary>精確刪除已完整盤點的記錄 ID；不依顯示名稱、空查詢或 wildcard 執行清理。</summary>
    private void ExecuteDeletes(IReadOnlyList<Entity> rows, CancellationToken ct) => ExecuteBatch(
        rows.Select(row => (OrganizationRequest)new DeleteRequest { Target = row.ToEntityReference() }).ToArray(), ct);

    /// <summary>同步 SDK 交易有界於當次租約；送出標記緊鄰 Execute，真正返回後先驗回應及保存確定結果再檢查取消，沒有遺棄工作。</summary>
    /// <remarks>單次作業 closure 只活到同步呼叫結束；SDK fault 原樣往上傳遞，不能由查無資料或取消 token 推論回滾。</remarks>
    private ExecuteTransactionResponse ExecuteBatch(OrganizationRequest[] requests, CancellationToken ct,
        Action? beforeSubmit = null, Action<ExecuteTransactionResponse>? validateResponse = null)
    {
        if (requests.Length is < 1 or > 512) throw ScopeFailure();
        ct.ThrowIfCancellationRequested();
        var collection = new OrganizationRequestCollection();
        collection.AddRange(requests);
        beforeSubmit?.Invoke();
        var response = service.Execute(new ExecuteTransactionRequest { Requests = collection, ReturnResponses = true });
        var transactionResponse = response as ExecuteTransactionResponse ?? throw ScopeFailure();
        validateResponse?.Invoke(transactionResponse);
        ct.ThrowIfCancellationRequested();
        return transactionResponse;
    }

    /// <summary>固定檔名由已驗證代碼組成，呼叫端不能傳任意路徑。</summary>
    private string ManifestPath(string code) => Path.Combine(artifactRoot, "test-tenants", code + ".json");

    /// <summary>刪前重讀並比對完整清單，避免蓋掉或刪除操作中被替換的本地證據。</summary>
    private void DeleteManifest(TestTenantManifest expected)
    {
        var current = TestTenantManifest.Load(artifactRoot, organization, expected.Code);
        if (current.Product != expected.Product || current.CreationOutcome != expected.CreationOutcome ||
            !current.Records.SequenceEqual(expected.Records)) throw ScopeFailure();
        File.Delete(ManifestPath(expected.Code));
    }

    /// <summary>拒絕訊息不含租戶名稱、LINE userId、邀請 token 或服務回應本文。</summary>
    private static InvalidDataException ScopeFailure() => new("Test tenant authoritative scope or stable identity is invalid.");
}

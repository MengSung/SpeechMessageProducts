/*
 * 檔案整體說明
 * 目的與定位：F8跨產品即時授權引擎，由組合根固定產品及組織租約；不反向依賴產品，不以角色名稱或瀏覽器自報範圍擴權。
 * 主要功能與流程：每次先驗Actor及目標同租戶，查active Contact與Account，再讀取最多256指派／角色及4096權限的完整候選；以正式ID驗重、角色聯集與Self／Group／Tenant判斷後才發布不可變決策。每次重新查詢，沒有跨請求快取。
 * 使用邊界與維護：目標須由伺服器依業務記錄或新建規則取得，本API沒有RecordId不能代替業務資料存取層。Group只接受產品權威來源並再驗完整有界樹；沒有來源就拒絕。SDK確實返回後由executor唯一歸還租約；錯誤、取消不發布部分候選，真正故障交共用診斷。傳輸逾時由既有pool設定，撤權與業務寫入的原子交易仍由資料owner負責，不能把此純決策當成交易鎖。
 */
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Access;

/// <summary>固定依賴、單次候選的權限查詢；可併行呼叫但不保留Actor、Entity或決策。</summary>
public sealed class DataverseAccessPolicy : IAccessPolicy
{
    private const int MaxRoles = 256;
    private const int MaxPermissions = 4096;
    private readonly AccessDataverseExecutor _executor;
    private readonly IAccessGroupSource? _groups;

    /// <summary>組合根提供固定產品租約與安全診斷，群組來源缺席時所有Group權限拒絕，不猜測產品schema。</summary>
    public DataverseAccessPolicy(Func<CancellationToken, IAccessDataverseLease> acquire, ExceptionDiagnostics diagnostics, string product, IAccessGroupSource? groups = null)
    {
        _executor = new AccessDataverseExecutor(acquire, diagnostics, product);
        _groups = groups;
    }

    /// <summary>每次重新驗證範圍並查角色，真正SDK故障會告警且拋出；同步SDK工作完成才返回Task。</summary>
    public Task<AccessDecision> AuthorizeAsync(Actor actor, string feature, AccessOperation operation, DataScope target, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (actor is null || target is null || actor.ContactId == Guid.Empty || actor.TenantId == Guid.Empty ||
            target.TenantId != actor.TenantId || actor.Product != _executor.Product ||
            !AccessValidation.ValidIdentifier(feature, 64) || !Enum.IsDefined(operation) ||
            target.OwnerContactId == Guid.Empty || target.GroupNodeId == Guid.Empty)
            return Task.FromResult(Denied("InvalidScope"));
        return Task.FromResult(_executor.Execute(service => Authorize(service, actor, feature, operation, target, ct), "Access.Authorize", ct));
    }

    /// <summary>只在單一租約及候選內查詢；所有資料驗證完成前不發布任何成功決策。</summary>
    private AccessDecision Authorize(IOrganizationService service, Actor actor, string feature, AccessOperation operation, DataScope target, CancellationToken ct)
    {
        if (!_executor.ValidateActor(service, actor, ct)) return Denied("InactiveActor");
        if (target.OwnerContactId is Guid owner && owner != actor.ContactId)
        {
            Entity contact = service.Retrieve("contact", owner, new ColumnSet("parentcustomerid", "statecode"));
            ct.ThrowIfCancellationRequested();
            if (contact is null || contact.LogicalName != "contact" || contact.Id != owner || !AccessDataverseExecutor.IsActive(contact) ||
                !AccessDataverseExecutor.RefMatches(contact, "parentcustomerid", "account", actor.TenantId)) return Denied("InvalidOwner");
        }
        var assignmentQuery = Query("new_roleassignment", MaxRoles, "new_contactid", "new_roleid", "statecode");
        assignmentQuery.Criteria.AddCondition("new_contactid", ConditionOperator.Equal, actor.ContactId);
        var assigned = new HashSet<Guid>();
        foreach (Entity row in Read(service, assignmentQuery, MaxRoles, ct))
        {
            if (!AccessDataverseExecutor.RefMatches(row, "new_contactid", "contact", actor.ContactId)) throw InvalidData();
            Guid roleId = Reference(row, "new_roleid", "new_role");
            if (Active(row) && !assigned.Add(roleId)) throw InvalidData();
        }
        if (assigned.Count == 0) return Denied("NoAssignment");
        var roleQuery = Query("new_role", MaxRoles, "new_tenantid", "statecode");
        roleQuery.Criteria.AddCondition("new_roleid", ConditionOperator.In, assigned.Select(id => (object)id).ToArray());
        roleQuery.Criteria.AddCondition("new_tenantid", ConditionOperator.Equal, actor.TenantId);
        var activeRoles = new HashSet<Guid>();
        foreach (Entity row in Read(service, roleQuery, MaxRoles, ct))
        {
            if (!assigned.Contains(row.Id) || !AccessDataverseExecutor.RefMatches(row, "new_tenantid", "account", actor.TenantId)) throw InvalidData();
            if (Active(row)) activeRoles.Add(row.Id);
        }
        if (activeRoles.Count == 0) return Denied("NoActiveRole");
        var permissionQuery = Query("new_rolepermission", MaxPermissions, "new_roleid", "new_feature", "new_operation", "new_scope", "statecode");
        permissionQuery.Criteria.AddCondition("new_roleid", ConditionOperator.In, activeRoles.Select(id => (object)id).ToArray());
        var scopes = new HashSet<ScopeLevel>();
        foreach (Entity row in Read(service, permissionQuery, MaxPermissions, ct))
        {
            if (!activeRoles.Contains(Reference(row, "new_roleid", "new_role"))) throw InvalidData();
            string? rowFeature = row.GetAttributeValue<string>("new_feature");
            if (!AccessValidation.ValidIdentifier(rowFeature, 64)) throw InvalidData();
            int rowOperation = Choice(row, "new_operation") - 100000000;
            int rowScope = Choice(row, "new_scope") - 100000000;
            if (rowOperation is < 0 or > 3 || rowScope is < 0 or > 2) throw InvalidData();
            if (Active(row) && rowFeature == feature && rowOperation == (int)operation) scopes.Add((ScopeLevel)rowScope);
        }
        if (scopes.Contains(ScopeLevel.Tenant)) return Granted(ScopeLevel.Tenant);
        if (scopes.Contains(ScopeLevel.Self) && target.OwnerContactId == actor.ContactId) return Granted(ScopeLevel.Self);
        if (scopes.Contains(ScopeLevel.Group) && target.GroupNodeId is Guid targetNode && InGroup(service, actor, targetNode, ct)) return Granted(ScopeLevel.Group);
        return Denied("NoPermission");
    }

    /// <summary>取得有上限的完整集合；不能把第一頁、重複ID或錯誤實體當成完整權限發布。</summary>
    private static Entity[] Read(IOrganizationService service, QueryExpression query, int maximum, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EntityCollection result = service.RetrieveMultiple(query);
        ct.ThrowIfCancellationRequested();
        if (result is null || result.MoreRecords || result.Entities.Count > maximum) throw InvalidData();
        var ids = new HashSet<Guid>();
        Entity[] rows = result.Entities.ToArray();
        foreach (Entity row in rows)
            if (row is null || row.LogicalName != query.EntityName || row.Id == Guid.Empty || !ids.Add(row.Id)) throw InvalidData();
        return rows;
    }

    /// <summary>建立小型投影及max+1哨兵，超限時拒絕而不是悄悄截斷或無界掃描。</summary>
    private static QueryExpression Query(string entity, int maximum, params string[] columns) =>
        new(entity) { ColumnSet = new ColumnSet(columns), TopCount = maximum + 1 };

    /// <summary>缺少或非SDK option的狀態屬資料故障；有效與停用是唯二接受值。</summary>
    private static bool Active(Entity row)
    {
        int state = Choice(row, "statecode");
        if (state is not (0 or 1)) throw InvalidData();
        return state == 0;
    }

    /// <summary>lookup種類及正式主鍵必須相符，不能用名稱或空ID替代。</summary>
    private static Guid Reference(Entity row, string field, string logicalName) =>
        row.Attributes.TryGetValue(field, out object? value) && value is EntityReference r && r.LogicalName == logicalName && r.Id != Guid.Empty ? r.Id : throw InvalidData();

    /// <summary>只接受SDK choice，不把bool、裸整數或字串當正式選項。</summary>
    private static int Choice(Entity row, string field) => row.Attributes.TryGetValue(field, out object? value) && value is OptionSetValue choice ? choice.Value : throw InvalidData();

    /// <summary>複製並線性驗證整張群組候選；任何缺父或循環使當次查詢故障，不能先命中子路徑就放行。</summary>
    private bool InGroup(IOrganizationService service, Actor actor, Guid targetNode, CancellationToken ct)
    {
        AccessGroupSnapshot? snapshot = _groups?.Read(service, actor, ct);
        ct.ThrowIfCancellationRequested();
        if (snapshot is null) return false;
        if (snapshot.ContactId != actor.ContactId || snapshot.TenantId != actor.TenantId || snapshot.Product != actor.Product ||
            snapshot.MemberNodeId == Guid.Empty || snapshot.Nodes is null || snapshot.Nodes.Count > MaxPermissions) throw InvalidData();
        AccessGroupNode[] nodes = snapshot.Nodes.ToArray();
        if (nodes.Length > MaxPermissions) throw InvalidData();
        var graph = new Dictionary<Guid, AccessGroupNode>(nodes.Length);
        foreach (AccessGroupNode node in nodes)
        {
            if (node is null || node.Id == Guid.Empty || node.TenantId != actor.TenantId || node.ParentId == Guid.Empty || !graph.TryAdd(node.Id, node)) throw InvalidData();
        }
        var verified = new HashSet<Guid>();
        foreach (AccessGroupNode node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            var path = new HashSet<Guid>();
            Guid? cursor = node.Id;
            while (cursor is Guid id && !verified.Contains(id))
            {
                if (!path.Add(id) || !graph.TryGetValue(id, out AccessGroupNode? current)) throw InvalidData();
                cursor = current.ParentId;
            }
            verified.UnionWith(path);
        }
        if (!graph.ContainsKey(snapshot.MemberNodeId) || !graph.ContainsKey(targetNode)) return false;
        Guid? ancestor = targetNode;
        while (ancestor is Guid id)
        {
            if (id == snapshot.MemberNodeId) return true;
            ancestor = graph[id].ParentId;
        }
        return false;
    }

    /// <summary>固定資料故障不帶欄位值或原始列；executor將真實例外交給共用安全告警。</summary>
    private static InvalidOperationException InvalidData() => new("Invalid access data.");
    /// <summary>拒絕結果沒有資料或可變參照，原因只使用編譯期固定碼。</summary>
    private static AccessDecision Denied(string code) => new(false, null, code);
    /// <summary>成功只發布最終範圍，不回傳SDK列或可供下個請求重用的權限快照。</summary>
    private static AccessDecision Granted(ScopeLevel scope) => new(true, scope, "Allowed");
}

/*
 * 檔案整體說明
 * 目的與定位：F8授權負向、併行及資源生命週期離線測試，補足需求矩陣以外的信任邊界。
 * 主要功能與流程：重用每案獨立SDK fixture，故障注入跨租戶列、選項缺損、重複ID、截斷集合與群組循環；對正常取消阻塞SDK直到確認未提前Dispose，再釋放及斷言唯一歸還。
 * 使用邊界與維護：所有資料與例外皆為合成，沒有實機連線或秘密；每輪併行任務全部await，事件及診斷由測試owner確定釋放，active lease歸零才清理。結果只能證明離線policy及executor，不宣稱產品組織樹或交易撤權已部署。
 */
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;
using Fixture = SpeechMessage.Access.Tests.AccessPolicyTests.Fixture;

namespace SpeechMessage.Access.Tests;

/// <summary>完整候選在故障時不得發布部分授權；依賴只活在每個測試內。</summary>
public sealed class AccessPolicySafetyTests
{
    /// <summary>Self只接受精確擁有者；同租戶另一人與未指定owner不能通過。</summary>
    [Theory]
    [InlineData("same", true)]
    [InlineData("other", false)]
    [InlineData("none", false)]
    public async Task Self_ExactOwnerOnly(string owner, bool allowed)
    {
        await using var f = new Fixture(); f.Grant("members", scope: ScopeLevel.Self);
        Guid? id = owner == "same" ? f.Contact : owner == "other" ? Guid.NewGuid() : null;
        Assert.Equal(allowed, (await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target with { OwnerContactId = id })).Allowed);
    }

    /// <summary>Group僅本節點及子孫；父或兄弟不因同租戶而獲准。</summary>
    [Theory]
    [InlineData("self", true)]
    [InlineData("child", true)]
    [InlineData("parent", false)]
    [InlineData("sibling", false)]
    [InlineData("unknown", false)]
    [InlineData("none", false)]
    public async Task Group_DescendantsOnly(string relation, bool allowed)
    {
        await using var f = new Fixture(); f.Grant("members", scope: ScopeLevel.Group);
        Guid grandchild = Guid.NewGuid(), sibling = Guid.NewGuid();
        f.GroupOverride = new(f.Contact, f.Tenant, "GoodShepherd2", f.Child,
            [new(f.Root, f.Tenant, null), new(f.Child, f.Tenant, f.Root), new(grandchild, f.Tenant, f.Child), new(sibling, f.Tenant, f.Root)]);
        Guid? target = relation switch { "self" => f.Child, "child" => grandchild, "parent" => f.Root, "sibling" => sibling, "none" => null, _ => Guid.NewGuid() };
        Assert.Equal(allowed, (await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target with { GroupNodeId = target })).Allowed);
    }

    /// <summary>群組沒有權威來源時拒絕，不能由target節點自行授權。</summary>
    [Fact]
    public async Task Group_NoSource_Denied()
    {
        await using var f = new Fixture(); f.Grant("members", scope: ScopeLevel.Group); f.NoGroups = true;
        Assert.False((await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target)).Allowed);
    }

    /// <summary>全樹故障必須拒絕且告警；即使目標路徑已匹配，也不能忽略其他循環或重複正式ID。</summary>
    [Theory]
    [InlineData("cycle")]
    [InlineData("missing-parent")]
    [InlineData("duplicate")]
    [InlineData("cross-tenant")]
    [InlineData("wrong-actor")]
    [InlineData("wrong-product")]
    [InlineData("too-many")]
    public async Task Group_InvalidGraph_FailsClosed(string fault)
    {
        await using var f = new Fixture(); f.Grant("members", scope: ScopeLevel.Group);
        var nodes = new List<AccessGroupNode> { new(f.Root, f.Tenant, null), new(f.Child, f.Tenant, f.Root) };
        var snapshot = new AccessGroupSnapshot(f.Contact, f.Tenant, "GoodShepherd2", f.Root, nodes);
        switch (fault)
        {
            case "cycle": nodes.Add(new(Guid.NewGuid(), f.Tenant, null)); nodes[2] = nodes[2] with { ParentId = nodes[2].Id }; break;
            case "missing-parent": nodes[1] = nodes[1] with { ParentId = Guid.NewGuid() }; break;
            case "duplicate": nodes.Add(nodes[0]); break;
            case "cross-tenant": nodes[1] = nodes[1] with { TenantId = Guid.NewGuid() }; break;
            case "wrong-actor": snapshot = snapshot with { ContactId = Guid.NewGuid() }; break;
            case "wrong-product": snapshot = snapshot with { Product = "GoodButler" }; break;
            case "too-many": for (int i = 0; i < 4096; i++) nodes.Add(new(Guid.NewGuid(), f.Tenant, null)); break;
        }
        f.GroupOverride = snapshot;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target));
        Assert.Equal(1, f.Faulted); Assert.Equal(1, f.Released);
    }

    /// <summary>錯產品、空ID、空功能與未知操作皆在取得SDK前拒絕，不允許actor選擇組織。</summary>
    [Theory]
    [InlineData("product")]
    [InlineData("contact")]
    [InlineData("tenant")]
    [InlineData("feature")]
    [InlineData("operation")]
    [InlineData("owner")]
    public async Task InvalidInput_DeniedBeforeSdk(string fault)
    {
        await using var f = new Fixture(); f.Grant("members");
        Actor actor = fault switch { "product" => f.Actor with { Product = "GoodButler" }, "contact" => f.Actor with { ContactId = Guid.Empty },
            "tenant" => f.Actor with { TenantId = Guid.Empty }, _ => f.Actor };
        var target = fault == "owner" ? f.Target with { OwnerContactId = Guid.Empty } : f.Target;
        Assert.False((await f.Policy.AuthorizeAsync(actor, fault == "feature" ? "" : "members", fault == "operation" ? (AccessOperation)99 : AccessOperation.Read, target)).Allowed);
        Assert.Equal(0, f.Acquired);
    }

    /// <summary>來源身分缺損或換租戶立即拒絕；Tenant旗標必須SDK choice，不能true或裸整數。</summary>
    [Theory]
    [InlineData("disabled-contact")]
    [InlineData("wrong-parent")]
    [InlineData("disabled-tenant")]
    [InlineData("missing-flag")]
    [InlineData("bool-flag")]
    [InlineData("integer-flag")]
    public async Task ActorAuthority_Revalidated(string fault)
    {
        await using var f = new Fixture(); f.Grant("members");
        f.RetrieveOverride = (name, id) =>
        {
            Entity row = name == "contact" ? Fixture.ContactRow(id, f.Tenant) : Fixture.TenantRow(id);
            if (name == "contact" && fault == "disabled-contact" || name == "account" && fault == "disabled-tenant") row["statecode"] = new OptionSetValue(1);
            if (name == "contact" && fault == "wrong-parent") row["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
            if (name == "account" && fault == "missing-flag") row.Attributes.Remove("new_istenant");
            if (name == "account" && fault == "bool-flag") row["new_istenant"] = true;
            if (name == "account" && fault == "integer-flag") row["new_istenant"] = 100000001;
            return row;
        };
        if (fault is "bool-flag" or "integer-flag")
            await Assert.ThrowsAsync<InvalidCastException>(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target));
        else Assert.False((await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target)).Allowed);
        Assert.Equal(1, f.Released);
    }

    /// <summary>任一消費集合重複非空主鍵都拒絕，不以Distinct或字典覆寫掩蓋。</summary>
    [Theory]
    [InlineData("assignment")]
    [InlineData("role")]
    [InlineData("permission")]
    public async Task DuplicateId_RejectsCandidate(string kind)
    {
        await using var f = new Fixture(); f.Grant("members");
        var rows = kind == "assignment" ? f.Assignments : kind == "role" ? f.Roles : f.Permissions;
        rows.Add(Fixture.Clone(rows[0]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target));
        Assert.Equal(1, f.Faulted); Assert.Equal(1, f.Released);
    }

    /// <summary>回應跨租戶角色、未知role或不完整選項均為資料故障，不可部分允許。</summary>
    [Theory]
    [InlineData("role-tenant")]
    [InlineData("assignment-contact")]
    [InlineData("permission-role")]
    [InlineData("permission-scope")]
    [InlineData("permission-operation")]
    [InlineData("missing-state")]
    public async Task MalformedSource_FailsClosed(string fault)
    {
        await using var f = new Fixture(); f.Grant("members");
        switch (fault)
        {
            case "role-tenant": f.Roles[0]["new_tenantid"] = new EntityReference("account", Guid.NewGuid()); break;
            case "assignment-contact": f.Assignments[0]["new_contactid"] = new EntityReference("contact", Guid.NewGuid()); break;
            case "permission-role": f.Permissions[0]["new_roleid"] = new EntityReference("new_role", Guid.NewGuid()); break;
            case "permission-scope": f.Permissions[0]["new_scope"] = new OptionSetValue(99); break;
            case "permission-operation": f.Permissions[0]["new_operation"] = new OptionSetValue(99); break;
            case "missing-state": f.Permissions[0].Attributes.Remove("statecode"); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target));
        Assert.Equal(1, f.Faulted); Assert.Equal(1, f.Released);
    }

    /// <summary>截斷或超限集合不能當完整授權資料，查詢投影與scope filter同時由測試核對。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundedQuery_TruncationRejected(bool excessive)
    {
        await using var f = new Fixture(); f.Grant("members");
        f.QueryOverride = q =>
        {
            Assert.False(q.ColumnSet.AllColumns);
            Assert.InRange(q.TopCount ?? 0, 2, 4097);
            if (q.EntityName == "new_roleassignment")
            {
                var condition = Assert.Single(q.Criteria.Conditions);
                Assert.Equal("new_contactid", condition.AttributeName);
                Assert.Equal(f.Contact, Assert.Single(condition.Values));
                if (excessive) return new EntityCollection(Enumerable.Range(0, 257).Select(_ => new Entity("new_roleassignment", Guid.NewGuid())).ToList());
                return new EntityCollection(f.Assignments) { MoreRecords = true };
            }
            throw new InvalidOperationException("Query should have been rejected.");
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target));
        Assert.Equal(1, f.Faulted);
    }

    /// <summary>故障後不發布部分授權；下一請求可重查完整資料，不殘留失敗快取。</summary>
    [Fact]
    public async Task LoaderFailure_NextRequestRetriesWithoutPartialPublication()
    {
        await using var f = new Fixture(); f.Grant("members");
        var error = new InvalidOperationException("Synthetic canary failure.");
        f.BeforeQuery = () => throw error;
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target)));
        f.BeforeQuery = null;
        Assert.True((await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target)).Allowed);
        Assert.Equal(2, f.Released); Assert.Equal(1, f.Faulted);
    }

    /// <summary>取消發生在同步SDK內時，返回前不可歸還租約；解除barrier後才恰好一次fault及Dispose。</summary>
    [Fact]
    public async Task CallerCancelled_DrainsSdkBeforeRelease()
    {
        await using var f = new Fixture(); f.Grant("members");
        using var started = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        f.BeforeQuery = () => { started.Set(); if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic barrier."); };
        Task<AccessDecision> pending = Task.Run(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target, cancellation.Token));
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            cancellation.Cancel();
            Assert.False(pending.IsCompleted); Assert.Equal(0, f.Released);
        }
        finally { finish.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, f.Released); Assert.Equal(1, f.Faulted);
    }

    /// <summary>兩主體三輪併行各32請求，A只有讀B只有改；結果不得交叉、候選或lease不得殘留。</summary>
    [Fact]
    public async Task ConcurrentAB_IsolatedAndDrained()
    {
        await using var a = new Fixture(); await using var b = new Fixture();
        a.Grant("members"); b.Grant("members", AccessOperation.Update);
        for (int round = 0; round < 3; round++)
        {
            await Task.WhenAll(Enumerable.Range(0, 32).Select(async i =>
            {
                Fixture f = i % 2 == 0 ? a : b;
                var result = await Task.Run(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target));
                Assert.Equal(i % 2 == 0, result.Allowed);
                Assert.False((await f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target with { TenantId = i % 2 == 0 ? b.Tenant : a.Tenant })).Allowed);
            }));
        }
        Assert.Equal(48, a.Acquired); Assert.Equal(48, b.Acquired);
        Assert.Equal(a.Acquired, a.Released); Assert.Equal(b.Acquired, b.Released);
        Assert.Equal(0, a.Faulted); Assert.Equal(0, b.Faulted);
    }

    /// <summary>最後管理員helper拒絕跨租戶、重複ID、偽造欲移除列及來源缺列，不宣稱具有原子刪除保證。</summary>
    [Fact]
    public void RoleAssignment_CorruptSnapshotCannotAuthorizeRemoval()
    {
        var a = new RoleAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true, true);
        var b = a with { AssignmentId = Guid.NewGuid(), ContactId = Guid.NewGuid() };
        Assert.True(RoleAssignmentRule.CanRemoveAssignment([a, b], a));
        Assert.False(RoleAssignmentRule.CanRemoveAssignment([a, b, b], a));
        Assert.False(RoleAssignmentRule.CanRemoveAssignment([a, b with { TenantId = Guid.NewGuid() }], a));
        Assert.False(RoleAssignmentRule.CanRemoveAssignment([a, b], a with { ContactId = Guid.NewGuid() }));
        Assert.False(RoleAssignmentRule.CanRemoveAssignment([b], a));
    }
    /// <summary>已要求讀出的必備metadata缺損不是一般業務拒絕，必須保留故障及安全告警。</summary>
    [Theory]
    [InlineData("contact-state")]
    [InlineData("tenant-state")]
    [InlineData("tenant-choice")]
    public async Task ActorMalformedMetadata_IsFaultNotSilentDenial(string fault)
    {
        await using var f = new Fixture(); f.Grant("members");
        f.RetrieveOverride = (name, id) =>
        {
            Entity row = name == "contact" ? Fixture.ContactRow(id, f.Tenant) : Fixture.TenantRow(id);
            if (name == "contact" && fault == "contact-state" || name == "account" && fault == "tenant-state") row.Attributes.Remove("statecode");
            if (name == "account" && fault == "tenant-choice") row["new_istenant"] = new OptionSetValue(99999);
            return row;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target));
        Assert.Equal(1, f.Faulted); Assert.Equal(1, f.Released);
        Assert.Contains("InvalidOperationException", f.DiagnosticsText, StringComparison.Ordinal);
    }

    /// <summary>caller未取消的內部OCE必須告警，fault與Dispose的雙重故障不能取代主要例外。</summary>
    [Fact]
    public async Task InternalCancellationAndCleanupFaults_PreservePrimaryAndDrain()
    {
        await using var f = new Fixture(); f.Grant("members");
        var error = new OperationCanceledException("syntheticSensitiveCanary");
        f.BeforeQuery = () => throw error;
        f.FaultError = new InvalidOperationException("syntheticFaultCanary");
        f.ReleaseError = new IOException("syntheticDisposeCanary");
        Assert.Same(error, await Assert.ThrowsAsync<OperationCanceledException>(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target)));
        Assert.Equal(1, f.Faulted); Assert.Equal(1, f.Released);
        Assert.Contains("OperationCanceledException", f.DiagnosticsText, StringComparison.Ordinal);
        Assert.DoesNotContain("Canary", f.DiagnosticsText, StringComparison.Ordinal);
        Assert.Equal(3, f.DiagnosticsText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    /// <summary>成功SDK但歸還故障仍不可宣稱成功；確定執行過歸還並回傳真正清理失敗。</summary>
    [Fact]
    public async Task SuccessfulSdk_ReleaseFailureIsNotSuccess()
    {
        await using var f = new Fixture(); f.Grant("members");
        var error = new IOException("syntheticSensitiveCanary"); f.ReleaseError = error;
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target)));
        Assert.Equal(1, f.Released);
        Assert.Contains("IOException", f.DiagnosticsText, StringComparison.Ordinal);
        Assert.DoesNotContain("syntheticSensitiveCanary", f.DiagnosticsText, StringComparison.Ordinal);
    }

    /// <summary>主SDK錯誤不能被fault／Dispose錯誤替代；兩個cleanup都要嘗試並歸還計數。</summary>
    [Fact]
    public async Task PrimaryFailure_CleanupFailuresDoNotMaskOrSkipRelease()
    {
        await using var f = new Fixture(); f.Grant("members");
        var primary = new InvalidOperationException("syntheticPrimaryCanary");
        f.BeforeQuery = () => throw primary;
        f.FaultError = new InvalidOperationException("syntheticFaultCanary");
        f.ReleaseError = new IOException("syntheticDisposeCanary");
        Assert.Same(primary, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Policy.AuthorizeAsync(f.Actor, "members", AccessOperation.Read, f.Target)));
        Assert.Equal(1, f.Faulted); Assert.Equal(1, f.Released);
        Assert.Contains("InvalidOperationException", f.DiagnosticsText, StringComparison.Ordinal);
        Assert.DoesNotContain("Canary", f.DiagnosticsText, StringComparison.Ordinal);
    }

}

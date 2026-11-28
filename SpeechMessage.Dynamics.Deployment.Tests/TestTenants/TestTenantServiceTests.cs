/*
檔案整體說明
目的與定位：保護 F12 建立、名額錯配復原及清理的測試租戶隔離契約。
主要功能與流程：使用 operation-local 記憶體 SDK 模擬資料庫主鍵、正式 metadata、同步配發結果、交易回復及可控延遲提交；真服務負責選項驗證、manifest 發布及遠端父子範圍判斷，fake 不代做這些規則。
資源擁有權與隔離：所有 GUID、LINE 格式字串及路徑皆為合成；每個 fixture 唯一擁有暫存目錄與記憶體列，Dispose 僅刪自身目錄，不連線 D365、SQL 或 LINE。
故障與斷言：注入 Channel 錯配、正式組織、他租戶清單、LINE 故障、明確交易回滾、提交前取消及逾時後延遲提交；斷言不擴大清理、錯配精確釋放、無個資 manifest，且失敗保留可續跑證據。通過只表示離線契約成立。
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
using SpeechMessage.Dynamics.Deployment.TestTenants;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.TestTenants;

/// <summary>以合成 SDK 驗證 F12 交易及範圍，不宣稱任何實機資料已建立。</summary>
public sealed class TestTenantServiceTests
{
    private const string User = "U11111111111111111111111111111111";

    /// <summary>正式及錯誤產品在任何 SDK 呼叫前拒絕，不用名稱相似或未知產品後備。</summary>
    [Theory]
    [InlineData("goodshepherdback", "GoodShepherd2")]
    [InlineData("goodshepherd20", "GoodButler")]
    public void Create_ProductionOrganization_Refused(string organization, string product)
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => new TestTenantService(fixture.Sdk, organization, product, fixture.Root));
        Assert.Empty(fixture.Sdk.Rows);
    }

    /// <summary>真服務建立兩個 choice 旗標、正式試用選項及角色；清單只記錄資料庫身份。</summary>
    [Fact]
    public void Create_OutputContainsNoLineUserId()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Service.Create("c201", "測試甲", "2012804957", [User], [], default);
        var account = fixture.Sdk.Rows[("account", manifest.TenantId)];
        Assert.Equal(100000001, account.GetAttributeValue<OptionSetValue>("new_istenant").Value);
        Assert.Equal(100000001, account.GetAttributeValue<OptionSetValue>("new_istesttenant").Value);
        Assert.Equal(912, account.GetAttributeValue<OptionSetValue>("new_lifecyclestatus").Value);
        Assert.DoesNotContain(User, File.ReadAllText(fixture.Path), StringComparison.Ordinal);
        Assert.Single(fixture.Sdk.Rows.Values.Where(row => row.LogicalName == "contact"));
        Assert.Equal(2, fixture.Sdk.Rows.Values.Count(row => row.LogicalName == "new_roleassignment"));
        Assert.All(manifest.Records, record => Assert.True(fixture.Sdk.Rows.ContainsKey((record.EntityLogicalName, record.Id))));
    }

    /// <summary>配到非預期 Channel 必須走正式釋放申請，刪除本次租戶，且回報失敗。</summary>
    [Fact]
    public void Create_ExpectChannelMismatch_ReleasedAndFails()
    {
        using var fixture = new Fixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Service.Create("c201", "測試甲", "2012804966", [], [], default));
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
        Assert.DoesNotContain(fixture.Sdk.Rows.Values, row => row.LogicalName == "account");
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>正式配發器回待配發且完全沒有 slot／Channel 時，建立失敗仍須清除本次列，不能留下一個永遠無法解析 Channel 的清單。</summary>
    [Fact]
    public void Create_NoCapacity_RemovesUnallocatedTenantWithoutCallingLineOrRelease()
    {
        using var fixture = new Fixture();
        fixture.Sdk.RefuseAllocation = true;
        Assert.Throws<InvalidOperationException>(() => fixture.Service.Create("c201", "測試甲", null, [], [User], default));
        Assert.Empty(fixture.Sdk.Rows);
        Assert.False(File.Exists(fixture.Path));
        Assert.Equal(0, fixture.Sdk.ReleaseCount);
    }

    /// <summary>缺 manifest 時不得呼叫 LINE 或任意 SDK 刪除，不能以租戶代碼直接廣域清理。</summary>
    [Fact]
    public async Task Cleanup_WithoutManifest_Refused()
    {
        using var fixture = new Fixture();
        var calls = 0;
        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.Service.CleanupAsync("c201",
            (_, _, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
    }

    /// <summary>完整建立後精確清理，無關同名租戶保留，名額回到可用基線。</summary>
    [Fact]
    public async Task CreateThenCleanup_OnlyOwnTenantAndReturnsToBaseline()
    {
        using var fixture = new Fixture();
        var other = new Entity("account", Guid.NewGuid()) { ["name"] = "測試甲", ["new_tenantcode"] = "c202" };
        fixture.Sdk.Rows.Add((other.LogicalName, other.Id), other);
        fixture.Service.Create("c201", "測試甲", null, [], [User], default);
        var calls = 0;
        await fixture.Service.CleanupAsync("c201", (channel, users, _) =>
        {
            Assert.Equal("2012804957", channel);
            Assert.Equal(User, Assert.Single(users));
            calls++;
            return Task.CompletedTask;
        }, default);
        Assert.Equal(1, calls);
        Assert.Same(other, fixture.Sdk.Rows[("account", other.Id)]);
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
        Assert.False(File.Exists(fixture.Path));
        Assert.Empty(fixture.Sdk.Rows.Values.Where(row => row.LogicalName is "contact" or "new_role" or "new_roleassignment" or "new_slotrequest"));
    }

    /// <summary>LINE 真故障不繼續刪除 CRM 或釋放 Channel，清單保留供精確重跑。</summary>
    [Fact]
    public async Task Cleanup_LineFailure_PreservesManifestAndTenant()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Service.Create("c201", "測試甲", null, [], [], default);
        var failure = new IOException("synthetic line fault");
        var actual = await Assert.ThrowsAsync<IOException>(() => fixture.Service.CleanupAsync("c201", (_, _, _) => Task.FromException(failure), default));
        Assert.Same(failure, actual);
        Assert.True(fixture.Sdk.Rows.ContainsKey(("account", manifest.TenantId)));
        Assert.True(File.Exists(fixture.Path));
        Assert.Equal(0, fixture.Sdk.ReleaseCount);
    }

    /// <summary>清單列被改指其他租戶時整批拒絕，LINE 也不得先產生副作用。</summary>
    [Fact]
    public async Task Cleanup_ForeignParent_RejectsBeforeAnyMutation()
    {
        using var fixture = new Fixture();
        fixture.Service.Create("c201", "測試甲", null, [], [User], default);
        fixture.Sdk.Rows.Values.Single(row => row.LogicalName == "contact")["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
        var calls = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.CleanupAsync("c201", (_, _, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
    }

    /// <summary>角色指派即使指向本租戶 Contact，只要 role 屬他租戶就必須整批拒絕，不能漏查後依 cascade 刪除。</summary>
    [Fact]
    public async Task Cleanup_ContactAssignmentHasForeignRole_RefusedBeforeLine()
    {
        using var fixture = new Fixture();
        fixture.Service.Create("c201", "測試甲", null, [], [User], default);
        var contact = fixture.Sdk.Rows.Values.Single(row => row.LogicalName == "contact");
        var foreign = new Entity("new_roleassignment", Guid.NewGuid())
        { ["new_contactid"] = contact.ToEntityReference(), ["new_roleid"] = new EntityReference("new_role", Guid.NewGuid()) };
        fixture.Sdk.Rows.Add((foreign.LogicalName, foreign.Id), foreign);
        var calls = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.CleanupAsync("c201", (_, _, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
    }

    /// <summary>同步交易在中途失敗時由伺服器原子回滾，復原必讀回所有預先記錄 ID 均不存在才移除清單。</summary>
    [Fact]
    public void Create_TransactionFailure_RollsBackAndRemovesOnlyEmptyManifest()
    {
        using var fixture = new Fixture();
        fixture.Sdk.FailCreateAt = 2;
        Assert.Same(fixture.Sdk.TransactionFailure, Assert.Throws<FaultException<OrganizationServiceFault>>(
            () => fixture.Service.Create("c201", "測試甲", null, [], [User], default)));
        Assert.Empty(fixture.Sdk.Rows);
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>已確認建立且遠端清理完成，本地 manifest 刪除失敗後可精確續跑；唯有完整正式 ID 全不存在才移除清單。</summary>
    [Fact]
    public async Task Cleanup_ConfirmedCreationAlreadyDeleted_RemovesRetainedManifest()
    {
        using var fixture = new Fixture();
        fixture.Service.Create("c201", "測試甲", null, [], [User], default);
        var retained = File.ReadAllBytes(fixture.Path);
        await fixture.Service.CleanupAsync("c201", static (_, _, _) => Task.CompletedTask, default);
        File.WriteAllBytes(fixture.Path, retained);
        await fixture.Service.CleanupAsync("c201", static (_, _, _) => throw new InvalidOperationException("LINE must not be called after confirmed cleanup."), default);
        Assert.False(File.Exists(fixture.Path));
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
    }

    /// <summary>已提交但回應遺失仍屬未知結果；建立不得立即清理，另一服務明確清理時才依持久化正式 ID 釋放。</summary>
    [Fact]
    public async Task Create_ResponseLostAfterCommit_RetainsManifestUntilExplicitCleanup()
    {
        using var fixture = new Fixture();
        fixture.Sdk.LoseNextCommitResponse = true;
        Assert.Throws<IOException>(() => fixture.Service.Create("c201", "測試甲", null, [], [User], default));
        var persisted = TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201");
        Assert.All(persisted.Records, record => Assert.True(fixture.Sdk.Rows.ContainsKey((record.EntityLogicalName, record.Id))));
        Assert.Equal(0, fixture.Sdk.ReleaseCount);
        Assert.Equal(0, fixture.Sdk.DeleteCount);

        await fixture.Service.CleanupAsync("c201", (_, _, _) => Task.CompletedTask, default);

        Assert.All(persisted.Records, record => Assert.False(fixture.Sdk.Rows.ContainsKey((record.EntityLogicalName, record.Id))));
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>伺服器尚未提交便逾時時保留同一原始例外與全部正式 ID；以同步閘門延遲交易，沒有遺留背景工作。</summary>
    [Fact]
    public void Create_TimeoutBeforeLateCommit_PreservesOriginalFailureAndManifest()
    {
        using var fixture = new Fixture();
        var failure = new TimeoutException("synthetic timeout before late commit");
        fixture.Sdk.NextSubmissionFailure = failure;

        Assert.Same(failure, Assert.Throws<TimeoutException>(
            () => fixture.Service.Create("c201", "測試甲", null, [], [User], default)));

        var persisted = TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201");
        var submitted = Assert.IsType<ExecuteTransactionRequest>(fixture.Sdk.DeferredTransaction).Requests
            .Cast<CreateRequest>().Select(request => (request.Target.LogicalName, request.Target.Id)).ToHashSet();
        Assert.Equal(6, persisted.Records.Count);
        Assert.Equal(submitted.Count, persisted.Records.Count);
        Assert.All(persisted.Records, record => Assert.Contains((record.EntityLogicalName, record.Id), submitted));
        Assert.Empty(fixture.Sdk.Rows);
        Assert.Equal(0, fixture.Sdk.ReleaseCount);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
    }

    /// <summary>即使程序重啟且所有正式 ID 目前不可見，未知清單仍不可視為空交易刪除；斷言零 LINE／SDK 變更及原檔保留。</summary>
    [Fact]
    public async Task Cleanup_UnknownCreationStillInvisible_RetainsManifestAcrossServiceInstances()
    {
        using var fixture = new Fixture();
        TestTenantManifest.Create("goodshepherd20", "GoodShepherd2", "c201", [
            new TestTenantRecordId("account", Guid.Parse("20100000-0000-0000-0000-000000000001")),
            new TestTenantRecordId("new_slotrequest", Guid.Parse("20100000-0000-0000-0000-000000000002"))
        ]).Save(fixture.Root);
        var before = File.ReadAllBytes(fixture.Path);
        var lineCalls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CleanupAsync("c201",
            (_, _, _) => { lineCalls++; return Task.CompletedTask; }, default));

        Assert.Equal(before, File.ReadAllBytes(fixture.Path));
        Assert.Equal(0, lineCalls);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
        Assert.Equal(0, fixture.Sdk.ReleaseCount);
    }

    /// <summary>逾時後由測試閘門真正提交原交易，再以新服務載入磁碟清單精確清理；同名不同 ID 租戶保留且名額歸零。</summary>
    [Fact]
    public async Task Cleanup_LateCommitAfterTimeout_ResumesWithExactPersistedIds()
    {
        using var fixture = new Fixture();
        fixture.Sdk.NextSubmissionFailure = new TimeoutException("synthetic delayed server commit");
        Assert.Throws<TimeoutException>(() => fixture.Service.Create("c201", "測試甲", null, [], [User], default));
        var persisted = TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201");
        var before = File.ReadAllBytes(fixture.Path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CleanupAsync("c201",
            (_, _, _) => throw new InvalidOperationException("Invisible tenant cannot call LINE"), default));
        Assert.Equal(before, File.ReadAllBytes(fixture.Path));
        fixture.Sdk.CompleteDeferredCommit();
        var other = new Entity("account", Guid.NewGuid()) { ["name"] = "測試甲", ["new_tenantcode"] = "c202" };
        fixture.Sdk.Rows.Add((other.LogicalName, other.Id), other);
        Assert.All(persisted.Records, record => Assert.True(fixture.Sdk.Rows.ContainsKey((record.EntityLogicalName, record.Id))));
        var lineCalls = 0;

        await fixture.Service.CleanupAsync("c201", (channel, users, _) =>
        {
            Assert.Equal("2012804957", channel);
            Assert.Equal(User, Assert.Single(users));
            lineCalls++;
            return Task.CompletedTask;
        }, default);

        Assert.Equal(1, lineCalls);
        Assert.All(persisted.Records, record => Assert.False(fixture.Sdk.Rows.ContainsKey((record.EntityLogicalName, record.Id))));
        Assert.Same(other, fixture.Sdk.Rows[("account", other.Id)]);
        Assert.Null(fixture.Sdk.DeferredTransaction);
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>一般 SDK fault 不是 ExecuteTransactionFault，不能證明已回滾；保留同一例外及清單，不立即清理或釋放。</summary>
    [Fact]
    public void Create_GenericSdkFault_DoesNotCertifyRollback()
    {
        using var fixture = new Fixture();
        var failure = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220891 });
        fixture.Sdk.NextSubmissionFailure = failure;

        Assert.Same(failure, Assert.Throws<FaultException<OrganizationServiceFault>>(
            () => fixture.Service.Create("c201", "測試甲", null, [], [], default)));

        Assert.True(File.Exists(fixture.Path));
        Assert.Equal(4, TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201").Records.Count);
        Assert.Empty(fixture.Sdk.Rows);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
        Assert.Equal(0, fixture.Sdk.ReleaseCount);
    }

    /// <summary>程序重啟後只接受已落盤的未送出或正式回滾證據執行空清理，確認零 LINE、零遠端刪除及精確移除清單。</summary>
    [Theory]
    [InlineData("notSubmitted")]
    [InlineData("rolledBack")]
    public async Task Cleanup_ProvenNoCommitAcrossServiceInstances_RemovesOnlyEmptyManifest(string outcome)
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fixture.Path)!);
        File.WriteAllText(fixture.Path, $$"""
            {"version":2,"organization":"goodshepherd20","product":"GoodShepherd2","code":"c201","creationOutcome":"{{outcome}}","records":[
            {"entity":"account","id":"20100000-0000-0000-0000-000000000001"},
            {"entity":"new_slotrequest","id":"20100000-0000-0000-0000-000000000002"}]}
            """);

        await fixture.Service.CleanupAsync("c201", (_, _, _) => throw new InvalidOperationException("Empty cleanup cannot call LINE"), default);

        Assert.False(File.Exists(fixture.Path));
        Assert.Empty(fixture.Sdk.Rows);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
        Assert.Equal(0, fixture.Sdk.ReleaseCount);
    }

    /// <summary>呼叫端在送出前已取消時不保存清單也不送 SDK 交易；取消來源維持原 token，沒有不確定寫入。</summary>
    [Fact]
    public void Create_PreSubmitCancellation_DoesNotSendOrLeaveManifest()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = Assert.Throws<OperationCanceledException>(() => fixture.Service.Create(
            "c201", "測試甲", null, [], [], cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Empty(fixture.Sdk.Rows);
        Assert.Null(fixture.Sdk.DeferredTransaction);
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>同步提交有完整回應後 caller 才取消，應先記錄已確認結果再精確復原；不能誤當未知而留下租戶。</summary>
    [Fact]
    public void Create_CancellationAfterConfirmedCommit_RecoversBeforeRethrowing()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Sdk.AfterCommit = () => cancellation.Cancel();

        var failure = Assert.Throws<OperationCanceledException>(() => fixture.Service.Create(
            "c201", "測試甲", null, [], [User], cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.DoesNotContain(fixture.Sdk.Rows.Values, row => row.LogicalName is "account" or "contact" or "new_role" or "new_roleassignment" or "new_slotrequest");
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>明確 SDK 交易回滾後讀回故障必須聚合原 fault 與清理故障；重啟服務再清理只依落盤回滾證據移除空清單。</summary>
    [Fact]
    public async Task Create_KnownRollbackCleanupFailure_PreservesBothFailuresAndDurableRecovery()
    {
        using var fixture = new Fixture();
        fixture.Sdk.FailCreateAt = 2;
        var cleanupFailure = new IOException("synthetic recovery read failure");
        fixture.Sdk.AfterRollback = () => fixture.Sdk.RetrieveFailure = cleanupFailure;

        var failure = Assert.Throws<AggregateException>(() => fixture.Service.Create(
            "c201", "測試甲", null, [], [User], default));

        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Same(fixture.Sdk.TransactionFailure, failure.InnerExceptions[0]);
        Assert.Same(cleanupFailure, failure.InnerExceptions[1]);
        Assert.Empty(fixture.Sdk.Rows);
        Assert.True(File.Exists(fixture.Path));
        Assert.Equal(TestTenantCreationOutcome.RolledBack,
            TestTenantManifest.Load(fixture.Root, "goodshepherd20", "c201").CreationOutcome);
        fixture.Sdk.RetrieveFailure = null;
        await fixture.Service.CleanupAsync("c201", (_, _, _) => throw new InvalidOperationException("LINE must not be used"), default);
        Assert.False(File.Exists(fixture.Path));
        Assert.Equal(0, fixture.Sdk.DeleteCount);
        Assert.Equal(0, fixture.Sdk.ReleaseCount);
    }

    /// <summary>無指定期望值時保留 F3 的實際配發結果，由命令白名單核對，不能替呼叫端改成第一個 Channel。</summary>
    [Fact]
    public void Create_ChannelValidationUsesAllocatedIdentityAndRecoversRejectedChannel()
    {
        using var fixture = new Fixture();
        string? observed = null;
        fixture.Service.Create("c201", "測試甲", null, [], [], default, channel => observed = channel);
        Assert.Equal("2012804957", observed);
        Assert.Equal(observed, fixture.Service.ReadChannelKey("c201", default));
    }

    /// <summary>配發結果不在命令核定清單時，原始驗證錯誤須保留並精確釋放本次租戶。</summary>
    [Fact]
    public void Create_UnapprovedAllocatedChannel_RecoversBeforeFailure()
    {
        using var fixture = new Fixture();
        var failure = new InvalidDataException("synthetic channel refused");
        Assert.Same(failure, Assert.Throws<InvalidDataException>(() => fixture.Service.Create(
            "c201", "測試甲", null, [], [], default, _ => throw failure)));
        Assert.False(File.Exists(fixture.Path));
        Assert.Equal(1, fixture.Sdk.ReleaseCount);
    }

    /// <summary>撤銷未用邀請後須重新盤點並清除已完成加入的會友；LINE 僅收到第二份完整快照的使用者。</summary>
    [Fact]
    public async Task Cleanup_RevokesInviteAndRefreshesJoinedContactBeforeLine()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Service.Create("c201", "測試甲", null, [], [], default);
        var invite = new Entity("new_testinvite", Guid.NewGuid())
        { RowVersion = "1", ["new_tenant"] = new EntityReference("account", manifest.TenantId) };
        fixture.Sdk.Rows.Add((invite.LogicalName, invite.Id), invite);
        fixture.Sdk.AfterUpdate = _ =>
        {
            var joined = new Entity("contact", Guid.NewGuid())
            { ["parentcustomerid"] = new EntityReference("account", manifest.TenantId), ["new_lineid"] = User };
            fixture.Sdk.Rows.Add((joined.LogicalName, joined.Id), joined);
        };
        var calls = 0;
        await fixture.Service.CleanupAsync("c201", (_, users, _) =>
        {
            calls++;
            Assert.Equal(User, Assert.Single(users));
            Assert.NotNull(invite.GetAttributeValue<DateTime?>("new_usedon"));
            return Task.CompletedTask;
        }, default);
        Assert.Equal(1, calls);
        Assert.Equal(1, fixture.Sdk.UpdateCount);
        Assert.False(File.Exists(fixture.Path));
        Assert.DoesNotContain(fixture.Sdk.Rows.Values, row => row.LogicalName is "contact" or "new_testinvite");
    }

    /// <summary>第二輪仍有新未用邀請時有界停止；不能無限重試，也不能先呼叫 LINE 或刪資料。</summary>
    [Fact]
    public async Task Cleanup_ContinuouslyChangingInvites_StopsAfterOneRevocation()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Service.Create("c201", "測試甲", null, [], [], default);
        var invite = new Entity("new_testinvite", Guid.NewGuid())
        { RowVersion = "1", ["new_tenant"] = new EntityReference("account", manifest.TenantId) };
        fixture.Sdk.Rows.Add((invite.LogicalName, invite.Id), invite);
        fixture.Sdk.AfterUpdate = _ =>
        {
            var added = new Entity("new_testinvite", Guid.NewGuid())
            { RowVersion = "1", ["new_tenant"] = new EntityReference("account", manifest.TenantId) };
            fixture.Sdk.Rows.Add((added.LogicalName, added.Id), added);
        };
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CleanupAsync("c201",
            (_, _, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, calls);
        Assert.Equal(1, fixture.Sdk.UpdateCount);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
        Assert.True(File.Exists(fixture.Path));
    }

    /// <summary>邀請缺少版本時必須在任何更新或 LINE 副作用前拒絕，不能退回無條件標記已用。</summary>
    [Fact]
    public async Task Cleanup_InviteMissingRowVersion_RefusedBeforeMutation()
    {
        using var fixture = new Fixture();
        var manifest = fixture.Service.Create("c201", "測試甲", null, [], [], default);
        var invite = new Entity("new_testinvite", Guid.NewGuid())
        { ["new_tenant"] = new EntityReference("account", manifest.TenantId) };
        fixture.Sdk.Rows.Add((invite.LogicalName, invite.Id), invite);
        var calls = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.CleanupAsync("c201",
            (_, _, _) => { calls++; return Task.CompletedTask; }, default));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Sdk.UpdateCount);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
        Assert.True(File.Exists(fixture.Path));
    }

    /// <summary>caller 在撤銷交易返回時取消，保留可續跑清單；重跑使用已用邀請，不遺棄或重做原 CAS。</summary>
    [Fact]
    public async Task Cleanup_CancellationAfterRevocation_RetainsManifestAndCanResume()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var manifest = fixture.Service.Create("c201", "測試甲", null, [], [], default);
        var invite = new Entity("new_testinvite", Guid.NewGuid())
        { RowVersion = "1", ["new_tenant"] = new EntityReference("account", manifest.TenantId) };
        fixture.Sdk.Rows.Add((invite.LogicalName, invite.Id), invite);
        fixture.Sdk.AfterUpdate = _ => cancellation.Cancel();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.CleanupAsync("c201",
            (_, _, _) => { calls++; return Task.CompletedTask; }, cancellation.Token));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Sdk.DeleteCount);
        Assert.True(File.Exists(fixture.Path));
        await fixture.Service.CleanupAsync("c201", (_, _, _) => Task.CompletedTask, default);
        Assert.Equal(1, fixture.Sdk.UpdateCount);
        Assert.False(File.Exists(fixture.Path));
    }

    /// <summary>fixture 唯一擁有記憶體 SDK 與隨機暫存根；不存在外部 client 或背景工作。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>案例獨占的隨機本地根，僅儲存合成正式 ID manifest，不使用真實個資。</summary>
        internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "f12-" + Guid.NewGuid().ToString("N"));
        /// <summary>真 manifest writer 使用的固定 c201 路徑，供案例驗證失敗保留及成功刪除。</summary>
        internal string Path => System.IO.Path.Combine(Root, "test-tenants", "c201.json");
        /// <summary>案例唯一擁有的記憶體 SDK；沒有外部連線或背景活動。</summary>
        internal MemorySdk Sdk { get; } = new();
        /// <summary>每次借用同一案例 SDK 的真服務，所有可變資料仍由本 fixture 限定。</summary>
        internal TestTenantService Service => new(Sdk, "goodshepherd20", "GoodShepherd2", Root);
        /// <summary>所有作業返回後先清空記憶體列、待提交交易與 callback 參照，再刪案例自己建立的隨機目錄。</summary>
        public void Dispose() { Sdk.Clear(); if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }

    /// <summary>僅模擬正式 SDK 的 ID、範圍查詢與同步配發；真服務仍擁有安全驗證及清理順序。</summary>
    private sealed class MemorySdk : IOrganizationService
    {
        /// <summary>依實體及資料庫主鍵保留合成正式列；同名不同 ID 不覆寫。</summary>
        internal Dictionary<(string Entity, Guid Id), Entity> Rows { get; } = new();
        /// <summary>模擬正式 F3 釋放申請次數，不能由刪除 account 直接湊出基線。</summary>
        internal int ReleaseCount { get; private set; }
        /// <summary>精確刪除次數，範圍拒絕時必須維持零。</summary>
        internal int DeleteCount { get; private set; }
        /// <summary>指定第幾次 Create 拋錯，由外層合成交易回滾，零表示不注入。</summary>
        internal int FailCreateAt { get; set; }
        /// <summary>下一筆交易已提交後遺失回應，用以驗證真服務寫前主鍵清單。</summary>
        internal bool LoseNextCommitResponse { get; set; }
        /// <summary>下一筆提交在伺服器執行前回報此原始例外；交易保留給測試明確開啟閘門，沒有背景計時或 task。</summary>
        internal Exception? NextSubmissionFailure { get; set; }
        /// <summary>尚未提交的完整原 SDK 交易，只活到案例明確提交或 fixture Dispose 清空，不跨案例保存。</summary>
        internal ExecuteTransactionRequest? DeferredTransaction { get; private set; }
        /// <summary>以正式 ExecuteTransactionFault 表示整筆已回滾，而非用傳輸 IOException 假冒已確認的伺服器結果。</summary>
        internal FaultException<OrganizationServiceFault> TransactionFailure { get; } = new(
            new ExecuteTransactionFault { ErrorCode = unchecked((int)0x80040265), FaultedRequestIndex = 1 });
        /// <summary>只對精確 Retrieve 注入原始故障，用於驗證清理失敗不覆蓋建立失敗。</summary>
        internal Exception? RetrieveFailure { get; set; }
        /// <summary>同步提交完畢但回應尚未返回時只呼叫一次，模擬 caller 取消；使用後立即解除 closure。</summary>
        internal Action? AfterCommit { get; set; }
        /// <summary>整批回滾完成後注入讀回故障，仍由真服務持有原始 SDK fault。</summary>
        internal Action? AfterRollback { get; set; }
        /// <summary>同步配發器回待配發且不建立任何 Channel／slot lookup 的容量耗盡注入。</summary>
        internal bool RefuseAllocation { get; set; }
        /// <summary>僅在已套用單筆同步更新後注入有界競賽或取消，不建立背景工作。</summary>
        internal Action<Entity>? AfterUpdate { get; set; }
        /// <summary>實際更新次數，證明撤銷沒有無界重試。</summary>
        internal int UpdateCount { get; private set; }
        private int creates;
        private readonly Guid channel = Guid.NewGuid();
        private readonly Guid slot = Guid.NewGuid();

        /// <summary>保存 caller 指定正式主鍵並模擬配發／釋放結果；不代做產品範圍驗證。</summary>
        public Guid Create(Entity entity)
        {
            creates++;
            if (creates == FailCreateAt) throw TransactionFailure;
            entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
            Rows.Add((entity.LogicalName, entity.Id), entity);
            if (entity.LogicalName == "new_slotrequest")
            {
                var account = Rows[("account", entity.GetAttributeValue<EntityReference>("new_tenant").Id)];
                var releasing = entity.GetAttributeValue<OptionSetValue>("new_operation").Value == 100000001;
                if (!releasing && RefuseAllocation)
                {
                    entity["new_result"] = new OptionSetValue(100000001);
                    return entity.Id;
                }
                entity["new_result"] = new OptionSetValue(releasing ? 100000004 : 100000000);
                entity["new_channel"] = new EntityReference("new_linechannel", channel);
                entity["new_slot"] = new EntityReference("new_channelslot", slot);
                account["new_channelslot"] = releasing ? null : new EntityReference("new_channelslot", slot);
                Rows[("new_channelslot", slot)] = new Entity("new_channelslot", slot)
                { ["new_channel"] = new EntityReference("new_linechannel", channel), ["new_tenant"] = releasing ? null : account.ToEntityReference(),
                  ["new_slotstatus"] = new OptionSetValue(releasing ? 100000000 : 100000001), ["new_usedrichmenus"] = 0 };
                Rows[("new_linechannel", channel)] = new Entity("new_linechannel", channel)
                { ["new_product"] = "GoodShepherd2", ["new_channelid"] = "2012804957" };
                if (releasing) ReleaseCount++;
            }
            return entity.Id;
        }

        /// <summary>只依精確實體與主鍵讀取；可注入原始讀回故障，不存在時回正式 ObjectDoesNotExist fault 供真服務分類。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (RetrieveFailure is not null) throw RetrieveFailure;
            return Rows.TryGetValue((entityName, id), out var row)
                ? row : throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -2147220969 });
        }
        /// <summary>解讀真服務的完整 AND／OR 條件，不用固定回傳清單掩蓋漏查跨租戶指派。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = (QueryExpression)query;
            var result = new EntityCollection();
            foreach (var row in Rows.Values.Where(row => row.LogicalName == expression.EntityName && Matches(row, expression.Criteria)))
                result.Entities.Add(row);
            return result;
        }
        /// <summary>只模擬案例使用的 Equal／In 及巢狀 AND／OR，保留 lookup 的正式 ID 比較。</summary>
        private static bool Matches(Entity row, FilterExpression filter)
        {
            var matches = filter.Conditions.Select(condition =>
            {
                var actual = condition.AttributeName.EndsWith("id", StringComparison.Ordinal) && condition.AttributeName == row.LogicalName + "id"
                ? row.Id : row.Attributes.TryGetValue(condition.AttributeName, out var value) ? value is EntityReference reference ? reference.Id : value : null;
                return condition.Operator == ConditionOperator.In ? condition.Values.Contains(actual) : Equals(actual, condition.Values.SingleOrDefault());
            }).Concat(filter.Filters.Select(child => Matches(row, child)));
            return filter.FilterOperator == LogicalOperator.Or ? matches.Any(match => match) : matches.All(match => match);
        }
        /// <summary>回已發布試用 choice 及逐項交易回應；明確子項失敗還原快照，傳輸逾時則保留完整交易供同步閘門後續提交。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveAttributeRequest)
            {
                var metadata = new PicklistAttributeMetadata
                { OptionSet = new OptionSetMetadata { Options = { new OptionMetadata(new Label("試用中", 1028), 912) } } };
                return new RetrieveAttributeResponse { Results = { ["AttributeMetadata"] = metadata } };
            }
            if (request is ExecuteTransactionRequest transaction)
            {
                if (NextSubmissionFailure is { } submissionFailure)
                {
                    NextSubmissionFailure = null;
                    DeferredTransaction = transaction;
                    throw submissionFailure;
                }
                var before = Rows.ToDictionary(pair => pair.Key, pair => Copy(pair.Value));
                var previousRelease = ReleaseCount;
                var responses = new OrganizationResponseCollection();
                try { foreach (var child in transaction.Requests) responses.Add(Execute(child)); }
                catch
                {
                    Rows.Clear();
                    foreach (var pair in before) Rows.Add(pair.Key, pair.Value);
                    ReleaseCount = previousRelease;
                    AfterRollback?.Invoke();
                    throw;
                }
                var afterCommit = AfterCommit;
                AfterCommit = null;
                afterCommit?.Invoke();
                if (LoseNextCommitResponse)
                {
                    LoseNextCommitResponse = false;
                    throw new IOException("synthetic response loss after commit");
                }
                return new ExecuteTransactionResponse { Results = { ["Responses"] = responses } };
            }
            if (request is CreateRequest create) return new CreateResponse { Results = { ["id"] = Create(create.Target) } };
            if (request is DeleteRequest delete) { Delete(delete.Target.LogicalName, delete.Target.Id); return new DeleteResponse(); }
            if (request is UpdateRequest update)
            {
                // fake 只執行 CAS，產品若退成無條件 Update，即使最後清理結果相同仍須失敗。
                Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, update.ConcurrencyBehavior);
                Assert.False(string.IsNullOrWhiteSpace(update.Target.RowVersion));
                Assert.Equal(Rows[(update.Target.LogicalName, update.Target.Id)].RowVersion, update.Target.RowVersion);
                Update(update.Target);
                return new UpdateResponse();
            }
            throw new NotSupportedException();
        }
        /// <summary>由案例同步提交先前已送出但回報逾時的原交易；提交一次即清除候選，避免睡眠與排程時序造成假紅綠。</summary>
        internal void CompleteDeferredCommit()
        {
            var transaction = DeferredTransaction ?? throw new InvalidOperationException("No delayed transaction exists.");
            DeferredTransaction = null;
            Execute(transaction);
        }

        /// <summary>fixture 結束時釋放所有合成資料、待提交交易及 callback 參照；不發送或補做任何 SDK 作業。</summary>
        internal void Clear()
        {
            Rows.Clear();
            DeferredTransaction = null;
            NextSubmissionFailure = null;
            RetrieveFailure = null;
            AfterCommit = null;
            AfterRollback = null;
            AfterUpdate = null;
        }

        /// <summary>只複製 fake 交易私有候選，不保留下一交易會原地修改的 Attribute 字典。</summary>
        private static Entity Copy(Entity row)
        {
            var copy = new Entity(row.LogicalName, row.Id) { RowVersion = row.RowVersion };
            foreach (var pair in row.Attributes) copy[pair.Key] = pair.Value;
            return copy;
        }
        /// <summary>套用本次 CAS 欄位後再觸發同步故障注入，讓真服務的第二次盤點與取消判斷可被觀察。</summary>
        public void Update(Entity entity)
        {
            foreach (var pair in entity.Attributes) Rows[(entity.LogicalName, entity.Id)][pair.Key] = pair.Value;
            UpdateCount++;
            AfterUpdate?.Invoke(entity);
        }
        /// <summary>只刪指定主鍵，不以名稱或父範圍模擬 cascade；因此遺漏的子列會留在決定性斷言中。</summary>
        public void Delete(string entityName, Guid id) { Rows.Remove((entityName, id)); DeleteCount++; }
        /// <summary>案例不接受關聯捷徑，避免假 SDK 替產品默默建立未追蹤資料。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>案例不接受解除關聯捷徑，清理必須經真服務明列正式 ID。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

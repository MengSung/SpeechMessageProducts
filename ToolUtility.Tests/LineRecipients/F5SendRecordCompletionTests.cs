/*
檔案整體說明
目的與定位：F5 終態 CAS、並行占用與取消確認的離線回歸，驗證真 adapter 而非僅測替身。
主要功能與流程：合成 SDK 注入版本衝突、遠端錯誤及 Create／Update 已回覆後取消；斷言
接受終態不被覆寫、同終態可重讀冪等、不同產品／正式 ID 不可挪用、同鍵只有一次 Acquired。
使用邊界與維護：每案 using 擁有 adapter、SDK 與 CTS；並行 32 操作在釋放前全部 await。
字典只是有界資料庫模型，不代表 D365 唯一鍵實機驗收；沒有 HTTP、秘密或長命背景工作。
*/
using System.ServiceModel;
using LineMessagingProcessor.Core.Gateway;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.LineRecipients;
using Xunit;

namespace ToolUtility.Tests.LineRecipients;

/// <summary>寫入確認後保留事實，未確認及 CAS 衝突則拒絕，不推測可重送。</summary>
public sealed class F5SendRecordCompletionTests
{
    /// <summary>先讀正式列，再使用原 RowVersion 的精確 UpdateRequest；相同終態不重寫時間或版本。</summary>
    [Fact]
    public async Task Completion_Accepted_IsCasAndIdempotent()
    {
        using var sdk = new SendRecordSdk();
        using var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        var claim = await store.ClaimBatchAsync(SendRecordSdk.Identity(), default);
        var result = new SendBatchCompletion(SendRecordOutcome.Accepted, null, 1);
        await store.CompleteBatchAsync(claim, result, default);
        await store.CompleteBatchAsync(claim, result, default);
        Assert.Equal(1, sdk.Updates);
        Assert.Equal("1", sdk.LastUpdate!.Target.RowVersion);
        Assert.Equal(claim.RecordId, sdk.LastUpdate.Target.Id);
        Assert.Equal(4, sdk.LastUpdate.Target.Attributes.Count);
        var repeated = await store.ClaimBatchAsync(claim.Identity, default);
        Assert.Equal(SendClaimDisposition.AlreadyAccepted, repeated.Disposition);
        Assert.Equal("2", repeated.RowVersion);
    }

    /// <summary>已終態不同內容、版本競爭或遠端失敗都不能以無版本 Update 重試。</summary>
    [Theory]
    [InlineData("terminal")]
    [InlineData("cas")]
    [InlineData("fault")]
    [InlineData("other-id")]
    public async Task Completion_ConflictOrFault_NeverOverwrites(string fault)
    {
        using var sdk = new SendRecordSdk();
        using var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        var claim = await store.ClaimBatchAsync(SendRecordSdk.Identity(), default);
        if (fault == "terminal")
            await store.CompleteBatchAsync(claim, new SendBatchCompletion(SendRecordOutcome.Accepted, null, 1), default);
        if (fault == "cas") sdk.UpdateFault = SendRecordSdk.VersionConflict;
        if (fault == "fault") sdk.UpdateFault = unchecked((int)0x80040216);
        if (fault == "other-id") claim = claim with { RecordId = Guid.NewGuid() };
        var error = await Record.ExceptionAsync(() => store.CompleteBatchAsync(claim,
            new SendBatchCompletion(SendRecordOutcome.Failed, "http-400", 1), default));
        Assert.NotNull(error);
        if (fault is "cas" or "fault") Assert.IsType<FaultException<OrganizationServiceFault>>(error);
        Assert.Equal(fault == "other-id" ? 0 : 1, sdk.Updates);
    }

    /// <summary>既有 Uncertain／Failed／Rejected 都不可接手發送，只有 Accepted 是確定平台接受。</summary>
    [Theory]
    [InlineData(SendRecordOutcome.Uncertain, SendClaimDisposition.Uncertain)]
    [InlineData(SendRecordOutcome.Failed, SendClaimDisposition.AlreadyFinal)]
    [InlineData(SendRecordOutcome.Rejected, SendClaimDisposition.AlreadyFinal)]
    public async Task Claim_ExistingTerminal_HasSafeDisposition(SendRecordOutcome outcome, SendClaimDisposition expected)
    {
        using var sdk = new SendRecordSdk();
        using var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        var identity = SendRecordSdk.Identity();
        if (outcome == SendRecordOutcome.Rejected)
        {
            var key = SendBatchKeys.Create(identity.Product, identity.ChannelKey, identity.TenantId, identity.CorrelationId, -1);
            identity = identity with { BatchIndex = -1, BatchKey = key, RetryKey = SendBatchKeys.RetryKey(key) };
        }
        var claim = await store.ClaimBatchAsync(identity, default);
        var code = outcome == SendRecordOutcome.Rejected ? "NonTestTenant" : outcome == SendRecordOutcome.Uncertain
            ? "transport-unconfirmed" : "http-400";
        await store.CompleteBatchAsync(claim, new SendBatchCompletion(outcome, code, outcome == SendRecordOutcome.Rejected ? 0 : 1), default);
        Assert.Equal(expected, (await store.ClaimBatchAsync(identity, default)).Disposition);
    }

    /// <summary>已確認 Create 與 Update 各在返回前取消 token，仍必須返回已確認成功的事實。</summary>
    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    public async Task Acknowledgement_LateCancellation_DoesNotHideWrite(string phase)
    {
        using var sdk = new SendRecordSdk();
        using var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        using var cancellation = new CancellationTokenSource();
        if (phase == "create") sdk.AfterCreate = cancellation.Cancel;
        var claim = await store.ClaimBatchAsync(SendRecordSdk.Identity(), cancellation.Token);
        Assert.Equal(SendClaimDisposition.Acquired, claim.Disposition);
        if (phase == "create") Assert.True(cancellation.IsCancellationRequested);
        if (phase == "update")
        {
            sdk.AfterUpdate = cancellation.Cancel;
            await store.CompleteBatchAsync(claim, new SendBatchCompletion(SendRecordOutcome.Accepted, null, 1), cancellation.Token);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(1, sdk.Updates);
        }
    }

    /// <summary>兩個 adapter 共享同一合成資料庫，32路完整排空後只有一個 Acquired，跨租戶可各自占用。</summary>
    [Fact]
    public async Task Claim_ConcurrentStoreInstances_OneOwnerAndTenantPartition()
    {
        using var sdk = new SendRecordSdk();
        using var first = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        using var second = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        var identity = SendRecordSdk.Identity();
        var tasks = Enumerable.Range(0, 32).Select(index => Task.Run(() =>
            (index % 2 == 0 ? first : second).ClaimBatchAsync(identity, default))).ToArray();
        var claims = await Task.WhenAll(tasks);
        Assert.Single(claims.Where(c => c.Disposition == SendClaimDisposition.Acquired));
        Assert.Equal(31, claims.Count(c => c.Disposition == SendClaimDisposition.Uncertain));
        var tenantB = await second.ClaimBatchAsync(SendRecordSdk.Identity(tenant: Guid.NewGuid()), default);
        Assert.Equal(SendClaimDisposition.Acquired, tenantB.Disposition);
        Assert.DoesNotContain(claims, c => c.RecordId == tenantB.RecordId);
    }

    /// <summary>產品、鍵與 retry UUID 不一致必須在 SDK Create 前拒絕，不能只信 caller 提供的 hash。</summary>
    [Theory]
    [InlineData("product")]
    [InlineData("key")]
    [InlineData("retry")]
    [InlineData("hash")]
    [InlineData("count")]
    public async Task Claim_UntrustedIdentity_FailsBeforeWrite(string fault)
    {
        using var sdk = new SendRecordSdk();
        using var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        var original = SendRecordSdk.Identity();
        var changed = fault switch
        {
            "product" => original with { Product = "GoodButler" },
            "key" => original with { BatchKey = new string('f', 64) },
            "retry" => original with { RetryKey = Guid.NewGuid() },
            "hash" => original with { OperationHash = "invalid" },
            _ => original with { RecipientCount = 501 }
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ClaimBatchAsync(changed, default));
        Assert.Equal(0, sdk.Creates);
    }

    /// <summary>資料庫已接受狀態若缺嘗試數或攜帶錯誤碼，不能被解釋成平台確定接受。</summary>
    [Theory]
    [InlineData("attempts")]
    [InlineData("errorcode")]
    public async Task Claim_CorruptTerminalFacts_RefusesAcceptance(string fault)
    {
        using var sdk = new SendRecordSdk();
        using var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        var claim = await store.ClaimBatchAsync(SendRecordSdk.Identity(), default);
        await store.CompleteBatchAsync(claim, new SendBatchCompletion(SendRecordOutcome.Accepted, null, 1), default);
        sdk.MutateRead = row =>
        {
            if (fault == "attempts") row["new_attempts"] = 0;
            else row["new_errorcode"] = "http-400";
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ClaimBatchAsync(claim.Identity, default));
        Assert.Equal(1, sdk.Updates);
    }
}

/*
檔案整體說明
目的與定位：F5 真正 Dataverse adapter 的雙目標離線原子性與隔離契約，禁止把本機鎖當實機。
主要功能與流程：用合成 SDK 驗 published Active 唯一鍵、原子 Create、完整身份讀回及
RowVersion CAS；注入未知 fault、同鍵改內容、錯正式列與後到取消，確定不自動接手或覆寫。
使用邊界與維護：每案獨佔有界 SDK 模型，using 以 adapter→SDK 順序清理；並行任務全部
await 才釋放依賴。沒有網路、秘密、手機投遞或真實組織寫入，通過只代表離線行為。
*/
using System.ServiceModel;
using LineMessagingProcessor.Core.Gateway;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using ToolUtilityNameSpace.LineRecipients;
using Xunit;

namespace ToolUtility.Tests.LineRecipients;

/// <summary>原子占用的決定性驗證，只有新建者可送，既有未完成者不能被重啟或並行操作接手。</summary>
public sealed class F5SendRecordStoreTests
{
    /// <summary>鍵必須受完整範圍分區；訊息雜湊不在事件鍵內，相同事件改內容應撞鍵。</summary>
    [Fact]
    public void Keys_CanonicalStableAndScopePartitioned()
    {
        var identity = SendRecordSdk.Identity();
        Assert.Equal(identity, SendRecordSdk.Identity());
        Assert.NotEqual(identity.BatchKey, SendRecordSdk.Identity(tenant: Guid.NewGuid()).BatchKey);
        Assert.NotEqual(identity.BatchKey, SendBatchKeys.Create("GoodButler", identity.ChannelKey, identity.TenantId, identity.CorrelationId, 0));
        Assert.NotEqual(identity.BatchKey, SendBatchKeys.Create(identity.Product, "2012804966", identity.TenantId, identity.CorrelationId, 0));
        Assert.NotEqual(identity.BatchKey, SendBatchKeys.Create(identity.Product, identity.ChannelKey, identity.TenantId, identity.CorrelationId, 1));
        Assert.NotEqual(identity.BatchKey, SendRecordSdk.Identity("event-2").BatchKey);
    }

    /// <summary>連線組織不符，即使路由產品正確也不能先查業務資料或 Create。</summary>
    [Fact]
    public void Constructor_WrongOrganization_Refuses()
    {
        using var sdk = new SendRecordSdk();
        Assert.Throws<InvalidOperationException>(() => new DataverseSendRecordStore(sdk, Guid.NewGuid(), "GoodShepherd2"));
        Assert.Equal(0, sdk.Creates);
    }

    /// <summary>能力未發布、key非Active、欄錯或非optimistic一律在占用前拒絕，不能降級本機鎖。</summary>
    [Theory]
    [InlineData("no-key")]
    [InlineData("pending-key")]
    [InlineData("wrong-key-column")]
    [InlineData("no-concurrency")]
    [InlineData("wrong-table")]
    public void Constructor_InvalidPublishedCapability_Refuses(string fault)
    {
        using var sdk = new SendRecordSdk();
        switch (fault)
        {
            case "no-key": SendRecordSdk.SetMetadata(sdk.Metadata, "Keys", Array.Empty<EntityKeyMetadata>()); break;
            case "pending-key": SendRecordSdk.SetMetadata(sdk.Metadata.Keys[0], "EntityKeyIndexStatus", EntityKeyIndexStatus.Pending); break;
            case "wrong-key-column": sdk.Metadata.Keys[0].KeyAttributes = new[] { "new_name" }; break;
            case "no-concurrency": SendRecordSdk.SetMetadata(sdk.Metadata, "IsOptimisticConcurrencyEnabled", false); break;
            case "wrong-table": sdk.Metadata.LogicalName = "account"; break;
        }
        Assert.Throws<InvalidOperationException>(() => new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2"));
        Assert.Equal(0, sdk.Creates);
    }

    /// <summary>新建回正式GUID及版本；同鍵Claimed永遠不能被當成可重入鎖。</summary>
    [Fact]
    public async Task Claim_OnlyCreateOwnerAcquired_ExistingClaimUncertain()
    {
        using var sdk = new SendRecordSdk();
        using var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        var first = await store.ClaimBatchAsync(SendRecordSdk.Identity(), default);
        var existing = await store.ClaimBatchAsync(SendRecordSdk.Identity(), default);
        Assert.Equal(SendClaimDisposition.Acquired, first.Disposition);
        Assert.NotEqual(Guid.Empty, first.RecordId);
        Assert.Equal("1", first.RowVersion);
        Assert.Equal(SendClaimDisposition.Uncertain, existing.Disposition);
        Assert.Equal(first.RecordId, existing.RecordId);
        Assert.Equal(1, sdk.Queries);
        Assert.False(sdk.LastQuery!.ColumnSet.AllColumns);
    }

    /// <summary>不可變內容任何一欄不同都不能挪用原事件；同一業務事件必須碰到同鍵。</summary>
    [Theory]
    [InlineData("payload")]
    [InlineData("operation")]
    [InlineData("recipients")]
    [InlineData("category")]
    [InlineData("batchcount")]
    [InlineData("sender")]
    public async Task Claim_SameKeyChangedImmutableFacts_Conflict(string change)
    {
        using var sdk = new SendRecordSdk();
        using var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        var original = SendRecordSdk.Identity();
        await store.ClaimBatchAsync(original, default);
        var changed = change switch
        {
            "payload" => original with { PayloadHash = new string('d', 64) },
            "operation" => original with { OperationHash = new string('d', 64) },
            "recipients" => original with { RecipientHash = new string('d', 64) },
            "category" => original with { Category = MessageCategory.Operational },
            "batchcount" => original with { BatchCount = 2 },
            _ => original with { SenderNote = "sender-name-contains-line" }
        };
        Assert.Equal(SendClaimDisposition.PayloadConflict, (await store.ClaimBatchAsync(changed, default)).Disposition);
        Assert.Equal(0, sdk.Updates);
    }

    /// <summary>只有精確唯一鍵代碼可查回；一般DuplicateRecord、內層字串或其他故障不可猜成冪等。</summary>
    [Theory]
    [InlineData(unchecked((int)0x80040237))]
    [InlineData(unchecked((int)0x80040216))]
    public async Task Claim_NonKeyFault_OriginalFaultAndNoLookup(int code)
    {
        using var sdk = new SendRecordSdk { CreateFault = code };
        using var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        var error = await Assert.ThrowsAsync<FaultException<OrganizationServiceFault>>(() => store.ClaimBatchAsync(SendRecordSdk.Identity(), default));
        Assert.Equal(code, error.Detail.ErrorCode);
        Assert.Equal(0, sdk.Queries);
    }

    /// <summary>查回多筆正式列或缺版本就是發布衝突；不能Distinct掉其中一筆再假稱成功。</summary>
    [Theory]
    [InlineData("duplicates")]
    [InlineData("version")]
    [InlineData("identity")]
    public async Task Claim_InconsistentReadback_FailsClosed(string fault)
    {
        using var sdk = new SendRecordSdk();
        using var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        if (fault == "version") sdk.MissingVersion = true;
        else
        {
            await store.ClaimBatchAsync(SendRecordSdk.Identity(), default);
            if (fault == "duplicates") sdk.DuplicateQueryRow = true;
            else sdk.MutateRead = row => row.Id = Guid.Empty;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ClaimBatchAsync(SendRecordSdk.Identity(), default));
    }

    /// <summary>借用SDK不可被adapter關閉；Dispose後拒新工作，預取消不得Create。</summary>
    [Fact]
    public async Task Lifecycle_PreCancellationAndDispose_NoWritesOrBorrowedDisposal()
    {
        using var sdk = new SendRecordSdk();
        var store = new DataverseSendRecordStore(sdk, sdk.OrganizationId, "GoodShepherd2");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ClaimBatchAsync(SendRecordSdk.Identity(), cancellation.Token));
        store.Dispose();
        store.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ClaimBatchAsync(SendRecordSdk.Identity(), default));
        Assert.Equal(0, sdk.Creates);
        Assert.Equal(0, sdk.Disposals);
    }
}

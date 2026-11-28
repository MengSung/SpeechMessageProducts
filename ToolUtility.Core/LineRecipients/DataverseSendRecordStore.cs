/*
檔案整體說明
目的與定位：F5 固定產品的 Dataverse 原子發送紀錄 owner，將 HTTP 前占用與結果 CAS 分開。
主要功能與流程：建構時驗組織 GUID、已發布唯一鍵及並行能力；原子 Create 後讀回正式 ID
與 RowVersion，僅精確唯一鍵衝突可讀回既有內容。完成時按原版本 CAS，終態不得互相覆寫。
使用邊界與維護：借用主機的有截止時間 SDK，不擁有連線、不保留訊息／名單／憑證、不開
背景工作或本機鎖；紀錄留在固定組織直到核定保存政策。主機須先排空 Gateway 再關 SDK。
Dispose 只清本 owner 的借用參照；寫入已確認後不得因後到取消而回報成可安全重送。
*/
using System;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core.Gateway;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.LineRecipients;

/// <summary>依資料庫唯一鍵及樂觀版本保護發送；例外由呼叫端交同一共用診斷 owner。</summary>
public sealed class DataverseSendRecordStore : ISendRecordStore, IDisposable
{
    // 只接受 SDK 的精確 DuplicateRecordEntityKey；通用 DuplicateRecord 不證明撞到本鍵。
    private const int DuplicateRecordEntityKey = unchecked((int)0x80060892);
    private readonly DataversePushBoundary boundary;

    /// <summary>組合根提供固定組織與產品及有截止時間的借用 SDK，不能接受 request 路由當權威。</summary>
    public DataverseSendRecordStore(IOrganizationService service, Guid organizationId, string product)
    {
        var candidate = new DataversePushBoundary(service, organizationId, product);
        try
        {
            var response = service.Execute(new RetrieveEntityRequest
            {
                LogicalName = SendRecordData.EntityName, EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = false
            }) as RetrieveEntityResponse;
            var metadata = response?.EntityMetadata;
            if (metadata is null || metadata.LogicalName != SendRecordData.EntityName
                || metadata.IsOptimisticConcurrencyEnabled != true || metadata.Keys is null)
                throw new InvalidOperationException("Published send record concurrency metadata is required.");
            var matches = 0;
            foreach (var key in metadata.Keys)
                if (key.LogicalName == "new_ak_sendrecord" && key.EntityKeyIndexStatus == EntityKeyIndexStatus.Active
                    && key.KeyAttributes is { Length: 1 } && key.KeyAttributes[0] == "new_batchkey") matches++;
            if (matches != 1) throw new InvalidOperationException("Published Active send record alternate key is required.");
            boundary = candidate;
        }
        catch
        {
            candidate.Dispose();
            throw;
        }
    }

    /// <summary>只有本次已確認 Create 的 claim 允許 HTTP；既有占用一律不得接手。</summary>
    public Task<SendBatchClaim> ClaimBatchAsync(SendBatchIdentity identity, CancellationToken cancellationToken)
    {
        var client = boundary.GetService();
        cancellationToken.ThrowIfCancellationRequested();
        SendRecordData.Validate(identity, boundary.Product);
        Guid id;
        try { id = client.Create(SendRecordData.Candidate(identity)); }
        catch (FaultException<OrganizationServiceFault> fault) when (fault.Detail?.ErrorCode == DuplicateRecordEntityKey)
        {
            return Task.FromResult(ReadExisting(client, identity, cancellationToken));
        }
        // Create 已確認；即使 caller 此時取消，仍讀回既有寫入事實，不回報為可安全重送的取消。
        if (id == Guid.Empty) throw new InvalidOperationException("Create did not return a database identity.");
        var row = client.Retrieve(SendRecordData.EntityName, id, SendRecordData.Columns());
        var actual = SendRecordData.Read(row, id, boundary.Product);
        if (actual != identity || SendRecordData.Outcome(row) != SendRecordOutcome.Claimed)
            throw new InvalidOperationException("Created send record did not match the claimed identity.");
        return Task.FromResult(new SendBatchClaim(identity, id, row.RowVersion, SendClaimDisposition.Acquired));
    }

    /// <summary>精確版本完成終態；已確定成功不會被晚到 caller 取消撤銷。</summary>
    public Task CompleteBatchAsync(SendBatchClaim claim, SendBatchCompletion completion, CancellationToken cancellationToken)
    {
        var client = boundary.GetService();
        cancellationToken.ThrowIfCancellationRequested();
#if NETFRAMEWORK
        if (claim is null) throw new ArgumentNullException(nameof(claim));
#else
        ArgumentNullException.ThrowIfNull(claim);
#endif
        SendRecordData.Validate(claim.Identity, boundary.Product);
        SendRecordData.ValidateCompletion(claim.Identity, completion);
        if (claim.RecordId == Guid.Empty || !SendRecordData.Version(claim.RowVersion) || claim.Disposition != SendClaimDisposition.Acquired)
            throw new ArgumentException("Only an acquired database claim may complete.", nameof(claim));
        var row = client.Retrieve(SendRecordData.EntityName, claim.RecordId, SendRecordData.Columns());
        cancellationToken.ThrowIfCancellationRequested();
        if (SendRecordData.Read(row, claim.RecordId, boundary.Product) != claim.Identity)
            throw new InvalidOperationException("Send record completion identity mismatch.");
        if (SendRecordData.SameCompletion(row, completion)) return Task.CompletedTask;
        if (SendRecordData.Outcome(row) != SendRecordOutcome.Claimed || row.RowVersion != claim.RowVersion)
            throw new InvalidOperationException("Send record outcome or version cannot be overwritten.");
        var target = new Entity(SendRecordData.EntityName, claim.RecordId) { RowVersion = claim.RowVersion };
        target["new_result"] = new OptionSetValue((int)completion.Outcome);
        target["new_errorcode"] = completion.ErrorCode;
        target["new_attempts"] = completion.Attempts;
        target["new_senton"] = DateTime.UtcNow;
        cancellationToken.ThrowIfCancellationRequested();
        if (client.Execute(new UpdateRequest { Target = target, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }) is not UpdateResponse)
            throw new InvalidOperationException("Send record update acknowledgement is missing.");
        // Update 的成功回覆是已確認終態；不在其後檢查 caller 取消而偽裝失敗。
        return Task.CompletedTask;
    }

    /// <summary>精確唯一鍵衝突只允許一次有界讀取；缺列、多列或不一致正式身分全部拒絕。</summary>
    private SendBatchClaim ReadExisting(IOrganizationService client, SendBatchIdentity identity, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var query = new QueryExpression(SendRecordData.EntityName) { ColumnSet = SendRecordData.Columns(), TopCount = 2 };
        query.Criteria.AddCondition("new_batchkey", ConditionOperator.Equal, identity.BatchKey);
        var rows = client.RetrieveMultiple(query);
        ct.ThrowIfCancellationRequested();
        if (rows is null || rows.MoreRecords || rows.Entities.Count != 1)
            throw new InvalidOperationException("Alternate key did not resolve exactly one send record.");
        var row = rows.Entities[0];
        var actual = SendRecordData.Read(row, row.Id, boundary.Product);
        var disposition = actual != identity ? SendClaimDisposition.PayloadConflict : SendRecordData.Outcome(row) switch
        {
            SendRecordOutcome.Accepted => SendClaimDisposition.AlreadyAccepted,
            SendRecordOutcome.Failed or SendRecordOutcome.Rejected => SendClaimDisposition.AlreadyFinal,
            _ => SendClaimDisposition.Uncertain
        };
        return new SendBatchClaim(identity, row.Id, row.RowVersion, disposition);
    }

    /// <summary>解除借用參照；不關閉主機擁有的 SDK，主機另負責先排空使用者。</summary>
    public void Dispose() => boundary.Dispose();
}

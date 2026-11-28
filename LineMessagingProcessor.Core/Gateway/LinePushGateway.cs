/*
檔案整體說明
目的與定位：F5 的雙目標唯一租戶推播編排 owner，不提供 broadcast 或跨產品 fallback。
主要功能與流程：任何 await 前凍結有界名單及 SDK 模型，先查權威租戶、全名單與測試保護，
全部 JSON 候選完成後才逐批 claim／HTTP／CAS。最多一次傳輸重試，每次有一次 F3 401 刷新，
相同 body/key 不變；舊 claim 不接手，不確定或中斷必須對帳。
使用邊界與維護：只保存固定產品外借依賴，不保存 per-user／tenant 操作資料，不建立 queue、
timer 或背景工作。HTTP owner 仍是 Core，完成紀錄另用十秒 CTS，不因後到 caller 取消
隱藏接受結果；依賴 owner 必須先排空 Gateway 再 Dispose。診斷只傳原 Exception 與固定 operation。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.Core.ChannelPool;

namespace LineMessagingProcessor.Core.Gateway;

/// <summary>借用固定產品 owner 的無狀態編排；所有操作候選與重試狀態均限於單次呼叫。</summary>
public sealed class LinePushGateway : ILinePushGateway
{
    private readonly string product;
    private readonly ILineCredentialExecutor credentials;
    private readonly LinePushClient client;
    private readonly IRecipientMembership membership;
    private readonly ISendRecordStore records;
    private readonly Action<Exception, string> reportFailure;
    private readonly PushSafetyOptions options;

    /// <summary>
    /// 借用固定產品及其資料／HTTP／診斷 owner；來源必須實作非同步 401 接縫，不能靜默降級。
    /// reportFailure 必須連到與 F3 相同的共用診斷 owner，依 Exception 身分去重。
    /// </summary>
    public LinePushGateway(string product, IChannelCredentialSource credentials, LinePushClient client,
        IRecipientMembership membership, ISendRecordStore records, Action<Exception, string> reportFailure,
        PushSafetyOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(product) || product.Length > 64) throw new ArgumentException("A fixed product is required.", nameof(product));
        this.product = product;
        this.credentials = credentials as ILineCredentialExecutor
            ?? throw new ArgumentException("The source must support bounded credential execution.", nameof(credentials));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
        this.records = records ?? throw new ArgumentNullException(nameof(records));
        this.reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        this.options = options ?? new PushSafetyOptions();
    }

    /// <summary>
    /// 全名單核對後才開始資料庫及 HTTP；僅 Acquired claim 可送。接受與記錄不確定時，
    /// 保留持久 claim，不以新鍵重試；輸入錯誤及預期拒絕不是系統例外告警。
    /// </summary>
    public async Task<LinePushGatewayResult> SendAsync(PushScope scope, IReadOnlyList<string> recipientUserIds,
        IReadOnlyList<ISendMessage> messages, MessageCategory category, string correlationId, CancellationToken ct)
    {
        var recipients = ValidateAndCopy(scope, recipientUserIds, category, correlationId);
        var input = LinePushClient.FreezeGatewayInput(messages);
        ct.ThrowIfCancellationRequested();
        if (!string.Equals(scope.Product, product, StringComparison.Ordinal))
            return new LinePushGatewayResult(0, false, PushRejectedReason.ForeignScope, Array.Empty<SendBatchSummary>());
        try
        {
            var facts = await membership.ReadTenantAsync(scope, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (facts is null)
                return new LinePushGatewayResult(0, false, PushRejectedReason.ForeignScope, Array.Empty<SendBatchSummary>());
            var frozen = SenderRules.Apply(input, facts.DisplayName, facts.IconUrl);
            var senderNote = SenderRules.GetSenderOmissionReason(facts.DisplayName);
            var operationHash = GatewayBatchFactory.OperationHash(recipients, frozen, facts, category);
            var reason = PushRejectedReason.None;
            if (options.TestTenantsOnly &&
                (!facts.IsTestTenant || !await membership.IsTestTenantAsync(scope.TenantId, ct).ConfigureAwait(false)))
                reason = PushRejectedReason.NonTestTenant;
            else if (!await membership.AreAllMembersAsync(scope, recipients, ct).ConfigureAwait(false))
                reason = PushRejectedReason.ForeignRecipient;
            ct.ThrowIfCancellationRequested();
            if (reason != PushRejectedReason.None)
            {
                var id = GatewayBatchFactory.Rejection(scope, correlationId, recipients, frozen, category, operationHash, senderNote, reason);
                var claim = await records.ClaimBatchAsync(id, ct).ConfigureAwait(false);
                if (claim.Disposition == SendClaimDisposition.PayloadConflict)
                    return new LinePushGatewayResult(0, false, PushRejectedReason.PayloadConflict, Array.Empty<SendBatchSummary>());
                ValidateClaim(claim, id);
                if (claim.Disposition == SendClaimDisposition.Acquired)
                    await CompleteAsync(claim, new SendBatchCompletion(SendRecordOutcome.Rejected, reason.ToString(), 0)).ConfigureAwait(false);
                return new LinePushGatewayResult(0, false, reason,
                    new[] { new SendBatchSummary(claim.RecordId, recipients.Length, SendRecordOutcome.Rejected) });
            }
            var batches = GatewayBatchFactory.Create(scope, correlationId, recipients, frozen, category, operationHash, senderNote);
            var summaries = new List<SendBatchSummary>(batches.Length);
            var accepted = 0;
            foreach (var batch in batches)
            {
                ct.ThrowIfCancellationRequested();
                var claim = await records.ClaimBatchAsync(batch.Identity, ct).ConfigureAwait(false);
                if (claim.Disposition == SendClaimDisposition.PayloadConflict)
                    return new LinePushGatewayResult(accepted, false, PushRejectedReason.PayloadConflict, summaries);
                ValidateClaim(claim, batch.Identity);
                if (claim.Disposition != SendClaimDisposition.Acquired)
                {
                    var existing = claim.Disposition == SendClaimDisposition.AlreadyAccepted ? SendRecordOutcome.Accepted
                        : claim.Disposition == SendClaimDisposition.Uncertain ? SendRecordOutcome.Uncertain : SendRecordOutcome.Failed;
                    summaries.Add(new SendBatchSummary(claim.RecordId, batch.Identity.RecipientCount, existing));
                    if (existing != SendRecordOutcome.Accepted)
                        return new LinePushGatewayResult(accepted, existing == SendRecordOutcome.Uncertain, PushRejectedReason.None, summaries);
                    accepted += batch.Identity.RecipientCount;
                    continue;
                }
                var attempts = 0;
                LinePushResult response;
                try
                {
                    var retry = 0;
                    do
                    {
                        // 每次 timeout 後重新入場，絕不重用已取消的 F3 operation token。
                        response = await credentials.ExecuteAsync(scope.ChannelKey, (credential, token) =>
                        {
                            attempts++;
                            return client.SendGatewayBatchAsync(credential, batch.Body, batch.Identity.RetryKey, batch.Multicast, token);
                        }, ct).ConfigureAwait(false);
                    } while (response.TransportException is not null && retry++ < 1);
                }
                catch (Exception)
                {
                    // HTTP 前後不確定時都保守保留對帳；記錄故障不得覆蓋原始傳輸例外。
                    try { await CompleteAsync(claim, new SendBatchCompletion(SendRecordOutcome.Uncertain, "execution-unconfirmed", attempts)).ConfigureAwait(false); }
                    catch (Exception recordFailure) { Report(recordFailure); }
                    throw;
                }
                var uncertain = response.TransportException is not null || (int?)response.StatusCode is >= 500 and <= 599;
                var outcome = response.Success ? SendRecordOutcome.Accepted : uncertain ? SendRecordOutcome.Uncertain : SendRecordOutcome.Failed;
                var code = response.StatusCode.HasValue
                    ? "http-" + ((int)response.StatusCode.Value).ToString(CultureInfo.InvariantCulture) : "transport-unconfirmed";
                await CompleteAsync(claim, new SendBatchCompletion(outcome, response.Success ? null : code, attempts)).ConfigureAwait(false);
                summaries.Add(new SendBatchSummary(claim.RecordId, batch.Identity.RecipientCount, outcome));
                if (response.Success) { accepted += batch.Identity.RecipientCount; continue; }
                // 第二次 401 已由 F3 owner 停用並通知，不新造第二個事件；其他終端故障交同一共用入口。
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                    Report(response.TransportException ?? new HttpRequestException("LINE push did not complete successfully."));
                return new LinePushGatewayResult(accepted, uncertain, PushRejectedReason.None, summaries);
            }
            return new LinePushGatewayResult(accepted, false, PushRejectedReason.None, summaries);
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException || !ct.IsCancellationRequested) Report(exception);
            throw;
        }
    }

    /// <summary>只在平台事實已知後使用獨立十秒期限；不建立脫離 await 的背景寫入。</summary>
    private async Task CompleteAsync(SendBatchClaim claim, SendBatchCompletion completion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await records.CompleteBatchAsync(claim, completion, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>來源必須回相同完整事實與正式資料庫 ID／版本；不信任空 ID 或任意列替換。</summary>
    private static void ValidateClaim(SendBatchClaim claim, SendBatchIdentity identity)
    {
        if (claim.Identity != identity || claim.RecordId == Guid.Empty || string.IsNullOrWhiteSpace(claim.RowVersion)
            || claim.Disposition is < SendClaimDisposition.Acquired or > SendClaimDisposition.PayloadConflict)
            throw new InvalidOperationException("Send record claim did not match the requested immutable identity.");
    }

    /// <summary>診斷入口故障只能用固定 stderr，不取代原例外或遞迴通知。</summary>
    private void Report(Exception exception)
    {
        try { reportFailure(exception, "LinePushGateway.Send"); }
        catch (Exception) { Console.Error.WriteLine("LinePushGateway diagnostics unavailable."); }
    }

    /// <summary>於任何 I/O 前完成有界身分及名單複製，重複 ID 拒絕而非靜默去重。</summary>
    private static string[] ValidateAndCopy(PushScope scope, IReadOnlyList<string> recipients, MessageCategory category, string correlationId)
    {
#if NETFRAMEWORK
        if (scope is null) throw new ArgumentNullException(nameof(scope));
        if (recipients is null) throw new ArgumentNullException(nameof(recipients));
#else
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(recipients);
#endif
        if (scope.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(scope.Product) || scope.Product.Length > 64
            || string.IsNullOrEmpty(scope.ChannelKey) || scope.ChannelKey.Length > 20)
            throw new ArgumentException("A complete tenant scope is required.", nameof(scope));
        foreach (var character in scope.ChannelKey)
            if (character is < '0' or > '9') throw new ArgumentException("A numeric channel key is required.", nameof(scope));
        if (category is < MessageCategory.Transactional or > MessageCategory.Marketing) throw new ArgumentOutOfRangeException(nameof(category));
        if (string.IsNullOrEmpty(correlationId) || correlationId.Length > 128)
            throw new ArgumentException("A bounded server operation identity is required.", nameof(correlationId));
        foreach (var character in correlationId)
            if (character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9')
                and not '-' and not '_' and not ':' and not '.')
                throw new ArgumentException("A safe server operation identity is required.", nameof(correlationId));
        if (recipients.Count is < 1 or > 5000) throw new ArgumentException("One through 5000 recipients are required.", nameof(recipients));
        var copy = new string[recipients.Count];
        var identities = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < copy.Length; index++)
        {
            var value = recipients[index];
            LinePushRequest.RequireUserId(value);
            if (!identities.Add(value)) throw new ArgumentException("Duplicate recipient identities are not allowed.", nameof(recipients));
            copy[index] = value;
        }
        return copy;
    }
}

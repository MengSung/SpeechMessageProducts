/*
檔案整體說明
目的與定位：F3 真正 SDK 目錄轉接器，固定產品及組織，借用既有池的一次性 IClientLease；不讀取主機設定或建立 CRM client。
主要功能與流程：每次租約先以 WhoAmI 驗證實際組織，本產品 metadata 與秘密查詢始終維持精確產品條件。完整憑證刷新另在同一租約以最多一筆、僅含 new_product 的查詢偵測非空外產品；空白忽略，外產品由來源 owner 經必填共用診斷上報一次，不讀取其 Channel 鍵、密文或個資。健康回寫重新核對同一 Id／產品／版本，使用樂觀併行 UpdateRequest。
使用邊界與維護：來源只保存固定部署值、借用工廠、診斷及一個告警旗標，不保存 SDK、User、Session 或外產品資料。每次操作最多一個租約、有限頁數與欄位；只有支援 cancellation 的 Async2 SDK 可用，總期限由外層目錄的十秒操作 token 控制。本次候選與密文不在來源快取；租約拒絕並行操作，關閉先拒新入口、等待在途 I/O 排空，再歸還並清空來源、SDK 與失敗參照。傳輸或取消均淘汰 uncertain client，清理故障與原例外一併保留；不 Dispose manager／原始 client、不新增背景工作。主機先排空目錄，再釋放借用依賴；離線 SDK、故障注入及弱參照測試驗證本檔契約。
*/
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using ToolUtilityNameSpace.Dataverse;
namespace ToolUtilityNameSpace.Channels;

/// <summary>
/// 固定產品與組織的 SDK adapter，借用既有 manager.Acquire；每次租約先驗 WhoAmI，再允許受限的產品查詢。
/// 來源只保存部署與借用依賴；所有 Entity、密文及 SDK 參照皆由單次 lease 操作持有，沒有跨使用者結果快取。
/// </summary>
public sealed class DataverseChannelDirectorySource : IChannelDirectorySource
{
    /// <summary>本產品驗證與 CAS 所需的精確中繼資料，不包含顯示名稱或秘密。</summary>
    private static readonly string[] MetadataColumns = ["new_linechannelid", "new_product", "new_channelid", "new_channelstatus", "modifiedon", "versionnumber"];
    /// <summary>僅在本產品正式 ID 與精確產品條件同時成立時投影；不得套用至外產品偵測。</summary>
    private static readonly string[] SecretColumns = ["new_product", "new_accesstokenencrypted", "new_channelsecretencrypted"];
    private readonly Func<CancellationToken, IClientLease> acquire;
    private readonly IChannelDiagnostics diagnostics;
    private readonly int maximumChannels;
    private int foreignReported;
    /// <summary>
    /// 固定部署及容量，借用工廠與必填診斷；不在建構時連線、不接管 manager 或建立告警工作。
    /// 每個來源 owner 最多一次外產品告警，沒有按外產品名稱累積的集合；主機須在目錄排空後才釋放依賴。
    /// </summary>
    public DataverseChannelDirectorySource(string product, Guid organizationId, Func<CancellationToken, IClientLease> acquire,
        IChannelDiagnostics diagnostics, int maximumChannels = 256)
    {
        ChannelDirectoryValidation.Deployment(product, organizationId, maximumChannels);
        Product = product; OrganizationId = organizationId;
        this.acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
        this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        this.maximumChannels = maximumChannels;
    }
    /// <summary>主機已驗證的組織 GUID，不是組織名稱或 caller 路由。</summary>
    public Guid OrganizationId { get; }
    /// <summary>精確三產品之一，建立後不可變。</summary>
    public string Product { get; }

    /// <summary>
    /// 取得唯一 SDK lease 並驗證實際組織；只接受可取消 Async2，後續 metadata／偵測／密文共用同一租約。
    /// 預取消不借用；取得後的身分或傳輸失敗先淘汰再歸還，清理故障不得遮蔽原始例外。
    /// </summary>
    public async ValueTask<IChannelDirectorySourceLease> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var lease = acquire(cancellationToken) ?? throw new InvalidOperationException("Dataverse manager returned no lease.");
        try
        {
            if (lease.Service is not IOrganizationServiceAsync2 asynchronous)
                throw new InvalidOperationException("Channel directory requires cancellation-aware Dataverse SDK.");
            var response = await asynchronous.ExecuteAsync(new WhoAmIRequest(), cancellationToken).ConfigureAwait(false);
            var actual = response.Results.TryGetValue("OrganizationId", out var value) && value is Guid id ? id : Guid.Empty;
            if (actual == Guid.Empty || actual != OrganizationId) throw new InvalidOperationException("Dataverse organization boundary mismatch.");
            return new SourceLease(this, lease, asynchronous);
        }
        catch (Exception failure)
        {
            try { await ReturnFailedLeaseAsync(lease, failure).ConfigureAwait(false); }
            catch (AggregateException cleanup) { throw cleanup; }
            throw;
        }
    }

    /// <summary>取得完整、受限的分頁結果；cookie 重複、頁數或列數超限均拒絕整批。</summary>
    private async Task<Entity[]> QueryAllAsync(IOrganizationServiceAsync2 service, ColumnSet columns, CancellationToken cancellationToken)
    {
        var result = new List<Entity>(Math.Min(maximumChannels + 1, 256));
        var cookies = new HashSet<string>(StringComparer.Ordinal);
        string? cookie = null;
        for (var pageNumber = 1; pageNumber <= Math.Min(maximumChannels + 1, ChannelDirectoryValidation.HardChannelLimit + 1); pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new QueryExpression("new_linechannel")
            {
                ColumnSet = columns,
                PageInfo = new PagingInfo { Count = Math.Min(50, maximumChannels), PageNumber = pageNumber, PagingCookie = cookie }
            };
            query.Criteria.AddCondition("new_product", ConditionOperator.Equal, Product);
            var page = await service.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Dataverse returned no channel page.");
            if (page.Entities == null || page.Entities.Count > maximumChannels + 1 - result.Count)
                throw new InvalidOperationException("Dataverse channel page exceeds configured capacity.");
            result.AddRange(page.Entities);
            if (result.Count > maximumChannels) throw new InvalidOperationException("Dataverse product channel capacity exceeded.");
            if (!page.MoreRecords) return result.ToArray();
            if (string.IsNullOrEmpty(page.PagingCookie) || !cookies.Add(page.PagingCookie))
                throw new InvalidOperationException("Dataverse channel paging did not advance.");
            cookie = page.PagingCookie;
        }
        throw new InvalidOperationException("Dataverse channel page limit exceeded.");
    }

    /// <summary>
    /// 先以資料庫精確過濾及逐列驗證建立本產品 metadata；純版本檢查與健康回寫不增加外產品查詢。
    /// 完整刷新才在同一已驗組織 lease 偵測一次外產品，再按本產品正式 ID 取得秘密；任何 SDK 失敗都不回部分結果。
    /// </summary>
    private async Task<IReadOnlyList<ChannelDirectoryRecord>> ReadAsync(IOrganizationServiceAsync2 service, bool includeCredentials, CancellationToken cancellationToken)
    {
        var metadata = await QueryAllAsync(service, new ColumnSet(MetadataColumns), cancellationToken).ConfigureAwait(false);
        var validated = ValidateMetadata(metadata);
        if (!includeCredentials) return validated;
        await ObserveForeignProductAsync(service, cancellationToken).ConfigureAwait(false);
        var records = new List<ChannelDirectoryRecord>(validated.Length);
        foreach (var row in validated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var query = new QueryExpression("new_linechannel")
            {
                ColumnSet = new ColumnSet(SecretColumns),
                TopCount = 2
            };
            query.Criteria.AddCondition("new_linechannelid", ConditionOperator.Equal, row.Id);
            query.Criteria.AddCondition("new_product", ConditionOperator.Equal, Product);
            var result = await service.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Dataverse returned no credential result.");
            if (result.Entities.Count != 1 || result.Entities[0].Id != row.Id
                || !string.Equals(result.Entities[0].GetAttributeValue<string>("new_product"), Product, StringComparison.Ordinal))
                throw new InvalidOperationException("Dataverse channel identity changed before credential read.");
            var encryptedToken = result.Entities[0].GetAttributeValue<string>("new_accesstokenencrypted");
            var encryptedSecret = result.Entities[0].GetAttributeValue<string>("new_channelsecretencrypted");
            records.Add(new ChannelDirectoryRecord(row.Id, row.Product, row.ChannelKey, row.Status, row.Revision,
                row.ModifiedOn, encryptedToken, encryptedSecret));
        }
        return records.ToArray();
    }

    /// <summary>
    /// 同一 WhoAmI 已驗租約最多取一筆非空且非本產品的產品欄位，絕不投影路由、秘密或個資，也不追頁。
    /// 再檢空白與精確產品以防異常回應；取消及 SDK 故障原樣傳出，由 lease 淘汰與外層目錄告警。
    /// 外產品事件只保存原子旗標；診斷入口故障走固定 stderr，不覆蓋本產品成功流程或遞迴通知。
    /// </summary>
    private async Task ObserveForeignProductAsync(IOrganizationServiceAsync2 service, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var query = new QueryExpression("new_linechannel") { ColumnSet = new ColumnSet("new_product"), TopCount = 1 };
        query.Criteria.AddCondition("new_product", ConditionOperator.NotNull);
        query.Criteria.AddCondition("new_product", ConditionOperator.NotEqual, Product);
        query.Criteria.AddCondition("new_product", ConditionOperator.NotEqual, string.Empty);
        var result = await service.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Dataverse returned no product observation result.");
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Entities == null || result.Entities.Count > 1)
            throw new InvalidOperationException("Dataverse product observation exceeds its bound.");
        if (result.Entities.Count == 0) return;
        var entity = result.Entities[0];
        if (entity == null || !string.Equals(entity.LogicalName, "new_linechannel", StringComparison.Ordinal))
            throw new InvalidOperationException("Dataverse returned an invalid product observation.");
        var product = entity.GetAttributeValue<string>("new_product");
        if (string.IsNullOrWhiteSpace(product) || string.Equals(product, Product, StringComparison.Ordinal)
            || Interlocked.Exchange(ref foreignReported, 1) != 0) return;
        try
        {
            // 拋出後交給共用入口，保留發現不一致的位置；例外不包含來源產品、Id 或任何資料內容。
            throw new InvalidOperationException("Foreign product channel ignored.");
        }
        catch (InvalidOperationException failure)
        {
            try { diagnostics.Report(failure, "ChannelDirectory.ForeignProduct", cancellationToken); }
            catch
            {
                try { Console.Error.WriteLine("[ChannelDirectorySource] DiagnosticsUnavailable"); }
                catch (System.IO.IOException) { /* stderr 不可用仍不得覆蓋產品結果，不建立第二個告警管道。 */ }
            }
        }
    }

    /// <summary>忽略空白／他產品（不取其密文），對本產品逐列驗正式欄位、choice、版本、UTC 與唯一主鍵／鍵。</summary>
    private ChannelDirectoryRecord[] ValidateMetadata(Entity[] entities)
    {
        var candidate = new List<ChannelDirectoryRecord>();
        foreach (var entity in entities)
        {
            if (entity == null || !string.Equals(entity.LogicalName, "new_linechannel", StringComparison.Ordinal))
                throw new InvalidOperationException("Dataverse returned an invalid channel entity.");
            var product = entity.GetAttributeValue<string>("new_product");
            if (string.IsNullOrWhiteSpace(product)) continue;
            if (!string.Equals(product, Product, StringComparison.Ordinal)) continue;
            var id = entity.GetAttributeValue<Guid>("new_linechannelid");
            var channelKey = entity.GetAttributeValue<string>("new_channelid");
            var choice = entity.GetAttributeValue<OptionSetValue>("new_channelstatus")?.Value;
            var modified = entity.GetAttributeValue<DateTime>("modifiedon");
            var version = entity.GetAttributeValue<long?>("versionnumber");
            if (id == Guid.Empty || entity.Id != id || !ChannelDirectoryValidation.Digits(channelKey, 20)
                || choice == null || !Enum.IsDefined(typeof(DirectoryChannelStatus), choice.Value)
                || modified.Kind != DateTimeKind.Utc || version is null or <= 0)
                throw new InvalidOperationException("Dataverse channel metadata is invalid.");
            candidate.Add(new ChannelDirectoryRecord(id, product, channelKey!, (DirectoryChannelStatus)choice.Value,
                version.Value.ToString(CultureInfo.InvariantCulture), modified));
        }
        return ChannelDirectoryValidation.OwnRows(candidate, Product, maximumChannels);
    }

    /// <summary>只讀回相同正式列與版本後送 RowVersion CAS；target 僅含三個健康欄位或 Disabled 加時間。</summary>
    private async Task WriteHealthAsync(IOrganizationServiceAsync2 service, ChannelDirectoryRecord channel,
        ChannelHealthResult result, bool disable, CancellationToken cancellationToken)
    {
        var query = new QueryExpression("new_linechannel") { ColumnSet = new ColumnSet(MetadataColumns), TopCount = 2 };
        query.Criteria.AddCondition("new_linechannelid", ConditionOperator.Equal, channel.Id);
        query.Criteria.AddCondition("new_product", ConditionOperator.Equal, Product);
        var matches = await service.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Dataverse returned no health target.");
        if (matches.Entities.Count != 1) throw new InvalidOperationException("Dataverse health target is missing or ambiguous.");
        var current = ValidateMetadata(matches.Entities.ToArray()).Single();
        if (!ChannelDirectoryValidation.SameRow(channel, current)) throw new InvalidOperationException("Dataverse health target changed concurrently.");
        var target = new Entity("new_linechannel", channel.Id) { RowVersion = channel.Revision };
        if (disable) target["new_channelstatus"] = new OptionSetValue((int)DirectoryChannelStatus.Disabled);
        else
        {
            if (result.MonthlyUsage < 0 || result.RichMenuCount < 0) throw new InvalidOperationException("Health values are outside their valid range.");
            target["new_monthlyusage"] = result.MonthlyUsage;
            target["new_actualrichmenucount"] = result.RichMenuCount;
        }
        target["new_lastcheckedon"] = result.CheckedAt.UtcDateTime;
        var request = new UpdateRequest { Target = target, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches };
        await service.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>同步返回失敗租約時先淘汰，再嘗試釋放；雙故障以原物件 Aggregate 保留。</summary>
    private static async Task ReturnFailedLeaseAsync(IClientLease lease, Exception primary)
    {
        Exception? fault = null;
        try { lease.MarkFaulted(); } catch (Exception exception) { fault = exception; }
        try { lease.Dispose(); } catch (Exception exception)
        {
            if (fault == null) fault = exception; else fault = new AggregateException(fault, exception);
        }
        if (fault != null) throw new AggregateException("Dataverse lease cleanup failed after the original failure.", primary, fault);
    }

    /// <summary>
    /// 一次目錄操作的唯一 SDK lease owner；依序讀取、驗證及回寫，重疊操作立即拒絕而不累積等待佇列。
    /// 關閉拒新入口並 await 在途操作；完成歸還後不保留 SDK、來源工廠、診斷或原例外。
    /// </summary>
    private sealed class SourceLease : IChannelDirectorySourceLease
    {
        private readonly object state = new();
        private DataverseChannelDirectorySource? owner;
        private IOrganizationServiceAsync2? service;
        private IClientLease? current;
        private Exception? failure;
        private TaskCompletionSource? drained;
        private Task? closeTask;
        private bool active;
        private bool closing;
        private bool faulted;
        /// <summary>接管本次已驗 SDK 租約；manager 及 raw service 仍由原連線池擁有。</summary>
        internal SourceLease(DataverseChannelDirectorySource owner, IClientLease lease, IOrganizationServiceAsync2 service)
        {
            this.owner = owner; current = lease; this.service = service;
        }
        /// <summary>先入場再依序完整讀頁、觀察產品及取密文；失敗先淘汰，finally 通知關閉 owner 已排空。</summary>
        public async Task<IReadOnlyList<ChannelDirectoryRecord>> ReadAsync(bool includeCredentials, CancellationToken cancellationToken)
        {
            var context = Begin(cancellationToken);
            try { return await context.Owner.ReadAsync(context.Service, includeCredentials, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { Capture(exception); throw; }
            finally { Exit(); }
        }
        /// <summary>重驗 target 並 CAS；錯誤 SDK 狀態不交回健康 pool。</summary>
        public async Task WriteHealthAsync(ChannelDirectoryRecord channel, ChannelHealthResult result, bool disable, CancellationToken cancellationToken)
        {
            var context = Begin(cancellationToken);
            try { await context.Owner.WriteHealthAsync(context.Service, channel, result, disable, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) { Capture(exception); throw; }
            finally { Exit(); }
        }
        /// <summary>同一 lease 最多一個在途操作；關閉後先拒絕，避免已歸還 client 被再次使用。</summary>
        private (DataverseChannelDirectorySource Owner, IOrganizationServiceAsync2 Service) Begin(CancellationToken cancellationToken)
        {
            lock (state)
            {
                ObjectDisposedException.ThrowIf(closing, this);
                cancellationToken.ThrowIfCancellationRequested();
                if (active) throw new InvalidOperationException("Concurrent channel source lease operations are not supported.");
                if (faulted) throw new InvalidOperationException("Faulted channel source lease cannot be reused.");
                active = true;
                return (owner!, service!);
            }
        }
        /// <summary>所有 SDK 與淘汰已完成才退出；關閉只等待這個受限 owner，不建立背景排空工作。</summary>
        private void Exit()
        {
            lock (state) { active = false; drained?.TrySetResult(); }
        }
        /// <summary>保留首次原例外供關閉故障聚合；取消也淘汰 uncertain client，正常與內部取消由外層目錄分類。</summary>
        private void Capture(Exception exception)
        {
            failure ??= exception;
            // SDK I/O 取消後傳輸狀態不確定；一律淘汰租約，caller cancellation 也不能把 client 交給下一操作。
            MarkFaulted();
        }
        /// <summary>停用 uncertain client；租約池仍是唯一 client owner。</summary>
        private void MarkFaulted()
        {
            if (faulted) return;
            faulted = true;
            try { current?.MarkFaulted(); }
            catch (Exception cleanup) { failure = failure == null ? cleanup : new AggregateException(failure, cleanup); }
        }
        /// <summary>
        /// 先拒絕入場再等待當次 I/O；並行關閉共用同一完成訊號，只有首位 owner 執行歸還。
        /// 外借 Dispose 一律在狀態鎖外執行；正常已排空路徑同步釋放，不為每次解析多排一個 continuation。
        /// </summary>
        public ValueTask DisposeAsync()
        {
            Task wait;
            TaskCompletionSource completion;
            lock (state)
            {
                if (closing) return closeTask == null ? ValueTask.CompletedTask : new ValueTask(closeTask);
                closing = true;
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                closeTask = completion.Task;
                wait = active ? (drained = new(TaskCreationOptions.RunContinuationsAsynchronously)).Task : Task.CompletedTask;
            }
            if (!wait.IsCompleted) return new ValueTask(CloseAfterDrainAsync(wait, completion));
            CompleteClose(completion);
            return new ValueTask(completion.Task);
        }
        /// <summary>只有關閉與 admitted 操作重疊才非同步等待；caller await 此方法即可觀察清理結果及完整排空。</summary>
        private async Task CloseAfterDrainAsync(Task wait, TaskCompletionSource completion)
        {
            await wait.ConfigureAwait(false);
            CompleteClose(completion);
            await completion.Task.ConfigureAwait(false);
        }
        /// <summary>排空後歸還一次，雙故障保留原物件；成功或失敗均先清參照，再喚醒所有關閉者。</summary>
        private void CompleteClose(TaskCompletionSource completion)
        {
            Exception? result = null;
            try
            {
                var value = Interlocked.Exchange(ref current, null);
                Exception? cleanup = null;
                try { value?.Dispose(); } catch (Exception exception) { cleanup = exception; }
                if (cleanup != null)
                {
                    if (failure != null) throw new AggregateException("Dataverse operation and lease cleanup failed.", failure, cleanup);
                    throw cleanup;
                }
                if (failure is AggregateException aggregate && aggregate.InnerExceptions.Count > 1)
                    throw new AggregateException("Dataverse operation and lease fault marking failed.", aggregate);
            }
            catch (Exception exception) { result = exception; }
            finally
            {
                lock (state)
                {
                    owner = null; service = null; failure = null; drained = null; closeTask = null;
                }
            }
            if (result == null) completion.TrySetResult();
            else completion.TrySetException(result);
        }
    }
}

/*
檔案整體說明
目的與定位：F3 名額申請單安全診斷的主機接收邊界，將沙箱隨交易提交的產品不符封套交給既有集中式例外輸出。
主要功能與流程：只在固定產品及每次 WhoAmI 核實的組織內，讀取有界診斷候選，以 RowVersion 領取租約，再驗正式列並提交同一 incident；接受後才標記完成。
使用邊界與維護：每次呼叫唯一擁有 SDK lease 與取消來源，不保存 request、使用者、憑證或跨次事件集合；接收器不自行建立背景工作，主機負責定期呼叫及停止排空。原始沙箱例外無法跨程序傳遞，安全封套只重建同型別例外，不能宣稱保有原始堆疊。崩潰於通知與確認之間可能以同一 incident 重送，屬至少一次傳遞，不保證分散式 exactly-once。
*/
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.ServiceModel;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace ToolUtilityNameSpace.Channels;

/// <summary>固定部署的有界持久事件接收器；不擁有 manager 或共用 diagnostics。</summary>
public sealed class SlotRequestDiagnosticReceiver
{
    private const string Operation = "SlotRequest.ProductMismatch";
    private static readonly string[] Columns = ["new_result", "new_f3diagnostic", "new_f3diagnosticstate",
        "new_f3diagnosticclaim", "new_f3diagnosticuntil", "versionnumber"];
    private readonly string product;
    private readonly Guid organizationId;
    private readonly Func<CancellationToken, IClientLease> acquire;
    private readonly IChurchClock clock;
    private readonly ExceptionDiagnostics diagnostics;
    private int processing;

    /// <summary>驗證固定部署依賴，建構時不連線或啟動任何工作。</summary>
    public SlotRequestDiagnosticReceiver(string product, Guid organizationId, Func<CancellationToken, IClientLease> acquire,
        IChurchClock clock, ExceptionDiagnostics diagnostics)
    {
        ChannelDirectoryValidation.Deployment(product, organizationId, 16);
        ArgumentNullException.ThrowIfNull(acquire);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.product = product; this.organizationId = organizationId;
        this.acquire = acquire; this.clock = clock; this.diagnostics = diagnostics;
    }

    /// <summary>處理至多十六筆已提交事件；回傳本輪共用入口接受數量，並非 LINE 送達數量。</summary>
    public async Task<int> DrainOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 同一 owner 滿載直接拒絕，不建立無界等待者；跨程序互斥仍由正式 RowVersion 提供。
        if (Interlocked.CompareExchange(ref processing, 1, 0) != 0) return 0;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            var token = deadline.Token;
            IClientLease? lease = null;
            Exception? failure = null;
            var accepted = 0;
            try
            {
                lease = acquire(token) ?? throw Invalid();
                if (lease.Service is not IOrganizationServiceAsync2 service) throw Invalid();
                var identity = await service.ExecuteAsync(new WhoAmIRequest(), token).ConfigureAwait(false);
                if (identity == null || !identity.Results.TryGetValue("OrganizationId", out var raw)
                    || raw is not Guid actual || actual != organizationId) throw Invalid();
                var now = clock.UtcNow.ToUniversalTime();
                var query = new QueryExpression("new_slotrequest") { ColumnSet = new ColumnSet(Columns), TopCount = 16 };
                query.AddOrder("new_slotrequestid", OrderType.Ascending);
                query.Criteria.AddCondition("new_result", ConditionOperator.Equal, 100000002);
                query.Criteria.AddCondition("new_f3diagnostic", ConditionOperator.NotNull);
                var eligible = query.Criteria.AddFilter(LogicalOperator.Or);
                eligible.AddCondition("new_f3diagnosticstate", ConditionOperator.Equal, 0);
                var expired = eligible.AddFilter(LogicalOperator.And);
                expired.AddCondition("new_f3diagnosticstate", ConditionOperator.Equal, 1);
                expired.AddCondition("new_f3diagnosticuntil", ConditionOperator.LessEqual, now.UtcDateTime);
                var page = await service.RetrieveMultipleAsync(query, token).ConfigureAwait(false) ?? throw Invalid();
                if (page.Entities.Count > 16) throw Invalid();
                var candidates = new List<Candidate>(page.Entities.Count);
                var ids = new HashSet<Guid>();
                foreach (var row in page.Entities)
                {
                    var candidate = Validate(row);
                    if (!ids.Add(candidate.Id) || (candidate.State != 0 &&
                        (candidate.State != 1 || !LeaseExpired(row, now)))) throw Invalid();
                    candidates.Add(candidate);
                }
                foreach (var candidate in candidates)
                {
                    token.ThrowIfCancellationRequested();
                    var claim = Guid.NewGuid().ToString("D");
                    var until = clock.UtcNow.ToUniversalTime().AddMinutes(2).UtcDateTime;
                    var target = Transition(candidate, 1, claim, until);
                    try { await UpdateAsync(service, target, token).ConfigureAwait(false); }
                    catch (FaultException<OrganizationServiceFault> conflict) when (conflict.Detail?.ErrorCode == -2147088254)
                    {
                        // 另一程序已先取得同一版本，屬預期競爭，未領取就不能發送。
                        continue;
                    }
                    var claimed = await service.RetrieveAsync("new_slotrequest", candidate.Id, new ColumnSet(Columns), token).ConfigureAwait(false);
                    var current = Validate(claimed);
                    if (current.Id != candidate.Id || current.Envelope != candidate.Envelope || current.State != 1 ||
                        claimed.GetAttributeValue<string>("new_f3diagnosticclaim") != claim ||
                        claimed.GetAttributeValue<DateTime?>("new_f3diagnosticuntil") != until) throw Invalid();
                    token.ThrowIfCancellationRequested();
                    // 僅重建已驗型別的安全投影。原沙箱 Exception 及堆疊不可能跨程序傳遞，不能偽造原始位置。
                    var projected = new InvalidPluginExecutionException("Persisted F3 product mismatch diagnostic.");
                    var published = diagnostics.ReportPersistedFailure(projected, current.Incident, current.Utc, product, Operation, cancellationToken);
                    await UpdateAsync(service, Transition(current, published ? 2 : 0, null, null), token).ConfigureAwait(false);
                    if (published) accepted++;
                }
            }
            catch (Exception exception) { failure = exception; }
            // SDK、淘汰及歸還錯誤皆保留原物件，全部清理後才上報或傳播；不 Dispose manager／raw client。
            if (lease != null)
            {
                if (failure != null)
                {
                    try { lease.MarkFaulted(); }
                    catch (Exception exception) { failure = new AggregateException(failure, exception); }
                }
                try { lease.Dispose(); }
                catch (Exception exception) { failure = failure == null ? exception : new AggregateException(failure, exception); }
            }
            if (failure != null)
            {
                diagnostics.Report(failure, "ChannelPool.Diagnostics.Drain", cancellationToken);
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return accepted;
        }
        finally { Volatile.Write(ref processing, 0); }
    }

    /// <summary>等待中的租約必須有真正 UTC 到期時間；缺值與錯型別不能當成已到期。</summary>
    private static bool LeaseExpired(Entity row, DateTimeOffset now) => row.Attributes.TryGetValue("new_f3diagnosticuntil", out var raw)
        && raw is DateTime until && until.Kind == DateTimeKind.Utc && until <= now.UtcDateTime;

    /// <summary>驗完整正式身分、版本及六個封套欄位；重複 JSON 名稱、未知欄位、其他產品或例外型別均拒絕。</summary>
    private Candidate Validate(Entity row)
    {
        if (row == null || row.Id == Guid.Empty || row.LogicalName != "new_slotrequest" ||
            row.GetAttributeValue<OptionSetValue>("new_result")?.Value != 100000002 ||
            !row.Attributes.TryGetValue("versionnumber", out var version) || version is not long number || number <= 0 ||
            !row.Attributes.TryGetValue("new_f3diagnosticstate", out var rawState) || rawState is not int state || state is < 0 or > 2 ||
            row.GetAttributeValue<string>("new_f3diagnostic") is not { Length: > 0 and <= 1024 } envelope) throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(envelope, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw Invalid();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name) || property.Name is not ("version" or "incident" or "utc" or "product" or "operation" or "exceptionType")) throw Invalid();
            if (names.Count != 6 || root.GetProperty("version").GetInt32() != 1 ||
                root.GetProperty("product").GetString() != product || root.GetProperty("operation").GetString() != Operation ||
                root.GetProperty("exceptionType").GetString() != typeof(InvalidPluginExecutionException).FullName ||
                !Guid.TryParseExact(root.GetProperty("incident").GetString(), "D", out var incident) || incident == Guid.Empty ||
                !DateTime.TryParseExact(root.GetProperty("utc").GetString(), "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var utc) || utc.Kind != DateTimeKind.Utc) throw Invalid();
            return new Candidate(row.Id, number.ToString(CultureInfo.InvariantCulture), state, envelope, incident, new DateTimeOffset(utc));
        }
        catch (JsonException) { throw Invalid(); }
        catch (KeyNotFoundException) { throw Invalid(); }
        catch (FormatException) { throw Invalid(); }
    }

    /// <summary>領取或確認只寫三個診斷狀態欄位，不更動原封套、業務結果、租戶或名額。</summary>
    private static Entity Transition(Candidate candidate, int state, string? claim, DateTime? until) => new("new_slotrequest", candidate.Id)
    {
        RowVersion = candidate.Revision, ["new_f3diagnosticstate"] = state,
        ["new_f3diagnosticclaim"] = claim, ["new_f3diagnosticuntil"] = until
    };

    /// <summary>每次正式寫入都要求 row version 相等；不能以程序內鎖代替跨程序資料庫互斥。</summary>
    private static Task<OrganizationResponse> UpdateAsync(IOrganizationServiceAsync2 service, Entity target, CancellationToken cancellationToken) =>
        service.ExecuteAsync(new UpdateRequest { Target = target, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches }, cancellationToken);

    /// <summary>固定訊息不透露封套原文或其他產品資料；錯誤型別仍由共用診斷記錄。</summary>
    private static InvalidOperationException Invalid() => new("F3 persisted diagnostic boundary is invalid.");

    /// <summary>一輪至多十六筆純值，方法返回即釋放；不持有 SDK、例外、租戶或憑證。</summary>
    private sealed record Candidate(Guid Id, string Revision, int State, string Envelope, Guid Incident, DateTimeOffset Utc);
}

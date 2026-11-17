/*
檔案整體說明
目的與定位：P7 MemberNotifyRelay唯一Host接線；缺省停用，只認領第四種待辦，不覆蓋付款／QR服務。
主要流程：啟動時嚴格讀布林開關；每次store操作自有scope，handler每筆自有async scope。
以同SDK的server組織metadata選TokenKey，沿WebhookScopeResolver共用讀取／清理契約；
缺key失敗，不推播。實際通知委派Core重讀來源及組字，HTTP使用無cookie／redirect有界client。
資源與隔離：singleton只存scope factory；每筆client/key/SDK活至ExecuteAsync排空，finally清零key、
釋放client，lease再Dispose scope。沒有static可變資料、cache、timer或脫離owner的Task。
真正失敗交既有worker告警；可重試錯誤保留真inner及狀態，無回應用明確transport分類，不捏造HTTP。
驗證方式：離線開關、only-type、缺key、A/B/A及停止中傳輸測試；實機另以P7Live執行。
*/
using LineMessagingProcessor.AspNetCore.Webhooks.Dispatch;
using LineMessagingProcessor.Core;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using SpeechMessage.Hosting.Outbound;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using ToolUtilityNameSpace.Outbound;

namespace LineMessagingProcessor.AspNetCore.Webhooks;

/// <summary>代送註冊與既有Webhook組合共用入口，未啟用不建立SDK或HTTP。</summary>
public static partial class WebhookServiceRegistration
{
    /// <summary>只接受純量bool；一個獨立worker，不變更付款／QR共用介面註冊。</summary>
    private static void RegisterMemberNotifyRelay(IServiceCollection services, IConfiguration configuration)
    {
        var flag = configuration.GetSection("Messaging:NotifyRelay:Enabled");
        if (flag.GetChildren().Any() || flag.Value is not null && !bool.TryParse(flag.Value, out _))
            throw new InvalidOperationException("Invalid member notification relay enabled configuration.");
        if (!bool.TryParse(flag.Value, out var enabled) || !enabled) return;
        if (services.Any(x => x.ServiceType == typeof(MemberNotifyRelayHostFactory))) throw new InvalidOperationException("Relay worker requires a single owner.");
        services.AddHttpClient("MemberNotifyRelay", client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, MaxConnectionsPerServer = 16 });
        services.AddSingleton<MemberNotifyRelayWorkStore>(); services.AddSingleton<MemberNotifyRelayHostFactory>();
        services.AddScoped<MemberNotifyRelayHandler>();
        services.AddSingleton<IHostedService>(provider => new OutboundTaskWorker(
            provider.GetRequiredService<MemberNotifyRelayWorkStore>(), provider.GetRequiredService<MemberNotifyRelayHostFactory>()));
    }
}

/// <summary>固定第四類的store，singleton只借scope factory，每次同步SDK排空再關閉scope。</summary>
public sealed class MemberNotifyRelayWorkStore(IServiceScopeFactory scopes) : IOutboundWorkStore
{
    /// <summary>最多五十筆候選，在SDK查詢端即限制第四類，結果為detached純值。</summary>
    public IReadOnlyList<OutboundWorkItem> ReadCandidates(DateTime now, int maximum, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); using var store = Open(scope.ServiceProvider);
        return store.ReadCandidates(now, maximum, ct).Select(ToWork).ToArray();
    }
    /// <summary>認領只接受第四類，以同scope完成CAS，不能保留SDK到下一次方法。</summary>
    public OutboundWorkItem? TryClaim(OutboundWorkItem item, DateTime now, CancellationToken ct)
    {
        RequireType(item); using var scope = scopes.CreateScope(); using var store = Open(scope.ServiceProvider);
        var claim = store.TryClaim(ToRecord(item), now, ct);
        return claim is null ? null : ToWork(claim);
    }
    /// <summary>本owner版本結算；同步SDK成功／失敗均先排空再釋放scope。</summary>
    public bool TrySettle(OutboundWorkItem item, OutboundWorkStatus status, int attempts, DateTime now, DateTime? next, string? error, CancellationToken ct)
    {
        RequireType(item); using var scope = scopes.CreateScope(); using var store = Open(scope.ServiceProvider);
        return store.TrySettle(ToRecord(item), (OutboundTaskStatus)status, attempts, now, next, error, ct);
    }
    /// <summary>只借目前scope已授權SDK，固定allowlist不來自payload。</summary>
    private static D365OutboundTaskStore Open(IServiceProvider provider) => new(provider.GetRequiredService<IOrganizationService>(), [OutboundTaskType.MemberNotifyRelay]);
    /// <summary>Core已驗證快照轉成Hosting純值，不保留SDK Entity或資源。</summary>
    private static OutboundWorkItem ToWork(OutboundTaskRecord row) => new(row.Id, row.RowVersion, row.ChurchId, row.ContactId,
        OutboundWorkType.MemberNotifyRelay, row.PayloadJson, row.IdempotencyKey, (OutboundWorkStatus)row.Status, row.Attempts, row.NextAttemptOn, row.ClaimedOn);
    /// <summary>固定第四類映射，Core constructor再次驗證版本及事件鍵。</summary>
    private static OutboundTaskRecord ToRecord(OutboundWorkItem row) => new(row.Id, row.RowVersion, row.ChurchId, row.ContactId,
        OutboundTaskType.MemberNotifyRelay, row.PayloadJson, row.IdempotencyKey, (OutboundTaskStatus)row.Status, row.Attempts, row.NextAttemptOn, row.ClaimedOn);
    /// <summary>store與factory各自核對type，避免程序內錯誤分派認領其他工作。</summary>
    internal static void RequireType(OutboundWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Type != OutboundWorkType.MemberNotifyRelay) throw new InvalidOperationException("Invalid member notification relay work type.");
    }
}

/// <summary>singleton只持scope factory，工作及SDK不進全域欄位。</summary>
public sealed class MemberNotifyRelayHostFactory(IServiceScopeFactory scopes) : IOutboundTaskHandlerFactory
{
    /// <summary>每筆建立新的async scope；失敗立即釋放，成功把唯一所有權交lease。</summary>
    public async ValueTask<IOutboundTaskHandlerScope> OpenAsync(OutboundWorkItem item, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); MemberNotifyRelayWorkStore.RequireType(item);
        var scope = scopes.CreateAsyncScope();
        try
        {
            var handler = scope.ServiceProvider.GetRequiredService<MemberNotifyRelayHandler>();
            handler.Bind(item); ct.ThrowIfCancellationRequested(); return new RelayScope(scope, handler);
        }
        catch { await scope.DisposeAsync().ConfigureAwait(false); throw; }
    }
    /// <summary>每筆lease唯一擁有scope；worker先await處理完成再Dispose，不允許跨工作借用handler。</summary>
    private sealed class RelayScope(AsyncServiceScope scope, IOutboundTaskHandler handler) : IOutboundTaskHandlerScope
    {
        private IAsyncDisposable? owner = scope; private IOutboundTaskHandler? handler = handler;
        /// <summary>scope釋放後拒絕取得handler，避免把SDK延長到下一筆。</summary>
        public IOutboundTaskHandler Handler => Volatile.Read(ref handler) ?? throw new ObjectDisposedException(nameof(RelayScope));
        /// <summary>先解除handler，再確定性釋放scope，重複Dispose不重複關閉。</summary>
        public async ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref handler, null); var value = Interlocked.Exchange(ref owner, null);
            if (value is not null) await value.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>只存在單筆scope的代送handler；key/client僅屬本次Execute，完成後清理，不保存在欄位。</summary>
public sealed class MemberNotifyRelayHandler(IOrganizationService sdk, IConfiguration configuration, IHttpClientFactory clients) : IOutboundTaskHandler, IDisposable
{
    private OutboundWorkItem? item; private int started;
    /// <summary>只允許factory綁定一次；payload未被當授權，Core執行時仍重驗來源。</summary>
    internal void Bind(OutboundWorkItem value)
    {
        MemberNotifyRelayWorkStore.RequireType(value);
        if (Interlocked.CompareExchange(ref item, value, null) is not null) throw new InvalidOperationException("Relay handler already owns a task.");
    }
    /// <summary>server組織名稱決定key路徑；Core重驗church後才解密，原故障保留給worker終端告警。</summary>
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var current = Volatile.Read(ref item) ?? throw new ObjectDisposedException(nameof(MemberNotifyRelayHandler));
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Relay handler can execute only once.");
        ct.ThrowIfCancellationRequested();
        if (sdk.Execute(new RetrieveCurrentOrganizationRequest { AccessType = EndpointAccessType.Default }) is not RetrieveCurrentOrganizationResponse response || response.Detail is null || response.Detail.OrganizationId == Guid.Empty)
            throw new InvalidOperationException("Relay server organization identity is unavailable.");
        ct.ThrowIfCancellationRequested();
        var key = WebhookScopeResolver.ReadOrganizationTokenKey(configuration, response.Detail.UniqueName);
        try
        {
            using var client = clients.CreateClient("MemberNotifyRelay");
            await MemberNotifyRelay.ExecuteAsync(sdk, new LinePushClient(client), current.ChurchId, current.PayloadJson, key, ct).ConfigureAwait(false);
        }
        catch (MemberNotifyRelayDeliveryException exception)
        {
            if (exception.ResponseStatus is { } status) throw new HttpRequestException("Relay delivery requires retry.", exception, status);
            throw new OutboundTransportException(exception);
        }
        finally { Array.Clear(key); Interlocked.Exchange(ref item, null); }
    }
    /// <summary>scope owner在Execute排空後清純值參照，不接管DI所有的SDK／factory或configuration。</summary>
    public void Dispose() => Interlocked.Exchange(ref item, null);
}

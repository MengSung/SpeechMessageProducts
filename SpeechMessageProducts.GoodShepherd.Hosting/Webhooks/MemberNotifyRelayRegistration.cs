/*
檔案整體說明
目的與定位：好牧人產品層的 MemberNotifyRelay 註冊與執行 adapter；缺省停用，只認領 relay 類待辦，不覆蓋付款或 QR worker。
主要流程：啟動時嚴格讀布林開關；每次 store 操作建立短 scope，每筆 handler lease 建立獨立 async scope。handler 先以同一 SDK 的 server 組織 metadata 決定 TokenKey，再交由 GoodShepherd.Core 重讀來源、授權教會及組裝通知；缺 key 時 fail closed，不推播。HTTP 使用無 cookie／redirect 的有界具名 client。
資源與隔離：singleton store／factory 只持有 scope factory；每筆 SDK scope、HTTP client 與 TokenKey 僅活到本次操作排空，key 在 finally 清零，client 由 using 釋放，lease 最後 Dispose scope。沒有 static 可變狀態、跨教會 cache、timer 或脫離 owner 的 Task。
失敗與驗證：可重試錯誤保留原始 inner exception 與固定狀態分類；無回應使用明確 transport 分類，不捏造 HTTP 狀態。離線測試涵蓋開關、only-type、缺 key、A/B/A 與停止中傳輸，不能視為 P7Live 實機送達驗收。
*/
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.Core;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using SpeechMessage.Hosting.Outbound;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Hosting.Webhooks;

/// <summary>好牧人產品組合根的代送註冊部分；未啟用時不建立 relay 專用 worker 或 HTTP client。</summary>
public static partial class GoodShepherdWebhookRegistration
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

/// <summary>
/// 固定只操作 MemberNotifyRelay 類待辦的 store。singleton 僅借用 scope factory；
/// 每個同步 SDK 操作完成後即關閉其獨立 scope，不保留跨教會或跨呼叫狀態。
/// </summary>
/// <param name="scopes">由 Host 擁有的 scope factory；本 store 不取得或保存 scoped SDK。</param>
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

/// <summary>每筆 relay 工作建立獨立 async scope 的 factory，不把工作或 SDK 放入 singleton 欄位。</summary>
/// <param name="scopes">由 Host 擁有的 scope factory；每個成功 lease 接管一個新 scope。</param>
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

/// <summary>
/// 只在單筆 scope 內執行的 relay handler；server 組織決定 key 的讀取範圍，
/// 每次執行只使用一個已綁定工作，結束時清除工作參照與金鑰。
/// </summary>
/// <param name="sdk">由目前工作 scope 擁有的 Dataverse SDK；handler 不 Dispose 外借 SDK。</param>
/// <param name="configuration">Host 擁有的部署設定，只用已核對 server 組織名稱讀取 key。</param>
/// <param name="clients">Host 擁有的 HTTP client factory；每次執行建立並釋放獨立 client。</param>
/// <param name="catalog">可選的文字 catalog 借用者，不保存跨工作輸入。</param>
public sealed class MemberNotifyRelayHandler(IOrganizationService sdk, IConfiguration configuration,
    IHttpClientFactory clients, ITextCatalog? catalog = null) : IOutboundTaskHandler, IDisposable
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
            await MemberNotifyRelay.ExecuteAsync(sdk, new LinePushClient(client), current.ChurchId,
                current.PayloadJson, key, ct, catalog, catalog is null ? "GoodShepherd" : "LineWebhook",
                response.Detail.UniqueName).ConfigureAwait(false);
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

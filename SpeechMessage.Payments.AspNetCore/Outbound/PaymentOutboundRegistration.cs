/*
 * 檔案整體說明
 *
 * 目的與定位：將只處理永豐撥款工作的 Hosting worker、D365 工作 store 與 handler factory 組合於
 * Payments ASP.NET Core integration 層；LineWebhook 只需依 Payment 設定選擇啟用與否。
 *
 * 主要功能與流程：AddSpeechMessagePaymentOutbound 僅在 Payment:Outbound:Enabled 為 true 時，
 * 發布 singleton store/factory 及 OutboundTaskWorker。每次 store 查詢、CAS 認領或結算都建立
 * operation-local DI scope，取得 IOrganizationService 後同步完成該次 D365 操作並依序釋放 adapter
 * 與 scope；每筆 handler factory open 另建立獨立 async scope，將其持有至 lease Dispose。
 * D365 查詢 allowlist 固定為 AllotQuery，避免 worker 認領其他產品類型。
 *
 * 使用邊界與維護：隔離、生命週期與失敗處理：singleton 只保存 root IServiceScopeFactory，不保存 SDK、教會、
 * 待辦、profile、credential 或 request。Store 的操作 scope 在方法返回／例外離開時同步釋放；
 * handler scope 由 worker await using 負責停止後 drain 及確定性 Dispose，factory 建構失敗時
 * 立即釋放候選 scope 並原樣傳遞例外。未啟用或缺少 Payment 區段時不註冊 worker/store/factory，
 * 不建立 D365 scope。離線 host 測試驗證 disabled、100 次 scope 回基線及缺省設定組建，不代表實機。
 */
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Hosting.Outbound;
using SpeechMessage.Localization;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.Workflows.Allot;
using ToolUtilityNameSpace.Outbound;

namespace SpeechMessage.Payments.AspNetCore.Outbound;

/// <summary>以 Payment 設定開關註冊受限於 AllotQuery 的 worker 與兩種明確 scope owner。</summary>
public static class PaymentOutboundRegistration
{
    /// <summary>
    /// 僅當 `Payment:Outbound:Enabled` 明確為 true 時註冊對外待辦處理。
    /// false 或缺少鍵值時不發布 worker、store 或 handler factory，也不建立任何 D365 scope。
    /// </summary>
    /// <param name="services">由 Web Host 組合根擁有的 service collection。</param>
    /// <param name="configuration">呼叫端提供的完整 Host configuration；本方法只讀取啟用開關。</param>
    /// <returns>原 service collection；未啟用時不加入任何付款 outbound 服務。</returns>
    /// <exception cref="ArgumentNullException">必要參數為 null。</exception>
    /// <exception cref="InvalidOperationException">開關存在但不是合法 Boolean 時拒絕模糊啟動。</exception>
    public static IServiceCollection AddSpeechMessagePaymentOutbound(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabledValue = configuration["Payment:Outbound:Enabled"];
        if (enabledValue is null)
        {
            return services;
        }

        if (!bool.TryParse(enabledValue, out var enabled))
        {
            throw new InvalidOperationException("Payment:Outbound:Enabled must be a Boolean.");
        }

        if (!enabled)
        {
            return services;
        }

        services.AddSingleton<IOutboundWorkStore, PaymentOutboundWorkStore>();
        services.AddSingleton<IOutboundTaskHandlerFactory, PaymentOutboundHandlerFactory>();
        services.AddHostedService<OutboundTaskWorker>();
        return services;
    }
}

/// <summary>
/// Hosting store 的 operation scope 轉接器。每個同步 store method 自行建立一個暫時 DI scope，
/// 取得授權產品組合根註冊的 SDK，完成單次操作後先釋放 adapter 再釋放 scope。
/// </summary>
internal sealed class PaymentOutboundWorkStore : IOutboundWorkStore
{
    private static readonly OutboundWorkType[] AllowedTypes = { OutboundWorkType.AllotQuery };
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>僅持有容器層級 scope factory，不捕獲 scoped SDK 或任何教會／工作狀態。</summary>
    /// <param name="scopeFactory">Host root provider 擁有的 scope factory。</param>
    public PaymentOutboundWorkStore(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    /// <summary>只讀取 AllotQuery 類型候選；D365 I/O 與 SDK 生命週期限於本方法的一個 scope。</summary>
    public IReadOnlyList<OutboundWorkItem> ReadCandidates(
        DateTime utcNow,
        int maximum,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        using var adapter = CreateAdapter(scope.ServiceProvider);
        return adapter.ReadCandidates(utcNow, maximum, cancellationToken);
    }

    /// <summary>以 D365 CAS 認領候選；同步 I/O 完成或失敗後即釋放本次專屬 scope。</summary>
    public OutboundWorkItem? TryClaim(
        OutboundWorkItem candidate,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        using var adapter = CreateAdapter(scope.ServiceProvider);
        return adapter.TryClaim(candidate, utcNow, cancellationToken);
    }

    /// <summary>以認領版本結算工作；不在不同 store 操作間保存 SDK 或 row state。</summary>
    public bool TrySettle(
        OutboundWorkItem claim,
        OutboundWorkStatus status,
        int attempts,
        DateTime utcNow,
        DateTime? nextAttemptOn,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        using var adapter = CreateAdapter(scope.ServiceProvider);
        return adapter.TrySettle(
            claim,
            status,
            attempts,
            utcNow,
            nextAttemptOn,
            errorCode,
            cancellationToken);
    }

    /// <summary>建立只允許 AllotQuery 的 Core adapter；它只借用目前 operation scope 的 SDK。</summary>
    private static D365OutboundWorkStoreAdapter CreateAdapter(IServiceProvider scopedProvider) =>
        new(scopedProvider.GetRequiredService<IOrganizationService>(), AllowedTypes);
}

/// <summary>為每筆待辦建立獨立 DI scope；singleton factory 本身不持有可變使用者或教會狀態。</summary>
internal sealed class PaymentOutboundHandlerFactory : IOutboundTaskHandlerFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>只保存 Host root scope factory，不解析或捕獲 scoped 服務。</summary>
    /// <param name="scopeFactory">Host root provider 擁有的 scope factory。</param>
    public PaymentOutboundHandlerFactory(IServiceScopeFactory scopeFactory) =>
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

    /// <summary>
    /// 建立目前工作專屬 async scope 與 handler lease。成功時 scope 由回傳 lease 唯一擁有；
    /// 建構或取消失敗時，factory 立即釋放尚未移交的 scope。
    /// </summary>
    /// <param name="item">worker 已從授權 store 讀取並成功 CAS 認領的單筆 detached work item。</param>
    /// <param name="cancellationToken">worker 的本工作取消權杖。</param>
    /// <returns>持有本工作 scoped SDK 的 handler lease，worker 必須 await DisposeAsync。</returns>
    public async ValueTask<IOutboundTaskHandlerScope> OpenAsync(
        OutboundWorkItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Type != OutboundWorkType.AllotQuery)
        {
            throw new InvalidOperationException("Payment outbound handler type is invalid.");
        }

        var scope = _scopeFactory.CreateAsyncScope();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scopedProvider = scope.ServiceProvider;
            var sdk = scopedProvider.GetRequiredService<IOrganizationService>();
            var gateway = scopedProvider.GetRequiredService<IPaymentAllotGateway>();
            var clock = scopedProvider.GetRequiredService<IChurchClock>();
            var handler = new AllotQueryHandler(item, sdk, gateway, clock);
            cancellationToken.ThrowIfCancellationRequested();
            return new PaymentHandlerScope(scope, handler);
        }
        catch
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>單筆 handler scope lease；Dispose 後清除 handler 參照並且只釋放一次底層 async DI scope。</summary>
internal sealed class PaymentHandlerScope : IOutboundTaskHandlerScope
{
    private IAsyncDisposable? _scopeOwner;
    private IOutboundTaskHandler? _handler;

    /// <summary>將 factory 新建的 scope 接為唯一 owner；handler 與 scope 同生共死。</summary>
    internal PaymentHandlerScope(AsyncServiceScope scope, IOutboundTaskHandler handler)
    {
        _scopeOwner = scope;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>取得尚未釋放的工作專屬 handler；關閉後拒絕再次取得，以免延長 scoped 資源生命。</summary>
    public IOutboundTaskHandler Handler =>
        Volatile.Read(ref _handler)
        ?? throw new ObjectDisposedException(nameof(PaymentHandlerScope));

    /// <summary>先解除 handler 參照，再釋放唯一 scope owner；重複 Dispose 不重複關閉 DI scope。</summary>
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _handler, null);
        var owner = Interlocked.Exchange(ref _scopeOwner, null);
        if (owner is not null)
        {
            await owner.DisposeAsync().ConfigureAwait(false);
        }
    }
}

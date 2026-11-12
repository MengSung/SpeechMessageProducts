/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P4-03 LineWebhook 對外撥款 worker 的開關、handler lease 資源所有權及產品主機組合。
 * 測試不啟動完整 LineWebhook、不連接 D365／永豐／LINE，也不讀取 User Secrets。
 *
 * 主要功能與流程：直接檢查 disabled 設定不發布 outbound 服務；啟用時透過真實 DI 容器解析 handler
 * factory，反覆建立並釋放 100 個單筆工作 scope；最後以 Program.BuildApplication 建立不含 Payment
 * 區段的真實 WebApplication，確認原有主機仍可組建且未註冊付款 worker。
 *
 * 使用邊界與維護：隔離、資源與失敗驗證：每個案例使用自己的組態、ServiceProvider、計數 scoped SDK proxy 與 host；
 * scope 唯一擁有 scoped 探針，handler lease Dispose 後須令建立／釋放數相等且 active 回到 0。
 * 測試替身不含憑證或外部連線，host 僅 Build、不 Start、不綁定本機埠；離線成功不代表沙箱實機驗收。
 */
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Hosting.Outbound;
using SpeechMessage.Localization;
using SpeechMessage.Payments.Abstractions;
using SpeechMessage.Payments.AspNetCore.Outbound;
using SpeechMessage.Payments.Models;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>
/// 鎖定金流 outbound disabled 預設、每筆 handler scope 釋放及 Payment 設定區缺省相容性。
/// 替身僅記錄生命週期計數，確定性斷言 scope 全部回到資源基線。
/// </summary>
public sealed class PaymentOutboundHostTests
{
    /// <summary>未啟用時不註冊 worker、工作儲存或 handler factory，避免意外建立 D365 scope。</summary>
    [Fact]
    public void Worker_Disabled_NotRegistered()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddSpeechMessagePaymentOutbound(configuration);

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IOutboundWorkStore)
            || descriptor.ServiceType == typeof(IOutboundTaskHandlerFactory)
            || descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(OutboundTaskWorker));
    }

    /// <summary>建立並釋放 100 個 handler lease 後，所有 DI scope 及 scoped SDK 探針均回到零。</summary>
    [Fact]
    public async Task Factory_ScopeDisposed_ResourcesReturnToBaseline()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:Outbound:Enabled"] = "true"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ScopeProbeCounts>();
        services.AddSingleton<IPaymentAllotGateway>(new UnusedAllotGateway());
        services.AddSingleton<IChurchClock>(new UnusedChurchClock());
        services.AddScoped<ScopeProbe>();
        services.AddScoped<IOrganizationService>(provider => provider.GetRequiredService<ScopeProbe>().Service);
        services.AddSpeechMessagePaymentOutbound(configuration);

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var factory = provider.GetRequiredService<IOutboundTaskHandlerFactory>();
        var item = CreateItem();

        for (var index = 0; index < 100; index++)
        {
            await using var scope = await factory.OpenAsync(item, CancellationToken.None);
            Assert.NotNull(scope.Handler);
        }

        var counts = provider.GetRequiredService<ScopeProbeCounts>();
        Assert.Equal(100, counts.Created);
        Assert.Equal(100, counts.Disposed);
        Assert.Equal(0, counts.Active);
    }

    /// <summary>缺少 Payment 區段時仍可透過產品 Program.BuildApplication 組建，且不發布 outbound worker。</summary>
    [Fact]
    public async Task Host_WithoutPaymentSection_StillStarts()
    {
        await using var host = new WebhookTestHost();

        Assert.NotNull(host.App);
        Assert.Null(host.App.Services.GetService<IOutboundWorkStore>());
        Assert.DoesNotContain(
            host.App.Services.GetServices<IHostedService>(),
            service => service is OutboundTaskWorker);
    }

    private static OutboundWorkItem CreateItem()
    {
        var churchId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var feeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        return new OutboundWorkItem(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "1",
            churchId,
            null,
            OutboundWorkType.AllotQuery,
            $"{{\"feeId\":\"{feeId:D}\"}}",
            $"{churchId:N}-{(int)OutboundWorkType.AllotQuery}-{feeId:N}",
            OutboundWorkStatus.Processing,
            0,
            null,
            new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc));
    }

    /// <summary>由 DI 建立的一筆 scoped owner；只在 ScopeProbeCounts 記錄有界生命週期，不提供 SDK 功能。</summary>
    public sealed class ScopeProbe : IDisposable
    {
        private readonly ScopeProbeCounts _counts;

        /// <summary>scope 建立探針並建立只接受無關 SDK 呼叫的合成代理。</summary>
        public ScopeProbe(ScopeProbeCounts counts)
        {
            _counts = counts;
            counts.Opened();
            Service = DispatchProxy.Create<IOrganizationService, OrganizationServiceProxy>();
        }

        /// <summary>提供給測試 handler 建構的合成 SDK 介面；ExecuteAsync 不會被呼叫。</summary>
        public IOrganizationService Service { get; }

        /// <summary>由 DI scope 確定性釋放時扣回 active 數。</summary>
        public void Dispose() => _counts.Closed();
    }

    /// <summary>Host 級有限生命週期計數器；只保存三個原子整數，不保存任何教會或工作資料。</summary>
    public sealed class ScopeProbeCounts
    {
        private int _created;
        private int _disposed;
        private int _active;

        /// <summary>取得測試期間建立的 scoped 探針數。</summary>
        public int Created => Volatile.Read(ref _created);
        /// <summary>取得已釋放的 scoped 探針數。</summary>
        public int Disposed => Volatile.Read(ref _disposed);
        /// <summary>取得目前存活的 scoped 探針數。</summary>
        public int Active => Volatile.Read(ref _active);

        /// <summary>記錄單一測試 scope 建立事件。</summary>
        public void Opened()
        {
            Interlocked.Increment(ref _created);
            Interlocked.Increment(ref _active);
        }

        /// <summary>記錄單一測試 scope 釋放事件。</summary>
        public void Closed()
        {
            Interlocked.Increment(ref _disposed);
            Interlocked.Decrement(ref _active);
        }
    }

    /// <summary>不執行外部呼叫的 SDK proxy；若 factory 之外有程式誤執行 handler，立即失敗。</summary>
    public class OrganizationServiceProxy : DispatchProxy
    {
        /// <summary>所有未預期的 SDK 操作都拒絕，不讓測試默默連線。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected SDK operation: {targetMethod?.Name ?? "unknown"}.");
    }

    /// <summary>handler 建構只需要此不執行的 gateway；本測試不呼叫永豐。</summary>
    private sealed class UnusedAllotGateway : IPaymentAllotGateway
    {
        public Task<PaymentAllotQueryResult> QueryAllotAsync(
            PaymentAllotQueryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The factory lifecycle test must not call a payment gateway.");
    }

    /// <summary>不含使用者或教會狀態的空時鐘；測試只建立 handler，不執行日期規則。</summary>
    private sealed class UnusedChurchClock : IChurchClock
    {
        public DateTimeOffset UtcNow => throw new InvalidOperationException("The factory lifecycle test must not read the clock.");
        public DateTime ToChurchTime(DateTime utc, string ianaTimeZoneId) =>
            throw new InvalidOperationException("The factory lifecycle test must not convert time.");
        public DateTime TodayInChurch(string ianaTimeZoneId) =>
            throw new InvalidOperationException("The factory lifecycle test must not read church today.");
        public DateTime ToUtc(LocalDateTimeOfChurch local) =>
            throw new InvalidOperationException("The factory lifecycle test must not convert time.");
    }
}

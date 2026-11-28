/*
檔案整體說明
目的與定位：F11好牧人2.0四項正式就緒probe的產品adapter，將固定部署manager、平台SQL、既有有界queue與固定產品Channel來源接到中立Hosting彙總。
主要功能與流程：D365取得短期lease驗WhoAmI三個GUID及已配置組織；Async2使用五秒token，既有OnPremiseClient只在獨占租約內設定剩餘傳輸期限，成功回復原Timeout，故障淘汰。SQL只select 1，不建schema或降級TLS。queue以總保留槽小於容量80%判斷；Channel只讀不含密文metadata，完整檢查產品及唯一正式ID，任一Disabled拒絕。
使用邊界與維護：各probe只在當次請求保存短期資源，原manager及診斷由Host擁有。同步pool建立／驗證無可強殺API，五秒到期後必須等其真正返回才淘汰及歸還，不用背景Task或提前釋放造成重用；故此傳輸五秒期限不宣稱冷連線建立與清理也有五秒硬延遲上限。SQL connection／command與Channel lease逐次確定Dispose；Channel讀取與清理雙故障保留兩個真實原因交上層診斷，單故障保留原始堆疊；不快取授權、query或結果，不讀舊產品Channel秘密、不對LINE發API、不建立額外worker。真故障保留原Exception交彙總告警，對外名稱固定。
*/
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Queue;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.PowerPlatform.Dataverse.Client;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Hosting.Observability;
using SpeechMessageProducts.GoodShepherd2.Web.Channels;
using ToolUtilityNameSpace.Channels;
using ToolUtilityNameSpace.Dataverse;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessageProducts.GoodShepherd2.Web.Health;

/// <summary>四項固定部署probe的組合入口；配置缺席不作外部查詢或冒稱健康。</summary>
internal static class GoodShepherd2Readiness
{
    /// <summary>Build前建立純服務描述子，不開SQL或CRM；既有合成probe可由測試明確注入。</summary>
    internal static void AddGoodShepherd2Readiness(WebApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton<HostingMeters>();
        var connection = builder.Configuration.GetConnectionString("Platform");
        builder.Services.TryAddSingleton(provider => new ReadinessChecks(
            ct => CheckD365Async(provider.GetRequiredService<IDataverseConnectionManager>(),
                provider.GetRequiredService<ExceptionDiagnostics>(),
                provider.GetService<GoodShepherd2ChannelConfiguration>()?.OrganizationId ?? Guid.Empty, ct),
            ct => CheckSqlAsync(connection, ct),
            ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(provider.GetRequiredService<IWebhookEventQueue>() is BoundedWebhookEventQueue queue &&
                    (long)queue.GetSnapshot().Retained * 5 < (long)provider.GetRequiredService<WebhookQueueOptions>().Capacity * 4);
            },
            ct => CheckChannelsAsync(provider.GetService<IChannelDirectorySource>(), ct),
            provider.GetRequiredService<ExceptionDiagnostics>()));
    }

    /// <summary>同一次短命租約涵蓋身分驗證、傳輸期限及完整清理；不確定或逾時一律淘汰，不讓其他請求重用。</summary>
    internal static async Task<bool> CheckD365Async(IDataverseConnectionManager manager, ExceptionDiagnostics diagnostics,
        Guid expectedOrganization, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        IClientLease? lease = null;
        Exception? primary = null;
        try
        {
            lease = manager.Acquire(ct);
            ct.ThrowIfCancellationRequested();
            Microsoft.Xrm.Sdk.OrganizationResponse response;
            if (lease.Service is IOrganizationServiceAsync2 asynchronous)
                response = await asynchronous.ExecuteAsync(new WhoAmIRequest(), ct).ConfigureAwait(false);
            else if (lease.Service is OnPremiseClient onPremise)
            {
                var remaining = TimeSpan.FromSeconds(5) - Stopwatch.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero) throw new TimeoutException("Readiness acquisition exceeded its deadline.");
                var previous = onPremise.Timeout;
                onPremise.Timeout = remaining;
                response = onPremise.Execute(new WhoAmIRequest());
                ct.ThrowIfCancellationRequested();
                onPremise.Timeout = previous;
            }
            else throw new InvalidOperationException("Readiness requires a bounded Dataverse transport.");
            ct.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(5))
                throw new TimeoutException("Readiness Dataverse deadline elapsed.");
            if (response is not WhoAmIResponse identity || identity.UserId == Guid.Empty ||
                identity.BusinessUnitId == Guid.Empty || identity.OrganizationId == Guid.Empty ||
                (expectedOrganization != Guid.Empty && identity.OrganizationId != expectedOrganization))
                throw new InvalidOperationException("Readiness Dataverse identity boundary is invalid.");
        }
        catch (Exception exception)
        {
            primary = exception;
            if (lease is not null)
            {
                try { lease.MarkFaulted(); }
                catch (Exception failure) { diagnostics.Report(failure, "GoodShepherd2.Readiness.Fault"); }
            }
        }
        finally
        {
            try { lease?.Dispose(); }
            catch (Exception failure)
            {
                diagnostics.Report(failure, "GoodShepherd2.Readiness.Release");
                primary ??= failure;
            }
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        return true;
    }

    /// <summary>只借既有平台連線池執行select 1；失敗清理該pool的不確定連線，不建立表或改TLS選項。</summary>
    internal static async Task<bool> CheckSqlAsync(string? connectionString, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        await using var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "select 1";
            command.CommandTimeout = 5;
            return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is int value && value == 1;
        }
        catch { SqlConnection.ClearPool(connection); throw; }
    }

    /// <summary>每次重新讀metadata，檢查本產品全部正式ID及狀態；不取憑證、不呼叫LINE、不把重複列靜默去除。</summary>
    internal static async Task<bool> CheckChannelsAsync(IChannelDirectorySource? source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (source is null) return false;
        if (source.Product != GoodShepherd2Product.Name || source.OrganizationId == Guid.Empty)
            throw new InvalidOperationException("Readiness channel source boundary is invalid.");
        var lease = await source.OpenAsync(ct).ConfigureAwait(false);
        Exception? primary = null;
        var healthy = false;
        try
        {
            var rows = await lease.ReadAsync(false, ct).ConfigureAwait(false);
            if (rows.Count > 256) throw new InvalidOperationException("Readiness channel capacity exceeded.");
            var ids = new HashSet<Guid>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            healthy = rows.Count != 0;
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                if (row is null || row.Id == Guid.Empty || row.Product != GoodShepherd2Product.Name ||
                    !ids.Add(row.Id) || string.IsNullOrEmpty(row.ChannelKey) || !keys.Add(row.ChannelKey) ||
                    !Enum.IsDefined(row.Status))
                    throw new InvalidOperationException("Readiness channel metadata identity is invalid.");
                if (row.Status == DirectoryChannelStatus.Disabled) healthy = false;
            }
        }
        catch (Exception exception) { primary = exception; }
        try { await lease.DisposeAsync().ConfigureAwait(false); }
        catch (Exception cleanup)
        {
            if (primary is not null) throw new AggregateException("Channel metadata read and release failed.", primary, cleanup);
            throw;
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        return healthy;
    }
}

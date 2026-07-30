using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace ChurchReport.Services;

/// <summary>
/// 在 ChurchReport host StartAsync 階段執行 Gateway WhoAmI preflight，確保正式流量進入前已驗證
/// ProductClient 設定與 Gateway operation pipeline。此服務不擁有 provider、executor、HttpClient 或 handler；
/// 它只借用主 DI singleton <see cref="IDonationDynamicsAccessProcessHost"/> 已擁有的 executor，因此不會建立
/// 第二個連線池。feature flag=false 與 Embedded mode 在解析 process host executor 前即返回，維持嚴格 no-op。
/// Gateway 失敗、內部逾時或無效設定全部 fail-closed 阻止 host ready；caller cancellation 則保留原取消語意。
/// </summary>
public sealed class DynamicsGatewayPreflightHostedService : IHostedService
{
    private const string PreflightWorkloadSubject = "church-report-host-preflight";
    private static readonly TimeSpan DefaultPreflightTimeout = TimeSpan.FromSeconds(15);

    private readonly IConfiguration _configuration;
    private readonly IDonationDynamicsAccessProcessHost _processHost;
    private readonly ILogger<DynamicsGatewayPreflightHostedService> _logger;
    private readonly TimeSpan _preflightTimeout;

    /// <summary>
    /// 建立 production preflight，使用 15 秒 bounded timeout。建構式只保存 DI 參考，不綁定 options、
    /// 不解析 executor、也不建立 cancellation timer；因此 flag=false 的 host 建構與啟動不會取得 HTTP 資源。
    /// </summary>
    /// <param name="configuration">ChurchReport host 設定；只讀 DynamicsAccess 公開產品欄位。</param>
    /// <param name="processHost">主 DI 唯一擁有的 Dynamics process host。</param>
    /// <param name="logger">只記錄固定訊息與例外型別，不記錄 endpoint、alias、credential 或 token。</param>
    public DynamicsGatewayPreflightHostedService(
        IConfiguration configuration,
        IDonationDynamicsAccessProcessHost processHost,
        ILogger<DynamicsGatewayPreflightHostedService> logger)
        : this(configuration, processHost, logger, DefaultPreflightTimeout)
    {
    }

    /// <summary>
    /// 建立具有明確 bounded timeout 的 preflight。此 overload 主要供 deterministic lifecycle 測試縮短等待，
    /// 但仍套用與 production 相同的 linked cancellation、fail-closed 與 cleanup 順序。
    /// </summary>
    /// <param name="configuration">ChurchReport host 設定。</param>
    /// <param name="processHost">主 DI 唯一擁有的 Dynamics process host。</param>
    /// <param name="logger">不回顯敏感設定的 logger。</param>
    /// <param name="preflightTimeout">大於零且不超過一分鐘的啟動探測上限。</param>
    public DynamicsGatewayPreflightHostedService(
        IConfiguration configuration,
        IDonationDynamicsAccessProcessHost processHost,
        ILogger<DynamicsGatewayPreflightHostedService> logger,
        TimeSpan preflightTimeout)
    {
        if (preflightTimeout <= TimeSpan.Zero || preflightTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preflightTimeout),
                "Dynamics Gateway preflight timeout must be greater than zero and at most one minute.");
        }

        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _processHost = processHost ?? throw new ArgumentNullException(nameof(processHost));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preflightTimeout = preflightTimeout;
    }

    /// <summary>
    /// 依 feature flag 與 execution mode 決定是否執行正式 WhoAmI pipeline。
    /// flag=false 先返回，保證不綁定 options、不解析 executor、不建立 provider／HttpClient／timer；Gateway mode
    /// 才向 process host 取得 executor，讓正式 options validator 在 StartAsync 內 fail-closed。探測使用 caller
    /// token 與 bounded timeout 的 linked token：host shutdown 取消保持 <see cref="OperationCanceledException"/>，
    /// 只有內部期限先到才轉成 <see cref="TimeoutException"/>。任何上游失敗只丟固定消毒訊息，不回顯內容。
    /// </summary>
    /// <param name="cancellationToken">Generic Host 的啟動／關機取消訊號。</param>
    /// <returns>只有正式 WhoAmI 成功或此分支為嚴格 no-op 時才完成。</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!DonationDynamicsAccessBootstrap.IsPackage01Enabled(_configuration))
        {
            return;
        }

        var options = DonationDynamicsAccessBootstrap.BindOptions(_configuration);
        if (options.ExecutionMode != DynamicsExecutionMode.Gateway)
        {
            return;
        }

        // process host 是 provider／HttpClient 的唯一 owner；preflight 只能借用同一 executor，不能自行 new client。
        // executor 解析會觸發正式 Gateway options validation，因此設定不完整會在 host StartAsync 立即失敗。
        var executor = _processHost.GetOrCreateGatewayExecutor(options);
        using var timeout = new CancellationTokenSource(_preflightTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        OperationExecutionResult result;
        try
        {
            result = await executor.ExecuteAsync(
                new OperationExecutionRequest
                {
                    ProfileAlias = options.ProfileAlias,
                    CapabilityOperationId = OperationIds.RuntimeHealthWhoAmI,
                    // 這個必要欄位只是產品內 request model 的固定 host label；Gateway executor 不會序列化它，
                    // 也不會轉成 X-Principal／X-Workload。真正 workload identity 必須由 Gateway server principal 推導。
                    WorkloadSubjectId = PreflightWorkloadSubject,
                    Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
                },
                linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // caller cancellation 擁有較高優先權；不可誤報為 timeout，否則 host shutdown 診斷與重試決策會錯誤。
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            // linked CTS 與兩個來源 CTS 都在方法離開前 Dispose，避免 cancellation registration／timer 留存。
            throw new TimeoutException(
                "Dynamics Gateway WhoAmI preflight exceeded its startup timeout.");
        }
        catch (Exception exception)
        {
            // 不把可能含 endpoint、credential、token 或 upstream body 的原始訊息／inner exception 帶到 host 例外。
            _logger.LogError(
                "Dynamics Gateway WhoAmI preflight threw an unexpected exception. ExceptionType={ExceptionType}",
                exception.GetType().Name);
            throw new InvalidOperationException(
                "Dynamics Gateway WhoAmI preflight failed.");
        }

        if (!result.Succeeded)
        {
            // ErrorMessage 屬於外部 Gateway 回應，不能在 startup exception 或 log 回顯；固定訊息仍清楚表達 fail-closed。
            throw new InvalidOperationException(
                "Dynamics Gateway WhoAmI preflight failed.");
        }
    }

    /// <summary>
    /// preflight 本身沒有長期資源、subscription 或 background task，因此 StopAsync 為 no-op。
    /// executor／provider 的 cleanup 由 <see cref="DonationDynamicsAccessBootstrapLifetime"/> 在較後的 shutdown
    /// 階段唯一協調，避免兩個 hosted service 競爭 Dispose 同一 handler generation。
    /// </summary>
    /// <param name="cancellationToken">未使用；本服務沒有需要取消的背景工作。</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

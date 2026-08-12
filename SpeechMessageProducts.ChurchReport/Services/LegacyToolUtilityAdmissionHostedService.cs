// ============================================================================
// 檔案：ChurchReport/Services/LegacyToolUtilityAdmissionHostedService.cs
// 目的：由 Generic Host 擁有 legacy ToolUtility admission controller 的停止接收與排空生命週期。
//
// 此 hosted service 不建立 CRM client、SQL coordinator、HTTP handler、queue 或背景 retry，
// 也不會啟用 Package01 flag。它只在 host stop 時停止新的「已註冊」legacy ingress，等待
// controller lease 回到基線；未註冊的 ToolUtility 呼叫與跨 host aggregate capacity 仍須由
// deployment-owned runbook 證明，不能由本機服務宣稱已完成。
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace ChurchReport.Services;

/// <summary>
/// 管理 legacy admission controller 的 host lifecycle；controller 本身由主 DI singleton 唯一擁有。
/// </summary>
public sealed class LegacyToolUtilityAdmissionHostedService : IHostedService
{
    private static readonly TimeSpan StopDrainTimeout = TimeSpan.FromSeconds(30);
    private readonly LegacyToolUtilityDrainController _controller;
    private int _stopped;

    /// <summary>建立不會解析外部資源的 lifecycle owner。</summary>
    /// <param name="controller">主 DI singleton 的本機受控 legacy admission controller。</param>
    public LegacyToolUtilityAdmissionHostedService(LegacyToolUtilityDrainController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    /// <summary>
    /// 啟動不需要建立背景工作；controller 預設已允許受控 legacy ingress。
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 先停止新的受控 legacy intake，再等待既有 lease 確定釋放。
    /// timeout、unknown active work 或 cancellation 都不能被當作成功 cleanup。
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        var result = await _controller.StopIntakeAndDrainAsync(
                StopDrainTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        if (result != LegacyToolUtilityDrainResult.Drained &&
            result != LegacyToolUtilityDrainResult.Disposed)
        {
            throw new InvalidOperationException(
                $"Legacy ToolUtility admission cleanup returned {result}.");
        }
    }
}

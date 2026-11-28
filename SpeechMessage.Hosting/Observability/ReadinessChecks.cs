/*
檔案整體說明
目的與定位：F11跨產品四項就緒檢查的中立彙總，不參考任何SQL、Dataverse、LINE或產品組件，僅接組合根已授權的短命probe委派。
主要功能與流程：依序等待D365、SQL、queue及Channel四項，每項各有五秒合作取消期限；只有四項皆true才回空失敗集合。真正例外交既有共用diagnostics，對外只發布固定d365／sql／queue／channels名稱，不回顯例外、識別或連線設定。
使用邊界與維護：probe與diagnostics由Host擁有且不保存request；本次CTS逐項Dispose，不建立Task.Run或以WaitAsync遺棄仍用資源的probe。呼叫端取消原樣傳播，內部期限為真正失敗；probe必須提供真正可取消傳輸並在完成前釋放租約。此owner不能強殺不合作同步SDK，不以此純彙總宣稱其已支援取消。結果只活至單次HTTP回應，沒有共用快取或背景工作。
*/
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Hosting.Observability;

/// <summary>四個固定probe與安全失敗名稱的彙總，正式依賴及資源所有權由各主機adapter負責。</summary>
public sealed class ReadinessChecks
{
    private readonly Func<CancellationToken, Task<bool>>[] checks;
    private readonly ExceptionDiagnostics diagnostics;

    /// <summary>只接受同一部署的四個非null probe；委派不得保存caller、身分或跨租戶狀態。</summary>
    public ReadinessChecks(Func<CancellationToken, Task<bool>> d365, Func<CancellationToken, Task<bool>> sql,
        Func<CancellationToken, Task<bool>> queue, Func<CancellationToken, Task<bool>> channels,
        ExceptionDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(d365);
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(diagnostics);
        checks = [d365, sql, queue, channels];
        this.diagnostics = diagnostics;
    }

    /// <summary>逐項等待完成及清理再發布失敗名稱；不是可提前返回而仍在背景用lease的超時包裝。</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failed = new List<string>(4);
        for (var index = 0; index < checks.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var passed = false;
            try
            {
                // 先保留候選值，期限檢查成功後才發布；不合作probe晚回true仍必須列失敗。
                var candidate = await checks[index](timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                passed = candidate;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                diagnostics.Report(exception, Operation(index), cancellationToken);
            }
            if (!passed) failed.Add(Name(index));
        }
        return failed.AsReadOnly();
    }

    /// <summary>固定封閉名稱，不取例外Message、route、組態或caller字串。</summary>
    private static string Name(int index) => index switch
    { 0 => "d365", 1 => "sql", 2 => "queue", 3 => "channels", _ => throw new ArgumentOutOfRangeException(nameof(index)) };

    /// <summary>固定diagnostic符號，不將任何查詢資料或連線字串放進operation。</summary>
    private static string Operation(int index) => index switch
    {
        0 => "Hosting.Readiness.D365", 1 => "Hosting.Readiness.Sql", 2 => "Hosting.Readiness.Queue",
        3 => "Hosting.Readiness.Channels", _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

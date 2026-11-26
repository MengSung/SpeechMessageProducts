/*
檔案整體說明
目的與定位：本Host專用的Hangfire狀態轉換filter，集中三次有界重試、正常速率延期及最終真實例外告警。
主要功能與流程：失敗的正常業務工作以一、二、四秒重試；第四次仍失敗才交共用diagnostics。速率不足直接Scheduled到下一UTC小時，不加RetryCount；停止取消及副作用不明永久失敗不重送。終態SQL歷史保留七日。
使用邊界與維護：不加入全域filter、不保存任何工作集合或例外快取。原Exception只在本次filter傳遞，Hangfire儲存固定外層訊息而不序列化原Message或InnerException；真正故障仍以原Exception交既有安全入口，正常取消不告警。SQL狀態交易由Hangfire本次狀態機擁有。
*/
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using System.Globalization;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.Diagnostics;

namespace SpeechMessage.Scheduling.Hangfire;

/// <summary>固定三次重試的instance狀態機，沒有跨產品或跨工作可變資料。</summary>
internal sealed class SchedulingStateFilter(IChurchClock clock, ExceptionDiagnostics diagnostics) : IElectStateFilter, IApplyStateFilter
{
    /// <summary>依故障類別選下個狀態；預期延期先處理，不消耗一般錯誤次數。</summary>
    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not FailedState failed) return;
        if (failed.Exception is SchedulingDeferredException deferred)
        {
            context.CandidateState = new ScheduledState(deferred.ResumeAtUtc.UtcDateTime);
            return;
        }
        if (failed.Exception is SchedulingFailureException { Permanent: true })
        {
            context.CandidateState = new FinalFailedState(failed.Exception, clock.UtcNow);
            return;
        }
        var retries = context.GetJobParameter<int>("RetryCount");
        if (retries < 3)
        {
            context.SetJobParameter("RetryCount", retries + 1);
            context.CandidateState = new ScheduledState(clock.UtcNow.AddSeconds(1 << retries).UtcDateTime);
        }
        else context.CandidateState = new FinalFailedState(failed.Exception, clock.UtcNow);
    }

    /// <summary>終態只交真實最終故障；沒有LINE client或額外queue，告警入口維持既有開關與flush順序。</summary>
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is FinalFailedState failed)
        {
            if (failed.Failure is SchedulingFailureException failure)
            {
                if (!failure.NormalCancellation) diagnostics.Report(failure.Original, "Scheduling.Job.Execute");
            }
            else diagnostics.Report(failed.Failure, "Scheduling.Job.Execute");
        }
        context.JobExpirationTimeout = TimeSpan.FromDays(7);
    }

    /// <summary>撤回狀態沒有另建資源或補償副作用；SQL交易仍由呼叫端擁有。</summary>
    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction) { }

    /// <summary>真正final的Failed狀態，使Hangfire狀態機採用七日期限；不沿用原生Failed無限保留政策。</summary>
    private sealed class FinalFailedState(Exception failure, DateTimeOffset failedAtUtc) : IState
    {
        /// <summary>僅供同次Apply交共用入口的原故障，不參與SerializeData。</summary>
        internal Exception Failure { get; } = failure;
        /// <summary>保留標準Failed名稱供查詢與營運判斷。</summary>
        public string Name => FailedState.StateName;
        /// <summary>固定安全原因，沒有payload、scope或原Exception.Message。</summary>
        public string Reason => "The scheduled operation failed; inspect the shared incident log.";
        /// <summary>明確final才能觸發JobExpirationTimeout，不留下無界失敗歷史。</summary>
        public bool IsFinal => true;
        /// <summary>工作載入故障仍須可診斷，不略過未知型別失敗。</summary>
        public bool IgnoreJobLoadException => false;
        /// <summary>只發布固定字串與UTC時刻；原例外由既有安全診斷另行記錄。</summary>
        public Dictionary<string, string> SerializeData() => new(StringComparer.Ordinal)
        {
            ["FailedAt"] = failedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["ExceptionType"] = "SchedulingFailure",
            ["ExceptionMessage"] = Reason,
            ["ExceptionDetails"] = "See shared incident log."
        };
    }
}

/// <summary>只向Hangfire暴露固定安全訊息；原例外私有於目前操作，不以InnerException進持久化錯誤文字。</summary>
internal sealed class SchedulingFailureException(Exception original, bool permanent = false, bool normalCancellation = false)
    : Exception("The scheduled operation failed; inspect the shared incident log.")
{
    /// <summary>供最終filter交既有安全入口，最長只保留本次worker狀態轉換期間。</summary>
    internal Exception Original { get; } = original;
    /// <summary>取消或不明副作用不得自動重送。</summary>
    internal bool Permanent { get; } = permanent;
    /// <summary>明確由執行token引起的正常停止不通知。</summary>
    internal bool NormalCancellation { get; } = normalCancellation;
}

/// <summary>內部純值延期訊號，沒有錯誤業務資料或外部資源。</summary>
internal sealed class SchedulingDeferredException(DateTimeOffset resumeAtUtc) : Exception("Channel rate budget deferred this job.")
{
    /// <summary>下一個明確UTC小時，由同一scoped速率預算計算。</summary>
    internal DateTimeOffset ResumeAtUtc { get; } = resumeAtUtc;
}

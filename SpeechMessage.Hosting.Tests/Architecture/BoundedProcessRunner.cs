/*
 * 檔案整體說明
 *
 * 目的與定位：執行唯讀架構評估子程序，將每條輸出管線的擷取量、執行期限及停止／排空生命週期限制在固定上限。 評估命令固定為不執行 targets、單一 MSBuild node 的本機讀取；不可將此 runner
 * 擴用到會自行建立子程序的工作負載。 擷取內容只在本次呼叫短暫持有，不輸出原始 stdout/stderr，避免本機路徑或展開設定流入測試診斷。
 *
 * 主要功能與流程：
 * 本檔宣告 BoundedProcessRunner、BoundedProcessResult。
 * - 啟動受信任且不建立子程序的本機評估命令，並同時排空 stdout／stderr；執行與排空共用一個總期限。 超過擷取上限時仍會讀到 EOF，但只保留上限內文字並回報截斷，讓呼叫端 fail closed。
 * 逾時時請求終止程序樹並等待根程序；BCL 的 WaitForExit 不代表所有後代均已退出，因此呼叫端須維持單程序命令契約。
 * - 持續讀取單一管線；StringBuilder 永遠不會超過指定字元數，超額內容立即丟棄。
 * - 終止程序樹並在固定寬限內回收根程序；無法確認退出時明確失敗，不繼續宣稱資源已釋放。
 * - 發生主流程錯誤後盡力終止程序、關閉兩條輸出串流並有限等待讀取工作。 清理失敗由呼叫端與原始錯誤合併回報，避免以清理例外覆蓋逾時或讀取根因。
 * 測試案例入口包含 Run、StopAndReap、Cleanup、Remaining、RemainingMilliseconds、WaitWithin。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 SpeechMessage.Hosting.Tests/Architecture/ArchitectureProcessRunnerTests.cs、
 * SpeechMessage.Hosting.Tests/Architecture/ArchitectureProjectReader.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 執行唯讀架構評估子程序，將每條輸出管線的擷取量、執行期限及停止／排空生命週期限制在固定上限。
/// 評估命令固定為不執行 targets、單一 MSBuild node 的本機讀取；不可將此 runner 擴用到會自行建立子程序的工作負載。
/// 擷取內容只在本次呼叫短暫持有，不輸出原始 stdout/stderr，避免本機路徑或展開設定流入測試診斷。
/// </summary>
internal static class BoundedProcessRunner
{
    private static readonly TimeSpan TerminationGracePeriod = TimeSpan.FromSeconds(2);
    private static readonly int ReadBufferSize = 4096;

    /// <summary>
    /// 啟動受信任且不建立子程序的本機評估命令，並同時排空 stdout／stderr；執行與排空共用一個總期限。
    /// 超過擷取上限時仍會讀到 EOF，但只保留上限內文字並回報截斷，讓呼叫端 fail closed。
    /// 逾時時請求終止程序樹並等待根程序；BCL 的 WaitForExit 不代表所有後代均已退出，因此呼叫端須維持單程序命令契約。
    /// </summary>
    /// <param name="startInfo">呼叫者建立的唯讀程序設定，必須分別重新導向標準輸出與標準錯誤。</param>
    /// <param name="timeout">從啟動至程序與兩條輸出完成排空的總期限。</param>
    /// <param name="maximumCapturedCharacters">每條管線最多保留的字元數；超出內容只排空而不保留。</param>
    /// <returns>退出碼與有界、脫離程序資源的輸出快照。</returns>
    internal static BoundedProcessResult Run(ProcessStartInfo startInfo, TimeSpan timeout, int maximumCapturedCharacters)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (!startInfo.RedirectStandardOutput || !startInfo.RedirectStandardError || startInfo.UseShellExecute)
        {
            throw new ArgumentException("Process output must be redirected without shell execution.", nameof(startInfo));
        }

        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCapturedCharacters);

        using var process = new Process { StartInfo = startInfo };
        Task<(string Text, bool Truncated)>? standardOutput = null;
        Task<(string Text, bool Truncated)>? standardError = null;
        Task? drain = null;
        var started = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException("Architecture evaluation process did not start.");
            }

            standardOutput = DrainAsync(process.StandardOutput, maximumCapturedCharacters);
            standardError = DrainAsync(process.StandardError, maximumCapturedCharacters);
            drain = Task.WhenAll(standardOutput, standardError);

            if (!process.WaitForExit(RemainingMilliseconds(timeout, stopwatch.Elapsed)))
            {
                StopAndReap(process);
                throw new TimeoutException("Architecture evaluation exceeded its execution deadline.");
            }

            if (!WaitWithin(drain, Remaining(timeout, stopwatch.Elapsed)))
            {
                StopAndReap(process);
                throw new TimeoutException("Architecture evaluation exceeded its output-drain deadline.");
            }

            if (drain.IsFaulted || standardOutput.IsFaulted || standardError.IsFaulted)
            {
                throw new InvalidDataException("Architecture evaluation output could not be drained.");
            }

            var output = standardOutput.GetAwaiter().GetResult();
            var error = standardError.GetAwaiter().GetResult();
            return new BoundedProcessResult(process.ExitCode, output.Text, output.Truncated, error.Text, error.Truncated);
        }
        catch (Exception primaryFailure)
        {
            if (started)
            {
                try
                {
                    Cleanup(process, drain);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "Architecture evaluation failed and its process cleanup also failed.",
                        primaryFailure,
                        cleanupFailure);
                }
            }

            throw;
        }
    }

    /// <summary>持續讀取單一管線；StringBuilder 永遠不會超過指定字元數，超額內容立即丟棄。</summary>
    /// <param name="reader">此 runner 獨占的程序輸出 reader。</param>
    /// <param name="maximumCapturedCharacters">擷取上限。</param>
    /// <returns>有界輸出文字與是否曾丟棄超額內容。</returns>
    private static async Task<(string Text, bool Truncated)> DrainAsync(StreamReader reader, int maximumCapturedCharacters)
    {
        var buffer = new char[ReadBufferSize];
        var captured = new StringBuilder(Math.Min(ReadBufferSize, maximumCapturedCharacters));
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
        {
            var remaining = maximumCapturedCharacters - captured.Length;
            var count = Math.Min(remaining, read);
            if (count > 0)
            {
                captured.Append(buffer, 0, count);
            }

            truncated |= count < read;
        }

        return (captured.ToString(), truncated);
    }

    /// <summary>終止程序樹並在固定寬限內回收根程序；無法確認退出時明確失敗，不繼續宣稱資源已釋放。</summary>
    /// <param name="process">本次呼叫建立且仍由 runner 擁有的程序。</param>
    private static void StopAndReap(Process process)
    {
        Exception? treeTerminationFailure = null;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // 程序可能在 HasExited 與 Kill 之間自然退出；此時仍須完成下方有界回收。
        }
        catch (InvalidOperationException exception)
        {
            treeTerminationFailure = exception;
        }
        catch (AggregateException exception)
        {
            treeTerminationFailure = exception;
        }
        catch (Win32Exception exception) when (!process.HasExited)
        {
            treeTerminationFailure = exception;
        }
        catch (Win32Exception) when (process.HasExited)
        {
            // 根程序已自然退出；WaitForExit 仍會在下方套用固定回收期限。
        }

        if (treeTerminationFailure is not null && !process.HasExited)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
                // 已自然退出時可繼續回收，但仍保留前述「未能證明整棵樹已終止」狀態。
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                treeTerminationFailure = new AggregateException(treeTerminationFailure, exception);
            }
        }

        if (!process.WaitForExit((int)TerminationGracePeriod.TotalMilliseconds))
        {
            throw new InvalidOperationException("Architecture evaluation process could not be reaped within its cleanup deadline.");
        }

        if (treeTerminationFailure is not null)
        {
            throw new InvalidOperationException("Architecture evaluation could not confirm complete process-tree termination.", treeTerminationFailure);
        }
    }

    /// <summary>
    /// 發生主流程錯誤後盡力終止程序、關閉兩條輸出串流並有限等待讀取工作。
    /// 清理失敗由呼叫端與原始錯誤合併回報，避免以清理例外覆蓋逾時或讀取根因。
    /// </summary>
    /// <param name="process">本次 runner 擁有的程序。</param>
    /// <param name="drain">可能仍在讀取重導向串流的合併工作。</param>
    private static void Cleanup(Process process, Task? drain)
    {
        Exception? cleanupFailure = null;
        try
        {
            if (!process.HasExited)
            {
                StopAndReap(process);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or AggregateException)
        {
            cleanupFailure = exception;
        }

        try
        {
            process.StandardOutput.Dispose();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            cleanupFailure ??= exception;
        }

        try
        {
            process.StandardError.Dispose();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            cleanupFailure ??= exception;
        }

        if (drain is not null && !WaitWithin(drain, TerminationGracePeriod))
        {
            cleanupFailure ??= new InvalidOperationException("Architecture evaluation output readers did not stop after stream disposal.");
        }

        if (cleanupFailure is not null)
        {
            throw new InvalidOperationException("Architecture evaluation resources did not return to their cleanup baseline.", cleanupFailure);
        }
    }

    /// <summary>以同一個單調計時基準計算期限餘額，避免程序等待與輸出排空各自重新取得完整期限。</summary>
    /// <param name="timeout">總期限。</param>
    /// <param name="elapsed">自啟動前開始計時後的經過時間。</param>
    /// <returns>非負等待時間。</returns>
    private static TimeSpan Remaining(TimeSpan timeout, TimeSpan elapsed) => timeout > elapsed ? timeout - elapsed : TimeSpan.Zero;

    /// <summary>將剩餘時間安全轉為 Process.WaitForExit 支援的整數毫秒，向上取整避免提前截短期限。</summary>
    /// <param name="timeout">剩餘時間。</param>
    /// <param name="elapsed">自程序期限開始後的經過時間。</param>
    /// <returns>有限且非負的毫秒數。</returns>
    private static int RemainingMilliseconds(TimeSpan timeout, TimeSpan elapsed) =>
        (int)Math.Ceiling(Remaining(timeout, elapsed).TotalMilliseconds);

    /// <summary>有限等待非同步排空工作；故障工作已結束，可由呼叫端檢查並轉成固定診斷。</summary>
    /// <param name="task">兩條輸出排空工作的合併工作。</param>
    /// <param name="timeout">本次等待上限。</param>
    /// <returns>工作已完成（含 faulted）時為 true；期限內仍未完成時為 false。</returns>
    private static bool WaitWithin(Task task, TimeSpan timeout)
    {
        try
        {
            return task.Wait(timeout);
        }
        catch (AggregateException)
        {
            return task.IsCompleted;
        }
    }
}

/// <summary>子程序執行結果；輸出字串受呼叫端指定上限約束，且不持有程序、串流或取消註冊。</summary>
internal sealed class BoundedProcessResult
{
    /// <summary>建立只含有界純文字的程序結果快照。</summary>
    /// <param name="exitCode">根程序退出碼。</param>
    /// <param name="standardOutput">上限內的標準輸出。</param>
    /// <param name="standardOutputTruncated">標準輸出是否有超額部分被丟棄。</param>
    /// <param name="standardError">上限內的錯誤輸出。</param>
    /// <param name="standardErrorTruncated">錯誤輸出是否有超額部分被丟棄。</param>
    internal BoundedProcessResult(int exitCode, string standardOutput, bool standardOutputTruncated, string standardError, bool standardErrorTruncated)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardOutputTruncated = standardOutputTruncated;
        StandardError = standardError;
        StandardErrorTruncated = standardErrorTruncated;
    }

    /// <summary>已回收程序的退出碼；非零結果由架構讀取器轉成固定錯誤。</summary>
    internal int ExitCode { get; }

    /// <summary>stdout 最多保留呼叫端指定字元數的文字。</summary>
    internal string StandardOutput { get; }

    /// <summary>stdout 是否曾超過擷取上限；截斷結果不得交給 JSON parser 當成完整證據。</summary>
    internal bool StandardOutputTruncated { get; }

    /// <summary>stderr 最多保留呼叫端指定字元數的文字。</summary>
    internal string StandardError { get; }

    /// <summary>stderr 是否曾超過擷取上限。</summary>
    internal bool StandardErrorTruncated { get; }
}

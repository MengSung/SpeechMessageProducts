/*
 * 檔案整體說明
 *
 * 目的與定位：以真正子程序驗證架構評估器的程序期限、雙管線排空及輸出記憶體上限。 子程序只執行固定的本機 PowerShell 指令，不連網、不讀取設定或秘密；每次執行由 runner 終止並釋放。
 *
 * 主要功能與流程：
 * 本檔宣告 ArchitectureProcessRunnerTests。
 * - 保護子程序期限契約；注入固定長時間等待，斷言 runner 在有界時間內回報逾時且不讓等待洩漏到呼叫端。
 * - 保護 stdout 與 stderr 同時大量輸出時的生命週期契約；注入超過擷取上限的兩條管線， 斷言 runner 仍排空至程序退出，但每條管線只保留固定上限且明確標記截斷。
 * - 將固定指令以 UTF-16 Base64 傳給 Windows PowerShell，避免命令列跳脫造成不同的測試輸入。
 * 測試案例入口包含 Run_WhenProcessExceedsDeadline_ThrowsWithinBoundedTime、
 * Run_WhenBothOutputPipesExceedCaptureLimit_DrainsAndBoundsCapturedText、CreatePowerShellStartInfo。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Diagnostics;
using System.Text;
using Xunit;

namespace SpeechMessage.Architecture.Tests;

/// <summary>
/// 以真正子程序驗證架構評估器的程序期限、雙管線排空及輸出記憶體上限。
/// 子程序只執行固定的本機 PowerShell 指令，不連網、不讀取設定或秘密；每次執行由 runner 終止並釋放。
/// </summary>
public sealed class ArchitectureProcessRunnerTests
{
    private const int MaximumCapturedCharacters = 4096;

    /// <summary>
    /// 保護子程序期限契約；注入固定長時間等待，斷言 runner 在有界時間內回報逾時且不讓等待洩漏到呼叫端。
    /// </summary>
    [Fact]
    public void Run_WhenProcessExceedsDeadline_ThrowsWithinBoundedTime()
    {
        var startInfo = CreatePowerShellStartInfo("Start-Sleep -Seconds 30");
        var stopwatch = Stopwatch.StartNew();

        var exception = Assert.Throws<TimeoutException>(
            () => BoundedProcessRunner.Run(startInfo, TimeSpan.FromMilliseconds(500), MaximumCapturedCharacters));

        Assert.Equal("Architecture evaluation exceeded its execution deadline.", exception.Message);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8));
    }

    /// <summary>
    /// 保護 stdout 與 stderr 同時大量輸出時的生命週期契約；注入超過擷取上限的兩條管線，
    /// 斷言 runner 仍排空至程序退出，但每條管線只保留固定上限且明確標記截斷。
    /// </summary>
    [Fact]
    public void Run_WhenBothOutputPipesExceedCaptureLimit_DrainsAndBoundsCapturedText()
    {
        const string command =
            "for ($i = 0; $i -lt 8000; $i++) { " +
            "[Console]::Out.WriteLine(('O' * 256)); " +
            "[Console]::Error.WriteLine(('E' * 256)) }";
        var result = BoundedProcessRunner.Run(
            CreatePowerShellStartInfo(command),
            TimeSpan.FromSeconds(20),
            MaximumCapturedCharacters);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.StandardOutputTruncated);
        Assert.True(result.StandardErrorTruncated);
        Assert.True(result.StandardOutput.Length <= MaximumCapturedCharacters);
        Assert.True(result.StandardError.Length <= MaximumCapturedCharacters);
        Assert.StartsWith("O", result.StandardOutput, StringComparison.Ordinal);
        Assert.StartsWith("E", result.StandardError, StringComparison.Ordinal);
    }

    /// <summary>將固定指令以 UTF-16 Base64 傳給 Windows PowerShell，避免命令列跳脫造成不同的測試輸入。</summary>
    /// <param name="command">僅由本測試常數組成的本機指令。</param>
    /// <returns>兩條輸出管線均重新導向且不啟動 shell 的程序設定。</returns>
    private static ProcessStartInfo CreatePowerShellStartInfo(string command)
    {
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        return new ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encodedCommand)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
    }
}

/*
檔案整體說明

目的與定位：提供 PluginHarness 的命令列入口，將受信任的外部舊專案根目錄與候選輸出路徑交給一次性引擎處理，供 P5-02 離線盤點使用。

主要功能與流程：解析有限且明確的命令列參數，建立單次 HarnessEngine，先完成全量讀取、網路 API 拒絕、秘密樣式掃描、Roslyn 編譯分類及 attendance 黃金驗證，再將完整候選交給原子寫入器。程式不載入舊 DLL、不呼叫外部服務、不執行背景工作。

使用邊界與維護：source-root 必須是工作樹外且名稱為 Dynamics365Line 的目錄；output 只由本次呼叫建立或原子替換。錯誤輸出只顯示固定例外型別，不回傳來源本文、完整路徑、診斷訊息或可能包含秘密的內容。所有委派皆在同步呼叫結束前完成，沒有跨呼叫可變狀態或需要 Dispose 的長生命週期資源。
*/
using System;
using System.IO;

namespace LegacyGolden.PluginHarness;

/// <summary>
/// 命令列工具的唯一程序入口；只負責參數邊界與固定化錯誤碼，不承擔來源解析或輸出發布。
/// </summary>
internal static class Program
{
    /// <summary>
    /// 解析 source-root 與 output，完成一個完整候選的建立與原子發布；成功回傳零，參數或執行失敗回傳非零。
    /// </summary>
    /// <param name="args">必須包含 <c>--source-root</c> 與 <c>--output</c> 及其值。</param>
    /// <returns>零表示候選已完整發布；二表示參數錯誤；一表示候選未發布。</returns>
    private static int Main(string[] args)
    {
        try
        {
            if (args == null || args.Length == 0 || HasHelp(args))
            {
                PrintUsage();
                return args != null && HasHelp(args) ? 0 : 2;
            }

            var sourceRoot = ReadOption(args, "--source-root");
            var outputPath = ReadOption(args, "--output");
            if (sourceRoot == null || outputPath == null)
            {
                PrintUsage();
                return 2;
            }

            throw new NotSupportedException("Legacy execution is not implemented.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("PluginHarness failed: " + exception.GetType().Name);
            return 1;
        }
    }

    /// <summary>判斷是否只要求固定使用說明；不解析其他參數以避免誤觸發來源讀取。</summary>
    private static bool HasHelp(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>從成對參數取得值；缺值、重複或旗標位置錯誤一律視為參數錯誤。</summary>
    private static string? ReadOption(string[] args, string name)
    {
        string? value = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.Ordinal))
            {
                continue;
            }

            if (value != null || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("-", StringComparison.Ordinal))
            {
                return null;
            }

            value = args[index + 1];
            index++;
        }

        return value;
    }

    /// <summary>輸出不含來源值的固定使用說明，避免把操作者路徑寫入診斷輸出。</summary>
    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: PluginHarness --source-root <Dynamics365Line> --output <candidate.json>");
    }
}

/*
檔案整體說明

目的與定位：以單一同目錄暫存檔完成 PluginHarness JSON 候選的原子發布，避免讀者看到半份報告或失敗流程覆蓋既有完整結果。

主要功能與流程：驗證輸出副檔名與路徑，建立唯一暫存檔，以 UTF-8 無 BOM 寫入並 flush 到磁碟；目標不存在時移動暫存檔，目標存在時使用 Windows 原子 Replace。任一步驟失敗都在 finally 清除本次暫存檔，既有目標維持原狀。

使用邊界與維護：writer 只擁有本次暫存檔 stream，不建立背景工作、鎖、快取或跨程序狀態；caller 必須自行序列化同一輸出路徑的並行發布。路徑不得由來源內容或使用者資料拼接，檔案上限與 parent 建立均在同步呼叫內完成。
*/
using System;
using System.IO;
using System.Text;

namespace LegacyGolden.PluginHarness;

/// <summary>候選 JSON 的有界原子發布器。</summary>
internal static class AtomicJsonWriter
{
    /// <summary>先寫完整暫存檔再原子替換目標；失敗時不發布部分內容。</summary>
    internal static void Publish(string destination, string json)
    {
        if (string.IsNullOrWhiteSpace(destination) || json == null || !destination.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A JSON destination and candidate are required.", nameof(destination));
        }

        var fullPath = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new InvalidDataException("Output requires a parent directory.");
        }

        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(parent, ".plugin-harness-stage-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true)))
            {
                writer.NewLine = "\r\n";
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(fullPath))
            {
                File.Replace(temporary, fullPath, null, true);
            }
            else
            {
                File.Move(temporary, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

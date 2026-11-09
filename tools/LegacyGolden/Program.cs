/*
檔案整體說明
目的與定位：提供 P3-01 黃金產生器的離線命令列入口，負責參數與結束碼，實際來源編譯、回覆抽取與發布由專用元件處理。
主要功能與流程：只接受明確來源、暫存、P0-05 腳本與輸出目錄，完成 reader、真正 matcher 與靜態回覆驗證後才一次發布十三檔。產生失敗回非零結束碼及固定分類，不自動尋找來源或服務設定。
使用邊界與維護：本檔不持有登入狀態或背景工作，來源編譯僅供可信舊程式，不是任意程式沙箱。暫存與輸出不得落在來源內，所有 process、stream 與 context 由下層單次 owner 清理。命令列錯誤只呈現固定分類，不複製來源本文、私人識別或憑證。
*/
namespace LegacyGolden;

/// <summary>單次離線產生入口；完整候選成功前不碰正式輸出。</summary>
internal static class Program
{
    /// <summary>Console 僅借用且不 Dispose；沒有未等待工作或長期背景資源。</summary>
    internal static Task<int> Main(string[] args) => RunAsync(args, Console.Out, Console.Error, CancellationToken.None);

    /// <summary>參數只有四個明確位置；所有預期失敗保持非零且不印原始例外內容，方便離線 CI 保存安全證據。</summary>
    internal static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length != 8 || args[0] != "--source-root" || args[2] != "--scratch-root" || args[4] != "--converter" || args[6] != "--output")
        {
            await error.WriteLineAsync("Usage: --source-root <trusted-legacy-root> --scratch-root <ignored-evidence> --converter <P0-05-script> --output <Golden-directory>").ConfigureAwait(false);
            return 2;
        }
        try
        {
            var destination = Path.GetFullPath(args[7]);
            var source = Path.GetFullPath(args[1]);
            var scratch = Path.GetFullPath(args[3]);
            if (LegacySourceReader.IsWithin(source, destination) || LegacySourceReader.IsWithin(destination, source) ||
                LegacySourceReader.IsWithin(scratch, destination) || LegacySourceReader.IsWithin(destination, scratch))
                throw new InvalidDataException("Source, scratch and output must be separate.");
            var sources = await LegacySourceReader.ReadAsync(source, scratch, args[5], cancellationToken).ConfigureAwait(false);
            var golden = GoldenGenerator.Generate(sources);
            var files = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["Keywords/routing.json"] = GoldenGenerator.Serialize(golden) };
            foreach (var item in SyntheticEvents.Create()) files.Add("Events/" + item.Key, GoldenGenerator.Serialize(item.Value));
            cancellationToken.ThrowIfCancellationRequested();
            GoldenOutput.Publish(destination, files);
            await output.WriteLineAsync("Legacy golden generated: original matcher executed; static replies only; 12 synthetic events.").ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException or KeyNotFoundException or AggregateException)
        {
            await error.WriteLineAsync("LegacyGolden failed: " + exception.GetType().Name).ConfigureAwait(false);
            return 1;
        }
    }
}

/*
檔案整體說明
目的與定位：F5 實機推播入口的環境鎖讀取租約，與一般離線測試放在同一測試專案，避免產品接受任意鎖路徑。
主要功能與流程：固定順序 D365 再 LINE，逐檔核對精確 lane=A／wp=F5，再持有禁止寫入與替換的讀取 handle；
舊 test-env.lock 尚存在則拒絕，完全不讀取或持有 SQL 鎖。每檔最多四 KiB，使用嚴格 UTF-8 並保留舊 PowerShell BOM 相容。
輸入與信任邊界：正式接縫固定由 CommonApplicationData 找機器目錄；目錄注入只供離線暫存檔測試，不載入秘密或連線。
鎖文字只是外部互斥協定，不代替服務端組織、產品、租戶或 Channel 驗證；後續呼叫端須先取得外部鎖再使用本租約。
資源與隔離：每個租約唯一持有最多兩個 FileStream，不建立全域快取、timer 或背景工作；部分取得失敗先關 LINE 再關 D365，
成功後由外層在所有工作實際排空時 Dispose，同一組 handle 只由一個 Dispose 呼叫反序釋放，不改寫或刪除外部鎖。
失敗與驗證：格式錯誤只產生固定安全訊息，I/O 或解碼原例外保留給外層共用診斷；離線錯 owner／缺檔／非法編碼及
獨占重開測試驗證零殘留 handle。此檔只是實機入口前置準備，不代表 F5 或 FS 已經實機驗收。
*/
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using SpeechMessage.Dynamics.Deployment.Schema;

namespace SpeechMessage.Dynamics.Deployment.Tests.Live;

/// <summary>F5 雙資源的單輪有界 owner，只持有讀取 handle，不取得、修改或刪除外部鎖。</summary>
internal sealed class F5PushLiveLockLease : IDisposable
{
    /// <summary>兩個 stream 以一個陣列原子轉移給 Dispose，避免併行重複釋放時破壞反序關閉。</summary>
    private FileStream[]? ownedStreams;

    /// <summary>只接收兩個已完整驗證的開啟 stream；建構成功才由呼叫端轉移唯一所有權。</summary>
    private F5PushLiveLockLease(FileStream d365, FileStream line) => ownedStreams = new[] { d365, line };

    /// <summary>只從機器固定鎖目錄取得讀取租約，呼叫端不能透過環境變數或參數指定其他鎖。</summary>
    internal static F5PushLiveLockLease Acquire() => AcquireFromDirectory(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SpeechMessage", "locks"));

    /// <summary>
    /// 離線可注入單案目錄的同流程接縫；每個既有父路徑都拒絕 reparse point，不建立目錄或缺少的鎖。
    /// 任一步失敗均保留原例外並關閉已開啟的 stream，成功後外層必須在工作 drain 完成後 Dispose。
    /// </summary>
    internal static F5PushLiveLockLease AcquireFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        directory = Path.GetFullPath(directory);
        SchemaDeploymentCli.EnsureNoLinks(directory);
        var legacy = Path.Combine(directory, "test-env.lock");
        SchemaDeploymentCli.EnsureNoLinks(legacy);
        if (File.Exists(legacy)) throw Rejected();
        FileStream? d365 = null;
        FileStream? line = null;
        var transferred = false;
        try
        {
            d365 = OpenValidated(Path.Combine(directory, "test-env-d365.lock"));
            line = OpenValidated(Path.Combine(directory, "test-env-line.lock"));
            var lease = new F5PushLiveLockLease(d365, line);
            transferred = true;
            return lease;
        }
        finally
        {
            if (!transferred)
            {
                try { line?.Dispose(); }
                finally { d365?.Dispose(); }
            }
        }
    }

    /// <summary>
    /// 逐 token 驗證精確 lane 與工作包；start 必須為 round-trip 格式且只出現一次，不新增鎖過期規則。
    /// 拒絕未知與重複 token，不以 substring 或最後值覆蓋放行；錯誤不回顯原始鎖本文。
    /// </summary>
    internal static void RequireOwner(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096) throw Rejected();
        var lane = false;
        var package = false;
        var start = false;
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "lane=A" && !lane) lane = true;
            else if (token == "wp=F5" && !package) package = true;
            else if (token.StartsWith("start=", StringComparison.Ordinal) && !start &&
                DateTimeOffset.TryParseExact(token[6..], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) start = true;
            else throw Rejected();
        }
        if (!lane || !package || !start) throw Rejected();
    }

    /// <summary>
    /// 開檔前拒絕 link，handle 只允許其他讀者；先檢查位元組上限再解碼，成功才把 handle 交給租約。
    /// reader 只借用 stream 且必定先 Dispose；驗證、解碼或 reader 清理故障都會關閉尚未轉交的 stream。
    /// </summary>
    private static FileStream OpenValidated(string path)
    {
        SchemaDeploymentCli.EnsureNoLinks(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (stream.Length is <= 0 or > 4096) throw Rejected();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            var text = reader.ReadToEnd();
            RequireOwner(text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>固定安全錯誤不包含路徑、鎖本文或使用者輸入；外層負責把實際例外交共用診斷。</summary>
    private static InvalidDataException Rejected() => new("F5 live environment lock preflight failed.");

    /// <summary>排空後由唯一外層 owner 呼叫；原子取走兩個 handle，反序釋放且可安全重複呼叫。</summary>
    public void Dispose()
    {
        var streams = Interlocked.Exchange(ref ownedStreams, null);
        if (streams is null) return;
        try { streams[1].Dispose(); }
        finally { streams[0].Dispose(); }
    }
}

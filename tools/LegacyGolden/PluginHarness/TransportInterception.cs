/*
檔案整體說明
目的與定位：為離線舊入口提供只能記錄的HttpClient及單次執行捕捉範圍，不建立真實HTTP handler或讀取任何部署憑證。
主要功能與流程：編譯器改寫HttpClient建構至RecordingHttpClient，handler同步完成內容讀取、移除header與query、把收件識別轉為情境alias，保存有界JSON並回固定成功。ScratchFiles將舊絕對或相對路徑雜湊映射到runner獨占暫存根，stream 同時登錄至有界 capture owner，舊 using 可提早關閉，capture finally 反向關閉遺漏的 stream。
使用邊界與維護：AsyncLocal只在單次獨立AppDomain內流動，finally清除，不在不同情境共用。capture最多512事件、訊息本文128KiB；任何超限或無owner呼叫明確失敗。runner在卸載域後刪除自己的scratch，未知網路與檔案API由compiler先拒絕；本檔不宣稱能執行任意敵意來源。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LegacyGolden.PluginHarness;

/// <summary>單一執行域內的捕捉owner；非同步上下文只持有本案例，結束時確定清除。</summary>
internal sealed class ExecutionCapture : IDisposable
{
    private static readonly AsyncLocal<ExecutionCapture?> Slot = new();
    private readonly Func<string, string> recipientAlias;
    private readonly HashSet<string> scratchNames = new(StringComparer.Ordinal);
    private readonly List<IDisposable> resources = new();
    /// <summary>建立唯一捕捉範圍，巢狀或同域重用一律拒絕。</summary>
    internal ExecutionCapture(string scratch, Func<string, string> recipientAlias)
    {
        if (Slot.Value is not null) throw new InvalidOperationException("Capture already active.");
        Scratch = Path.GetFullPath(scratch);
        this.recipientAlias = recipientAlias;
        Slot.Value = this;
    }
    /// <summary>執行域自己的暫存目錄，由外層runner清除。</summary>
    internal string Scratch { get; }
    /// <summary>有界LINE或其他HTTP請求記錄，只包含必要比較值。</summary>
    internal JArray Calls { get; } = new();
    /// <summary>沒有單次owner就禁止傳輸或檔案操作。</summary>
    internal static ExecutionCapture Current => Slot.Value ?? throw new InvalidOperationException("No capture owner.");
    /// <summary>單案例最多128個路徑，不沿用舊輸入路徑的磁碟語意或把名稱放入診斷。</summary>
    internal string RegisterPath(string key)
    {
        if (!scratchNames.Contains(key) && scratchNames.Count >= 128)
            throw new InvalidDataException("Scratch path capacity exceeded.");
        scratchNames.Add(key);
        return Path.Combine(Scratch, key);
    }
    /// <summary>最多256個stream由案例持有，包含舊程式忘記Dispose者，最後依反向順序確定關閉。</summary>
    internal FileStream Own(FileStream stream)
    {
        if (resources.Count >= 256)
        {
            stream.Dispose();
            throw new InvalidDataException("Scratch handle capacity exceeded.");
        }
        resources.Add(stream);
        return stream;
    }
    /// <summary>記錄固定端點及已解碼JSON內容，不保存Authorization或任何其他header。</summary>
    internal void Record(string method, Uri endpoint, string body)
    {
        if (Calls.Count >= 512 || body.Length > 131072) throw new InvalidDataException("Transport capacity exceeded.");
        var json = string.IsNullOrEmpty(body) ? new JObject() : JToken.Parse(body);
        var recipients = new JArray();
        if (json["to"] is JArray many)
            foreach (var value in many) recipients.Add(recipientAlias(value.Value<string>() ?? ""));
        else if (json["to"] is JValue one) recipients.Add(recipientAlias(one.Value<string>() ?? ""));
        var content = json["messages"]?.DeepClone() ?? new JArray();
        Calls.Add(new JObject
        {
            ["method"] = method, ["host"] = endpoint.Host, ["endpoint"] = endpoint.AbsolutePath,
            ["recipients"] = recipients, ["messageCount"] = content is JArray array ? array.Count : 0,
            ["content"] = content
        });
    }
    /// <summary>停止持有本次capture；不能清掉別的owner。</summary>
    public void Dispose()
    {
        var cleanupFailed = false;
        try
        {
            for (var index = resources.Count - 1; index >= 0; index--)
            {
                try
                {
                    resources[index].Dispose();
                }
                catch (IOException)
                {
                    cleanupFailed = true;
                }
            }
        }
        finally
        {
            resources.Clear();
            scratchNames.Clear();
            Calls.Clear();
            if (ReferenceEquals(Slot.Value, this)) Slot.Value = null;
        }
        if (cleanupFailed) throw new IOException("Scratch resource cleanup failed.");
    }
}

/// <summary>只能使用記錄handler的舊HttpClient替身；每個實例由舊程式或域卸載擁有。</summary>
public sealed class RecordingHttpClient : HttpClient
{
    /// <summary>從建構開始即沒有可建立socket的handler。</summary>
    public RecordingHttpClient() : base(new RecordingHandler(), true) { }
}

/// <summary>固定同步完成的傳輸替身，不排入無界背景工作或保留request。</summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    /// <summary>取消先拒絕，成功只記錄後回傳獨立可Dispose的固定JSON回應。</summary>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoint = request.RequestUri ?? throw new InvalidDataException("Endpoint required.");
        var body = request.Content is null ? "" : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        ExecutionCapture.Current.Record(request.Method.Method, endpoint, body);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }
}

/// <summary>舊檔案API的唯一替身；所有路徑必須落在已建立的單次scratch根。</summary>
public static class ScratchFiles
{
    /// <summary>以完整輸入路徑雜湊為本案例檔名，不沿用磁碟或相對路徑語意。</summary>
    public static string Map(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path required.", nameof(path));
        using var hash = SHA256.Create();
        var key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path))).Replace("-", "");
        return ExecutionCapture.Current.RegisterPath(key);
    }
    /// <summary>傳回只指向scratch的檔案stream，呼叫端必須Dispose。</summary>
    public static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share) =>
        ExecutionCapture.Current.Own(new ScratchBoundedStream(Map(path), mode, access, share));
    /// <summary>保留FileStream常用雙參數建構的預設存取。</summary>
    public static FileStream Open(string path, FileMode mode) => Open(path, mode, mode == FileMode.Append ? FileAccess.Write : FileAccess.ReadWrite, FileShare.None);
    /// <summary>保留顯式存取，不允許原始路徑逃逸。</summary>
    public static FileStream Open(string path, FileMode mode, FileAccess access) => Open(path, mode, access, FileShare.None);
    /// <summary>讀取本案例先前寫入的檔案，不讀取原系統檔案。</summary>
    public static byte[] ReadAllBytes(string path) => File.ReadAllBytes(Map(path));
    /// <summary>讀取scratch文字，不變更內容編碼。</summary>
    public static string ReadAllText(string path) => File.ReadAllText(Map(path));
    /// <summary>依舊指定編碼讀取scratch。</summary>
    public static string ReadAllText(string path, Encoding encoding) => File.ReadAllText(Map(path), encoding);
    /// <summary>最多1MiB資料寫入scratch，沒有真路徑副作用。</summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        if (bytes.Length > 1048576) throw new InvalidDataException("Scratch capacity exceeded.");
        using var stream = Open(path, FileMode.Create, FileAccess.Write);
        stream.Write(bytes, 0, bytes.Length);
    }
    /// <summary>寫入有界UTF8文字。</summary>
    public static void WriteAllText(string path, string text) => WriteAllText(path, text, new UTF8Encoding(false));
    /// <summary>保留呼叫端編碼並限制單次文字大小。</summary>
    public static void WriteAllText(string path, string text, Encoding encoding)
    {
        if (text.Length > 1048576) throw new InvalidDataException("Scratch capacity exceeded.");
        using var stream = Open(path, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(stream, encoding);
        writer.Write(text);
    }
    /// <summary>追加前先檢查目前大小，不能以多次小寫入無界增長。</summary>
    public static void AppendAllText(string path, string text) => AppendAllText(path, text, new UTF8Encoding(false));
    /// <summary>追加指定編碼文字，總檔案上限1MiB。</summary>
    public static void AppendAllText(string path, string text, Encoding encoding)
    {
        if (text.Length > 1048576) throw new InvalidDataException("Scratch capacity exceeded.");
        using var stream = Open(path, FileMode.Append, FileAccess.Write);
        using var writer = new StreamWriter(stream, encoding);
        writer.Write(text);
    }
    /// <summary>唯讀每列仍受單檔一MiB上限，檔案只可能由本案例建立。</summary>
    public static string[] ReadAllLines(string path) => File.ReadAllLines(Map(path));
    /// <summary>以呼叫端編碼讀取本案例有界文字。</summary>
    public static string[] ReadAllLines(string path, Encoding encoding) => File.ReadAllLines(Map(path), encoding);
    /// <summary>逐列寫入有界stream，避免先將未知長度enumerable物化。</summary>
    public static void WriteAllLines(string path, IEnumerable<string> lines) => WriteLines(path, lines, new UTF8Encoding(false), FileMode.Create);
    /// <summary>保留呼叫端編碼但不放寬容量。</summary>
    public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding encoding) => WriteLines(path, lines, encoding, FileMode.Create);
    /// <summary>逐列追加到相同有界stream。</summary>
    public static void AppendAllLines(string path, IEnumerable<string> lines) => WriteLines(path, lines, new UTF8Encoding(false), FileMode.Append);
    /// <summary>指定編碼的逐列追加仍受一MiB上限。</summary>
    public static void AppendAllLines(string path, IEnumerable<string> lines, Encoding encoding) => WriteLines(path, lines, encoding, FileMode.Append);
    /// <summary>唯一逐列寫入owner；enumeration、flush失敗仍關閉writer與stream。</summary>
    private static void WriteLines(string path, IEnumerable<string> lines, Encoding encoding, FileMode mode)
    {
        using var stream = Open(path, mode, FileAccess.Write);
        using var writer = new StreamWriter(stream, encoding);
        foreach (var line in lines)
        {
            if (line != null && line.Length > 1048576) throw new InvalidDataException("Scratch capacity exceeded.");
            writer.WriteLine(line);
        }
    }
    /// <summary>目錄也映射到本次scratch。</summary>
    public static DirectoryInfo CreateDirectory(string path) => Directory.CreateDirectory(Map(path));
    /// <summary>只檢查映射後的案例檔案。</summary>
    public static bool Exists(string path) => File.Exists(Map(path));
    /// <summary>目錄存在檢查保留Directory語意，不誤用File.Exists。</summary>
    public static bool DirectoryExists(string path) => Directory.Exists(Map(path));
    /// <summary>只刪除本案例的映射檔案。</summary>
    public static void Delete(string path) => File.Delete(Map(path));
}

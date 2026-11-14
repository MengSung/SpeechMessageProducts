/*
檔案整體說明
目的與定位：為離線舊入口提供只能記錄的HttpClient及單次執行捕捉範圍，不建立真實HTTP handler或讀取任何部署憑證。
主要功能與流程：編譯器改寫HttpClient建構至RecordingHttpClient，handler同步完成內容讀取、移除header與query、把收件識別轉為情境alias，保存有界JSON並回固定成功。ScratchFiles將舊絕對或相對路徑雜湊映射到runner獨占暫存根，所有stream仍由舊using或域卸載釋放。
使用邊界與維護：AsyncLocal只在單次獨立AppDomain內流動，finally清除，不在不同情境共用。capture最多512事件、訊息本文128KiB；任何超限或無owner呼叫明確失敗。runner在卸載域後刪除自己的scratch，未知網路與檔案API由compiler先拒絕；本檔不宣稱能執行任意敵意來源。
*/
using System;
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
        if (ReferenceEquals(Slot.Value, this)) Slot.Value = null;
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
        return Path.Combine(ExecutionCapture.Current.Scratch, key);
    }
    /// <summary>傳回只指向scratch的檔案stream，呼叫端必須Dispose。</summary>
    public static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share) => new(Map(path), mode, access, share);
    /// <summary>保留FileStream常用雙參數建構的預設存取。</summary>
    public static FileStream Open(string path, FileMode mode) => new(Map(path), mode);
    /// <summary>保留顯式存取，不允許原始路徑逃逸。</summary>
    public static FileStream Open(string path, FileMode mode, FileAccess access) => new(Map(path), mode, access);
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
        File.WriteAllBytes(Map(path), bytes);
    }
    /// <summary>寫入有界UTF8文字。</summary>
    public static void WriteAllText(string path, string text) => WriteAllText(path, text, new UTF8Encoding(false));
    /// <summary>保留呼叫端編碼並限制單次文字大小。</summary>
    public static void WriteAllText(string path, string text, Encoding encoding)
    {
        if (text.Length > 1048576) throw new InvalidDataException("Scratch capacity exceeded.");
        File.WriteAllText(Map(path), text, encoding);
    }
    /// <summary>追加前先檢查目前大小，不能以多次小寫入無界增長。</summary>
    public static void AppendAllText(string path, string text) => AppendAllText(path, text, new UTF8Encoding(false));
    /// <summary>追加指定編碼文字，總檔案上限1MiB。</summary>
    public static void AppendAllText(string path, string text, Encoding encoding)
    {
        var target = Map(path);
        var length = File.Exists(target) ? new FileInfo(target).Length : 0;
        if (length + encoding.GetByteCount(text) > 1048576) throw new InvalidDataException("Scratch capacity exceeded.");
        File.AppendAllText(target, text, encoding);
    }
    /// <summary>目錄也映射到本次scratch。</summary>
    public static DirectoryInfo CreateDirectory(string path) => Directory.CreateDirectory(Map(path));
    /// <summary>只檢查映射後的案例檔案。</summary>
    public static bool Exists(string path) => File.Exists(Map(path));
    /// <summary>只刪除本案例的映射檔案。</summary>
    public static void Delete(string path) => File.Delete(Map(path));
}

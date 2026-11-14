/*
檔案整體說明
目的與定位：提供黃金JSON的單一安全發布邊界，避免舊來源中的秘密及部分候選進入版控。
主要功能與流程：先驗證完整候選的解碼字串，再以固定JSON格式及CRLF交給原子writer；驗證失敗先於建立目標或暫存檔。
使用邊界與維護：候選屬於呼叫端，不保留來源、憑證或全域快取，不建立外部服務。測試必須證明拒絕秘密時既有檔案保持完整，不能把任意例外當成成功產生黃金。
*/
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace LegacyGolden.PluginHarness;
/// <summary>完整候選的安全發布入口。</summary>
public static class GoldenWriter
{
    /// <summary>解碼掃描通過後固定排序與換行，再原子發布完整候選。</summary>
    public static void Write(string destination, JObject candidate)
    {
        OutputSecretScan.Validate(candidate);
        var json = JsonConvert.SerializeObject(candidate, Formatting.Indented,
            new JsonSerializerSettings { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii });
        AtomicJsonWriter.Publish(destination, json.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n");
    }
}

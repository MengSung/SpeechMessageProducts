/*
 * 檔案整體說明
 * 目的與定位：P5-07連結模組兩個測試宿主共用的唯讀情境入口，以Compile Link
 * 給外殼測試，避免各自持有不同scenarios／expected或從產品輸出重生期望。
 * 主要流程：只讀lessonslink的兩個嵌入JSON，限制1MiB及深度32；合成alias按照
 * harness既有SHA-256前16 bytes取得固定GUID，讓payload與安全摘要可逐欄核對。
 * 使用邊界與維護：只接受固定兩種檔名，不讀磁碟或秘密；reader、stream及hash
 * 各由方法using同步釋放，不保存JSON樹、ID映射或可變static資料，沒有背景工作。
 * 返回樹只屬於呼叫測試；這是離線資料身分，不冒充伺服器授權或實機驗收。
 */
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>兩個測試宿主共用同份有界輸入及固定合成資料庫ID。</summary>
internal static class LessonLinkGoldenFixture
{
    /// <summary>資源由本組件擁有；每次獨立讀取並立即釋放串流，不快取測試狀態。</summary>
    internal static JObject Read(string kind)
    {
        if (kind is not ("scenarios" or "expected")) throw new ArgumentException("Unknown lesson fixture.", nameof(kind));
        var assembly = typeof(LessonLinkGoldenFixture).Assembly;
        var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith(".lessonslink." + kind + ".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Missing lesson fixture.");
        if (stream.Length > 1048576) throw new InvalidDataException("Lesson fixture exceeds capacity.");
        using var text = new StreamReader(stream, new UTF8Encoding(false, true));
        using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
        return JObject.Load(reader);
    }

    /// <summary>僅供合成種子身分；與harness既有alias規則相同，不呼叫產品的事件GUID生成器。</summary>
    internal static Guid Id(string alias)
    {
        if (string.IsNullOrEmpty(alias) || alias.Length > 128) throw new ArgumentException("Invalid synthetic alias.", nameof(alias));
#if NETFRAMEWORK
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(alias));
#else
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(alias));
#endif
        var bytes = new byte[16]; Array.Copy(hash, bytes, bytes.Length);
        return new Guid(bytes);
    }
}

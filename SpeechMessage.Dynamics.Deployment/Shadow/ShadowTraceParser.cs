/*
 * 檔案整體說明
 *
 * 目的與定位：解析 P5 影子模式寫入的有界 SMV-SHADOW|1| 記錄，供離線比較及
 * 實機 shadow 報告使用。本檔不連線、不執行 Plugin、不保存跨命令狀態，也不把
 * token、LINE userId 或訊息正文帶入結果。
 *
 * 主要功能與流程：逐行檢查固定前綴、JSON 物件、序號與 end/overflow 標記，
 * 將欄位投影轉成 detached record；超過 8,000 字元、缺 end、重複序號或格式
 * 不合法都 fail closed。呼叫端擁有輸入字串及回傳結果，沒有背景工作或可釋放資源。
 *
 * 使用邊界與維護：解析器只接受產生器定義的安全投影；它不是 JSON 任意輸入
 * 清理器，也不會猜測欄位意義。變更格式時必須同步更新 comparer、黃金測試及
 * 05A §4.3 的長度與結尾契約。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpeechMessage.Dynamics.Deployment.Shadow;

/// <summary>
/// 一筆已脫離原始 trace 的影子意圖。Projection 是 JsonElement.Clone 的唯讀快照，
/// 保留數字型別、decimal 原文、關聯及推播欄位，不借用已釋放的 JsonDocument。
/// </summary>
internal sealed record ShadowIntent(int Sequence, string Operation, string Entity, string Id, JsonElement Projection);

/// <summary>解析結果；Total 為溢位前原始筆數，HasEnd 只表示有合法的最後標記。</summary>
internal sealed record ShadowTrace(IReadOnlyList<ShadowIntent> Intents, bool Overflow, bool HasEnd, long Total)
{
    /// <summary>只有完整、未溢位且有結尾時才允許宣稱可比較。</summary>
    public bool Comparable => !Overflow && HasEnd;
}

/// <summary>解析有界影子 trace，任何異常都回傳固定診斷而不回顯輸入內容。</summary>
internal static class ShadowTraceParser
{
    private const string Prefix = "SMV-SHADOW|1|";
    private const int MaximumCharacters = 8_000;
    private static readonly string[] LineSeparators = { "\r\n", "\n" };

    /// <summary>
    /// 解析固定版本的完整摘要。序號須從一連續遞增，end.count 等於保留筆數，
    /// overflow.total 必須大於保留筆數；end 後禁止再有資料。缺 end 不可比較。
    /// </summary>
    /// <param name="trace">由 recorder 產生的安全摘要，最多 8,000 個 UTF-16 字元。</param>
    /// <returns>只屬本次操作的 detached 解析結果。</returns>
    /// <exception cref="InvalidDataException">格式、長度或標記不符；例外不包含原始 trace。</exception>
    public static ShadowTrace Parse(string trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (trace.Length > MaximumCharacters) throw InvalidTrace();
        try
        {
            var intents = new List<ShadowIntent>();
            var overflow = false;
            var hasEnd = false;
            long total = 0;
            foreach (var line in trace.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (hasEnd || !line.StartsWith(Prefix, StringComparison.Ordinal)) throw InvalidTrace();
                using var document = JsonDocument.Parse(line[Prefix.Length..], new JsonDocumentOptions { MaxDepth = 32 });
                var root = document.RootElement;
                ValidateObject(root);
                if (root.TryGetProperty("end", out var end))
                {
                    if (end.ValueKind != JsonValueKind.True || PropertyCount(root) != 2 ||
                        ReadCount(root, "count") != intents.Count) throw InvalidTrace();
                    hasEnd = true;
                    if (!overflow) total = intents.Count;
                    continue;
                }
                if (root.TryGetProperty("overflow", out var marker))
                {
                    if (overflow || marker.ValueKind != JsonValueKind.True || PropertyCount(root) != 2) throw InvalidTrace();
                    total = ReadCount(root, "total");
                    if (total <= intents.Count) throw InvalidTrace();
                    overflow = true;
                    continue;
                }
                if (overflow || ReadCount(root, "seq") != intents.Count + 1) throw InvalidTrace();
                var operation = ReadString(root, "operation");
                ValidateIntent(root, operation);
                intents.Add(new ShadowIntent(intents.Count + 1, operation,
                    operation == "Push" ? "" : ReadString(root, "entity"),
                    operation == "Push" ? ReadString(root, "contactId") : ReadString(root, "id"), root.Clone()));
            }
            return new ShadowTrace(intents.AsReadOnly(), overflow, hasEnd, hasEnd || overflow ? total : intents.Count);
        }
        catch (JsonException)
        {
            // JsonException 可能攜帶輸入片段或路徑；終端只能收到固定安全例外。
            throw InvalidTrace();
        }
    }

    /// <summary>依正式 recorder 的操作形狀驗證必要欄位，不忽略未知的頂層投影。</summary>
    private static void ValidateIntent(JsonElement root, string operation)
    {
        var allowed = operation switch
        {
            "Push" => "|seq|operation|contactId|templateKey|retryKey|messageCount|contentSha256|",
            "Create" or "Update" => "|seq|operation|entity|id|fields|keys|",
            "Delete" => "|seq|operation|entity|id|",
            "Associate" or "Disassociate" => "|seq|operation|entity|id|relationship|role|related|",
            _ => throw InvalidTrace()
        };
        foreach (var property in root.EnumerateObject())
            if (!allowed.Contains("|" + property.Name + "|", StringComparison.Ordinal)) throw InvalidTrace();
        if (operation == "Push")
        {
            _ = ReadString(root, "contactId");
            _ = ReadString(root, "retryKey");
            _ = ReadString(root, "templateKey");
            var hash = ReadString(root, "contentSha256");
            if (hash.Length != 64 || hash.Any(character => !char.IsAsciiHexDigit(character)) ||
                ReadCount(root, "messageCount") > 20) throw InvalidTrace();
            return;
        }
        _ = ReadString(root, "entity");
        _ = ReadString(root, "id");
        if (operation is "Create" or "Update")
        {
            if (!root.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object) throw InvalidTrace();
        }
        if (operation is "Associate" or "Disassociate")
        {
            _ = ReadString(root, "relationship");
            if (!root.TryGetProperty("related", out var related) || related.ValueKind != JsonValueKind.Array) throw InvalidTrace();
        }
    }

    /// <summary>遞迴拒絕重複 JSON 屬性，避免 last-wins 解析掩蓋不同身分或金額。</summary>
    private static void ValidateObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidTrace();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw InvalidTrace();
            ValidateNested(property.Value);
        }
    }

    /// <summary>沿有界 JSON 深度驗證巢狀物件，保留陣列順序，不排序或丟棄資料。</summary>
    private static void ValidateNested(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object) ValidateObject(element);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) ValidateNested(child);
    }

    /// <summary>取得物件實際屬性數，用於拒絕混入意圖欄位的控制標記。</summary>
    private static int PropertyCount(JsonElement root) => root.EnumerateObject().Count();

    /// <summary>writer 總數使用 Int64；讀取時不以 Int32 截斷或接受負數。</summary>
    private static long ReadCount(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt64(out var value) && value >= 0 ? value : throw InvalidTrace();

    /// <summary>必要結構識別字不能為空；保留值的原始大小寫，不做未授權的正規化。</summary>
    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString()) ? property.GetString()! : throw InvalidTrace();

    /// <summary>固定錯誤不攜帶輸入、秘密或內層 JSON 例外。</summary>
    private static InvalidDataException InvalidTrace() => new("影子記錄格式、長度或完整性不符。");
}

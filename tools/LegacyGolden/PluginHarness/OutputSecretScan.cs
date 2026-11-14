/*
檔案整體說明
目的與定位：在PluginHarness任何候選發布之前拒絕LINE識別與token樣式，不能把舊來源中的硬寫秘密帶進版控。
主要功能與流程：逐一檢查JSON解碼後的屬性名稱與字串值，因此unicode跳脫不能繞過掃描；拒絕LINE U加32hex、長base64、JWT及Bearer字串，並限制JSON圖深度與總量。失敗只拋固定類型與訊息，不回顯匹配內容。
使用邊界與維護：掃描器是無狀態純驗證，不讀來源或部署設定，也不保留Match、憑證及跨情境資料。正規表示式有一秒上限，逾時視同拒絕；writer必須在建立目標或暫存檔之前完成本驗證。
*/
using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace LegacyGolden.PluginHarness;

/// <summary>脫敏輸出的最終拒絕邊界。</summary>
public static class OutputSecretScan
{
    private static readonly Regex Pattern = new(
        @"U[0-9a-fA-F]{32}|[A-Za-z0-9+/=_-]{101,}|[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]+|Bearer\s+",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    /// <summary>驗證解碼後JSON；秘密或超過4MiB候選一律不發布。</summary>
    public static void Validate(JToken candidate)
    {
        if (candidate is null) throw new ArgumentNullException(nameof(candidate));
        if (candidate.ToString().Length > 4194304) throw new InvalidDataException("Output capacity exceeded.");
        Visit(candidate, 0);
    }
    /// <summary>遞迴保護字串與屬性名稱，圖深度最多32。</summary>
    private static void Visit(JToken token, int depth)
    {
        if (depth > 32) throw new InvalidDataException("Output depth exceeded.");
        if (token is JProperty property) Check(property.Name);
        if (token is JValue value && value.Type == JTokenType.String) Check(value.Value<string>() ?? "");
        foreach (var child in token.Children()) Visit(child, depth + 1);
    }
    /// <summary>使用固定訊息拒絕，不回傳匹配片段。</summary>
    private static void Check(string value)
    {
        if (Pattern.IsMatch(value)) throw new InvalidDataException("Output rejected by secret scan.");
    }
}

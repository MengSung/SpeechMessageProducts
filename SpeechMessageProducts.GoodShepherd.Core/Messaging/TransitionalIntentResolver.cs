/*
檔案整體說明
目的與定位：P3-06 唯一過渡意圖解析器，忠實保留 Golden 的舊 matcher 行為；P3-07 將以正式 IKeywordCatalog 接管路由。
主要功能與流程：來源 MessageRoutingKeywords.cs:350–410、MessageKeywordMatcher.cs:13–123。依 Exact、Prefix、六組 Helper、Contains 順序比對，只有路由使用 Trim；傳入業務的原文不變。Exact 保留原 FrozenDictionary 遺失 ignore-case 的行為，不引入 NFKC 或新的命令。
使用邊界與維護：詞彙僅來自本產品 resx 正式生成表，固定表無使用者狀態；不讀檔、不快取輸入、不建立 I/O 或背景資源。輸入有界，未命中明確回 Unknown；731 Golden 案例必須逐案驗證，不能以此命中結果取代 CRM 操作驗證。
*/
using System;
using System.Collections.Generic;
using SpeechMessageProducts.GoodShepherd.Core.Resources;

namespace SpeechMessageProducts.GoodShepherd.Core.Messaging;

/// <summary>無狀態舊路由相容器；只使用生成詞彙，不含正式 P3-07 的新正規化政策。</summary>
public static class TransitionalIntentResolver
{
    /// <summary>有界比對純輸入；結果為穩定意圖碼，不保存原文或任何租戶資料。</summary>
    public static string Resolve(string input)
    {
        if (input is null || input.Length > 16384) throw new ArgumentException("Invalid bounded message.", nameof(input));
        var candidate = input.Trim();
        foreach (var row in GoodShepherdTextData.Rows)
        {
            if (!row[0]!.StartsWith("Webhook.Route.", StringComparison.Ordinal)) continue;
            var parts = row[0]!.Split('.');
            var word = row[1]!;
            var mode = parts[4];
            var comparison = mode[mode.Length - 1] == 'I' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if ((mode.StartsWith("Exact", StringComparison.Ordinal) && string.Equals(candidate, word, comparison)) ||
                (mode.StartsWith("Prefix", StringComparison.Ordinal) && candidate.StartsWith(word, comparison)) ||
                (mode.StartsWith("Contains", StringComparison.Ordinal) && Contains(candidate, word, comparison)))
                return parts[3];
        }
        return "Unknown";
    }
    /// <summary>兩目標共用相同比較語義；Framework 沒有帶比較選項的 Contains 多載。</summary>
    internal static bool Contains(string input, string value, StringComparison comparison)
    {
#if NETFRAMEWORK
        return input.IndexOf(value, comparison) >= 0;
#else
        return input.Contains(value, comparison);
#endif
    }
}

/// <summary>提供固定中性協定詞彙及回覆模板鍵的組件內存取；不代替正式的語系／覆寫 catalog。</summary>
public static class WebhookText
{
    /// <summary>讀取生成的中性值；缺鍵直接失敗，不以顯示名稱建立動態快取。</summary>
    public static string Neutral(string key)
    {
        foreach (var row in GoodShepherdTextData.Rows)
            if (string.Equals(row[0], key, StringComparison.Ordinal)) return row[1]!;
        throw new KeyNotFoundException("The webhook resource key is unavailable.");
    }
}

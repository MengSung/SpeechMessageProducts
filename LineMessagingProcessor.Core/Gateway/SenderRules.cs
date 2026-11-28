/*
檔案整體說明
目的與定位：F5 推播出口的純值品牌格式政策；只能處理由上游授權查詢得到的租戶名稱及圖示，
本檔不執行授權、不讀取網路或資料庫，也不下載圖示。
主要功能與流程：深複製已由 Core 凍結的 SDK 訊息，移除 caller sender，再寫入租戶名稱、
可用 HTTPS 圖示；含 LINE 的名稱不設定 sender。Flex 替代文字保留完整前綴並限制 1500 UTF-16。
使用邊界與維護：候選 JToken 完全屬於當次操作，不保存到 static、快取或背景工作；函數沒有
可釋放資源。非法形狀整批拋出，不發布半成品；所有截斷不得切開 surrogate pair。
*/
using System;
using Newtonsoft.Json.Linq;

namespace LineMessagingProcessor.Core.Gateway;

/// <summary>權威租戶品牌的無狀態純函數；輸入名稱的授權由呼叫端完成。</summary>
public static class SenderRules
{
    /// <summary>固定的省略原因，不回傳或記錄可能含個資的原始租戶名稱。</summary>
    /// <param name="tenantDisplayName">經伺服器驗證、最多 200 UTF-16 的完整名稱。</param>
    /// <returns>名稱含 LINE 時回固定代碼，否則為 null。</returns>
    public static string? GetSenderOmissionReason(string tenantDisplayName)
    {
        ValidateName(tenantDisplayName);
#if NETFRAMEWORK
        return tenantDisplayName.IndexOf("LINE", StringComparison.OrdinalIgnoreCase) >= 0
#else
        return tenantDisplayName.Contains("LINE", StringComparison.OrdinalIgnoreCase)
#endif
            ? "sender-name-contains-line" : null;
    }

    /// <summary>
    /// 深複製已凍結訊息並套用 sender／Flex 政策，不修改 caller 或共用 SDK 模型。
    /// 原始 sender 一律移除，避免省略政策意外保留冒名值；圖示不合法時僅省略圖示。
    /// </summary>
    /// <param name="messages">一至五則、已經 Core 型別白名單序列化的訊息。</param>
    /// <param name="tenantDisplayName">權威完整租戶名稱，不以 scope 顯示值替代。</param>
    /// <param name="tenantIconUrl">可省略；只接受無 userinfo 的絕對 HTTPS URL，長度最多 2000。</param>
    /// <returns>只屬於當次操作的新候選，所有列驗證成功後才返回。</returns>
    public static JArray Apply(JArray messages, string tenantDisplayName, string? tenantIconUrl)
    {
#if NETFRAMEWORK
        if (messages is null) throw new ArgumentNullException(nameof(messages));
#else
        ArgumentNullException.ThrowIfNull(messages);
#endif
        if (messages.Count is < 1 or > 5) throw new ArgumentException("One through five messages are required.", nameof(messages));
        var omit = GetSenderOmissionReason(tenantDisplayName) is not null;
        var iconValid = tenantIconUrl is not null && tenantIconUrl.Length <= 2000
            && Uri.TryCreate(tenantIconUrl, UriKind.Absolute, out var icon)
            && string.Equals(icon.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(icon.UserInfo) && !string.IsNullOrEmpty(icon.Host);
        var prefix = "【" + tenantDisplayName + "】";
        var candidate = (JArray)messages.DeepClone();
        foreach (var item in candidate)
        {
            if (item is not JObject row || row["type"]?.Type != JTokenType.String)
                throw new ArgumentException("Invalid frozen message.", nameof(messages));
            row.Remove("sender");
            if (!omit)
            {
                var sender = new JObject { ["name"] = Truncate(tenantDisplayName, 20) };
                if (iconValid) sender["iconUrl"] = tenantIconUrl;
                row["sender"] = sender;
            }
            if (!string.Equals((string?)row["type"], "flex", StringComparison.Ordinal)) continue;
            if (row["altText"]?.Type != JTokenType.String)
                throw new ArgumentException("Flex alternate text must be a string.", nameof(messages));
            var text = (string)row["altText"]!;
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) text = prefix + text;
            row["altText"] = Truncate(text, 1500);
        }
        return candidate;
    }

    /// <summary>名稱上限與 account schema 對齊；先拒絕空值，避免產生無身分的 sender。</summary>
    private static void ValidateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            throw new ArgumentException("A bounded authoritative tenant name is required.", nameof(value));
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index])) continue;
            if (!char.IsHighSurrogate(value[index]) || index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                throw new ArgumentException("The tenant name must contain valid Unicode.", nameof(value));
            index++;
        }
    }

    /// <summary>保留最多指定 UTF-16 長度，不留下被截開的高 surrogate；不按目前文化轉換文字。</summary>
    private static string Truncate(string value, int maximum)
    {
        if (value.Length <= maximum) return value;
        var count = char.IsHighSurrogate(value[maximum - 1]) ? maximum - 1 : maximum;
        return value.Substring(0, count);
    }
}

/*
檔案整體說明
目的與定位：F5 Gateway 與 Dataverse adapter 共用的純值事件鍵契約，避免兩層各自猜分隔格式。
主要功能與流程：完整產品、Channel、正式租戶、業務事件與批次序號採固定 JSON 陣列計算
SHA256，再導出穩定 retry UUID。鍵不含訊息與名單，內容衝突由持久紀錄的獨立雜湊拒絕。
使用邊界與維護：只在同步呼叫內處理有界純值，不保存憑證、SDK、收件人或共用可變狀態；
雜湊演算法逐次釋放，鍵不能取代授權或資料庫正式 ID；同事件重試必須使用完全相同的鍵。
*/
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LineMessagingProcessor.Core.Gateway;

/// <summary>跨雙目標及 adapter 一致的事件鍵格式；格式變更會影響既有紀錄，不可隨意修改。</summary>
public static class SendBatchKeys
{
    /// <summary>只由已驗證範圍與業務事件建立鍵；-1 保留給不執行 HTTP 的政策拒絕紀錄。</summary>
    public static string Create(string product, string channelKey, Guid tenantId, string correlationId, int batchIndex)
    {
        if (string.IsNullOrWhiteSpace(product) || product.Length > 64 || tenantId == Guid.Empty)
            throw new ArgumentException("A fixed product and database tenant identity are required.", nameof(product));
        if (string.IsNullOrEmpty(channelKey) || channelKey.Length > 20)
            throw new ArgumentException("A bounded numeric channel is required.", nameof(channelKey));
        foreach (var character in channelKey)
            if (character is < '0' or > '9') throw new ArgumentException("A numeric channel is required.", nameof(channelKey));
        if (string.IsNullOrEmpty(correlationId) || correlationId.Length > 128)
            throw new ArgumentException("A bounded server event is required.", nameof(correlationId));
        foreach (var character in correlationId)
            if (character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z') and not (>= '0' and <= '9')
                and not '-' and not '_' and not ':' and not '.')
                throw new ArgumentException("A safe server event is required.", nameof(correlationId));
        if (batchIndex is < -1 or > 9) throw new ArgumentOutOfRangeException(nameof(batchIndex));
        var canonical = new JArray(product, channelKey, tenantId.ToString("D", CultureInfo.InvariantCulture),
            correlationId, batchIndex).ToString(Formatting.None);
        var bytes = new UTF8Encoding(false, true).GetBytes(canonical);
#if NETFRAMEWORK
        using var algorithm = SHA256.Create();
        var hash = algorithm.ComputeHash(bytes);
#else
        var hash = SHA256.HashData(bytes);
#endif
        var text = new StringBuilder(64);
        foreach (var item in hash) text.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        return text.ToString();
    }

    /// <summary>由完整小寫十六進位批次鍵導出固定 UUID；不是資料庫主鍵或新事件。</summary>
    public static Guid RetryKey(string batchKey)
    {
        if (batchKey is null || batchKey.Length != 64)
            throw new ArgumentException("A canonical SHA256 batch key is required.", nameof(batchKey));
        foreach (var character in batchKey)
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                throw new ArgumentException("A lowercase SHA256 batch key is required.", nameof(batchKey));
        return new Guid(batchKey.Substring(0, 8) + "-" + batchKey.Substring(8, 4) + "-4" + batchKey.Substring(13, 3)
            + "-a" + batchKey.Substring(17, 3) + "-" + batchKey.Substring(20, 12));
    }
}

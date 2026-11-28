/*
檔案整體說明
目的與定位：F5 單次操作的私有批次候選產生器，集中不可變內容與業務事件身分的分離。
主要功能與流程：完整名單、品牌及訊息做一次整案雜湊；各批以固定 JSON、當批名單雜湊與
不含 payload 的 batchkey 組成計畫。同事件換內容不能靠新 retry key 隱藏衝突。
使用邊界與維護：只處理 Gateway 已完成授權的有界快照，不存 static 資料；SHA256 owner
逐次 using 釋放。候選最多十批、每批最多五則有界訊息，返回集合不含 SDK 或 HTTP 資源。
雜湊不是匿名化授權機制，仍不得把原始名單、JSON 或 hash 集合放入共用診斷。
*/
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LineMessagingProcessor.Core.Gateway;

/// <summary>單批私有純值；body 含當次收件人及訊息，只活到本次 Send 完成。</summary>
internal sealed record PreparedPushBatch(SendBatchIdentity Identity, string Body, bool Multicast);

/// <summary>只在當次操作使用的穩定純值投影，沒有跨操作快取。</summary>
internal static class GatewayBatchFactory
{
    /// <summary>權威品牌也進入整案 hash，即使 sender 被政策省略仍不可冒用舊事件改品牌。</summary>
    internal static string OperationHash(string[] recipients, JArray messages, TenantPushFacts facts, MessageCategory category) =>
        Hash(new JObject
        {
            ["recipients"] = new JArray(recipients),
            ["messages"] = messages.DeepClone(),
            ["category"] = (int)category,
            ["tenantName"] = facts.DisplayName,
            ["tenantIcon"] = facts.IconUrl,
            ["testTenant"] = facts.IsTestTenant
        }.ToString(Formatting.None));

    /// <summary>在首批 claim 前完成所有 JSON 投影，序列化中途失敗不得先送部分批次。</summary>
    internal static PreparedPushBatch[] Create(PushScope scope, string correlationId, string[] recipients,
        JArray messages, MessageCategory category, string operationHash, string? senderNote)
    {
        var count = (recipients.Length + 499) / 500;
        var result = new PreparedPushBatch[count];
        for (var index = 0; index < count; index++)
        {
            var batch = new string[Math.Min(500, recipients.Length - index * 500)];
            Array.Copy(recipients, index * 500, batch, 0, batch.Length);
            var multicast = recipients.Length > 1;
            var body = new JObject
            {
                ["to"] = multicast ? new JArray(batch) : new JValue(batch[0]),
                ["messages"] = messages.DeepClone()
            }.ToString(Formatting.None);
            result[index] = new PreparedPushBatch(Identity(scope, correlationId, index, count,
                operationHash, Hash(body), Hash(new JArray(batch).ToString(Formatting.None)),
                category, batch.Length, messages.Count, senderNote), body, multicast);
        }
        return result;
    }

    /// <summary>前置拒絕使用 -1 獨立紀錄，不會被誤當合法 HTTP batch0。</summary>
    internal static SendBatchIdentity Rejection(PushScope scope, string correlationId, string[] recipients,
        JArray messages, MessageCategory category, string operationHash, string? senderNote, PushRejectedReason reason) =>
        Identity(scope, correlationId, -1, 1, operationHash, Hash(reason.ToString()),
            Hash(new JArray(recipients).ToString(Formatting.None)), category, recipients.Length, messages.Count, senderNote);

    /// <summary>JSON 陣列消除分隔符碰撞；UUID 只由固定事件鍵導出，不隨內容或重試改變。</summary>
    private static SendBatchIdentity Identity(PushScope scope, string correlationId, int index, int batchCount,
        string operationHash, string payloadHash, string recipientHash, MessageCategory category,
        int recipientCount, int messageCount, string? senderNote)
    {
        var key = SendBatchKeys.Create(scope.Product, scope.ChannelKey, scope.TenantId, correlationId, index);
        return new SendBatchIdentity(scope.Product, scope.ChannelKey, scope.TenantId, correlationId, index,
            batchCount, key, operationHash, payloadHash, recipientHash, SendBatchKeys.RetryKey(key), category,
            recipientCount, messageCount, senderNote);
    }

    /// <summary>跨目標一致的 SHA256；strict UTF-8 拒絕不完整 Unicode，演算法 owner 逐次釋放。</summary>
    private static string Hash(string value)
    {
        var input = new UTF8Encoding(false, true).GetBytes(value);
#if NETFRAMEWORK
        using var algorithm = SHA256.Create();
        var bytes = algorithm.ComputeHash(input);
#else
        var bytes = SHA256.HashData(input);
#endif
        var text = new StringBuilder(64);
        foreach (var item in bytes) text.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        return text.ToString();
    }
}

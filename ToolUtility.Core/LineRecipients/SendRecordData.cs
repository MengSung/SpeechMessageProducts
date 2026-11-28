/*
檔案整體說明
目的與定位：F5 發送紀錄的純 SDK 欄位映射與驗證，防止 caller hash 或錯誤列挪用正式 claim。
主要功能與流程：驗完整 immutable identity、canonical batch/retry 鍵與有界計數；Create
只含安全摘要，不保存 userId／訊息；讀回重建純值並驗正式主鍵、版本與固定產品。
使用邊界與維護：不執行 I/O、不存 static 可變集合；每次返回新 ColumnSet／Entity，呼叫端
獨佔候選。資料庫持久保存摘要的生命週期由主機核定政策管理，hash 不代表匿名化或授權。
缺欄、錯型別、未知選項與不一致版本全部拒絕；不以 display name 去重或猜預設值。
*/
using System;
using System.Globalization;
using LineMessagingProcessor.Core.Gateway;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.LineRecipients;

/// <summary>只處理有界純值的映射，沒有 SDK owner、資源或跨操作快取。</summary>
internal static class SendRecordData
{
    internal const string EntityName = "new_sendrecord";

    /// <summary>明列所有參與身份、終態與正式主鍵核對的欄，避免 AllColumns 無界暴露。</summary>
    internal static ColumnSet Columns() => new("new_sendrecordid", "new_name", "new_product", "new_tenant",
        "new_channelkey", "new_correlationid", "new_batchindex", "new_batchcount", "new_batchkey",
        "new_operationhash", "new_payloadhash", "new_recipienthash", "new_retrykey", "new_category",
        "new_recipientcount", "new_messagecount", "new_sendernote", "new_result", "new_errorcode", "new_attempts");

    /// <summary>不可相信 caller 自填 key；完整範圍重算及所有有界欄位通過後才可碰資料庫。</summary>
    internal static void Validate(SendBatchIdentity identity, string product)
    {
#if NETFRAMEWORK
        if (identity is null) throw new ArgumentNullException(nameof(identity));
#else
        ArgumentNullException.ThrowIfNull(identity);
#endif
        if (!string.Equals(identity.Product, product, StringComparison.Ordinal))
            throw new ArgumentException("Send record product mismatch.", nameof(identity));
        var key = SendBatchKeys.Create(identity.Product, identity.ChannelKey, identity.TenantId, identity.CorrelationId, identity.BatchIndex);
        if (!string.Equals(identity.BatchKey, key, StringComparison.Ordinal) || identity.RetryKey != SendBatchKeys.RetryKey(key)
            || !Hash(identity.OperationHash) || !Hash(identity.PayloadHash) || !Hash(identity.RecipientHash)
            || identity.BatchCount is < 1 or > 10 || identity.BatchIndex >= identity.BatchCount
            || (identity.BatchIndex == -1 && identity.BatchCount != 1)
            || identity.RecipientCount < 1 || identity.RecipientCount > (identity.BatchIndex == -1 ? 5000 : 500)
            || identity.MessageCount is < 1 or > 5
            || identity.Category is < MessageCategory.Transactional or > MessageCategory.Marketing
            || identity.SenderNote is not (null or "sender-name-contains-line"))
            throw new ArgumentException("Invalid immutable send batch identity.", nameof(identity));
    }

    /// <summary>只有占用初態被 Create，其他流程不能用 Upsert 或直接建立已接受結果。</summary>
    internal static Entity Candidate(SendBatchIdentity identity) => new(EntityName)
    {
        ["new_name"] = identity.BatchKey, ["new_product"] = identity.Product,
        ["new_tenant"] = new EntityReference("account", identity.TenantId),
        ["new_channelkey"] = identity.ChannelKey, ["new_correlationid"] = identity.CorrelationId,
        ["new_batchindex"] = identity.BatchIndex, ["new_batchcount"] = identity.BatchCount,
        ["new_batchkey"] = identity.BatchKey, ["new_operationhash"] = identity.OperationHash,
        ["new_payloadhash"] = identity.PayloadHash, ["new_recipienthash"] = identity.RecipientHash,
        ["new_retrykey"] = identity.RetryKey.ToString("D", CultureInfo.InvariantCulture),
        ["new_category"] = new OptionSetValue((int)identity.Category),
        ["new_recipientcount"] = identity.RecipientCount, ["new_messagecount"] = identity.MessageCount,
        ["new_sendernote"] = identity.SenderNote, ["new_result"] = new OptionSetValue((int)SendRecordOutcome.Claimed),
        ["new_attempts"] = 0
    };

    /// <summary>讀回正式 ID、完整 immutable 欄及版本；錯型別或缺值不得用 caller identity 補洞。</summary>
    internal static SendBatchIdentity Read(Entity row, Guid expectedId, string product)
    {
        DataversePushBoundary.RequireIdentity(row, EntityName, expectedId);
        if (row.GetAttributeValue<Guid>("new_sendrecordid") != expectedId || !Version(row.RowVersion))
            throw new InvalidOperationException("Send record database identity or version is missing.");
        var retry = row.GetAttributeValue<string>("new_retrykey");
        if (!Guid.TryParseExact(retry, "D", out var retryKey))
            throw new InvalidOperationException("Send record retry identity is missing.");
        var identity = new SendBatchIdentity(Text(row, "new_product"), Text(row, "new_channelkey"),
            DataversePushBoundary.Lookup(row, "new_tenant", "account"), Text(row, "new_correlationid"),
            Integer(row, "new_batchindex"), Integer(row, "new_batchcount"), Text(row, "new_batchkey"),
            Text(row, "new_operationhash"), Text(row, "new_payloadhash"), Text(row, "new_recipienthash"), retryKey,
            (MessageCategory)Choice(row, "new_category"), Integer(row, "new_recipientcount"),
            Integer(row, "new_messagecount"), row.GetAttributeValue<string>("new_sendernote"));
        Validate(identity, product);
        if (Text(row, "new_name") != identity.BatchKey)
            throw new InvalidOperationException("Send record primary name does not match its immutable key.");
        var outcome = Outcome(row);
        var attempts = Integer(row, "new_attempts");
        var errorCode = row.GetAttributeValue<string>("new_errorcode");
        if (outcome == SendRecordOutcome.Claimed)
        {
            if (attempts != 0 || errorCode is not null)
                throw new InvalidOperationException("Claimed send record contains inconsistent terminal facts.");
        }
        else ValidateCompletion(identity, new SendBatchCompletion(outcome, errorCode, attempts));
        return identity;
    }

    /// <summary>只有明定狀態可發布；未知數值不能被當成成功或可重新發送。</summary>
    internal static SendRecordOutcome Outcome(Entity row)
    {
        var value = (SendRecordOutcome)Choice(row, "new_result");
        return value is >= SendRecordOutcome.Claimed and <= SendRecordOutcome.Rejected
            ? value : throw new InvalidOperationException("Unknown send record outcome.");
    }

    /// <summary>終態只接受 Gateway 的固定安全代碼；拒絕紀錄與 HTTP 批次不可互換。</summary>
    internal static void ValidateCompletion(SendBatchIdentity identity, SendBatchCompletion completion)
    {
#if NETFRAMEWORK
        if (completion is null) throw new ArgumentNullException(nameof(completion));
#else
        ArgumentNullException.ThrowIfNull(completion);
#endif
        var validCode = completion.ErrorCode is "transport-unconfirmed" or "execution-unconfirmed";
        var code = completion.ErrorCode;
        if (code is not null && code.Length == 8 && code.StartsWith("http-", StringComparison.Ordinal))
            validCode = code[5] is >= '1' and <= '5' && code[6] is >= '0' and <= '9' && code[7] is >= '0' and <= '9';
        var rejection = completion.Outcome == SendRecordOutcome.Rejected && identity.BatchIndex == -1
            && completion.Attempts == 0 && code is "NonTestTenant" or "ForeignRecipient";
        var httpResult = identity.BatchIndex >= 0 && completion.Attempts is >= 0 and <= 4 &&
            (completion.Outcome == SendRecordOutcome.Accepted && completion.Attempts > 0 && code is null
            || completion.Outcome is SendRecordOutcome.Failed or SendRecordOutcome.Uncertain && validCode);
        if (!rejection && !httpResult) throw new ArgumentException("Invalid send batch completion.", nameof(completion));
    }

    /// <summary>以同終態、相同代碼與嘗試數界定冪等，不能把不同結果悄悄覆寫。</summary>
    internal static bool SameCompletion(Entity row, SendBatchCompletion completion) =>
        Outcome(row) == completion.Outcome && row.GetAttributeValue<string>("new_errorcode") == completion.ErrorCode
        && Integer(row, "new_attempts") == completion.Attempts;

    /// <summary>SDK RowVersion 為正整數字串，空白、負值與自造超長版本全部拒絕。</summary>
    internal static bool Version(string? value) => value is not null && value.Length <= 20
        && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version > 0;

    /// <summary>只接受完整小寫 SHA256；不替 caller 補值或標準化未知 hash。</summary>
    private static bool Hash(string? value)
    {
        if (value is null || value.Length != 64) return false;
        foreach (var character in value)
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        return true;
    }

    /// <summary>必填文字不可靠 null／預設值補出，資料庫候選不合法時停止發布。</summary>
    private static string Text(Entity row, string name) => row.GetAttributeValue<string>(name)
        ?? throw new InvalidOperationException("Required send record text is missing.");

    /// <summary>區分合法零與缺欄，批次0／嘗試0不能成為缺資料的隱性後備。</summary>
    private static int Integer(Entity row, string name) => row.Attributes.TryGetValue(name, out var value) && value is int number
        ? number : throw new InvalidOperationException("Required send record integer is missing.");

    /// <summary>未知或缺少的選項不能以列舉預設值解釋成接受。</summary>
    private static int Choice(Entity row, string name) => row.GetAttributeValue<OptionSetValue>(name)?.Value
        ?? throw new InvalidOperationException("Required send record option is missing.");
}

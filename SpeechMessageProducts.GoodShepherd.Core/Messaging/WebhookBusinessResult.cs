/*
檔案整體說明
目的與定位：定義 P3-06 領域處理的不可變結果及經主機驗證的事件範圍，供雙目標核心與 net10 adapter 使用。
主要功能與流程：區分已寫入、待 Reply 傳送、明確拒絕、舊入口刻意忽略與尚待裁決的業務；結果只保留穩定代碼、資源鍵、有界 detached 具名參數與正式記錄 ID，不把意圖命中當成業務完成。
使用邊界與維護：範圍值不是授權機制；adapter 先核對部署組織、教會與頻道，領域入口再查 Contact 的綁定及教會。資料最多存活至單筆 queue scope 排空，無 Session、快取、計時器、SDK 或憑證。Reply arguments 只供本筆 catalog 格式化，不保存原始結果或跨事件狀態。例外原樣交 queue 共用告警。
*/
using System;
using System.Collections.Generic;
using System.Linq;

namespace SpeechMessageProducts.GoodShepherd.Core.Messaging;

/// <summary>處理結果的業務分類；待傳送與待裁決不能被當作完成外部通知。</summary>
public enum WebhookBusinessStatus
{
    /// <summary>具體資料操作已完成；不表示 Reply API 已送達。</summary>
    Applied,
    /// <summary>已組成回覆意義，P3-08 尚須接通傳送。</summary>
    ReplyPending,
    /// <summary>預期業務拒絕且沒有該操作的寫入。</summary>
    Rejected,
    /// <summary>來源明確沒有業務，保留具名忽略證據。</summary>
    Ignored,
    /// <summary>缺少可證明的契約，尚未移植，不可記為成功。</summary>
    Deferred
}

/// <summary>
/// 單次回覆的有界具名參數。值只保存到本次 queue 事件完成，不能攜帶 Entity、token、request 或可變集合。
/// </summary>
public sealed class WebhookReplyArgument
{
    /// <summary>建立已驗證的具名參數；名稱與值都必須是有限純文字。</summary>
    /// <param name="name">資源鍵使用的 Ordinal 參數名稱。</param>
    /// <param name="value">本次格式化使用的純文字值。</param>
    public WebhookReplyArgument(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 ||
            name.Any(character => !IsAsciiLetterOrDigit(character) && character != '_'))
            throw new ArgumentException("A bounded reply argument name is required.", nameof(name));
        if (value is null || value.Length > 512 || value.Any(char.IsControl))
            throw new ArgumentException("A bounded reply argument value is required.", nameof(value));
        Name = name;
        Value = value;
    }

    /// <summary>以雙目標可用的明確範圍驗證參數名稱，避免依賴較新 runtime API。</summary>
    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    /// <summary>固定資源中的具名欄位。</summary>
    public string Name { get; }
    /// <summary>只供本次格式化的 bounded detached 值。</summary>
    public string Value { get; }
}

/// <summary>完成全部操作後才發布的純值；集合複製並唯讀包裝，避免 caller 修改其他事件結果。</summary>
public sealed class WebhookBusinessResult
{
    /// <summary>建立具名結果；記錄必須是真正資料庫 ID，且同一結果不得重複。</summary>
    public WebhookBusinessResult(
        WebhookBusinessStatus status,
        string code,
        string? replyKey = null,
        IEnumerable<Guid>? recordIds = null,
        IEnumerable<WebhookReplyArgument>? replyArguments = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A result code is required.", nameof(code));
        var ids = recordIds?.ToArray() ?? Array.Empty<Guid>();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new InvalidOperationException("Invalid result record identities.");
        if (replyKey is not null && (replyKey.Length == 0 || replyKey.Length > 256 ||
            replyKey.Any(char.IsControl)))
            throw new ArgumentException("A bounded reply key is required.", nameof(replyKey));
        var arguments = replyArguments?.ToArray() ?? Array.Empty<WebhookReplyArgument>();
        if (arguments.Length > 16 || arguments.Any(argument => argument is null) ||
            arguments.Select(argument => argument.Name).Distinct(StringComparer.Ordinal).Count() != arguments.Length)
            throw new InvalidOperationException("Reply arguments must be bounded and unique.");
        Status = status;
        Code = code;
        ReplyKey = replyKey;
        RecordIds = Array.AsReadOnly(ids);
        ReplyArguments = Array.AsReadOnly(arguments);
    }
    /// <summary>資料操作、回覆及刻意忽略分開標示，不隱藏待接通項目。</summary>
    public WebhookBusinessStatus Status { get; }
    /// <summary>固定安全診斷代碼，不含使用者、token、本文或資料庫欄位值。</summary>
    public string Code { get; }
    /// <summary>由 resx 生成的文字鍵；不是已傳送文字，語系與覆寫由 P3-08 正式 catalog 處理。</summary>
    public string? ReplyKey { get; }
    /// <summary>供 Reply catalog 使用的 detached 具名參數；不暴露可變來源陣列。</summary>
    public IReadOnlyList<WebhookReplyArgument> ReplyArguments { get; }
    /// <summary>本事件的正式記錄身分；不同 ID 即使名稱相同仍完整保留。</summary>
    public IReadOnlyList<Guid> RecordIds { get; }
}

/// <summary>主機已核對的事件範圍；不持有 CRM、request 或解密 token。</summary>
public sealed class WebhookActor
{
    /// <summary>保存完整組織、頻道、教會及 actor 邊界；所有值只屬目前事件，不建立共用狀態。</summary>
    public WebhookActor(Guid organizationId, string channelId, Guid churchId, Guid contactId, string userId, string timeZone)
    {
        if (organizationId == Guid.Empty || churchId == Guid.Empty || contactId == Guid.Empty ||
            string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(timeZone))
            throw new ArgumentException("A complete authorized actor boundary is required.");
        OrganizationId = organizationId; ChannelId = channelId; ChurchId = churchId;
        ContactId = contactId; UserId = userId; TimeZone = timeZone;
    }
    /// <summary>實際 CRM WhoAmI 核對的組織正式 ID。</summary>
    public Guid OrganizationId { get; }
    /// <summary>已驗簽且經部署及資料庫指派核對的供應商頻道。</summary>
    public string ChannelId { get; }
    /// <summary>伺服器授權的 account 正式 ID。</summary>
    public Guid ChurchId { get; }
    /// <summary>唯一匹配本教會綁定的 contact 正式 ID。</summary>
    public Guid ContactId { get; }
    /// <summary>已核對的 LINE actor，僅用於本事件及必要綁定複核，不作診斷。</summary>
    public string UserId { get; }
    /// <summary>部署的教會 IANA 時區，不使用伺服器 local。</summary>
    public string TimeZone { get; }
    /// <summary>固定遮罩，避免隱含記錄識別及個資。</summary>
    public override string ToString() => "WebhookActor(***)";
}

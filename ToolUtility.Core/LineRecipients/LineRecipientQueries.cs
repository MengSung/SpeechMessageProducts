/*
 * 檔案整體說明
 *
 * 目的與定位：P2-04 單人通知的最小欄位 SDK 查詢，提供 GoodShepherd 組合服務所需的
 * 會友綁定、教會頻道指派、加密 Token 與語系，不擁有連線或自行授權登入者。
 * 主要功能與流程：以正式主鍵讀 Contact，再以來源 Account 的 lookup 讀頻道；每次
 * 都使用明列 ColumnSet，驗回應表名／正式ID與資料型別後才回傳不可變純值。
 * 使用邊界與維護：借入的 SDK 由 Host／Plugin 驗證組織與身分並擁有，呼叫期間須有
 * transport deadline；同步 I/O 前後檢查取消，返回前已排空，不建立 Task.Run。
 * adapter只在當次通知保留借用SDK，Dispose清參照；不存查詢個資、憑證或快取，
 * 加密封包只由當次通知解密後即放棄參照，不記錄
 * 欄位值。真正故障向最終主機共用告警傳播，不把錯資料當查無或默認其他教會。
 */
using System;
using System.Threading;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;

namespace ToolUtilityNameSpace.LineRecipients;

/// <summary>單次讀出的頻道指派與語系純值，不包含SDK及已解密憑證。</summary>
public sealed class ChurchChannelRecord
{
    /// <summary>以完整候選建立不可變值，呼叫端限制加密封包至單次操作生命週期。</summary>
    public ChurchChannelRecord(Guid churchId, Guid assignedChannelId, Guid channelId, string churchName,
        string encryptedToken, ChurchLocaleSettings locales)
    {
        ChurchId = churchId; AssignedChannelId = assignedChannelId; ChannelId = channelId;
        ChurchName = churchName; EncryptedToken = encryptedToken; Locales = locales;
    }
    /// <summary>來源Account正式ID。</summary>
    public Guid ChurchId { get; }
    /// <summary>來源教會lookup的頻道資料庫ID。</summary>
    public Guid AssignedChannelId { get; }
    /// <summary>讀回頻道的正式ID，不能以供應商Channel ID代替。</summary>
    public Guid ChannelId { get; }
    /// <summary>只供字典sender參數使用的教會名稱。</summary>
    public string ChurchName { get; }
    /// <summary>已受長度預算約束的v1封包，禁止記錄或當成可長期共享設定。</summary>
    public string EncryptedToken { get; }
    /// <summary>與該教會同次讀取並複製的語系設定。</summary>
    public ChurchLocaleSettings Locales { get; }
    /// <summary>一般診斷不輸出加密封包、正式ID或教會名稱。</summary>
    public override string ToString() => "ChurchChannelRecord(***)";
}

/// <summary>GoodShepherd所借用的單次來源契約，結果只含不可變純值，不暴露SDK。</summary>
public interface ILineRecipientSource
{
    /// <summary>只取綁定、教會lookup及偏好語言，不查其他個資或AllColumns。</summary>
    MemberRecipient ReadMember(Guid contactId, CancellationToken ct);
    /// <summary>只讀來源教會指定的頻道，不以名稱掃描或由收件者傳入任意Token。</summary>
    ChurchChannelRecord ReadChannel(Guid sourceChurchId, CancellationToken ct);
}

/// <summary>單次通知的借用SDK來源；Dispose只清參照與停止新入口，不處置Host的SDK。</summary>
public sealed class LineRecipientQueries : ILineRecipientSource, IDisposable
{
    private IOrganizationService? service;
    /// <summary>Host必須先驗組織／身分並設定transport期限；此adapter只可在當次通知using中存活。</summary>
    public LineRecipientQueries(IOrganizationService service) =>
        this.service = service ?? throw new ArgumentNullException(nameof(service));
    /// <summary>只取綁定、教會lookup及偏好語言，不查其他個資或AllColumns。</summary>
    public MemberRecipient ReadMember(Guid contactId, CancellationToken ct)
    {
        var sdk = GetService();
        ct.ThrowIfCancellationRequested();
        if (contactId == Guid.Empty) throw new ArgumentException("A contact identity is required.", nameof(contactId));
        var member = sdk.Retrieve("contact", contactId,
            new ColumnSet("new_lineid", "parentcustomerid", "new_preferredlanguage"));
        ct.ThrowIfCancellationRequested();
        RequireIdentity(member, "contact", contactId);
        var churchId = RequireLookup(member, "parentcustomerid", "account");
        return new MemberRecipient(contactId, OptionalText(member, "new_lineid", 33), churchId,
            OptionalText(member, "new_preferredlanguage", 128));
    }
    /// <summary>只讀來源教會指定的頻道，不以名稱掃描或由收件者傳入任意Token。</summary>
    public ChurchChannelRecord ReadChannel(Guid sourceChurchId, CancellationToken ct)
    {
        var sdk = GetService();
        ct.ThrowIfCancellationRequested();
        if (sourceChurchId == Guid.Empty) throw new ArgumentException("A church identity is required.", nameof(sourceChurchId));
        var church = sdk.Retrieve("account", sourceChurchId,
            new ColumnSet("name", "new_linechannelid", "new_defaultlanguage", "new_enabledlanguages"));
        ct.ThrowIfCancellationRequested();
        RequireIdentity(church, "account", sourceChurchId);
        var channelId = RequireLookup(church, "new_linechannelid", "new_linechannel");
        var name = OptionalText(church, "name", 160);
        var defaultCulture = OptionalText(church, "new_defaultlanguage", 128);
        var enabled = OptionalText(church, "new_enabledlanguages", 8255);
        var names = string.IsNullOrWhiteSpace(enabled) ? new[] { "zh-TW" } : enabled!.Split(',');
        if (names.Length > 64) throw new InvalidOperationException("Culture count exceeds its budget.");
        for (var index = 0; index < names.Length; index++) names[index] = names[index].Trim();
        var locales = new ChurchLocaleSettings(string.IsNullOrWhiteSpace(defaultCulture) ? "zh-TW" : defaultCulture!, names);
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("A church name is required.");
        var channel = sdk.Retrieve("new_linechannel", channelId, new ColumnSet("new_accesstokenencrypted"));
        ct.ThrowIfCancellationRequested();
        RequireIdentity(channel, "new_linechannel", channelId);
        var envelope = OptionalText(channel, "new_accesstokenencrypted", 4000);
        if (string.IsNullOrWhiteSpace(envelope)) throw new InvalidOperationException("A channel envelope is required.");
        return new ChurchChannelRecord(sourceChurchId, channelId, channelId, name!, envelope!, locales);
    }

    /// <summary>取得本次局部借用；Dispose只阻擋新入口，已入場同步SDK由外層owner排空。</summary>
    private IOrganizationService GetService() =>
        Volatile.Read(ref service) ?? throw new ObjectDisposedException(nameof(LineRecipientQueries));

    /// <summary>只接受指定主鍵與實體，不把錯列、查無或空ID發布為另一位會友／教會。</summary>
    private static void RequireIdentity(Entity? row, string table, Guid id)
    {
        if (row is null || row.Id != id || !string.Equals(row.LogicalName, table, StringComparison.Ordinal))
            throw new InvalidOperationException("The source returned an unexpected record identity.");
    }

    /// <summary>lookup必須具有真正資料庫主鍵及指定目標表，顯示名稱和外部Channel代碼不構成身分。</summary>
    private static Guid RequireLookup(Entity row, string field, string table)
    {
        if (!row.Attributes.TryGetValue(field, out var value) || value is not EntityReference reference ||
            reference.Id == Guid.Empty || !string.Equals(reference.LogicalName, table, StringComparison.Ordinal))
            throw new InvalidOperationException("The source returned an invalid assignment.");
        return reference.Id;
    }

    /// <summary>在純值邊界驗證有界字串；缺值與型別錯誤不同，不使用ToString吞掉資料錯誤。</summary>
    private static string? OptionalText(Entity row, string field, int maximum)
    {
        if (!row.Attributes.TryGetValue(field, out var value) || value is null) return null;
        if (value is not string text || text.Length > maximum)
            throw new InvalidOperationException("The source returned an invalid bounded field.");
        return text;
    }
    /// <summary>停止新入口並清借用參照；已取得局部SDK的同步I/O須由Host等待排空。</summary>
    public void Dispose() => Interlocked.Exchange(ref service, null);
}

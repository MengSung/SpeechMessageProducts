/*
 * 檔案整體說明
 *
 * 目的與定位：P2-04 跨目標單一會友推播的純值契約，不讀 D365 或自行判定登入授權。
 * 主要功能與流程：recipient 保存資料庫 Contact／教會 ID 及當次 LINE 綁定；channel
 * 同時保存查詢所得的教會指派 ID 與實際頻道 ID，傳輸前核對兩者及來源教會。
 * 使用邊界與維護：資料只供單次呼叫，Host／Plugin 必須先驗證組織與服務身分；共用
 * Channel 不代表可跨教會借用 sender 或 recipient。憑證字串由外層限制至單次通知，
 * 不可序列化、記錄或快取；本檔無 I/O、timer、背景工作及需 Dispose 的 owner。
 * 語言只影響取字，不影響任何授權；所有真正故障由最終主機共用告警入口處理。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;

namespace LineMessagingProcessor.Core;

/// <summary>由已授權 SDK 查出的單一正式會友純值；LINE 綁定不是授權依據。</summary>
public sealed class MemberRecipient
{
    /// <summary>保存當次資料庫值；未綁定可為空，由通知入口回傳 NotBound。</summary>
    public MemberRecipient(Guid contactId, string? lineUserId, Guid churchId, string? language)
    {
        ContactId = contactId;
        LineUserId = lineUserId;
        ChurchId = churchId;
        Language = language;
    }
    /// <summary>正式 Contact 主鍵，不是 LINE ID 或重試鍵。</summary>
    public Guid ContactId { get; }
    /// <summary>當次綁定值，只用於當次傳輸，不記錄或保留到下一次通知。</summary>
    public string? LineUserId { get; }
    /// <summary>會友資料庫所屬教會正式 ID。</summary>
    public Guid ChurchId { get; }
    /// <summary>原始語言偏好，須再由既有 resolver 核對啟用與發布政策。</summary>
    public string? Language { get; }
    /// <summary>避免一般診斷意外輸出 LINE ID 或正式身分。</summary>
    public override string ToString() => "MemberRecipient(***)";
}

/// <summary>當次已授權教會的頻道指派快照；同一頻道可被不同教會獨立指派。</summary>
public sealed class ChurchChannel
{
    /// <summary>建立操作純值，不自行查詢或驗證呼叫者權限，亦不接管憑證生命週期。</summary>
    public ChurchChannel(Guid churchId, Guid assignedChannelId, Guid channelId,
        LineChannelCredential credential, string senderName)
    {
        ChurchId = churchId;
        AssignedChannelId = assignedChannelId;
        ChannelId = channelId;
        Credential = credential ?? throw new ArgumentNullException(nameof(credential));
        SenderName = senderName;
    }
    /// <summary>讀取 Account 指派時的教會正式 ID。</summary>
    public Guid ChurchId { get; }
    /// <summary>Account.new_linechannelid 的實際資料庫 lookup ID。</summary>
    public Guid AssignedChannelId { get; }
    /// <summary>讀出的 new_linechannel 正式主鍵，必須與指派 ID 相等。</summary>
    public Guid ChannelId { get; }
    /// <summary>僅供當次請求使用的已解密憑證，不可存入 client 預設標頭。</summary>
    internal LineChannelCredential Credential { get; }
    /// <summary>由發送教會設定導出的名稱，不採用呼叫端訊息內原有 sender。</summary>
    public string SenderName { get; }
    /// <summary>一般診斷不顯示憑證、教會、頻道或名稱。</summary>
    public override string ToString() => "ChurchChannel(***)";
}

/// <summary>單一會友的教會核對傳輸入口，不包含群播、圖片產生或背景重試。</summary>
public interface ILineMemberPushClient
{
    /// <summary>兩項教會核對通過後，凍結當次訊息與 sender 再傳輸一次；取消與 HTTP 分類沿用 Core。</summary>
    Task<LinePushResult> PushToMemberAsync(MemberRecipient recipient, ChurchChannel channel, Guid sourceChurchId,
        IReadOnlyList<ISendMessage> messages, Guid retryKey, CancellationToken ct);
}

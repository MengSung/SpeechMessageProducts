/*
 * 檔案整體說明
 *
 * 目的與定位：P2-04 GoodShepherd 單一會友通知組合服務，連接最小SDK查詢、Token
 * 解密、語言政策、字典與Core教會核對傳輸，不自行建立D365或HTTP連線。
 *
 * 主要功能與流程：先讀正式Contact，未綁定或教會不符即拒絕；再讀來源教會指派，
 * 決定語言、以字典組文字及sender、解密Token，沿用來源正式記錄ID作Retry Key。
 *
 * 使用邊界與維護：只借用無request狀態的resolver、catalog與push client；SDK及key
 * 僅在同步準備期間借用，不被async狀態機或服務欄位保存。HTTP輸入純值最多存活至
 * 十秒傳輸排空；不快取會友／Token、不建立queue。查詢與解密例外原樣交主機共用告警，
 * caller取消明確區分。Host須先授權完整組織／產品／來源教會並擁有SDK的transport期限。
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using SpeechMessage.Localization;
using ToolUtilityNameSpace.LineRecipients;

namespace SpeechMessageProducts.GoodShepherd.Core.Notifications;

/// <summary>無操作狀態的會友通知組合服務，所有依賴由Host擁有且必須不捕捉request。</summary>
public sealed class MemberNotificationService
{
    private readonly ILineMemberPushClient push;
    private readonly IMemberCultureResolver cultures;
    private readonly ITextCatalog catalog;

    /// <summary>只借用共用策略及client；不接管連線、catalog或任何秘密的所有權。</summary>
    public MemberNotificationService(ILineMemberPushClient push, IMemberCultureResolver cultures, ITextCatalog catalog)
    {
        this.push = push ?? throw new ArgumentNullException(nameof(push));
        this.cultures = cultures ?? throw new ArgumentNullException(nameof(cultures));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>同步準備後直接交回傳輸Task，不讓SDK或TokenKey被async狀態機保留；來源context必須先經Host授權。</summary>
    public Task<LinePushResult> NotifyAsync(ILineRecipientSource source, TextContext sourceContext, Guid contactId,
        Guid sourceRecordId, byte[] tokenKey, TextKey messageKey, CancellationToken ct)
    {
#if NETFRAMEWORK
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (sourceContext is null) throw new ArgumentNullException(nameof(sourceContext));
#else
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceContext);
#endif
        ct.ThrowIfCancellationRequested();
        if (contactId == Guid.Empty) throw new ArgumentException("A contact identity is required.", nameof(contactId));
        if (sourceRecordId == Guid.Empty) throw new ArgumentException("An event identity is required.", nameof(sourceRecordId));
        var member = source.ReadMember(contactId, ct);
        ct.ThrowIfCancellationRequested();
        if (member is null || member.ContactId != contactId)
            throw new InvalidOperationException("The source returned an unexpected contact.");
        if (member.ChurchId != sourceContext.ChurchId) return Task.FromResult(LinePushResult.Reject("ChurchMismatch"));
        if (string.IsNullOrWhiteSpace(member.LineUserId)) return Task.FromResult(LinePushResult.Reject("NotBound"));
        var record = source.ReadChannel(sourceContext.ChurchId, ct);
        ct.ThrowIfCancellationRequested();
        if (record is null || record.ChurchId != sourceContext.ChurchId || record.ChannelId == Guid.Empty ||
            record.AssignedChannelId != record.ChannelId)
            return Task.FromResult(LinePushResult.Reject("ChurchMismatch"));
        var resolved = cultures.Resolve(new MemberLanguagePreference(member.Language), record.Locales);
        var context = sourceContext.WithCultures(resolved.TextCulture, resolved.FormatCulture);
        var text = catalog.GetText(context, messageKey);
        var sender = catalog.GetText(context, new TextKey("Line.Sender.Template"), new TextArg("churchName", record.ChurchName));
        var credential = new LineChannelCredential(ChannelTokenProtector.Unprotect(record.EncryptedToken, tokenKey));
        var channel = new ChurchChannel(record.ChurchId, record.AssignedChannelId, record.ChannelId, credential, sender);
        // 舊SDK建構式會靜默截到2000字；用屬性保留完整字典值，由Core以5000字規格明確驗證。
        return push.PushToMemberAsync(member, channel, sourceContext.ChurchId,
            new ISendMessage[] { new TextMessage(string.Empty) { Text = text } }, sourceRecordId, ct);
    }
}

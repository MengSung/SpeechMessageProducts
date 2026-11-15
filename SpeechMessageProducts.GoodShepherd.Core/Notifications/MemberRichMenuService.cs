/*
 * 檔案整體說明
 *
 * 目的與定位：P5-04 GoodShepherd 會友語言換綁組合服務，借用最小收件來源、語系政策及
 * 既有 LINE client，把當下偏好解析成共用 alias；不建立圖片、連線或新的傳輸抽象。
 * 主要功能與流程：先核對正式 Contact、來源教會、LINE 綁定與頻道指派，再解析語系並呼叫
 * RichMenuAlias.Build。非法 alias 轉為既有固定業務資源鍵，兩模式皆拒絕且不回顯輸入。
 * Shadow 只同步交付 Contact GUID 與 alias；Live 才解密 Token 並呼叫既有 alias 查詢換綁。
 * 使用邊界與維護：Host 先授權組織／產品／教會並擁有 SDK deadline、client 與金鑰；本服務
 * 只保存無操作狀態的 client／resolver。來源、key、sink 僅在同步準備階段借用，不由 async
 * 狀態機、快取或 queue 保留。HTTP 必要純值存活至既有每次十秒傳輸排空；request、response
 * 與取消來源仍由 client using 唯一釋放。未知查詢、解密與回呼例外原樣交 Host 共用告警；
 * caller 取消在 I/O 與發布邊界檢查，不吞成成功。離線雙目標測試保護先驗拒絕與影子零解密。
 */
using System;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using LineMessagingProcessor.Core.Security;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.LineRecipients;
namespace SpeechMessageProducts.GoodShepherd.Core.Notifications;
/// <summary>單次會友 alias 換綁的無狀態組合服務；來源授權及依賴生命週期由 Host 擁有。</summary>
public sealed class MemberRichMenuService
{
    /// <summary>借用無使用者狀態的既有傳輸 client，不處置 Host 的連線池。</summary>
    private readonly LinePushClient client;
    /// <summary>借用純語系政策；不得快取本次會友偏好或教會設定。</summary>
    private readonly IMemberCultureResolver cultures;
    /// <summary>只借用共用策略與 client；不建立或保存 SDK、教會、金鑰與 request 狀態。</summary>
    /// <param name="client">由 Host 擁有且預設標頭不含使用者／教會憑證的 client。</param>
    /// <param name="cultures">依已開放與已啟用交集決定文字語言的純政策。</param>
    public MemberRichMenuService(LinePushClient client, IMemberCultureResolver cultures)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.cultures = cultures ?? throw new ArgumentNullException(nameof(cultures));
    }
    /// <summary>
    /// 同步驗證及準備後交回既有傳輸 Task，避免 source／key／sink 被非同步狀態機保留。
    /// 恰好選擇一種模式：Live 提供 key，Shadow 提供 sink。兩模式先執行相同收件者及頻道
    /// 核對；未綁定不讀頻道，錯配不解析 alias 或解密。Shadow 回呼只接安全意圖純值，
    /// 其結果為 Rejected／ShadowRecorded 且 Success=false；Live 的 HTTP 分類沿用 client。
    /// </summary>
    /// <param name="source">已授權的當次來源，Host 負責其 SDK 逾時、排空與 Dispose。</param>
    /// <param name="sourceContext">已驗證的組織／產品／來源教會範圍，語言不作授權。</param>
    /// <param name="contactId">正式 Contact GUID，必須與讀回身分相同。</param>
    /// <param name="churchCode">伺服器讀取的教會代碼，禁止小寫清洗或截斷。</param>
    /// <param name="combination">伺服器決定的版面組合識別。</param>
    /// <param name="tab">伺服器決定的頁籤識別。</param>
    /// <param name="tokenKey">Live 的當次解密 key；Shadow 必須為 null，不保存或修改。</param>
    /// <param name="shadowAliasSink">Shadow 的同步 Contact／alias 回呼；Live 必須為 null，不保存。</param>
    /// <param name="ct">明確 caller 取消訊號；正常取消不轉成傳輸成功。</param>
    /// <returns>既有 Live 傳輸 Task，或完成的固定本地拒絕／影子結果。</returns>
    /// <exception cref="BusinessRuleException">alias 不符共用規則，使用無輸入內容的固定資源鍵。</exception>
    public Task<LinePushResult> LinkAsync(ILineRecipientSource source, TextContext sourceContext,
        Guid contactId, string churchCode, string combination, string tab, byte[]? tokenKey,
        Action<Guid, string>? shadowAliasSink, CancellationToken ct)
    {
#if NETFRAMEWORK
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (sourceContext is null) throw new ArgumentNullException(nameof(sourceContext));
#else
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceContext);
#endif
        if ((tokenKey is null) == (shadowAliasSink is null))
            throw new ArgumentException("Exactly one rich menu mode is required.");
        ct.ThrowIfCancellationRequested();
        if (contactId == Guid.Empty) throw new ArgumentException("A contact identity is required.", nameof(contactId));
        var member = source.ReadMember(contactId, ct);
        ct.ThrowIfCancellationRequested();
        if (member is null || member.ContactId != contactId)
            throw new InvalidOperationException("The source returned an unexpected contact.");
        var rejection = LinePushClient.ValidateRecipient(member, sourceContext.ChurchId);
        if (rejection is not null) return Task.FromResult(rejection);
        var record = source.ReadChannel(sourceContext.ChurchId, ct);
        ct.ThrowIfCancellationRequested();
        if (record is null || record.ChurchId != sourceContext.ChurchId || record.ChannelId == Guid.Empty ||
            record.AssignedChannelId != record.ChannelId)
            return Task.FromResult(LinePushResult.Reject("ChurchMismatch"));
        var resolved = cultures.Resolve(new MemberLanguagePreference(member.Language), record.Locales);
        ct.ThrowIfCancellationRequested();
        string alias;
        try { alias = RichMenuAlias.Build(churchCode, combination, tab, resolved.TextCulture); }
        catch (ArgumentException)
        {
            // 不保留原例外或輸入代碼；可預期的命名拒絕由外殼以既有具名資源顯示。
            throw new BusinessRuleException(new TextKey("Common.Plugin.RuleRejected"));
        }
        ct.ThrowIfCancellationRequested();
        if (shadowAliasSink is not null)
        {
            shadowAliasSink(contactId, alias);
            return Task.FromResult(LinePushResult.ShadowRecorded());
        }
        var credential = new LineChannelCredential(ChannelTokenProtector.Unprotect(record.EncryptedToken, tokenKey!));
        // 換綁不組訊息也不使用 sender；空純值不讀取或保留教會顯示名稱。
        var channel = new ChurchChannel(record.ChurchId, record.AssignedChannelId, record.ChannelId, credential, string.Empty);
        return client.LinkRichMenuToUserAsync(member, channel, sourceContext.ChurchId, alias, ct);
    }
}

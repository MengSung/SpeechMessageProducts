/*
 * 檔案整體說明
 * 目的與定位：P5-07 課程 Plugin／活動共用的簽核分派與通知服務，保留舊
 * MajorExecute 的訊息加參數 Contains 優先序。只有新增通知本人，其餘已識別
 * 課程簽核／更新通知小組長；舊付款fallback依P5-PROP-006不在D365實作，
 * 明確識別並由活動Shadow記核定缺口、Live拒絕，不冒送簽核或建立付款。
 * 主要功能與流程：先驗證分派及傳輸模式，使用平台使用者的借用 SDK 重讀正式
 * stor→member→list→leader，從 member.parentcustomerid 推導教會。收件者再經共用
 * LINE recipient 驗證及 MemberNotificationService 重驗後，只組合一次共用 Flex。
 * 使用邊界與維護：參數僅決定業務，不提供教會或資料存取權。SDK、純值、通知
 * client 由外殼借入，recipient source 屬本次有界呼叫；source 在 await 排空後 using 釋放，
 * 平台 SDK 不 Dispose。key 只借用，呼叫端 finally 清零；Shadow 不解密、不推播。
 * 不存 static 可變資料、Session、cache、背景 queue 或 timer；不讀來源列 token，
 * 也不回落固定收件者。取消交既有通知契約，真傳輸例外保留原實例交外殼處理。
 * 驗證方式：真外殼離線測試以不同學員／組長 ID、付款拒絕及跨教會故障保護分派，
 * Core 具名簽核測試保護訊息內容；不宣稱 D365 或 LINE 實機送達。
 */
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.LineRecipients;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>僅在當次通知期間借用 SDK 與 key 的共用分派；所有來源由服務端正式 lookup 決定。</summary>
public static class LessonSigningDispatch
{
    /// <summary>核定缺口的固定繁中標記，從中性資源取得，不帶來源或credential。</summary>
    public static string ApprovedGapMarker => LessonText.Get("Lessons.Signing.ApprovedGapMarker");

    /// <summary>Plugin只依平台Create／Update選擇既有分派參數，業務文字由固定資源取得。</summary>
    public static string DefaultParameter(string message) => message switch
    {
        "Create" => LessonText.Get("Lessons.Signing.CreateParameter"),
        "Update" => LessonText.Get("Lessons.Signing.UpdateParameter"),
        _ => throw new InvalidPluginExecutionException("Lesson signing message is unsupported.")
    };

    /// <summary>
    /// 保留舊 Contains 語意及課程簽核優先序，回傳是否通知組長。
    /// 未識別參數在舊版走付款fallback，本通知入口仍在查詢前拒絕；
    /// 活動須以TryNotifyLeader識別核定缺口，不把付款當作普通通知。
    /// </summary>
    /// <param name="message">平台訊息，不能以 caller 提供值取代。</param>
    /// <param name="parameter">本次活動求值或 Plugin 依平台訊息產生的參數，僅供業務分派。</param>
    /// <returns>課程簽核／更新為 true，新增為 false；其他情況不回傳正常簽核路徑。</returns>
    public static bool NotifyLeader(string message, string? parameter)
    {
        if (TryNotifyLeader(message, parameter, out var leader)) return leader;
        throw new InvalidPluginExecutionException(ApprovedGapMarker);
    }

    /// <summary>
    /// 純分派辨識，不讀SDK、token或呼叫金流。已知簽核回true並提供收件者方向；
    /// 舊付款fallback回false，活動據此發布固定核定缺口，不能冒充功能或黃金比對完成。
    /// 非法訊息、null或超量參數仍拒絕，不把不可信輸入一概包成正常付款。
    /// </summary>
    /// <param name="message">平台提供的固定業務訊息。</param>
    /// <param name="parameter">當次求值字串，不提供授權或credential。</param>
    /// <param name="leader">只在回true時有效；不保存到跨呼叫狀態。</param>
    /// <returns>true代表普通簽核通知；false代表已核定不實作的付款fallback。</returns>
    public static bool TryNotifyLeader(string message, string? parameter, out bool leader)
    {
        leader = false;
        if (parameter is null || parameter.Length > 2000)
            throw new InvalidPluginExecutionException("Lesson signing parameter is invalid.");
        if (message is not ("ExecuteWorkflow" or "Create" or "Update"))
            throw new InvalidPluginExecutionException("Lesson signing message is unsupported.");
        if (Contains(parameter, LessonText.Get("Lessons.Signing.SigningParameter"))) { leader = true; return true; }
        if (message == "Create" && Contains(parameter, LessonText.Get("Lessons.Signing.CreateParameter"))) return true;
        if (message == "Update" && Contains(parameter, LessonText.Get("Lessons.Signing.UpdateParameter"))) { leader = true; return true; }
        return false;
    }

    /// <summary>
    /// 完整解析一次來源並同步 await 單一通知；key 與 shadowSink 必須恰有一種，不能
    /// 以缺 key 的 Live 假稱成功。lookup 驗證不取代平台使用者授權及收件者教會重驗。
    /// </summary>
    /// <param name="organization">由平台依本次使用者授權借用的 SDK，服務不擁有其生命週期。</param>
    /// <param name="push">外殼／Host 擁有的無使用者狀態傳輸 client，Core 不建立或關閉連線池。</param>
    /// <param name="storId">平台主要課程紀錄正式 ID，不能以活動參考參數覆寫。</param>
    /// <param name="organizationId">平台組織正式 ID，僅傳入通知 context，不由業務參數指定。</param>
    /// <param name="message">平台訊息。</param>
    /// <param name="parameter">已評估的本次業務參數。</param>
    /// <param name="shadowSink">Shadow 當次 recorder，Live 必須為 null，由外殼排空釋放。</param>
    /// <param name="key">Live 借用的 64-byte key，Shadow 必須為 null，呼叫端負責清零。</param>
    /// <param name="ct">呼叫端取消；不保留註冊或建立脫離呼叫的背景工作。</param>
    public static async Task NotifyAsync(IOrganizationService organization, ILineMemberPushClient push, Guid storId,
        Guid organizationId, string message, string? parameter, IShadowPushSink? shadowSink,
        byte[]? key, CancellationToken ct)
    {
#if NETFRAMEWORK
        if (organization is null) throw new ArgumentNullException(nameof(organization));
        if (push is null) throw new ArgumentNullException(nameof(push));
#else
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(push);
#endif
        var leader = NotifyLeader(message, parameter);
        if (storId == Guid.Empty || organizationId == Guid.Empty)
            throw new InvalidPluginExecutionException("Lesson signing identity is invalid.");
        if ((shadowSink is null) == (key is null) || (key is not null && key.Length != 64))
            throw new InvalidPluginExecutionException("Lesson signing secure mode is unavailable.");
        ct.ThrowIfCancellationRequested();
        var stor = organization.Retrieve("new_stor_lessons", storId,
            new ColumnSet("new_contact_new_stor_lessons", "new_new_disciple_lessons_new_stor_les"));
        var memberId = Lookup(stor, "new_contact_new_stor_lessons", "contact");
        var courseId = Lookup(stor, "new_new_disciple_lessons_new_stor_les", "new_disciple_lessons");
        var member = organization.Retrieve("contact", memberId,
            new ColumnSet("fullname", "new_cell_list_contact", "parentcustomerid", "new_equipment_status"));
        var churchId = Lookup(member, "parentcustomerid", "account");
        var recipientId = memberId;
        var signer = member;
        if (leader)
        {
            var list = organization.Retrieve("list", Lookup(member, "new_cell_list_contact", "list"),
                new ColumnSet("new_contact_family_leader_list"));
            recipientId = Lookup(list, "new_contact_family_leader_list", "contact");
            signer = organization.Retrieve("contact", recipientId, new ColumnSet("fullname"));
        }
        using var source = new LineRecipientQueries(organization);
        var recipient = source.ReadMember(recipientId, ct);
        if (LinePushClient.ValidateRecipient(recipient, churchId) is not null) return;
        var course = organization.Retrieve("new_disciple_lessons", courseId,
            new ColumnSet("new_name", "new_picture_link"));
        var account = organization.Retrieve("account", churchId, new ColumnSet("new_defaultlanguage"));
        var culture = OptionalText(account, "new_defaultlanguage", "zh-TW");
        var context = new TextContext(churchId, culture, culture, organizationId.ToString("D"), "GoodShepherd");
        var values = new LessonSigningMessageInput(storId, recipientId, Text(course, "new_name"),
            Text(member, "fullname"), OptionalText(member, "new_equipment_status", LessonText.Get("Lessons.Signing.EquipmentUnset")),
            Text(course, "new_picture_link"), Text(signer, "fullname"));
        var notifications = new MemberNotificationService(push,
            new MemberCultureResolver(new LocalizationOptions()), new GoodShepherdTextCatalog(new NoOverrides()));
        IReadOnlyList<ISendMessage> Compose(TextContext _) =>
            new ISendMessage[] { LessonSigningMessageFactory.Create(values) };
        var retryKey = DeterministicGuid.Create("lessons.signing", storId, recipientId);
        var result = shadowSink is not null
            ? await notifications.NotifyComposedAsync(source, context, recipientId, retryKey,
                "Lessons.Signing", Compose, shadowSink, ct).ConfigureAwait(false)
            : await notifications.NotifyComposedAsync(source, context, recipientId, retryKey,
                "Lessons.Signing", Compose, key!, ct).ConfigureAwait(false);
        if (result.TransportException is { } transport) ExceptionDispatchInfo.Capture(transport).Throw();
        if (!result.Success && result.Reason is not ("NotBound" or "ChurchMismatch" or "ShadowRecorded"))
            throw new InvalidPluginExecutionException("Lesson signing notification failed.");
    }

    /// <summary>兩目標均採 ordinal Contains，不讓執行環境文化改變舊業務關鍵字分派。</summary>
    private static bool Contains(string value, string keyword)
    {
#if NETFRAMEWORK
        return value.IndexOf(keyword, StringComparison.Ordinal) >= 0;
#else
        return value.Contains(keyword, StringComparison.Ordinal);
#endif
    }

    /// <summary>只接受目標表精確符合且具正式 ID 的 lookup；不接受 alternate key 冒充列識別。</summary>
    private static Guid Lookup(Entity row, string field, string table) =>
        row.Attributes.TryGetValue(field, out var value) && value is EntityReference reference &&
        reference.LogicalName == table && reference.Id != Guid.Empty && reference.KeyAttributes.Count == 0
            ? reference.Id : throw new InvalidPluginExecutionException("Lesson signing source scope is invalid.");

    /// <summary>必需文字缺少或超出上限時拒絕，不在例外回顯原始來源。</summary>
    private static string Text(Entity row, string field) =>
        row.Attributes.TryGetValue(field, out var value) && value is string text &&
        !string.IsNullOrWhiteSpace(text) && text.Length <= 2000
            ? text : throw new InvalidPluginExecutionException("Lesson signing source text is invalid.");

    /// <summary>語系／顯示狀態可缺省，固定非敏感後備值避免保留其他操作的值。</summary>
    private static string OptionalText(Entity row, string field, string fallback) =>
        row.Attributes.TryGetValue(field, out var value) && value is string text &&
        !string.IsNullOrWhiteSpace(text) && text.Length <= 2000 ? text : fallback;

    /// <summary>共用固定文字策略，每次回傳獨立空快照，不留跨教會可變覆寫。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>只建立當次 context 的空覆寫，不保存 context 或來源物件。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}

/*
檔案整體說明
目的與定位：提供 P3-06 文字事件的具體領域入口，以真正 CRM SDK 操作重用 ToolUtility.Core 查詢，供 Plugin 與 net10 主機共用。
主要功能與流程：主機先授權事件範圍，本入口複核會友綁定與教會，依唯一過渡意圖路由執行各領域規則。只有完整成功才發布純值結果；排除註冊及待後續接通的項目採具名結果，不偽造資料成功。
使用邊界與維護：SDK 由事件 scope 擁有，本類不保存 Entity、使用者或憑證；同步 I/O 前後查取消且等待操作排空，transport deadline 屬主機。例外不吞掉、不另建 notifier；資料列以正式 ID 驗證，更新須 rowversion，避免併行覆寫。測試以窄假 SDK 驗證參數與實際異動，不代表實機。
*/
using System;
using System.Threading;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Attendance;

namespace SpeechMessageProducts.GoodShepherd.Core.Messaging;

/// <summary>單事件文字處理器；只有無操作狀態的時鐘依賴，SDK 由每次呼叫明確借入。</summary>
public sealed class WebhookTextHandler
{
    private readonly IChurchClock clock;
    private readonly IKeywordCatalog keywordCatalog;

    /// <summary>
    /// 借入共用教會時鐘與已驗證關鍵字 catalog，不取得時鐘或 catalog 的外部資源所有權。
    /// 這個相容入口只供既有離線呼叫端使用；正式 Host 由 DI 注入 singleton catalog。
    /// </summary>
    public WebhookTextHandler(IChurchClock clock) : this(clock, GoodShepherdKeywordCatalog.Create()) { }

    /// <summary>
    /// 建立單事件文字處理器；catalog 只含固定資源，不能攜帶 request、actor 或租戶狀態。
    /// </summary>
    /// <param name="clock">供領域操作使用的借入時鐘。</param>
    /// <param name="keywordCatalog">由組合根提供的不可變關鍵字 catalog。</param>
    public WebhookTextHandler(IChurchClock clock, IKeywordCatalog keywordCatalog)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.keywordCatalog = keywordCatalog ?? throw new ArgumentNullException(nameof(keywordCatalog));
    }

    /// <summary>
    /// 保留沒有 display source 的相容入口；單人訊息以 actor Contact 作為顯示來源，
    /// 群組／聊天室文字則由 Host 呼叫下方多來源 overload。
    /// </summary>
    public WebhookBusinessResult Handle(IOrganizationService service, WebhookActor actor, string description, CancellationToken ct) =>
        Handle(service, actor, null, actor.UserId, description, ct);

    /// <summary>
    /// 執行一筆已授權訊息；actor 負責 CRM 授權，display source 僅供需要顯示名稱的
    /// 舊業務使用。兩者都必須由 Host resolver 以組織／教會／Contact 正式資料驗證。
    /// </summary>
    public WebhookBusinessResult Handle(
        IOrganizationService service,
        WebhookActor actor,
        Entity? displayContact,
        string displaySourceId,
        string description,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var intent = keywordCatalog.TryMatch("zh-TW", Array.Empty<string>(), description, out var matchedIntent)
            ? matchedIntent
            : "Unknown";
        if (intent == "Registration") return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "RegistrationExcluded");
        if (intent is "Help" or "QueryEnrollment" or "QrCode" or "UserReply" or "NotifyLink")
            return new WebhookBusinessResult(WebhookBusinessStatus.ReplyPending, intent + "LegacyReplyPendingP308", "Webhook.Reply." + intent + ".V0");
        if (intent == "QueryUserId") return new WebhookBusinessResult(WebhookBusinessStatus.ReplyPending, "ActorIdReplyPendingP308");
        if (intent == "Prayer247") return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "Prayer247BusinessHandlerNotMigrated");
        if (intent == "Unknown") return new WebhookBusinessResult(WebhookBusinessStatus.Ignored, "UnknownText");
        var actorContact = WebhookRecordOperations.ReadActor(service, actor, ct);
        switch (intent)
        {
            case "SundayAttendance": case "SmallGroupAttendance": case "Rpg": case "Devotion": case "PrayerRequest": case "StaffReport":
                return WebhookAttendanceHandler.Handle(service, actor, intent, description, clock, ct);
            case "ClassAttendance":
                return WebhookCourseAttendanceHandler.Handle(
                    service,
                    actor,
                    displayContact ?? (string.Equals(displaySourceId, actor.UserId, StringComparison.Ordinal) ? actorContact : null),
                    displaySourceId,
                    description,
                    clock,
                    ct);
            case "StaffMeeting":
                return WebhookStaffMeetingHandler.Handle(service, actor, actorContact, description, clock, ct);
            default: return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, intent + "SourceContractNotMigrated");
        }
    }
}

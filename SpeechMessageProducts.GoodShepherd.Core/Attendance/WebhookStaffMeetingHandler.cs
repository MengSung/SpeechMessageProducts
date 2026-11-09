/*
檔案整體說明

目的與定位：移植主副本 LineMessagePlugIn/Attendance/ServantMasterPresentHandler.cs
的四種同工聚會出席規則，供 GoodShepherd.Core 雙目標共用。

主要功能與流程：以固定路由詞彙選出聚會欄位，先驗證 actor Contact 的
customertypecode 是否在該聚會白名單，再依 actor Contact 與教會當週日期找唯一
既有 new_present_record。只有候選完整且帶正式 rowversion 時，才更新聚會時間與
人次欄位；不在角色白名單、查無記錄或正式列衝突時不建立或猜測資料。

使用邊界與維護：Contact、Entity、時間與 SDK 只屬單筆事件；本檔不建立週記錄、
週報或背景工作，也不以顯示名稱授權。查無記錄的 read-then-create 路徑刻意保留
為具名 Deferred，待有跨程序原子建立契約的工作包處理。測試只證明離線 SDK 契約，
不冒稱 D365 實機驗收。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;

namespace SpeechMessageProducts.GoodShepherd.Core.Attendance;

/// <summary>移植門徒與領袖聚會的既有週記錄更新契約。</summary>
public static class WebhookStaffMeetingHandler
{
    private static readonly int[] DiscipleRoles = { 100000006, 100000002, 100000003, 100000008, 100000012 };
    private static readonly int[] LeaderRoles = { 100000006, 100000002, 100000003, 100000008 };

    /// <summary>檢查角色並以 rowversion 更新既有週記錄，禁止在本入口先查再建。</summary>
    public static WebhookBusinessResult Handle(
        IOrganizationService sdk,
        WebhookActor actor,
        Entity actorContact,
        string description,
        IChurchClock clock,
        CancellationToken ct)
    {
#if NETFRAMEWORK
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
        if (actor is null) throw new ArgumentNullException(nameof(actor));
        if (actorContact is null) throw new ArgumentNullException(nameof(actorContact));
        if (clock is null) throw new ArgumentNullException(nameof(clock));
#else
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(actorContact);
        ArgumentNullException.ThrowIfNull(clock);
#endif
        WebhookRecordOperations.RequireLookup(actorContact, "parentcustomerid", "account", actor.ChurchId);
        if (!string.Equals(actorContact.GetAttributeValue<string>("new_lineid"), actor.UserId, StringComparison.Ordinal))
            throw new InvalidOperationException("The actor binding changed.");
        if (!TryGetMeeting(description, out var meeting))
            return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "InvalidStaffMeeting");

        var role = actorContact.GetAttributeValue<OptionSetValue>("customertypecode")?.Value;
        var allowed = role.HasValue && (meeting.IsDisciple
            ? ContainsRole(DiscipleRoles, role.Value)
            : ContainsRole(LeaderRoles, role.Value));
        if (!allowed)
            return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "StaffMeetingRoleNotAllowed");

        var now = clock.ToChurchTime(clock.UtcNow.UtcDateTime, actor.TimeZone);
        var sunday = now.Date.AddDays(1 - ((int)now.DayOfWeek + 1) % 7);
        var query = new QueryExpression("new_present_record")
        {
            ColumnSet = new ColumnSet(
                "new_contact_new_present_record",
                "new_sunday_date",
                meeting.TimeField,
                meeting.NumberField,
                "versionnumber")
        };
        query.Criteria.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, actor.ContactId);
        query.Criteria.AddCondition("new_sunday_date", ConditionOperator.Equal, sunday);
        var rows = WebhookRecordOperations.Query(sdk, query, ct);
        if (rows.Entities.Count == 0)
            return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "StaffMeetingWeeklyRecordRequired");
        if (rows.Entities.Count > 1)
            throw new InvalidOperationException("Conflicting weekly present records require remediation.");
        var row = rows.Entities[0];
        WebhookRecordOperations.RequireLookup(row, "new_contact_new_present_record", "contact", actor.ContactId);
        var patch = new Entity(row.LogicalName, row.Id)
        {
            [meeting.TimeField] = now,
            [meeting.NumberField] = 1
        };
        WebhookRecordOperations.Update(sdk, row, patch, ct);
        return new WebhookBusinessResult(
            WebhookBusinessStatus.Applied,
            "StaffMeetingUpdated",
            recordIds: new[] { row.Id });
    }

    /// <summary>依生成資源的四個 exact route 選欄位；不從使用者輸入組成欄位名稱。</summary>
    private static bool TryGetMeeting(string description, out MeetingField meeting)
    {
        var values = new[]
        {
            new MeetingField("Webhook.Route.R066.StaffMeeting.ExactI", "new_child_on_time", "new_child_number", true),
            new MeetingField("Webhook.Route.R067.StaffMeeting.ExactI", "new_big_disciple_on_time", "new_big_disciple_number", true),
            new MeetingField("Webhook.Route.R068.StaffMeeting.ExactI", "new_leadership_small_lecture_on_time", "new_leadership_small_lecture_number", false),
            new MeetingField("Webhook.Route.R069.StaffMeeting.ExactI", "new_leaders_gather_on_time", "new_leaders_gather_number", false)
        };
        foreach (var value in values)
        {
            if (string.Equals(description, WebhookText.Neutral(value.RouteKey), StringComparison.OrdinalIgnoreCase))
            {
                meeting = value;
                return true;
            }
        }

        meeting = null!;
        return false;
    }

    /// <summary>只做固定小陣列角色比對，不建立共享可變集合。</summary>
    private static bool ContainsRole(IEnumerable<int> roles, int value)
    {
        foreach (var role in roles)
            if (role == value)
                return true;
        return false;
    }

    /// <summary>一筆聚會的固定欄位映射，不保存任何 Contact 或 SDK 狀態。</summary>
    private sealed class MeetingField
    {
        /// <summary>建立固定路由到欄位的映射值。</summary>
        public MeetingField(string routeKey, string timeField, string numberField, bool isDisciple)
        {
            RouteKey = routeKey;
            TimeField = timeField;
            NumberField = numberField;
            IsDisciple = isDisciple;
        }

        /// <summary>生成資源鍵。</summary>
        public string RouteKey { get; }

        /// <summary>CRM 時間欄位。</summary>
        public string TimeField { get; }

        /// <summary>CRM 人次欄位。</summary>
        public string NumberField { get; }

        /// <summary>是否屬門徒類聚會。</summary>
        public bool IsDisciple { get; }
    }
}

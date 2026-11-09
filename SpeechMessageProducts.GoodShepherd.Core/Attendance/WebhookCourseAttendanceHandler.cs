/*
檔案整體說明

目的與定位：移植主副本 LineMessagePlugIn/Attendance/ClassAttendanceHandler.cs
的第 1～15 課簽到／簽退規則，供已完成 actor 授權的文字 webhook 使用。

主要功能與流程：先以已驗證的 display Contact fullname 作為查詢提示，再從
actor Contact 已驗證的 new_stor_lessons 關聯反查唯一課程；不對
new_disciple_lessons 做未帶教會範圍的全域名稱查詢。完整候選與 rowversion
驗證後，才以原子條件更新對應的 signon／signoff 與 present 欄位。查無資料
具名延後，重複正式列或作用域不符直接 fail closed。

使用邊界與維護：display Contact 只提供伺服器已查證的課程名稱，不能取代 actor
授權；所有 Entity、SDK 與取消 token 只活於本次事件，SDK 由呼叫端擁有。本檔
不建立課程或報名資料，不使用名稱去重，不使用背景工作或共享快取。測試以窄
IOrganizationService 驗證雙目標欄位與隔離；實際 D365 行為仍須由指定實機包驗收。
*/
using System;
using System.Globalization;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;

namespace SpeechMessageProducts.GoodShepherd.Core.Attendance;

/// <summary>移植第 1～15 課簽到／簽退的具體 CRM 更新規則。</summary>
public static class WebhookCourseAttendanceHandler
{
    /// <summary>
    /// 依 display Contact 對應課程名稱、actor Contact 對應課程記錄，完成一筆有 rowversion
    /// 條件的簽到／簽退更新。任何查無或衝突都不建立新資料。
    /// </summary>
    public static WebhookBusinessResult Handle(
        IOrganizationService sdk,
        WebhookActor actor,
        Entity? displayContact,
        string displaySourceId,
        string description,
        IChurchClock clock,
        CancellationToken ct)
    {
#if NETFRAMEWORK
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
        if (actor is null) throw new ArgumentNullException(nameof(actor));
        if (clock is null) throw new ArgumentNullException(nameof(clock));
#else
        ArgumentNullException.ThrowIfNull(sdk);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(clock);
#endif
        if (!WebhookRecordOperations.IsValidLineIdentity(displaySourceId))
            throw new ArgumentException("Invalid display source identity.", nameof(displaySourceId));
        if (string.IsNullOrEmpty(description))
            return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "InvalidClassAttendance");
        if (displayContact is null)
            return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "DisplaySourceContactRequired");

        WebhookRecordOperations.RequireLookup(displayContact, "parentcustomerid", "account", actor.ChurchId);
        if (!string.Equals(displayContact.GetAttributeValue<string>("new_lineid"), displaySourceId, StringComparison.Ordinal))
            throw new InvalidOperationException("The display source binding changed.");
        var lessonName = displayContact.GetAttributeValue<string>("fullname");
        if (string.IsNullOrWhiteSpace(lessonName))
            return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "DisplaySourceNameRequired");
        if (!TryGetLessonInfo(description, out var lessonNumber, out var isSignOn))
            return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "InvalidClassAttendance");

        var now = clock.ToChurchTime(clock.UtcNow.UtcDateTime, actor.TimeZone);
        var storedLessonQuery = new QueryExpression("new_stor_lessons")
        {
            ColumnSet = new ColumnSet(
                "new_contact_new_stor_lessons",
                "new_new_disciple_lessons_new_stor_les",
                "new_enroll_status",
                "statecode",
                "statuscode",
                "versionnumber")
        };
        storedLessonQuery.Criteria.AddCondition("new_contact_new_stor_lessons", ConditionOperator.Equal, actor.ContactId);
        storedLessonQuery.Criteria.AddCondition("new_enroll_status", ConditionOperator.NotIn, 100000007, 100000009, 100000003);
        storedLessonQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        storedLessonQuery.Criteria.AddCondition("statuscode", ConditionOperator.NotEqual, 2);
        var lessonLink = storedLessonQuery.AddLink(
            "new_disciple_lessons",
            "new_new_disciple_lessons_new_stor_les",
            "new_disciple_lessonsid",
            JoinOperator.Inner);
        lessonLink.Columns = new ColumnSet("new_name", "statecode", "statuscode");
        lessonLink.LinkCriteria.AddCondition("new_name", ConditionOperator.Equal, lessonName);
        lessonLink.LinkCriteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        lessonLink.LinkCriteria.AddCondition("statuscode", ConditionOperator.NotEqual, 2);
        var storedLessons = WebhookRecordOperations.Query(sdk, storedLessonQuery, ct);
        if (storedLessons.Entities.Count == 0)
            return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "ClassAttendanceLessonOrEnrollmentNotFound");
        if (storedLessons.Entities.Count > 1)
            throw new InvalidOperationException("Conflicting stored lessons require remediation.");
        var storedLesson = storedLessons.Entities[0];
        var lesson = storedLesson.GetAttributeValue<EntityReference>("new_new_disciple_lessons_new_stor_les");
        if (lesson is null)
            throw new InvalidOperationException("The stored lesson is missing its formal disciple lesson reference.");
        WebhookRecordOperations.RequireLookup(storedLesson, "new_contact_new_stor_lessons", "contact", actor.ContactId);
        WebhookRecordOperations.RequireLookup(storedLesson, "new_new_disciple_lessons_new_stor_les", "new_disciple_lessons", lesson.Id);

        var patch = new Entity(storedLesson.LogicalName, storedLesson.Id)
        {
            [$"new_{lessonNumber}_{(isSignOn ? "signon" : "signoff")}_time"] = now,
            [$"new_{lessonNumber}_present"] = true
        };
        WebhookRecordOperations.Update(sdk, storedLesson, patch, ct);
        return new WebhookBusinessResult(
            WebhookBusinessStatus.Applied,
            "ClassAttendanceUpdated",
            recordIds: new[] { storedLesson.Id });
    }

    /// <summary>保留舊 matcher 的 1～15 課及簽到／簽退固定詞彙，不將使用者文字轉成欄位名稱。</summary>
    private static bool TryGetLessonInfo(string description, out int lessonNumber, out bool isSignOn)
    {
        for (var index = 0; index < 30; index++)
        {
            var routeNumber = 70 + index;
            var key = "Webhook.Route.R" + routeNumber.ToString("D3", CultureInfo.InvariantCulture) + ".ClassAttendance.ContainsI";
            var word = WebhookText.Neutral(key);
            if (!TransitionalIntentResolver.Contains(description, word, StringComparison.OrdinalIgnoreCase))
                continue;

            lessonNumber = (index % 15) + 1;
            isSignOn = index < 15;
            return true;
        }

        lessonNumber = 0;
        isSignOn = false;
        return false;
    }
}

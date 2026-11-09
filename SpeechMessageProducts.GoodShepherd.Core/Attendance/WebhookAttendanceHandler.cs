/*
檔案整體說明
目的與定位：移植主副本 LineMessagePlugIn/Attendance 的出席、RPG、靈修、代禱與同工週記錄規則；不依賴 net10 主機。
主要功能與流程：SundayPresent.cs:436–546、SmallGroupPresent.cs:636–687、RpgPractice.cs:137–180、SpiritualPractice.cs:149–222、ChurchStaffReportHandler.cs:140–208、PrayItem.cs:145–225 為來源。由已授權 contact 及教會週日查唯一記錄，完整組成局部 patch 後條件更新。查無記錄時不以舊 read-then-random-create 製造重複，明列待原子 weekly 建立契約。
使用邊界與維護：來源只讀，原 description 不 Trim；SDK 及 Entity 僅屬這筆事件，時間沿用共用 IChurchClock，週界線預設星期六。保留 Webhook 的 new_small_group_signing_time，未混入 Plugin 欄位。rowversion 衝突與真正 SDK 故障向 queue 傳播；緊急代禱的通知仍由 P3-08 接通，不宣稱已發送。沒有快取或背景資源。
*/
using System;
using System.Globalization;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;

namespace SpeechMessageProducts.GoodShepherd.Core.Attendance;

/// <summary>已有週記錄的具體業務更新；建立與 weekly 關聯未具備原子契約時具名延後，絕不假成功。</summary>
public static class WebhookAttendanceHandler
{
    /// <summary>依正式 contact／週日查唯一記錄並驗作用域，保留原輸入對精確場次與前綴的影響。</summary>
    public static WebhookBusinessResult Handle(IOrganizationService sdk, WebhookActor actor, string intent, string text, IChurchClock clock, CancellationToken ct)
    {
        var now = clock.ToChurchTime(clock.UtcNow.UtcDateTime, actor.TimeZone);
        var sunday = now.Date.AddDays(1 - ((int)now.DayOfWeek + 1) % 7);
        var query = new QueryExpression("new_present_record") { ColumnSet = new ColumnSet("new_contact_new_present_record", "new_rpg_number", "new_spiritual_content", "new_explanation", "new_visit_small_group", "new_watch_over", "new_church_staff_rpg", "versionnumber") };
        query.Criteria.AddCondition("new_contact_new_present_record", ConditionOperator.Equal, actor.ContactId);
        query.Criteria.AddCondition("new_sunday_date", ConditionOperator.Equal, sunday);
        var rows = WebhookRecordOperations.Query(sdk, query, ct);
        if (rows.Entities.Count > 1) throw new InvalidOperationException("Conflicting attendance records require remediation.");
        if (rows.Entities.Count == 0) return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "AtomicPresentAndWeeklyCreationRequired");
        var row = rows.Entities[0];
        WebhookRecordOperations.RequireLookup(row, "new_contact_new_present_record", "contact", actor.ContactId);
        var patch = new Entity(row.LogicalName, row.Id);
        switch (intent)
        {
            case "SundayAttendance":
                patch["new_sunday_present_this_week"] = 1; patch["new_sunday_signing_time"] = now;
                if (MatchesRoute(text, 44, 50, 51)) patch["new_sunday_first_qr_on_time"] = now;
                if (MatchesRoute(text, 45, 52, 53)) patch["new_sunday_second_qr_on_time"] = now;
                if (MatchesRoute(text, 46, 54, 55)) patch["new_saturday_worship_on_time"] = now;
                if (text.StartsWith(Route(48), StringComparison.Ordinal)) patch["new_sunday_location"] = new OptionSetValue(100000001);
                if (text.StartsWith(Route(49), StringComparison.Ordinal)) patch["new_sunday_location"] = new OptionSetValue(100000002);
                break;
            case "SmallGroupAttendance":
                patch["new_group_present_this_week"] = 1; patch["new_small_group_signing_time"] = now;
                SetLocation(patch, "new_group_location", text); break;
            case "Rpg":
                var number = RemoveIgnoreCase(text, "rpg");
                var increment = 1;
                if (!string.IsNullOrWhiteSpace(number) &&
                    (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out increment) ||
                     increment is < 1 or > 100))
                    return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "InvalidRpgIncrement");
                patch["new_rpg_number"] = checked(Math.Max(0, row.GetAttributeValue<int>("new_rpg_number")) + increment); break;
            case "Devotion":
                var prefix = Word("DevotionContent");
                if (text.StartsWith(prefix, StringComparison.Ordinal)) patch["new_spiritual_content"] = Append(row, "new_spiritual_content", text.Substring(prefix.Length));
                else
                {
                    var count = ReplaceOrdinal(ReplaceOrdinal(text, Word("Devotion")), Word("Times"));
                    if (!int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var work) || work is < 0 or > 100)
                        return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "InvalidDevotionCount");
                    patch["new_spiritual_work"] = work;
                }
                break;
            case "PrayerRequest":
                var content = text;
                foreach (var candidate in new[] { Word("EmergencyPrayer"), Word("Prayer"), "Pray" })
                    if (text.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) { content = text.Substring(candidate.Length); break; }
                patch["new_explanation"] = Append(row, "new_explanation", content); break;
            case "StaffReport":
#if NETFRAMEWORK
                var colon = text.IndexOf(':'); if (colon < 0) colon = text.IndexOf('\uFF1A');
#else
                var colon = text.IndexOf(':', StringComparison.Ordinal); if (colon < 0) colon = text.IndexOf('\uFF1A', StringComparison.Ordinal);
#endif
                if (colon < 0) return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "InvalidStaffReport");
                var category = text.Substring(0, colon);
                var field = category == Word("Visit") ? "new_visit_small_group" : category == Word("Watch") ? "new_watch_over" : category == Word("StaffRpg") ? "new_church_staff_rpg" : null;
                if (field is null) return new WebhookBusinessResult(WebhookBusinessStatus.Rejected, "InvalidStaffReport");
                patch[field] = Append(row, field, text.Substring(colon + 1)); break;
            default: return new WebhookBusinessResult(WebhookBusinessStatus.Deferred, "StaffMeetingRoleContractRequired");
        }
        WebhookRecordOperations.Update(sdk, row, patch, ct);
        return new WebhookBusinessResult(WebhookBusinessStatus.Applied, intent == "PrayerRequest" && text.StartsWith(Word("EmergencyPrayer"), StringComparison.Ordinal) ? "PrayerStoredNotificationPendingP308" : intent + "Updated", recordIds: new[] { row.Id });
    }
    /// <summary>從 resx 取已核定的協定詞，不增加中文字面掃描例外。</summary>
    private static string Word(string key) => WebhookText.Neutral("Webhook.Word." + key);
    /// <summary>保留固定來源規則的精確字面值；不是第二套路由。</summary>
    private static string Route(int index) => WebhookText.Neutral("Webhook.Route.R" + index.ToString("D3", CultureInfo.InvariantCulture) + ".SundayAttendance.ExactI");
    /// <summary>場次只接受原 description 精確相等，不把 matcher 的 Trim 套入業務。</summary>
    private static bool MatchesRoute(string value, params int[] indexes)
    {
        foreach (var index in indexes) if (value == Route(index)) return true;
        return false;
    }
    /// <summary>原小組位置是包含文字判斷；實體優先於線上。</summary>
    private static void SetLocation(Entity patch, string field, string text)
    {
        if (TransitionalIntentResolver.Contains(text, Word("Physical"), StringComparison.Ordinal)) patch[field] = new OptionSetValue(100000001);
        else if (TransitionalIntentResolver.Contains(text, Word("Online"), StringComparison.Ordinal)) patch[field] = new OptionSetValue(100000002);
    }
    /// <summary>追加長度明確有界；不讓歷史本文無限增長或覆寫其他事件的 rowversion。</summary>
    internal static string Append(Entity row, string field, string value)
    {
        var previous = row.GetAttributeValue<string>(field);
        var result = string.IsNullOrEmpty(previous) ? value : previous + "\r\n" + value;
        if (result.Length > 32768) throw new InvalidOperationException("The accumulated content exceeds its budget.");
        return result;
    }
    /// <summary>Framework 與 net10 採相同 OrdinalIgnoreCase 刪除；輸入長度已由入口限制。</summary>
    private static string RemoveIgnoreCase(string value, string word)
    {
        var position = value.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (position >= 0) { value = value.Remove(position, word.Length); position = value.IndexOf(word, StringComparison.OrdinalIgnoreCase); }
        return value;
    }
    /// <summary>固定 Ordinal 子字串替換，兩個目標同義且不依目前文化改變。</summary>
    private static string ReplaceOrdinal(string value, string word)
    {
        var position = value.IndexOf(word, StringComparison.Ordinal);
        while (position >= 0) { value = value.Remove(position, word.Length); position = value.IndexOf(word, StringComparison.Ordinal); }
        return value;
    }
}

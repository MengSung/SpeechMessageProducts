/*
 * 檔案整體說明
 *
 * 目的與定位：把 P5-10 關懷純規則接到 Dataverse 活動外殼，承接退役生日與年資
 * 流程的 contact 讀取、正式教會歸屬驗證、教會時區解析及單次 patch 更新。
 * 本檔不建立 LINE client、不解密 token，也不把 caller 提供的名稱或時區當成授權。
 *
 * 主要流程：以平台主要 contact ID 讀取必要欄位，再由 parentcustomerid 指向 account；
 * 從該正式 account 讀取 new_timezone（缺值沿既有產品預設 Asia/Taipei），用 ChurchClock
 * 取得當地日期，交給 ContactAnniversaryRule 計算生日及年資欄位，最後一次 Update。
 * Shadow 使用外殼提供的 RecordingOrganizationService，因此查詢與 patch 都會被安全記錄，
 * 而非執行任何外部通知。
 *
 * 使用邊界與維護：本服務只在當次同步呼叫中工作，輸入不跨執行保存。
 * 隔離與生命週期：SDK service 由平台／外殼借用，本服務不 Dispose、不快取、不保存 Entity；
 * 每次呼叫的 contact、account、patch 僅存活於同步堆疊，成功 Update 後即釋放參照。
 * 缺少正式父教會、錯誤型別或空主鍵均 fail-closed；未建立背景工作、timer、queue、
 * session 或跨教會共享狀態。離線測試以假 service 驗證資料邊界，實機 shadow 另由部署包驗收。
 */
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;

namespace SpeechMessageProducts.GoodShepherd.Core.Care;

/// <summary>承接生日與年資活動的同步 Dataverse adapter；不擁有傳入 SDK。</summary>
public sealed class ContactAnniversaryService
{
    private const string DefaultTimeZone = "Asia/Taipei";

    /// <summary>依正式 contact 與其 parent account 計算並更新關懷欄位。</summary>
    /// <param name="service">平台借用的同步 SDK；呼叫端擁有其生命週期。</param>
    /// <param name="contactId">平台主要 contact 的資料庫 ID。</param>
    public static void Execute(IOrganizationService service, Guid contactId)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (contactId == Guid.Empty) throw new ArgumentException("A contact identity is required.", nameof(contactId));

        var contact = service.Retrieve("contact", contactId, new ColumnSet(
            "birthdate", "new_onboard_date", "new_next_update_onboard_date", "parentcustomerid"));
        if (contact is null || contact.Id != contactId) throw new InvalidOperationException("The contact identity was not returned.");
        var parent = contact.GetAttributeValue<EntityReference>("parentcustomerid");
        if (parent is null || parent.Id == Guid.Empty || !string.Equals(parent.LogicalName, "account", StringComparison.Ordinal))
            throw new InvalidOperationException("The contact must belong to an account church.");

        var church = service.Retrieve("account", parent.Id, new ColumnSet("new_timezone"));
        if (church is null || church.Id != parent.Id) throw new InvalidOperationException("The church identity was not returned.");
        var timeZone = church.GetAttributeValue<string>("new_timezone");
        if (string.IsNullOrWhiteSpace(timeZone)) timeZone = DefaultTimeZone;
        var today = new ChurchClock().TodayInChurch(timeZone);

        var birthday = contact.GetAttributeValue<DateTime>("birthdate");
        if (birthday == default) birthday = new DateTime(ContactAnniversaryRule.UnknownYear, 1, 1);
        var onboarding = contact.GetAttributeValue<DateTime>("new_onboard_date");
        if (onboarding == default) onboarding = new DateTime(ContactAnniversaryRule.UnknownYear, 1, 1);
        var nextUpdate = contact.GetAttributeValue<DateTime>("new_next_update_onboard_date");
        if (nextUpdate == default) nextUpdate = new DateTime(ContactAnniversaryRule.UnknownYear, 1, 1);

        var birthdayUpdate = ContactAnniversaryRule.CalculateBirthday(birthday, today);
        var onboardingUpdate = ContactAnniversaryRule.CalculateOnboarding(onboarding, nextUpdate, today);
        var patch = new Entity("contact", contactId);
        if (birthdayUpdate.NextBirthdate.HasValue) patch["new_next_birthdate"] = birthdayUpdate.NextBirthdate.Value;
        if (birthdayUpdate.BirthdayMonth.HasValue) patch["new_birthday_month"] = new OptionSetValue(birthdayUpdate.BirthdayMonth.Value);
        if (birthdayUpdate.AgeNumber.HasValue) patch["new_age_number"] = birthdayUpdate.AgeNumber.Value;
        if (birthdayUpdate.AgeLevel.HasValue) patch["new_age_level"] = new OptionSetValue(birthdayUpdate.AgeLevel.Value);
        if (onboardingUpdate.OnboardDays.HasValue) patch["new_on_board_year"] = onboardingUpdate.OnboardDays.Value;
        if (onboardingUpdate.SpecialLeaveDays.HasValue) patch["new_special_leave_days"] = onboardingUpdate.SpecialLeaveDays.Value;
        if (onboardingUpdate.NextOnboardUpdate.HasValue) patch["new_next_update_onboard_date"] = onboardingUpdate.NextOnboardUpdate.Value;
        if (patch.Attributes.Count != 0) service.Update(patch);
    }
}


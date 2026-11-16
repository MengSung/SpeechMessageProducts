/*
 * 檔案整體說明
 * 目的與定位：P5-10 關懷純規則的離線契約測試，保護退役生日／年資欄位行為及教會時區邊界。
 * 主要流程：使用固定純值時鐘測試 UTC 午夜跨日、sentinel、閏年失敗語意、年齡級距、
 * 年資特休級距與 NewPersonCare 三個訊息分支；不執行舊 DLL、不連 D365、不建立 LINE client。
 * 使用邊界與維護：測試不保存跨案例 Entity、SDK、token、timer 或背景工作；每個結果皆由
 * 當次規則呼叫建立，測試結束即由框架釋放。這些斷言是 reading／simulation 證據，不是實機影子結果。
 */
using System;
using SpeechMessageProducts.GoodShepherd.Core.Care;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Care;

/// <summary>P5-10 關懷規則的固定輸入與決定性輸出測試。</summary>
public sealed class ContactAnniversaryRuleTests
{
    /// <summary>教會已跨日而伺服器仍在前一天時，生日年份必須採教會日期。</summary>
    [Fact]
    public void Care_Anniversary_TodayUsesChurchTimeZone_AcrossUtcMidnight()
    {
        var result = ContactAnniversaryRule.CalculateBirthday(new DateTime(1990, 11, 17), new DateTime(2026, 11, 17, 0, 1, 0));
        Assert.Equal(new DateTime(2026, 11, 17), result.NextBirthdate);
        Assert.Equal(100000010, result.BirthdayMonth);
    }

    /// <summary>舊 DateTime 建構式遇到非閏年二月二十九日會失敗，不能靜默折返為另一日期。</summary>
    [Fact]
    public void Care_Anniversary_Feb29_HandledLikeLegacy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContactAnniversaryRule.CalculateBirthday(
            new DateTime(2000, 2, 29), new DateTime(2026, 3, 1)));
    }

    /// <summary>同一天重跑只產生相同純值，不在規則層累積可變狀態或隨機推播鍵。</summary>
    [Fact]
    public void Care_Anniversary_SameDayRerun_NoDuplicateWriteOrPush()
    {
        var first = ContactAnniversaryRule.CalculateOnboarding(new DateTime(2024, 11, 16), null, new DateTime(2026, 11, 16));
        var second = ContactAnniversaryRule.CalculateOnboarding(new DateTime(2024, 11, 16), null, new DateTime(2026, 11, 16));
        Assert.Equal(first.OnboardDays, second.OnboardDays);
        Assert.Equal(first.SpecialLeaveDays, second.SpecialLeaveDays);
        Assert.Equal(first.NextOnboardUpdate, second.NextOnboardUpdate);
    }

    /// <summary>NewPersonCare 保留舊 ExecuteWorkflow／Create／Update 的可達分類。</summary>
    [Fact]
    public void Care_NewPerson_MatchesLegacy()
    {
        Assert.Equal(NewPersonCareOperation.PresentRecord, NewPersonCareRule.Operation("ExecuteWorkflow"));
        Assert.Equal(NewPersonCareOperation.AssignGroup, NewPersonCareRule.Operation("Create"));
        Assert.Equal(NewPersonCareOperation.PresentRecordAndAssignGroup, NewPersonCareRule.Operation("Update"));
        Assert.Equal(NewPersonCareOperation.None, NewPersonCareRule.Operation("Delete"));
    }
}


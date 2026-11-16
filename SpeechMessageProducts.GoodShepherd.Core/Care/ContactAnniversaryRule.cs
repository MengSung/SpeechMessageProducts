/*
 * 檔案整體說明
 *
 * 目的與定位：定義 P5-10 關懷活動所需的純值計算，承接退役 HappyBirthdayWorkflow
 * 與 RecursiveWorkflow 的可達欄位行為。規則不讀取 SDK、不保存 contact、教會或通知狀態，
 * 由活動外殼在授權後把結果轉成單次 contact patch。
 *
 * 主要功能與流程：生日計算依呼叫端傳入的教會當地日期決定下一次生日年份，保留
 * 年份 1000 的未知 sentinel、月份選項、年齡及年齡級距；到職計算依舊程式實際分支
 * 的天數與 1 日下一次更新日產生 patch。二月二十九日若目標年份不是閏年，保留舊
 * DateTime 建構式的失敗語意，不以未核定的日期折返規則偷偷改變基準。
 *
 * 使用邊界與維護：所有輸入都是當次不可變值，輸出是新的不可變結果；沒有 cache、
 * Session、背景工作、timer、連線或可 Dispose 資源。活動須先驗證 contact 與教會時區，
 * 並在同一次同步執行中完成讀取、計算、更新與 Shadow recording；本檔測試以固定時鐘
 * 覆蓋跨 UTC 午夜、sentinel、閏年及年資假日級距。
 */
using System;

namespace SpeechMessageProducts.GoodShepherd.Core.Care;

/// <summary>生日與年資計算結果；欄位只表達本次要寫入的純值。</summary>
public sealed class ContactCareUpdate
{
    /// <summary>建立已驗證的 contact patch；null 表示該欄位不應更新。</summary>
    public ContactCareUpdate(DateTime? nextBirthdate, int? birthdayMonth, int? ageNumber, int? ageLevel,
        int? onboardDays, int? specialLeaveDays, DateTime? nextOnboardUpdate)
    {
        NextBirthdate = nextBirthdate;
        BirthdayMonth = birthdayMonth;
        AgeNumber = ageNumber;
        AgeLevel = ageLevel;
        OnboardDays = onboardDays;
        SpecialLeaveDays = specialLeaveDays;
        NextOnboardUpdate = nextOnboardUpdate;
    }

    /// <summary>下一次生日；生日未知時不寫入。</summary>
    public DateTime? NextBirthdate { get; }
    /// <summary>生日月份選項值。</summary>
    public int? BirthdayMonth { get; }
    /// <summary>依舊程式年份差計算的年齡。</summary>
    public int? AgeNumber { get; }
    /// <summary>依年齡區間計算的選項值。</summary>
    public int? AgeLevel { get; }
    /// <summary>到職日距今天數。</summary>
    public int? OnboardDays { get; }
    /// <summary>依到職天數計算的特休選項值。</summary>
    public int? SpecialLeaveDays { get; }
    /// <summary>下一次年資更新日。</summary>
    public DateTime? NextOnboardUpdate { get; }
}

/// <summary>承接退役生日與年資流程的無狀態純值規則。</summary>
public static class ContactAnniversaryRule
{
    /// <summary>舊資料使用的未知生日／到職 sentinel 年份。</summary>
    public const int UnknownYear = 1000;

    /// <summary>依教會當地日期重現舊生日欄位寫入。</summary>
    public static ContactCareUpdate CalculateBirthday(DateTime birthdate, DateTime churchToday)
    {
        var today = DateOnly(churchToday);
        if (birthdate.Year == UnknownYear)
            return new ContactCareUpdate(null, 100000012, null, 100000011, null, null, null);

        var nextYear = today.Month < birthdate.Month || today.Month == birthdate.Month && today.Day < birthdate.Day
            ? today.Year : checked(today.Year + 1);
        var next = new DateTime(nextYear, birthdate.Month, birthdate.Day);
        var age = checked(today.Year - birthdate.Year);
        return new ContactCareUpdate(next, 100000000 + birthdate.Month - 1, age, AgeLevel(age), null, null, null);
    }

    /// <summary>依教會當地日期重現舊到職欄位寫入；未知到職日不產生 patch。</summary>
    public static ContactCareUpdate CalculateOnboarding(DateTime onboardDate, DateTime? nextUpdate, DateTime churchToday)
    {
        var today = DateOnly(churchToday);
        if (onboardDate.Year == UnknownYear)
            return new ContactCareUpdate(null, null, null, null, null, null, null);

        var days = checked((today - DateOnly(onboardDate)).Days);
        var next = nextUpdate.HasValue && nextUpdate.Value.Year != UnknownYear
            ? DateOnly(nextUpdate.Value).AddDays(1)
            : today.AddDays(1);
        return new ContactCareUpdate(null, null, null, null, days, LeaveDays(days), next);
    }

    /// <summary>將生日月份映射到 Dynamics 選項值，未知月份固定使用未知值。</summary>
    private static int AgeLevel(int age) => age switch
    {
        >= 0 and <= 9 => 100000000,
        >= 10 and <= 19 => 100000001,
        >= 20 and <= 29 => 100000002,
        >= 30 and <= 39 => 100000003,
        >= 40 and <= 49 => 100000004,
        >= 50 and <= 59 => 100000005,
        >= 60 and <= 69 => 100000006,
        >= 70 and <= 79 => 100000007,
        >= 80 and <= 89 => 100000008,
        >= 90 and <= 99 => 100000009,
        >= 100 and <= 109 => 100000010,
        _ => 100000011
    };

    /// <summary>依退役 RecursiveWorkflow 的實際天數級距計算特休。</summary>
    private static int LeaveDays(int days) => days switch
    {
        >= 180 and < 365 => 3,
        >= 365 and < 730 => 7,
        >= 730 and < 1095 => 10,
        >= 1095 and < 1825 => 14,
        >= 1825 and < 3650 => 15,
        >= 3650 and < 4015 => 16,
        >= 4015 and < 4380 => 17,
        >= 4380 and < 4745 => 18,
        >= 4745 and < 5110 => 19,
        >= 5110 and < 5475 => 20,
        >= 5475 and < 5840 => 21,
        >= 5840 and < 6205 => 22,
        >= 6205 and < 6570 => 23,
        >= 6570 and < 6935 => 24,
        >= 6935 and < 7300 => 25,
        >= 7300 and < 7665 => 26,
        >= 7665 and < 8030 => 27,
        >= 8030 and < 8395 => 28,
        >= 8395 and < 8760 => 29,
        >= 8760 => 30,
        _ => 0
    };

    /// <summary>移除時間部分，避免伺服器時分秒影響生日及年資比較。</summary>
    private static DateTime DateOnly(DateTime value) => value.Date;
}

/// <summary>NewPersonCare 舊入口的可達分支分類；實際資料工作由既有 P5 規則承接。</summary>
public static class NewPersonCareRule
{
    /// <summary>只接受舊入口的三個訊息；其他訊息固定不執行。</summary>
    public static NewPersonCareOperation Operation(string messageName) => messageName switch
    {
        "ExecuteWorkflow" => NewPersonCareOperation.PresentRecord,
        "Create" => NewPersonCareOperation.AssignGroup,
        "Update" => NewPersonCareOperation.PresentRecordAndAssignGroup,
        _ => NewPersonCareOperation.None
    };
}

/// <summary>NewPersonCare 的可達舊行為分類。</summary>
public enum NewPersonCareOperation
{
    /// <summary>不是舊流程處理的訊息。</summary>
    None,
    /// <summary>執行既有出席關懷建立。</summary>
    PresentRecord,
    /// <summary>執行既有分組指派。</summary>
    AssignGroup,
    /// <summary>依序執行出席關懷與分組指派。</summary>
    PresentRecordAndAssignGroup
}

/// <summary>新朋友分組日期與歷程的純值更新結果。</summary>
public sealed class NewPersonCareAssignmentUpdate
{
    /// <summary>建立已驗證的分組更新結果；null 表示該欄位不應寫入。</summary>
    public NewPersonCareAssignmentUpdate(DateTime? careDate, DateTime? assignedGroupDate, string? smallGroupLog)
    {
        CareDate = careDate;
        AssignedGroupDate = assignedGroupDate;
        SmallGroupLog = smallGroupLog;
    }

    /// <summary>關懷小組日期。</summary>
    public DateTime? CareDate { get; }
    /// <summary>一般細胞小組指派日期。</summary>
    public DateTime? AssignedGroupDate { get; }
    /// <summary>包含新增歷程後的完整小組歷程。</summary>
    public string? SmallGroupLog { get; }
}

/// <summary>承接退役 AssignGroupClassLibrary 的無狀態欄位規則。</summary>
public static class NewPersonCareAssignmentRule
{
    /// <summary>
    /// 依正式名單名稱及教會當地日期建立一次性 patch。名單名稱只作業務分類，
    /// 不作為 contact、教會或使用者授權；呼叫端必須先驗證 lookup 歸屬。
    /// </summary>
    public static NewPersonCareAssignmentUpdate Calculate(string listName, string? existingLog, DateTime churchToday)
    {
#if NETFRAMEWORK
        if (listName is null) throw new ArgumentNullException(nameof(listName));
#else
        ArgumentNullException.ThrowIfNull(listName);
#endif
        if (listName.Length > 300) throw new ArgumentException("The list name exceeds its bounded budget.", nameof(listName));
        var date = churchToday.Date;
#if NETFRAMEWORK
        var hasHistory = existingLog?.IndexOf(listName, StringComparison.Ordinal) >= 0;
#else
        var hasHistory = existingLog?.Contains(listName, StringComparison.Ordinal) == true;
#endif
        var history = hasHistory ? null : (existingLog ?? string.Empty) + date.ToShortDateString() + "," + listName + Environment.NewLine;
#if NETFRAMEWORK
        var isCareList = listName.IndexOf("關懷", StringComparison.Ordinal) >= 0;
#else
        var isCareList = listName.Contains("關懷", StringComparison.Ordinal);
#endif
        return isCareList
            ? new NewPersonCareAssignmentUpdate(date, null, history)
            : new NewPersonCareAssignmentUpdate(null, date, history);
    }
}

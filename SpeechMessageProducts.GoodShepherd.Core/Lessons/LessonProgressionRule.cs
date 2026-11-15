/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P5-07 兩套舊課程進階評估可共用的設定式純值引擎。差異
 * 不以組織名稱、組織代碼或呼叫端文字分支，而由已驗證的教會設定 profile
 * 明確描述；本檔不讀取 SDK、Session、credential、快取或外部服務。
 *
 * 主要功能與流程：呼叫端先以正式資料列建立 LessonProgressionInput，再選取
 * 已授權的 LessonProgressionProfile，規則依完成單位、核准旗標及 profile
 * 的門檻輸出固定 Outcome。Profile 與 input 都是單次不可變值，結果只含狀態
 * 與下一步需求，不回傳個資或資料列內容。
 *
 * 使用邊界與維護：profile 是部署／教會設定的資料，不是使用者提供的權限；
 * 服務層仍須先驗證來源教會、課程及 profile 所屬範圍。規則無背景工作、
 * timer、非同步資源或共用可變狀態，呼叫返回後不保留 input。無效設定與
 * 不可能的計數 fail closed，避免兩套舊行為被隱含合併。
 *
 * 驗證方式：測試以兩組不同門檻的合成 profile 比對固定結果，並掃描 Lessons
 * 原始碼不得出現組織名稱字面值；這些離線測試只驗證設定式引擎，不宣稱
 * 正式組織的實機或影子結果。
 */
using System;

namespace SpeechMessageProducts.GoodShepherd.Core.Lessons;

/// <summary>進階評估的固定輸出狀態。</summary>
public enum LessonProgressionOutcome
{
    /// <summary>尚未完成任何 profile 要求。</summary>
    NotStarted,
    /// <summary>已開始但尚未達成目前階段要求。</summary>
    InProgress,
    /// <summary>已達成 profile 的所有要求。</summary>
    Completed,
    /// <summary>資料或核准條件阻擋進階。</summary>
    Blocked
}

/// <summary>
/// 單次進階評估所需的 detached 事實。數值來自已授權資料列，不能當成授權依據。
/// </summary>
public sealed class LessonProgressionInput
{
    /// <summary>建立單次 detached 評估事實並拒絕負數完成單位。</summary>
    public LessonProgressionInput(int completedUnits, bool leaderApproved, bool sourceValid)
    {
#if NETFRAMEWORK
        if (completedUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(completedUnits));
#else
        ArgumentOutOfRangeException.ThrowIfNegative(completedUnits);
#endif
        CompletedUnits = completedUnits;
        LeaderApproved = leaderApproved;
        SourceValid = sourceValid;
    }

    /// <summary>已完成的正式課程單位數。</summary>
    public int CompletedUnits { get; }
    /// <summary>來源資料列所帶出的核准事實。</summary>
    public bool LeaderApproved { get; }
    /// <summary>來源是否已通過服務層身分與欄位驗證。</summary>
    public bool SourceValid { get; }
}

/// <summary>由設定提供的進階 profile，不含組織名稱或組織代碼。</summary>
public sealed class LessonProgressionProfile
{
    /// <summary>建立設定 profile 並驗證 key 與有限門檻。</summary>
    public LessonProgressionProfile(string key, int requiredUnits, bool requireLeaderApproval)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
            throw new ArgumentException("A bounded progression profile key is required.", nameof(key));
#if NETFRAMEWORK
        if (requiredUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(requiredUnits));
#else
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredUnits);
#endif
#if NETFRAMEWORK
        if (requiredUnits > 10000)
            throw new ArgumentOutOfRangeException(nameof(requiredUnits));
#else
        ArgumentOutOfRangeException.ThrowIfGreaterThan(requiredUnits, 10000);
#endif
        Key = key;
        RequiredUnits = requiredUnits;
        RequireLeaderApproval = requireLeaderApproval;
    }

    /// <summary>設定檔的部署識別，不含組織名稱或組織代碼。</summary>
    public string Key { get; }
    /// <summary>完成所需的最小正式單位數。</summary>
    public int RequiredUnits { get; }
    /// <summary>是否需要已驗證的領袖核准事實。</summary>
    public bool RequireLeaderApproval { get; }
}

/// <summary>只依 profile 與已驗證 input 計算進階結果的無狀態規則。</summary>
public static class LessonProgressionRule
{
    /// <summary>
    /// 先檢查來源有效性，再檢查核准與完成門檻；不以名稱猜測 profile，
    /// 也不把缺資料當成完成。
    /// </summary>
    public static LessonProgressionOutcome Evaluate(
        LessonProgressionProfile profile,
        LessonProgressionInput input)
    {
#if NETFRAMEWORK
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (input is null) throw new ArgumentNullException(nameof(input));
#else
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(input);
#endif
        if (!input.SourceValid)
            return LessonProgressionOutcome.Blocked;
        if (profile.RequireLeaderApproval && !input.LeaderApproved)
            return LessonProgressionOutcome.Blocked;
        if (input.CompletedUnits == 0)
            return LessonProgressionOutcome.NotStarted;
        return input.CompletedUnits >= profile.RequiredUnits
            ? LessonProgressionOutcome.Completed
            : LessonProgressionOutcome.InProgress;
    }
}

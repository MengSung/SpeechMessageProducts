// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72AttendanceWeeklyReportDecision.cs
// 用途：定義 P7.2 continuation Slice H 的週報 cardinality 本機決策；本檔只處理已驗證的
//       完整性與列數，不持有 CRM ID、Entity、Owner、connector、Session 或任何外部資源。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 表示受治理唯讀週報投影的最小 cardinality 資訊。
///
/// <para>
/// 此型別刻意不攜帶週報 ID、名稱、Entity 或 caller-provided list identity。未來 CE executor
/// 必須在已驗證的 task-owned fixture 與 server-derived scope 內自行完成精確 lookup，並只把
/// 「查詢是否完整」與「啟用列數」投影至這個 local-only 決策器。這可避免 static/cache/session
/// 保留另一位使用者、另一個小組或另一個週次的 CRM reference。
/// </para>
/// </summary>
public sealed class P72AttendanceWeeklyReportObservation
{
    /// <summary>
    /// 指示唯讀查詢是否可證明投影完整。paging、transport timeout、schema mismatch、權限失敗或
    /// 其他不確定狀態一律為 <see langword="false"/>；即使目前列數看似零，也不能推論 zero-active。
    /// </summary>
    public required bool IsComplete { get; init; }

    /// <summary>
    /// 已完整投影的啟用週報列數。有效值為零、正數；負數代表上游 contract 違反，必須 fail closed。
    /// 此值僅用於 cardinality 分支，不可當作 CRM record identifier 或可供 caller 選擇的資料來源。
    /// </summary>
    public required int ActiveReportCount { get; init; }
}

/// <summary>
/// 表示 attendance 操作在週報 cardinality 已確認後可採用的本機 disposition。
/// </summary>
public enum P72AttendanceWeeklyReportDisposition
{
    /// <summary>不可安全繼續；下游不得 dispatch、retry 或自動建立週報。</summary>
    NoGo = 0,

    /// <summary>確認 zero-active；若日後受治理 executor 寫入出席紀錄，必須不關聯週報。</summary>
    ProceedUnlinked = 1,

    /// <summary>確認 exactly-one-active；下游必須以其受信任精確 lookup 連結並 read-back。</summary>
    ProceedWithExactLink = 2
}

/// <summary>
/// 表示週報 cardinality 決策的固定去識別化失敗分類。
/// </summary>
public enum P72AttendanceWeeklyReportFailureCategory
{
    /// <summary>週報觀測完整且 cardinality 可安全決策。</summary>
    None = 0,

    /// <summary>查詢不完整、投影不可信或 cardinality 為不可能值。</summary>
    Unavailable = 1,

    /// <summary>同一目標小組與週次有兩筆以上啟用週報，不能任選一筆連結。</summary>
    DuplicateActive = 2
}

/// <summary>
/// 表示週報 cardinality 的 immutable local-only 決策結果。
///
/// <para>
/// 結果不含 CRM ID 或 raw query 資料。它只允許 zero-active 的不關聯分支，或 exactly-one-active
/// 的「必須做精確 link read-back」分支；這使本機程式無法自行猜選週報，也無法把本機測試誤宣稱為
/// CE 實證。此型別沒有可釋放資源，因為建立過程只使用 stack-local primitive values。
/// </para>
/// </summary>
public sealed class P72AttendanceWeeklyReportDecisionResult
{
    internal P72AttendanceWeeklyReportDecisionResult(
        P72AttendanceWeeklyReportDisposition disposition,
        P72AttendanceWeeklyReportFailureCategory failureCategory)
    {
        Disposition = disposition;
        FailureCategory = failureCategory;
    }

    /// <summary>指出 cardinality 規則是否允許後續受治理流程繼續。</summary>
    public bool CanProceed => Disposition is P72AttendanceWeeklyReportDisposition.ProceedUnlinked or
        P72AttendanceWeeklyReportDisposition.ProceedWithExactLink;

    /// <summary>zero-active、exactly-one-active 或 no-go 的固定 disposition。</summary>
    public P72AttendanceWeeklyReportDisposition Disposition { get; }

    /// <summary>成功或 no-go 的固定去識別化分類。</summary>
    public P72AttendanceWeeklyReportFailureCategory FailureCategory { get; }

    /// <summary>
    /// 指出 exactly-one-active 分支必須做精確週報連結與 read-back。此布林值不是 ID，也不授權
    /// caller 指定連結目標；它僅強制後段 executor 不得以不關聯寫入取代唯一週報關聯。
    /// </summary>
    public bool RequiresExactLinkReadBack =>
        Disposition == P72AttendanceWeeklyReportDisposition.ProceedWithExactLink;
}

/// <summary>
/// 將完整性與啟用列數轉成 Slice H 週報 cardinality 決策。
///
/// <para>
/// 方法是純函式：不讀取設定、環境、Session、cache、clock 或前一請求，也不建立 CRM client、
/// connector、lease、task、timer 或 process。任何 ambiguous/incomplete 狀態直接回傳 no-go，不
/// retry、不掃描、不自動建立週報，也不從既有資料猜選目標。因而它可以安全用於本機契約測試，
/// 但不能替代未來受治理 CE cycle 的 exact lookup、read-back、reconcile 與 cleanup。
/// </para>
/// </summary>
public static class P72AttendanceWeeklyReportDecision
{
    /// <summary>
    /// 依完整性與啟用週報列數決定 attendance 後續 disposition。zero-active 是允許不關聯的
    /// 正常分支；exactly-one-active 只允許「精確連結且讀回」；兩筆以上及任何不完整／不可能
    /// 值一律 fail closed。輸出不含原始 query、例外或 CRM identity。
    /// </summary>
    /// <param name="observation">由受信任 read-only projection 產生的最小 cardinality 資訊。</param>
    /// <returns>不可變的本機分支決策，不構成任何 CE mutation 授權。</returns>
    public static P72AttendanceWeeklyReportDecisionResult Resolve(
        P72AttendanceWeeklyReportObservation? observation)
    {
        if (observation is null || !observation.IsComplete || observation.ActiveReportCount < 0)
        {
            return new P72AttendanceWeeklyReportDecisionResult(
                P72AttendanceWeeklyReportDisposition.NoGo,
                P72AttendanceWeeklyReportFailureCategory.Unavailable);
        }

        return observation.ActiveReportCount switch
        {
            0 => new P72AttendanceWeeklyReportDecisionResult(
                P72AttendanceWeeklyReportDisposition.ProceedUnlinked,
                P72AttendanceWeeklyReportFailureCategory.None),
            1 => new P72AttendanceWeeklyReportDecisionResult(
                P72AttendanceWeeklyReportDisposition.ProceedWithExactLink,
                P72AttendanceWeeklyReportFailureCategory.None),
            _ => new P72AttendanceWeeklyReportDecisionResult(
                P72AttendanceWeeklyReportDisposition.NoGo,
                P72AttendanceWeeklyReportFailureCategory.DuplicateActive)
        };
    }
}

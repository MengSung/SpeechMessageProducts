// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72AttendanceLocalPlanBuilder.cs
// 用途：在通用 local-only plan 之上驗證 Slice H attendance 的週次與狀態語意；不執行
//       CRM I/O、不解析 caller authority，也不建立任何外部資源。
// ============================================================================

using System.Globalization;

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 建立 Slice H attendance 本機計畫的純語意驗證器。
///
/// <para>
/// 它先委派通用 plan builder 完成 operation ID、完整 key allowlist、字串長度與防禦性複製，
/// 再對 <c>weekStartDate</c> 執行不受目前文化設定影響的 ISO 日期驗證，並要求日期為星期日。
/// <c>presentState</c> 只接受固定的產品狀態文字。此類別不持有 Session、principal、CRM service、
/// connector、timer、cache 或 background task；每次方法返回後所有暫存值均可回收。
/// </para>
/// </summary>
public static class P72AttendanceLocalPlanBuilder
{
    private const string WeekStartDateInputName = "weekStartDate";
    private const string PresentStateInputName = "presentState";
    private const string IsoDateFormat = "yyyy-MM-dd";

    private static readonly HashSet<string> AllowedPresentStates = new(StringComparer.Ordinal)
    {
        "present",
        "absent",
        "visited"
    };

    /// <summary>
    /// 建立 attendance create／upsert 的 local-only plan。未知 operation、缺值、非 ISO Sunday、
    /// 未知 present state 或其他 allowlist 問題均固定回傳 no-go；不猜測日期、不以 caller 指定
    /// 週報 ID、不 retry，也不呼叫 Data8 executor。
    /// </summary>
    /// <param name="operationId">必須是 catalog 中的兩個 server-owned attendance operation ID。</param>
    /// <param name="inputs">只含 attendanceKey、weekStartDate、presentState 的不透明輸入。</param>
    /// <returns>成功時為不可執行 local-only plan；否則為固定分類結果。</returns>
    public static P72ContinuationLocalPlanBuildResult Build(
        string? operationId,
        IReadOnlyDictionary<string, string?>? inputs)
    {
        if (operationId is null || inputs is null ||
            !P72ContinuationLocalOnlyCatalog.TryGet(operationId, out var definition) ||
            definition is null || definition.Slice != P72ContinuationSlice.Attendance)
        {
            return P72ContinuationLocalPlanBuildResult.Failure(P72LocalPlanFailureCategory.UnknownOperation);
        }

        var generic = P72ContinuationLocalOnlyPlanBuilder.Build(new P72ContinuationLocalPlanRequest
        {
            OperationId = operationId,
            Inputs = inputs
        });
        if (!generic.Succeeded)
        {
            return generic;
        }

        var weekStartDate = generic.Plan!.Inputs[WeekStartDateInputName];
        var presentState = generic.Plan.Inputs[PresentStateInputName];
        if (!IsSunday(weekStartDate) || presentState is null || !AllowedPresentStates.Contains(presentState))
        {
            return P72ContinuationLocalPlanBuildResult.Failure(P72LocalPlanFailureCategory.InputValueInvalid);
        }

        return generic;
    }

    /// <summary>
    /// 以固定 ISO 格式解析日期並確認其 UTC 週起始日為星期日。沒有時區轉換或目前文化設定
    /// 參與，避免同一值在不同 process／使用者環境被解讀成不同週次。
    /// </summary>
    private static bool IsSunday(string? value)
        => value is not null &&
           DateOnly.TryParseExact(
               value,
               IsoDateFormat,
               CultureInfo.InvariantCulture,
               DateTimeStyles.None,
               out var parsed) &&
           parsed.DayOfWeek == DayOfWeek.Sunday;
}

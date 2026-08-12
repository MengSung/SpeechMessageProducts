// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyPlanBuilder.cs
// 用途：把 P7.2 continuation Slice D–H 的 catalog metadata 與單次呼叫輸入組成不可變、
//       僅本機可用的操作計畫；本檔不擁有 connector、CRM service、lease、Session、快取、
//       timer、背景工作或任何外部資源。
// ============================================================================

using System.Collections.ObjectModel;

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 描述呼叫端交給 P7.2 continuation local-only 層的單次操作意圖。
///
/// <para>
/// <see cref="OperationId"/> 必須是伺服器擁有的 catalog ID，<see cref="Inputs"/> 僅能含
/// 該 ID 的固定 allowlist 名稱與有界 opaque 值。這個型別不接受 Owner、CRM entity、endpoint、
/// credential、token、organization 或 profile 等選路 authority；它們也不能藉由字典中的額外
/// key 繞過驗證。request 僅由目前同步呼叫持有，建立器會立即複製字典，故呼叫端後續修改不會
/// 影響結果，也不會在跨使用者、跨租戶或跨 profile 的 shared state 留下資料。
/// </para>
/// </summary>
public sealed class P72ContinuationLocalPlanRequest
{
    /// <summary>指定既有 immutable catalog 中的固定 operation ID。</summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// 指定一組只含 opaque fixture／draft key 與固定狀態名稱的輸入。建立器不保留此字典參考，
    /// 並會要求其 key 集合與 catalog 完全相等，避免缺值、額外 authority 或前一請求值被重用。
    /// </summary>
    public required IReadOnlyDictionary<string, string?> Inputs { get; init; }
}

/// <summary>
/// 表示 local-only 計畫建立失敗的固定去識別化分類。
///
/// <para>
/// 此 enum 是唯一允許向上層公開的失敗語意；它不包含 CRM ID、fixture marker、Owner、endpoint、
/// credential、原始例外或 upstream response，避免診斷本身跨請求或跨產品洩漏資料。
/// </para>
/// </summary>
public enum P72LocalPlanFailureCategory
{
    /// <summary>計畫建立成功，沒有失敗分類。</summary>
    None = 0,

    /// <summary>operation ID 空白或不在 server-owned local-only catalog。</summary>
    UnknownOperation = 1,

    /// <summary>輸入 key 缺少、重複語意不符或包含 catalog 未允許的欄位。</summary>
    InputNamesMismatch = 2,

    /// <summary>allowlisted key 的值為 null、空白或超過固定安全上限。</summary>
    InputValueInvalid = 3
}

/// <summary>
/// 表示已驗證的單次 local-only 操作計畫。
///
/// <para>
/// 計畫只保存 immutable catalog definition 與防禦性複製後的 bounded opaque values。它不擁有
/// 外部資源，沒有 dispose、retry 或 cleanup 責任；若未來受治理 CE cycle 要使用同一 capability，
/// 必須由另一個擁有 nonce、ledger、fixture、read-back 與 exact cleanup 的 executor 重新驗證，
/// 不能把本型別當作 CE write 授權。兩個布林值永遠由 catalog 的 fail-closed metadata 決定，
/// 目前固定為 false，防止本機規劃意外成為 P7.4 consumer 或 P7.5 migration 的切換開關。
/// </para>
/// </summary>
public sealed class P72ContinuationLocalPlan
{
    internal P72ContinuationLocalPlan(
        P72ContinuationLocalCapabilityDefinition definition,
        IReadOnlyDictionary<string, string?> inputs)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
    }

    /// <summary>本次操作所對應的 server-owned catalog definition。</summary>
    public P72ContinuationLocalCapabilityDefinition Definition { get; }

    /// <summary>
    /// 防禦性複製並封閉為唯讀後的 opaque input snapshot；它不與 caller dictionary 共用可變集合。
    /// </summary>
    public IReadOnlyDictionary<string, string?> Inputs { get; }

    /// <summary>
    /// 指出本機計畫是否可發出 CE dispatch。P7.2 continuation 未取得各 Slice CE evidence 前，
    /// 這個值由 catalog 固定為 <see langword="false"/>。
    /// </summary>
    public bool CeDispatchAllowed => Definition.CeExecutorEnabled;

    /// <summary>
    /// 指出產品 consumer 是否可使用本計畫。P7.4 逐 capability cutover 未完成前，這個值由
    /// catalog 固定為 <see langword="false"/>，避免 legacy Session/ToolUtility 呼叫鏈誤接。
    /// </summary>
    public bool ProductConsumerAllowed => Definition.ConsumerEnabled;
}

/// <summary>
/// 表示 local-only 計畫建立結果；失敗時只傳遞固定分類，成功時才帶有 immutable 計畫。
/// </summary>
public sealed class P72ContinuationLocalPlanBuildResult
{
    private P72ContinuationLocalPlanBuildResult(
        P72ContinuationLocalPlan? plan,
        P72LocalPlanFailureCategory failureCategory)
    {
        Plan = plan;
        FailureCategory = failureCategory;
    }

    /// <summary>當且僅當輸入通過完整 local-only contract 驗證時為 true。</summary>
    public bool Succeeded => Plan is not null && FailureCategory == P72LocalPlanFailureCategory.None;

    /// <summary>成功時為 immutable 操作計畫；失敗時一律為 null，杜絕 partial plan。</summary>
    public P72ContinuationLocalPlan? Plan { get; }

    /// <summary>成功或失敗的固定去識別化分類。</summary>
    public P72LocalPlanFailureCategory FailureCategory { get; }

    /// <summary>建立成功結果。</summary>
    internal static P72ContinuationLocalPlanBuildResult Success(P72ContinuationLocalPlan plan)
        => new(plan ?? throw new ArgumentNullException(nameof(plan)), P72LocalPlanFailureCategory.None);

    /// <summary>建立失敗結果；不得附帶 partial plan 或原始例外。</summary>
    internal static P72ContinuationLocalPlanBuildResult Failure(P72LocalPlanFailureCategory failureCategory)
        => new(null, failureCategory);
}

/// <summary>
/// 建立 P7.2 continuation Slice D–H 的不可執行本機操作計畫。
///
/// <para>
/// 此建立器是純同步、無 I/O 的 validation boundary。它不讀取環境、設定、Session、principal、
/// profile、Factory、ToolUtility 或 CRM；也不建立 connector、lease、client、timer、task 或 cache。
/// 因此每次呼叫在完成時即釋放其所有暫存集合，沒有需要跨請求清理的資源。真正的授權、fixture
/// ownership、preflight、read-back、reconcile 與 cleanup 仍是未來 governed CE executor 的責任。
/// </para>
/// </summary>
public static class P72ContinuationLocalOnlyPlanBuilder
{
    /// <summary>
    /// 容許 opaque local key／狀態值的最大 UTF-16 字元數。這不是 CRM payload 上限；它只是
    /// 保護本機 contract 不被無界字串、記憶體保留或診斷資料放大。超限一律 fail closed。
    /// </summary>
    public const int MaximumInputValueCharacters = 256;

    /// <summary>
    /// 驗證 operation 與完整 allowlisted input key 集合，然後建立防禦性複製的 immutable local-only
    /// 計畫。任何 null、未知 ID、缺額外 key、空白或超限 value 都只回傳固定分類；不擲出原始
    /// 例外、不猜值、不重試、不讀取上一個 request，也不建立 partial plan。
    /// </summary>
    /// <param name="request">由目前呼叫端暫時擁有的操作意圖。</param>
    /// <returns>成功時的 immutable plan，否則只含 bounded failure category 的 no-go 結果。</returns>
    public static P72ContinuationLocalPlanBuildResult Build(P72ContinuationLocalPlanRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OperationId) ||
            !P72ContinuationLocalOnlyCatalog.TryGet(request.OperationId, out var definition) ||
            definition is null)
        {
            return P72ContinuationLocalPlanBuildResult.Failure(P72LocalPlanFailureCategory.UnknownOperation);
        }

        if (request.Inputs is null || !HasExactlyAllowedInputNames(request.Inputs, definition.AllowedInputNames))
        {
            return P72ContinuationLocalPlanBuildResult.Failure(P72LocalPlanFailureCategory.InputNamesMismatch);
        }

        var snapshot = new Dictionary<string, string?>(definition.AllowedInputNames.Count, StringComparer.Ordinal);
        foreach (var inputName in definition.AllowedInputNames)
        {
            var value = request.Inputs[inputName];
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumInputValueCharacters)
            {
                return P72ContinuationLocalPlanBuildResult.Failure(P72LocalPlanFailureCategory.InputValueInvalid);
            }

            snapshot.Add(inputName, value);
        }

        return P72ContinuationLocalPlanBuildResult.Success(new P72ContinuationLocalPlan(
            definition,
            new ReadOnlyDictionary<string, string?>(snapshot)));
    }

    /// <summary>
    /// 比對 caller key 集合與 catalog allowlist 是否完全相同。比較使用 ordinal 名稱與精確數量，
    /// 不做大小寫寬容、前後空白修剪或忽略額外欄位，因為任何隱式正規化都可能把 caller-supplied
    /// routing authority 誤當合法資料。方法只枚舉目前 request 與 immutable metadata，沒有快取。
    /// </summary>
    private static bool HasExactlyAllowedInputNames(
        IReadOnlyDictionary<string, string?> inputs,
        IReadOnlyList<string> allowedInputNames)
    {
        if (inputs.Count != allowedInputNames.Count)
        {
            return false;
        }

        foreach (var allowedInputName in allowedInputNames)
        {
            if (!inputs.ContainsKey(allowedInputName))
            {
                return false;
            }
        }

        return true;
    }
}

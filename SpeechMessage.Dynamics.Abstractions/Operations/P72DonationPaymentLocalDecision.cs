// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72DonationPaymentLocalDecision.cs
// 用途：以純本機、零 I/O 的方式決定 P7.2 continuation Slice D 付款結果是否僅能準備未來
//       受治理操作、已處理、需對帳或必須 fail closed；此檔案絕不執行 CRM 寫入。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// 表示付款提供者已在產品邊界完成正規化後的結果分類。
///
/// <para>
/// 此列舉不接受、保存或推導原始金流回應碼、訂單編號、信用卡資料、CRM 實體或例外文字。
/// 正規化必須由付款邊界完成；Slice D 只根據有限分類決定是否可為未來的受治理 CE cycle
/// 準備一個 local-only 意圖。這避免金流回應、使用者資料或跨 request 狀態流入靜態欄位、
/// Session、cache、connector 或診斷輸出。
/// </para>
/// </summary>
public enum P72DonationPaymentOutcome
{
    /// <summary>
    /// 上游無法完成、超時、資料不完整或尚未能安全分類；一律不得重播或建立部分寫入計畫。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 付款已明確成功，但仍須再結合去識別化的已處理與待付款觀察，才能決定是否準備後續操作。
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// 付款已明確失敗；只能要求未來受治理流程對帳，不能因失敗回呼自動補寫或重播。
    /// </summary>
    Failed = 2,

    /// <summary>
    /// 金流仍處於待確認狀態；它不是成功，也不是可安全重試的 CE 寫入授權。
    /// </summary>
    Pending = 3
}

/// <summary>
/// 描述付款回呼與受治理 read-back 提供的最小、去識別化本機觀察。
///
/// <para>
/// <see cref="IsComplete"/> 必須由擁有上游查詢與讀取範圍的元件設定；false 代表 timeout、
/// ambiguous transport、分頁／schema 不完整或任何不能證明目前狀態的情形。其餘兩個旗標只表示
/// 同一筆付款是否已由既有紀錄證明處理，以及費用是否仍在等待付款，不能攜帶訂單、CRM ID、
/// Owner、profile、endpoint、credential、token 或 raw response。此型別不擁有外部資源，也不會
/// 被保存在 Session、static、singleton、cache、queue 或背景工作中。
/// </para>
/// </summary>
public sealed class P72DonationPaymentLocalObservation
{
    /// <summary>
    /// 指出讀取與正規化是否完整可信。false 時必須 fail closed，因為預設布林值不能證明付款尚未處理。
    /// </summary>
    public required bool IsComplete { get; init; }

    /// <summary>
    /// 付款邊界輸出的固定結果分類，不包含原始提供者回應內容。
    /// </summary>
    public required P72DonationPaymentOutcome Outcome { get; init; }

    /// <summary>
    /// 指出受治理讀取是否已證明存在相同付款的處理紀錄。true 時，第二個回呼不得再次準備寫入。
    /// </summary>
    public required bool HasMatchingProcessedOrder { get; init; }

    /// <summary>
    /// 指出費用是否仍在舊流程語意中的待付款狀態。false 代表前一個完整流程可能已轉換狀態，
    /// 因此不能以過時成功回呼覆寫它。
    /// </summary>
    public required bool IsAwaitingPayment { get; init; }
}

/// <summary>
/// 表示 Slice D 本機決策的下一步分類。
///
/// <para>
/// 這是控制面分類，不是 CE 操作、產品切流或對外 API 回應。只有
/// <see cref="PrepareFutureGovernedDispatch"/> 可以成為未來受治理 fixture cycle 的輸入，且仍必須
/// 通過自己的 ledger、preflight、一次 dispatch、exact read-back、reconciliation 與 cleanup。
/// </para>
/// </summary>
public enum P72DonationPaymentDisposition
{
    /// <summary>
    /// 觀察不足或結果尚未終態；停止目前處理，絕不以 timeout、ambiguous 或 unknown 結果重播。
    /// </summary>
    NoGo = 0,

    /// <summary>
    /// 成功、未處理且仍待付款；只可準備未來的受治理操作，不能由本機決策直接發出 CE 寫入。
    /// </summary>
    PrepareFutureGovernedDispatch = 1,

    /// <summary>
    /// 相同訂單已處理或費用不再待付款；保持現況並禁止任何重播。
    /// </summary>
    AlreadyProcessed = 2,

    /// <summary>
    /// 付款已失敗且觀察完整；僅要求後續受治理流程做精確對帳，不能修改既有資料或自動重播。
    /// </summary>
    RequireReconciliation = 3
}

/// <summary>
/// 表示本機付款決策無法安全前進的固定原因分類。
/// </summary>
public enum P72DonationPaymentFailureCategory
{
    /// <summary>
    /// 觀察完整且分類有效；此值不代表 CE 實證成功。
    /// </summary>
    None = 0,

    /// <summary>
    /// timeout、ambiguous、pending、unknown 或不完整資料使付款狀態不可證明，必須 fail closed。
    /// </summary>
    Unavailable = 1
}

/// <summary>
/// 封裝 immutable 的付款本機決策結果。
///
/// <para>
/// 此結果只暴露固定分類與布林保護旗標，不含任何可識別付款、使用者、CRM 或連線資料。
/// 它在呼叫端堆疊內建立並立即交還，不配置或保留 client、lease、timer、subscription、stream、
/// process 或 background task；因此沒有跨使用者、跨 profile 或跨 request 的可變資源可洩漏。
/// </para>
/// </summary>
public sealed class P72DonationPaymentDecisionResult
{
    internal P72DonationPaymentDecisionResult(
        P72DonationPaymentDisposition disposition,
        P72DonationPaymentFailureCategory failureCategory)
    {
        Disposition = disposition;
        FailureCategory = failureCategory;
    }

    /// <summary>
    /// 指出本機層是否可準備一個未來受治理的付款更新意圖。true 不授權 CE dispatch、產品 consumer、
    /// feature flag 或 ToolUtility migration；這些動作仍受 P7.2 CE evidence、P7.4 與 P7.5 gate 保護。
    /// </summary>
    public bool CanPrepareFutureDispatch =>
        Disposition == P72DonationPaymentDisposition.PrepareFutureGovernedDispatch;

    /// <summary>
    /// 指出未來受治理流程是否必須以精確 read-back 與 reconciliation 確認實際資料狀態。
    /// 所有結果都需要這項保護，因為本機分類不能證明 CRM 寫入是否已提交。
    /// </summary>
    public bool RequiresReconciliation => true;

    /// <summary>
    /// 指出目前結果是否禁止重播。除完整且尚未處理的成功以外，所有結果都禁止重播；即使前者，
    /// 未來治理 cycle 一旦 timeout、ambiguous、no-go、read-back mismatch 或 cleanup uncertainty，
    /// 也必須立即將它轉為不可重播的終態。
    /// </summary>
    public bool ProhibitsReplay => !CanPrepareFutureDispatch;

    /// <summary>
    /// 受限的下一步分類，沒有 CRM、付款或身分資料。
    /// </summary>
    public P72DonationPaymentDisposition Disposition { get; }

    /// <summary>
    /// fail-closed 的固定失敗分類；不暴露原始 provider／CRM 例外。
    /// </summary>
    public P72DonationPaymentFailureCategory FailureCategory { get; }
}

/// <summary>
/// 建立 Slice D 付款結果的純本機決策。
///
/// <para>
/// 此方法是同步、無副作用的 reducer：不呼叫 CRM、Data8、ToolUtility、Factory、Session、cache、
/// connector、lease、timer、process 或背景工作。它不接受 caller 所給的 Owner、entity、profile、
/// endpoint、credential、token 或訂單識別值，並以完整性優先於成功旗標。這讓 A/B request 可安全
/// 交錯，因為所有輸入與輸出只存活於呼叫堆疊，且沒有可供下一個 request 重用的 mutable state。
/// </para>
/// </summary>
public static class P72DonationPaymentLocalDecision
{
    /// <summary>
    /// 根據最小、去識別化觀察決定付款回呼的本機 disposition。
    ///
    /// <para>
    /// 成功只有在尚未處理且仍待付款時才可準備未來受治理操作；已處理成功禁止重播；失敗只要求
    /// reconciliation；pending、unknown、null 或不完整觀察一律 Unavailable no-go。這重現既有
    /// `hasProcessedOrder` 與待付款狀態的防重語意，但不複製 legacy ToolUtility 寫入路徑。
    /// </para>
    /// </summary>
    /// <param name="observation">付款邊界與受治理讀取提供的最小本機觀察。</param>
    /// <returns>不含外部資料或 side effect 的 immutable 決策結果。</returns>
    public static P72DonationPaymentDecisionResult Resolve(P72DonationPaymentLocalObservation? observation)
    {
        if (observation is null || !observation.IsComplete ||
            (observation.Outcome != P72DonationPaymentOutcome.Succeeded &&
             observation.Outcome != P72DonationPaymentOutcome.Failed))
        {
            return new P72DonationPaymentDecisionResult(
                P72DonationPaymentDisposition.NoGo,
                P72DonationPaymentFailureCategory.Unavailable);
        }

        if (observation.Outcome == P72DonationPaymentOutcome.Failed)
        {
            return new P72DonationPaymentDecisionResult(
                P72DonationPaymentDisposition.RequireReconciliation,
                P72DonationPaymentFailureCategory.None);
        }

        if (observation.Outcome == P72DonationPaymentOutcome.Succeeded &&
            !observation.HasMatchingProcessedOrder && observation.IsAwaitingPayment)
        {
            return new P72DonationPaymentDecisionResult(
                P72DonationPaymentDisposition.PrepareFutureGovernedDispatch,
                P72DonationPaymentFailureCategory.None);
        }

        return new P72DonationPaymentDecisionResult(
            P72DonationPaymentDisposition.AlreadyProcessed,
            P72DonationPaymentFailureCategory.None);
    }
}

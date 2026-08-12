// ============================================================================
// 檔案：SpeechMessage.Dynamics.Abstractions/Operations/P72ContinuationLocalOnlyCatalog.cs
// 用途：保存 P7.2 continuation Slice D–H 的本機-only capability contract metadata。
//
// 安全邊界：
// 1. 本檔案只保存 immutable、server-owned metadata，不建立 connector、CRM service、session、
//    token、cache、timer、queue、stream 或 background task。
// 2. 所有定義明確標記 CE executor 與 consumer 都關閉；這個 catalog 不能成為 Data8 dispatch
//    allowlist，也不能啟動 P7.4 流量或 P7.5 ToolUtility migration。
// 3. input 名稱只描述 task-owned fixture 的不透明 key／enum，不接受 Entity、欄位 map、Owner、
//    organization、endpoint、credential、FetchXML 或原始 payment/card data。
// ============================================================================

namespace SpeechMessage.Dynamics.Abstractions.Operations;

/// <summary>
/// P7.2 continuation 的本機 Slice 分類。它只用於 contract metadata 與測試索引，不是 CE version、
/// connector kind、profile alias 或產品流量旗標。
/// </summary>
public enum P72ContinuationSlice
{
    /// <summary>奉獻、付款、booking 與 card profile 的固定 lifecycle。</summary>
    DonationLifecycle = 0,

    /// <summary>約談記錄的建立、更新與 deterministic cleanup contract。</summary>
    Appointments = 1,

    /// <summary>新人聯絡人多記錄 graph 的 onboarding contract。</summary>
    ContactOnboarding = 2,

    /// <summary>stor-lesson 與 fee 狀態的固定 transition contract。</summary>
    FeeLessons = 3,

    /// <summary>下載／上傳出席紀錄與週報關聯規則的 contract。</summary>
    Attendance = 4
}

/// <summary>
/// local capability 是否允許外部副作用。所有真正的 CE operation 在取得完整證據前只能保持
/// <see cref="SingleAllowlistedDispatch"/> 的描述狀態；catalog 本身不執行該 dispatch。
/// </summary>
public enum P72LocalMutationPolicy
{
    /// <summary>未來若獲得證據，最多一個固定 mutation；timeout 後不可在此層重播。</summary>
    SingleAllowlistedDispatch = 0,

    /// <summary>只改變操作範圍的本機 draft，禁止任何外部 CRM mutation。</summary>
    OperationLocalInMemoryOnly = 1
}

/// <summary>
/// 本機契約要求的 read-back 形狀。它不代表目前已連線或已取得 CE 證據。
/// </summary>
public enum P72LocalReadBackPolicy
{
    /// <summary>必須以固定 projection 證明 expected graph；不接受部分或模糊 read-back。</summary>
    ExactProjection = 0,

    /// <summary>只驗證 operation-local draft 的 immutable projection。</summary>
    LocalStateProjection = 1
}

/// <summary>
/// timeout／取消後的固定策略。未知 transport 狀態永遠不能自動 replay mutation。
/// </summary>
public enum P72LocalTimeoutPolicy
{
    /// <summary>只可 reconcile 已知狀態；unknown、ambiguous 或 timeout 直接 no-go。</summary>
    NoReplayAfterUncertainDispatch = 0,

    /// <summary>本機 draft 不會產生外部 dispatch，只能丟棄或恢復 operation-local state。</summary>
    NoExternalDispatch = 1
}

/// <summary>
/// local fixture／draft 的 deterministic cleanup 政策。
/// </summary>
public enum P72LocalCleanupPolicy
{
    /// <summary>只依 ledger 已證實的 known keys 逆序清理；未知 key 不猜測。</summary>
    ReverseKnownKeys = 0,

    /// <summary>只丟棄 operation-local draft，不觸及共享或正式資料。</summary>
    DiscardOperationLocalState = 1
}

/// <summary>
/// local-only contract 對週報關聯的固定語意。缺少週報不是錯誤，但 duplicate／unavailable 必須 no-go。
/// </summary>
public enum P72LocalWeeklyReportPolicy
{
    /// <summary>此 Slice 不涉及週報。</summary>
    NotApplicable = 0,

    /// <summary>zero-active 不關聯；exactly-one-active 精確關聯；duplicate/unavailable fail closed。</summary>
    ZeroActiveUnlinkedOrExactlyOneLinked = 1
}

/// <summary>
/// 一筆 D–H local-only capability 的 immutable contract metadata。
///
/// <para>
/// 此型別不擁有實際 request value 或外部資源。<see cref="CeExecutorEnabled"/> 與
/// <see cref="ConsumerEnabled"/> 必須保持 false；若未來要進入 CE，必須另建立有 fresh fixture、
/// ledger、preflight、read-back、reconcile 與 cleanup 證據的治理變更，不能只修改 catalog。
/// </para>
/// </summary>
public sealed class P72ContinuationLocalCapabilityDefinition
{
    /// <summary>與 coverage matrix 對齊的 server-owned capability ID。</summary>
    public required string OperationId { get; init; }

    /// <summary>與 phase0 matrix 對齊的固定 call-site 識別。</summary>
    public required string CallSiteId { get; init; }

    /// <summary>本機 Slice 分類。</summary>
    public required P72ContinuationSlice Slice { get; init; }

    /// <summary>固定 capability family；不能由 caller 提供。</summary>
    public required string CapabilityFamily { get; init; }

    /// <summary>本機模板名稱；它不是可連線的 OData、FetchXML 或 CRM URL。</summary>
    public required string TemplateId { get; init; }

    /// <summary>write/action 的固定本機副作用策略。</summary>
    public required P72LocalMutationPolicy MutationPolicy { get; init; }

    /// <summary>read-back 或 draft projection 的要求。</summary>
    public required P72LocalReadBackPolicy ReadBackPolicy { get; init; }

    /// <summary>timeout／取消時的不可重播策略。</summary>
    public required P72LocalTimeoutPolicy TimeoutPolicy { get; init; }

    /// <summary>fixture／draft 的唯一 cleanup 策略。</summary>
    public required P72LocalCleanupPolicy CleanupPolicy { get; init; }

    /// <summary>出席操作的週報關聯政策；其他 Slice 固定為 NotApplicable。</summary>
    public required P72LocalWeeklyReportPolicy WeeklyReportPolicy { get; init; }

    /// <summary>
    /// 可由 local contract validator 接受的不透明輸入名稱。這不是 arbitrary JSON schema，也不包含
    /// Owner、entity、欄位 map、endpoint、credential、token 或原始 upstream data。
    /// </summary>
    public required IReadOnlyList<string> AllowedInputNames { get; init; }

    /// <summary>
    /// 封閉且去識別化的 local outcome 欄位。不得新增 CRM ID、姓名、endpoint、原始 exception 或 baseline。
    /// </summary>
    public required IReadOnlyList<string> AllowedOutputNames { get; init; }

    /// <summary>本機 catalog 永遠不會啟用 Data8/CE executor。</summary>
    public bool CeExecutorEnabled { get; init; }

    /// <summary>本機 catalog 永遠不會啟用產品 consumer 或 feature flow。</summary>
    public bool ConsumerEnabled { get; init; }
}

/// <summary>
/// P7.2 continuation Slice D–H 的唯一 local-only catalog owner。
///
/// <para>
/// 這個 catalog 是 process-static immutable metadata；它不等同於
/// <see cref="Package01OperationRegistry"/> 的 Data8 executor allowlist。D–H 在 CE evidence
/// 完成前必須繼續由 Data8 executor 拒絕，並維持 P7.4/P7.5 fail closed。
/// </para>
/// </summary>
public static class P72ContinuationLocalOnlyCatalog
{
    private static readonly string[] SanitizedOutcomeFields =
    [
        "outcome",
        "reasonCategory",
        "reconciliationCategory",
        "cleanupCategory",
        "operationExecuted"
    ];

    private static readonly IReadOnlyDictionary<string, P72ContinuationLocalCapabilityDefinition> Definitions =
        Build().ToDictionary(definition => definition.OperationId, StringComparer.Ordinal);

    /// <summary>
    /// 取得新陣列 snapshot；呼叫端不能以回傳集合保存 request、profile 或任何外部資源。
    /// </summary>
    public static IReadOnlyCollection<P72ContinuationLocalCapabilityDefinition> All => Definitions.Values.ToArray();

    /// <summary>
    /// 以 operation ID 取得 local-only definition。未知 ID 必須在任何 connector／admission 前拒絕。
    /// </summary>
    /// <param name="operationId">server-owned operation ID。</param>
    /// <param name="definition">找到的 immutable metadata。</param>
    /// <returns>是否存在於 D–H local catalog。</returns>
    public static bool TryGet(
        string operationId,
        out P72ContinuationLocalCapabilityDefinition? definition)
        => Definitions.TryGetValue(operationId, out definition);

    /// <summary>
    /// 建立所有 D–H 定義並在 type initialization 時驗證不可啟用 CE、不得輸入敏感 routing 欄位。
    /// 此處只有純值集合配置，沒有 I/O、client、timer 或長生命週期資源。
    /// </summary>
    private static IEnumerable<P72ContinuationLocalCapabilityDefinition> Build()
    {
        yield return Definition(
            OperationIds.PaymentsFeeUpdateAfterPayment,
            "ORG-CALL-00036",
            P72ContinuationSlice.DonationLifecycle,
            "churchreport.donation.lifecycle",
            "payments.fee.update.after.payment.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["fixtureKey", "transition"]);
        yield return Definition(
            OperationIds.PaymentsDedicationCompleteRecurring,
            "ORG-CALL-00037",
            P72ContinuationSlice.DonationLifecycle,
            "churchreport.donation.lifecycle",
            "payments.dedication.complete.recurring.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["fixtureKey", "transition"]);
        yield return Definition(
            OperationIds.PaymentsContactCreateOrUpdate,
            "ORG-CALL-00038",
            P72ContinuationSlice.DonationLifecycle,
            "churchreport.donation.lifecycle",
            "payments.contact.create.or.update.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["fixtureKey", "mode"]);
        yield return Definition(
            OperationIds.PaymentsDedicationCancelBooking,
            "ORG-CALL-00042",
            P72ContinuationSlice.DonationLifecycle,
            "churchreport.donation.lifecycle",
            "payments.dedication.cancel.booking.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["fixtureKey", "transition"]);
        yield return Definition(
            OperationIds.PaymentsContactCreateWithDedicationNumber,
            "ORG-CALL-00043",
            P72ContinuationSlice.DonationLifecycle,
            "churchreport.donation.lifecycle",
            "payments.contact.create.with.dedication.number.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["fixtureKey", "mode"]);
        yield return Definition(
            OperationIds.PaymentsContactUpdateCardProfile,
            "ORG-CALL-00049",
            P72ContinuationSlice.DonationLifecycle,
            "churchreport.donation.lifecycle",
            "payments.contact.update.card.profile.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["fixtureKey", "maskedCardMode"]);
        yield return Definition(
            OperationIds.AppointmentsEntityCreateOrUpdate,
            "ORG-CALL-00039",
            P72ContinuationSlice.Appointments,
            "churchreport.appointments",
            "appointments.entity.create.or.update.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["fixtureKey", "changeMode"]);
        yield return Definition(
            OperationIds.NewPersonContactCreateFullOnboarding,
            "ORG-CALL-00044",
            P72ContinuationSlice.ContactOnboarding,
            "churchreport.contact.onboarding",
            "newperson.contact.create.full.onboarding.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["fixtureGraphKey"]);
        yield return Definition(
            OperationIds.FeesEditorUpdateByStorLesson,
            "ORG-CALL-00048",
            P72ContinuationSlice.FeeLessons,
            "churchreport.fee.lessons",
            "fees.editor.update.by.storlesson.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["fixtureKey", "changeSet"]);
        yield return Definition(
            OperationIds.FeesEditorStageInmemoryChange,
            "ORG-CALL-00050",
            P72ContinuationSlice.FeeLessons,
            "churchreport.fee.lessons",
            "fees.editor.stage.inmemory.change.local.v1",
            P72LocalMutationPolicy.OperationLocalInMemoryOnly,
            ["draftKey", "changeSet"],
            P72LocalReadBackPolicy.LocalStateProjection,
            P72LocalTimeoutPolicy.NoExternalDispatch,
            P72LocalCleanupPolicy.DiscardOperationLocalState);
        yield return Definition(
            OperationIds.FeesCreateFromStorLesson,
            "ORG-CALL-00067",
            P72ContinuationSlice.FeeLessons,
            "churchreport.fee.lessons",
            "fees.create.from.storlesson.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["fixtureKey"]);
        yield return Definition(
            OperationIds.PresentRecordCreateOnDownload,
            "ORG-CALL-00068",
            P72ContinuationSlice.Attendance,
            "churchreport.attendance",
            "presentrecord.create.on.download.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["attendanceKey", "weekStartDate", "presentState"]);
        yield return Definition(
            OperationIds.PresentRecordUpsertOnUpload,
            "ORG-CALL-00069",
            P72ContinuationSlice.Attendance,
            "churchreport.attendance",
            "presentrecord.upsert.on.upload.local.v1",
            P72LocalMutationPolicy.SingleAllowlistedDispatch,
            ["attendanceKey", "weekStartDate", "presentState"]);
    }

    /// <summary>
    /// 建立一筆 local-only definition。可變 params 會立即轉為唯讀複本，避免 static metadata 被呼叫端改寫。
    /// </summary>
    private static P72ContinuationLocalCapabilityDefinition Definition(
        string operationId,
        string callSiteId,
        P72ContinuationSlice slice,
        string capabilityFamily,
        string templateId,
        P72LocalMutationPolicy mutationPolicy,
        string[] allowedInputNames,
        P72LocalReadBackPolicy readBackPolicy = P72LocalReadBackPolicy.ExactProjection,
        P72LocalTimeoutPolicy timeoutPolicy = P72LocalTimeoutPolicy.NoReplayAfterUncertainDispatch,
        P72LocalCleanupPolicy cleanupPolicy = P72LocalCleanupPolicy.ReverseKnownKeys)
    {
        if (allowedInputNames.Any(ContainsForbiddenInputAuthority))
        {
            throw new InvalidOperationException("P7.2 local-only input names cannot carry routing or raw CRM authority.");
        }

        return new P72ContinuationLocalCapabilityDefinition
        {
            OperationId = operationId,
            CallSiteId = callSiteId,
            Slice = slice,
            CapabilityFamily = capabilityFamily,
            TemplateId = templateId,
            MutationPolicy = mutationPolicy,
            ReadBackPolicy = readBackPolicy,
            TimeoutPolicy = timeoutPolicy,
            CleanupPolicy = cleanupPolicy,
            WeeklyReportPolicy = slice == P72ContinuationSlice.Attendance
                ? P72LocalWeeklyReportPolicy.ZeroActiveUnlinkedOrExactlyOneLinked
                : P72LocalWeeklyReportPolicy.NotApplicable,
            AllowedInputNames = Array.AsReadOnly(allowedInputNames.ToArray()),
            AllowedOutputNames = Array.AsReadOnly(SanitizedOutcomeFields.ToArray()),
            CeExecutorEnabled = false,
            ConsumerEnabled = false
        };
    }

    /// <summary>
    /// 判定 input 名稱是否試圖攜帶 caller-controlled routing、身份或 CRM authority。此方法只處理
    /// compile-time catalog string，不讀取 request、profile、token、connector 或外部資源；拒絕
    /// <c>organization</c> 與 <c>profile</c> 可避免未來誤把 Data8 選路資訊納入 local-only contract，
    /// 拒絕 <c>token</c> 可避免任何 credential 被保存或傳播。
    /// </summary>
    /// <param name="inputName">即將放入 immutable catalog 的輸入名稱。</param>
    /// <returns>名稱含有被禁止的 authority fragment 時為 <see langword="true"/>。</returns>
    private static bool ContainsForbiddenInputAuthority(string inputName)
    {
        ArgumentNullException.ThrowIfNull(inputName);

        return inputName.Contains("owner", StringComparison.OrdinalIgnoreCase) ||
               inputName.Contains("endpoint", StringComparison.OrdinalIgnoreCase) ||
               inputName.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
               inputName.Contains("entity", StringComparison.OrdinalIgnoreCase) ||
               inputName.Contains("fetch", StringComparison.OrdinalIgnoreCase) ||
               inputName.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               inputName.Contains("organization", StringComparison.OrdinalIgnoreCase) ||
               inputName.Contains("profile", StringComparison.OrdinalIgnoreCase);
    }

}

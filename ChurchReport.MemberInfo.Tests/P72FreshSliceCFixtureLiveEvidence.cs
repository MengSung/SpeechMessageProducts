// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveEvidence.cs
// 用途：P7.2 Slice C fresh-fixture child 對 parent 的單次去識別化 evidence 邊界。
//
// 此檔只屬於 test-fixture control plane；不承載 CRM ID、credential、token、endpoint、
// browser session 或原始例外。每次 child invocation 只能寫入 parent 指派的一個 temporary
// JSON 檔，parent 會先驗證 child exit code，再讀取並刪除該檔案。
// ============================================================================

using System.Text;
using System.Text.Json;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// fresh-fixture child 對 parent 輸出的固定去識別化證據值。它只描述 lane、結果類別與
/// descriptor 是否可被 parent 考慮發布；所有 CRM entity、GUID、nonce、使用者、密碼、
/// endpoint、token、cookie 與原始例外都留在 child 的方法區域或 current-user ledger，
/// 絕不跨 process evidence boundary。
/// </summary>
/// <param name="Lane">固定為 <c>provision</c> 或 <c>cleanup</c> 的 child lane。</param>
/// <param name="Outcome">固定為 <c>go</c> 或 <c>no-go</c> 的去識別化結果。</param>
/// <param name="Reason">僅限本類別 allowlist 的固定故障或完成分類。</param>
/// <param name="OperationExecuted">至少一項遠端 mutation 已開始時為 <see langword="true"/>。</param>
/// <param name="DescriptorPublicationReady">只有完整 provision graph 證明後才可能為 <see langword="true"/>。</param>
internal sealed record P72FreshSliceCFixtureLiveEvidenceValue(
    string Lane,
    string Outcome,
    string Reason,
    bool OperationExecuted,
    bool DescriptorPublicationReady);

/// <summary>
/// 表示 fresh preflight child 對 parent 的固定 read-only evidence。所有分類均直接來自
/// <see cref="P72FreshSliceCFixturePreflightProbe"/> 的封閉 result vocabulary；record 不含 CRM ID、
/// 名稱、owner identity、endpoint、credential、token、cookie、baseline、raw Entity 或 exception。
/// 即使 <see cref="Outcome"/> 是 <c>go</c>，它也只證明本次 read projection，不能發布 descriptor、
/// 建立 ledger、執行 cleanup 或重試先前 no-go provision。
/// </summary>
/// <param name="Outcome">固定為 <c>go</c> 或 <c>no-go</c>。</param>
/// <param name="Reason">固定 fresh preflight completion/failure category。</param>
/// <param name="ReadOnlyProbeExecuted">所有允許的 remote read 完成並可安全分類時為 true。</param>
/// <param name="RequestShape">fixed descriptor/request scalar shape category。</param>
/// <param name="OperationalLists">five operational list aggregate category。</param>
/// <param name="LeaderMarker">descriptor-bound leader marker category。</param>
/// <param name="OwnerKind">owner logical-kind category。</param>
/// <param name="OwnerState">owner enabled/disabled category。</param>
/// <param name="OwnerRelation">owner-vs-Data8-WhoAmI relation category。</param>
/// <param name="WeeklyReport">fixed target-list/Sunday weekly-report category。</param>
internal sealed record P72FreshSliceCFixturePreflightProbeLiveEvidenceValue(
    string Outcome,
    string Reason,
    bool ReadOnlyProbeExecuted,
    string RequestShape,
    string OperationalLists,
    string LeaderMarker,
    string OwnerKind,
    string OwnerState,
    string OwnerRelation,
    string WeeklyReport);

/// <summary>
/// 將 fresh-fixture live child 的固定 evidence 以 create-new + flush + atomic-move 寫入
/// parent-owned temporary root。此類別不保存任何 static mutable state；每個呼叫者各自擁有
/// path、暫存檔、UTF-8 byte buffer 與檔案 stream，finally 會清除 buffer 並刪除未發布的
/// 暫存檔，避免不同使用者、profile 或 child session 讀到前次 invocation 的殘留資料。
/// </summary>
internal static class P72FreshSliceCFixtureLiveEvidence
{
    private const string EvidenceFileName = "P72FreshSliceCFixtureEvidence.json";
    private const string DiagnosticFileName = "P72FreshSliceCFixtureDiagnostic.json";
    private const string PreflightProbeEvidenceFileName = "P72FreshSliceCFixturePreflightProbeEvidence.json";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ProvisionReasons =
    [
        "fresh-fixture-provisioned",
        "fixture-precondition-failed",
        "baseline-owner-unavailable",
        "fresh-source-readback-failed",
        "fresh-leader-readback-failed",
        "fresh-relationship-readback-failed",
        "remove-membership-readback-failed",
        "transfer-source-membership-readback-failed",
        "baseline-owner-readback-failed",
        "fresh-graph-unproven",
        "provisioning-ambiguous",
        "runtime-failure",
        "cleanup-failure"
    ];
    private static readonly string[] CleanupReasons =
    [
        "fresh-fixture-cleaned",
        "cleanup-precondition-failed",
        "cleanup-membership-readback-failed",
        "cleanup-relationship-readback-failed",
        "cleanup-source-readback-failed",
        "cleanup-leader-readback-failed",
        "cleanup-ambiguous",
        "runtime-failure",
        "cleanup-failure"
    ];
    private static readonly string[] ProvisionDiagnosticCategories =
    [
        "fixture-precondition-failed",
        "baseline-owner-unavailable",
        "fresh-source-readback-failed",
        "fresh-leader-readback-failed",
        "fresh-relationship-readback-failed",
        "remove-membership-readback-failed",
        "transfer-source-membership-readback-failed",
        "baseline-owner-readback-failed",
        "fresh-graph-unproven",
        "provisioning-ambiguous",
        "runtime-failure",
        "cleanup-failure"
    ];
    private static readonly string[] PreflightProbeReasons =
    [
        "fresh-preconditions-proven",
        "fresh-preconditions-not-proven",
        "probe-input-invalid",
        "probe-unavailable",
        "cleanup-failure"
    ];

    /// <summary>
    /// 發布一份固定 schema 的 evidence。path 與 root 均由 parent 建立並以 process gate 傳入；
    /// child 只接受 root 內精確檔名，拒絕 junction/symlink、既有檔案與任意輸出路徑。stream
    /// 在寫入後同步 flush，成功後才原子移動至 parent 預期檔名；失敗時不留下可被下次 child
    /// 誤信任的 evidence 或 mutable buffer。
    /// </summary>
    /// <param name="path">parent 指派的固定 evidence file path。</param>
    /// <param name="ownedRoot">parent 為本 child nonce 建立的非 reparse temporary root。</param>
    /// <param name="value">已被 child 投影為固定 allowlist 的 evidence 值。</param>
    internal static void Write(string path, string ownedRoot, P72FreshSliceCFixtureLiveEvidenceValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateValue(value);

        var document = new FreshFixtureEvidenceDocument(
            SchemaVersion: 1,
            value.Lane,
            value.Outcome,
            value.Reason,
            value.OperationExecuted,
            value.DescriptorPublicationReady,
            FeatureFlagChanged: false);
        WriteDocument(path, ownedRoot, EvidenceFileName, document);
    }

    /// <summary>
    /// 將 fresh preflight probe 的固定 read-only classification 發布到 parent 指派的唯一 temporary
    /// evidence path。此方法的唯一外部效果是 create-new/flush/atomic-move 一個小型 JSON 檔；它不會
    /// 建立 ledger、變更 descriptor、保留 probe result、接觸 CRM 或觸發任何 feature flag。path/root
    /// 檢查、UTF-8 no-BOM/CRLF 與 finally buffer cleanup 全部由 <see cref="WriteDocument"/> 集中擁有。
    /// </summary>
    /// <param name="path">parent 建立且 child 只可使用一次的 fixed evidence file path。</param>
    /// <param name="ownedRoot">parent 為本次 child 建立的 non-reparse temporary root。</param>
    /// <param name="value">已被 probe 投影為固定 allowlist categories 的 immutable evidence value。</param>
    internal static void WritePreflightProbe(
        string path,
        string ownedRoot,
        P72FreshSliceCFixturePreflightProbeLiveEvidenceValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidatePreflightProbeValue(value);

        WriteDocument(
            path,
            ownedRoot,
            PreflightProbeEvidenceFileName,
            new FreshPreflightProbeEvidenceDocument(
                SchemaVersion: 1,
                Outcome: value.Outcome,
                Reason: value.Reason,
                ProfileAlias: "sunnyvalechback",
                DeploymentProfileAlias: "crm91",
                CeVersion: "9.1",
                Connector: "Data8",
                PreflightOnly: false,
                OperationExecuted: false,
                ReadOnlyProbeExecuted: value.ReadOnlyProbeExecuted,
                FeatureFlagChanged: false,
                Probe: new FreshPreflightProbeProjection(
                    value.RequestShape,
                    value.OperationalLists,
                    value.LeaderMarker,
                    value.OwnerKind,
                    value.OwnerState,
                    value.OwnerRelation,
                    value.WeeklyReport)));
    }

    /// <summary>
    /// 在 fresh provision child 已經確定會以 <c>no-go</c> 結束時，盡力寫入一個僅含固定分類的
    /// 診斷檔。此檔案不是 evidence；父程序即使成功讀到，也只能保留 <c>child-process-failed</c>
    /// 與 <c>safeToRetry=false</c>，不得據此發布 descriptor、執行 cleanup、修改 CRM 或改變任何
    /// feature flag。寫入失敗會被刻意忽略，因為診斷可用性絕不可把既有 no-go 變成成功或留下資源。
    /// </summary>
    /// <param name="path">由 parent 在單次 child temporary root 內建立的固定診斷檔路徑。</param>
    /// <param name="ownedRoot">由 parent 擁有且會在 finally 刪除的 temporary root。</param>
    /// <param name="category">不含 CRM、帳密、端點、識別碼或例外內容的 allowlisted 失敗分類。</param>
    internal static void TryWriteDiagnostic(string path, string ownedRoot, string category)
    {
        if (Array.IndexOf(ProvisionDiagnosticCategories, category) < 0)
        {
            return;
        }

        try
        {
            WriteDocument(
                path,
                ownedRoot,
                DiagnosticFileName,
                new FreshFixtureDiagnosticDocument(SchemaVersion: 1, category));
        }
        catch (Exception)
        {
            // 診斷檔沒有任何授權或復原語意；所有資源仍由 WriteDocument 的 finally 釋放。
            // 不回傳例外可避免 secondary diagnostic I/O 遮蔽既有 runtime/precondition no-go。
        }
    }

    /// <summary>
    /// 以 create-new、flush-to-disk 與 atomic move 將一個嚴格、去識別化 JSON 文件寫入 parent-owned
    /// temporary root。此函式只接受固定檔名，拒絕 reparse point，並在 finally 清除序列化 buffer
    /// 與未發布的 temporary file，因此不會把 child session、檔案控制資訊或記憶體保留到下一次執行。
    /// </summary>
    /// <param name="path">目標檔案的完整路徑。</param>
    /// <param name="ownedRoot">目標檔案唯一允許所在的 parent-owned temporary root。</param>
    /// <param name="expectedFileName">不接受 caller 任意路徑時所需的固定檔名。</param>
    /// <param name="document">只含已驗證常數與去識別化欄位的 immutable JSON 文件。</param>
    private static void WriteDocument(string path, string ownedRoot, string expectedFileName, object document)
    {
        var fullRoot = Path.GetFullPath(ownedRoot);
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullRoot) ||
            !string.Equals(Path.GetDirectoryName(fullPath), fullRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(fullPath), expectedFileName, StringComparison.Ordinal) ||
            File.Exists(fullPath))
        {
            throw new InvalidOperationException("The fresh-fixture handoff path is invalid.");
        }

        RejectReparsePoint(fullRoot);
        RejectReparsePoint(fullPath);

        var bytes = Utf8WithoutBom.GetBytes(JsonSerializer.Serialize(document, JsonOptions) + "\r\n");
        var temporaryPath = Path.Combine(fullRoot, ".p72-fresh-handoff.tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            RejectReparsePoint(temporaryPath);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// 驗證 live evidence 只能使用固定 lane、outcome 與 reason。這個 local boundary 在 child
    /// 尚未寫檔前就拒絕任意文字，避免 CRM 回應、例外、使用者資料或外部輸入被序列化進 parent
    /// 可讀檔案；feature flag 永遠不由此控制面改變。
    /// </summary>
    private static void ValidateValue(P72FreshSliceCFixtureLiveEvidenceValue value)
    {
        var validLane = value.Lane is "provision" or "cleanup";
        var validOutcome = value.Outcome is "go" or "no-go";
        var allowedReasons = value.Lane == "provision" ? ProvisionReasons : CleanupReasons;
        var validReason = Array.IndexOf(allowedReasons, value.Reason) >= 0;
        var publicationIsValid = value.DescriptorPublicationReady ==
            (value.Lane == "provision" && value.Outcome == "go" && value.Reason == "fresh-fixture-provisioned");
        if (!validLane || !validOutcome || !validReason || !publicationIsValid)
        {
            throw new ArgumentException("The fresh-fixture evidence value is invalid.", nameof(value));
        }
    }

    /// <summary>
    /// 驗證 probe evidence 的 top-level semantics 與全部 nested categories 都屬固定字彙。這是 child
    /// 寫檔前的最後資料外洩防線：任何 caller text、CRM value、exception message 或不一致 bit 都會在
    /// serialization 前被拒絕，且 method 不把該值寫入 static/cache/session 或另一個 process。
    /// </summary>
    /// <param name="value">待發布的 immutable, deidentified read-only evidence。</param>
    private static void ValidatePreflightProbeValue(P72FreshSliceCFixturePreflightProbeLiveEvidenceValue value)
    {
        var allRemoteUnavailable = value.OperationalLists == "unavailable" &&
            value.LeaderMarker == "unavailable" &&
            value.OwnerKind == "unavailable" &&
            value.OwnerState == "unavailable" &&
            value.OwnerRelation == "unavailable" &&
            value.WeeklyReport == "unavailable";
        var allGreen = value.RequestShape == "valid" &&
            value.OperationalLists == "valid" &&
            value.LeaderMarker == "valid" &&
            value.OwnerKind == "systemuser" &&
            value.OwnerState == "active" &&
            value.OwnerRelation == "different-from-data8" &&
            value.WeeklyReport == "exactly-one-active";
        var validCategories =
            value.RequestShape is "valid" or "invalid" &&
            value.OperationalLists is "valid" or "invalid" or "unavailable" &&
            value.LeaderMarker is "valid" or "invalid" or "unavailable" &&
            value.OwnerKind is "systemuser" or "other-or-missing" or "unavailable" &&
            value.OwnerState is "active" or "inactive-or-missing" or "unavailable" &&
            value.OwnerRelation is "different-from-data8" or "same-as-data8" or "unavailable" &&
            value.WeeklyReport is "exactly-one-active" or "not-exactly-one-active" or "unavailable";
        var validReason = Array.IndexOf(PreflightProbeReasons, value.Reason) >= 0;
        var validCombination =
            (value.Outcome == "go" && value.Reason == "fresh-preconditions-proven" && value.ReadOnlyProbeExecuted && allGreen) ||
            (value.Outcome == "no-go" && value.Reason == "fresh-preconditions-not-proven" && value.ReadOnlyProbeExecuted && value.RequestShape == "valid" && !allGreen) ||
            (value.Outcome == "no-go" && value.Reason == "probe-input-invalid" && !value.ReadOnlyProbeExecuted && value.RequestShape == "invalid" && allRemoteUnavailable) ||
            (value.Outcome == "no-go" && value.Reason is "probe-unavailable" or "cleanup-failure" && !value.ReadOnlyProbeExecuted && value.RequestShape == "valid" && allRemoteUnavailable);
        if (!validCategories || !validReason || !validCombination)
        {
            throw new ArgumentException("The fresh preflight probe evidence value is invalid.", nameof(value));
        }
    }

    /// <summary>
    /// 拒絕 root、最終檔或暫存檔已是 reparse point。parent 在 child 前建立 root，child 不跟隨
    /// 可重導的路徑，因此同機其他 session 無法把 evidence 轉向其他使用者或 repository 位置。
    /// </summary>
    private static void RejectReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("The fresh-fixture evidence path is invalid.");
        }
    }

    /// <summary>
    /// 僅用於 JSON serialization 的私有 immutable record；欄位順序就是 PowerShell strict parser
    /// 的 wire contract，故不得加入 CRM IDs、owner、nonce 或任何上游 payload。
    /// </summary>
    private sealed record FreshFixtureEvidenceDocument(
        int SchemaVersion,
        string Lane,
        string Outcome,
        string Reason,
        bool OperationExecuted,
        bool DescriptorPublicationReady,
        bool FeatureFlagChanged);

    /// <summary>
    /// 表示 nonzero child exit 的非授權、去識別化診斷內容。這個 immutable 文件沒有任何 CRM entity、
    /// profile、credential、session、端點、GUID 或原始例外資料，且無法被視為成功 evidence。
    /// </summary>
    /// <param name="SchemaVersion">固定數字版本，供 parent strict parser 拒絕未知 wire shape。</param>
    /// <param name="Category">固定 allowlist 的前置、runtime 或 cleanup 失敗分類。</param>
    private sealed record FreshFixtureDiagnosticDocument(int SchemaVersion, string Category);

    /// <summary>
    /// 只用於 JSON serialization 的 preflight probe outer document。record property order 就是 parent
    /// strict parser 的 wire contract；不得加入 lane、ledger、descriptor publication、CRM identity、
    /// endpoint、credential 或任意 child diagnostic，否則會混淆 read-only proof 與 mutation control plane。
    /// </summary>
    private sealed record FreshPreflightProbeEvidenceDocument(
        int SchemaVersion,
        string Outcome,
        string Reason,
        string ProfileAlias,
        string DeploymentProfileAlias,
        string CeVersion,
        string Connector,
        bool PreflightOnly,
        bool OperationExecuted,
        bool ReadOnlyProbeExecuted,
        bool FeatureFlagChanged,
        FreshPreflightProbeProjection Probe);

    /// <summary>
    /// 表示 preflight probe 的 seven-field immutable nested projection。每個值已先經
    /// <see cref="ValidatePreflightProbeValue"/> 受限為 fixed vocabulary；此 record 不知道 CRM SDK、
    /// profile config、owner ID 或 raw response，避免 evidence DTO 成為資料外洩容器。
    /// </summary>
    private sealed record FreshPreflightProbeProjection(
        string RequestShape,
        string OperationalLists,
        string LeaderMarker,
        string OwnerKind,
        string OwnerState,
        string OwnerRelation,
        string WeeklyReport);
}

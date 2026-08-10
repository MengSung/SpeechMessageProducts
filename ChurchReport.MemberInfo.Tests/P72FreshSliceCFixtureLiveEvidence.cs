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
/// 將 fresh-fixture live child 的固定 evidence 以 create-new + flush + atomic-move 寫入
/// parent-owned temporary root。此類別不保存任何 static mutable state；每個呼叫者各自擁有
/// path、暫存檔、UTF-8 byte buffer 與檔案 stream，finally 會清除 buffer 並刪除未發布的
/// 暫存檔，避免不同使用者、profile 或 child session 讀到前次 invocation 的殘留資料。
/// </summary>
internal static class P72FreshSliceCFixtureLiveEvidence
{
    private const string EvidenceFileName = "P72FreshSliceCFixtureEvidence.json";
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

        var fullRoot = Path.GetFullPath(ownedRoot);
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullRoot) ||
            !string.Equals(Path.GetDirectoryName(fullPath), fullRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(fullPath), EvidenceFileName, StringComparison.Ordinal) ||
            File.Exists(fullPath))
        {
            throw new InvalidOperationException("The fresh-fixture evidence path is invalid.");
        }

        RejectReparsePoint(fullRoot);
        RejectReparsePoint(fullPath);

        var document = new FreshFixtureEvidenceDocument(
            SchemaVersion: 1,
            value.Lane,
            value.Outcome,
            value.Reason,
            value.OperationExecuted,
            value.DescriptorPublicationReady,
            FeatureFlagChanged: false);
        var bytes = Utf8WithoutBom.GetBytes(JsonSerializer.Serialize(document, JsonOptions) + "\r\n");
        var temporaryPath = Path.Combine(fullRoot, ".p72-fresh-evidence.tmp-" + Guid.NewGuid().ToString("N"));
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
}

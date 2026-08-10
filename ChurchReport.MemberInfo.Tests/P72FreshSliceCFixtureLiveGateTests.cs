// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveGateTests.cs
// 用途：P7.2 Slice C fresh-fixture child 的明確 opt-in gate 合約。
//
// 測試只驗證 live child 是否在缺少完整 parent-owned process gate 時 fail closed；
// 不讀取 Credential Manager、不建立 Data8 runtime、不呼叫 CE，也不寫入任何產品設定。
// ============================================================================

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 fresh-fixture provision/cleanup 只能由 parent runner 提供完整、短生命週期、
/// current-user 綁定的 process gate 後才允許進入 child。缺少任一輸入時，測試必須
/// 由 xUnit skip，而不是自行猜測 descriptor、讀取密碼或建立 CRM 連線。
/// </summary>
public sealed class P72FreshSliceCFixtureLiveGateTests
{
    private static readonly string[] GateEnvironmentNames =
    [
        "SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION",
        "SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP",
        "P7_2_SLICE_C_FRESH_LEDGER_ROOT",
        "P7_2_SLICE_C_FRESH_LEDGER_PATH",
        "P7_2_SLICE_C_FRESH_EVIDENCE_PATH",
        "P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION"
    ];

    /// <summary>
    /// 缺少任何 fresh-fixture gate 時，provision 與 cleanup attribute 都必須保持 skip。
    /// 這保護一般 test discovery、CI 與其他使用者 session，不讓 child 因為殘留環境變數
    /// 而意外取得 credential 或執行 CRM mutation。
    /// </summary>
    [Fact]
    public void Fresh_fixture_live_attributes_require_complete_explicit_gate()
    {
        var snapshot = GateEnvironmentNames.ToDictionary(
            static name => name,
            static name => Environment.GetEnvironmentVariable(name));
        try
        {
            foreach (var name in GateEnvironmentNames)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            var provisionAttribute = new P72Data8SliceCFreshProvisionFactAttribute();
            var cleanupAttribute = new P72Data8SliceCFreshCleanupFactAttribute();

            provisionAttribute.Skip.Should().NotBeNullOrWhiteSpace();
            cleanupAttribute.Skip.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            foreach (var pair in snapshot)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    /// <summary>
    /// provision child 只能向 parent 指派的 temporary path 寫入單一去識別化 evidence。
    /// 此測試保護 strict JSON schema、UTF-8 no-BOM、CRLF 與無 CRM ID/credential 的輸出邊界，
    /// 讓 parent 可以在 child exit code 成功後才決定是否發布本機 descriptor。
    /// </summary>
    [Fact]
    public void Fresh_fixture_evidence_writer_emits_only_the_strict_sanitized_contract()
    {
        var root = Path.Combine(Path.GetTempPath(), "speechmessage-p72-fresh-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "P72FreshSliceCFixtureEvidence.json");
            P72FreshSliceCFixtureLiveEvidence.Write(
                path,
                root,
                new P72FreshSliceCFixtureLiveEvidenceValue(
                    "provision",
                    "go",
                    "fresh-fixture-provisioned",
                    OperationExecuted: true,
                    DescriptorPublicationReady: true));

            var bytes = File.ReadAllBytes(path);
            try
            {
                bytes.Should().NotBeEmpty();
                (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).Should().BeFalse();
                var text = Encoding.UTF8.GetString(bytes);
                text.Should().NotMatchRegex("(?<!\\r)\\n");
                text.Should().EndWith("\r\n");

                using var document = JsonDocument.Parse(text);
                document.RootElement.EnumerateObject().Select(static property => property.Name).Should().BeEquivalentTo(
                [
                    "schemaVersion",
                    "lane",
                    "outcome",
                    "reason",
                    "operationExecuted",
                    "descriptorPublicationReady",
                    "featureFlagChanged"
                ], options => options.WithStrictOrdering());
                document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
                document.RootElement.GetProperty("lane").GetString().Should().Be("provision");
                document.RootElement.GetProperty("outcome").GetString().Should().Be("go");
                document.RootElement.GetProperty("reason").GetString().Should().Be("fresh-fixture-provisioned");
                document.RootElement.GetProperty("operationExecuted").GetBoolean().Should().BeTrue();
                document.RootElement.GetProperty("descriptorPublicationReady").GetBoolean().Should().BeTrue();
                document.RootElement.GetProperty("featureFlagChanged").GetBoolean().Should().BeFalse();
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

/// <summary>
/// 只在 parent runner 明確提供 fresh provision 所需的短生命週期環境時開啟 child。
/// 這個 attribute 不讀取 secret 內容，也不自行建立 runtime；它只在 test discovery
/// 階段檢查固定 gate 是否完整，避免殘留環境讓一般測試意外連到 CE。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P72Data8SliceCFreshProvisionFactAttribute : FactAttribute
{
    private static readonly string[] RequiredEnvironmentNames =
    [
        "SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION",
        "CRM_PASSWORD",
        "P7_2_SLICE_C_FRESH_LEDGER_ROOT",
        "P7_2_SLICE_C_FRESH_LEDGER_PATH",
        "P7_2_SLICE_C_FRESH_EVIDENCE_PATH",
        "P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION",
        "P7_2_SLICE_C_FRESH_NONCE",
        "P7_2_SLICE_C_FRESH_OWNER",
        "P7_2_SLICE_C_FRESH_ADD_LIST_ID",
        "P7_2_SLICE_C_FRESH_REMOVE_LIST_ID",
        "P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID",
        "P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID",
        "P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID",
        "P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID",
        "P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC"
    ];

    /// <summary>
    /// 缺少任一固定 gate 時採用 skip；不把缺少輸入當成可嘗試的 live operation。
    /// </summary>
    public P72Data8SliceCFreshProvisionFactAttribute()
    {
        if (!HasCompleteGate("SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION"))
        {
            Skip = "P7.2 fresh-fixture provision requires an explicit parent-owned Data8 gate.";
        }
    }

    /// <summary>
    /// 驗證固定 provision gate 與所有 bounded child inputs 都存在且不是空白文字。
    /// </summary>
    private static bool HasCompleteGate(string modeName)
        => string.Equals(Environment.GetEnvironmentVariable(modeName), "1", StringComparison.Ordinal) &&
           RequiredEnvironmentNames.All(static name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
}

/// <summary>
/// 只在 parent runner 明確提供 fresh cleanup 所需的短生命週期環境時開啟 child。
/// cleanup 仍必須以 ledger-listed IDs 為唯一來源，不能藉由環境變數自行掃描 CRM。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P72Data8SliceCFreshCleanupFactAttribute : FactAttribute
{
    private static readonly string[] RequiredEnvironmentNames =
    [
        "SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP",
        "CRM_PASSWORD",
        "P7_2_SLICE_C_FRESH_LEDGER_ROOT",
        "P7_2_SLICE_C_FRESH_LEDGER_PATH",
        "P7_2_SLICE_C_FRESH_EVIDENCE_PATH",
        "P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION",
        "P7_2_SLICE_C_FRESH_OWNER",
        "P7_2_SLICE_C_FRESH_ADD_LIST_ID",
        "P7_2_SLICE_C_FRESH_REMOVE_LIST_ID",
        "P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID",
        "P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID",
        "P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID",
        "P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC"
    ];

    /// <summary>
    /// 缺少任一固定 gate 時採用 skip；不執行猜測式 cleanup 或刪除未知資料。
    /// </summary>
    public P72Data8SliceCFreshCleanupFactAttribute()
    {
        if (!HasCompleteGate("SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP"))
        {
            Skip = "P7.2 fresh-fixture cleanup requires an explicit parent-owned Data8 gate.";
        }
    }

    /// <summary>
    /// 驗證 cleanup 所需的固定 process inputs；credential 只驗證由 parent 傳入，不在此輸出。
    /// </summary>
    private static bool HasCompleteGate(string modeName)
        => string.Equals(Environment.GetEnvironmentVariable(modeName), "1", StringComparison.Ordinal) &&
           RequiredEnvironmentNames.All(static name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
}

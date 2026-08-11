// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveGateTests.cs
// 用途：P7.2 Slice C fresh-fixture child 的明確 opt-in gate 合約。
//
// 測試只驗證 live child 是否在缺少完整 parent-owned process gate 時 fail closed；
// 不讀取 Credential Manager、不建立 Data8 runtime、不呼叫 CE，也不寫入任何產品設定。
// ============================================================================

using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 將會暫存及還原 process-wide environment variables 的 fresh-fixture gate tests 放入單一不可平行
/// xUnit collection。這些測試不會接觸 Credential Manager、Data8 runtime 或 CE，但若與其他 process
/// environment 測試並行，仍可能在讀取與 finally 還原之間看到另一個測試的值。停用 collection 內平行化
/// 讓每個測試對其完整變數集合擁有明確、短暫且可還原的生命週期，不把 mutable environment state 洩漏到
/// 其他使用者、profile、child process 或測試案例。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class P72FreshSliceCFixtureLiveGateTestCollection
{
    /// <summary>
    /// 提供所有 fresh-fixture gate 測試共用的固定 collection 名稱；名稱本身不含 credential、路徑、
    /// nonce 或 CRM identity，僅作為 xUnit 的 process-wide state 隔離鍵。
    /// </summary>
    public const string Name = "P7.2 Slice C fresh-fixture process environment";
}

/// <summary>
/// 驗證 fresh-fixture provision/cleanup/read-only preflight probe 只能由 parent runner 提供完整、短生命週期、
/// current-user 綁定的 process gate 後才允許進入 child。缺少任一輸入時，測試必須
/// 由 xUnit skip，而不是自行猜測 descriptor、讀取密碼或建立 CRM 連線。
/// </summary>
[Collection(P72FreshSliceCFixtureLiveGateTestCollection.Name)]
public sealed class P72FreshSliceCFixtureLiveGateTests
{
    private static readonly string[] GateEnvironmentNames =
    [
        "SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION",
        "SPEECHMESSAGE_P7_2_SLICE_C_FRESH_CLEANUP",
        "SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE",
        "P7_2_SLICE_C_FRESH_LEDGER_ROOT",
        "P7_2_SLICE_C_FRESH_LEDGER_PATH",
        "P7_2_SLICE_C_FRESH_EVIDENCE_PATH",
        "P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH",
        "P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION",
        "P7_2_SLICE_C_FRESH_PREFLIGHT_EVIDENCE_PATH",
        "P7_2_SLICE_C_FRESH_OWNER",
        "P7_2_SLICE_C_FRESH_ADD_LIST_ID",
        "P7_2_SLICE_C_FRESH_REMOVE_LIST_ID",
        "P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID",
        "P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID",
        "P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID",
        "P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID",
        "P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC",
        "CRM_PASSWORD"
    ];

    /// <summary>
    /// 定義 provision child 在進入 live method 前必須由 parent 提供的完整 process-scoped input 名稱。
    /// 此集合只用於暫存、還原測試自己的環境變數；測試不會將任何值傳至 Data8 runtime、Credential
    /// Manager 或 CE，並會在 finally 恢復先前值，以免污染同一 Windows session 的其他測試。
    /// </summary>
    private static readonly string[] ProvisionEnvironmentNames =
    [
        "SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION",
        "CRM_PASSWORD",
        "P7_2_SLICE_C_FRESH_LEDGER_ROOT",
        "P7_2_SLICE_C_FRESH_LEDGER_PATH",
        "P7_2_SLICE_C_FRESH_EVIDENCE_PATH",
        "P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH",
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
    /// 缺少任何 fresh-fixture gate 時，provision、cleanup 與 read-only probe attribute 都必須保持 skip。
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
            var preflightProbeAttribute = new P72Data8SliceCFreshPreflightProbeFactAttribute();

            provisionAttribute.Skip.Should().NotBeNullOrWhiteSpace();
            cleanupAttribute.Skip.Should().NotBeNullOrWhiteSpace();
            preflightProbeAttribute.Skip.Should().NotBeNullOrWhiteSpace();
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
    /// 驗證 provision Fact gate 對非空但不存在的 parent-owned ledger root 立即 fail closed。故障注入
    /// 為完整的字串環境輸入搭配一個尚未建立的唯一 temporary root；決定性斷言是 attribute 保持 skip，
    /// 且 child 不建立該目錄。這個 gate 位於 live test body 之前，因此 root 不存在時不會讀取
    /// <c>CRM_PASSWORD</c>、建立 Data8 runtime 或執行任何 CE I/O。finally 還原每個 process variable，
    /// 避免測試 session 的值流入其他 child、profile 或使用者。
    /// </summary>
    [Fact]
    public void Fresh_provision_gate_rejects_a_nonexistent_parent_owned_ledger_root_without_creating_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "speechmessage-p72-fresh-gate-missing-root-" + Guid.NewGuid().ToString("N"));
        var snapshot = ProvisionEnvironmentNames.ToDictionary(
            static name => name,
            static name => Environment.GetEnvironmentVariable(name));
        try
        {
            foreach (var name in ProvisionEnvironmentNames)
            {
                Environment.SetEnvironmentVariable(name, "test-only-input");
            }

            Environment.SetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION", "1");
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_LEDGER_ROOT", root);

            var provisionAttribute = new P72Data8SliceCFreshProvisionFactAttribute();

            provisionAttribute.Skip.Should().NotBeNullOrWhiteSpace();
            Directory.Exists(root).Should().BeFalse();
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
    /// 驗證 live child 的最早本機輸入解析也會拒絕不存在的 parent-owned ledger root，而不是讓後續流程
    /// 先讀取 credential、配置 runtime 或接觸 CE。故障注入提供所有格式正確的 scalar 與不存在 root，
    /// 再透過反映只呼叫 private local environment parser；決定性斷言是 parser 回傳
    /// <see cref="InvalidOperationException"/> 且目錄仍不存在。此測試完全不進入 live operation，所有
    /// 環境值僅為測試字串並在 finally 還原，故不會保留 secret、session 或跨 profile 狀態。
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void Fresh_provision_environment_rejects_a_nonexistent_parent_owned_ledger_root_without_creating_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "speechmessage-p72-fresh-environment-missing-root-" + Guid.NewGuid().ToString("N"));
        var snapshot = ProvisionEnvironmentNames.ToDictionary(
            static name => name,
            static name => Environment.GetEnvironmentVariable(name));
        try
        {
            var nonce = Guid.NewGuid();
            foreach (var name in ProvisionEnvironmentNames)
            {
                Environment.SetEnvironmentVariable(name, "test-only-input");
            }

            Environment.SetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PROVISION", "1");
            Environment.SetEnvironmentVariable("CRM_PASSWORD", "must-not-be-read");
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_LEDGER_ROOT", root);
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_LEDGER_PATH", Path.Combine(root, "fresh-slice-c-ledger.json"));
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_EVIDENCE_PATH", Path.Combine(root, "P72FreshSliceCFixtureEvidence.json"));
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH", Path.Combine(root, "P72FreshSliceCFixtureDiagnostic.json"));
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_DESCRIPTOR_CONFIRMATION", "replace-stale-descriptor");
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_NONCE", nonce.ToString("D"));
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_OWNER", WindowsIdentity.GetCurrent().Name);
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_ADD_LIST_ID", Guid.NewGuid().ToString("D"));
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_REMOVE_LIST_ID", Guid.NewGuid().ToString("D"));
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_SMALL_GROUP_LIST_ID", Guid.NewGuid().ToString("D"));
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_EXISTING_TARGET_LEADER_ID", Guid.NewGuid().ToString("D"));
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_TRANSFER_SOURCE_LIST_ID", Guid.NewGuid().ToString("D"));
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_TRANSFER_TARGET_LIST_ID", Guid.NewGuid().ToString("D"));
            Environment.SetEnvironmentVariable("P7_2_SLICE_C_FRESH_TRANSFER_WEEK_START_UTC", "2026-08-09T00:00:00.0000000+00:00");

            var environmentParser = typeof(LivePackage02Data8ListManagementFreshFixtureTests).GetMethod(
                "ReadProvisionEnvironment",
                BindingFlags.NonPublic | BindingFlags.Static);
            environmentParser.Should().NotBeNull();

            Action action = () => environmentParser!.Invoke(null, null);

            action.Should().Throw<TargetInvocationException>()
                .Which.InnerException.Should().BeOfType<InvalidOperationException>();
            Directory.Exists(root).Should().BeFalse();
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

    /// <summary>
    /// 保護 fresh preflight probe 的 child-to-parent wire boundary。測試以完整的固定 go categories
    /// 寫入 parent-owned temporary root；決定性 assertions 是 JSON 使用 UTF-8 no-BOM/CRLF、固定
    /// property order、operationExecuted/featureFlagChanged 永遠為 false，且 nested projection 不含
    /// CRM ID、名稱、owner identity、endpoint、credential 或 raw exception。finally 只刪除本測試
    /// 建立的 temporary root，不會保留其他 session、profile 或 fixture state。
    /// </summary>
    /// <param name="weeklyReportCategory">唯一週報或零週報的固定正常業務分類。</param>
    [Theory]
    [InlineData("exactly-one-active")]
    [InlineData("zero-active")]
    public void Fresh_preflight_probe_evidence_writer_emits_only_the_strict_zero_mutation_contract(string weeklyReportCategory)
    {
        var root = Path.Combine(Path.GetTempPath(), "speechmessage-p72-fresh-preflight-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "P72FreshSliceCFixturePreflightProbeEvidence.json");
            P72FreshSliceCFixtureLiveEvidence.WritePreflightProbe(
                path,
                root,
                new P72FreshSliceCFixturePreflightProbeLiveEvidenceValue(
                    "go",
                    "fresh-preconditions-proven",
                    ReadOnlyProbeExecuted: true,
                    RequestShape: "valid",
                    OperationalLists: "valid",
                    LeaderMarker: "valid",
                    OwnerKind: "systemuser",
                    OwnerState: "active",
                    OwnerRelation: "different-from-data8",
                    WeeklyReport: weeklyReportCategory));

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
                    "outcome",
                    "reason",
                    "profileAlias",
                    "deploymentProfileAlias",
                    "ceVersion",
                    "connector",
                    "preflightOnly",
                    "operationExecuted",
                    "readOnlyProbeExecuted",
                    "featureFlagChanged",
                    "probe"
                ], options => options.WithStrictOrdering());
                document.RootElement.GetProperty("outcome").GetString().Should().Be("go");
                document.RootElement.GetProperty("reason").GetString().Should().Be("fresh-preconditions-proven");
                document.RootElement.GetProperty("preflightOnly").GetBoolean().Should().BeFalse();
                document.RootElement.GetProperty("operationExecuted").GetBoolean().Should().BeFalse();
                document.RootElement.GetProperty("readOnlyProbeExecuted").GetBoolean().Should().BeTrue();
                document.RootElement.GetProperty("featureFlagChanged").GetBoolean().Should().BeFalse();
                document.RootElement.GetProperty("probe").GetProperty("weeklyReport").GetString().Should().Be(weeklyReportCategory);
                document.RootElement.GetProperty("probe").EnumerateObject().Select(static property => property.Name).Should().BeEquivalentTo(
                [
                    "requestShape",
                    "operationalLists",
                    "leaderMarker",
                    "ownerKind",
                    "ownerState",
                    "ownerRelation",
                    "weeklyReport"
                ], options => options.WithStrictOrdering());
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

    /// <summary>
    /// 驗證 child 在 fresh provision 以 <c>no-go</c> 結束時，只能透過 parent-owned 暫存根目錄
    /// 寫入固定 allowlist 的診斷分類。此檔案不是成功 evidence，parent 即使讀到它也必須維持
    /// <c>child-process-failed</c>、不發布 descriptor、不清理遠端資料、且永遠不自動重試。
    /// 測試使用獨立暫存目錄並在 finally 遞迴刪除，避免保留任何跨測試 session、fixture 或診斷狀態。
    /// </summary>
    [Fact]
    public void Fresh_fixture_diagnostic_writer_emits_only_a_strict_deidentified_category()
    {
        var root = Path.Combine(Path.GetTempPath(), "speechmessage-p72-fresh-diagnostic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "P72FreshSliceCFixtureDiagnostic.json");
            P72FreshSliceCFixtureLiveEvidence.TryWriteDiagnostic(
                path,
                root,
                "fixture-precondition-failed");

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
                ["schemaVersion", "category"], options => options.WithStrictOrdering());
                document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);
                document.RootElement.GetProperty("category").GetString().Should().Be("fixture-precondition-failed");
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

    /// <summary>
    /// 驗證 parent-owned root gate 會拒絕 normal leaf 下方的 reparse-point ancestor，而不是只查看 leaf
    /// attribute。故障注入建立本測試唯一擁有的 target directory、directory symbolic link 與 link 下的
    /// 正常 leaf；決定性斷言是 ancestor 具有 <see cref="FileAttributes.ReparsePoint"/>、leaf 不具有它，
    /// 但 gate 仍傳回 <see langword="false"/>，因此 Fact attribute 與 child parser 都會在 credential/runtime/
    /// CE 路徑前 fail closed。若 Windows policy 不允許建立 symbolic link，測試會明確 skip，不將本機權限
    /// 當成產品失敗；finally 僅刪除此測試的 temporary tree，不保留 link、ID、session 或環境變數。
    /// </summary>
    [Fact(Skip = "Requires SeCreateSymbolicLinkPrivilege; the current Windows test policy returns ERROR_PRIVILEGE_NOT_HELD for directory symbolic links.")]
    public void Parent_owned_root_gate_rejects_a_reparse_point_ancestor()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "speechmessage-p72-fresh-parent-gate-reparse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            var targetRoot = Path.Combine(testRoot, "target");
            var reparseAncestor = Path.Combine(testRoot, "reparse-ancestor");
            var ownedRoot = Path.Combine(targetRoot, "owned-root");
            Directory.CreateDirectory(ownedRoot);
            try
            {
                Directory.CreateSymbolicLink(reparseAncestor, targetRoot);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw Xunit.Sdk.SkipException.ForSkip(
                    "Windows denied directory symbolic-link creation required to prove the parent gate ancestor reparse guard: " +
                    exception.GetType().Name);
            }
            catch (PlatformNotSupportedException exception)
            {
                throw Xunit.Sdk.SkipException.ForSkip(
                    "This platform does not support the directory symbolic-link test required to prove the parent gate ancestor reparse guard: " +
                    exception.GetType().Name);
            }
            catch (IOException exception) when (exception.HResult == unchecked((int)0x80070522))
            {
                throw Xunit.Sdk.SkipException.ForSkip(
                    "Windows policy denied the directory symbolic-link privilege required to prove the parent gate ancestor reparse guard: " +
                    exception.GetType().Name);
            }

            var rootViaReparseAncestor = Path.Combine(reparseAncestor, "owned-root");
            (File.GetAttributes(reparseAncestor) & FileAttributes.ReparsePoint).Should().NotBe(0);
            (File.GetAttributes(rootViaReparseAncestor) & FileAttributes.ReparsePoint).Should().Be(0);

            P72FreshSliceCFixtureParentOwnedRootGate
                .IsExistingNonReparseDirectory(rootViaReparseAncestor)
                .Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}

/// <summary>
/// 集中驗證 fresh-fixture child 僅可使用 parent 已建立的 local ledger root。這是 process gate 與
/// live child 共用的前置條件；它只讀取 path metadata，不建立目錄、不讀取 credential、不配置 runtime，
/// 也不會保留任何 profile、owner、nonce 或 session state。不存在、無法正規化或 reparse 的目錄一律
/// 回傳 <see langword="false"/>，由呼叫端在任何 Data8/CE I/O 前 fail closed。
/// </summary>
internal static class P72FreshSliceCFixtureParentOwnedRootGate
{
    /// <summary>
    /// 判斷指定 root 是否為可安全交給 child 使用的既有普通目錄。parent 是該目錄的唯一建立與 finally
    /// cleanup owner；此檢查刻意不呼叫 <see cref="Directory.CreateDirectory(string)"/>，避免 child 對
    /// 不存在的 invocation 路徑取得持久化 recovery state。取得 attributes 失敗時視為不可信輸入，
    /// 不把原始路徑或例外傳到 evidence、console 或跨 session state。
    /// </summary>
    /// <param name="root">由 parent process 透過受限環境變數傳入的 bounded local root。</param>
    /// <returns>root 已存在且不是 reparse point 時為 <see langword="true"/>。</returns>
    internal static bool IsExistingNonReparseDirectory(string? root)
        // FileLedger 與 Fact gate 必須對同一條 lexical path 套用完全相同的 ancestor 防護；否則 gate
        // 可接受經 junction/symlink 離開 parent root 的路徑，而 child 才在較晚 I/O 時失敗。共用 guard
        // 不建立目錄、不讀取 credential，且對 metadata 例外 fail closed 回傳 false。
        => P72FreshSliceCFixtureOwnedPathGuard.IsExistingNonReparseDirectory(root);
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
        "P7_2_SLICE_C_FRESH_DIAGNOSTIC_PATH",
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
    /// 驗證固定 provision gate 與所有 bounded child inputs 都存在且不是空白文字。ledger root 必須先
    /// 被證明為 parent 預先建立的普通目錄，才會檢查其餘輸入；因此缺失 root 不會觸及 password gate。
    /// </summary>
    private static bool HasCompleteGate(string modeName)
        => string.Equals(Environment.GetEnvironmentVariable(modeName), "1", StringComparison.Ordinal) &&
           P72FreshSliceCFixtureParentOwnedRootGate.IsExistingNonReparseDirectory(
               Environment.GetEnvironmentVariable("P7_2_SLICE_C_FRESH_LEDGER_ROOT")) &&
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
    /// 驗證 cleanup 所需的固定 process inputs；credential 只驗證由 parent 傳入，不在此輸出。缺失或
    /// reparse ledger root 會先使 gate 保持 skip，避免 child 在非 parent-owned 路徑建立或讀取 recovery state。
    /// </summary>
    private static bool HasCompleteGate(string modeName)
        => string.Equals(Environment.GetEnvironmentVariable(modeName), "1", StringComparison.Ordinal) &&
           P72FreshSliceCFixtureParentOwnedRootGate.IsExistingNonReparseDirectory(
               Environment.GetEnvironmentVariable("P7_2_SLICE_C_FRESH_LEDGER_ROOT")) &&
           RequiredEnvironmentNames.All(static name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
}

/// <summary>
/// 只在 parent runner 提供 explicit、短生命週期的 fresh read-only preflight inputs 時開啟 child。
/// attribute 不建立 ledger、directory、Data8 runtime 或 CRM connection，也不讀取 password 內容；
/// 它只檢查 fixed process variables，避免一般 test discovery、CI 或殘留 shell state 意外執行
/// deployment-owned Data8 WhoAmI/Retrieve/RetrieveMultiple diagnostic。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class P72Data8SliceCFreshPreflightProbeFactAttribute : FactAttribute
{
    private static readonly string[] RequiredEnvironmentNames =
    [
        "SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE",
        "CRM_PASSWORD",
        "P7_2_SLICE_C_FRESH_PREFLIGHT_EVIDENCE_PATH",
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
    /// 缺少任一 fixed environment input 時保持 skip。這與 provision/cleanup 的 explicit gate
    /// 原則一致，但不要求 ledger/nonce/descriptor-confirmation，因為 probe 的完整契約是零 mutation、
    /// 零 ledger publication、零 descriptor publication 與零 cleanup。
    /// </summary>
    public P72Data8SliceCFreshPreflightProbeFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("SPEECHMESSAGE_P7_2_SLICE_C_FRESH_PREFLIGHT_PROBE"),
                "1",
                StringComparison.Ordinal) ||
            !RequiredEnvironmentNames.All(static name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = "P7.2 fresh preflight probe requires an explicit parent-owned Data8 read-only gate.";
        }
    }
}

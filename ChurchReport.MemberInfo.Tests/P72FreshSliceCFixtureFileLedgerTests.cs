// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureFileLedgerTests.cs
// 用途：驗證 P7.2 Slice C fresh-fixture pending ledger 的 current-user 與 atomic-file 契約。
//
// 此檔案只測試本機 temporary root，不建立 Data8 runtime、不讀取 Credential Manager、不連線 CE，
// 也不把 ledger 內容寫入 evidence、TRX、console 或 repository。ledger 是 remote mutation recovery
// input，必須只由目前 Windows owner 使用，且每次 stage 都以同一個 owned path 原子取代。
// ============================================================================

using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 fresh-fixture ledger 的檔案邊界、owner 綁定與 deterministic cleanup。
/// 測試資料只存在於每個 test 自己建立的 temporary root；測試結束時完整移除該 root，
/// 因而不會讓某次 fixture、profile 或 Windows session 的資料留給下一個測試。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class P72FreshSliceCFixtureFileLedgerTests
{
    private static readonly Guid Nonce = Guid.Parse("12121212-1212-1212-1212-121212121212");
    private static readonly Guid SourceContactId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private static readonly Guid LeaderContactId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
    private static readonly Guid RelationshipListId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
    private static readonly Guid OriginalTargetLeaderContactId = Guid.Parse("dddddddd-4444-4444-4444-444444444444");

    /// <summary>
    /// 保護 ledger 的 cross-user 邊界：寫入文件必須帶有目前 operator 的 owner、固定 profile、
    /// CE 版本與 Data8 connector，並且只輸出 allowlisted stage/ID 欄位。UTF-8 no-BOM、CRLF-only
    /// 與 final CRLF 是 parent PowerShell strict parser 的 wire contract；任何違反都不可作為
    /// recovery authorization。測試讀完 bytes 後立即清除，避免長生命週期測試 process 留存資料。
    /// </summary>
    [Fact]
    public void Persist_writes_current_user_bound_strict_document()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            using var ledger = new P72FreshSliceCFixtureFileLedger(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            ledger.Persist(new P72FreshSliceCFixtureLedgerState(
                "fresh-graph-proven",
                Nonce,
                     SourceContactId,
                     LeaderContactId,
                     RelationshipListId,
                     OriginalTargetLeaderContactId));

            var bytes = File.ReadAllBytes(path);
            try
            {
                bytes.Should().NotBeEmpty();
                var hasUtf8Bom = bytes.Length >= 3 &&
                    bytes[0] == 0xEF &&
                    bytes[1] == 0xBB &&
                    bytes[2] == 0xBF;
                hasUtf8Bom.Should().BeFalse();
                var text = Encoding.UTF8.GetString(bytes);
                text.Should().NotMatchRegex("(?<!\\r)\\n");
                text.Should().EndWith("\r\n");

                using var document = JsonDocument.Parse(text);
                var propertyNames = document.RootElement.EnumerateObject().Select(static property => property.Name);
                propertyNames.Should().BeEquivalentTo(
                [
                    "schemaVersion",
                    "fixtureId",
                    "profileAlias",
                    "ceVersion",
                    "connector",
                    "ownerIdentity",
                    "stage",
                    "nonce",
                    "sourceContactId",
                    "leaderContactId",
                    "relationshipListId",
                    "originalTargetLeaderContactId"
                ], options => options.WithStrictOrdering());
                document.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(2);
                document.RootElement.GetProperty("fixtureId").GetString().Should().Be("p7.2-slice-c-fresh-fixture");
                document.RootElement.GetProperty("profileAlias").GetString().Should().Be("crm91");
                document.RootElement.GetProperty("ceVersion").GetString().Should().Be("9.1");
                document.RootElement.GetProperty("connector").GetString().Should().Be("Data8");
                document.RootElement.GetProperty("ownerIdentity").GetString().Should().Be(owner);
                document.RootElement.GetProperty("stage").GetString().Should().Be("fresh-graph-proven");
                document.RootElement.GetProperty("nonce").GetGuid().Should().Be(Nonce);
                document.RootElement.GetProperty("sourceContactId").GetGuid().Should().Be(SourceContactId);
                document.RootElement.GetProperty("leaderContactId").GetGuid().Should().Be(LeaderContactId);
                document.RootElement.GetProperty("relationshipListId").GetGuid().Should().Be(RelationshipListId);
                document.RootElement.GetProperty("originalTargetLeaderContactId").GetGuid().Should().Be(OriginalTargetLeaderContactId);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }

            Directory.EnumerateFiles(root, "*.tmp-*", SearchOption.TopDirectoryOnly).Should().BeEmpty();
        }
        finally
        {
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 保護 stage replacement 的唯一 owner：同一個 owned path 可以由後續 read-back stage 原子取代，
    /// 但不會留下 temp 檔、備份檔或兩份可被不同 session 讀取的 ledger。測試第二次寫入只改變
    /// stage 與已證明 ID，不會建立新的共享 cache 或 static state。
    /// </summary>
    [Fact]
    public void Persist_replaces_the_same_owned_path_without_residual_files()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            using var ledger = new P72FreshSliceCFixtureFileLedger(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

             ledger.Persist(new P72FreshSliceCFixtureLedgerState(
                 "preflight-proven",
                 Nonce,
                 null,
                 null,
                 null,
                 OriginalTargetLeaderContactId));
            ledger.Persist(new P72FreshSliceCFixtureLedgerState(
                "source-contact-created",
                Nonce,
                 SourceContactId,
                 null,
                 null,
                 OriginalTargetLeaderContactId));

            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            document.RootElement.GetProperty("stage").GetString().Should().Be("source-contact-created");
            document.RootElement.GetProperty("sourceContactId").GetGuid().Should().Be(SourceContactId);
            document.RootElement.TryGetProperty("leaderContactId", out _).Should().BeTrue();
            Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Should().Equal("fresh-slice-c-ledger.json");
        }
        finally
        {
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證同一個 fresh-fixture invocation 的原始 baseline leader 一旦在第一個 ledger stage
    /// 被接受後，後續 stage 不得藉由相同 nonce 改寫它。故障注入會先寫入
    /// <c>preflight-proven</c>，再以另一個非空 GUID 寫入 <c>source-contact-created</c>；
    /// 若 writer 接受第二筆資料，cleanup 便可能在跨程序恢復時將錯誤的既有領隊當作
    /// baseline，破壞 owner isolation 與可回復性。決定性斷言是第二次寫入失敗，且檔案
    /// 仍保留第一個 stage 的原始 baseline；測試只使用每次呼叫專屬的 temporary root，
    /// 因此不會保留跨測試、跨使用者或跨 profile 的檔案、session 或 CRM 資源。
    /// </summary>
    [Fact]
    public void Persist_rejects_a_stage_that_changes_the_immutable_original_baseline_leader()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            using var ledger = new P72FreshSliceCFixtureFileLedger(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            ledger.Persist(new P72FreshSliceCFixtureLedgerState(
                "preflight-proven",
                Nonce,
                null,
                null,
                null,
                OriginalTargetLeaderContactId));

            var changedBaselineLeader = Guid.Parse("eeeeeeee-5555-5555-5555-555555555555");
            Action action = () => ledger.Persist(new P72FreshSliceCFixtureLedgerState(
                "source-contact-created",
                Nonce,
                SourceContactId,
                null,
                null,
                changedBaselineLeader));

            action.Should().Throw<InvalidOperationException>();

            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            document.RootElement.GetProperty("stage").GetString().Should().Be("preflight-proven");
            document.RootElement.GetProperty("originalTargetLeaderContactId").GetGuid()
                .Should().Be(OriginalTargetLeaderContactId);
        }
        finally
        {
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// cleanup child 只能讀取同一個 current-user、crm91/Data8/CE 9.1 ledger，且 stage 必須已達
    /// <c>fresh-graph-proven</c>。這個測試保護 ambiguous provision 後的 recovery path 不會接受
    /// 其他 profile、其他 Windows owner 或半完成 stage 的任意 GUID。
    /// </summary>
    [Fact]
    public void Read_returns_only_the_final_fresh_graph_stage_for_the_same_owner()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            using (var ledger = new P72FreshSliceCFixtureFileLedger(
                       path,
                       root,
                       owner,
                       "crm91",
                       "sunnyvalechback",
                       "9.1",
                       "Data8"))
            {
                ledger.Persist(new P72FreshSliceCFixtureLedgerState(
                    "fresh-graph-proven",
                    Nonce,
                    SourceContactId,
                    LeaderContactId,
                    RelationshipListId,
                    OriginalTargetLeaderContactId));
            }

            var state = P72FreshSliceCFixtureFileLedger.Read(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            state.Stage.Should().Be("fresh-graph-proven");
            state.Nonce.Should().Be(Nonce);
            state.SourceContactId.Should().Be(SourceContactId);
            state.LeaderContactId.Should().Be(LeaderContactId);
            state.RelationshipListId.Should().Be(RelationshipListId);
            state.OriginalTargetLeaderContactId.Should().Be(OriginalTargetLeaderContactId);
        }
        finally
        {
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證清理控制平面不會把完整且看似可用的舊版帳本當成目前的復原授權。
    /// 此測試刻意寫入結構版本一且其餘識別、擁有者、設定檔與新鮮圖形欄位皆正確的本機檔案；
    /// 讀取端必須在任何 CRM 清理前拒絕該檔案，避免舊結構遺失不可變原始領隊基準時仍能跨執行續用。
    /// 決定性斷言是 <see cref="P72FreshSliceCFixtureFileLedger.Read"/> 拋出受限的
    /// <see cref="InvalidOperationException"/>，而不是推測、升級或接受舊版資料。
    /// </summary>
    [Fact]
    public void Read_rejects_schema_version_one_even_when_its_recovery_ids_are_complete()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            WriteFinalLedgerDocument(path, owner, schemaVersion: 1, OriginalTargetLeaderContactId);

            Action action = () => P72FreshSliceCFixtureFileLedger.Read(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            action.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證 JSON 結構清單把不可變原始領隊基準列為必要欄位，而不是讓遺失欄位退化為空值。
    /// 故障注入僅移除 <c>originalTargetLeaderContactId</c>，其餘欄位仍是同一 Windows 擁有者、
    /// 同一 Data8/CE 9.1 綁定及已完成的新鮮圖形；此情境必須在讀取階段失敗，才不會讓清理工作
    /// 對由重新發布描述元件改變過的領隊來源作出猜測。
    /// </summary>
    [Fact]
    public void Read_rejects_a_document_missing_the_immutable_original_baseline_leader()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            WriteFinalLedgerDocument(
                path,
                owner,
                schemaVersion: 2,
                OriginalTargetLeaderContactId,
                includeOriginalTargetLeaderContactId: false);

            Action action = () => P72FreshSliceCFixtureFileLedger.Read(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            action.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證空的原始領隊基準不能取得清理授權。
    /// 測試建立版本二且其他固定復原資訊完整的檔案，只把原始領隊識別碼注入為空 GUID；
    /// 讀取端必須失敗關閉，因為空值無法證明新建來源聯絡人原本由哪一個非服務帳號擁有。
    /// </summary>
    [Fact]
    public void Read_rejects_an_empty_immutable_original_baseline_leader()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            WriteFinalLedgerDocument(path, owner, schemaVersion: 2, Guid.Empty);

            Action action = () => P72FreshSliceCFixtureFileLedger.Read(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            action.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證不可變原始領隊不能被已發布的新鮮領隊取代。
    /// 故障注入把 <c>originalTargetLeaderContactId</c> 寫成目前圖形的 <c>leaderContactId</c>；
    /// 若讀取端接受它，描述元件重新發布後的值便能回寫為清理基準，破壞原始擁有者證明並可能
    /// 讓下一次清理使用錯誤的跨階段身分。決定性斷言是拒絕整份本機帳本。
    /// </summary>
    [Fact]
    public void Read_rejects_a_fresh_leader_reused_as_the_immutable_original_baseline()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            WriteFinalLedgerDocument(path, owner, schemaVersion: 2, LeaderContactId);

            Action action = () => P72FreshSliceCFixtureFileLedger.Read(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            action.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證帳本解析器維持固定欄位集合，避免未知欄位在不同工具版本間形成未審核的復原語意。
    /// 此測試加入單一額外欄位但不改變任何已核准值；讀取端必須於清理前拒絕它，確保本機檔案
    /// 只承載已經過隔離與生命週期審查的最小資料集。
    /// </summary>
    [Fact]
    public void Read_rejects_a_document_with_an_unexpected_schema_property()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            WriteFinalLedgerDocument(
                path,
                owner,
                schemaVersion: 2,
                OriginalTargetLeaderContactId,
                includeUnexpectedProperty: true);

            Action action = () => P72FreshSliceCFixtureFileLedger.Read(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            action.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 保護路徑信任邊界：ledger 不得寫到指定 owned root 以外，也不得藉由 caller-provided path
    /// 逃出 current-user control plane。拒絕必須發生在任何檔案建立前；測試最後確認 target 與
    /// root 都沒有殘留，避免失敗路徑造成資料或 handle leakage。
    /// </summary>
    [Fact]
    public void Rejects_a_ledger_path_outside_the_owned_root_before_writing()
    {
        var root = CreateOwnedTemporaryRoot();
        var outsideRoot = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(outsideRoot, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;

            Action action = () => _ = new P72FreshSliceCFixtureFileLedger(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            action.Should().Throw<ArgumentException>();
            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            RemoveOwnedTemporaryRoot(outsideRoot);
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 建立本測試唯一擁有的 temporary root；root 不會交給 production runner 或任何 child process。
    /// </summary>
    private static string CreateOwnedTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "speechmessage-p7-2-ledger-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// 以測試擁有的暫存根目錄建立一份結構完整但可精準注入故障的最終帳本。
    /// 這個輔助程式不會接觸 CRM、共用快取或使用者憑證；檔案僅在呼叫端 <c>try/finally</c>
    /// 擁有的唯一暫存目錄中存活，並以 UTF-8 無 BOM 及 CRLF 終止，讓讀取測試能只驗證
    /// schema/provenance 的失敗關閉邊界。呼叫端的 <see cref="RemoveOwnedTemporaryRoot"/> 是
    /// 唯一釋放路徑，因此測試之間不會保留或交叉使用本機復原狀態。
    /// </summary>
    /// <param name="path">測試專屬且位於已擁有暫存根目錄內的固定帳本路徑。</param>
    /// <param name="owner">目前 Windows 身分；它必須與讀取端的所有權驗證完全相符。</param>
    /// <param name="schemaVersion">要注入的帳本結構版本。</param>
    /// <param name="originalTargetLeaderContactId">要注入的不可變原始領隊基準。</param>
    /// <param name="includeOriginalTargetLeaderContactId">是否寫入必要的原始領隊欄位。</param>
    /// <param name="includeUnexpectedProperty">是否額外寫入未經核准的 schema 欄位。</param>
    private static void WriteFinalLedgerDocument(
        string path,
        string owner,
        int schemaVersion,
        Guid originalTargetLeaderContactId,
        bool includeOriginalTargetLeaderContactId = true,
        bool includeUnexpectedProperty = false)
    {
        var properties = new Dictionary<string, object?>
        {
            ["schemaVersion"] = schemaVersion,
            ["fixtureId"] = "p7.2-slice-c-fresh-fixture",
            ["profileAlias"] = "crm91",
            ["ceVersion"] = "9.1",
            ["connector"] = "Data8",
            ["ownerIdentity"] = owner,
            ["stage"] = "fresh-graph-proven",
            ["nonce"] = Nonce,
            ["sourceContactId"] = SourceContactId,
            ["leaderContactId"] = LeaderContactId,
            ["relationshipListId"] = RelationshipListId
        };
        if (includeOriginalTargetLeaderContactId)
        {
            properties.Add("originalTargetLeaderContactId", originalTargetLeaderContactId);
        }

        if (includeUnexpectedProperty)
        {
            properties.Add("unexpected", "must-be-rejected");
        }

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(properties) + "\r\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
    }

    /// <summary>
    /// 只刪除本測試以固定 prefix 建立的 temporary root，並在測試結束 best-effort 清除所有檔案。
    /// </summary>
    private static void RemoveOwnedTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

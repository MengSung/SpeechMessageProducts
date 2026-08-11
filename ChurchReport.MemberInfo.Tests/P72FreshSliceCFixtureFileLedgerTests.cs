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
    /// 驗證第二個獨立 FileLedger writer 無法把同一 current-user 路徑上的既有帳本，改寫成不同的
    /// 原始 target-leader baseline。故障注入先由第一個 writer 原子寫入完整的
    /// <c>preflight-proven</c> snapshot，再讓第二個 writer 以相同 binding 與 nonce、但不同 baseline
    /// 嘗試寫入下一個 stage。決定性斷言是第二次寫入 fail closed，且檔案位元組仍逐一等於第一個 writer
    /// 的內容；這可阻止不同 process、profile 或 Windows session 把既有 recovery provenance 覆寫掉。
    /// 測試資料只存在於 finally 移除的專屬 temporary root，不會保留任何 CRM、credential 或跨測試狀態。
    /// </summary>
    [Fact]
    public void Persist_rejects_a_changed_baseline_from_a_new_file_backed_writer_and_preserves_prior_bytes()
    {
        var root = CreateOwnedTemporaryRoot();
        byte[] priorBytes = [];
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            using (var firstWriter = new P72FreshSliceCFixtureFileLedger(
                       path,
                       root,
                       owner,
                       "crm91",
                       "sunnyvalechback",
                       "9.1",
                       "Data8"))
            {
                firstWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                    "preflight-proven",
                    Nonce,
                    null,
                    null,
                    null,
                    OriginalTargetLeaderContactId));
                priorBytes = File.ReadAllBytes(path);
            }

            using var secondWriter = new P72FreshSliceCFixtureFileLedger(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");
            var changedBaselineLeader = Guid.Parse("eeeeeeee-5555-5555-5555-555555555555");

            Action action = () => secondWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                "source-contact-created",
                Nonce,
                SourceContactId,
                null,
                null,
                changedBaselineLeader));

            action.Should().Throw<InvalidOperationException>();

            var retainedBytes = File.ReadAllBytes(path);
            try
            {
                retainedBytes.Should().Equal(priorBytes);
            }
            finally
            {
                Array.Clear(retainedBytes, 0, retainedBytes.Length);
            }
        }
        finally
        {
            Array.Clear(priorBytes, 0, priorBytes.Length);
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證第二個 process writer 即使持有相同 binding、nonce 與原始 baseline，也不能在前一個
    /// <c>source-contact-created</c> snapshot 已 materialize source ID 後改寫該 ID。故障注入先
    /// 發布來源 contact，再讓新 writer 以合法的下一個 leader stage 攜帶不同 source ID；決定性斷言是
    /// 寫入在 <see cref="File.Replace(string, string, string?, bool)"/> 前 fail closed，原帳本位元組完全不變。
    /// 此測試不建立 Data8 runtime、不使用 credential 或 CE，temporary root 僅屬於本測試並於 finally 移除，
    /// 因此不會將 recovery state、ID 或 session 資料留給另一個使用者、profile 或測試。
    /// </summary>
    [Fact]
    public void Persist_rejects_a_new_writer_that_changes_an_existing_source_id_and_preserves_prior_bytes()
    {
        var root = CreateOwnedTemporaryRoot();
        byte[] priorBytes = [];
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            using (var firstWriter = new P72FreshSliceCFixtureFileLedger(
                       path,
                       root,
                       owner,
                       "crm91",
                       "sunnyvalechback",
                       "9.1",
                       "Data8"))
            {
                firstWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                    "source-contact-created",
                    Nonce,
                    SourceContactId,
                    null,
                    null,
                    OriginalTargetLeaderContactId));
                priorBytes = File.ReadAllBytes(path);
            }

            using var secondWriter = new P72FreshSliceCFixtureFileLedger(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");
            Action action = () => secondWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                "leader-contact-created",
                Nonce,
                Guid.Parse("f1f1f1f1-1111-1111-1111-111111111111"),
                LeaderContactId,
                null,
                OriginalTargetLeaderContactId));

            action.Should().Throw<InvalidOperationException>();

            var retainedBytes = File.ReadAllBytes(path);
            try
            {
                retainedBytes.Should().Equal(priorBytes);
            }
            finally
            {
                Array.Clear(retainedBytes, 0, retainedBytes.Length);
            }
        }
        finally
        {
            Array.Clear(priorBytes, 0, priorBytes.Length);
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證已 materialize 的 fresh leader ID 是跨程序 recovery provenance 的不可變部分。故障注入先
    /// 發布 <c>leader-contact-created</c>，再讓新 writer 嘗試在唯一合法的 relationship-list 下一階段
    /// 換成不同 leader；決定性斷言是替換失敗且舊檔每一個位元組均被保留，不能把 cleanup 日後使用的
    /// exact ID 指向另一個 contact。測試只操作本機 temporary file，finally 清除 byte buffer 與目錄，
    /// 不會保留可跨 Windows session、使用者或 connector profile 重用的 mutable state。
    /// </summary>
    [Fact]
    public void Persist_rejects_a_new_writer_that_changes_an_existing_leader_id_and_preserves_prior_bytes()
    {
        var root = CreateOwnedTemporaryRoot();
        byte[] priorBytes = [];
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            using (var firstWriter = new P72FreshSliceCFixtureFileLedger(
                       path,
                       root,
                       owner,
                       "crm91",
                       "sunnyvalechback",
                       "9.1",
                       "Data8"))
            {
                firstWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                    "leader-contact-created",
                    Nonce,
                    SourceContactId,
                    LeaderContactId,
                    null,
                    OriginalTargetLeaderContactId));
                priorBytes = File.ReadAllBytes(path);
            }

            using var secondWriter = new P72FreshSliceCFixtureFileLedger(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");
            Action action = () => secondWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                "relationship-list-created",
                Nonce,
                SourceContactId,
                Guid.Parse("f2f2f2f2-2222-2222-2222-222222222222"),
                RelationshipListId,
                OriginalTargetLeaderContactId));

            action.Should().Throw<InvalidOperationException>();

            var retainedBytes = File.ReadAllBytes(path);
            try
            {
                retainedBytes.Should().Equal(priorBytes);
            }
            finally
            {
                Array.Clear(retainedBytes, 0, retainedBytes.Length);
            }
        }
        finally
        {
            Array.Clear(priorBytes, 0, priorBytes.Length);
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證 relationship-list ID 一旦經 exact read-back 寫入帳本，就不能被第二個 writer 以同一個
    /// lifecycle 的下一 stage 換成另一個 list。故障注入先發布 relationship-list state，再嘗試寫入
    /// <c>remove-membership-added</c> 並改變 list ID；決定性斷言是原子替換被拒絕且既有檔案位元組不變。
    /// 此 contract 防止不同 process 將 cleanup 的 exact-delete 對象導向未證明的 CRM row；本測試完全離線，
    /// 並在 finally 釋放 temporary directory 與所有讀取 byte buffer。
    /// </summary>
    [Fact]
    public void Persist_rejects_a_new_writer_that_changes_an_existing_relationship_list_id_and_preserves_prior_bytes()
    {
        var root = CreateOwnedTemporaryRoot();
        byte[] priorBytes = [];
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            using (var firstWriter = new P72FreshSliceCFixtureFileLedger(
                       path,
                       root,
                       owner,
                       "crm91",
                       "sunnyvalechback",
                       "9.1",
                       "Data8"))
            {
                firstWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                    "relationship-list-created",
                    Nonce,
                    SourceContactId,
                    LeaderContactId,
                    RelationshipListId,
                    OriginalTargetLeaderContactId));
                priorBytes = File.ReadAllBytes(path);
            }

            using var secondWriter = new P72FreshSliceCFixtureFileLedger(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");
            Action action = () => secondWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                "remove-membership-added",
                Nonce,
                SourceContactId,
                LeaderContactId,
                Guid.Parse("f3f3f3f3-3333-3333-3333-333333333333"),
                OriginalTargetLeaderContactId));

            action.Should().Throw<InvalidOperationException>();

            var retainedBytes = File.ReadAllBytes(path);
            try
            {
                retainedBytes.Should().Equal(priorBytes);
            }
            finally
            {
                Array.Clear(retainedBytes, 0, retainedBytes.Length);
            }
        }
        finally
        {
            Array.Clear(priorBytes, 0, priorBytes.Length);
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證相同 binding 的新 process 不得把 ledger stage 倒退。故障注入先發布
    /// <c>remove-membership-added</c>，再讓另一個 writer 提交 ID 相同但較早的
    /// <c>relationship-list-created</c>；決定性斷言是狀態倒退在檔案取代前遭拒，舊位元組完全保留。
    /// 這避免 cleanup/reconciliation 依賴被回捲的 local recovery history 而重複或錯序 remote mutation；
    /// 測試沒有 CRM I/O，並由 finally 負責刪除其唯一 temporary root。
    /// </summary>
    [Fact]
    public void Persist_rejects_a_new_writer_that_regresses_an_existing_stage_and_preserves_prior_bytes()
    {
        var root = CreateOwnedTemporaryRoot();
        byte[] priorBytes = [];
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            using (var firstWriter = new P72FreshSliceCFixtureFileLedger(
                       path,
                       root,
                       owner,
                       "crm91",
                       "sunnyvalechback",
                       "9.1",
                       "Data8"))
            {
                firstWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                    "remove-membership-added",
                    Nonce,
                    SourceContactId,
                    LeaderContactId,
                    RelationshipListId,
                    OriginalTargetLeaderContactId));
                priorBytes = File.ReadAllBytes(path);
            }

            using var secondWriter = new P72FreshSliceCFixtureFileLedger(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");
            Action action = () => secondWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                "relationship-list-created",
                Nonce,
                SourceContactId,
                LeaderContactId,
                RelationshipListId,
                OriginalTargetLeaderContactId));

            action.Should().Throw<InvalidOperationException>();

            var retainedBytes = File.ReadAllBytes(path);
            try
            {
                retainedBytes.Should().Equal(priorBytes);
            }
            finally
            {
                Array.Clear(retainedBytes, 0, retainedBytes.Length);
            }
        }
        finally
        {
            Array.Clear(priorBytes, 0, priorBytes.Length);
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證 cleanup 可由另一個 process 只從完整的 <c>fresh-graph-proven</c> ledger 接續，並以完全
    /// 相同的三個 recovery IDs 發布唯一合法的 <c>cleanup-preflight-proven</c> 下一階段。決定性斷言是
    /// 第二個 writer 成功後帳本 stage 前進、ID 與 nonce/baseline 均不變；這證明單調轉移不會阻斷正確的
    /// cross-process cleanup。測試不接觸 CE，所有 temporary file 由 finally 的單一 owner 清除，避免跨測試
    /// 或跨使用者保留 recovery state。
    /// </summary>
    [Fact]
    public void Persist_allows_a_new_writer_to_transition_from_fresh_graph_to_cleanup_preflight()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            using (var firstWriter = new P72FreshSliceCFixtureFileLedger(
                       path,
                       root,
                       owner,
                       "crm91",
                       "sunnyvalechback",
                       "9.1",
                       "Data8"))
            {
                firstWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                    "fresh-graph-proven",
                    Nonce,
                    SourceContactId,
                    LeaderContactId,
                    RelationshipListId,
                    OriginalTargetLeaderContactId));
            }

            using (var secondWriter = new P72FreshSliceCFixtureFileLedger(
                       path,
                       root,
                       owner,
                       "crm91",
                       "sunnyvalechback",
                       "9.1",
                       "Data8"))
            {
                secondWriter.Persist(new P72FreshSliceCFixtureLedgerState(
                    "cleanup-preflight-proven",
                    Nonce,
                    SourceContactId,
                    LeaderContactId,
                    RelationshipListId,
                    OriginalTargetLeaderContactId));
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            document.RootElement.GetProperty("stage").GetString().Should().Be("cleanup-preflight-proven");
            document.RootElement.GetProperty("nonce").GetGuid().Should().Be(Nonce);
            document.RootElement.GetProperty("sourceContactId").GetGuid().Should().Be(SourceContactId);
            document.RootElement.GetProperty("leaderContactId").GetGuid().Should().Be(LeaderContactId);
            document.RootElement.GetProperty("relationshipListId").GetGuid().Should().Be(RelationshipListId);
            document.RootElement.GetProperty("originalTargetLeaderContactId").GetGuid().Should().Be(OriginalTargetLeaderContactId);
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
    /// 驗證帳本 parser 拒絕內嵌的單獨 CR 字元，即使檔案最後仍有合法 final CRLF。
    /// JSON 規範允許 CR 作為空白，因此單純確認每個 LF 前有 CR 並不足以證明整份檔案是 CRLF-only；
    /// 若接受這種內容，另一個 process 便可能把非 repository 規格的控制平面文件當成可清理的 recovery
    /// state。故障注入只在開頭大括號後加入一個 standalone CR，保留所有 binding、ID 與 final CRLF，
    /// 使斷言精確保護行結尾 trust boundary，而非依賴 schema 或 identity 驗證失敗。
    /// </summary>
    [Fact]
    public void Read_rejects_a_document_with_an_embedded_standalone_carriage_return()
    {
        var root = CreateOwnedTemporaryRoot();
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;
            WriteFinalLedgerDocument(path, owner, schemaVersion: 2, OriginalTargetLeaderContactId);
            var canonicalText = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
            File.WriteAllText(
                path,
                canonicalText.Replace("{", "{\r", StringComparison.Ordinal),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));

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
    /// 驗證 FileLedger 僅接受 parent 預先建立的 current-user root，不能把 child 收到的非空但不存在路徑
    /// 當成可自行建立的控制面目錄。故障注入使用尚未存在的 temporary 路徑；決定性斷言是 constructor
    /// 在任何帳本 I/O 前拒絕該輸入，且該目錄仍不存在。這讓 parent 保有 temporary root 的唯一生命週期
    /// 與 finally 清理責任，避免 child 將另一個 invocation、profile 或使用者的路徑變成可保留的 recovery
    /// state。finally 僅移除本測試已知的唯一 temporary path，避免資源殘留。
    /// </summary>
    [Fact]
    public void Constructor_rejects_a_missing_parent_owned_root_without_creating_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "speechmessage-p7-2-ledger-missing-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;

            Action action = () => _ = new P72FreshSliceCFixtureFileLedger(
                path,
                root,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            action.Should().Throw<InvalidOperationException>();
            Directory.Exists(root).Should().BeFalse();
        }
        finally
        {
            RemoveOwnedTemporaryRoot(root);
        }
    }

    /// <summary>
    /// 驗證 ledger constructor 不只檢查 root leaf，也會拒絕 lexical path 中的既有 reparse-point ancestor。
    /// 故障注入在本測試專屬 temporary root 內建立一個指向另一個同測試目錄的 directory symbolic link，並將
    /// 其正常子目錄作為 parent-owned root；決定性斷言是 link 本身帶有 reparse attribute、leaf 不帶該
    /// attribute，仍在任何 ledger I/O 前遭拒。Windows 若因本機 symbolic-link 權限或 policy 不允許建立
    /// 測試 link，會明確標示 skip 而不將環境限制誤判為產品行為；實作仍必須保守地檢查祖先。finally 只
    /// 清除本測試建立的 temporary tree，因此不會觸及外部檔案、session、profile 或 recovery state。
    /// </summary>
    [Fact(Skip = "Requires SeCreateSymbolicLinkPrivilege; the current Windows test policy returns ERROR_PRIVILEGE_NOT_HELD for directory symbolic links.")]
    public void Constructor_rejects_a_parent_owned_root_with_a_reparse_point_ancestor()
    {
        var testRoot = CreateOwnedTemporaryRoot();
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
                    "Windows denied directory symbolic-link creation required to prove the ancestor reparse guard: " +
                    exception.GetType().Name);
            }
            catch (PlatformNotSupportedException exception)
            {
                throw Xunit.Sdk.SkipException.ForSkip(
                    "This platform does not support the directory symbolic-link test required to prove the ancestor reparse guard: " +
                    exception.GetType().Name);
            }
            catch (IOException exception) when (exception.HResult == unchecked((int)0x80070522))
            {
                throw Xunit.Sdk.SkipException.ForSkip(
                    "Windows policy denied the directory symbolic-link privilege required to prove the ancestor reparse guard: " +
                    exception.GetType().Name);
            }

            var rootViaReparseAncestor = Path.Combine(reparseAncestor, "owned-root");
            (File.GetAttributes(reparseAncestor) & FileAttributes.ReparsePoint).Should().NotBe(0);
            (File.GetAttributes(rootViaReparseAncestor) & FileAttributes.ReparsePoint).Should().Be(0);
            var path = Path.Combine(rootViaReparseAncestor, "fresh-slice-c-ledger.json");
            var owner = WindowsIdentity.GetCurrent().Name;

            Action action = () => _ = new P72FreshSliceCFixtureFileLedger(
                path,
                rootViaReparseAncestor,
                owner,
                "crm91",
                "sunnyvalechback",
                "9.1",
                "Data8");

            action.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            RemoveOwnedTemporaryRoot(testRoot);
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

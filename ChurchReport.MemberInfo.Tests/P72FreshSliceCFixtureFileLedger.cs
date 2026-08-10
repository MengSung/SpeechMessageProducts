// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureFileLedger.cs
// 用途：P7.2 Slice C fresh-fixture child 的 current-user local pending ledger。
//
// 這是 test-fixture control plane，而非產品 API。它只接受 parent 已驗證的本機 owned root、
// 固定檔名與固定 crm91/Data8/CE 9.1 binding；所有 CRM ID、nonce 與 stage 都只寫到同一位
// Windows operator 的 local recovery file，絕不輸出到 evidence、log、TRX、console 或 repository。
// ============================================================================

using System.Text;
using System.Text.Json;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 將 provisioner 每個已 read-back 的 fresh-fixture stage 原子持久化到 current-user local ledger。
/// 此類別不保存 CRM service、credential、token、Entity、WCF channel 或背景工作；唯一可保留的
/// 狀態是本次 invocation 的 nonce 與固定本機檔案路徑。每次 <see cref="Persist"/> 以同一個
/// root 內的 create-new temporary file 寫入、flush，然後原子取代目標，避免半寫入 JSON 被另一個
/// process、profile 或 session 誤當成有效 recovery state。<see cref="Dispose"/> 不保有開啟的
/// handle；它只封閉後續寫入，讓呼叫端在 child 結束前不會意外重用 instance。
/// </summary>
internal sealed class P72FreshSliceCFixtureFileLedger : IP72FreshSliceCFixtureLedger, IDisposable
{
    private const string LedgerFileName = "fresh-slice-c-ledger.json";
    /// <summary>
    /// 定義含有不可變原始領隊基準的唯一帳本結構版本。版本一缺少此跨階段 provenance 邊界，
    /// 因此不得在清理流程中相容讀取或推測升級，避免重新發布的 descriptor 覆寫已證明的基準。
    /// </summary>
    private const int CurrentSchemaVersion = 2;
    private const string FixtureId = "p7.2-slice-c-fresh-fixture";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] AllowedStages =
    [
        "preflight-proven",
        "source-contact-created",
        "leader-contact-created",
        "relationship-list-created",
        "remove-membership-added",
        "transfer-source-membership-added",
        "baseline-owner-assigned",
        "fresh-graph-proven",
        "cleanup-preflight-proven",
        "cleanup-transfer-source-membership-removed",
        "cleanup-remove-membership-removed",
        "cleanup-relationship-list-deleted",
        "cleanup-source-contact-deleted",
        "cleanup-leader-contact-deleted"
    ];

    private readonly string _path;
    private readonly string _ownedRoot;
    private readonly string _ownerIdentity;
    private readonly string _profileAlias;
    private readonly string _ceVersion;
    private readonly string _connector;
    private Guid? _nonce;
    // 同一個 ledger instance 代表一次 fresh-fixture invocation；原始 baseline leader 是 cleanup
    // 唯一允許用來還原 owner 的跨 stage 不可變資料。即使呼叫端意外重用 nonce，writer 也不能
    // 讓後續 stage 用另一筆 leader 覆寫這個值，否則 child 間的 recovery state 可能跨 profile
    // 或跨 fixture 指向錯誤擁有者。此欄位僅存放當前 invocation 的 GUID，Dispose 後 instance
    // 不可再使用，且不會進入 static/cache/log/response，因此不存在跨 session 的保留路徑。
    private Guid? _originalTargetLeaderContactId;
    private bool _disposed;

    /// <summary>
    /// 建立一個只屬於 parent 指定 current-user root 的 ledger writer。root 與 filename 都在建構時
    /// 精確驗證，故 C# child 無法將 recovery state 導向任意磁碟位置、repository 或別人的 profile。
    /// 此建構子不建立 CRM client、不開啟檔案，也不讀取舊 ledger；真正的檔案 I/O 僅在已完成 remote
    /// read-back 後由 <see cref="Persist"/> 執行。
    /// </summary>
    /// <param name="path">parent 產生的固定 <c>fresh-slice-c-ledger.json</c> 路徑。</param>
    /// <param name="ownedRoot">current-user local control plane 的直接父目錄。</param>
    /// <param name="ownerIdentity">已由 parent 與 child 驗證相同的 Windows identity。</param>
    /// <param name="profileAlias">固定 deployment profile alias <c>crm91</c>。</param>
    /// <param name="expectedProfileAlias">固定 product alias <c>sunnyvalechback</c>。</param>
    /// <param name="ceVersion">固定 CE version <c>9.1</c>。</param>
    /// <param name="connector">固定 connector <c>Data8</c>。</param>
    /// <exception cref="ArgumentException">路徑、root、owner 或固定 deployment binding 不合法時擲出。</exception>
    internal P72FreshSliceCFixtureFileLedger(
        string path,
        string ownedRoot,
        string ownerIdentity,
        string profileAlias,
        string expectedProfileAlias,
        string ceVersion,
        string connector)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(ownedRoot) ||
            string.IsNullOrWhiteSpace(ownerIdentity) ||
            !string.Equals(profileAlias, "crm91", StringComparison.Ordinal) ||
            !string.Equals(expectedProfileAlias, "sunnyvalechback", StringComparison.Ordinal) ||
            !string.Equals(ceVersion, "9.1", StringComparison.Ordinal) ||
            !string.Equals(connector, "Data8", StringComparison.Ordinal))
        {
            throw new ArgumentException("The fresh-fixture ledger binding is invalid.");
        }

        _ownedRoot = Path.GetFullPath(ownedRoot);
        _path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(_path), _ownedRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(_path), LedgerFileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The fresh-fixture ledger path is outside its owned root.", nameof(path));
        }

        EnsureOwnedRoot();
        RejectReparsePoint(_path);
        _ownerIdentity = ownerIdentity;
        _profileAlias = profileAlias;
        _ceVersion = ceVersion;
        _connector = connector;
    }

    /// <summary>
    /// 以 fixed schema 將一次已確認的 stage 寫入 ledger。stage 只能由 provisioner private allowlist
    /// 產生，nonce 在同一 writer 內不可更換；若 writing、flush、replace 或 temporary cleanup 任一處
    /// 失敗，例外會交回 provisioner，後者必須回報 non-retryable ambiguous result 並停止後續 CRM
    /// mutation。此方法不記錄原始例外，亦不保留 byte buffer、stream 或 temporary path 到下一次呼叫。
    /// </summary>
    /// <param name="state">剛完成 exact read-back 的 immutable stage snapshot。</param>
    public void Persist(P72FreshSliceCFixtureLedgerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ThrowIfDisposed();
        ValidateState(state);

        _nonce ??= state.Nonce;
        if (_nonce != state.Nonce)
        {
            throw new InvalidOperationException("The fresh-fixture ledger nonce changed during one invocation.");
        }

        _originalTargetLeaderContactId ??= state.OriginalTargetLeaderContactId;
        if (_originalTargetLeaderContactId != state.OriginalTargetLeaderContactId)
        {
            throw new InvalidOperationException("The fresh-fixture ledger baseline leader changed during one invocation.");
        }

        var document = new FreshFixtureLedgerDocument(
            SchemaVersion: CurrentSchemaVersion,
            FixtureId,
            _profileAlias,
            _ceVersion,
            _connector,
            _ownerIdentity,
            state.Stage,
            state.Nonce,
            state.SourceContactId,
            state.LeaderContactId,
            state.RelationshipListId,
            state.OriginalTargetLeaderContactId);
        var json = JsonSerializer.Serialize(document, JsonOptions) + "\r\n";
        var bytes = Utf8WithoutBom.GetBytes(json);
        var temporaryPath = Path.Combine(_ownedRoot, ".fresh-slice-c-ledger.tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            EnsureOwnedRoot();
            RejectReparsePoint(_path);
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

            if (File.Exists(_path))
            {
                RejectReparsePoint(_path);
                File.Replace(temporaryPath, _path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _path);
            }
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
    /// 讀取 cleanup child 所需的 final fresh graph ledger。reader 只接受固定檔名、同一個
    /// current-user owned root、crm91/Data8/CE 9.1 binding、完整 top-level schema 與
    /// <c>fresh-graph-proven</c> stage；它不會建立資料夾、不會修復 ledger，也不會猜測缺失 ID。
    /// 任一不確定性都以 sanitized local exception fail closed，呼叫端必須保留 ledger 供人工
    /// recovery，而不是重試或刪除未知 CRM row。
    /// </summary>
    /// <param name="path">固定 <c>fresh-slice-c-ledger.json</c> 的 local path。</param>
    /// <param name="ownedRoot">同一個 current-user local recovery root。</param>
    /// <param name="ownerIdentity">parent 驗證過的 Windows identity。</param>
    /// <param name="profileAlias">固定 deployment profile <c>crm91</c>。</param>
    /// <param name="expectedProfileAlias">固定 product profile <c>sunnyvalechback</c>。</param>
    /// <param name="ceVersion">固定 CE version <c>9.1</c>。</param>
    /// <param name="connector">固定 connector <c>Data8</c>。</param>
    /// <returns>可供 cleanup 使用、含完整 fresh graph IDs 的 immutable ledger state。</returns>
    internal static P72FreshSliceCFixtureLedgerState Read(
        string path,
        string ownedRoot,
        string ownerIdentity,
        string profileAlias,
        string expectedProfileAlias,
        string ceVersion,
        string connector)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(ownedRoot) ||
            string.IsNullOrWhiteSpace(ownerIdentity) ||
            !string.Equals(profileAlias, "crm91", StringComparison.Ordinal) ||
            !string.Equals(expectedProfileAlias, "sunnyvalechback", StringComparison.Ordinal) ||
            !string.Equals(ceVersion, "9.1", StringComparison.Ordinal) ||
            !string.Equals(connector, "Data8", StringComparison.Ordinal) ||
            ownerIdentity.Length > 256 ||
            ownerIdentity.IndexOfAny(['\0', '\r', '\n']) >= 0)
        {
            throw new InvalidOperationException("The fresh-fixture ledger binding is invalid.");
        }

        var resolvedRoot = Path.GetFullPath(ownedRoot);
        var resolvedPath = Path.GetFullPath(path);
        if (!Directory.Exists(resolvedRoot) ||
            !File.Exists(resolvedPath) ||
            !string.Equals(Path.GetDirectoryName(resolvedPath), resolvedRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(resolvedPath), LedgerFileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The fresh-fixture ledger path is invalid.");
        }

        try
        {
            RejectReparsePoint(resolvedRoot);
            RejectReparsePoint(resolvedPath);
            var bytes = File.ReadAllBytes(resolvedPath);
            try
            {
                if (bytes.Length == 0 ||
                    bytes.Length > 32 * 1024 ||
                    (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF))
                {
                    throw new InvalidOperationException("The fresh-fixture ledger encoding is invalid.");
                }

                var text = Utf8WithoutBom.GetString(bytes);
                if (text.IndexOf('\n') >= 0 && text.IndexOf("\r\n", StringComparison.Ordinal) < 0 ||
                    text.Contains("\n", StringComparison.Ordinal) && text.Contains("\n", StringComparison.Ordinal) &&
                    System.Text.RegularExpressions.Regex.IsMatch(text, "(?<!\\r)\\n") ||
                    !text.EndsWith("\r\n", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The fresh-fixture ledger line endings are invalid.");
                }

                using var document = JsonDocument.Parse(text);
                var expectedNames = new[]
                {
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
                };
                var actualNames = document.RootElement.EnumerateObject().Select(static property => property.Name).ToArray();
                if (actualNames.Length != expectedNames.Length ||
                    actualNames.Any(name => Array.IndexOf(expectedNames, name) < 0) ||
                    expectedNames.Any(name => Array.IndexOf(actualNames, name) < 0))
                {
                    throw new InvalidOperationException("The fresh-fixture ledger schema is invalid.");
                }

                var ledger = JsonSerializer.Deserialize<FreshFixtureLedgerDocument>(text, JsonOptions);
                if (ledger is null ||
                    ledger.SchemaVersion != CurrentSchemaVersion ||
                    !string.Equals(ledger.FixtureId, FixtureId, StringComparison.Ordinal) ||
                    !string.Equals(ledger.ProfileAlias, profileAlias, StringComparison.Ordinal) ||
                    !string.Equals(ledger.CeVersion, ceVersion, StringComparison.Ordinal) ||
                    !string.Equals(ledger.Connector, connector, StringComparison.Ordinal) ||
                    !string.Equals(ledger.OwnerIdentity, ownerIdentity, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(ledger.Stage, "fresh-graph-proven", StringComparison.Ordinal) ||
                    ledger.Nonce == Guid.Empty ||
                    ledger.SourceContactId is not Guid sourceId || sourceId == Guid.Empty ||
                    ledger.LeaderContactId is not Guid leaderId || leaderId == Guid.Empty ||
                    ledger.RelationshipListId is not Guid relationshipId || relationshipId == Guid.Empty ||
                    ledger.OriginalTargetLeaderContactId == Guid.Empty ||
                    ledger.OriginalTargetLeaderContactId == leaderId)
                {
                    throw new InvalidOperationException("The fresh-fixture ledger contents are invalid.");
                }

                return new P72FreshSliceCFixtureLedgerState(
                    ledger.Stage,
                    ledger.Nonce,
                    ledger.SourceContactId,
                    ledger.LeaderContactId,
                    ledger.RelationshipListId,
                    ledger.OriginalTargetLeaderContactId);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("The fresh-fixture ledger could not be read.");
        }
    }

    /// <summary>
    /// 封閉 writer 的使用權。沒有常駐 stream 或 CRM resource 需要釋放；此旗標只防止 child 的後續
    /// catch/finally path 在已決定結果後重寫 ledger，避免跨 stage 或跨 session 的 mutable-state reuse。
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }

    /// <summary>
    /// 確認 stage、nonce 與每個階段允許的 exact-ID shape。這些檢查不將 null/empty ID 自動補成值，
    /// 因為不完整 ledger 不能成為 cleanup 或 descriptor publication 的權限來源。
    /// </summary>
    /// <param name="state">待持久化的 immutable stage snapshot。</param>
    private static void ValidateState(P72FreshSliceCFixtureLedgerState state)
    {
        if (state.Nonce == Guid.Empty ||
            state.OriginalTargetLeaderContactId == Guid.Empty ||
            Array.IndexOf(AllowedStages, state.Stage) < 0)
        {
            throw new ArgumentException("The fresh-fixture ledger state is invalid.", nameof(state));
        }

        var hasSource = state.SourceContactId is Guid sourceId && sourceId != Guid.Empty;
        var hasLeader = state.LeaderContactId is Guid leaderId && leaderId != Guid.Empty;
        var hasRelationship = state.RelationshipListId is Guid relationshipId && relationshipId != Guid.Empty;
        if (state.LeaderContactId is Guid existingLeaderId &&
            existingLeaderId != Guid.Empty &&
            state.OriginalTargetLeaderContactId == existingLeaderId)
        {
            // 原始領隊是 provision preflight 證明的外部基準；一旦等於本次建立的 fresh leader，
            // 代表 descriptor 或呼叫端嘗試把已發布圖形回寫為清理授權，必須在任何本機持久化前失敗關閉。
            throw new ArgumentException("The fresh-fixture ledger baseline leader is invalid.", nameof(state));
        }

        var expectedShape = state.Stage switch
        {
            "preflight-proven" => !hasSource && !hasLeader && !hasRelationship,
            "source-contact-created" => hasSource && !hasLeader && !hasRelationship,
            "leader-contact-created" => hasSource && hasLeader && !hasRelationship,
            "relationship-list-created" or
            "remove-membership-added" or
            "transfer-source-membership-added" or
            "baseline-owner-assigned" or
            "fresh-graph-proven" or
            "cleanup-preflight-proven" or
            "cleanup-transfer-source-membership-removed" or
            "cleanup-remove-membership-removed" or
            "cleanup-relationship-list-deleted" or
            "cleanup-source-contact-deleted" or
            "cleanup-leader-contact-deleted" => hasSource && hasLeader && hasRelationship,
            _ => false
        };
        if (!expectedShape)
        {
            throw new ArgumentException("The fresh-fixture ledger state shape is invalid.", nameof(state));
        }
    }

    /// <summary>
    /// 建立或重新驗證 parent 指定的 owned root。root 是 single local owner 的範圍，不接受 reparse point；
    /// 這可防止 temporary ledger 寫入或 cleanup 跟隨 junction/symlink 離開 current-user control plane。
    /// </summary>
    private void EnsureOwnedRoot()
    {
        Directory.CreateDirectory(_ownedRoot);
        var root = new DirectoryInfo(_ownedRoot);
        if ((root.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("The fresh-fixture ledger root is invalid.");
        }
    }

    /// <summary>
    /// 拒絕既有 reparse point。不存在的目標可安全由本 writer 建立；存在的 junction、symlink 或其他
    /// reparse point 一律不能作為 ledger 或 temporary file，避免所有權越界與不預期的資料刪改。
    /// </summary>
    /// <param name="path">待檢查的 exact owned path。</param>
    private static void RejectReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("The fresh-fixture ledger path is invalid.");
        }
    }

    /// <summary>
    /// 拒絕 Dispose 後的任何持久化。這是 pure in-process lifecycle guard；不會存取檔案、CRM 或
    /// 外部資源，避免 teardown path 對已完成 invocation 重新引入 mutable recovery state。
    /// </summary>
    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// ledger 的唯一 JSON schema。record 僅含 fixed profile binding 與 recovery IDs；它刻意不含
    /// password、endpoint、OrganizationId、token、cookie、CRM payload、exception 或原 descriptor bytes。
    /// </summary>
    private sealed record FreshFixtureLedgerDocument(
        int SchemaVersion,
        string FixtureId,
        string ProfileAlias,
        string CeVersion,
        string Connector,
        string OwnerIdentity,
        string Stage,
        Guid Nonce,
        Guid? SourceContactId,
        Guid? LeaderContactId,
        Guid? RelationshipListId,
        Guid OriginalTargetLeaderContactId);
}

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
    /// <summary>
    /// 以不可跳號的明確順序定義 fresh-fixture ledger 的唯一生命週期。索引不只是輸入 allowlist：
    /// 已存在帳本的 cross-process replacement 只可重播同一 stage 或前進一格，因此 provision 完成後只能
    /// 進入 cleanup preflight，cleanup 亦不能回退、跳過或重啟 provision。這個 immutable static metadata
    /// 不包含使用者、profile、credential、CRM ID 或 mutable execution state，且不會建立跨 invocation 的
    /// session retention；每次驗證只使用目前磁碟 snapshot 與呼叫端傳入的 state。
    /// </summary>
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
    /// 產生，nonce 在同一 writer 內不可更換；若目標已存在，新的 process writer 必須先嚴格解析既有
    /// 文件，確認固定 binding、nonce 與不可變原始 baseline leader 全部相同，才可原子取代。任何不符
    /// 都會保留舊檔並 fail closed。若 writing、flush、replace 或 temporary cleanup 任一處失敗，例外會
    /// 交回 provisioner，後者必須回報 non-retryable ambiguous result 並停止後續 CRM mutation。此方法
    /// 不記錄原始例外，亦不保留 byte buffer、stream 或 temporary path 到下一次呼叫。
    /// </summary>
    /// <param name="state">剛完成 exact read-back 的 immutable stage snapshot。</param>
    public void Persist(P72FreshSliceCFixtureLedgerState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ThrowIfDisposed();
        ValidateState(state);

        if (_nonce is Guid persistedNonce && persistedNonce != state.Nonce)
        {
            throw new InvalidOperationException("The fresh-fixture ledger nonce changed during one invocation.");
        }

        if (_originalTargetLeaderContactId is Guid persistedBaselineLeader &&
            persistedBaselineLeader != state.OriginalTargetLeaderContactId)
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
                ValidateExistingLedgerForReplacement(state);
                File.Replace(temporaryPath, _path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _path);
            }

            // 只有磁碟上的 atomic publication 成功後才更新 instance snapshot；失敗 writer 不得把
            // 不可變 nonce/baseline 留在可被後續呼叫誤用的記憶體狀態。
            _nonce ??= state.Nonce;
            _originalTargetLeaderContactId ??= state.OriginalTargetLeaderContactId;
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
                var ledger = ParseStrictLedgerDocument(bytes);
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
    /// 在原子取代前讀取既有檔案並驗證它仍屬於同一個 parent-owned binding。這是不同 child process
    /// 共用同一路徑時的 fail-closed provenance gate：既有 JSON 必須符合完整 schema 與 stage/ID shape，
    /// 且 profile、CE、connector、Windows owner、nonce 與原始 baseline leader 都要與目前 snapshot 相同。
    /// 不同 baseline、毀損檔案或另一個 profile 的 state 一律不取代，讓既有 recovery data 保持完整。
    /// 讀入的 byte buffer 在 finally 清除；此方法不快取任何 ID、內容或 exception，因此不會跨 invocation、
    /// session 或使用者保留 mutable state。
    /// </summary>
    /// <param name="state">目前 writer 欲發布、且已通過本地 shape validation 的 immutable snapshot。</param>
    /// <exception cref="InvalidOperationException">既有文件無法嚴格驗證或與目前 binding 不相容時擲出。</exception>
    private void ValidateExistingLedgerForReplacement(P72FreshSliceCFixtureLedgerState state)
    {
        try
        {
            var bytes = File.ReadAllBytes(_path);
            try
            {
                var existingLedger = ParseStrictLedgerDocument(bytes);
                var existingState = new P72FreshSliceCFixtureLedgerState(
                    existingLedger.Stage,
                    existingLedger.Nonce,
                    existingLedger.SourceContactId,
                    existingLedger.LeaderContactId,
                    existingLedger.RelationshipListId,
                    existingLedger.OriginalTargetLeaderContactId);
                try
                {
                    ValidateState(existingState);
                }
                catch (ArgumentException)
                {
                    throw new InvalidOperationException("The existing fresh-fixture ledger contents are invalid.");
                }

                if (existingLedger.SchemaVersion != CurrentSchemaVersion ||
                    !string.Equals(existingLedger.FixtureId, FixtureId, StringComparison.Ordinal) ||
                    !string.Equals(existingLedger.ProfileAlias, _profileAlias, StringComparison.Ordinal) ||
                    !string.Equals(existingLedger.CeVersion, _ceVersion, StringComparison.Ordinal) ||
                    !string.Equals(existingLedger.Connector, _connector, StringComparison.Ordinal) ||
                    !string.Equals(existingLedger.OwnerIdentity, _ownerIdentity, StringComparison.OrdinalIgnoreCase) ||
                    existingLedger.Nonce != state.Nonce ||
                    existingLedger.OriginalTargetLeaderContactId != state.OriginalTargetLeaderContactId)
                {
                    throw new InvalidOperationException("The existing fresh-fixture ledger binding is invalid.");
                }

                // 已 materialize 的 ID 是 cleanup/reconciliation 唯一可用的 exact remote identity。不同
                // process 即使持有相同 nonce 也不能替換它；否則後續 exact delete 可能落到未證明的 CRM row。
                // 尚未 materialize 的 null ID 不在此處猜測或填補，而是交由下一段單調 stage transition
                // 驗證，確保它只能在其指定的生命週期時點第一次出現。
                if ((existingState.SourceContactId is Guid existingSourceContactId &&
                     state.SourceContactId != existingSourceContactId) ||
                    (existingState.LeaderContactId is Guid existingLeaderContactId &&
                     state.LeaderContactId != existingLeaderContactId) ||
                    (existingState.RelationshipListId is Guid existingRelationshipListId &&
                     state.RelationshipListId != existingRelationshipListId))
                {
                    throw new InvalidOperationException("The existing fresh-fixture ledger identities are invalid.");
                }

                ValidateReplacementStageTransition(existingState.Stage, state.Stage);
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
            throw new InvalidOperationException("The existing fresh-fixture ledger could not be read.");
        }
    }

    /// <summary>
    /// 驗證既有檔案與候選 snapshot 的跨程序生命週期轉移。帳本只能重播同一個已確認 stage（例如 child
    /// 在 local publication 後重啟）或前進到其緊鄰的下一個 stage；不能跳過 read-back/mutation 邊界、
    /// 倒退到較早階段，或由 cleanup 回到 provision。由於 cleanup stages 仍保留三個原始 IDs，即使
    /// remote delete 已經完成也不會清空 ledger identity，讓 ambiguous cleanup 後的 exact-ID recovery
    /// 仍可 fail closed。此純函式不配置資源、不做 I/O，也不保留跨 request、user 或 profile 的狀態。
    /// </summary>
    /// <param name="existingStage">由已嚴格解析的 ledger 提供的完成 stage。</param>
    /// <param name="candidateStage">目前 writer 欲以原子檔案取代發布的 stage。</param>
    /// <exception cref="InvalidOperationException">stage 不在唯一單調序列或不是同階／下一階時擲出。</exception>
    private static void ValidateReplacementStageTransition(string existingStage, string candidateStage)
    {
        var existingIndex = Array.IndexOf(AllowedStages, existingStage);
        var candidateIndex = Array.IndexOf(AllowedStages, candidateStage);
        if (existingIndex < 0 ||
            candidateIndex < 0 ||
            candidateIndex != existingIndex &&
            candidateIndex != existingIndex + 1)
        {
            throw new InvalidOperationException("The existing fresh-fixture ledger stage transition is invalid.");
        }
    }

    /// <summary>
    /// 將磁碟位元組解析成唯一允許的 ledger document。此共同 parser 同時供 cleanup read 與跨 process
    /// replacement 使用，確保兩條路徑都拒絕 BOM、超過 32 KiB、LF-only/缺 final CRLF、額外欄位、重複
    /// 欄位或無法反序列化的內容。它只回傳短生命週期 record；呼叫端必須在各自 finally 清除來源 buffer，
    /// 並依其操作目的再驗證 binding 與 stage，不能把 parsed document 當成跨 session cache。
    /// </summary>
    /// <param name="bytes">從 parent-owned exact ledger path 讀取的 bounded UTF-8 bytes。</param>
    /// <returns>符合固定 JSON schema 的未信任本機 ledger document。</returns>
    /// <exception cref="InvalidOperationException">編碼、行結尾、schema 或 JSON 內容不合法時擲出。</exception>
    private static FreshFixtureLedgerDocument ParseStrictLedgerDocument(byte[] bytes)
    {
        if (bytes.Length == 0 ||
            bytes.Length > 32 * 1024 ||
            (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF))
        {
            throw new InvalidOperationException("The fresh-fixture ledger encoding is invalid.");
        }

        var text = Utf8WithoutBom.GetString(bytes);
        if (!HasOnlyCrLfLineEndings(text))
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

        return JsonSerializer.Deserialize<FreshFixtureLedgerDocument>(text, JsonOptions)
            ?? throw new InvalidOperationException("The fresh-fixture ledger contents are invalid.");
    }

    /// <summary>
    /// 驗證 ledger UTF-8 text 僅使用 CRLF 且最後一行完整終止。這避免不同工具產生的半行或 LF-only
    /// 文件被另一個 process 當成可恢復的 state；檢查只掃描最大 32 KiB 的 local document，沒有 I/O、
    /// 配置或跨 invocation 的 retained buffer。
    /// </summary>
    /// <param name="text">已由嚴格 UTF-8 decoder 取得的 bounded ledger text。</param>
    /// <returns>每個 CR 與 LF 都恰好組成 CRLF，且文字以 final CRLF 結尾時為 <see langword="true"/>。</returns>
    private static bool HasOnlyCrLfLineEndings(string text)
    {
        if (!text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 0; index < text.Length; index++)
        {
            if ((text[index] == '\n' && (index == 0 || text[index - 1] != '\r')) ||
                (text[index] == '\r' && (index + 1 >= text.Length || text[index + 1] != '\n')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 重新驗證 parent 已建立的 owned root；child 永遠不得建立缺失 root。root 是 single local owner 的
    /// 範圍，不接受 reparse point，這可防止 temporary ledger 寫入或 cleanup 跟隨 junction/symlink 離開
    /// current-user control plane，也讓 parent 成為 temporary directory 建立與 finally 清理的唯一 owner。
    /// </summary>
    private void EnsureOwnedRoot()
    {
        // parent 只把既有 root 交給 child，但 root leaf 正常不足以證明實際磁碟路徑安全：任一 lexical
        // ancestor 若是 junction/symlink，之後的 temporary ledger 或 cleanup 就可能離開 invocation
        // 的 current-user control plane。共用 guard 逐層驗證並在失敗時不建立目錄、不配置 runtime。
        if (!P72FreshSliceCFixtureOwnedPathGuard.IsExistingNonReparseDirectory(_ownedRoot))
        {
            throw new InvalidOperationException("The fresh-fixture ledger root is invalid.");
        }
    }

    /// <summary>
    /// 拒絕既有 reparse point。parent 已驗證 root 後，不存在的目標 ledger/temporary file 可由本 writer
    /// 在該 root 內建立；存在的 junction、symlink 或其他 reparse point 一律不能作為檔案目標，避免
    /// 所有權越界與不預期的資料刪改。
    /// </summary>
    /// <param name="path">待檢查的 exact owned path。</param>
    private static void RejectReparsePoint(string path)
        => P72FreshSliceCFixtureOwnedPathGuard.ThrowIfReparsePointPathOrAncestor(path);

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

/// <summary>
/// 集中驗證 Slice C parent-owned temporary path 的既有 lexical component。此 guard 不保留任何 path、ID、
/// owner、profile、credential 或 handle；它每次只使用呼叫端提供的 bounded path 讀取 file-system metadata，
/// 因此不會形成跨 child process、Windows session、使用者或 tenant 的 shared mutable state。FileLedger 在
/// constructor 與每個 atomic file operation 前重複呼叫它，parent gate 也使用同一規則，讓不存在、無法正規化、
/// reparse leaf 或任一 reparse ancestor 一律在 credential/runtime/CE 路徑前 fail closed。
/// </summary>
internal static class P72FreshSliceCFixtureOwnedPathGuard
{
    /// <summary>
    /// 判斷 root 是否已由 parent 建立且其完整 lexical ancestor chain 都是普通目錄。此方法不會建立、
    /// 刪除或開啟檔案，僅作為 child 可否開始 local recovery state I/O 的前置 gate；任何 metadata error 都
    /// 回傳 <see langword="false"/>，由上層保持 skip/no-go，而不向 evidence、console 或 session 傳播原始
    /// path/exception。檢查是固定深度的 path-component 走訪，不會掃描目錄內容或引入 unbounded I/O。
    /// </summary>
    /// <param name="root">parent 傳入的既有 current-user temporary root。</param>
    /// <returns>root 存在且自身與所有 lexical ancestors 均非 reparse point 時為 <see langword="true"/>。</returns>
    internal static bool IsExistingNonReparseDirectory(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            var fullRoot = Path.GetFullPath(root);
            if (!Directory.Exists(fullRoot))
            {
                return false;
            }

            ThrowIfReparsePointPathOrAncestor(fullRoot);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 拒絕指定 path 自身及其每一個 existing lexical ancestor 的 reparse point。對不存在的 final temporary
    /// file 仍會往上檢查其已存在 root，因此 <see cref="FileMode.CreateNew"/> 前不會因 leaf 尚未存在而略過
    /// junction/symlink 防護。這是 metadata preflight，不能取代 parent 的 single-owner root 與每次 file
    /// operation 前的重複檢查；兩者一起限制 TOCTOU 視窗並讓不確定路徑 fail closed。
    /// </summary>
    /// <param name="path">需在 parent-owned root 內使用的 final 或 temporary path。</param>
    /// <exception cref="InvalidOperationException">path 或任一既有 lexical ancestor 是 reparse point 時擲出。</exception>
    internal static void ThrowIfReparsePointPathOrAncestor(string path)
    {
        var currentPath = Path.GetFullPath(path);
        while (true)
        {
            if ((File.Exists(currentPath) || Directory.Exists(currentPath)) &&
                (File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("The fresh-fixture ledger path is invalid.");
            }

            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(parentPath) ||
                string.Equals(parentPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            currentPath = parentPath;
        }
    }
}

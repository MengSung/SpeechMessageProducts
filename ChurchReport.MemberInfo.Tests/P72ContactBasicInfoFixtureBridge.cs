// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72ContactBasicInfoFixtureBridge.cs
// 用途：提供 P7.2 contact basic-info live evidence 專用、可離線驗證的 bounded
//       orchestration state machine；不屬於 ChurchReport production runtime。
//
// 信任、隔離與生命週期：
// 1. Bridge 只接受固定 contact GUID、兩個 bounded sentinel 字串與 typed ProductClient；
//    不接受 endpoint、OrganizationId、ConnectorKind、CE version、credential、Entity、
//    QueryBase、FetchXML 或 OrganizationRequest。
// 2. Fixture store 的唯一 owner 是呼叫端。Bridge 不 Dispose、不快取、不跨 await 保存
//    CRM service；live test 必須以 using/finally 確定釋放其 WCF client。
// 3. capability 最多 dispatch 一次。例外或取消發生在 dispatch 後時，只做固定 read-back
//    reconciliation；既非 baseline 也非 sentinel 就停止，不猜測、不重送、不覆寫。
// 4. Result 只含固定分類與布林值，不含 contact GUID、baseline/sentinel、個資、帳密、
//    endpoint、token、cookie 或原始例外，適合由 PowerShell 轉為 sanitized evidence。
// ============================================================================

using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// P7.2 fixture 的兩個 allowlisted 欄位快照。物件只在單一 live test process 與 task-owned
/// contact scope 內存活；不得寫入檔案、TRX marker、log、cache、Session 或跨測試 static state。
/// </summary>
/// <param name="Phone">contact.mobilephone 的 baseline 或 sentinel；可為 null 以表示原值為空。</param>
/// <param name="Address">contact.address2_line1 的 baseline 或 sentinel；可為 null 以表示原值為空。</param>
internal sealed record P72ContactBasicInfoSnapshot(string? Phone, string? Address);

/// <summary>
/// P7.2 task-owned contact 的固定欄位存取邊界。Production ProductClient 不會看到此介面；
/// live 實作只能以固定 entity/ColumnSet 讀取，並只用於 baseline restore。呼叫端是唯一
/// Dispose owner，必須在 evidence marker 輸出前釋放底層 client。
/// </summary>
internal interface IP72ContactBasicInfoFixtureStore : IDisposable
{
    /// <summary>讀取指定 task-owned contact 的兩欄純值快照，不保留 SDK Entity。</summary>
    P72ContactBasicInfoSnapshot Read(Guid contactId);

    /// <summary>以固定 contact identity 還原兩欄 baseline；不得執行其他 mutation。</summary>
    void Restore(Guid contactId, P72ContactBasicInfoSnapshot baseline);
}

/// <summary>
/// P7.2 live evidence 專用的 Data8 fixture store。它刻意不是 generic repository：entity logical
/// name、primary key、ColumnSet、restore 欄位與型別全部是程式常數；呼叫端只能提供已由 local
/// descriptor 驗證的 contact GUID。建構後此類別是 <see cref="IOrganizationService"/> 的唯一 owner，
/// Dispose 會釋放 underlying <c>OnPremiseClient</c> 及其 WCF Channel/Factory，並拒絕後續存取。
/// </summary>
internal sealed class P72Data8ContactBasicInfoFixtureStore : IP72ContactBasicInfoFixtureStore
{
    private const string ContactEntityName = "contact";
    private const string MobilePhoneAttribute = "mobilephone";
    private const string AddressLine1Attribute = "address2_line1";
    private IOrganizationService? _service;
    private IDisposable? _disposableService;

    /// <summary>
    /// 接手單一 Data8 Organization service 的完整 ownership。無法 Dispose 的 service 被拒絕，
    /// 避免 live evidence 因測試完成而遺留 WCF channel、native handle 或 credential graph。
    /// </summary>
    /// <param name="service">只連到 sunnyvalechback CE 9.1 的 task-local OnPremiseClient。</param>
    internal P72Data8ContactBasicInfoFixtureStore(IOrganizationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        if (service is not IDisposable disposable)
        {
            throw new ArgumentException("The fixture service must support deterministic disposal.", nameof(service));
        }

        _service = service;
        _disposableService = disposable;
    }

    /// <summary>
    /// 以固定 contact identity 與兩欄 ColumnSet 讀取 baseline/reconciliation snapshot。SDK Entity
    /// 只存在於本 stack frame；回傳前投影為兩個純字串並驗證 logical name、GUID 與欄位型別。
    /// </summary>
    public P72ContactBasicInfoSnapshot Read(Guid contactId)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("A task-owned contact is required.", nameof(contactId));
        }

        var service = _service ?? throw new ObjectDisposedException(nameof(P72Data8ContactBasicInfoFixtureStore));
        var entity = service.Retrieve(
            ContactEntityName,
            contactId,
            new ColumnSet(MobilePhoneAttribute, AddressLine1Attribute));
        if (entity is null ||
            !string.Equals(entity.LogicalName, ContactEntityName, StringComparison.Ordinal) ||
            entity.Id != contactId)
        {
            throw new InvalidOperationException("The fixture contact read-back is invalid.");
        }

        return new P72ContactBasicInfoSnapshot(
            ReadOptionalString(entity, MobilePhoneAttribute),
            ReadOptionalString(entity, AddressLine1Attribute));
    }

    /// <summary>
    /// 只還原同一 contact 的兩個 allowlisted baseline 欄位。null 具有明確「清除至原始空值」語意；
    /// 不接受 field map、Entity input、OptionSet、額外 attribute 或 caller-selected entity name。
    /// read-back 由 bridge 緊接著以 <see cref="Read"/> 完成，restore 本身絕不自動重送。
    /// </summary>
    public void Restore(Guid contactId, P72ContactBasicInfoSnapshot baseline)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("A task-owned contact is required.", nameof(contactId));
        }

        ArgumentNullException.ThrowIfNull(baseline);
        var service = _service ?? throw new ObjectDisposedException(nameof(P72Data8ContactBasicInfoFixtureStore));
        var update = new Entity(ContactEntityName, contactId)
        {
            [MobilePhoneAttribute] = baseline.Phone,
            [AddressLine1Attribute] = baseline.Address
        };
        service.Update(update);
    }

    /// <summary>
    /// 釋放 store 唯一擁有的 Data8 service。先以 exchange 清空欄位，確保 concurrent/重複 Dispose
    /// 最多執行一次；即使 underlying Dispose 拋錯，store 仍永久進入 disposed 狀態，不會重用不可信 client。
    /// </summary>
    public void Dispose()
    {
        _service = null;
        var disposable = Interlocked.Exchange(ref _disposableService, null);
        disposable?.Dispose();
    }

    /// <summary>只接受 null 或純字串欄位，拒絕 SDK graph 與不相容型別。</summary>
    private static string? ReadOptionalString(Entity entity, string attributeName)
    {
        if (!entity.Attributes.TryGetValue(attributeName, out var value) || value is null)
        {
            return null;
        }

        return value as string
            ?? throw new InvalidOperationException("The fixture contact attribute type is invalid.");
    }
}

/// <summary>
/// 去識別化 fixture bridge 結果。所有字串都是程式固定分類，不能包含 caller input、
/// contact identity、欄位值、exception message 或 routing metadata。
/// </summary>
internal sealed record P72ContactBasicInfoFixtureBridgeResult
{
    /// <summary><c>go</c> 或 <c>no-go</c>。</summary>
    public required string Outcome { get; init; }

    /// <summary>固定失敗／reconciliation 分類；成功時為空。</summary>
    public required string Reason { get; init; }

    /// <summary>表示 typed capability 已進入唯一一次 dispatch；不代表 server 一定 commit。</summary>
    public required bool OperationExecuted { get; init; }

    /// <summary>baseline/sentinel/unknown 的固定 read-back 分類。</summary>
    public required string SentinelState { get; init; }

    /// <summary>restored/not-required/manual-reconciliation-required 的固定清理分類。</summary>
    public required string CleanupState { get; init; }
}

/// <summary>
/// P7.2 contact basic-info evidence 的單次 state machine。它先取得 baseline，再透過
/// <see cref="IPackage02ContactBasicInfoUpdateClient"/> dispatch 唯一一次 sentinel update，
/// 之後一律以 task-owned store read-back 判定是否可安全 restore。任何 ambiguous 狀態
/// 都維持 no-go；即使 read-back 證明資料已復原，也不把 transport ambiguity 升格為 go。
/// </summary>
internal static class P72ContactBasicInfoFixtureBridge
{
    private const int MaximumSentinelCharacters = 128;

    /// <summary>
    /// 執行一次 bounded fixture evidence。Bridge 不取得 client/store ownership；呼叫端必須在本方法
    /// 完成後 Dispose store 與 runtime。取消在 dispatch 前直接傳播；dispatch 後若 client 回報例外，
    /// 仍必須完成 read-back/cleanup，因此 cleanup 不使用已取消 token，也絕不再呼叫 typed client。
    /// </summary>
    /// <param name="client">只允許 contact basic-info capability 的 typed ProductClient。</param>
    /// <param name="store">只允許固定 contact 兩欄 read/restore 的 task-owned store。</param>
    /// <param name="contactId">已由 local descriptor 與 Windows owner 驗證的非空 GUID。</param>
    /// <param name="idempotencyKey">單次 evidence 的 bounded URL-safe key；不重用於 retry。</param>
    /// <param name="sentinelPhone">本次唯一手機 sentinel；不輸出到 evidence。</param>
    /// <param name="sentinelAddress">本次唯一地址 sentinel；不輸出到 evidence。</param>
    /// <param name="cancellationToken">只控制 baseline 與首次 dispatch；dispatch 後不阻斷必要 cleanup。</param>
    /// <returns>不含秘密或個資的固定分類。</returns>
    internal static async Task<P72ContactBasicInfoFixtureBridgeResult> ExecuteAsync(
        IPackage02ContactBasicInfoUpdateClient client,
        IP72ContactBasicInfoFixtureStore store,
        Guid contactId,
        string idempotencyKey,
        string sentinelPhone,
        string sentinelAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("A task-owned contact is required.", nameof(contactId));
        }

        var normalizedKey = RequireBounded(idempotencyKey, nameof(idempotencyKey));
        var sentinel = new P72ContactBasicInfoSnapshot(
            RequireBounded(sentinelPhone, nameof(sentinelPhone)),
            RequireBounded(sentinelAddress, nameof(sentinelAddress)));

        cancellationToken.ThrowIfCancellationRequested();
        var baseline = store.Read(contactId);
        if (baseline == sentinel)
        {
            throw new InvalidOperationException("The fixture sentinel must differ from baseline.");
        }

        var operationExecuted = false;
        var writeReportedSuccess = false;
        var writeFaulted = false;
        try
        {
            operationExecuted = true;
            var updateResult = await client.UpdateAsync(new ContactBasicInfoUpdateRequest
            {
                ProfileAlias = "sunnyvalechback",
                WorkloadSubjectId = "p7.2-contact-basic-info-fixture",
                ContactId = contactId,
                Phone = sentinel.Phone,
                Address = sentinel.Address,
                IdempotencyKey = normalizedKey
            }, cancellationToken).ConfigureAwait(false);

            writeReportedSuccess = updateResult.Disposition == ContactBasicInfoUpdateDisposition.Changed &&
                updateResult.CorrelationCategory == ContactBasicInfoUpdateCorrelationCategory.ReadBackConfirmed;
        }
        catch (Exception) when (operationExecuted)
        {
            // Dispatch 後任何例外都可能是 timeout-after-commit；不得回顯例外或再次呼叫 client。
            writeFaulted = true;
        }

        P72ContactBasicInfoSnapshot current;
        try
        {
            current = store.Read(contactId);
        }
        catch (Exception)
        {
            return NoGo(
                "reconciliation-failed",
                operationExecuted,
                "unknown",
                "manual-reconciliation-required");
        }

        if (current == baseline)
        {
            return NoGo(
                writeReportedSuccess ? "write-response-state-mismatch" : "write-not-committed",
                operationExecuted,
                "baseline",
                "not-required");
        }

        if (current != sentinel)
        {
            return NoGo(
                "write-ambiguous",
                operationExecuted,
                "unknown",
                "manual-reconciliation-required");
        }

        var sentinelState = writeFaulted ? "confirmed-after-fault" : "confirmed";
        if (!writeReportedSuccess && !writeFaulted)
        {
            // Typed client 回傳非 Changed/read-back-confirmed，但外部狀態已是 sentinel；仍 cleanup，
            // evidence 維持 no-go，避免錯誤 response contract 被視為成功。
            sentinelState = "confirmed-after-invalid-response";
        }

        var cleanupFaulted = false;
        try
        {
            store.Restore(contactId, baseline);
        }
        catch (Exception)
        {
            cleanupFaulted = true;
        }

        P72ContactBasicInfoSnapshot afterCleanup;
        try
        {
            afterCleanup = store.Read(contactId);
        }
        catch (Exception)
        {
            return NoGo(
                "cleanup-reconciliation-failed",
                operationExecuted,
                sentinelState,
                "manual-reconciliation-required");
        }

        if (afterCleanup != baseline)
        {
            return NoGo(
                "cleanup-failed",
                operationExecuted,
                sentinelState,
                "manual-reconciliation-required");
        }

        if (cleanupFaulted)
        {
            return NoGo(
                "cleanup-ambiguous-reconciled",
                operationExecuted,
                sentinelState,
                "restored-after-fault");
        }

        if (writeFaulted)
        {
            return NoGo(
                "write-ambiguous-reconciled",
                operationExecuted,
                sentinelState,
                "restored");
        }

        if (!writeReportedSuccess)
        {
            return NoGo(
                "write-result-invalid",
                operationExecuted,
                sentinelState,
                "restored");
        }

        return new P72ContactBasicInfoFixtureBridgeResult
        {
            Outcome = "go",
            Reason = string.Empty,
            OperationExecuted = operationExecuted,
            SentinelState = sentinelState,
            CleanupState = "restored"
        };
    }

    /// <summary>建立只含固定分類的 no-go 結果，不保留輸入或例外。</summary>
    private static P72ContactBasicInfoFixtureBridgeResult NoGo(
        string reason,
        bool operationExecuted,
        string sentinelState,
        string cleanupState)
        => new()
        {
            Outcome = "no-go",
            Reason = reason,
            OperationExecuted = operationExecuted,
            SentinelState = sentinelState,
            CleanupState = cleanupState
        };

    /// <summary>
    /// 驗證 bridge 自有 scalar 上限；ProductClient 會再驗證 UTF-8 byte limit 與 URL-safe key。
    /// 此處只防止 fixture process 在 dispatch 前保留空白或不受限的大字串。
    /// </summary>
    private static string RequireBounded(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A bounded fixture value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > MaximumSentinelCharacters)
        {
            throw new ArgumentException("The fixture value exceeds its bounded size.", parameterName);
        }

        return normalized;
    }
}

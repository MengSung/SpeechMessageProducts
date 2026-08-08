// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/P72ContactBasicInfoFixtureBridgeTests.cs
// 用途：以離線狀態機驗證 P7.2 contact basic-info fixture bridge 的單次寫入、
//       ambiguous reconciliation、baseline restore 與去識別化結果契約。
//
// 安全與生命週期：
// 1. 測試替身只保存兩個 allowlisted 字串，不含 endpoint、credential、token、cookie、
//    OrganizationId、CRM Entity 或跨測試 static state。
// 2. 每個測試建立自己的 store 與 client；bridge 不取得其 dispose ownership，呼叫端在
//    using scope 結束時確定釋放，避免 live 實作的 WCF client 跨 fixture 留存。
// 3. fault injection 明確區分 timeout-before-commit、timeout-after-commit、restore-before-
//    commit 與 restore-after-commit，決定性斷言不會盲目重送寫入。
// ============================================================================

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.ProductClient.MemberInfo;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證 P7.2 fixture bridge 的安全狀態轉移。每個案例都以單一 task-owned contact 的
/// baseline/sentinel 純值模擬 CE，並以 client 呼叫次數、restore 次數與固定分類作為
/// 決定性 assertion；若 bridge 新增 retry、跨 contact state 或未確認 cleanup，測試會失敗。
/// </summary>
public sealed class P72ContactBasicInfoFixtureBridgeTests
{
    private static readonly Guid ContactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly P72ContactBasicInfoSnapshot Baseline = new("baseline-phone", "baseline-address");
    private static readonly P72ContactBasicInfoSnapshot Sentinel = new("p72-phone-sentinel", "p72-address-sentinel");

    /// <summary>
    /// 保護正常路徑：typed capability 只 dispatch 一次，read-back 已確認後仍由 fixture owner
    /// 還原 baseline，且只有 restore read-back 完全相符才可回傳 go。
    /// </summary>
    [Fact]
    public async Task Successful_write_restores_baseline_and_returns_go()
    {
        using var store = new RecordingFixtureStore(Baseline);
        var client = new RecordingContactClient(request =>
        {
            store.Current = new(request.Phone, request.Address);
            return Task.FromResult(ChangedResult());
        });

        var result = await P72ContactBasicInfoFixtureBridge.ExecuteAsync(
            client,
            store,
            ContactId,
            "p72-fixture-success",
            Sentinel.Phone!,
            Sentinel.Address!);

        result.Outcome.Should().Be("go");
        result.Reason.Should().BeEmpty();
        result.OperationExecuted.Should().BeTrue();
        result.SentinelState.Should().Be("confirmed");
        result.CleanupState.Should().Be("restored");
        client.CallCount.Should().Be(1);
        store.RestoreCount.Should().Be(1);
        store.Current.Should().Be(Baseline);
    }

    /// <summary>
    /// 注入「寫入已 commit 但 caller 收到例外」；bridge 必須先 read-back 證明 sentinel，
    /// 再還原 baseline，整體仍維持 no-go，且不得以相同 idempotency key 重送。
    /// </summary>
    [Fact]
    public async Task Ambiguous_write_after_commit_reconciles_and_restores_without_retry()
    {
        using var store = new RecordingFixtureStore(Baseline);
        var client = new RecordingContactClient(request =>
        {
            store.Current = new(request.Phone, request.Address);
            throw new InvalidOperationException("fault-after-commit");
        });

        var result = await P72ContactBasicInfoFixtureBridge.ExecuteAsync(
            client,
            store,
            ContactId,
            "p72-fixture-ambiguous",
            Sentinel.Phone!,
            Sentinel.Address!);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("write-ambiguous-reconciled");
        result.SentinelState.Should().Be("confirmed-after-fault");
        result.CleanupState.Should().Be("restored");
        client.CallCount.Should().Be(1);
        store.RestoreCount.Should().Be(1);
        store.Current.Should().Be(Baseline);
    }

    /// <summary>
    /// 注入「寫入前失敗」；目前值仍是 baseline 時不得做第二次 write，也不需要 restore。
    /// </summary>
    [Fact]
    public async Task Write_fault_before_commit_stops_without_retry_or_restore()
    {
        using var store = new RecordingFixtureStore(Baseline);
        var client = new RecordingContactClient(_ => throw new InvalidOperationException("fault-before-commit"));

        var result = await P72ContactBasicInfoFixtureBridge.ExecuteAsync(
            client,
            store,
            ContactId,
            "p72-fixture-not-committed",
            Sentinel.Phone!,
            Sentinel.Address!);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("write-not-committed");
        result.SentinelState.Should().Be("baseline");
        result.CleanupState.Should().Be("not-required");
        client.CallCount.Should().Be(1);
        store.RestoreCount.Should().Be(0);
    }

    /// <summary>
    /// 注入部分／未知狀態；既非完整 baseline 也非完整 sentinel 時 bridge 必須停止，
    /// 不猜測哪一欄已 commit，也不得自行覆寫未知狀態。
    /// </summary>
    [Fact]
    public async Task Unknown_state_after_write_fault_stops_without_cleanup_guess()
    {
        using var store = new RecordingFixtureStore(Baseline);
        var client = new RecordingContactClient(request =>
        {
            store.Current = new(request.Phone, "unexpected-address");
            throw new InvalidOperationException("partial-state");
        });

        var result = await P72ContactBasicInfoFixtureBridge.ExecuteAsync(
            client,
            store,
            ContactId,
            "p72-fixture-partial",
            Sentinel.Phone!,
            Sentinel.Address!);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("write-ambiguous");
        result.SentinelState.Should().Be("unknown");
        result.CleanupState.Should().Be("manual-reconciliation-required");
        client.CallCount.Should().Be(1);
        store.RestoreCount.Should().Be(0);
    }

    /// <summary>
    /// 注入 restore 已 commit 後才拋出；read-back 若證明 baseline，資料已清理但 evidence
    /// 仍保持 no-go，讓後續 activation 不把 transport ambiguity 當成完整成功。
    /// </summary>
    [Fact]
    public async Task Restore_fault_after_commit_is_reconciled_but_remains_no_go()
    {
        using var store = new RecordingFixtureStore(Baseline)
        {
            RestoreFault = RestoreFaultMode.AfterCommit
        };
        var client = new RecordingContactClient(request =>
        {
            store.Current = new(request.Phone, request.Address);
            return Task.FromResult(ChangedResult());
        });

        var result = await P72ContactBasicInfoFixtureBridge.ExecuteAsync(
            client,
            store,
            ContactId,
            "p72-fixture-restore-after",
            Sentinel.Phone!,
            Sentinel.Address!);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("cleanup-ambiguous-reconciled");
        result.CleanupState.Should().Be("restored-after-fault");
        client.CallCount.Should().Be(1);
        store.RestoreCount.Should().Be(1);
        store.Current.Should().Be(Baseline);
    }

    /// <summary>
    /// 注入 restore 尚未 commit 即失敗；read-back 仍是 sentinel 時必須回傳 cleanup failure，
    /// 不得重做 restore，以免把不具 server-side idempotency 的清理變成盲目重送。
    /// </summary>
    [Fact]
    public async Task Restore_fault_before_commit_stops_without_retry()
    {
        using var store = new RecordingFixtureStore(Baseline)
        {
            RestoreFault = RestoreFaultMode.BeforeCommit
        };
        var client = new RecordingContactClient(request =>
        {
            store.Current = new(request.Phone, request.Address);
            return Task.FromResult(ChangedResult());
        });

        var result = await P72ContactBasicInfoFixtureBridge.ExecuteAsync(
            client,
            store,
            ContactId,
            "p72-fixture-restore-before",
            Sentinel.Phone!,
            Sentinel.Address!);

        result.Outcome.Should().Be("no-go");
        result.Reason.Should().Be("cleanup-failed");
        result.CleanupState.Should().Be("manual-reconciliation-required");
        client.CallCount.Should().Be(1);
        store.RestoreCount.Should().Be(1);
        store.Current.Should().Be(Sentinel);
    }

    /// <summary>
    /// 驗證 live store 只用固定 contact/ColumnSet 讀取兩欄，restore 只提交相同 contact 的兩欄
    /// baseline，且 Dispose 將唯一 service ownership 釋放一次。任何 generic query 或額外欄位都會使 assertion 失敗。
    /// </summary>
    [Fact]
    public void Live_store_reads_and_restores_only_two_allowlisted_fields()
    {
        var service = new RecordingOrganizationService(ContactId, Baseline);
        using (var store = new P72Data8ContactBasicInfoFixtureStore(service))
        {
            store.Read(ContactId).Should().Be(Baseline);
            store.Restore(ContactId, new(null, "restored-address"));
        }

        service.RetrieveCount.Should().Be(1);
        service.UpdateCount.Should().Be(1);
        service.DisposalCount.Should().Be(1);
        service.RetrievedColumns.Should().Equal("mobilephone", "address2_line1");
        service.UpdatedEntity.Should().NotBeNull();
        service.UpdatedEntity!.LogicalName.Should().Be("contact");
        service.UpdatedEntity.Id.Should().Be(ContactId);
        service.UpdatedEntity.Attributes.Keys.Should().BeEquivalentTo(["mobilephone", "address2_line1"]);
        service.UpdatedEntity["mobilephone"].Should().BeNull();
        service.UpdatedEntity["address2_line1"].Should().Be("restored-address");
    }

    /// <summary>建立 typed capability 成功且 read-back confirmed 的固定結果。</summary>
    private static ContactBasicInfoUpdateResult ChangedResult()
        => new()
        {
            Disposition = ContactBasicInfoUpdateDisposition.Changed,
            CorrelationCategory = ContactBasicInfoUpdateCorrelationCategory.ReadBackConfirmed
        };

    /// <summary>
    /// 離線 fixture store。Current 只存兩個純值，Dispose 後拒絕存取；RestoreFault 用來
    /// 精確注入 cleanup commit 前／後錯誤，決定性驗證 reconciliation 與不重試契約。
    /// </summary>
    private sealed class RecordingFixtureStore : IP72ContactBasicInfoFixtureStore
    {
        private bool _disposed;

        /// <summary>建立單一測試擁有的 baseline。</summary>
        public RecordingFixtureStore(P72ContactBasicInfoSnapshot current) => Current = current;

        /// <summary>目前兩欄純值；不跨測試共享。</summary>
        public P72ContactBasicInfoSnapshot Current { get; set; }

        /// <summary>restore fault injection。</summary>
        public RestoreFaultMode RestoreFault { get; init; }

        /// <summary>restore 呼叫次數，用來證明沒有盲目重送。</summary>
        public int RestoreCount { get; private set; }

        /// <summary>回傳目前 snapshot；Dispose 後 fail closed。</summary>
        public P72ContactBasicInfoSnapshot Read(Guid contactId)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            contactId.Should().Be(ContactId);
            return Current;
        }

        /// <summary>依 fault injection 在 commit 前或後失敗；正常時只套用一次 baseline。</summary>
        public void Restore(Guid contactId, P72ContactBasicInfoSnapshot baseline)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            contactId.Should().Be(ContactId);
            RestoreCount++;
            if (RestoreFault == RestoreFaultMode.BeforeCommit)
            {
                throw new InvalidOperationException("restore-before-commit");
            }

            Current = baseline;
            if (RestoreFault == RestoreFaultMode.AfterCommit)
            {
                throw new InvalidOperationException("restore-after-commit");
            }
        }

        /// <summary>結束 fixture scope；本替身無外部資源，但保護 live store 的相同 ownership contract。</summary>
        public void Dispose() => _disposed = true;
    }

    /// <summary>以委派注入唯一一次 typed capability 行為，並記錄呼叫次數。</summary>
    private sealed class RecordingContactClient : IPackage02ContactBasicInfoUpdateClient
    {
        private readonly Func<ContactBasicInfoUpdateRequest, Task<ContactBasicInfoUpdateResult>> _operation;

        /// <summary>建立不保存 credential、service 或 session 的離線 client。</summary>
        public RecordingContactClient(Func<ContactBasicInfoUpdateRequest, Task<ContactBasicInfoUpdateResult>> operation)
            => _operation = operation ?? throw new ArgumentNullException(nameof(operation));

        /// <summary>實際呼叫次數；所有 ambiguous 案例都必須保持 1。</summary>
        public int CallCount { get; private set; }

        /// <summary>執行一次注入行為；不建立背景 task、timer 或 retry queue。</summary>
        public Task<ContactBasicInfoUpdateResult> UpdateAsync(
            ContactBasicInfoUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return _operation(request);
        }
    }

    /// <summary>
    /// 固定 contact 的 IOrganizationService 替身。只允許一次 Retrieve 與一次 Update；所有 generic
    /// CRUD/Execute/association 路徑都拋錯，確保 live store 不會擴張成任意 CRM API。
    /// </summary>
    private sealed class RecordingOrganizationService : IOrganizationService, IDisposable
    {
        private readonly Guid _contactId;
        private readonly P72ContactBasicInfoSnapshot _snapshot;

        /// <summary>建立固定 contact 與兩欄 read-back。</summary>
        public RecordingOrganizationService(Guid contactId, P72ContactBasicInfoSnapshot snapshot)
        {
            _contactId = contactId;
            _snapshot = snapshot;
        }

        /// <summary>Retrieve 呼叫次數。</summary>
        public int RetrieveCount { get; private set; }

        /// <summary>Update 呼叫次數。</summary>
        public int UpdateCount { get; private set; }

        /// <summary>Dispose 呼叫次數。</summary>
        public int DisposalCount { get; private set; }

        /// <summary>實際固定欄位順序。</summary>
        public IReadOnlyList<string> RetrievedColumns { get; private set; } = [];

        /// <summary>restore 提交的單一 Entity；只供 assertion。</summary>
        public Entity? UpdatedEntity { get; private set; }

        /// <summary>只接受 contact + fixed ColumnSet。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            entityName.Should().Be("contact");
            id.Should().Be(_contactId);
            RetrieveCount++;
            RetrievedColumns = columnSet.Columns.ToArray();
            return new Entity("contact", _contactId)
            {
                ["mobilephone"] = _snapshot.Phone,
                ["address2_line1"] = _snapshot.Address
            };
        }

        /// <summary>只記錄 fixed restore Entity。</summary>
        public void Update(Entity entity)
        {
            UpdateCount++;
            UpdatedEntity = entity;
        }

        /// <summary>釋放唯一 service owner。</summary>
        public void Dispose() => DisposalCount++;

        /// <inheritdoc />
        public Guid Create(Entity entity) => throw Unexpected();

        /// <inheritdoc />
        public void Delete(string entityName, Guid id) => throw Unexpected();

        /// <inheritdoc />
        public OrganizationResponse Execute(OrganizationRequest request) => throw Unexpected();

        /// <inheritdoc />
        public EntityCollection RetrieveMultiple(QueryBase query) => throw Unexpected();

        /// <inheritdoc />
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw Unexpected();

        /// <inheritdoc />
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw Unexpected();

        /// <summary>建立固定錯誤，證明未核准 API 不可被 live store 呼叫。</summary>
        private static InvalidOperationException Unexpected()
            => new("The fixture store called an unapproved CRM operation.");
    }

    /// <summary>restore fault injection 的封閉集合。</summary>
    private enum RestoreFaultMode
    {
        /// <summary>無 fault。</summary>
        None,

        /// <summary>在 baseline 套用前失敗。</summary>
        BeforeCommit,

        /// <summary>baseline 已套用後才失敗。</summary>
        AfterCommit
    }
}

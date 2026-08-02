// ============================================================================
// 檔案：SpeechMessage.Dynamics.ControlPlane/Capacity/OrganizationAdmissionRegistry.cs
// 目的：為同一實體 Dynamics Organization 建立唯一、引用計數的 Admission Manager，
//       並拒絕 GUID、Base URI、Admission Namespace 或 Lease Namespace 的模糊／衝突映射。
//
// 資源擁有權：
// - Registry 唯一擁有它建立的 OrganizationAdmissionManager。
// - Profile Runtime 只持有 Registration，不直接 Dispose Manager。
// - 最後一個 Registration 釋放時，Entry 先從所有索引移除，再於鎖外 await Manager.DisposeAsync。
// - Registry Shutdown 會原子封閉新 Acquire、清除索引並確定性等待所有 Manager 完成清理。
// ============================================================================

using Microsoft.Extensions.Logging;

namespace SpeechMessage.Dynamics.ControlPlane.Capacity;

/// <summary>
/// Canonical Organization Admission Registry 的預設實作。
/// 所有索引與引用計數都由單一私有鎖保護，不使用 static／global mutable state，
/// 因此不同 ServiceProvider、測試 Host 或 Gateway Process 之間不會共用 Request、Credential 或 Session 狀態。
/// Registry 鎖只保護短時間 Dictionary 操作；任何可能等待的 Manager Dispose 都在鎖外執行，
/// 避免 shutdown、registration release 與 runtime drain 互相形成 deadlock。
/// </summary>
public sealed class OrganizationAdmissionRegistry : IOrganizationAdmissionRegistry
{
    private readonly object _gate = new();
    private readonly IRuntimeHostSlotCoordinator _slotCoordinator;
    private readonly ILogger<OrganizationAdmissionRegistry> _logger;
    private readonly ILogger<OrganizationAdmissionManager> _managerLogger;
    private readonly Dictionary<CanonicalOrganizationCapacityKey, Entry> _entries = new();
    private readonly Dictionary<Guid, CanonicalOrganizationCapacityKey> _organizationIdBindings = new();
    private readonly Dictionary<string, CanonicalOrganizationCapacityKey> _baseUriBindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CanonicalOrganizationCapacityKey> _admissionNamespaceBindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CanonicalOrganizationCapacityKey> _leaseNamespaceBindings = new(StringComparer.Ordinal);

    private bool _disposed;
    private Task? _disposeTask;

    /// <summary>
    /// 建立一個程序內 Registry；Coordinator 與 Logger 由外層 Host 注入，但 Manager 的建立與釋放完全由 Registry 擁有。
    /// Constructor 不取得 Host Slot、不啟動 Timer，也不解析 Secret；真正的外部資源只會在 Manager 被使用時依既有契約建立。
    /// </summary>
    /// <param name="slotCoordinator">負責 Runtime Host Slot 的程序內或 Durable Coordinator。</param>
    /// <param name="logger">只記錄 bounded lifecycle 訊號、不得記錄 Token、Credential 或 Request Body 的 Registry Logger。</param>
    /// <param name="managerLogger">提供給新建 Admission Manager 的 Logger。</param>
    public OrganizationAdmissionRegistry(
        IRuntimeHostSlotCoordinator slotCoordinator,
        ILogger<OrganizationAdmissionRegistry> logger,
        ILogger<OrganizationAdmissionManager> managerLogger)
    {
        _slotCoordinator = slotCoordinator ?? throw new ArgumentNullException(nameof(slotCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _managerLogger = managerLogger ?? throw new ArgumentNullException(nameof(managerLogger));
    }

    /// <summary>
    /// 取得目前仍由 Registry 擁有的 Canonical Organization Entry 數量。
    /// 此值在 Registry 鎖內建立快照，只反映共享 Admission Manager ownership，不包含 Profile、Request、User、Token 或 Session 數量。
    /// </summary>
    public int EntryCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// 依已驗證的 Canonical Plan 取得引用計數 Registration；相同實體 Organization 只能共用一個相容 Admission Manager。
    /// 方法先驗證 GUID、Base URI、Admission Namespace 與 Lease Namespace 的雙向碰撞，再原子增加引用或建立新 Entry；
    /// 任何不相容容量、Epoch 或 Namespace 都在配置新 Manager、Semaphore、CTS 或 Host Slot 前 fail closed。
    /// 回傳 Registration 由呼叫端唯一擁有，最後一個 Registration 釋放時才會在鎖外確定性 Dispose Manager。
    /// </summary>
    public IOrganizationAdmissionRegistration Acquire(OrganizationAdmissionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ValidateReverseBindings(plan);

            if (_entries.TryGetValue(plan.CanonicalKey, out var existing))
            {
                ValidateCompatiblePlan(existing.Plan, plan);
                checked
                {
                    existing.ReferenceCount++;
                }

                return new Registration(this, existing);
            }

            // Manager constructor 只配置 bounded in-memory primitives，不執行網路或等待；
            // 因此可在鎖內建立並原子發布，避免兩個平行 Acquire 為同一 Canonical Key 建立雙 Manager。
            var manager = new OrganizationAdmissionManager(plan, _slotCoordinator, _managerLogger);
            var entry = new Entry(plan, manager);
            _entries.Add(plan.CanonicalKey, entry);
            AddReverseBindings(plan);

            _logger.LogDebug(
                "Organization admission registry created one canonical entry. EntryCount={EntryCount}",
                _entries.Count);
            return new Registration(this, entry);
        }
    }

    /// <summary>
    /// 同步釋放 Registry，並確定性等待非同步 Manager 清理完成。
    /// 使用 Task.Run 隔離呼叫端 SynchronizationContext，避免 UI／舊 ASP.NET Context 因同步等待 async continuation 而 deadlock；
    /// Task 仍會被同步等待完成，因此不會留下 fire-and-forget 背景清理或未觀察例外。
    /// </summary>
    public void Dispose()
        => RunSynchronously(DisposeAsync);

    /// <summary>
    /// 原子停止新的 Acquire、清除所有索引，接著在鎖外平行等待每個剩餘 Manager 完成 bounded drain 與 Host Slot release。
    /// 多次呼叫會共享同一個 Dispose Task；因此 Manager、Semaphore、CTS 與 Lease 不會被重複釋放。
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_gate)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposed = true;
            var managers = _entries.Values
                .Select(entry => entry.Manager)
                .ToArray();

            _entries.Clear();
            _organizationIdBindings.Clear();
            _baseUriBindings.Clear();
            _admissionNamespaceBindings.Clear();
            _leaseNamespaceBindings.Clear();

            disposeTask = DisposeManagersAsync(managers);
            _disposeTask = disposeTask;
        }

        return new ValueTask(disposeTask);
    }

    /// <summary>
    /// 驗證同一 Canonical Key 的共享容量與 Namespace 完全相容。
    /// 即使 ConfigurationDigest 未來演算法改變，顯式欄位比較仍可防止 Namespace／Epoch 漂移被意外接受。
    /// </summary>
    private static void ValidateCompatiblePlan(
        OrganizationAdmissionPlan existing,
        OrganizationAdmissionPlan candidate)
    {
        if (!string.Equals(existing.ConfigurationDigest, candidate.ConfigurationDigest, StringComparison.Ordinal) ||
            existing.AdmissionKey != candidate.AdmissionKey ||
            existing.LeaseNamespace != candidate.LeaseNamespace ||
            existing.AdmissionEpoch != candidate.AdmissionEpoch)
        {
            throw new InvalidOperationException(
                "The canonical Dynamics organization is already registered with a different capacity, namespace, or epoch.");
        }
    }

    /// <summary>
    /// 以四個反向索引拒絕部分碰撞與跨 Organization Namespace 重用。
    /// 驗證必須在建立 Manager 前完成，否則失敗設定仍可能配置 Semaphore、CTS 或背景租約狀態。
    /// </summary>
    private void ValidateReverseBindings(OrganizationAdmissionPlan plan)
    {
        ValidateReverseBinding(
            _organizationIdBindings,
            plan.CanonicalKey.ExpectedOrganizationId,
            plan.CanonicalKey,
            "ExpectedOrganizationId");
        ValidateReverseBinding(
            _baseUriBindings,
            plan.CanonicalKey.NormalizedOrganizationBaseUri,
            plan.CanonicalKey,
            "NormalizedOrganizationBaseUri");
        ValidateReverseBinding(
            _admissionNamespaceBindings,
            plan.AdmissionKey.AdmissionNamespaceId,
            plan.CanonicalKey,
            "AdmissionNamespaceId");
        ValidateReverseBinding(
            _leaseNamespaceBindings,
            plan.LeaseNamespace.LeaseNamespaceId,
            plan.CanonicalKey,
            "LeaseNamespaceId");
    }

    /// <summary>
    /// 驗證單一反向索引。錯誤訊息只指出衝突欄位，不輸出實際內部 URI、Namespace 或 Organization GUID，
    /// 避免把部署拓撲與識別資訊洩漏到一般產品錯誤回應或未經過濾的上層記錄。
    /// </summary>
    private static void ValidateReverseBinding<TKey>(
        IReadOnlyDictionary<TKey, CanonicalOrganizationCapacityKey> bindings,
        TKey reverseKey,
        CanonicalOrganizationCapacityKey canonicalKey,
        string fieldName)
        where TKey : notnull
    {
        if (bindings.TryGetValue(reverseKey, out var existing) && existing != canonicalKey)
        {
            throw new InvalidOperationException(
                $"The configured {fieldName} is already bound to a different canonical Dynamics organization.");
        }
    }

    /// <summary>
    /// 在新 Entry 已成功建立後一次加入所有反向索引。
    /// 這段程式只會在 Registry 鎖內執行，因此其他執行緒不會看到只建立一半的 Canonical Binding。
    /// </summary>
    private void AddReverseBindings(OrganizationAdmissionPlan plan)
    {
        _organizationIdBindings.Add(plan.CanonicalKey.ExpectedOrganizationId, plan.CanonicalKey);
        _baseUriBindings.Add(plan.CanonicalKey.NormalizedOrganizationBaseUri, plan.CanonicalKey);
        _admissionNamespaceBindings.Add(plan.AdmissionKey.AdmissionNamespaceId, plan.CanonicalKey);
        _leaseNamespaceBindings.Add(plan.LeaseNamespace.LeaseNamespaceId, plan.CanonicalKey);
    }

    /// <summary>
    /// 釋放一個 Registration。只有仍存在且物件身分相同的 Entry 才會遞減引用，
    /// 可避免 Registry Shutdown 後的晚到 Dispose 或舊 Registration 誤傷後來重建的同 Key Entry。
    /// </summary>
    private async ValueTask ReleaseAsync(Entry entry)
    {
        IOrganizationAdmissionManager? managerToDispose = null;
        lock (_gate)
        {
            if (_disposed ||
                !_entries.TryGetValue(entry.Plan.CanonicalKey, out var current) ||
                !ReferenceEquals(current, entry))
            {
                return;
            }

            if (entry.ReferenceCount <= 0)
            {
                throw new InvalidOperationException("Organization admission registration reference count underflow.");
            }

            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                _entries.Remove(entry.Plan.CanonicalKey);
                RemoveReverseBindings(entry.Plan);
                managerToDispose = entry.Manager;
            }
        }

        // Manager shutdown 可能等待現有 Permit、續租 Task 與 Host Slot release，絕不可持有 Registry 鎖等待。
        if (managerToDispose is not null)
        {
            await managerToDispose.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 移除最後一個 Registration 所屬的反向索引。
    /// Remove 前再次比對 Canonical Key，避免未來擴充或異常狀態誤刪其他 Organization 的綁定。
    /// </summary>
    private void RemoveReverseBindings(OrganizationAdmissionPlan plan)
    {
        RemoveReverseBinding(_organizationIdBindings, plan.CanonicalKey.ExpectedOrganizationId, plan.CanonicalKey);
        RemoveReverseBinding(_baseUriBindings, plan.CanonicalKey.NormalizedOrganizationBaseUri, plan.CanonicalKey);
        RemoveReverseBinding(_admissionNamespaceBindings, plan.AdmissionKey.AdmissionNamespaceId, plan.CanonicalKey);
        RemoveReverseBinding(_leaseNamespaceBindings, plan.LeaseNamespace.LeaseNamespaceId, plan.CanonicalKey);
    }

    /// <summary>
    /// 只有反向索引仍指向預期 Canonical Key 時才移除，避免 race 或資料損壞擴大到其他 Entry。
    /// </summary>
    private static void RemoveReverseBinding<TKey>(
        IDictionary<TKey, CanonicalOrganizationCapacityKey> bindings,
        TKey reverseKey,
        CanonicalOrganizationCapacityKey expectedCanonicalKey)
        where TKey : notnull
    {
        if (bindings.TryGetValue(reverseKey, out var current) && current == expectedCanonicalKey)
        {
            bindings.Remove(reverseKey);
        }
    }

    /// <summary>
    /// 等待所有 Registry-owned Manager 完成清理。Task.WhenAll 會保留任一釋放失敗，
    /// 不會把 Host Slot release、Timer 停止或 Semaphore 清理問題悄悄吞掉。
    /// </summary>
    private async Task DisposeManagersAsync(IReadOnlyCollection<IOrganizationAdmissionManager> managers)
    {
        try
        {
            await Task.WhenAll(managers.Select(
                static manager => manager.DisposeAsync().AsTask())).ConfigureAwait(false);
        }
        finally
        {
            _logger.LogDebug(
                "Organization admission registry disposal completed. ManagerCount={ManagerCount}",
                managers.Count);
        }
    }

    /// <summary>
    /// 將非同步清理移到 ThreadPool 並同步等待結果，避免捕捉呼叫端 SynchronizationContext，
    /// 同時確保例外可被呼叫端觀察且不留下背景清理工作。
    /// </summary>
    private static void RunSynchronously(Func<ValueTask> disposeAction)
        => Task.Run(async () => await disposeAction().ConfigureAwait(false))
            .GetAwaiter()
            .GetResult();

    /// <summary>
    /// Registry 內部 Entry，只保存不可變 Plan、Manager 與受鎖保護的引用計數。
    /// 它不保存 Profile Alias、Request、Token、Credential、User 或 Session，因此不能形成跨租戶資料保留。
    /// </summary>
    private sealed class Entry
    {
        /// <summary>
        /// 建立引用計數為一的新 Entry；呼叫端已在同一 Registry 鎖內完成所有反向碰撞驗證。
        /// </summary>
        public Entry(OrganizationAdmissionPlan plan, IOrganizationAdmissionManager manager)
        {
            Plan = plan;
            Manager = manager;
            ReferenceCount = 1;
        }

        /// <summary>取得此 Entry 的不可變容量計畫。</summary>
        public OrganizationAdmissionPlan Plan { get; }

        /// <summary>取得 Registry 唯一擁有的 Admission Manager。</summary>
        public IOrganizationAdmissionManager Manager { get; }

        /// <summary>取得或設定目前 Registration 數量；只可在 Registry 鎖內讀寫。</summary>
        public int ReferenceCount { get; set; }
    }

    /// <summary>
    /// 每次 Acquire 回傳的獨立 Registration。Interlocked 旗標確保同步／非同步 Dispose 競速時只釋放一次引用，
    /// 避免 ReferenceCount underflow、Manager 雙重 Dispose 或 Host Slot 過早釋放。
    /// </summary>
    private sealed class Registration : IOrganizationAdmissionRegistration
    {
        private readonly OrganizationAdmissionRegistry _owner;
        private readonly Entry _entry;
        private int _disposed;

        /// <summary>
        /// 建立一個由呼叫端唯一擁有的 Registration；Entry 引用計數已由 Registry 在同一鎖內增加。
        /// </summary>
        public Registration(OrganizationAdmissionRegistry owner, Entry entry)
        {
            _owner = owner;
            _entry = entry;
        }

        /// <summary>取得 Registration 綁定的不可變 Canonical 容量計畫；不得由 Profile 或 Request 在執行期間修改。</summary>
        public OrganizationAdmissionPlan Plan => _entry.Plan;

        /// <summary>
        /// 取得 Registry-owned Admission Manager。Registration 只借用此參考，不能直接 Dispose Manager；
        /// Manager 的最後清理必須由 Registry 引用計數歸零或 Registry Shutdown 統一執行。
        /// </summary>
        public IOrganizationAdmissionManager Manager => _entry.Manager;

        /// <summary>
        /// 同步釋放 Registration 並等待可能觸發的最後一個 Manager 非同步清理完成；
        /// 不會留下 fire-and-forget Host Slot release。
        /// </summary>
        public void Dispose()
            => RunSynchronously(DisposeAsync);

        /// <summary>
        /// 以 Interlocked 保證只釋放一次引用。Registry 已先 Shutdown 時會安全 no-op，
        /// 因為 Registry 已經統一擁有並等待該 Manager 的清理工作。
        /// </summary>
        public ValueTask DisposeAsync()
            => Interlocked.Exchange(ref _disposed, 1) == 0
                ? _owner.ReleaseAsync(_entry)
                : ValueTask.CompletedTask;
    }
}

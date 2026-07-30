// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Caching/SessionScopedResourceDisposalCoordinator.cs
// 所屬區塊：ChurchReport Session-owned 記憶體快取資源生命週期。
// 檔案責任：集中管理每個 Session resource generation 的 cache visibility、request lease、drain 與唯一 Dispose owner。
// 信任邊界：只保存應用程式產生的 bounded resource key 與 IDisposable；不得保存 HttpContext、Controller、Credential、Token 或使用者資料。
// 生命週期：singleton coordinator 在 host stop 時停止新 acquire；eviction 先撤銷可見世代，最後 lease 歸還後同步確定性清理。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與 final CRLF。
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace ChurchReport.Services.Caching
{
    /// <summary>
    /// 管理 Session-scoped、cache-owned 可釋放資源的 coordinator。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每個 resource key 具有獨立的 <see cref="ResourceSlot"/> 鎖與 current generation，因此某個 Session 的 factory、
    /// eviction 或 drain 不會取得全域鎖，也不會序列化其他 Session。slot 只在世代可見期間存在；一旦進入 drain 就從
    /// dictionary 移除，避免 singleton 永久保留 Session-derived key。舊世代本身可在 outstanding lease 中存活，
    /// 但已不可被新 request 取得。
    /// </para>
    /// <para>
    /// cache callback 使用 static method group，delegate 不捕捉 HttpContext、Controller 或 coordinator 的 DI graph；
    /// callback state 只保存 singleton coordinator，entry 則由 cache value 提供。顯式 eviction、TTL callback 與 host stop
    /// 都呼叫相同的 <see cref="BeginDrain"/> 狀態轉移，確保只有最後一個 cleanup owner 執行 Dispose。
    /// </para>
    /// </remarks>
    public sealed class SessionScopedResourceDisposalCoordinator<TResource> : IDisposable
        where TResource : class, IDisposable
    {
        private static readonly object RequestLeaseHolderKey = new object();
        private const string ResourceScopeSessionKey = "_DonationPaymentResourceScopeId";
        private static readonly object[] ResourceScopeCreationLocks =
            Enumerable.Range(0, 64).Select(static _ => new object()).ToArray();
        private readonly IMemoryCache _memoryCache;
        private readonly object _disposeGate = new object();
        private readonly ConcurrentDictionary<string, ResourceSlot> _slots =
            new ConcurrentDictionary<string, ResourceSlot>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<ResourceEntry, byte> _failedCleanupEntries =
            new ConcurrentDictionary<ResourceEntry, byte>();
        private int _activeEntryCount;
        private int _outstandingLeaseCount;
        private int _cleanupFailureCount;
        private int _disposeState;

        /// <summary>
        /// 建立由指定 <see cref="IMemoryCache"/> 支援的 singleton coordinator。
        /// </summary>
        /// <param name="memoryCache">由應用程式 host 擁有的 process-level memory cache；coordinator 不會 Dispose 它。</param>
        public SessionScopedResourceDisposalCoordinator(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        }

        /// <summary>
        /// 取得尚未完成最終清理的 generation 數量；供 health/lifecycle 測試確認 drain 後回到基準線。
        /// </summary>
        public int ActiveEntryCount => Volatile.Read(ref _activeEntryCount);

        /// <summary>
        /// 取得尚未歸還的 request lease 數量；此數值不包含 cache 自身的 visibility ownership。
        /// </summary>
        public int OutstandingLeaseCount => Volatile.Read(ref _outstandingLeaseCount);

        /// <summary>
        /// 取得 process lifetime 內 resource cleanup 失敗的累計次數；成功重試不會把歷史故障歸零。
        /// </summary>
        /// <remarks>
        /// 此計數不包含 key、Session 或例外內容，可安全供 health/測試判斷是否曾出現 retention 風險。
        /// 當數值增加時，對應 entry 仍由 failed-cleanup dictionary 強持有，直到後續 host Dispose 重試成功；
        /// 因此 <see cref="ActiveEntryCount"/> 只有在實際 cleanup 成功後才會歸零，不會產生假 baseline。
        /// </remarks>
        public int CleanupFailureCount => Volatile.Read(ref _cleanupFailureCount);

        /// <summary>
        /// 取得或建立目前 Session 的 256-bit 隨機 opaque resource scope。
        /// </summary>
        /// <param name="session">目前 request 的 ASP.NET Session；必須可用且具有非空 ID。</param>
        /// <returns>固定 43 字元 Base64Url scope；不包含 Session ID、使用者 ID、指紋、Token 或 Credential。</returns>
        /// <remarks>
        /// Session ID 只在方法內用來選擇 64 個 bounded stripe lock 之一；UTF-8 與 SHA-256 暫存 bytes
        /// 會在 finally 立即清零，不會保存於 coordinator、cache、log 或 metric。Lock 僅包住同一 Session
        /// 首次建立與身份重設的短區段，不會以全域 semaphore 序列化無關 Session。
        /// </remarks>
        public string GetOrCreateResourceScopeId(ISession session)
        {
            ValidateSession(session, "取得");
            var creationLock = GetResourceScopeCreationLock(session.Id);
            lock (creationLock)
            {
                var existingScopeId = session.GetString(ResourceScopeSessionKey);
                if (!string.IsNullOrEmpty(existingScopeId))
                {
                    ValidateResourceScopeId(existingScopeId);
                    return existingScopeId;
                }

                var randomBytes = RandomNumberGenerator.GetBytes(32);
                try
                {
                    var scopeId = Convert.ToBase64String(randomBytes)
                        .TrimEnd('=')
                        .Replace('+', '-')
                        .Replace('/', '_');
                    ValidateResourceScopeId(scopeId);
                    session.SetString(ResourceScopeSessionKey, scopeId);
                    var persistedScopeId = session.GetString(ResourceScopeSessionKey);
                    if (!string.Equals(scopeId, persistedScopeId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "無法確定性保存 Session resource scope。");
                    }

                    return scopeId;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(randomBytes);
                }
            }
        }

        /// <summary>
        /// 在登出或重新登入前先撤銷目前 Session 的 generation，再移除 opaque scope。
        /// </summary>
        /// <param name="session">尚未 Clear 的目前 Session。</param>
        /// <returns>原本有合法 scope 且已啟動 drain 時為 true；沒有 scope 時為 false。</returns>
        /// <remarks>
        /// Drain 與 scope 建立共享同一 stripe lock，因此身份重設期間不會 publish 同 scope 的新 generation。
        /// 既有 request lease 不會被強制中止；最後 lease 歸還者才 Dispose。scope 格式受污染時立即 fail closed，
        /// 不先 Clear Session，以保留可追蹤的 owner 邊界並避免遺留無法定位的資源。
        /// </remarks>
        public bool DrainSessionResourceScope(ISession session)
        {
            ValidateSession(session, "撤銷");
            var creationLock = GetResourceScopeCreationLock(session.Id);
            lock (creationLock)
            {
                var scopeId = session.GetString(ResourceScopeSessionKey);
                if (string.IsNullOrEmpty(scopeId))
                {
                    return false;
                }

                ValidateResourceScopeId(scopeId);
                var drained = EvictAndDrain(scopeId);
                session.Remove(ResourceScopeSessionKey);
                return drained;
            }
        }

        /// <summary>
        /// 取得或建立指定 resource key 的 current generation，並為 caller 建立一份冪等 lease。
        /// </summary>
        /// <typeparam name="T">由 generation 唯一 owner 最終釋放的資源型別。</typeparam>
        /// <param name="resourceKey">應用程式產生且具 Session 隔離的 bounded cache key；不得包含 Credential 或 Token。</param>
        /// <param name="factory">只在該 key 沒有 live generation 時執行的同步 factory。</param>
        /// <param name="absoluteExpiration">資源可見性的絕對上限。</param>
        /// <param name="slidingExpiration">每次 request acquire 可延長的滑動期限。</param>
        /// <returns>包含資源與唯一歸還權的 lease；caller 必須 Dispose，且可安全重複呼叫。</returns>
        /// <remarks>
        /// Factory 在 per-key slot lock 內執行，避免同一 Session 建立重複 Manager；不同 key 使用不同 slot，彼此不阻塞。
        /// 若 host stop 與 factory 競爭，factory 建立的資源會在 publish 前被同步釋放並 fail closed，不會成為孤兒。
        /// </remarks>
        public SessionScopedResourceLease<TResource> Acquire(
            string resourceScopeId,
            Func<TResource> factory,
            TimeSpan absoluteExpiration,
            TimeSpan slidingExpiration)
        {
            ValidateAcquireArguments(resourceScopeId, factory, absoluteExpiration, slidingExpiration);

            while (true)
            {
                ThrowIfDisposed();
                var slot = _slots.GetOrAdd(resourceScopeId, static _ => new ResourceSlot());
                ResourceEntry? entryToDispose = null;
                SessionScopedResourceLease<TResource>? acquiredLease = null;
                var retryWithFreshSlot = false;
                var rejectPublicationAfterCleanup = false;

                lock (slot.SyncRoot)
                {
                    // slot 可能已在 acquire 等待鎖時被上一個世代移除；重新讀 dictionary 可避免在失效 slot 上 publish 新資源。
                    if (!_slots.TryGetValue(resourceScopeId, out var registeredSlot) ||
                        !ReferenceEquals(slot, registeredSlot))
                    {
                        continue;
                    }

                    if (Volatile.Read(ref _disposeState) != 0)
                    {
                        // Host stop 可能在第一次檢查後介入；空 slot 不能把 Session-derived key 永久留在 singleton dictionary。
                        _slots.TryRemove(new KeyValuePair<string, ResourceSlot>(resourceScopeId, slot));
                        throw new ObjectDisposedException(nameof(SessionScopedResourceDisposalCoordinator<TResource>));
                    }

                    var entry = slot.Current;
                    if (entry != null &&
                        (!_memoryCache.TryGetValue(entry.CacheKey, out var cachedValue) ||
                         !ReferenceEquals(entry, cachedValue)))
                    {
                        // MemoryCache 可能已撤銷可見性，但 callback 尚在 ThreadPool 排隊；在新 acquire 前先同步完成同一 drain 轉移。
                        slot.Current = null;
                        _slots.TryRemove(new KeyValuePair<string, ResourceSlot>(resourceScopeId, slot));
                        entryToDispose = BeginDrain(entry);
                        entry = null;
                        // 此 slot 已從 dictionary 撤銷，無論舊 entry 能否立即 Dispose，都不得在同一 slot 發佈新世代。
                        // 退出鎖後先完成可立即執行的 cleanup，再回到迴圈從 _slots.GetOrAdd 取得已註冊的新 slot；
                        // 否則新 generation 只存在於區域 slot，後續 request、logout 與 host shutdown 都看不到它。
                        retryWithFreshSlot = true;
                    }

                    if (!retryWithFreshSlot && entryToDispose == null)
                    {
                        if (entry == null)
                        {
                            TResource resource;
                            try
                            {
                                resource = factory() ?? throw new InvalidOperationException(
                                    "Session resource factory 不得回傳 null。");
                            }
                            catch
                            {
                                // Factory fault 不能留下沒有 generation 的 slot，否則 singleton 會長期保留該 Session-derived key。
                                _slots.TryRemove(new KeyValuePair<string, ResourceSlot>(resourceScopeId, slot));
                                throw;
                            }

                            if (Volatile.Read(ref _disposeState) != 0)
                            {
                                // Factory 已把資源 ownership 交給 coordinator，但 host stop 在 cache publication 前介入。
                                // 仍要建立正式 entry／Active 計數並走同一 cleanup state machine；若 Dispose 失敗，
                                // failed-cleanup dictionary 會保留強參考供後續 host Dispose 重試，不能直接呼叫 resource.Dispose
                                // 後失去 owner。slot 先撤銷，避免 shutdown 執行緒把空 slot 誤認成 live generation。
                                _slots.TryRemove(new KeyValuePair<string, ResourceSlot>(resourceScopeId, slot));
                                entry = new ResourceEntry(resourceScopeId, resource);
                                Interlocked.Increment(ref _activeEntryCount);
                                entryToDispose = BeginDrain(entry)
                                    ?? throw new InvalidOperationException(
                                        "Pre-publication resource did not enter its unique cleanup-owner state.");
                                entry = null;
                                rejectPublicationAfterCleanup = true;
                            }

                            if (!rejectPublicationAfterCleanup)
                            {
                                entry = new ResourceEntry(resourceScopeId, resource);
                                slot.Current = entry;
                                Interlocked.Increment(ref _activeEntryCount);

                                try
                                {
                                    var options = new MemoryCacheEntryOptions()
                                        .SetAbsoluteExpiration(absoluteExpiration)
                                        .SetSlidingExpiration(slidingExpiration);
                                    options.RegisterPostEvictionCallback(CacheEntryEvicted, this);
                                    _memoryCache.Set(entry.CacheKey, entry, options);
                                }
                                catch
                                {
                                    slot.Current = null;
                                    _slots.TryRemove(new KeyValuePair<string, ResourceSlot>(resourceScopeId, slot));
                                    entryToDispose = BeginDrain(entry);
                                    entry = null;
                                    try
                                    {
                                        if (entryToDispose != null)
                                        {
                                            DisposeOwnedEntry(entryToDispose);
                                        }
                                    }
                                    catch
                                    {
                                        // Publish exception 是主要失敗；若補償 cleanup 也失敗，resource Dispose 例外較能指出 retention 風險。
                                        throw;
                                    }

                                    throw;
                                }
                            }
                        }

                        if (entry != null)
                        {
                            lock (entry.SyncRoot)
                            {
                                if (entry.State != ResourceEntryState.Live)
                                {
                                    slot.Current = null;
                                    _slots.TryRemove(new KeyValuePair<string, ResourceSlot>(resourceScopeId, slot));
                                }
                                else
                                {
                                    entry.LeaseCount++;
                                    Interlocked.Increment(ref _outstandingLeaseCount);
                                    acquiredLease = new SessionScopedResourceLease<TResource>(
                                        entry.Resource,
                                        () => Release(entry));
                                }
                            }
                        }
                    }
                }

                if (entryToDispose != null)
                {
                    DisposeOwnedEntry(entryToDispose);
                }

                if (rejectPublicationAfterCleanup)
                {
                    throw new ObjectDisposedException(nameof(SessionScopedResourceDisposalCoordinator<TResource>));
                }

                if (retryWithFreshSlot)
                {
                    continue;
                }

                if (acquiredLease != null)
                {
                    return acquiredLease;
                }
            }
        }

        /// <summary>
        /// 取得同一 HTTP request 共用的單一 lease，並把歸還動作綁到 response completion/disposal。
        /// </summary>
        /// <remarks>
        /// 多數 legacy Controller 會手動建立 InMemoryDataContext，不能假設 DI scoped disposal 一定發生。
        /// 因此第一個存取會把 lease 註冊到 <see cref="HttpResponse.OnCompleted(Func{Task})"/>，並以
        /// <see cref="HttpResponse.RegisterForDispose(IDisposable)"/> 作為中止/釋放備援。lease 本身使用 Interlocked 冪等歸還，
        /// 兩條 callback 同時觸發也只會讓 ref-count 減一。HttpContext 只持有 request-local holder；singleton coordinator
        /// 不會保存 HttpContext、Session、Controller 或 caller identity。
        /// </remarks>
        public SessionScopedResourceLease<TResource> AcquireForRequest(
            HttpContext httpContext,
            string resourceScopeId,
            Func<TResource> factory,
            TimeSpan absoluteExpiration,
            TimeSpan slidingExpiration)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            RequestLeaseHolder holder;
            lock (httpContext)
            {
                if (httpContext.Items[RequestLeaseHolderKey] is not RequestLeaseHolder existingHolder)
                {
                    existingHolder = new RequestLeaseHolder();
                    httpContext.Items[RequestLeaseHolderKey] = existingHolder;
                }

                holder = existingHolder;
            }

            var identity = new RequestResourceIdentity(this, resourceScopeId);
            lock (holder.SyncRoot)
            {
                if (holder.Leases.TryGetValue(identity, out var existingLease))
                {
                    return existingLease;
                }

                var lease = Acquire(resourceScopeId, factory, absoluteExpiration, slidingExpiration);
                holder.Leases.Add(identity, lease);

                try
                {
                    httpContext.Response.OnCompleted(
                        static state =>
                        {
                            ((IDisposable)state).Dispose();
                            return Task.CompletedTask;
                        },
                        lease);
                    httpContext.Response.RegisterForDispose(lease);
                }
                catch
                {
                    holder.Leases.Remove(identity);
                    lease.Dispose();
                    throw;
                }

                return lease;
            }
        }

        /// <summary>
        /// 以目前 ASP.NET Session 為身份重設同步邊界，取得 scope 並完成 request lease publication。
        /// </summary>
        /// <param name="httpContext">目前 request；只用來保存 request-local lease 與註冊 response cleanup。</param>
        /// <param name="session">目前可用 Session；opaque scope 只存在 Session 內，不會複製到 coordinator 的身份索引。</param>
        /// <param name="factory">沒有 live generation 時建立資源的同步 factory。</param>
        /// <param name="absoluteExpiration">cache 可見性的絕對存活上限。</param>
        /// <param name="slidingExpiration">成功 acquire 時延長的滑動期限。</param>
        /// <returns>已綁定目前 request cleanup 的 generation lease。</returns>
        /// <remarks>
        /// <para>
        /// 本方法與 <see cref="DrainSessionResourceScope"/> 使用相同的 bounded stripe lock，並把鎖持有到
        /// <see cref="AcquireForRequest"/> 已完成 slot/cache/lease publication。這使 logout/re-login 不可能在舊 request
        /// 讀到 scope 後、generation 尚未可見前先完成 drain；身份重設一旦取得 stripe，就一定能看見並撤銷所有先前 acquire。
        /// </para>
        /// <para>
        /// Factory 不接受 cancellation，因為既有 Manager 建構式為同步；若 factory 很慢，同一 Session 或碰撞到同一 stripe
        /// 的 request 會短暫排隊。固定 64 個 lock 避免無界 per-session lock retention，而不同 stripe 仍可平行建立。
        /// Factory/response registration 失敗會由既有 Acquire rollback 歸還 lease 或 Dispose 未發佈資源，例外直接向 caller 傳遞，
        /// 不會清 Session、記錄 scope 或建立 fallback owner。
        /// </para>
        /// </remarks>
        public SessionScopedResourceLease<TResource> AcquireForSessionRequest(
            HttpContext httpContext,
            ISession session,
            Func<TResource> factory,
            TimeSpan absoluteExpiration,
            TimeSpan slidingExpiration)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ValidateSession(session, "取得");
            var creationLock = GetResourceScopeCreationLock(session.Id);
            lock (creationLock)
            {
                // Monitor 允許同一執行緒重入；GetOrCreate 仍走唯一驗證／隨機值／持久化確認實作，
                // 同時外層鎖把 scope 讀取到 lease publication 形成單一 identity-reset 線性化區段。
                var resourceScopeId = GetOrCreateResourceScopeId(session);
                return AcquireForRequest(
                    httpContext,
                    resourceScopeId,
                    factory,
                    absoluteExpiration,
                    slidingExpiration);
            }
        }

        /// <summary>
        /// 撤銷指定 key 的 current generation，並在最後 request lease 歸還時執行最終 Dispose。
        /// </summary>
        /// <returns>找到 live generation 並啟動 drain 時為 true；已被其他 cleanup owner 處理時為 false。</returns>
        public bool EvictAndDrain(string resourceKey)
        {
            ValidateResourceScopeId(resourceKey);

            if (!_slots.TryGetValue(resourceKey, out var slot))
            {
                // 「找不到 slot」就是本次 no-op drain 的線性化點。不可再依相同 key 呼叫 cache.Remove：
                // 另一個 request 可能在線性化點之後已建立 slot 並發佈新 generation，而無條件 Remove 會誤刪
                // 那個較新的世代。所有正式 cache entry 在發佈前一定先擁有相同 slot，因此此分支沒有孤立 cache
                // 需要補償清理；直接返回才能維持 generation ownership 與新 request 可見性的先後關係。
                return false;
            }

            ResourceEntry? entry;
            ResourceEntry? entryToDispose;
            lock (slot.SyncRoot)
            {
                if (!_slots.TryGetValue(resourceKey, out var registeredSlot) ||
                    !ReferenceEquals(slot, registeredSlot) ||
                    slot.Current == null)
                {
                    _slots.TryRemove(new KeyValuePair<string, ResourceSlot>(resourceKey, slot));
                    return false;
                }

                entry = slot.Current;
                slot.Current = null;
                entryToDispose = BeginDrain(entry);

                // 必須在 slot 仍阻止同 key 新世代 publish 時完成 cache Remove；否則舊世代的 Remove 可能誤刪剛建立的新世代。
                _memoryCache.Remove(entry.CacheKey);
                _slots.TryRemove(new KeyValuePair<string, ResourceSlot>(resourceKey, slot));
            }

            if (entryToDispose != null)
            {
                DisposeOwnedEntry(entryToDispose);
            }

            return entry != null;
        }

        /// <summary>
        /// Host shutdown 的同步 drain 入口。先原子停止新 acquire，再撤銷所有 current generation；
        /// 已在執行的 request 不會被阻塞或強制取消，最後 lease 歸還者仍是其 generation 的 cleanup owner。
        /// </summary>
        public void Dispose()
        {
            lock (_disposeGate)
            {
                // 第一次進入先終止新 acquire；後續 Dispose 仍需進入 gate，才能重試先前失敗且仍由本 coordinator
                // 強持有的 cleanup entry。同步 gate 讓 cache callback、host Stop 與 DI container 重複 Dispose
                // 不會同時對同一 resource 執行 retry；所有例外在完成本輪其他 entry 後聚合回傳。
                Interlocked.Exchange(ref _disposeState, 1);

                List<Exception>? cleanupFailures = null;

                // 只重試本輪開始前就已失敗的 entry。若稍後 drain current slot 才第一次失敗，保留給下一次
                // host/DI Dispose，避免同一呼叫無界重試永久失敗的 IDisposable。
                foreach (var failedEntry in _failedCleanupEntries.Keys.ToArray())
                {
                    if (!TryBeginCleanupRetry(failedEntry))
                    {
                        continue;
                    }

                    try
                    {
                        DisposeOwnedEntry(failedEntry);
                    }
                    catch (Exception exception)
                    {
                        (cleanupFailures ??= new List<Exception>()).Add(exception);
                    }
                }

                foreach (var resourceKey in _slots.Keys.ToArray())
                {
                    try
                    {
                        EvictAndDrain(resourceKey);
                    }
                    catch (Exception exception)
                    {
                        // 一個 generation cleanup 失敗不能阻止其他 Session 的資源撤銷與釋放；全部 drain 後再聚合回報。
                        (cleanupFailures ??= new List<Exception>()).Add(exception);
                    }
                }

                if (cleanupFailures != null)
                {
                    throw new AggregateException(
                        "一或多個 Session-scoped resource 在 host drain 時清理失敗。",
                        cleanupFailures);
                }
            }
        }

        private static void ValidateAcquireArguments(
            string resourceScopeId,
            Func<TResource> factory,
            TimeSpan absoluteExpiration,
            TimeSpan slidingExpiration)
        {
            ValidateResourceScopeId(resourceScopeId);
            ArgumentNullException.ThrowIfNull(factory);
            if (absoluteExpiration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(absoluteExpiration));
            }

            if (slidingExpiration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(slidingExpiration));
            }
        }

        private static void ValidateSession(ISession session, string operation)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (!session.IsAvailable || string.IsNullOrWhiteSpace(session.Id))
            {
                throw new InvalidOperationException(
                    $"Session resource scope 必須在可用的 ASP.NET Session request 中{operation}。");
            }
        }

        /// <summary>
        /// 以 Session ID 的 SHA-256 第一個位元組選擇固定 stripe；原始 ID、UTF-8 bytes 與 hash 都不離開方法。
        /// 固定 64 個 lock 避免建立無界 per-session lock dictionary；偶發 stripe 碰撞只造成極短建立／drain 排隊，
        /// 不會讓不同 Session 共用 scope、resource 或 authorization state。
        /// </summary>
        private static object GetResourceScopeCreationLock(string sessionId)
        {
            var sessionIdBytes = Encoding.UTF8.GetBytes(sessionId);
            var sessionIdHash = SHA256.HashData(sessionIdBytes);
            try
            {
                return ResourceScopeCreationLocks[
                    sessionIdHash[0] % ResourceScopeCreationLocks.Length];
            }
            finally
            {
                CryptographicOperations.ZeroMemory(sessionIdBytes);
                CryptographicOperations.ZeroMemory(sessionIdHash);
            }
        }

        /// <summary>
        /// Coordinator 自身強制 opaque scope 邊界，避免任一 caller 繞過 Session owner 而把可識別複合 key 留在 singleton/cache。
        /// </summary>
        private static void ValidateResourceScopeId(string resourceScopeId)
        {
            if (string.IsNullOrWhiteSpace(resourceScopeId) ||
                resourceScopeId.Length != 43 ||
                resourceScopeId.Any(static value =>
                    !(value >= 'A' && value <= 'Z') &&
                    !(value >= 'a' && value <= 'z') &&
                    !(value >= '0' && value <= '9') &&
                    value != '-' &&
                    value != '_'))
            {
                throw new ArgumentException(
                    "Session resource scope 必須是固定長度的 opaque Base64Url 值。",
                    nameof(resourceScopeId));
            }
        }

        /// <summary>
        /// IMemoryCache 的 static callback；不得改為捕捉 lambda。state 僅為 singleton coordinator，value 僅為 bounded entry。
        /// Callback 執行緒沒有 request error channel，因此 cleanup 例外只寫入不含 key/value 的 Trace，避免洩漏 Session identity。
        /// </summary>
        private static void CacheEntryEvicted(object key, object? value, EvictionReason reason, object? state)
        {
            if (state is SessionScopedResourceDisposalCoordinator<TResource> coordinator && value is ResourceEntry entry)
            {
                coordinator.OnCacheEntryEvicted(entry);
            }
        }

        private void OnCacheEntryEvicted(ResourceEntry entry)
        {
            if (_slots.TryGetValue(entry.ResourceScopeId, out var slot))
            {
                lock (slot.SyncRoot)
                {
                    if (ReferenceEquals(slot.Current, entry))
                    {
                        slot.Current = null;
                        _slots.TryRemove(new KeyValuePair<string, ResourceSlot>(entry.ResourceScopeId, slot));
                    }
                }
            }

            var entryToDispose = BeginDrain(entry);
            if (entryToDispose == null)
            {
                return;
            }

            try
            {
                DisposeOwnedEntry(entryToDispose);
            }
            catch (Exception exception)
            {
                // Cache callback 沒有可安全傳回的 caller；只記錄例外型別，不記 resource key、Session 或 resource 內容。
                System.Diagnostics.Trace.TraceError(
                    "Session-scoped cache resource cleanup failed with {0}.",
                    exception.GetType().FullName);
            }
        }

        private ResourceEntry? BeginDrain(ResourceEntry entry)
        {
            lock (entry.SyncRoot)
            {
                if (entry.State == ResourceEntryState.Live)
                {
                    entry.State = ResourceEntryState.Draining;
                }

                if (entry.State == ResourceEntryState.Draining && entry.LeaseCount == 0)
                {
                    entry.State = ResourceEntryState.CleanupInProgress;
                    return entry;
                }

                return null;
            }
        }

        private void Release(ResourceEntry entry)
        {
            ResourceEntry? entryToDispose = null;
            lock (entry.SyncRoot)
            {
                if (entry.LeaseCount <= 0)
                {
                    throw new InvalidOperationException("Session resource lease ref-count 發生 underflow。");
                }

                entry.LeaseCount--;
                Interlocked.Decrement(ref _outstandingLeaseCount);
                if (entry.LeaseCount == 0 && entry.State == ResourceEntryState.Draining)
                {
                    entry.State = ResourceEntryState.CleanupInProgress;
                    entryToDispose = entry;
                }
            }

            if (entryToDispose != null)
            {
                DisposeOwnedEntry(entryToDispose);
            }
        }

        private void DisposeOwnedEntry(ResourceEntry entry)
        {
            try
            {
                entry.Resource.Dispose();
            }
            catch
            {
                lock (entry.SyncRoot)
                {
                    if (entry.State == ResourceEntryState.CleanupInProgress)
                    {
                        entry.State = ResourceEntryState.CleanupFailed;
                    }
                }

                _failedCleanupEntries.TryAdd(entry, 0);
                Interlocked.Increment(ref _cleanupFailureCount);
                throw;
            }

            lock (entry.SyncRoot)
            {
                if (entry.State != ResourceEntryState.CleanupInProgress)
                {
                    throw new InvalidOperationException(
                        "Session resource cleanup completed outside the unique cleanup-owner state.");
                }

                entry.State = ResourceEntryState.Disposed;
            }

            _failedCleanupEntries.TryRemove(entry, out _);
            Interlocked.Decrement(ref _activeEntryCount);
        }

        /// <summary>
        /// 讓後續 host Dispose 對先前失敗的 entry 取得唯一 retry ownership。
        /// CleanupFailed 以外的狀態代表 callback、lease 或另一個 Dispose 已經擁有／完成清理，本 caller 必須 no-op。
        /// </summary>
        private static bool TryBeginCleanupRetry(ResourceEntry entry)
        {
            lock (entry.SyncRoot)
            {
                if (entry.State != ResourceEntryState.CleanupFailed)
                {
                    return false;
                }

                entry.State = ResourceEntryState.CleanupInProgress;
                return true;
            }
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        }

        private sealed class ResourceSlot
        {
            public object SyncRoot { get; } = new object();

            public ResourceEntry? Current { get; set; }
        }

        private sealed class ResourceEntry
        {
            public ResourceEntry(string resourceScopeId, TResource resource)
            {
                ResourceScopeId = resourceScopeId;
                CacheKey = new ResourceCacheKey(resourceScopeId);
                Resource = resource;
            }

            public object SyncRoot { get; } = new object();

            public string ResourceScopeId { get; }

            public ResourceCacheKey CacheKey { get; }

            public TResource Resource { get; }

            public int LeaseCount { get; set; }

            public ResourceEntryState State { get; set; }
        }

        private enum ResourceEntryState
        {
            Live,
            Draining,
            CleanupInProgress,
            CleanupFailed,
            Disposed
        }

        /// <summary>
        /// IMemoryCache 的型別化 key。不同 closed generic coordinator 即使收到相同 opaque scope ID，
        /// 也不會互相覆寫或取得錯誤資源型別；字串本身仍不包含 Session/user identity。
        /// </summary>
        private readonly record struct ResourceCacheKey(string ResourceScopeId);

        private sealed class RequestLeaseHolder
        {
            public object SyncRoot { get; } = new object();

            public Dictionary<RequestResourceIdentity, SessionScopedResourceLease<TResource>> Leases { get; } =
                new Dictionary<RequestResourceIdentity, SessionScopedResourceLease<TResource>>();
        }

        private readonly record struct RequestResourceIdentity(
            SessionScopedResourceDisposalCoordinator<TResource> Coordinator,
            string ResourceScopeId);
    }

    /// <summary>
    /// 一個 request 對特定 Session resource generation 的冪等 lease。
    /// </summary>
    /// <typeparam name="T">被保護且不得在 lease 存活期間清理的資源型別。</typeparam>
    /// <remarks>
    /// Lease 最長只存活到目前 HTTP response completion/disposal，不保存 HttpContext、Session、Token 或 Credential。
    /// OnCompleted 與 RegisterForDispose 可能同時呼叫 <see cref="Dispose"/>；Interlocked.Exchange 保證 release action 精確一次。
    /// </remarks>
    public sealed class SessionScopedResourceLease<T> : IDisposable
        where T : class, IDisposable
    {
        private Action? _release;
        private T? _value;

        internal SessionScopedResourceLease(T value, Action release)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
            _release = release ?? throw new ArgumentNullException(nameof(release));
        }

        /// <summary>
        /// 取得本 lease 保護的 resource；caller 不擁有它，不得直接 Dispose。
        /// </summary>
        public T Value => Volatile.Read(ref _value)
            ?? throw new ObjectDisposedException(nameof(SessionScopedResourceLease<T>));

        /// <summary>
        /// 歸還 generation ref-count；重複或併發呼叫只有第一個 caller 執行 release。
        /// </summary>
        public void Dispose()
        {
            var release = Interlocked.Exchange(ref _release, null);
            if (release == null)
            {
                return;
            }

            // 先切斷 lease 對 resource 的強參考，再歸還 ref-count；即使 response feature 暫時保留已 Dispose lease，
            // 它也不會把已清理的 Manager/HttpClient/Semaphore graph 一起保留到下一個 request。
            Interlocked.Exchange(ref _value, null);
            release();
        }
    }
}

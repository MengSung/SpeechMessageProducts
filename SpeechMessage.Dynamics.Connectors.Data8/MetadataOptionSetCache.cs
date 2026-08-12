// ============================================================================
// 檔案：SpeechMessage.Dynamics.Connectors.Data8/MetadataOptionSetCache.cs
// 用途：提供由單一 Data8 runtime 唯一擁有的有界 metadata OptionSet 純值快取。
// ============================================================================

using System.Text;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// metadata OptionSet 快取的完整、不可變隔離鍵。<see cref="ProfileAlias"/> 與 <see cref="GenerationId"/>
/// 必須來自已驗證的 <c>ResolvedProfile</c>；<see cref="Target"/> 與 <see cref="ServerResolvedLocale"/> 則只能
/// 由 connector 的封閉 allowlist 與伺服器端 metadata projection 決定。瀏覽器、ProductClient 或 caller supplied
/// JSON 不得把這些欄位當成路由 authority。
/// </summary>
/// <remarks>
/// alias 採 trim + ordinal invariant upper-case 正規化，使既有 profile resolver 的大小寫不敏感語意在 dictionary
/// key equality 中保持一致。此型別只保存隔離所需的 primitive 值，絕不保存 <c>ResolvedProfile</c>、credential、
/// endpoint、SDK metadata graph、request、session 或使用者資料；呼叫端亦不得將 key 寫入 log 或產品回應。
/// </remarks>
public readonly record struct MetadataOptionSetCacheKey
{
    /// <summary>
    /// 建立完整 metadata cache isolation key，並立即拒絕缺少 alias、非正 generation、未知 target 或非正的
    /// server-resolved locale。這防止不完整 key 混合不同 Data8 runtime generation 的 metadata。
    /// </summary>
    /// <param name="profileAlias">從已驗證 profile snapshot 取得的 alias，而非 caller routing input。</param>
    /// <param name="generationId">從已驗證 profile snapshot 取得、必須大於零的 generation。</param>
    /// <param name="target">connector private allowlist 已認可的封閉 metadata target。</param>
    /// <param name="serverResolvedLocale">由伺服器解析、必須大於零的 metadata locale identifier。</param>
    public MetadataOptionSetCacheKey(
        string profileAlias,
        long generationId,
        MetadataOptionSetTarget target,
        int serverResolvedLocale)
    {
        if (string.IsNullOrWhiteSpace(profileAlias))
        {
            throw new ArgumentException("A profile alias is required.", nameof(profileAlias));
        }

        if (generationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generationId));
        }

        if (!Enum.IsDefined(target))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }

        if (serverResolvedLocale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(serverResolvedLocale));
        }

        // Resolver/registry 對 alias 使用 ordinal case-insensitive lookup。明確 canonicalize 可避免同一 runtime
        // 因 caller 外觀大小寫不同形成第二個 cache partition；真正 authority 仍在上游 resolver。
        ProfileAlias = profileAlias.Trim().ToUpperInvariant();
        GenerationId = generationId;
        Target = target;
        ServerResolvedLocale = serverResolvedLocale;
    }

    /// <summary>取得已正規化、不可為空的 profile alias；僅供 cache partition，禁止寫入診斷或產品回應。</summary>
    public string ProfileAlias { get; }

    /// <summary>取得已驗證且大於零的 Data8 runtime generation；generation replacement 一律使用不同 key。</summary>
    public long GenerationId { get; }

    /// <summary>取得封閉 metadata target；此 enum 不攜帶 CRM entity 或 attribute string。</summary>
    public MetadataOptionSetTarget Target { get; }

    /// <summary>取得伺服器端已解析的 locale identifier；它不是 HTTP/IPC caller 可任意選擇的欄位。</summary>
    public int ServerResolvedLocale { get; }
}

/// <summary>
/// 由 Data8 runtime 唯一擁有的 bounded metadata OptionSet pure-value cache。
/// 不啟動 timer、背景 task、lease、client、connection 或 subscription；TTL 僅在同步讀取與 Store 入口檢查，
/// runtime owner 在 Dispose 時呼叫 <see cref="Dispose"/> 即可確定釋放全部
/// retained metadata reference。此類別不可註冊成 process-wide 或 static cache。
/// </summary>
/// <remarks>
/// 每筆 value 僅是新建 <see cref="OptionSetOptionRecord"/> pure values 與 private read-only collection，不保存
/// CRM <c>AttributeMetadata</c>/<c>OptionMetadata</c> graph、<c>JsonElement</c>、request、user、profile object、
/// credential、token、cookie、endpoint、logger、cache-key log 或原始 upstream response。單一 lock 只保護有界
/// dictionary 的本機 eviction，不包住 CRM I/O，因此 thread safe 而不會全域序列化遠端呼叫。
/// </remarks>
public sealed class MetadataOptionSetCache : IDisposable
{
    private const int MaximumOptionsPerEntry = 1_024;
    private const int MaximumLabelCharacters = 512;
    private const int EntryFixedByteCost = 96;
    private const int OptionFixedByteCost = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly object _gate = new();
    private readonly Dictionary<MetadataOptionSetCacheKey, CacheEntry> _entries = [];
    private readonly Dictionary<MetadataOptionSetRuntimeTargetKey, MetadataOptionSetCacheKey> _latestKeys = [];
    private readonly int _maximumEntryCount;
    private readonly int _maximumByteCount;
    private readonly TimeSpan _timeToLive;
    private readonly TimeProvider _timeProvider;
    private int _retainedByteCount;
    private long _nextStoreSequence;
    private bool _disposed;

    /// <summary>
    /// 建立有固定 TTL 與雙重容量上限的 cache。<paramref name="timeProvider"/> 是唯一時間來源，讓 runtime 可以
    /// 在不建立 timer 的前提下測試到期邊界；host 使用 <see cref="TimeProvider.System"/> 時仍由 runtime Dispose
    /// 作為唯一長生命週期 cleanup owner。
    /// </summary>
    /// <param name="maximumEntryCount">可保留的最大完整隔離鍵數量，必須為正。</param>
    /// <param name="maximumByteCount">估算的最大 retained pure-value byte 數量，必須為正。</param>
    /// <param name="timeToLive">每筆短生命週期 TTL，必須為正且有限。</param>
    /// <param name="timeProvider">可信時間來源；不保存 request/session 或任何 caller state。</param>
    public MetadataOptionSetCache(
        int maximumEntryCount,
        int maximumByteCount,
        TimeSpan timeToLive,
        TimeProvider? timeProvider = null)
    {
        if (maximumEntryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntryCount));
        }

        if (maximumByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumByteCount));
        }

        if (timeToLive <= TimeSpan.Zero || timeToLive == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
        }

        _maximumEntryCount = maximumEntryCount;
        _maximumByteCount = maximumByteCount;
        _timeToLive = timeToLive;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 讀取完整隔離鍵相符且尚未到期的 immutable option snapshot。miss、過期或已釋放 cache 都回傳
    /// <see langword="false"/>；不會 fallback 到另一個 alias/generation/locale，也不會呼叫 CRM 或建立 lease。
    /// </summary>
    /// <param name="key">由目前已驗證 Data8 runtime 與 server metadata projection 建立的完整隔離鍵。</param>
    /// <param name="options">命中時的 private immutable snapshot；否則為 <see langword="null"/>。</param>
    /// <returns>只有精確鍵命中且尚未到期時才為 <see langword="true"/>。</returns>
    public bool TryGet(MetadataOptionSetCacheKey key, out IReadOnlyList<OptionSetOptionRecord>? options)
    {
        options = null;
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            RemoveExpiredEntriesLocked(now);
            if (!_entries.TryGetValue(key, out var entry))
            {
                return false;
            }

            options = entry.Options;
            return true;
        }
    }

    /// <summary>
    /// 以目前 runtime 已知、由伺服器 metadata projection 證實的 locale 讀取快取。此 overload 不接受 caller locale：
    /// 它先以 alias/generation/target 找到最近一次成功 projection 所建立的完整 key，才會呼叫完整 key 版本。
    /// 若尚無可信 locale、entry 已過期或 runtime 已釋放，一律回傳 miss；不會猜測主機語系、HTTP Accept-Language
    /// 或另一個 profile/generation 的 locale。
    /// </summary>
    /// <param name="profileAlias">已驗證 profile snapshot 的 alias，僅用於 runtime partition。</param>
    /// <param name="generationId">已驗證 profile snapshot 的正 generation。</param>
    /// <param name="target">connector allowlist 的封閉 metadata target。</param>
    /// <param name="options">命中時的 private immutable snapshot；否則為 <see langword="null"/>。</param>
    /// <returns>只有完整 runtime target 已映射至未到期 server locale entry 時才為 <see langword="true"/>。</returns>
    public bool TryGet(
        string profileAlias,
        long generationId,
        MetadataOptionSetTarget target,
        out IReadOnlyList<OptionSetOptionRecord>? options)
    {
        options = null;
        if (!TryCreateRuntimeTargetKey(profileAlias, generationId, target, out var runtimeTarget))
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            RemoveExpiredEntriesLocked(now);
            if (!_latestKeys.TryGetValue(runtimeTarget, out var key) ||
                !_entries.TryGetValue(key, out var entry))
            {
                return false;
            }

            options = entry.Options;
            return true;
        }
    }

    /// <summary>
    /// 將完整成功的 metadata projection 以 defensive copy 存入 cache。空集合、invalid option、超過單筆 byte
    /// budget、不可表示的到期時間或已釋放 cache 都 fail closed 並回傳 <see langword="false"/>；此方法不快取
    /// failure、partial result、SDK graph 或 caller collection。
    /// </summary>
    /// <param name="key">由 server-validated profile/generation/target/locale 組成的完整隔離鍵。</param>
    /// <param name="options">已完成 CRM projection 的 pure-value collection；完成後不與 caller list 共用。</param>
    /// <returns>資料成功存入有界 cache 時為 <see langword="true"/>；不適合快取時為 <see langword="false"/>。</returns>
    public bool Store(MetadataOptionSetCacheKey key, IReadOnlyList<OptionSetOptionRecord>? options)
    {
        if (!TryCreateSnapshot(key, options, out var snapshot, out var byteCount) || snapshot is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        DateTimeOffset expiresAtUtc;
        try
        {
            expiresAtUtc = now.Add(_timeToLive);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed || byteCount > _maximumByteCount)
            {
                return false;
            }

            RemoveExpiredEntriesLocked(now);
            if (_entries.TryGetValue(key, out var existing))
            {
                RemoveEntryLocked(key, existing);
            }

            var runtimeTarget = MetadataOptionSetRuntimeTargetKey.From(key);
            if (_latestKeys.TryGetValue(runtimeTarget, out var latestKey) && latestKey != key &&
                _entries.TryGetValue(latestKey, out var latestEntry))
            {
                // 同一 runtime/target 的新 server locale 代表舊投影不可再作為「最新」回應來源。移除舊 entry
                // 而非只改 reverse index，可限制多 locale metadata label 在同 generation 無界累積，也避免未來
                // 某個 direct-key caller 誤讀過時的 UI language snapshot。
                RemoveEntryLocked(latestKey, latestEntry);
            }

            while (_entries.Count >= _maximumEntryCount || _retainedByteCount > _maximumByteCount - byteCount)
            {
                if (!TryRemoveOldestEntryLocked())
                {
                    return false;
                }
            }

            _entries.Add(key, new CacheEntry(snapshot, byteCount, expiresAtUtc, NextStoreSequenceLocked()));
            _retainedByteCount += byteCount;
            _latestKeys[runtimeTarget] = key;
            return true;
        }
    }

    /// <summary>
    /// 清除目前 runtime 擁有的全部 entry。這不會觸及 connector pool、lease、client 或其他 runtime，因此 generation
    /// retirement owner 可在自己已證明的 replace/drain 順序中明確呼叫它，不會錯誤釋放共享 Organization admission。
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                ClearLocked();
            }
        }
    }

    /// <summary>
    /// 以 idempotent 方式終止 cache lifetime。釋放後讀寫皆 fail closed，且 dictionary 不再保留 option label 或
    /// profile/generation key；不需要 asynchronous cleanup，因為本類別刻意沒有 timer、I/O、lease 或 handle。
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ClearLocked();
        }
    }

    /// <summary>
    /// 驗證並複製 caller projection 成 private immutable snapshot。string 在 .NET 為 immutable，可安全重用文字值；
    /// record 與 collection 則必定新建並重新驗證，避免 caller mutable list、raw SDK graph 或 empty/partial result
    /// 進入長生命週期 retained state。
    /// </summary>
    private static bool TryCreateSnapshot(
        MetadataOptionSetCacheKey key,
        IReadOnlyList<OptionSetOptionRecord>? source,
        out IReadOnlyList<OptionSetOptionRecord>? snapshot,
        out int byteCount)
    {
        snapshot = null;
        byteCount = 0;
        if (source is null || source.Count is <= 0 or > MaximumOptionsPerEntry)
        {
            return false;
        }

        var copied = new OptionSetOptionRecord[source.Count];
        var values = new HashSet<int>();
        var configuredOrders = new HashSet<int>();
        var estimatedBytes = EntryFixedByteCost;
        if (!TryAddUtf8Bytes(ref estimatedBytes, key.ProfileAlias) ||
            !TryAddBytes(ref estimatedBytes, sizeof(long) + sizeof(int) + sizeof(int)))
        {
            return false;
        }

        for (var index = 0; index < source.Count; index++)
        {
            var option = source[index];
            if (option is null ||
                string.IsNullOrWhiteSpace(option.Label) ||
                option.Label.Length > MaximumLabelCharacters ||
                option.ConfiguredOrder < 0 ||
                !values.Add(option.Value) ||
                !configuredOrders.Add(option.ConfiguredOrder) ||
                !TryAddBytes(ref estimatedBytes, OptionFixedByteCost) ||
                !TryAddUtf8Bytes(ref estimatedBytes, option.Label))
            {
                return false;
            }

            copied[index] = new OptionSetOptionRecord
            {
                Value = option.Value,
                Label = option.Label,
                ConfiguredOrder = option.ConfiguredOrder
            };
        }

        // ReadOnlyCollection 包住此 private array；IReadOnlyList 呼叫端不能改寫，IList 寫入 API 則會丟出例外。
        snapshot = Array.AsReadOnly(copied);
        byteCount = estimatedBytes;
        return true;
    }

    /// <summary>以 strict UTF-8 計算字串成本；invalid surrogate 或 overflow 都讓 Store fail closed，不保留半筆資料。</summary>
    private static bool TryAddUtf8Bytes(ref int totalBytes, string value)
    {
        try
        {
            return TryAddBytes(ref totalBytes, StrictUtf8.GetByteCount(value));
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>以 checked 算術累加 cache 成本，避免超大 label/collection 造成負數繞回而失去 byte cap。</summary>
    private static bool TryAddBytes(ref int totalBytes, int additionalBytes)
    {
        try
        {
            totalBytes = checked(totalBytes + additionalBytes);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>移除 TTL 已到期 entry；無 timer，故 cleanup 只在正常存取路徑、受同一 lock 保護時發生。</summary>
    private void RemoveExpiredEntriesLocked(DateTimeOffset now)
    {
        List<MetadataOptionSetCacheKey>? expired = null;
        foreach (var pair in _entries)
        {
            if (now >= pair.Value.ExpiresAtUtc)
            {
                (expired ??= []).Add(pair.Key);
            }
        }

        if (expired is not null)
        {
            foreach (var key in expired)
            {
                RemoveEntryLocked(key, _entries[key]);
            }
        }
    }

    /// <summary>移除 Store sequence 最小的 entry；sequence 唯一，故 entry/byte cap eviction 在所有執行下都可預期。</summary>
    private bool TryRemoveOldestEntryLocked()
    {
        var found = false;
        var oldestKey = default(MetadataOptionSetCacheKey);
        CacheEntry? oldestEntry = null;
        foreach (var pair in _entries)
        {
            if (!found || pair.Value.StoreSequence < oldestEntry!.StoreSequence)
            {
                found = true;
                oldestKey = pair.Key;
                oldestEntry = pair.Value;
            }
        }

        if (!found)
        {
            return false;
        }

        RemoveEntryLocked(oldestKey, oldestEntry!);
        return true;
    }

    /// <summary>移除已確認 entry 並同步回收估算 byte 成本；不留下空 key 或 stale label reference。</summary>
    private void RemoveEntryLocked(MetadataOptionSetCacheKey key, CacheEntry entry)
    {
        if (_entries.Remove(key))
        {
            _retainedByteCount -= entry.ByteCount;
            var runtimeTarget = MetadataOptionSetRuntimeTargetKey.From(key);
            if (_latestKeys.TryGetValue(runtimeTarget, out var latestKey) && latestKey == key)
            {
                _latestKeys.Remove(runtimeTarget);
            }
        }
    }

    /// <summary>清空 owner 管理的全部 retained reference，重設容量與淘汰順序，且不建立延遲 cleanup 工作。</summary>
    private void ClearLocked()
    {
        _entries.Clear();
        _latestKeys.Clear();
        _retainedByteCount = 0;
        _nextStoreSequence = 0;
    }

    /// <summary>
    /// 取得不會 overflow 的 deterministic Store sequence。極端溢位時清空有界 cache 後從一重新排序；此情形只造成
    /// 安全 cache miss，絕不讓 sequence wraparound 產生不確定 eviction 或跨 profile/generation 資料混合。
    /// </summary>
    private long NextStoreSequenceLocked()
    {
        if (_nextStoreSequence == long.MaxValue)
        {
            ClearLocked();
        }

        return ++_nextStoreSequence;
    }

    /// <summary>
    /// 保存單筆 private immutable projection 與 bounded accounting。它不公開、不保存 caller collection、SDK object、
    /// lease 或其他 runtime resource；entry 被 eviction/Clear/Dispose 移除後只由 GC 回收 pure managed values。
    /// </summary>
    private sealed record CacheEntry(
        IReadOnlyList<OptionSetOptionRecord> Options,
        int ByteCount,
        DateTimeOffset ExpiresAtUtc,
        long StoreSequence);

    /// <summary>
    /// 代表不含 locale 的 runtime-local lookup key。它只存在於 cache 私有 dictionary，並且只能由完整 key
    /// 削減而來；因此 executor 的 lookup 沒有路徑能自行提供或持久化猜測的 locale。
    /// </summary>
    private readonly record struct MetadataOptionSetRuntimeTargetKey(
        string ProfileAlias,
        long GenerationId,
        MetadataOptionSetTarget Target)
    {
        /// <summary>從已驗證完整 key 取得 runtime-local lookup key。</summary>
        public static MetadataOptionSetRuntimeTargetKey From(MetadataOptionSetCacheKey key)
            => new(key.ProfileAlias, key.GenerationId, key.Target);
    }

    /// <summary>
    /// 驗證 runtime lookup 的 non-locale 部分並正規化 alias。這個 helper 不建立完整 cache key，故不能把任意
    /// locale 變成 cache authority；詳細 locale 仍只能透過先前 Store 的 server-resolved projection 取得。
    /// </summary>
    private static bool TryCreateRuntimeTargetKey(
        string profileAlias,
        long generationId,
        MetadataOptionSetTarget target,
        out MetadataOptionSetRuntimeTargetKey runtimeTarget)
    {
        runtimeTarget = default;
        if (string.IsNullOrWhiteSpace(profileAlias) || generationId <= 0 || !Enum.IsDefined(target))
        {
            return false;
        }

        runtimeTarget = new MetadataOptionSetRuntimeTargetKey(profileAlias.Trim().ToUpperInvariant(), generationId, target);
        return true;
    }
}

// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/MetadataOptionSetCacheTests.cs
// 用途：驗證 P7.3 Data8 metadata OptionSet 快取的隔離、容量、到期與釋放契約。
// ============================================================================

using System.Collections;
using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;
using SpeechMessage.Dynamics.Connectors.Data8;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 <see cref="MetadataOptionSetCache"/> 只保存有界、純值且 server-derived metadata projection。
/// 每個案例使用可手動推進的 <see cref="TimeProvider"/>，因此不建立 timer、背景工作或 sleep；測試離開
/// scope 時會明確釋放快取，確保不把 option label、profile/generation cache key 或測試資料保留至下一個案例。
/// </summary>
public sealed class MetadataOptionSetCacheTests
{
    /// <summary>
    /// 保護快取在 Store 當下複製 option collection，且讀取端只得到不可寫入的新 snapshot。
    /// 故障注入為 Store 後清空 caller list；decisive assertions 是 cache 命中仍保留原投影，且透過
    /// <see cref="IList"/> 的寫入會失敗，避免呼叫端的可變集合跨 request、profile 或 generation 污染快取。
    /// </summary>
    [Fact]
    public void Store_deep_copies_options_and_returns_an_immutable_snapshot()
    {
        var time = new ManualTimeProvider();
        using var cache = CreateCache(time);
        var source = new List<OptionSetOptionRecord>
        {
            CreateOption(1, "原始標籤", 0)
        };

        cache.Store(CreateKey(" profile-a ", 1), source).Should().BeTrue();
        source.Clear();

        cache.TryGet(CreateKey("profile-a", 1), out var snapshot).Should().BeTrue();
        snapshot.Should().NotBeNull();
        snapshot!.Should().ContainSingle().Which.Label.Should().Be("原始標籤");
        var mutableView = snapshot.Should().BeAssignableTo<IList>().Subject;
        mutableView.Invoking(list => list.Clear()).Should().Throw<NotSupportedException>();
    }

    /// <summary>
    /// 保護 alias 與 generation 同時構成快取隔離邊界。故障注入為交錯寫入兩個 profile 及同一 profile 的
    /// 新 generation；decisive assertions 是僅完全相同的正規化 alias/generation/target/locale 能命中，
    /// 所以 profile replacement 或 A/B 交錯讀取都不會收到另一個 profile 的 metadata label。
    /// </summary>
    [Fact]
    public void TryGet_isolates_normalized_profile_alias_and_generation()
    {
        var time = new ManualTimeProvider();
        using var cache = CreateCache(time);
        var first = CreateKey(" profile-a ", 1);
        var secondProfile = CreateKey("profile-b", 1);
        var replacementGeneration = CreateKey("profile-a", 2);

        cache.Store(first, [CreateOption(1, "A-generation-1", 0)]).Should().BeTrue();
        cache.Store(secondProfile, [CreateOption(2, "B-generation-1", 0)]).Should().BeTrue();
        cache.Store(replacementGeneration, [CreateOption(3, "A-generation-2", 0)]).Should().BeTrue();

        cache.TryGet(CreateKey("PROFILE-A", 1), out var firstOptions).Should().BeTrue();
        firstOptions!.Should().ContainSingle().Which.Label.Should().Be("A-generation-1");
        cache.TryGet(secondProfile, out var secondOptions).Should().BeTrue();
        secondOptions!.Should().ContainSingle().Which.Label.Should().Be("B-generation-1");
        cache.TryGet(replacementGeneration, out var replacementOptions).Should().BeTrue();
        replacementOptions!.Should().ContainSingle().Which.Label.Should().Be("A-generation-2");
        cache.TryGet(CreateKey("profile-a", 3), out _).Should().BeFalse();
        cache.TryGet(CreateKey("profile-a", 1, locale: 1028), out _).Should().BeFalse();
    }

    /// <summary>
    /// 保護 runtime 在尚未接觸 connector 前，只能用先前由伺服器 metadata projection 證實的 locale 尋找快取。
    /// 故障注入為同一 profile/generation/target 回傳新的 server locale；decisive assertions 是舊 locale entry
    /// 被替換、新 locale 才能從 runtime-target lookup 命中，且不同 alias/generation 絕不取得這份 label。這讓
    /// executor 不必猜測主機或 caller locale，仍可使用完整 cache key 並在 server locale 變動時失效舊投影。
    /// </summary>
    [Fact]
    public void TryGet_for_runtime_target_uses_only_the_latest_server_resolved_locale()
    {
        var time = new ManualTimeProvider();
        using var cache = CreateCache(time);
        var originalLocale = CreateKey("profile-a", 1, locale: 1033);
        var replacementLocale = CreateKey("profile-a", 1, locale: 1028);

        cache.Store(originalLocale, [CreateOption(1, "English", 0)]).Should().BeTrue();
        cache.TryGet("profile-a", 1, MetadataOptionSetTarget.ContactCustomerTypeCode, out var original)
            .Should().BeTrue();
        original!.Should().ContainSingle().Which.Label.Should().Be("English");

        cache.Store(replacementLocale, [CreateOption(1, "繁體中文", 0)]).Should().BeTrue();

        cache.TryGet(originalLocale, out _).Should().BeFalse();
        cache.TryGet("profile-a", 1, MetadataOptionSetTarget.ContactCustomerTypeCode, out var replacement)
            .Should().BeTrue();
        replacement!.Should().ContainSingle().Which.Label.Should().Be("繁體中文");
        cache.TryGet("profile-b", 1, MetadataOptionSetTarget.ContactCustomerTypeCode, out _).Should().BeFalse();
        cache.TryGet("profile-a", 2, MetadataOptionSetTarget.ContactCustomerTypeCode, out _).Should().BeFalse();
    }

    /// <summary>
    /// 保護短 TTL 會在讀取時同步移除過期 entry。故障注入為手動把可信時間推進到 TTL 邊界；decisive assertion
    /// 是 cache miss 且沒有任何 timer、延遲或後台 callback 參與，避免 runtime disposal 後殘留非受控資源。
    /// </summary>
    [Fact]
    public void TryGet_removes_entry_when_the_injected_time_reaches_ttl()
    {
        var time = new ManualTimeProvider();
        using var cache = CreateCache(time, timeToLive: TimeSpan.FromMinutes(1));
        var key = CreateKey("profile-a", 1);

        cache.Store(key, [CreateOption(1, "到期測試", 0)]).Should().BeTrue();
        time.Advance(TimeSpan.FromMinutes(1));

        cache.TryGet(key, out var expired).Should().BeFalse();
        expired.Should().BeNull();
    }

    /// <summary>
    /// 保護 entry cap 以最舊 Store sequence 做可預期淘汰。故障注入為在上限為二時寫入第三筆不同鍵；
    /// decisive assertions 是第一筆被移除、後兩筆仍存在，避免長時間 runtime 因未界定字典成長保留 metadata。
    /// </summary>
    [Fact]
    public void Store_evicts_the_oldest_entry_when_entry_cap_is_reached()
    {
        var time = new ManualTimeProvider();
        using var cache = CreateCache(time, maximumEntryCount: 2, maximumByteCount: 8_192);
        var first = CreateKey("profile-a", 1);
        var second = CreateKey("profile-b", 1);
        var third = CreateKey("profile-c", 1);

        cache.Store(first, [CreateOption(1, "first", 0)]).Should().BeTrue();
        cache.Store(second, [CreateOption(2, "second", 0)]).Should().BeTrue();
        cache.Store(third, [CreateOption(3, "third", 0)]).Should().BeTrue();

        cache.TryGet(first, out _).Should().BeFalse();
        cache.TryGet(second, out _).Should().BeTrue();
        cache.TryGet(third, out _).Should().BeTrue();
    }

    /// <summary>
    /// 保護 byte cap 也遵循相同的最舊淘汰規則，而非僅依 entry count 假定 metadata 永遠很小。
    /// 故障注入為兩筆各自可容納、合計超出 budget 的 UTF-8 label；decisive assertions 是新 entry 成功保存、
    /// 舊 entry 被淘汰，證明容量不會隨 locale label 長度無限制成長。
    /// </summary>
    [Fact]
    public void Store_evicts_the_oldest_entry_when_byte_cap_is_reached()
    {
        var time = new ManualTimeProvider();
        using var cache = CreateCache(time, maximumEntryCount: 3, maximumByteCount: 300);
        var first = CreateKey("profile-a", 1);
        var second = CreateKey("profile-b", 1);

        cache.Store(first, [CreateOption(1, new string('a', 120), 0)]).Should().BeTrue();
        cache.Store(second, [CreateOption(2, new string('b', 120), 0)]).Should().BeTrue();

        cache.TryGet(first, out _).Should().BeFalse();
        cache.TryGet(second, out var current).Should().BeTrue();
        current!.Should().ContainSingle().Which.Label.Should().Be(new string('b', 120));
    }

    /// <summary>
    /// 保護 runtime owner Dispose 會同步清除全部 entry 並讓後續 read/write fail closed。
    /// 故障注入為釋放後仍嘗試存取同一實例；decisive assertions 是沒有回傳舊 label、Store 不再保留資料，
    /// 因此 metadata cache 不會比其 Data8 runtime 活得更久。
    /// </summary>
    [Fact]
    public void Dispose_clears_entries_and_rejects_subsequent_access()
    {
        var time = new ManualTimeProvider();
        var cache = CreateCache(time);
        var key = CreateKey("profile-a", 1);

        cache.Store(key, [CreateOption(1, "釋放前", 0)]).Should().BeTrue();
        cache.Dispose();

        cache.TryGet(key, out var disposedValue).Should().BeFalse();
        disposedValue.Should().BeNull();
        cache.Store(key, [CreateOption(2, "釋放後", 0)]).Should().BeFalse();
    }

    /// <summary>
    /// 建立固定 target 與明確 server-resolved locale 的快取鍵。locale 在本測試只是已驗證 server 解析結果的
    /// surrogate，並非瀏覽器或 ProductClient 可選的路由參數。
    /// </summary>
    private static MetadataOptionSetCacheKey CreateKey(string profileAlias, long generationId, int locale = 1033)
        => new(profileAlias, generationId, MetadataOptionSetTarget.ContactCustomerTypeCode, locale);

    /// <summary>
    /// 建立 bounded pure-value metadata projection；不建立 CRM SDK graph、localized-label graph 或可延續的 request state。
    /// </summary>
    private static OptionSetOptionRecord CreateOption(int value, string label, int order)
        => new() { Value = value, Label = label, ConfiguredOrder = order };

    /// <summary>
    /// 建立測試專用快取；時間由案例唯一擁有的 <paramref name="time"/> 提供，避免跨測試共用 static clock。
    /// </summary>
    private static MetadataOptionSetCache CreateCache(
        TimeProvider time,
        int maximumEntryCount = 8,
        int maximumByteCount = 8_192,
        TimeSpan? timeToLive = null)
        => new(maximumEntryCount, maximumByteCount, timeToLive ?? TimeSpan.FromMinutes(1), time);

    /// <summary>
    /// 提供完全由測試控制的 UTC 時鐘。它只保存單一 immutable instant，不使用 timer、Thread.Sleep 或共用狀態，
    /// 因而能精確覆蓋 TTL 邊界又不引入背景資源或測試間的時間競爭。
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

        /// <summary>取得測試目前控制的 UTC instant。</summary>
        public override DateTimeOffset GetUtcNow() => _utcNow;

        /// <summary>以正的有限 duration 推進時鐘，避免測試透過倒退時間掩蓋 cache 到期行為。</summary>
        public void Advance(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            _utcNow = _utcNow.Add(duration);
        }
    }
}

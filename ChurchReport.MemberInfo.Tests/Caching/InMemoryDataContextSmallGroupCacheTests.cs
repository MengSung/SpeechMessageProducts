using System;
using System.Threading;
using ChurchReport.Models;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Caching;

public sealed class InMemoryDataContextSmallGroupCacheTests
{
    [Fact]
    public void ApplySessionCachePolicyForTesting_UsesBoundedExpiration()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var entry = cache.CreateEntry("key");

        InMemoryDataContextSmallGroup.ApplySessionCachePolicyForTesting(entry);

        entry.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromMinutes(30));
        entry.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(30));
        entry.PostEvictionCallbacks.Should().NotBeEmpty();
    }

    [Fact]
    public void DisposeCachedValueForTesting_DisposesDisposableValues()
    {
        var disposable = new RecordingDisposable();

        InMemoryDataContextSmallGroup.DisposeCachedValueForTesting("key", disposable, EvictionReason.Removed, null);

        disposable.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void DisposeCachedValueForTesting_IgnoresNonDisposableValues()
    {
        var action = () => InMemoryDataContextSmallGroup.DisposeCachedValueForTesting("key", new object(), EvictionReason.Removed, null);

        action.Should().NotThrow();
    }

    private sealed class RecordingDisposable : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => _disposeCount;

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}

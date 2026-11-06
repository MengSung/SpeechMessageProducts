using System.Runtime.CompilerServices;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 不信任注入的Repository會替上層驗頁；五入口自己拒絕錯表、重複／空ID與超量，且失敗不能返回部分結果。
    /// 固定訊息及原InnerException保護相容失敗鏈；無SDK網路，不宣稱實機D365通過。
    /// </summary>
    public sealed class RepositoryQuerySafetyTests
    {
        /// <summary>五入口及故障獨立列案，避免一個失敗遮蔽其他路徑；只包含固定測試分類。</summary>
        public static IEnumerable<object[]> InvalidPages() => Entries.SelectMany(entry =>
            new[] { "null", "empty-id", "table", "duplicate", "budget" }.Select(mode => new object[] { entry, mode }));

        /// <summary>整頁驗證先於第一筆選擇及集合發布；single的top1不能默認SDK遵守，其他頁最多5000列。</summary>
        [Theory]
        [MemberData(nameof(InvalidPages))]
        public void EveryQuery_RejectsInvalidRepositoryPage(string entry, string mode)
        {
            var repository = Source(query =>
            {
                var table = Table(query);
                var row = new Entity(mode == "table" ? "account" : table,
                    mode == "empty-id" ? Guid.Empty : ContactCharacterizationTests.FirstId);
                if (mode == "null") return null!;
                if (mode == "budget") return new EntityCollection(Enumerable.Range(1, entry == "single" ? 2 : 5001)
                    .Select(index => new Entity(table, new Guid(index, 0, 0, new byte[8]))).ToArray());
                return new EntityCollection(mode == "duplicate" ? new[] { row, row } : new[] { row });
            });
            var failure = Assert.Throws<InvalidOperationException>(() => RepositoryQueryCharacterizationTests.Read(New(repository.Object), entry));
            Assert.IsType<InvalidOperationException>(failure.InnerException);
        }

        /// <summary>非法metadata、空GUID與非正月份皆在repository之前拒絕，不能因值存在而誤當授權。</summary>
        [Theory]
        [InlineData("entity")] [InlineData("field")] [InlineData("blood")] [InlineData("present")] [InlineData("months")]
        public void Inputs_RejectBeforeIo(string mode)
        {
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
            var entry = mode == "entity" || mode == "field" ? "collection" : mode == "months" ? "present" : mode;
            var failure = Assert.Throws<InvalidOperationException>(() => RepositoryQueryCharacterizationTests.Read(New(repository.Object), entry,
                id: mode == "blood" || mode == "present" ? Guid.Empty : null,
                entity: mode == "entity" ? "a<&" : "contact", field: mode == "field" ? "a<&" : "fullname",
                months: mode == "months" ? 0 : 3));
            Assert.IsAssignableFrom<ArgumentException>(failure.InnerException);
            repository.VerifyNoOtherCalls();
        }

        /// <summary>即使repository替身不驗XML，DTD、雙根表、無表及錯根仍須先拒絕；不解析檔案或網路。</summary>
        [Theory]
        [InlineData("<!DOCTYPE fetch [<!ENTITY x SYSTEM 'file:///nonexistent'>]><fetch><entity name='contact'/></fetch>")]
        [InlineData("<fetch><entity name='contact'/><entity name='account'/></fetch>")]
        [InlineData("<fetch/>")]
        [InlineData("<wrong><entity name='contact'/></wrong>")]
        public void UnsafeFetch_RejectsBeforeRepository(string xml)
        {
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
            var failure = Assert.Throws<InvalidOperationException>(() => New(repository.Object).QueryByFetchXml(xml));
            Assert.IsAssignableFrom<ArgumentException>(failure.InnerException);
            repository.VerifyNoOtherCalls();
        }

        /// <summary>五入口空頁沿用單筆null／集合空值；故障保留同一InnerException且下一次明確重試可恢復。</summary>
        [Fact]
        public void EmptyAndFailureRetry_PreserveContracts()
        {
            foreach (var entry in Entries)
            {
                var calls = 0;
                var failure = new TimeoutException("synthetic-private");
                var repository = Source(_ => ++calls == 1 ? throw failure : new EntityCollection());
                var service = New(repository.Object);
                var actual = Assert.Throws<InvalidOperationException>(() => RepositoryQueryCharacterizationTests.Read(service, entry));
                Assert.Same(failure, actual.InnerException);
                Assert.DoesNotContain("synthetic-private", actual.Message);
                var result = RepositoryQueryCharacterizationTests.Read(service, entry);
                if (entry == "single" || entry == "blood") Assert.Null(result);
                else Assert.Empty(Assert.IsType<EntityCollection>(result).Entities);
                Assert.Equal(2, calls);
            }
        }

        /// <summary>借入repository與SDK都不能被Dispose；五入口Dispose後直接拒絕，不能改包成業務查詢失敗。</summary>
        [Fact]
        public void Dispose_RejectsAllEntriesWithoutOwningDependencies()
        {
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
            var disposable = repository.As<IDisposable>();
            var service = New(repository.Object);
            ((IDisposable)service).Dispose();
            foreach (var entry in Entries)
                Assert.Throws<ObjectDisposedException>(() => RepositoryQueryCharacterizationTests.Read(service, entry));
            ((IDisposable)service).Dispose();
            disposable.Verify(item => item.Dispose(), Times.Never);
            repository.VerifyNoOtherCalls();
        }

        /// <summary>已入場同步查詢可drain；Dispose只清owner參照，事件與Task在finally排空，沒有懸掛工作。</summary>
        [Fact]
        public async Task DisposeRace_DrainsStartedRepositoryWork()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var service = New(Source(_ =>
            {
                entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                return new EntityCollection();
            }).Object);
            var work = Task.Run(() => service.RetrieveEntityCollectionByField("contact", "fullname", "synthetic"));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                ((IDisposable)service).Dispose();
                Assert.Throws<ObjectDisposedException>(() => service.QueryBloodReportByContactId(ContactCharacterizationTests.FirstId));
            }
            finally { release.Set(); await work; }
        }

        /// <summary>1000次A/B不同repository並行不串scope或caller mutation，排空active=0；服務沒有結果cache。</summary>
        [Fact]
        public async Task ConcurrentScopes_AreIsolatedAndDrain()
        {
            var active = 0;
            var a = New(Scope(ContactCharacterizationTests.FirstId).Object);
            var b = New(Scope(ContactCharacterizationTests.SecondId).Object);
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var actual = RepositoryQueryCharacterizationTests.Read(index % 2 == 0 ? a : b, Entries[index % Entries.Length]);
                var row = actual is Entity entity ? entity : Assert.Single(Assert.IsType<EntityCollection>(actual).Entities);
                Assert.Equal(index % 2 == 0 ? ContactCharacterizationTests.FirstId : ContactCharacterizationTests.SecondId, row.Id);
                Assert.Equal("synthetic", row["fullname"]); row["fullname"] = "caller";
            })));
            Assert.Equal(0, active);
            ((IDisposable)a).Dispose(); ((IDisposable)b).Dispose();

            Mock<IEntityRepository> Scope(Guid id) => Source(query =>
            {
                Interlocked.Increment(ref active);
                try { return new EntityCollection(new[] { new Entity(Table(query), id) { ["fullname"] = "synthetic" } }); }
                finally { Interlocked.Decrement(ref active); }
            });
        }

        /// <summary>服務不使用的SDK參數不保留，Dispose後repository可回收；有限GC檢查與實際drain互補。</summary>
        [Fact]
        public void Ownership_ReleasesUnusedSdkAndDisposedRepository()
        {
            var (service, repository, sdk) = Weak();
            Collect(); Assert.False(sdk.IsAlive); Assert.True(repository.IsAlive);
            ((IDisposable)service).Dispose();
            Collect(); Assert.False(repository.IsAlive); GC.KeepAlive(service);
        }

        /// <summary>實作及五入口契約必須位於同一雙目標Core組件，不依host同名副本。</summary>
        [Fact]
        public void Implementation_IsInCore() => Assert.Same(typeof(IQueryService).Assembly, typeof(QueryService).Assembly);

        /// <summary>固定入口清單不包含任何request或業務scope。</summary>
        private static readonly string[] Entries = { "single", "collection", "fetch", "blood", "present" };

        /// <summary>替身只回應注入故障，不替上層做驗證。</summary>
        private static Mock<IEntityRepository> Source(Func<QueryBase, EntityCollection> read)
        {
            var result = new Mock<IEntityRepository>(MockBehavior.Strict);
            result.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(read);
            return result;
        }

        /// <summary>合成根表對應原輸入，不代表caller授權。</summary>
        private static string Table(QueryBase query) => query is QueryExpression expression ? expression.EntityName : "contact";

        /// <summary>來源相容SDK只供建構檢查，測試不建立連線。</summary>
        private static QueryService New(IEntityRepository repository) =>
            new QueryService(repository, new Mock<IOrganizationService>(MockBehavior.Strict).Object);

        /// <summary>移除JIT區域變數偽owner，唯一強參照應來自受測服務。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (QueryService, WeakReference, WeakReference) Weak()
        {
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict).Object;
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
            return (new QueryService(repository, sdk), new WeakReference(repository), new WeakReference(sdk));
        }

        /// <summary>固定三輪GC，沒有無界等待或重試。</summary>
        private static void Collect()
        {
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        }
    }
}

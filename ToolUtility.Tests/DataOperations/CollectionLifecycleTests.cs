using System.Runtime.CompilerServices;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.CollectionOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 驗證集合服務的 caller 範圍、取消、借用 SDK 與可回收參照。
    /// 測試自己擁有的 task 以 WhenAll 排空；同步關卡使用 using 釋放，沒有真連線或持久狀態。
    /// </summary>
    public sealed class CollectionLifecycleTests
    {
        /// <summary>
        /// 兩個已授權 SDK scope 並行 1000 次，故障明確重試後回傳本 scope 的完整獨立結果。
        /// 斷言相同顯示名的不同 ID 保留、修改 caller 清單不污染其他結果、active=0 且借用 SDK 未被 Dispose。
        /// </summary>
        [Fact]
        public async Task ParallelScopes_MutationAndFailureRetryDrainWithoutCrossTalk()
        {
            var active = 0;
            var calls = 0;
            var retries = 0;
            var aId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var bId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var a = Source(aId);
            var b = Source(bId);
            using var serviceA = new CollectionQueryService(new object(), a.Object);
            using var serviceB = new CollectionQueryService(new object(), b.Object);
            var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(async () =>
            {
                var expected = index % 2 == 0 ? aId : bId;
                var service = index % 2 == 0 ? serviceA : serviceB;
                var input = index % 17 == 0 ? "fail" : "ok";
                EntityCollection result;
                try { result = await service.RetrieveEntityCollectionByFieldAsync("contact", "new_scope", input); }
                catch (TimeoutException)
                {
                    Interlocked.Increment(ref retries);
                    result = await service.RetrieveEntityCollectionByFieldAsync("contact", "new_scope", "ok");
                }
                Assert.Equal(expected, Assert.Single(result.Entities).Id);
                Assert.Equal("same-name", result.Entities[0]["fullname"]);
                result.Entities[0]["fullname"] = "caller-mutation";
                result.Entities.Clear();
            })).ToArray();
            await Task.WhenAll(tasks);
            Assert.Equal(59, retries);
            Assert.Equal(1059, calls);
            Assert.Equal(0, active);
            a.As<IDisposable>().Verify(owner => owner.Dispose(), Times.Never);
            b.As<IDisposable>().Verify(owner => owner.Dispose(), Times.Never);

            Mock<IOrganizationService> Source(Guid id)
            {
                var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
                sdk.As<IDisposable>();
                sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        Interlocked.Increment(ref calls);
                        if (Equals(Assert.IsType<QueryByAttribute>(query).Values[0], "fail")) throw new TimeoutException();
                        return new EntityCollection { Entities = { new Entity("contact", id) { ["fullname"] = "same-name" } } };
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
                return sdk;
            }
        }

        /// <summary>Dispose 不搶走進行中 SDK 所有權；舊操作可排空，新入口立即拒絕，無 timer／registration 殘留。</summary>
        [Fact]
        public async Task DisposeDuringIo_ExistingOperationDrainsAndNewOperationFails()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.As<IDisposable>();
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic drain barrier.");
                return CollectionCharacterizationTests.Page("contact");
            });
            var service = new CollectionQueryService(new object(), sdk.Object);
            var running = Task.Run(() => service.RetrieveEntityCollectionByField("contact", "new_scope", "synthetic"));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                service.Dispose();
                await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                    service.RetrieveEntityCollectionByFieldAsync("contact", "new_scope", "synthetic"));
            }
            finally { release.Set(); }
            Assert.Equal(2, (await running).Entities.Count);
            sdk.As<IDisposable>().Verify(owner => owner.Dispose(), Times.Never);
        }

        /// <summary>保持服務存活仍可回收相容 logger；Dispose 後借用 SDK 可回收，不僅驗證私人欄位是否為 null。</summary>
        [Fact]
        public void LoggerAndDisposedSdk_AreCollectible()
        {
            var references = Create();
            Collect();
            Assert.False(references.Logger.IsAlive);
            Assert.True(references.Sdk.IsAlive);
            references.Service.Dispose();
            Collect();
            Assert.False(references.Sdk.IsAlive);
            GC.KeepAlive(references.Service);
        }

        /// <summary>僅測試執行有界完整 GC；不以記憶體總量單點代替可達性，也不新增產品端清理接口。</summary>
        private static void Collect()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        /// <summary>禁止內聯，避免 JIT 的測試局部生命週期被誤認為產品持有參照。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (CollectionQueryService Service, WeakReference Logger, WeakReference Sdk) Create()
        {
            var logger = new object();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
            var service = new CollectionQueryService(logger, sdk);
            var result = (service, new WeakReference(logger), new WeakReference(sdk));
            return result;
        }

        /// <summary>明確頁碼支援無 cookie 第三頁，filter 及欄位保留原值，未知總數 -1 不改成頁大小。</summary>
        [Fact]
        public async Task ExplicitPageNumber_WorksWithoutCookieAndPreservesMetadata()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var filter = new FilterExpression(LogicalOperator.Or);
            filter.AddCondition("new_scope", ConditionOperator.Equal, "synthetic");
            var columns = new ColumnSet("fullname");
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                Assert.Equal(3, query.PageInfo.PageNumber);
                Assert.Equal(20, query.PageInfo.Count);
                Assert.Null(query.PageInfo.PagingCookie);
                Assert.Same(filter, query.Criteria);
                Assert.Same(columns, query.ColumnSet);
                var page = CollectionCharacterizationTests.Page("contact");
                page.TotalRecordCount = -1;
                page.MoreRecords = true;
                return page;
            });
            using var service = new CollectionQueryService(new object(), sdk.Object);
            var result = await service.RetrievePagedEntitiesAsync("contact", 3, filter, columns, 20);
            Assert.Equal(-1, result.TotalCount);
            Assert.True(result.MoreRecords);
            Assert.Null(result.PagingCookie);
        }

        /// <summary>pageNumber、大小與 cookie 頁碼錯配在 I/O 前拒絕，不默默截斷或重置頁碼。</summary>
        [Theory]
        [InlineData(0, 10, null)]
        [InlineData(1001, 10, null)]
        [InlineData(1, 0, null)]
        [InlineData(1, 5001, null)]
        [InlineData(3, 10, "<cookie page=\"1\" />")]
        public async Task PageBoundsAndMismatch_RejectBeforeIo(int page, int size, string? cookie)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            using var service = new CollectionQueryService(new object(), sdk.Object);
            await Assert.ThrowsAnyAsync<ArgumentException>(() => service.RetrievePagedEntitiesAsync("contact", page,
                pageSize: size, pagingCookie: cookie));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>輸入不可無界列舉；第 5001 個唯一 ID 即拒絕並 Dispose enumerator，不送無效 IN 給 SDK。</summary>
        [Fact]
        public async Task InputBudget_DisposesEnumeratorWithoutIo()
        {
            var moved = 0;
            var drained = false;
            IEnumerable<Guid> Source()
            {
                try
                {
                    for (var index = 1; ; index++)
                    {
                        moved++;
                        yield return new Guid(index, 0, 0, new byte[8]);
                    }
                }
                finally { drained = true; }
            }
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            using var service = new CollectionQueryService(new object(), sdk.Object);
            await Assert.ThrowsAsync<ArgumentException>(() => service.RetrieveBatchByIdsAsync("contact", "contactid", Source()));
            Assert.Equal(5001, moved);
            Assert.True(drained);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>無 cookie 且每頁一筆的惡意無盡來源也只能讀 1000 正式頁與一個探測頁；不得發布部分集合。</summary>
        [Fact]
        public async Task PageBudget_StopsUnboundedContinuation()
        {
            var calls = 0;
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
            {
                calls++;
                return new EntityCollection
                {
                    MoreRecords = true,
                    Entities = { new Entity("contact", new Guid(calls, 0, 0, new byte[8])) }
                };
            });
            using var service = new CollectionQueryService(new object(), sdk.Object);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RetrieveBatchByFieldValuesAsync("contact", "new_scope", new List<string> { "synthetic" }));
            Assert.Equal(1001, calls);
        }
    }
}

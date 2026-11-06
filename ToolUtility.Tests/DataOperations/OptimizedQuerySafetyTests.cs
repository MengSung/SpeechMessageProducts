/*
 * 檔案整體說明
 *
 * 目的與定位：對優化查詢真實入口注入錯誤身份、截斷、取消及 Dispose 競爭。 SDK 替身僅模擬網路回應，斷言產品是否拒絕發布、保留完整候選且清除借用參照；不連真實 D365。
 *
 * 主要功能與流程：
 * 本檔宣告 OptimizedQuerySafetyTests。
 * - 五個同步／Task 發布入口分別接受四種故障；每案建立獨立 SDK／候選。
 * - 空 ID、錯表、錯／重複 ID 與 null 回應都不可成為成功資料；訊息不得回顯資料值。
 * - OR 條件必須整體與啟用條件 AND，而非把 statecode 加入 OR 擴張範圍；原輸入樹不能被修改。
 * - 批次必須讀完第三頁後才回字典；每頁 ID 各異、同名都保留，分頁中斷不可回部分成功。
 * 測試案例入口包含 InvalidPages、EveryEntry_RejectsInvalidIdentity、Condition_DoesNotMutateCallerOrExpandOrFilter、
 * Batch_ReadsAllPagesAndDoesNotPublishFailure、Paging_UsesThirdPageAndPreservesUnfilteredContract、
 * Condition_RejectsInvalidTopBeforeIo、Condition_RejectsOutputBeyondRequestedTop、
 * Paging_ExplicitPageSupportsNoCookieAndPreservesMetadata；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Runtime.CompilerServices;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 對優化查詢真實入口注入錯誤身份、截斷、取消及 Dispose 競爭。
    /// SDK 替身僅模擬網路回應，斷言產品是否拒絕發布、保留完整候選且清除借用參照；不連真實 D365。
    /// </summary>
    public sealed class OptimizedQuerySafetyTests
    {
        /// <summary>五個同步／Task 發布入口分別接受四種故障；每案建立獨立 SDK／候選。</summary>
        public static IEnumerable<object[]> InvalidPages()
        {
            for (var operation = 0; operation < 5; operation++)
                for (var defect = 0; defect < 4; defect++)
                    yield return new object[] { operation, defect };
        }

        /// <summary>空 ID、錯表、錯／重複 ID 與 null 回應都不可成為成功資料；訊息不得回顯資料值。</summary>
        [Theory]
        [MemberData(nameof(InvalidPages))]
        public async Task EveryEntry_RejectsInvalidIdentity(int operation, int defect)
        {
            var page = CollectionCharacterizationTests.Page("contact");
            var single = page.Entities[0];
            if (defect == 0) single.Id = Guid.Empty;
            if (defect == 1) single.LogicalName = "account";
            if (defect == 2)
            {
                single.Id = CollectionCharacterizationTests.SecondId;
                page.Entities[1].Id = single.Id;
            }
            var sdk = Source(() => defect == 3 ? null! : page);
            sdk.Setup(item => item.Retrieve("contact", CollectionCharacterizationTests.FirstId, It.IsAny<ColumnSet>()))
                .Returns(defect == 3 ? null! : single);
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(service, operation));
            Assert.DoesNotContain("same-name", failure.Message);
        }

        /// <summary>OR 條件必須整體與啟用條件 AND，而非把 statecode 加入 OR 擴張範圍；原輸入樹不能被修改。</summary>
        [Fact]
        public void Condition_DoesNotMutateCallerOrExpandOrFilter()
        {
            var input = new FilterExpression(LogicalOperator.Or);
            input.AddCondition("fullname", ConditionOperator.Equal, "synthetic");
            var sdk = Source(() => CollectionCharacterizationTests.Page("contact"));
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                var actual = Assert.IsType<QueryExpression>(query).Criteria;
                Assert.NotSame(input, actual);
                Assert.Equal(LogicalOperator.And, actual.FilterOperator);
                Assert.Same(input, Assert.Single(actual.Filters));
                var state = Assert.Single(actual.Conditions);
                Assert.Equal("statecode", state.AttributeName);
                Assert.Equal(0, Assert.Single(state.Values));
                return CollectionCharacterizationTests.Page("contact");
            });
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            Assert.Equal(2, service.RetrieveByCondition("contact", input).Entities.Count);
            Assert.Single(input.Conditions);
            Assert.Empty(input.Filters);
            Assert.Equal(LogicalOperator.Or, input.FilterOperator);
        }

        /// <summary>批次必須讀完第三頁後才回字典；每頁 ID 各異、同名都保留，分頁中斷不可回部分成功。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Batch_ReadsAllPagesAndDoesNotPublishFailure(bool failLater)
        {
            var calls = 0;
            var fault = new TimeoutException("synthetic-private-value");
            var sdk = Source(() =>
            {
                calls++;
                if (calls == 3 && failLater) throw fault;
                // 首頁是相容探測，正式穩定排序從第一頁重讀。
                return new EntityCollection
                {
                    Entities = { new Entity("contact", calls <= 2
                        ? CollectionCharacterizationTests.FirstId : CollectionCharacterizationTests.SecondId) },
                    MoreRecords = calls < 3
                };
            });
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            var ids = new List<Guid> { CollectionCharacterizationTests.FirstId, CollectionCharacterizationTests.SecondId };
            if (failLater) Assert.Same(fault, Assert.Throws<TimeoutException>(() => service.RetrieveBatch("contact", ids)));
            else Assert.Equal(2, service.RetrieveBatch("contact", ids).Count);
            Assert.Equal(3, calls);
        }

        /// <summary>舊 cookie 入口以 cookie page+1 續讀，不可永遠送第 2 頁；缺省 filter 維持無 statecode 條件。</summary>
        [Fact]
        public async Task Paging_UsesThirdPageAndPreservesUnfilteredContract()
        {
            var sdk = Source(() => CollectionCharacterizationTests.Page("contact"));
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.Equal(3, expression.PageInfo.PageNumber);
                Assert.Empty(expression.Criteria.Conditions);
                Assert.Contains(expression.Orders, order => order.AttributeName == "contactid");
                return CollectionCharacterizationTests.Page("contact");
            });
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            var page = await service.RetrievePagedAsync("contact", pagingCookie: "<cookie page=\"2\" />");
            Assert.Equal(2, page.Entities.Count);
        }

        /// <summary>條件查詢的本地 top 預算為 1..5000；拒絕無界或反向值，且 SDK 呼叫數維持零。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(5001)]
        public void Condition_RejectsInvalidTopBeforeIo(int top)
        {
            var sdk = Source(() => CollectionCharacterizationTests.Page("contact"));
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                service.RetrieveByCondition("contact", new FilterExpression(), top));
            sdk.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Never);
        }

        /// <summary>SDK 若違反 top 回更多列，不能以合法身份檢查通過為理由發布超預算資料。</summary>
        [Fact]
        public void Condition_RejectsOutputBeyondRequestedTop()
        {
            var sdk = Source(() => CollectionCharacterizationTests.Page("contact"));
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            Assert.Throws<InvalidOperationException>(() =>
                service.RetrieveByCondition("contact", new FilterExpression(), 1));
        }

        /// <summary>明確頁碼多載支援無 cookie 第三頁；來源 metadata 與 null cookie 原樣保留，不在服務保存游標。</summary>
        [Fact]
        public async Task Paging_ExplicitPageSupportsNoCookieAndPreservesMetadata()
        {
            var sdk = Source(() => CollectionCharacterizationTests.Page("contact"));
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.Equal(3, expression.PageInfo.PageNumber);
                Assert.Null(expression.PageInfo.PagingCookie);
                var page = CollectionCharacterizationTests.Page("contact");
                page.MoreRecords = true;
                page.TotalRecordCount = 9;
                return page;
            });
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            var page = await service.RetrievePagedAsync("contact", 3);
            Assert.Equal(2, page.Entities.Count);
            Assert.True(page.MoreRecords);
            Assert.Equal(9, page.TotalCount);
            Assert.Null(page.PagingCookie);
        }

        /// <summary>Dispose 與已開始的同步 I/O 競爭時，舊工作排空、新入口拒絕；關卡必須在 finally 釋放。</summary>
        [Fact]
        public async Task DisposeDuringIo_DrainsExistingCallAndRejectsNewOne()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var sdk = Source(() =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic drain barrier.");
                return CollectionCharacterizationTests.Page("contact");
            });
            sdk.As<IDisposable>();
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            var running = Task.Run(() => service.RetrieveByFieldValue("contact", "new_scope", "synthetic"));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                service.Dispose();
                Assert.Throws<ObjectDisposedException>(() => service.RetrieveBatch("contact", null));
            }
            finally { release.Set(); }
            Assert.Equal(2, (await running).Entities.Count);
            sdk.As<IDisposable>().Verify(owner => owner.Dispose(), Times.Never);
        }

        /// <summary>caller 在同步 I/O 中取消後不能回成功；內部取消而 caller 未取消時仍須保留故障 Task。</summary>
        [Fact]
        public async Task Cancellation_DuringIoDoesNotPublishAndInternalTimeoutIsFaulted()
        {
            using var cancellation = new CancellationTokenSource();
            var sdk = Source(() =>
            {
                cancellation.Cancel();
                return CollectionCharacterizationTests.Page("contact");
            });
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.RetrieveByFieldValueAsync("contact", "fullname", "synthetic", cancellationToken: cancellation.Token));
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(new OperationCanceledException());
            var faulted = service.RetrieveByFieldValueAsync("contact", "fullname", "synthetic");
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => faulted);
            Assert.True(faulted.IsFaulted);
            Assert.False(faulted.IsCanceled);
        }

        /// <summary>五個 Task 入口預先取消都零 I/O；未取消 caller 的內部取消必須 faulted 且保留原物件，不是 canceled Task。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task EveryAsyncEntry_PreCancellationSkipsIoAndInternalCancellationIsFaulted(int entry)
        {
            var fault = new OperationCanceledException("synthetic-internal-timeout");
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(fault);
            sdk.Setup(item => item.Retrieve("contact", CollectionCharacterizationTests.FirstId, It.IsAny<ColumnSet>())).Throws(fault);
            using var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var cancelled = Start(cancellation.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            Assert.True(cancelled.IsCanceled);
            sdk.VerifyNoOtherCalls();

            var failed = Start(default);
            Assert.Same(fault, await Assert.ThrowsAsync<OperationCanceledException>(() => failed));
            Assert.True(failed.IsCompleted);
            Assert.True(failed.IsFaulted);
            Assert.False(failed.IsCanceled);

            Task Start(CancellationToken token) => entry switch
            {
                0 => service.RetrieveEntityAsync("contact", CollectionCharacterizationTests.FirstId, token),
                1 => service.RetrieveBatchAsync("contact", new List<Guid> { CollectionCharacterizationTests.FirstId }, token),
                2 => service.RetrieveByConditionAsync("contact", new FilterExpression(), cancellationToken: token),
                3 => service.RetrieveByFieldValueAsync("contact", "new_scope", "synthetic", cancellationToken: token),
                4 => service.RetrievePagedAsync("contact", cancellationToken: token),
                _ => throw new ArgumentOutOfRangeException(nameof(entry))
            };
        }

        /// <summary>所有入口在 Dispose 後拒絕，即使是空批次；不會處置 host 所有的 SDK。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task Dispose_StopsEveryNewEntryWithoutDisposingBorrowedSdk(int operation)
        {
            var sdk = Source(() => CollectionCharacterizationTests.Page("contact"));
            sdk.As<IDisposable>();
            var service = new EntityOptimizedQueryService(new object(), sdk.Object);
            service.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => Invoke(service, operation));
            sdk.As<IDisposable>().Verify(owner => owner.Dispose(), Times.Never);
        }

        /// <summary>保持服務活著仍可回收相容 logger；Dispose 後 SDK 亦可回收，不能只清一個無效旗標。</summary>
        [Fact]
        public void Dispose_ReleasesBorrowedReferenceAndDoesNotRetainLogger()
        {
            var references = References();
            Collect();
            Assert.False(references.Logger.IsAlive);
            Assert.True(references.Sdk.IsAlive);
            references.Service.Dispose();
            Collect();
            Assert.False(references.Sdk.IsAlive);
            GC.KeepAlive(references.Service);
        }

        /// <summary>兩個授權 SDK scope 並行 1000 次，Task 排空後 active=0；結果修改不能影響下一個 caller。</summary>
        [Fact]
        public async Task ParallelScopes_DrainWithoutCrossTalkOrMutableResultRetention()
        {
            var active = 0;
            using var a = new EntityOptimizedQueryService(new object(), For(CollectionCharacterizationTests.FirstId).Object);
            using var b = new EntityOptimizedQueryService(new object(), For(CollectionCharacterizationTests.SecondId).Object);
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(async () =>
            {
                var expected = index % 2 == 0 ? CollectionCharacterizationTests.FirstId : CollectionCharacterizationTests.SecondId;
                var result = await (index % 2 == 0 ? a : b).RetrieveByFieldValueAsync("contact", "new_scope", "synthetic");
                Assert.Equal(expected, Assert.Single(result.Entities).Id);
                result.Entities.Clear();
            })));
            Assert.Equal(0, active);

            Mock<IOrganizationService> For(Guid id) => Source(() =>
            {
                Interlocked.Increment(ref active);
                try { return new EntityCollection { Entities = { new Entity("contact", id) } }; }
                finally { Interlocked.Decrement(ref active); }
            });
        }

        /// <summary>每案的 SDK 只回此案提供的候選；不持全域狀態，也沒有可被產品當作成功的預設空結果。</summary>
        private static Mock<IOrganizationService> Source(Func<EntityCollection> response)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(response);
            return sdk;
        }

        /// <summary>經實際同步與 Task 入口執行，不反射呼叫 private validator 來繞過發布路徑。</summary>
        private static async Task Invoke(EntityOptimizedQueryService service, int operation)
        {
            switch (operation)
            {
                case 0: service.RetrieveEntity("contact", CollectionCharacterizationTests.FirstId); break;
                case 1: service.RetrieveByCondition("contact", new FilterExpression()); break;
                case 2: await service.RetrieveByFieldValueAsync("contact", "new_scope", "synthetic"); break;
                case 3: service.RetrieveBatch("contact", new List<Guid>
                    { CollectionCharacterizationTests.FirstId, CollectionCharacterizationTests.SecondId }); break;
                case 4: await service.RetrievePagedAsync("contact"); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        /// <summary>隔離配置 stack，避免 JIT 延長局部物件生命影響弱參照斷言；產品沒有新增測試 hook。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (EntityOptimizedQueryService Service, WeakReference Logger, WeakReference Sdk) References()
        {
            var sdk = new Mock<IOrganizationService>().Object;
            var logger = new object();
            return (new EntityOptimizedQueryService(logger, sdk), new WeakReference(logger), new WeakReference(sdk));
        }

        /// <summary>有界五次完整 GC 只用於可達性測試；不把單次總記憶體量或產品強制 GC 當生命週期證明。</summary>
        private static void Collect()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }
    }
}

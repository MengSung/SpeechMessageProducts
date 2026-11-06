/*
 * 檔案整體說明
 *
 * 目的與定位：SDK async facade的身份、批次發布、取消與查詢物件所有權測試；只替換同步SDK邊界，不代表實機CRM效能。
 *
 * 主要功能與流程：
 * 本檔宣告 CrmAsyncExtensionSafetyTests。
 * - 回傳字典只以Guid為key，重複輸入ID（含跨實體）必須在SDK前拒絕，不能由後寫覆蓋前寫。
 * - 並行度須落在固定上限內，空／重複／錯表／錯ID回應不得發布入字典。
 * - 任一SDK回應的logical name與Id必須完全對應本次請求，不能用別表同Guid污染結果。
 * - 分頁查詢在成功或錯誤都還原caller的PageInfo物件與值，並跨頁拒絕重複穩定ID。
 * 測試案例入口包含 ParallelRetrieve_RejectsDuplicateIdentityBeforeIo、ParallelRetrieve_RejectsUnboundedConcurrency、
 * ParallelRetrieve_RejectsWrongReturnedEntity、RetrieveAll_RestoresCallerPagingAndRejectsDuplicateAcrossPages、
 * RetrieveAll_FailureRestoresCallerPagingAndPreservesException、
 * BatchRetrieve_RejectsDuplicateRowsAndOversizedInput、SingleOperations_PreserveCancellationOwnership、
 * CallerCancellationDuringIo_DoesNotPublishResponse；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Microsoft.Xrm.Sdk;
using System.Runtime.CompilerServices;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.Extensions;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// SDK async facade的身份、批次發布、取消與查詢物件所有權測試；只替換同步SDK邊界，不代表實機CRM效能。
    /// </summary>
    public sealed class CrmAsyncExtensionSafetyTests
    {
        /// <summary>回傳字典只以Guid為key，重複輸入ID（含跨實體）必須在SDK前拒絕，不能由後寫覆蓋前寫。</summary>
        [Fact]
        public async Task ParallelRetrieve_RejectsDuplicateIdentityBeforeIo()
        {
            var service = new Mock<IOrganizationService>(MockBehavior.Strict);
            await Assert.ThrowsAsync<ArgumentException>(() => service.Object.ParallelRetrieveAsync(
                new[] { ("contact", ContactCharacterizationTests.FirstId), ("account", ContactCharacterizationTests.FirstId) },
                new ColumnSet(true)));
            service.VerifyNoOtherCalls();
        }

        /// <summary>並行度須落在固定上限內，空／重複／錯表／錯ID回應不得發布入字典。</summary>
        [Theory]
        [InlineData(0)] [InlineData(-1)] [InlineData(65)]
        public async Task ParallelRetrieve_RejectsUnboundedConcurrency(int degree)
        {
            var service = new Mock<IOrganizationService>(MockBehavior.Strict);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.Object.ParallelRetrieveAsync(
                new[] { ("contact", ContactCharacterizationTests.FirstId) }, new ColumnSet(true), degree, deadline.Token));
            service.VerifyNoOtherCalls();
        }

        /// <summary>任一SDK回應的logical name與Id必須完全對應本次請求，不能用別表同Guid污染結果。</summary>
        [Fact]
        public async Task ParallelRetrieve_RejectsWrongReturnedEntity()
        {
            var service = new Mock<IOrganizationService>(MockBehavior.Strict);
            service.Setup(item => item.Retrieve("contact", ContactCharacterizationTests.FirstId, It.IsAny<ColumnSet>()))
                .Returns(new Entity("account", ContactCharacterizationTests.FirstId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Object.ParallelRetrieveAsync(
                new[] { ("contact", ContactCharacterizationTests.FirstId) }, new ColumnSet(true)));
        }

        /// <summary>分頁查詢在成功或錯誤都還原caller的PageInfo物件與值，並跨頁拒絕重複穩定ID。</summary>
        [Fact]
        public async Task RetrieveAll_RestoresCallerPagingAndRejectsDuplicateAcrossPages()
        {
            var original = new PagingInfo { Count = 17, PageNumber = 7, PagingCookie = "caller-cookie", ReturnTotalRecordCount = true };
            var query = new QueryExpression("contact") { ColumnSet = new ColumnSet(true), PageInfo = original };
            var calls = 0;
            var service = new Mock<IOrganizationService>(MockBehavior.Strict);
            service.Setup(item => item.RetrieveMultiple(query)).Returns(() =>
            {
                calls++;
                Assert.Equal(2, query.PageInfo.Count);
                Assert.Equal(calls, query.PageInfo.PageNumber);
                return new EntityCollection(new[] { new Entity("contact", ContactCharacterizationTests.FirstId) })
                {
                    MoreRecords = calls == 1,
                    PagingCookie = calls == 1 ? "next-cookie" : null
                };
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Object.RetrieveAllAsync(query, 2));
            Assert.Same(original, query.PageInfo);
            Assert.Equal(17, original.Count);
            Assert.Equal(7, original.PageNumber);
            Assert.Equal("caller-cookie", original.PagingCookie);
            Assert.Equal(2, calls);
        }

        /// <summary>後頁故障必須保留同一Exception且恢復caller輸入，不回傳先前候選的部分清單。</summary>
        [Fact]
        public async Task RetrieveAll_FailureRestoresCallerPagingAndPreservesException()
        {
            var original = new PagingInfo { Count = 19, PageNumber = 4, PagingCookie = "original" };
            var query = new QueryExpression("contact") { ColumnSet = new ColumnSet(true), PageInfo = original };
            var failure = new TimeoutException("synthetic-private");
            var calls = 0;
            var service = new Mock<IOrganizationService>(MockBehavior.Strict);
            service.Setup(item => item.RetrieveMultiple(query)).Returns(() =>
            {
                calls++;
                if (calls == 2) throw failure;
                return new EntityCollection(new[] { new Entity("contact", ContactCharacterizationTests.FirstId) })
                    { MoreRecords = true, PagingCookie = "next" };
            });
            Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(() => service.Object.RetrieveAllAsync(query, 2)));
            Assert.Same(original, query.PageInfo);
            Assert.Equal(19, original.Count);
            Assert.Equal(4, original.PageNumber);
            Assert.Equal("original", original.PagingCookie);
            Assert.Equal(2, calls);
        }

        /// <summary>批次查詢拒絕不可信的重複候選與超量輸入，不能覆寫或無界保留字典項目。</summary>
        [Fact]
        public async Task BatchRetrieve_RejectsDuplicateRowsAndOversizedInput()
        {
            var service = new Mock<IOrganizationService>(MockBehavior.Strict);
            service.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection(new[]
            {
                new Entity("contact", ContactCharacterizationTests.FirstId),
                new Entity("contact", ContactCharacterizationTests.FirstId)
            }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.Object.BatchRetrieveAsync("contact",
                new[] { ContactCharacterizationTests.FirstId }, new ColumnSet(true)));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.Object.BatchRetrieveAsync("contact",
                Enumerable.Range(1, 5001).Select(index => new Guid(index, 0, 0, new byte[8])), new ColumnSet(true)));
            service.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>所有六種單次Task入口對caller預取消不送SDK；內部timeout維持faulted，正常取消才canceled。</summary>
        [Theory]
        [InlineData("retrieve")] [InlineData("multiple")] [InlineData("create")]
        [InlineData("update")] [InlineData("delete")] [InlineData("execute")]
        public async Task SingleOperations_PreserveCancellationOwnership(string operation)
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var task = Invoke(sdk.Object, operation, cancelled.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.True(task.IsCanceled);
            sdk.VerifyNoOtherCalls();
            var timeout = new OperationCanceledException("synthetic-internal-timeout", cancelled.Token);
            sdk.Setup(item => item.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>())).Throws(timeout);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(timeout);
            sdk.Setup(item => item.Create(It.IsAny<Entity>())).Throws(timeout);
            sdk.Setup(item => item.Update(It.IsAny<Entity>())).Throws(timeout);
            sdk.Setup(item => item.Delete(It.IsAny<string>(), It.IsAny<Guid>())).Throws(timeout);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Throws(timeout);
            task = Invoke(sdk.Object, operation, CancellationToken.None);
            Assert.Same(timeout, await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task));
            Assert.True(task.IsFaulted);
        }

        /// <summary>單次成功I/O後caller取消也不發布資料；Task.Run之外的輕量wrapper維持同步SDK所有權。</summary>
        [Fact]
        public async Task CallerCancellationDuringIo_DoesNotPublishResponse()
        {
            using var caller = new CancellationTokenSource();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
            {
                caller.Cancel();
                return new EntityCollection(new[] { new Entity("contact", ContactCharacterizationTests.FirstId) });
            });
            var task = sdk.Object.RetrieveMultipleAsync(new QueryExpression("contact"), caller.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.True(task.IsCanceled);
        }

        /// <summary>三個worker真正同時進入SDK且不超上限；故障不讓方法提早返回，必須等另外兩個SDK工作排空。</summary>
        [Fact]
        public async Task ParallelWorkers_AreBoundedAndDrainBeforeReturningFailure()
        {
            using var entered = new CountdownEvent(3);
            using var release = new ManualResetEventSlim();
            var active = 0;
            var maximum = 0;
            var failure = new TimeoutException("synthetic");
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Retrieve("contact", It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                .Returns((string name, Guid id, ColumnSet _) =>
                {
                    var current = Interlocked.Increment(ref active);
                    lock (entered) maximum = Math.Max(maximum, current);
                    entered.Signal();
                    try
                    {
                        Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                        if (id == ContactCharacterizationTests.FirstId) throw failure;
                        return new Entity(name, id);
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            var task = sdk.Object.ParallelRetrieveAsync(new[]
            {
                ("contact", ContactCharacterizationTests.FirstId),
                ("contact", ContactCharacterizationTests.SecondId),
                ("contact", Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"))
            }, new ColumnSet(true), 3);
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                Assert.Equal(3, active);
                Assert.False(task.IsCompleted);
            }
            finally { release.Set(); }
            Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(() => task));
            Assert.Equal(3, maximum);
            Assert.Equal(0, active);
        }

        /// <summary>一千次A/Bscope操作使用不同SDK且沒有靜態候選；每次worker全部drain後才回傳，caller mutation不回寫。</summary>
        [Fact]
        public async Task ParallelScopes_AndRepeatedDrain_DoNotShareResults()
        {
            var active = 0;
            var a = Scope("A"); var b = Scope("B");
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(async index =>
            {
                var result = await (index % 2 == 0 ? a : b).ParallelRetrieveAsync(
                    new[] { ("contact", ContactCharacterizationTests.FirstId) }, new ColumnSet(true), 1);
                var row = Assert.Single(result.Values);
                Assert.Equal(index % 2 == 0 ? "A" : "B", row["fullname"]);
                row["fullname"] = "caller";
            }));
            Assert.Equal(0, active);
            IOrganizationService Scope(string name)
            {
                var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
                sdk.Setup(item => item.Retrieve("contact", ContactCharacterizationTests.FirstId, It.IsAny<ColumnSet>()))
                    .Returns(() =>
                    {
                        Interlocked.Increment(ref active);
                        try { return new Entity("contact", ContactCharacterizationTests.FirstId) { ["fullname"] = name }; }
                        finally { Interlocked.Decrement(ref active); }
                    });
                return sdk.Object;
            }
        }

        /// <summary>頁budget、cookie、空ID、跨頁scope錯表及內部timeout都不能發布先前有效頁。</summary>
        [Theory]
        [InlineData("oversize")] [InlineData("cookie")] [InlineData("empty-id")] [InlineData("table")]
        public async Task FullPaging_RejectsUntrustedContinuation(string mode)
        {
            var original = new PagingInfo();
            var query = new QueryExpression("contact") { PageInfo = original };
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(query)).Returns(() => new EntityCollection(
                Enumerable.Range(1, mode == "oversize" ? 3 : 1)
                    .Select(index => new Entity(mode == "table" ? "account" : "contact",
                        mode == "empty-id" ? Guid.Empty : new Guid(index, 0, 0, new byte[8]))).ToArray())
            {
                MoreRecords = mode == "cookie",
                PagingCookie = mode == "cookie" ? new string('x', 65537) : null
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => sdk.Object.RetrieveAllAsync(query, 2));
            Assert.Same(original, query.PageInfo);
        }

        /// <summary>Core組件不依賴host副本；靜態擴充僅含固定budget常數，沒有資源owner。</summary>
        [Fact]
        public void AsyncExtensions_AreInPureCore() =>
            Assert.Equal("ToolUtility.Core", typeof(CrmAsyncExtensions).Assembly.GetName().Name);

        /// <summary>即使caller保留已完成Task，worker closure也不能繼續保留借入SDK；固定GC觀測補實際drain證據。</summary>
        [Fact]
        public async Task CompletedTask_DoesNotRetainBorrowedSdk()
        {
            var (task, sdk) = CreateWeakTask();
            Assert.Single(await task);
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            Assert.False(sdk.IsAlive);
            GC.KeepAlive(task);
        }

        /// <summary>SDK替身局部離開stack；Task完成後預期只保留caller字典，不保留連線owner。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (Task<Dictionary<Guid, Entity>>, WeakReference) CreateWeakTask()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Retrieve("contact", ContactCharacterizationTests.FirstId, It.IsAny<ColumnSet>()))
                .Returns(() => new Entity("contact", ContactCharacterizationTests.FirstId));
            return (sdk.Object.ParallelRetrieveAsync(new[] { ("contact", ContactCharacterizationTests.FirstId) },
                new ColumnSet(true)), new WeakReference(sdk.Object));
        }

        /// <summary>明確選六種原入口，Task對正常取消及故障分類由受測wrapper決定。</summary>
        private static Task Invoke(IOrganizationService sdk, string operation, CancellationToken token) => operation switch
        {
            "retrieve" => sdk.RetrieveAsync("contact", ContactCharacterizationTests.FirstId, new ColumnSet(true), token),
            "multiple" => sdk.RetrieveMultipleAsync(new QueryExpression("contact"), token),
            "create" => sdk.CreateAsync(new Entity("contact"), token),
            "update" => sdk.UpdateAsync(new Entity("contact", ContactCharacterizationTests.FirstId), token),
            "delete" => sdk.DeleteAsync("contact", ContactCharacterizationTests.FirstId, token),
            "execute" => sdk.ExecuteAsync(new OrganizationRequest("Synthetic"), token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：名單批次的輸入snapshot、借用SDK與完整頁發布生命週期；測試gate皆有界並在finally排空。
 *
 * 主要功能與流程：
 * 本檔宣告 ListLifecycleTests。
 * - 逐一驗六批次入口，不能只測共用helper而漏掉產品不同route。
 * - caller在同步SDK入場後取消／Dispose，Task仍等drain；新入口拒絕，借用連線Dispose=0且不啟後批。
 * - 第一筆入SDK後修改caller原List，後批仍用完整驗證的snapshot；同一owner最大active=1。
 * - 0／負批次值在任何I/O前拒絕，防止舊ChunkList無限列舉；不以真實卡死作測試。
 * 測試案例入口包含 Writes、CancellationAndDispose_DrainStartedWrite、InputSnapshot_AndSerialOwnership_AreStable、
 * InvalidBatchSize_RejectsBeforeIo、CompleteProjection_ValidatesEveryPage、
 * CompleteProjection_UsesDatabaseIdentityOrder、ConcurrentScopes_StayIsolatedAndDrain、
 * Ownership_ReleasesDependenciesAndCompletedTaskClosure；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Runtime.CompilerServices;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.ListOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>名單批次的輸入snapshot、借用SDK與完整頁發布生命週期；測試gate皆有界並在finally排空。</summary>
    public sealed class ListLifecycleTests
    {
        /// <summary>逐一驗六批次入口，不能只測共用helper而漏掉產品不同route。</summary>
        public static IEnumerable<object[]> Writes() => ListSafetyTests.WriteEntries.Select(entry => new object[] { entry });

        /// <summary>caller在同步SDK入場後取消／Dispose，Task仍等drain；新入口拒絕，借用連線Dispose=0且不啟後批。</summary>
        [Theory]
        [MemberData(nameof(Writes))]
        public async Task CancellationAndDispose_DrainStartedWrite(string entry)
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var cancellation = new CancellationTokenSource();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var active = 0; var calls = 0;
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
            {
                Interlocked.Increment(ref active); Interlocked.Increment(ref calls);
                try { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return Success(request); }
                finally { Interlocked.Decrement(ref active); }
            });
            using var list = new ListService(new object(), sdk.Object);
            using var marketing = new MarketingListService(new Mock<IEntityRepository>(MockBehavior.Strict).Object, sdk.Object);
            var task = Start(list, marketing, sdk.Object, entry, ListSafetyTests.Members(), 1, cancellation.Token);
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                cancellation.Cancel(); list.Dispose(); marketing.Dispose();
                Assert.False(task.IsCompleted); Assert.Equal(1, active);
                Assert.Throws<ObjectDisposedException>(() => list.RetrieveLists());
                Assert.Throws<ObjectDisposedException>(() => marketing.RetrieveMembersByListId(ListCharacterizationTests.ListId));
            }
            finally { release.Set(); }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.True(task.IsCanceled); Assert.Equal(0, active); Assert.Equal(1, calls);
            disposable.Verify(item => item.Dispose(), Times.Never);
        }

        /// <summary>第一筆入SDK後修改caller原List，後批仍用完整驗證的snapshot；同一owner最大active=1。</summary>
        [Theory]
        [MemberData(nameof(Writes))]
        public async Task InputSnapshot_AndSerialOwnership_AreStable(string entry)
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var ids = new List<Guid>(); var active = 0; var maximum = 0;
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
            {
                var current = Interlocked.Increment(ref active); maximum = Math.Max(maximum, current);
                try
                {
                    if (ids.Count == 0) { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); }
                    if (request is AddListMembersListRequest add) ids.AddRange(add.MemberIds);
                    else if (request is ExecuteMultipleRequest multiple)
                        ids.AddRange(multiple.Requests.Cast<RemoveMemberListRequest>().Select(item => item.EntityId));
                    else ids.Add(request is AddMemberListRequest single ? single.EntityId : ((RemoveMemberListRequest)request).EntityId);
                    return Success(request);
                }
                finally { Interlocked.Decrement(ref active); }
            });
            var input = ListSafetyTests.Members(); var expected = input.ToArray();
            var task = ListSafetyTests.Write(entry, sdk.Object, input, 2);
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                input.Clear(); input.Add(Guid.Empty);
            }
            finally { release.Set(); }
            Assert.Equal(3, await task); Assert.Equal(expected, ids);
            Assert.Equal(1, maximum); Assert.Equal(0, active);
        }

        /// <summary>0／負批次值在任何I/O前拒絕，防止舊ChunkList無限列舉；不以真實卡死作測試。</summary>
        [Theory]
        [InlineData(0)] [InlineData(-1)]
        public async Task InvalidBatchSize_RejectsBeforeIo(int size)
        {
            foreach (var entry in ListSafetyTests.WriteEntries)
            {
                var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ListSafetyTests.Write(entry, sdk.Object,
                    ListSafetyTests.Members(), size));
                sdk.VerifyNoOtherCalls();
            }
        }

        /// <summary>三種完整名單讀取必須續頁且延後發布；後頁故障／重複／壞cookie不得回第一頁，明確重試可成功。</summary>
        [Theory]
        [InlineData("static", "success")] [InlineData("dynamic", "success")] [InlineData("marketing", "success")]
        [InlineData("static", "failure")] [InlineData("dynamic", "failure")] [InlineData("marketing", "failure")]
        [InlineData("static", "duplicate")] [InlineData("dynamic", "duplicate")] [InlineData("marketing", "duplicate")]
        [InlineData("static", "cookie")] [InlineData("dynamic", "cookie")] [InlineData("marketing", "cookie")]
        public void CompleteProjection_ValidatesEveryPage(string kind, string mode)
        {
            var calls = 0; var fail = mode != "success";
            var fault = new TimeoutException("synthetic-private");
            EntityCollection Read(QueryBase _)
            {
                calls++;
                if (calls == 2 && fail && mode == "failure") throw fault;
                var id = calls % 2 == 1 || (fail && mode == "duplicate")
                    ? ListCharacterizationTests.FirstId : ListCharacterizationTests.SecondId;
                var row = new Entity(kind == "dynamic" ? "contact" : "listmember", id)
                { [kind == "dynamic" ? "contactid" : "entityid"] = id };
                return new EntityCollection(new[] { row })
                { MoreRecords = calls % 2 == 1, PagingCookie = fail && mode == "cookie" ? new string('x', 65537) : "synthetic" };
            }
            var sdk = ListSafetyTests.Source(Read);
            sdk.Setup(item => item.Retrieve("list", ListCharacterizationTests.ListId, It.IsAny<ColumnSet>()))
                .Returns(new Entity("list", ListCharacterizationTests.ListId)
                { ["type"] = kind == "dynamic", ["query"] = RepositoryQueryCharacterizationTests.Fetch });
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
            repository.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) => Read(query));
            using var list = new ListService(new object(), sdk.Object);
            using var marketing = new MarketingListService(repository.Object, sdk.Object);
            System.Collections.ArrayList ReadAll() => kind == "marketing" ? marketing.GetAllMemberData(ListCharacterizationTests.ListId)
                : list.GetAllMemberDataFromList(ListCharacterizationTests.ListId);
            if (fail)
            {
                var actual = Assert.ThrowsAny<Exception>(() => ReadAll());
                if (mode == "failure") Assert.Same(fault, actual);
                else Assert.IsType<InvalidOperationException>(actual);
                fail = false; calls = 0;
            }
            Assert.Equal(new[] { ListCharacterizationTests.FirstId, ListCharacterizationTests.SecondId }, ReadAll().Cast<Guid>());
            Assert.Equal(2, calls);
        }

        /// <summary>只有完整名單投影增加資料庫主鍵穩定排序；單頁既有golden不改，避免續頁在同名資料上不穩定。</summary>
        [Theory]
        [InlineData("static")] [InlineData("dynamic")] [InlineData("marketing")]
        public void CompleteProjection_UsesDatabaseIdentityOrder(string kind)
        {
            EntityCollection Read(QueryBase query)
            {
                if (query is FetchExpression fetch)
                {
                    var xml = System.Xml.Linq.XElement.Parse(fetch.Query);
                    Assert.Contains(xml.Element("entity")!.Elements("order"), order =>
                        (string?)order.Attribute("attribute") == "contactid" && (string?)order.Attribute("descending") == "false");
                }
                else
                {
                    var orders = query is QueryExpression expression ? expression.Orders : ((QueryByAttribute)query).Orders;
                    var order = Assert.Single(orders); Assert.Equal("listmemberid", order.AttributeName);
                    Assert.Equal(OrderType.Ascending, order.OrderType);
                }
                return new EntityCollection();
            }
            var sdk = ListSafetyTests.Source(Read);
            sdk.Setup(item => item.Retrieve("list", ListCharacterizationTests.ListId, It.IsAny<ColumnSet>()))
                .Returns(new Entity("list", ListCharacterizationTests.ListId)
                { ["type"] = kind == "dynamic", ["query"] = RepositoryQueryCharacterizationTests.Fetch });
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
            repository.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) => Read(query));
            using var list = new ListService(new object(), sdk.Object);
            using var marketing = new MarketingListService(repository.Object, sdk.Object);
            Assert.Empty(kind == "marketing" ? marketing.GetAllMemberData(ListCharacterizationTests.ListId)
                : list.GetAllMemberDataFromList(ListCharacterizationTests.ListId));
        }

        /// <summary>1000次不同scope讀寫，結果與寫入SDK皆屬各自scope；caller mutation不回寫，排空active=0。</summary>
        [Fact]
        public async Task ConcurrentScopes_StayIsolatedAndDrain()
        {
            var active = 0;
            var a = Scope(ListCharacterizationTests.FirstId); var b = Scope(ListCharacterizationTests.SecondId);
            using var listA = new ListService(new object(), a.Item1); using var listB = new ListService(new object(), b.Item1);
            using var marketingA = new MarketingListService(a.Item2, a.Item1); using var marketingB = new MarketingListService(b.Item2, b.Item1);
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(async () =>
            {
                var list = index % 2 == 0 ? listA : listB; var marketing = index % 2 == 0 ? marketingA : marketingB;
                var id = index % 2 == 0 ? ListCharacterizationTests.FirstId : ListCharacterizationTests.SecondId;
                var row = Assert.Single((index % 3 == 0 ? list.RetrieveLists() : marketing.RetrieveMembersByListId(id)).Entities);
                Assert.Equal(id, row.Id); Assert.Equal("same-name", row["new_name"]); row["new_name"] = "caller";
                Assert.Equal(1, await list.AddMembersAsync(id, new List<Guid> { id }));
            })));
            Assert.Equal(0, active);

            (IOrganizationService, IEntityRepository) Scope(Guid id)
            {
                EntityCollection Page(string table)
                {
                    Interlocked.Increment(ref active);
                    try { return new EntityCollection(new[] { new Entity(table, id) { ["new_name"] = "same-name" } }); }
                    finally { Interlocked.Decrement(ref active); }
                }
                var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
                sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() => Page("list"));
                sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        var add = Assert.IsType<AddListMembersListRequest>(request);
                        Assert.Equal(id, add.ListId); Assert.Equal(id, Assert.Single(add.MemberIds)); return new OrganizationResponse();
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
                var repository = new Mock<IEntityRepository>(MockBehavior.Strict);
                repository.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() => Page("listmember"));
                return (sdk.Object, repository.Object);
            }
        }

        /// <summary>服務Dispose解除依賴、已完成Task即使被保留也不保留SDK；有限GC驗證補實際排空。</summary>
        [Fact]
        public async Task Ownership_ReleasesDependenciesAndCompletedTaskClosure()
        {
            var (list, marketing, sdk, repository, task, taskSdk) = Weak();
            Assert.Equal(1, await task);
            Collect(); Assert.False(taskSdk.IsAlive);
            list.Dispose(); marketing.Dispose(); Collect();
            Assert.False(sdk.IsAlive); Assert.False(repository.IsAlive);
            GC.KeepAlive(list); GC.KeepAlive(marketing); GC.KeepAlive(task);
        }

        /// <summary>依入口轉接真實服務，保留同一個scope下的owner物件供Dispose競爭測試。</summary>
        private static Task<int> Start(ListService list, MarketingListService marketing, IOrganizationService sdk, string entry,
            List<Guid> input, int size, CancellationToken token) => entry switch
        {
            "list-add" => list.AddMembersAsync(ListCharacterizationTests.ListId, input, size, token),
            "list-sdk" => list.AddMembersUsingSdkAsync(ListCharacterizationTests.ListId, input, sdk, size, token),
            "list-remove" => list.RemoveMembersAsync(ListCharacterizationTests.ListId, input, size, token),
            "marketing-add" => marketing.AddMembersAsync(ListCharacterizationTests.ListId, input, size, token),
            "marketing-sdk" => marketing.AddMembersUsingSdkAsync(ListCharacterizationTests.ListId, input, sdk, size, token),
            _ => marketing.RemoveMembersAsync(ListCharacterizationTests.ListId, input, size, token)
        };

        /// <summary>只提供符合原生SDK的成功回應，不替產品處理Fault或ID。</summary>
        private static OrganizationResponse Success(OrganizationRequest request) => request is ExecuteMultipleRequest
            ? new ExecuteMultipleResponse { Results = { ["Responses"] = new ExecuteMultipleResponseItemCollection() } }
            : new OrganizationResponse();

        /// <summary>移除JIT堆疊偽owner；第二個SDK只由獨立批次Task持有，完成後必須回收。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (ListService, MarketingListService, WeakReference, WeakReference, Task<int>, WeakReference) Weak()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
            var repository = new Mock<IEntityRepository>(MockBehavior.Strict).Object;
            var taskSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            taskSdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns(new OrganizationResponse());
            var task = ListSafetyTests.Write("marketing-add", taskSdk.Object, new List<Guid> { ListCharacterizationTests.FirstId }, 1);
            return (new ListService(new object(), sdk), new MarketingListService(repository, sdk), new WeakReference(sdk),
                new WeakReference(repository), task, new WeakReference(taskSdk.Object));
        }

        /// <summary>固定三輪GC，沒有無界重試或依賴記憶體門檻猜測成功。</summary>
        private static void Collect()
        {
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        }
    }
}

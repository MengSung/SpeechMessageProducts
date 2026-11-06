/*
 * 檔案整體說明
 *
 * 目的與定位：Trace listener 為程序集合，這組測試獨占執行以免干擾其他診斷測試；不清空外部 listener。
 *
 * 主要功能與流程：
 * 本檔宣告 RepositoryTraceCollection、RepositorySafetyTests、NonRetainingSdk、MarkerListener。
 * - 以反射在新 overload 尚不存在時取得可執行紅測；公開三參數入口須保留原 ColumnSet 參照。 不把 overload 加到既有介面而破壞外部實作者，兩參數公開契約亦必須繼續存在。
 * - 六入口遇到真正 SDK 逾時或 ArgumentException 都維持 InvalidOperationException wrapper， 同一 InnerException、固定訊息及單次 I/O；不能以 
 * catch ArgumentException 一律重拋而誤改 SDK 故障契約。
 * - SDK 未確認建立 ID 時拒絕回報成功，不盲目重試或改寫 caller 的未建立候選。
 * - 單筆缺失、錯 ID 或跨表回應都拒絕，不能把 ColumnSet 子集當成缺少識別的理由。
 * 測試案例入口包含 QueryColumnOverload_PreservesColumnsWithoutChangingExistingInterface、
 * SdkFailure_PreservesRealExceptionAndFixedOuterMessage、Create_EmptySdkIdentity_RejectsWithoutRetry、
 * Retrieve_InvalidIdentity_RejectsWholeResponse、RetrieveMultiple_InvalidPage_RejectsBeforePublication、
 * AggregateProjectionWithoutRecordIdentity_RejectsWithoutInventingId、AdapterInvalidInput_RejectsWithoutSdk、
 * FailureRetryAndCallerMutation_UseFreshCandidatesAndBorrowSdk；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>Trace listener 為程序集合，這組測試獨占執行以免干擾其他診斷測試；不清空外部 listener。</summary>
    [CollectionDefinition("Repository trace isolation", DisableParallelization = true)]
    public sealed class RepositoryTraceCollection { }

    /// <summary>
    /// 保護 Repository 搬移新增的 fail-closed、固定安全包裝、借入 SDK 所有權與無快取隔離。
    /// 故障均由合成 SDK 邊界注入；驗證不代表真正 D365 或跨程序資料庫唯一索引驗收。
    /// </summary>
    [Collection("Repository trace isolation")]
    public sealed class RepositorySafetyTests
    {
        private static readonly Guid RecordId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid OwnerId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>
        /// 以反射在新 overload 尚不存在時取得可執行紅測；公開三參數入口須保留原 ColumnSet 參照。
        /// 不把 overload 加到既有介面而破壞外部實作者，兩參數公開契約亦必須繼續存在。
        /// </summary>
        /// <param name="explicitColumns">是否提供精確欄位子集；null 保持 AllColumns 舊預設。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void QueryColumnOverload_PreservesColumnsWithoutChangingExistingInterface(bool explicitColumns)
        {
            var overload = typeof(EntityQueryService).GetMethod("RetrieveEntity",
                new[] { typeof(string), typeof(Guid), typeof(ColumnSet) });
            Assert.NotNull(overload);
            Assert.NotNull(typeof(EntityQueryService).GetMethod("RetrieveEntity", new[] { typeof(string), typeof(Guid) }));
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var columns = explicitColumns ? new ColumnSet("fullname") : null;
            var expected = new Entity("contact", RecordId);
            ColumnSet? sent = null;
            sdk.Setup(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()))
                .Callback<string, Guid, ColumnSet>((_, _, value) => sent = value).Returns(expected);
            using var service = new EntityQueryService(organizationService: sdk.Object);
            Assert.Same(expected, overload!.Invoke(service, new object?[] { "contact", RecordId, columns }));
            if (explicitColumns) Assert.Same(columns, sent);
            else Assert.True(sent!.AllColumns);
            sdk.Verify(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 六入口遇到真正 SDK 逾時或 ArgumentException 都維持 InvalidOperationException wrapper，
        /// 同一 InnerException、固定訊息及單次 I/O；不能以 catch ArgumentException 一律重拋而誤改 SDK 故障契約。
        /// </summary>
        /// <param name="operation">合成 SDK 操作。</param>
        /// <param name="argumentFault">是否注入 SDK 自身的參數例外。</param>
        [Theory]
        [InlineData("create", false)]
        [InlineData("update", false)]
        [InlineData("delete", false)]
        [InlineData("retrieve", false)]
        [InlineData("multiple", false)]
        [InlineData("assign", false)]
        [InlineData("create", true)]
        [InlineData("update", true)]
        [InlineData("delete", true)]
        [InlineData("retrieve", true)]
        [InlineData("multiple", true)]
        [InlineData("assign", true)]
        public void SdkFailure_PreservesRealExceptionAndFixedOuterMessage(string operation, bool argumentFault)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Exception failure = argumentFault ? new ArgumentException("synthetic_private_transport")
                : new TimeoutException("synthetic_private_transport");
            var entity = new Entity("p1_repository_marker", RecordId);
            var query = new QueryExpression(entity.LogicalName);
            if (operation == "create") sdk.Setup(s => s.Create(entity)).Throws(failure);
            else if (operation == "update") sdk.Setup(s => s.Update(entity)).Throws(failure);
            else if (operation == "delete") sdk.Setup(s => s.Delete(entity.LogicalName, RecordId)).Throws(failure);
            else if (operation == "retrieve") sdk.Setup(s => s.Retrieve(entity.LogicalName, RecordId, It.IsAny<ColumnSet>())).Throws(failure);
            else if (operation == "multiple") sdk.Setup(s => s.RetrieveMultiple(query)).Throws(failure);
            else sdk.Setup(s => s.Execute(It.IsAny<AssignRequest>())).Throws(failure);
            var repository = new EntityRepository(sdk.Object);
            var actual = Assert.Throws<InvalidOperationException>(() => Invoke(repository, operation, entity, query));
            Assert.Same(failure, actual.InnerException);
            Assert.Equal(Message(operation), actual.Message);
            sdk.VerifyAll();
            Assert.Single(sdk.Invocations);
        }

        /// <summary>SDK 未確認建立 ID 時拒絕回報成功，不盲目重試或改寫 caller 的未建立候選。</summary>
        [Fact]
        public void Create_EmptySdkIdentity_RejectsWithoutRetry()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("contact");
            sdk.Setup(s => s.Create(entity)).Returns(Guid.Empty);
            var failure = Assert.Throws<InvalidOperationException>(() => new EntityRepository(sdk.Object).Create(entity));
            Assert.Equal(Message("create"), failure.Message);
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Equal(Guid.Empty, entity.Id);
            sdk.Verify(s => s.Create(entity), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>單筆缺失、錯 ID 或跨表回應都拒絕，不能把 ColumnSet 子集當成缺少識別的理由。</summary>
        /// <param name="shape">合成回應的不完整形狀。</param>
        [Theory]
        [InlineData("missing")]
        [InlineData("wrong-id")]
        [InlineData("wrong-table")]
        public void Retrieve_InvalidIdentity_RejectsWholeResponse(string shape)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var candidate = shape == "missing" ? null : new Entity(shape == "wrong-table" ? "account" : "contact",
                shape == "wrong-id" ? OwnerId : RecordId);
            sdk.Setup(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>())).Returns(candidate!);
            var failure = Assert.Throws<InvalidOperationException>(() => new EntityRepository(sdk.Object)
                .Retrieve("contact", RecordId, new ColumnSet(false)));
            Assert.Equal(Message("retrieve"), failure.Message);
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            sdk.Verify(s => s.Retrieve("contact", RecordId, It.IsAny<ColumnSet>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>整頁缺失、空 ID、重複 ID 或跨表一律拒絕；不修剪原候選以偽造成功。</summary>
        /// <param name="shape">不合法頁面形狀。</param>
        /// <param name="fetch">是否經 FetchXML 根實體辨識。</param>
        [Theory]
        [InlineData("missing", false)]
        [InlineData("duplicate", false)]
        [InlineData("empty", false)]
        [InlineData("wrong-table", false)]
        [InlineData("missing", true)]
        [InlineData("duplicate", true)]
        [InlineData("empty", true)]
        [InlineData("wrong-table", true)]
        public void RetrieveMultiple_InvalidPage_RejectsBeforePublication(string shape, bool fetch)
        {
            var page = new EntityCollection();
            page.Entities.Add(new Entity("contact", RecordId));
            page.Entities.Add(new Entity(shape == "wrong-table" ? "account" : "contact",
                shape == "empty" ? Guid.Empty : shape == "duplicate" ? RecordId : OwnerId));
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            QueryBase query = fetch ? new FetchExpression("<fetch><entity name='contact'/></fetch>") : new QueryExpression("contact");
            sdk.Setup(s => s.RetrieveMultiple(query)).Returns(shape == "missing" ? null! : page);
            var failure = Assert.Throws<InvalidOperationException>(() => new EntityRepository(sdk.Object).RetrieveMultiple(query));
            Assert.Equal(Message("multiple"), failure.Message);
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Equal(2, page.Entities.Count);
            sdk.Verify(s => s.RetrieveMultiple(query), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 聚合投影不等於正式資料列；SDK count列缺資料庫ID必須拒絕，不補造ID或把0筆偽裝成功。
        /// 本測試具體化既有空ID紅測所保護的契約，不新增聚合功能或修改查詢。
        /// </summary>
        [Fact]
        public void AggregateProjectionWithoutRecordIdentity_RejectsWithoutInventingId()
        {
            var query = new FetchExpression("<fetch aggregate='true'><entity name='contact'><attribute name='contactid' alias='total' aggregate='count'/></entity></fetch>");
            var row = new Entity("contact") { ["total"] = new AliasedValue("contact", "contactid", 2) };
            var page = new EntityCollection(new[] { row });
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(query)).Returns(page);
            var failure = Assert.Throws<InvalidOperationException>(() => new EntityRepository(sdk.Object).RetrieveMultiple(query));
            Assert.Equal(Message("multiple"), failure.Message);
            Assert.IsType<InvalidOperationException>(failure.InnerException);
            Assert.Same(row, Assert.Single(page.Entities));
            Assert.Equal(Guid.Empty, row.Id);
            Assert.Equal(2, Assert.IsType<AliasedValue>(row["total"]).Value);
            sdk.Verify(s => s.RetrieveMultiple(query), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 新增 adapter 的輸入防禦在 I/O 前拒絕，維持舊入口對原本未檢查情況的操作失敗包裝。
        /// 這是明列的安全差異，不宣稱原 host 已驗證名稱、Assign 目標一致性或非空 ID。
        /// </summary>
        /// <param name="shape">原 host 未檢查的合成輸入。</param>
        [Theory]
        [InlineData("create-name")]
        [InlineData("update-name")]
        [InlineData("assign-name")]
        [InlineData("assign-id")]
        [InlineData("fetch-dtd")]
        [InlineData("fetch-two-roots")]
        public void AdapterInvalidInput_RejectsWithoutSdk(string shape)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var repository = new EntityRepository(sdk.Object);
            var failure = Assert.Throws<InvalidOperationException>(() =>
            {
                if (shape == "create-name") repository.Create(new Entity());
                else if (shape == "update-name") repository.Update(new Entity { Id = RecordId });
                else if (shape == "assign-name") repository.AssignOwner("contact", new Entity("account", RecordId), OwnerId);
                else if (shape == "assign-id") repository.AssignOwner("contact", new Entity("contact"), OwnerId);
                else repository.RetrieveMultiple(new FetchExpression(shape == "fetch-dtd"
                    ? "<!DOCTYPE fetch [<!ENTITY value 'synthetic'>]><fetch><entity name='contact'/></fetch>"
                    : "<fetch><entity name='contact'/><entity name='account'/></fetch>"));
            });
            Assert.IsAssignableFrom<ArgumentException>(failure.InnerException);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>合法空頁、caller 明確重試及原分頁補值皆可用；失敗或 caller 修改不留存在 Repository。</summary>
        [Fact]
        public void FailureRetryAndCallerMutation_UseFreshCandidatesAndBorrowSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var fault = new TimeoutException("synthetic_private_transport");
            var query = new QueryExpression("contact") { PageInfo = null };
            sdk.SetupSequence(s => s.RetrieveMultiple(query)).Throws(fault)
                .Returns(new EntityCollection()).Returns(new EntityCollection());
            var repository = new EntityRepository(sdk.Object);
            Assert.Same(fault, Assert.Throws<InvalidOperationException>(() => repository.RetrieveMultiple(query)).InnerException);
            var first = repository.RetrieveMultiple(query);
            Assert.Empty(first.Entities);
            first.Entities.Add(new Entity("contact", RecordId));
            var next = repository.RetrieveMultiple(query);
            Assert.Empty(next.Entities);
            Assert.NotSame(first, next);
            Assert.NotNull(query.PageInfo);
            Assert.Equal(5000, query.PageInfo.Count);
            Assert.Equal(1, query.PageInfo.PageNumber);
            disposable.Verify(d => d.Dispose(), Times.Never);
            sdk.Verify(s => s.RetrieveMultiple(query), Times.Exactly(3));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A/B 已授權 SDK scope 各持自己的 Repository，執行 1000 次含故障及重試的有界操作。
        /// Task 全數 await、活動 SDK 計數回零；相同名稱與不同版本／使用者值不跨 scope、不保存結果、不處置借入 SDK。
        /// </summary>
        /// <returns>測試擁有並完整排空的有限工作集合。</returns>
        [Fact]
        public async Task ConcurrentScopes_FaultsRetryAndMutation_DoNotCrossTalkAndDrain()
        {
            var active = 0;
            var finished = 0;
            var failures = 0;
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            for (var scope = 0; scope < clients.Length; scope++)
            {
                var expected = scope == 0 ? "scope-a" : "scope-b";
                clients[scope].As<IDisposable>();
                clients[scope].Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        var query = Assert.IsType<QueryByAttribute>(input);
                        Assert.Equal(expected, query.Values[0]);
                        if ((bool)query.Values[2]) throw new TimeoutException("synthetic_retry");
                        var row = new Entity("contact", Guid.NewGuid())
                            { ["fullname"] = "合成同名", ["scope"] = expected, ["version"] = query.Values[1] };
                        return new EntityCollection(new[] { row });
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            }
            var repositories = clients.Select(sdk => new EntityRepository(sdk.Object)).ToArray();
            var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var scope = index % 2;
                var expected = scope == 0 ? "scope-a" : "scope-b";
                var query = new QueryByAttribute("contact");
                query.AddAttributeValue("scope", expected);
                query.AddAttributeValue("version", index);
                query.AddAttributeValue("inject", index % 17 == 0);
                if (index % 17 == 0)
                {
                    Assert.IsType<TimeoutException>(Assert.Throws<InvalidOperationException>(() => repositories[scope].RetrieveMultiple(query)).InnerException);
                    Interlocked.Increment(ref failures);
                    query.Values[2] = false;
                }
                var first = Assert.Single(repositories[scope].RetrieveMultiple(query).Entities);
                Assert.Equal(expected, first["scope"]);
                Assert.Equal(index, first["version"]);
                first["scope"] = "caller-mutated";
                var second = Assert.Single(repositories[scope].RetrieveMultiple(query).Entities);
                Assert.Equal(expected, second["scope"]);
                Assert.Equal(index, second["version"]);
                Assert.NotSame(first, second);
                Interlocked.Increment(ref finished);
            })).ToArray();
            await Task.WhenAll(tasks);
            Assert.All(tasks, task => Assert.True(task.IsCompleted));
            Assert.Equal(1000, Volatile.Read(ref finished));
            Assert.Equal(59, Volatile.Read(ref failures));
            Assert.Equal(0, Volatile.Read(ref active));
            foreach (var sdk in clients) sdk.As<IDisposable>().Verify(d => d.Dispose(), Times.Never);
        }

        /// <summary>
        /// 保持 Repository 存活時，已返回的 Entity／Query／頁面必須可回收。
        /// SDK callback 只產生新物件而不記錄呼叫；NoInlining 隔離 JIT 區域，GC 後三個弱參照皆失效。
        /// </summary>
        [Fact]
        public void CompletedOperation_DoesNotRetainQueryPageOrEntity()
        {
            var repository = CreateAfterOperation(out var query, out var page, out var row);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(query.IsAlive);
            Assert.False(page.IsAlive);
            Assert.False(row.IsAlive);
            GC.KeepAlive(repository);
        }

        /// <summary>
        /// 成功與故障皆不得把實體名稱、資料庫 ID 或原例外訊息寫到程序 Trace。
        /// listener 只計本測試的合成 marker，不保存任何訊息，finally 僅移除自己並 Dispose。
        /// </summary>
        [Fact]
        public void Trace_DoesNotPublishOperationDataOrRawFailure()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("p1_repository_marker", RecordId);
            sdk.SetupSequence(s => s.Create(entity)).Returns(RecordId).Throws(new TimeoutException("p1_repository_marker_private"));
            var listener = new MarkerListener();
            Trace.Listeners.Add(listener);
            try
            {
                var repository = new EntityRepository(sdk.Object);
                Assert.Equal(RecordId, repository.Create(entity));
                Assert.Throws<InvalidOperationException>(() => repository.Create(entity));
                Trace.Flush();
                Assert.Equal(0, listener.MatchCount);
            }
            finally
            {
                Trace.Listeners.Remove(listener);
                listener.Dispose();
            }
            Assert.Equal(1, listener.Disposals);
        }

        /// <summary>實際呼叫指定入口，不在 helper 模擬產品包裝或驗證邏輯。</summary>
        private static void Invoke(EntityRepository repository, string operation, Entity entity, QueryBase query)
        {
            if (operation == "create") repository.Create(entity);
            else if (operation == "update") repository.Update(entity);
            else if (operation == "delete") repository.Delete(entity.LogicalName, entity.Id);
            else if (operation == "retrieve") repository.Retrieve(entity.LogicalName, entity.Id);
            else if (operation == "multiple") repository.RetrieveMultiple(query);
            else repository.AssignOwner(entity.LogicalName, entity, OwnerId);
        }

        /// <summary>測試明定的固定公開操作訊息，不包含 logical name、ID 或故障本文。</summary>
        private static string Message(string operation) => operation == "create" ? "The entity could not be created."
            : operation == "update" ? "The entity could not be updated." : operation == "delete" ? "The entity could not be deleted."
            : operation == "retrieve" ? "The entity could not be retrieved."
            : operation == "multiple" ? "The entity collection could not be retrieved." : "The entity owner could not be assigned.";

        /// <summary>不使用 Moq 呼叫紀錄，避免測試替身自己保留待測物件而導致假陽性。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static EntityRepository CreateAfterOperation(out WeakReference queryReference, out WeakReference pageReference, out WeakReference rowReference)
        {
            var repository = new EntityRepository(new NonRetainingSdk());
            var query = new QueryExpression("contact");
            var page = repository.RetrieveMultiple(query);
            queryReference = new WeakReference(query);
            pageReference = new WeakReference(page);
            rowReference = new WeakReference(page.Entities[0]);
            return repository;
        }

        /// <summary>只在同步讀取產生新候選；不保存任何輸入、回應或 disposable 資源，其餘入口若被誤用立即失敗。</summary>
        private sealed class NonRetainingSdk : IOrganizationService
        {
            /// <summary>每次產生新頁與資料庫 ID，沒有 callback 或記錄集合。</summary>
            public EntityCollection RetrieveMultiple(QueryBase query) => new EntityCollection(new[] { new Entity("contact", RecordId) });
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public Guid Create(Entity entity) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public void Update(Entity entity) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public void Delete(string entityName, Guid id) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
            /// <summary>非本 fixture 契約，誤用即失敗。</summary>
            public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        }

        /// <summary>只保留有限計數器；不保存其他測試／使用者的 Trace 內容，生命周期由單一測試 finally 擁有。</summary>
        private sealed class MarkerListener : TraceListener
        {
            private int _matches;
            private int _disposals;
            /// <summary>以原子讀取觀察合成 marker 命中數。</summary>
            internal int MatchCount => Volatile.Read(ref _matches);
            /// <summary>驗證本測試擁有的 listener 恰釋放一次。</summary>
            internal int Disposals => Volatile.Read(ref _disposals);
            /// <summary>只比對合成字串；訊息離開此呼叫後不被保留。</summary>
            public override void Write(string? message)
            {
                if (message != null && (message.IndexOf("p1_repository_marker", StringComparison.Ordinal) >= 0
                    || message.IndexOf(RecordId.ToString(), StringComparison.Ordinal) >= 0))
                    Interlocked.Increment(ref _matches);
            }
            /// <summary>與 Write 使用同一有限計數，不建立診斷佇列。</summary>
            public override void WriteLine(string? message) => Write(message);
            /// <summary>僅增加處置計數後交基底清理；不接管全域 Trace 集合。</summary>
            protected override void Dispose(bool disposing)
            {
                if (disposing) Interlocked.Increment(ref _disposals);
                base.Dispose(disposing);
            }
        }
    }
}

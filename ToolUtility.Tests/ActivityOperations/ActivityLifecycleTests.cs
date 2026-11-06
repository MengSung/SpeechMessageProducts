/*
 * 檔案整體說明
 *
 * 目的與定位：保護舊 ArrayList 邊界、借用 SDK 生命週期及不同授權 scope 的並行隔離。 所有 I/O 為合成替身；測試擁有等待事件與工作並在 finally 排空，不連 VM／D365 或發 LINE。
 *
 * 主要功能與流程：
 * 本檔宣告 ActivityLifecycleTests、AdversarialList。
 * - 普通清單以外的 wrapper／衍生類及兩參數別名皆在 I/O 前拒絕，不能執行任意 Add callback。
 * - 來源 null／錯型集合及空欄位名稱皆拒絕，兩個入口保留固定安全包裝與前綴。
 * - 缺欄位、null 欄位、空集合沿用合法不追加語義；沒有 SDK 呼叫，也不重建或清除既有前綴。
 * - 5000 個既有正式 ID 合法；再 append 一列或前綴已超界時皆整體拒絕，不以分批突破總量。
 * 測試案例入口包含 UnsupportedList_RejectsWithoutAnySdkOrMutation、InvalidSource_RejectsWithoutIo、
 * EmptySource_PreservesPrefixWithoutSdk、TotalBudget_IncludesExistingPrefix、
 * ConcurrentScopes_FaultRetryMutationAndDrainRemainIsolated、
 * Dispose_RejectsNewWorkButAllowsStartedCallToDrainWithoutDisposingBorrowedSdk、
 * UnusedLoggerAndReleasedSdk_AreNotRetainedByLiveService、CreateRetentionProbe；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.ActivityOperations;
using Xunit;

namespace ToolUtility.Tests.ActivityOperations
{
    /// <summary>
    /// 保護舊 ArrayList 邊界、借用 SDK 生命週期及不同授權 scope 的並行隔離。
    /// 所有 I/O 為合成替身；測試擁有等待事件與工作並在 finally 排空，不連 VM／D365 或發 LINE。
    /// </summary>
    public sealed class ActivityLifecycleTests
    {
        private static readonly Guid Identity = new Guid("11111111-1111-1111-1111-111111111111");

        /// <summary>普通清單以外的 wrapper／衍生類及兩參數別名皆在 I/O 前拒絕，不能執行任意 Add callback。</summary>
        /// <param name="shape">不能保證獨占正常陣列複製的清單形狀。</param>
        /// <param name="ids">是否測純 ID 入口。</param>
        [Theory]
        [InlineData("alias", false)]
        [InlineData("alias", true)]
        [InlineData("readonly", false)]
        [InlineData("readonly", true)]
        [InlineData("fixed", false)]
        [InlineData("fixed", true)]
        [InlineData("subclass", false)]
        [InlineData("subclass", true)]
        public void UnsupportedList_RejectsWithoutAnySdkOrMutation(string shape, bool ids)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var values = shape == "readonly" ? ArrayList.ReadOnly(new ArrayList())
                : shape == "fixed" ? ArrayList.FixedSize(new ArrayList())
                : shape == "subclass" ? new AdversarialList() : new ArrayList();
            var types = shape == "alias" ? values : new ArrayList();
            Assert.Throws<InvalidOperationException>(() => Invoke(new ActivityService(new object(), sdk.Object), Input(Identity), values, types, ids));
            Assert.Empty(values);
            Assert.Empty(types);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>來源 null／錯型集合及空欄位名稱皆拒絕，兩個入口保留固定安全包裝與前綴。</summary>
        /// <param name="shape">不合法欄位或容器。</param>
        /// <param name="ids">是否測純 ID 入口。</param>
        [Theory]
        [InlineData("field", false)]
        [InlineData("field", true)]
        [InlineData("collection", false)]
        [InlineData("collection", true)]
        [InlineData("container", false)]
        [InlineData("container", true)]
        public void InvalidSource_RejectsWithoutIo(string shape, bool ids)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var input = Input(Identity);
            if (shape == "collection") input["to"] = "synthetic_wrong_type";
            if (shape == "container") ((EntityCollection)input["to"]).Entities[0] = null!;
            var service = new ActivityService(new object(), sdk.Object);
            var values = new ArrayList();
            var types = new ArrayList();
            var error = Assert.Throws<InvalidOperationException>(() =>
            {
                if (ids) service.GetActivityPartyIdList(input, shape == "field" ? " " : "to", values, types);
                else service.GetActivityPartyList(input, shape == "field" ? " " : "to", values, types);
            });
            Assert.Equal(ids ? "The activity party identities could not be retrieved." : "The activity parties could not be retrieved.", error.Message);
            Assert.Empty(values);
            Assert.Empty(types);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>缺欄位、null 欄位、空集合沿用合法不追加語義；沒有 SDK 呼叫，也不重建或清除既有前綴。</summary>
        /// <param name="shape">原本允許的空來源。</param>
        /// <param name="ids">是否測純 ID 入口。</param>
        [Theory]
        [InlineData("missing", false)]
        [InlineData("missing", true)]
        [InlineData("null", false)]
        [InlineData("null", true)]
        [InlineData("empty", false)]
        [InlineData("empty", true)]
        public void EmptySource_PreservesPrefixWithoutSdk(string shape, bool ids)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var input = new Entity("email");
            if (shape == "null") input["to"] = null;
            if (shape == "empty") input["to"] = new EntityCollection();
            object prefix = ids ? (object)Identity : new Entity("contact", Identity);
            var values = new ArrayList { prefix };
            var types = new ArrayList { "contact" };
            Invoke(new ActivityService(new object(), sdk.Object), input, values, types, ids);
            Assert.Equal(new[] { prefix }, values.ToArray());
            Assert.Equal(new[] { "contact" }, types.Cast<string>());
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>5000 個既有正式 ID 合法；再 append 一列或前綴已超界時皆整體拒絕，不以分批突破總量。</summary>
        /// <param name="overPrefix">是否一開始就有 5001 個既有 ID。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TotalBudget_IncludesExistingPrefix(bool overPrefix)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var values = new ArrayList(Enumerable.Range(0, overPrefix ? 5001 : 5000).Select(_ => (object)Guid.NewGuid()).ToArray());
            var types = new ArrayList(Enumerable.Repeat((object)"contact", values.Count).ToArray());
            var old = values.ToArray();
            var service = new ActivityService(new object(), sdk.Object);
            if (!overPrefix) service.GetActivityPartyIdList(new Entity("email"), "to", values, types);
            Assert.Throws<InvalidOperationException>(() => service.GetActivityPartyIdList(Input(Identity), "to", values, types));
            Assert.Equal(old, values.ToArray());
            Assert.Equal(old.Length, types.Count);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 1000 次並行 A/B 操作以同一 GUID 查不同 scope，59 次合成逾時後明確重試成功；
        /// 每次回傳新物件、caller mutation 不回流，工作／SDK 使用計數最後回到 0。
        /// </summary>
        [Fact]
        public async Task ConcurrentScopes_FaultRetryMutationAndDrainRemainIsolated()
        {
            var pendingFailures = new ConcurrentDictionary<Guid, byte>();
            var active = 0;
            var failures = 0;
            var calls = 0;
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            foreach (var indexed in clients.Select((client, index) => new { client, index }))
            {
                indexed.client.As<IDisposable>();
                indexed.client.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase source) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        Interlocked.Increment(ref calls);
                        var query = Assert.IsType<QueryExpression>(source);
                        var id = Assert.IsType<Guid>(Assert.Single(Assert.Single(query.Criteria.Conditions).Values));
                        if (pendingFailures.TryRemove(id, out _))
                        {
                            Interlocked.Increment(ref failures);
                            throw new TimeoutException("synthetic_private_fault");
                        }
                        return new EntityCollection(new[] { new Entity("contact", id) { ["scope"] = indexed.index } });
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            }
            var services = clients.Select(client => new ActivityService(new object(), client.Object)).ToArray();
            try
            {
                var tasks = Enumerable.Range(0, 1000).Select(index =>
                {
                    var id = index % 17 == 0 ? Guid.NewGuid() : Identity;
                    if (index % 17 == 0) Assert.True(pendingFailures.TryAdd(id, 0));
                    return Task.Run(() =>
                    {
                        var scope = index % 2;
                        var values = new ArrayList();
                        var types = new ArrayList();
                        var input = Input(id);
                        try { services[scope].GetActivityPartyList(input, "to", values, types); }
                        catch (InvalidOperationException failure) when (failure.InnerException is TimeoutException)
                        {
                            Assert.Empty(values);
                            Assert.Empty(types);
                            services[scope].GetActivityPartyList(input, "to", values, types);
                        }
                        var row = Assert.IsType<Entity>(Assert.Single(values.Cast<object>()));
                        Assert.Equal(scope, row["scope"]);
                        Assert.Equal(id, row.Id);
                        row["scope"] = "caller_changed";
                        values.Clear();
                        types.Clear();
                        services[scope].GetActivityPartyList(input, "to", values, types);
                        Assert.Equal(scope, Assert.IsType<Entity>(values[0])["scope"]);
                        Assert.Equal(new[] { "contact" }, types.Cast<string>());
                    });
                }).ToArray();
                await Task.WhenAll(tasks);
                Assert.Equal(0, active);
                Assert.Equal(59, failures);
                Assert.Equal(2059, calls);
                Assert.Empty(pendingFailures);
            }
            finally { foreach (var service in services) ((IDisposable)service).Dispose(); }
            foreach (var client in clients) client.As<IDisposable>().Verify(owner => owner.Dispose(), Times.Never);
        }

        /// <summary>
        /// Dispose 可與已開始的同步 I/O 競爭：新操作立即拒絕，既有局部借用安全完成且不釋放借入 SDK。
        /// 等待事件由此測試唯一擁有，finally 解除阻塞並等待工作排空，避免測試自身遺留 task／handle。
        /// </summary>
        [Fact]
        public async Task Dispose_RejectsNewWorkButAllowsStartedCallToDrainWithoutDisposingBorrowedSdk()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.As<IDisposable>();
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                return new EntityCollection(new[] { new Entity("contact", Identity) });
            });
            var service = new ActivityService(new object(), sdk.Object);
            var owner = Assert.IsAssignableFrom<IDisposable>(service);
            var values = new ArrayList();
            var types = new ArrayList();
            var work = Task.Run(() => service.GetActivityPartyList(Input(Identity), "to", values, types));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                owner.Dispose();
                owner.Dispose();
                Assert.Throws<ObjectDisposedException>(() => service.GetActivityPartyList(Input(Identity), "to", new ArrayList(), new ArrayList()));
                Assert.Throws<ObjectDisposedException>(() => service.GetActivityPartyIdList(Input(Identity), "to", new ArrayList(), new ArrayList()));
                Assert.Throws<ObjectDisposedException>(() => service.SetActivityStatusToCompleted("email", Identity));
                Assert.Throws<ObjectDisposedException>(() => service.SetActivityStatusToCompleted("email", Identity, sdk.Object));
                Assert.Throws<ObjectDisposedException>(() => service.SetAppointmentStatusToScheduled(Identity));
                Assert.Throws<ObjectDisposedException>(() => service.SetAppointmentStatusToScheduled(Identity, sdk.Object));
            }
            finally
            {
                release.Set();
                await work;
                owner.Dispose();
            }
            Assert.Single(values);
            Assert.Equal(new[] { "contact" }, types.Cast<string>());
            sdk.As<IDisposable>().Verify(ownerSdk => ownerSdk.Dispose(), Times.Never);
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>保留服務本身仍可回收 ctor logger 及 Dispose 後的 SDK；不以 GC 回收代替 SDK 正常 owner 釋放。</summary>
        [Fact]
        public void UnusedLoggerAndReleasedSdk_AreNotRetainedByLiveService()
        {
            var fixture = CreateRetentionProbe();
            for (var attempt = 0; attempt < 5 && (fixture.Item2.IsAlive || fixture.Item3.IsAlive); attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(fixture.Item2.IsAlive);
            Assert.False(fixture.Item3.IsAlive);
            GC.KeepAlive(fixture.Item1);
        }

        /// <summary>隔離 JIT 區域變數生命，讓 weak reference 真正測服務是否仍保存 logger／已清除 SDK。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Tuple<ActivityService, WeakReference, WeakReference> CreateRetentionProbe()
        {
            var logger = new object();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
            var service = new ActivityService(logger, sdk);
            ((IDisposable)service).Dispose();
            return Tuple.Create(service, new WeakReference(logger), new WeakReference(sdk));
        }

        /// <summary>本次操作的合成活動與正式 contact 識別，不保存於共用 fixture 或產品狀態。</summary>
        private static Entity Input(Guid id) => new Entity("email")
        {
            ["to"] = new EntityCollection(new[] { new Entity("activityparty") { ["partyid"] = new EntityReference("contact", id) } })
        };

        /// <summary>兩個公開入口共用的測試呼叫，不替代解析或驗證邏輯。</summary>
        private static void Invoke(ActivityService service, Entity input, ArrayList values, ArrayList types, bool ids)
        {
            if (ids) service.GetActivityPartyIdList(input, "to", values, types);
            else service.GetActivityPartyList(input, "to", values, types);
        }

        /// <summary>若產品錯誤接受衍生清單，任何發布 callback 都確定失敗；正確實作必須在 SDK 前拒絕。</summary>
        private sealed class AdversarialList : ArrayList
        {
            /// <summary>故障注入禁止把自訂集合當成普通陣列複製，沒有自有資源或背景工作。</summary>
            public override void AddRange(ICollection collection) => throw new InvalidOperationException("synthetic_callback");
        }
    }
}

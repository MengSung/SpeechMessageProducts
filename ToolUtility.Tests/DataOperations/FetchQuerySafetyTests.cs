using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 七個Fetch公開入口在SDK邊界注入非法頁／故障／錯型別，保護精確身份、top、XML與借用生命週期。
    /// 替身不接網路、不幫產品驗證候選；每案例只有合成資料且全部Task/事件確定排空。
    /// </summary>
    public sealed class FetchQuerySafetyTests
    {
        /// <summary>公開介面的七個多載不改名/參數，產品與契約必須實際來自Core，不能只移檔或空殼通過。</summary>
        [Fact]
        public void ContractAndImplementation_BelongToSameCoreAssembly()
        {
            Assert.Same(typeof(IFetchXmlQueryService).Assembly, typeof(FetchXmlQueryService).Assembly);
            Assert.Equal("ToolUtility.Core", typeof(FetchXmlQueryService).Assembly.GetName().Name);
            Assert.Equal(7, typeof(IFetchXmlQueryService).GetMethods().Length);
        }

        /// <summary>每個入口在取回SDK後驗整頁，null/空ID/錯表/重複/超top都拒絕而不回部分或默默去重。</summary>
        [Theory]
        [MemberData(nameof(InvalidPages))]
        public void AllEntrypoints_RejectInvalidCandidates(string entry, string mode)
        {
            var sdk = Source((name, top) =>
            {
                if (mode == "null") return null!;
                var row = new Entity(mode == "table" ? "account" : name,
                    mode == "empty" ? Guid.Empty : ContactCharacterizationTests.FirstId);
                return mode == "over-top" ? new EntityCollection(Enumerable.Range(1, top + 1).Select(index =>
                    new Entity(name, new Guid(index, 0, 0, new byte[8]))).ToArray())
                    : new EntityCollection(mode == "duplicate" ? new[] { row, row } : new[] { row });
            });
            var service = new FetchXmlQueryService(new object(), sdk.Object);
            Assert.Throws<InvalidOperationException>(() => FetchQueryCharacterizationTests.Read(service, entry));
        }

        /// <summary>七入口各自形成獨立案例，避免迴圈第一個失敗遮蔽Fee委派入口的top及候選缺陷。</summary>
        public static IEnumerable<object[]> InvalidPages() =>
            Entries.SelectMany(entry => new[] { "null", "empty", "table", "duplicate", "over-top" }
                .Select(mode => new object[] { entry, mode }));

        /// <summary>錯誤OrganizationResponse/null回應屬SDK完整性故障，應有固定診斷而不是InvalidCast/NullReference洩漏。</summary>
        [Theory]
        [MemberData(nameof(InvalidResponses))]
        public void AllEntrypoints_RejectInvalidResponseTypes(string entry, bool missing)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>()))
                .Returns(missing ? null! : new OrganizationResponse());
            Assert.Throws<InvalidOperationException>(() =>
                FetchQueryCharacterizationTests.Read(new FetchXmlQueryService(new object(), sdk.Object), entry));
        }

        /// <summary>每入口分開注入null及錯誤response，確保沒有任一委派路徑只因前一入口通過而漏測。</summary>
        public static IEnumerable<object[]> InvalidResponses() =>
            Entries.SelectMany(entry => new[] { false, true }.Select(missing => new object[] { entry, missing }));

        /// <summary>每個入口真正SDK故障保留同一Exception，下一次明確呼叫可重試；不從空資料/舊快取偽裝成功。</summary>
        [Fact]
        public void FailureAndRetry_PreserveOriginalExceptionWithoutRetainedCandidate()
        {
            foreach (var entry in Entries)
            {
                var failure = new TimeoutException("synthetic-private");
                var calls = 0;
                var sdk = Source((name, _) =>
                {
                    if (++calls == 1) throw failure;
                    return new EntityCollection(new[] { new Entity(name, ContactCharacterizationTests.FirstId) });
                });
                var service = new FetchXmlQueryService(new object(), sdk.Object);
                Assert.Same(failure, Assert.Throws<TimeoutException>(() => FetchQueryCharacterizationTests.Read(service, entry)));
                Assert.Single(FetchQueryCharacterizationTests.Read(service, entry).Entities);
                Assert.Equal(2, calls);
            }
        }

        /// <summary>lookup必須非空資料庫GUID；無效值在SDK前失敗，不因顯示名稱存在而擴張查詢範圍。</summary>
        [Theory]
        [InlineData("")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [InlineData("a<&\"'")]
        public void LookupIdentifiers_RejectInvalidValuesBeforeIo(string id)
        {
            foreach (var entry in new[] { "contact", "lesson", "dedication", "fee" })
            {
                var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
                var service = new FetchXmlQueryService(new object(), sdk.Object);
                Assert.Throws<ArgumentException>(() => FetchQueryCharacterizationTests.Read(service, entry, id: id));
                sdk.VerifyNoOtherCalls();
            }
        }

        /// <summary>四種lookup名稱只作顯示提示；XML特殊字元逐字roundtrip且不生成額外條件／節點。</summary>
        [Theory]
        [InlineData("contact")]
        [InlineData("lesson")]
        [InlineData("dedication")]
        [InlineData("fee")]
        public void DisplayNames_AreEncodedAndCannotChangeQueryStructure(string entry)
        {
            const string value = "a<&\"'";
            var sdk = Source((_, _) => new EntityCollection(), xml =>
            {
                var lookup = Assert.Single(xml.Descendants("condition").Where(node => node.Attribute("uiname") != null));
                Assert.Equal(value, (string?)lookup.Attribute("uiname"));
                Assert.Equal("{11111111-1111-4111-8111-111111111111}", (string?)lookup.Attribute("value"));
                Assert.Empty(xml.Descendants("injected"));
            });
            FetchQueryCharacterizationTests.Read(new FetchXmlQueryService(new object(), sdk.Object), entry, value);
        }

        /// <summary>Dispose後七個公開入口全部拒絕；不偷偷留下FeeService可繞過服務停止狀態，借入SDK不Dispose。</summary>
        [Fact]
        public void Dispose_RejectsEveryEntrypointWithoutDisposingBorrowedSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var owner = sdk.As<IDisposable>();
            var service = new FetchXmlQueryService(new object(), sdk.Object);
            ((IDisposable)service).Dispose();
            foreach (var entry in Entries)
                Assert.Throws<ObjectDisposedException>(() => FetchQueryCharacterizationTests.Read(service, entry));
            ((IDisposable)service).Dispose();
            owner.Verify(item => item.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>已開始同步SDK與Dispose競爭時舊工作可drain、新入口拒絕；測試事件與Task在finally清理，不掛背景。</summary>
        [Fact]
        public async Task DisposeRace_AllowsStartedOperationToDrain()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var sdk = Source((name, _) =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                return new EntityCollection(new[] { new Entity(name, ContactCharacterizationTests.FirstId) });
            });
            var service = new FetchXmlQueryService(new object(), sdk.Object);
            var operation = Task.Run(() => service.RetrieveListByFetchXml());
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                ((IDisposable)service).Dispose();
                Assert.Throws<ObjectDisposedException>(() => service.RetrieveListByFetchXml());
            }
            finally { release.Set(); await operation; }
        }

        /// <summary>1000次七入口A/B並行只見本SDK結果、保留同名異ID；caller修改不污染新讀，排空後active=0。</summary>
        [Fact]
        public async Task ConcurrentScopes_KeepOperationResultsIsolatedAndDrain()
        {
            var active = 0;
            var a = new FetchXmlQueryService(new object(), Scope(ContactCharacterizationTests.FirstId).Object);
            var b = new FetchXmlQueryService(new object(), Scope(ContactCharacterizationTests.SecondId).Object);
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var row = Assert.Single(FetchQueryCharacterizationTests.Read(index % 2 == 0 ? a : b,
                    Entries[index % Entries.Length]).Entities);
                Assert.Equal(index % 2 == 0 ? ContactCharacterizationTests.FirstId : ContactCharacterizationTests.SecondId, row.Id);
                Assert.Equal("synthetic", row["new_name"]);
                row["new_name"] = "caller-mutation";
            })));
            Assert.Equal(0, active);
            ((IDisposable)a).Dispose(); ((IDisposable)b).Dispose();

            Mock<IOrganizationService> Scope(Guid id) => Source((name, _) =>
            {
                Interlocked.Increment(ref active);
                try { return new EntityCollection(new[] { new Entity(name, id) { ["new_name"] = "synthetic" } }); }
                finally { Interlocked.Decrement(ref active); }
            });
        }

        /// <summary>服務活著也不持有logger，Dispose清SDK及任何局部Fee委派參照；weak reference補證有界owner。</summary>
        [Fact]
        public void Ownership_DoesNotRetainLoggerOrDisposedSdk()
        {
            var (service, logger, sdk) = CreateWeakOwner();
            Collect();
            Assert.False(logger.IsAlive);
            Assert.True(sdk.IsAlive);
            ((IDisposable)service).Dispose();
            Collect();
            Assert.False(sdk.IsAlive);
            GC.KeepAlive(service);
        }

        /// <summary>私有固定操作清單，不保存任何使用者／組織資料，測試只在本案例借讀。</summary>
        private static readonly string[] Entries = { "contact", "lesson", "meeting", "list", "smallgroup", "dedication", "fee" };

        /// <summary>避免JIT局部變數造成偽保留；唯一強owner是回傳的受測服務。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (FetchXmlQueryService Service, WeakReference Logger, WeakReference Sdk) CreateWeakOwner()
        {
            var logger = new object();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
            return (new FetchXmlQueryService(logger, sdk), new WeakReference(logger), new WeakReference(sdk));
        }

        /// <summary>有限GC觀測，不建立timer／輪詢或無界等待，不能用瞬時記憶體值冒充soak量測。</summary>
        private static void Collect()
        {
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        }

        /// <summary>SDK替身只從實際XML取得根表/top以回傳測試候選；驗證邏輯仍完全由產品執行。</summary>
        private static Mock<IOrganizationService> Source(Func<string, int, EntityCollection> read, Action<XDocument>? inspect = null)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest input) =>
            {
                var xml = XDocument.Parse(Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(input).Query).Query);
                inspect?.Invoke(xml);
                var response = new RetrieveMultipleResponse();
                response.Results["EntityCollection"] = read((string)xml.Root!.Element("entity")!.Attribute("name")!,
                    (int)xml.Root.Attribute("top")!);
                return response;
            });
            return sdk;
        }
    }
}

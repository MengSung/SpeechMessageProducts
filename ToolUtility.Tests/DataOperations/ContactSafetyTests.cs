/*
 * 檔案整體說明
 *
 * 目的與定位：聯絡人抽取的正式 ID、完整發布、XML、隔離及生命週期契約。 故障直接注入 SDK 邊界，斷言產品拒絕候選且不回部分資料；所有替身／Task／事件限本案例且確定排空。
 *
 * 主要功能與流程：
 * 本檔宣告 ContactSafetyTests。
 * - 三個Task入口在兩宿主都精確區分正常caller取消與內部timeout。 分別注入預取消／I/O中取消／內部取消／真故障／正常完成；不建立隱藏背景工作，決定性斷言Task狀態與原Exception。
 * - 外部SDK多載保留null/ref與資訊字串；只讀傳入SDK，不能偷偷用服務內另一個scope。
 * - 所有單筆空頁與格式化空頁維持null/空字串，不把空頁誤當故障或生成假資料庫ID。
 * - 建構失敗在保存任何依賴前發生；精確參數名稱讓host診斷設定錯誤，不接受空logger或SDK。
 * 測試案例入口包含 AsyncEntrypoints_PreserveCancellationAndOriginalFailures、
 * ExternalSdkOverloads_PreserveNullCountAndExactBorrowedScope、EmptySingleQueries_PreserveNullAndEmptyInformation、
 * Constructor_RejectsMissingDependencies、EveryCollection_RejectsInvalidCandidate、
 * EverySingle_RejectsInvalidCandidate、Collection_ReadsCompletePagesAndPreservesSameNameRows、
 * Collection_RejectsNonTerminatingPagingAtExactPageBudget；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.ContactOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 聯絡人抽取的正式 ID、完整發布、XML、隔離及生命週期契約。
    /// 故障直接注入 SDK 邊界，斷言產品拒絕候選且不回部分資料；所有替身／Task／事件限本案例且確定排空。
    /// </summary>
    public sealed class ContactSafetyTests
    {
        /// <summary>
        /// 三個Task入口在兩宿主都精確區分正常caller取消與內部timeout。
        /// 分別注入預取消／I/O中取消／內部取消／真故障／正常完成；不建立隱藏背景工作，決定性斷言Task狀態與原Exception。
        /// </summary>
        [Theory]
        [InlineData("line")]
        [InlineData("name")]
        [InlineData("account")]
        public async Task AsyncEntrypoints_PreserveCancellationAndOriginalFailures(string entry)
        {
            foreach (var mode in new[] { "before", "during", "internal", "fault", "success" })
            {
                using var cancellation = new CancellationTokenSource();
                var failure = mode == "internal" ? (Exception)new OperationCanceledException("synthetic-internal")
                    : new TimeoutException("synthetic-timeout");
                var sdk = Sdk(() =>
                {
                    if (mode == "during") cancellation.Cancel();
                    if (mode == "internal" || mode == "fault") throw failure;
                    return new EntityCollection(new[] { Row(ContactCharacterizationTests.FirstId) });
                });
                if (mode == "before") cancellation.Cancel();
                using var service = (ContactDataService)ContactCharacterizationTests.Create(sdk.Object);
                var operation = entry == "line" ? service.RetrieveByLineIdAsync("synthetic", cancellation.Token)
                    : entry == "name" ? service.RetrieveByFullNameAsync("synthetic", cancellation.Token)
                    : service.RetrieveByAccountNumberAsync("synthetic", "synthetic", cancellation.Token);
                if (mode == "before" || mode == "during")
                {
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
                    Assert.True(operation.IsCanceled);
                }
                else if (mode == "internal" || mode == "fault")
                {
                    Assert.Same(failure, await Record.ExceptionAsync(() => operation));
                    Assert.True(operation.IsFaulted);
                    Assert.False(operation.IsCanceled);
                }
                else Assert.Equal(ContactCharacterizationTests.FirstId, (await operation)!.Id);
                sdk.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()),
                    mode == "before" ? Times.Never() : Times.Once());
            }
        }

        /// <summary>外部SDK多載保留null/ref與資訊字串；只讀傳入SDK，不能偷偷用服務內另一個scope。</summary>
        [Fact]
        public void ExternalSdkOverloads_PreserveNullCountAndExactBorrowedScope()
        {
            var unused = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sdk = Sdk(() => ContactCharacterizationTests.Rows());
            using var service = (ContactDataService)ContactCharacterizationTests.Create(unused.Object);
            var count = 99;
            Assert.Null(service.RetrieveByContactId(null, "synthetic", ref count));
            Assert.Equal(0, count);
            Assert.Null(service.RetrieveByFullName(null, "synthetic"));
            Assert.Equal(string.Empty, service.GetContactInfoByFullName(null, "synthetic"));
            Assert.Equal(ContactCharacterizationTests.FirstId,
                service.RetrieveByContactId(sdk.Object, "synthetic", ref count)!.Id);
            Assert.Equal(1, count);
            Assert.Equal(ContactCharacterizationTests.FirstId, service.RetrieveByFullName(sdk.Object, "synthetic")!.Id);
            Assert.Equal("姓名:synthetic" + Environment.NewLine, service.GetContactInfoByFullName(sdk.Object, "synthetic"));
            unused.VerifyNoOtherCalls();
            sdk.Verify(item => item.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(3));
        }

        /// <summary>所有單筆空頁與格式化空頁維持null/空字串，不把空頁誤當故障或生成假資料庫ID。</summary>
        [Fact]
        public void EmptySingleQueries_PreserveNullAndEmptyInformation()
        {
            using var service = (ContactDataService)ContactCharacterizationTests.Create(Sdk(() => new EntityCollection()).Object);
            Assert.Null(service.RetrieveByLineId("synthetic"));
            Assert.Null(service.RetrieveByFullName("synthetic"));
            Assert.Null(service.RetrieveByFullNameAndMobile("synthetic", "synthetic"));
            Assert.Null(service.RetrieveByAccountNumber("synthetic", "synthetic"));
            Assert.Equal(string.Empty, service.GetContactInfoByFullName("synthetic"));
        }

        /// <summary>建構失敗在保存任何依賴前發生；精確參數名稱讓host診斷設定錯誤，不接受空logger或SDK。</summary>
        [Fact]
        public void Constructor_RejectsMissingDependencies()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
            Assert.Equal("logger", Assert.Throws<ArgumentNullException>(() => new ContactDataService(null!, sdk)).ParamName);
            Assert.Equal("organizationService",
                Assert.Throws<ArgumentNullException>(() => new ContactDataService(new object(), null!)).ParamName);
        }

        /// <summary>六個集合入口均須拒絕 null、空 ID、錯表及同 ID；不能只驗其中一種查詢或以名稱刪除重複。</summary>
        [Theory]
        [InlineData("null")]
        [InlineData("empty-id")]
        [InlineData("wrong-table")]
        [InlineData("duplicate")]
        public void EveryCollection_RejectsInvalidCandidate(string fault)
        {
            foreach (var read in Collections())
            {
                var sdk = Sdk(() => Invalid(fault));
                Assert.Throws<InvalidOperationException>(() => read(ContactCharacterizationTests.Create(sdk.Object)));
            }
        }

        /// <summary>九個單筆／資訊／帳密入口驗整個候選，不能取第一筆便略過第二筆、錯表或 null 回應。</summary>
        [Theory]
        [InlineData("null")]
        [InlineData("empty-id")]
        [InlineData("wrong-table")]
        [InlineData("duplicate")]
        [InlineData("over-top")]
        public void EverySingle_RejectsInvalidCandidate(string fault)
        {
            foreach (var read in Singles())
            {
                var sdk = Sdk(() => Invalid(fault));
                Assert.Throws<InvalidOperationException>(() => read(ContactCharacterizationTests.Create(sdk.Object)));
            }
        }

        /// <summary>無 top 集合跨兩頁保持同名異 ID，傳遞 cookie/page；舊單頁回傳不能假裝整個名單完整。</summary>
        [Fact]
        public void Collection_ReadsCompletePagesAndPreservesSameNameRows()
        {
            var calls = 0;
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryByAttribute>(input);
                Assert.Equal(++calls, query.PageInfo.PageNumber);
                Assert.Equal(5000, query.PageInfo.Count);
                Assert.Equal(calls == 1 ? null : "<cookie page='1' />", query.PageInfo.PagingCookie);
                Assert.Null(query.TopCount);
                Assert.Equal("contactid", Assert.Single(query.Orders).AttributeName);
                Assert.Equal(OrderType.Ascending, query.Orders[0].OrderType);
                Assert.Equal(new[] { "fullname", "statecode" }, query.Attributes);
                Assert.Equal(new object[] { "synthetic", 0 }, query.Values);
                return calls == 1 ? new EntityCollection(new[] { Row(ContactCharacterizationTests.FirstId) })
                    { MoreRecords = true, PagingCookie = "<cookie page='1' />" }
                    : new EntityCollection(new[] { Row(ContactCharacterizationTests.SecondId) });
            });
            var result = ContactCharacterizationTests.Create(sdk.Object).RetrieveCollectionByName("synthetic");
            Assert.Equal(2, calls);
            Assert.Equal(new[] { ContactCharacterizationTests.FirstId, ContactCharacterizationTests.SecondId },
                result.Entities.Select(row => row.Id));
            Assert.False(result.MoreRecords);
            var inputs = sdk.Invocations.Where(call => call.Method.Name == "RetrieveMultiple").ToArray();
            Assert.Equal(2, inputs.Length);
        }

        /// <summary>即使每頁ID不同、總列未滿預算，無終止SDK也只能呼叫1000頁後明確失敗，不能保留無界工作。</summary>
        [Fact]
        public void Collection_RejectsNonTerminatingPagingAtExactPageBudget()
        {
            var calls = 0;
            var sdk = Sdk(() => new EntityCollection(new[] { Row(new Guid(++calls, 0, 0, new byte[8])) })
                { MoreRecords = true });
            Assert.Throws<InvalidOperationException>(() =>
                ContactCharacterizationTests.Create(sdk.Object).RetrieveCollectionByName("synthetic"));
            Assert.Equal(1000, calls);
        }

        /// <summary>第一頁成功後第二頁 timeout 必須保留原 Exception；重試只能重新從第一頁完整建立候選。</summary>
        [Fact]
        public void LaterPageFailure_DoesNotPublishAndCanRetry()
        {
            var failure = new TimeoutException("synthetic-private");
            var calls = 0;
            var sdk = Sdk(() =>
            {
                if (++calls == 2) throw failure;
                return new EntityCollection(new[] { Row(ContactCharacterizationTests.FirstId) })
                    { MoreRecords = calls == 1, PagingCookie = calls == 1 ? "<cookie page='1' />" : null };
            });
            var service = ContactCharacterizationTests.Create(sdk.Object);
            Assert.Same(failure, Assert.Throws<TimeoutException>(() => service.RetrieveCollectionByLineId("synthetic")));
            Assert.Single(service.RetrieveCollectionByLineId("synthetic").Entities);
            Assert.Equal(3, calls);
        }

        /// <summary>跨頁重複正式 ID、空續頁、過大 cookie 或超過5000列都拒絕；不是透過無界重試解決。</summary>
        [Theory]
        [InlineData("duplicate")]
        [InlineData("empty-page")]
        [InlineData("large-cookie")]
        [InlineData("over-budget")]
        public void Collection_RejectsUnboundedOrConflictingPaging(string mode)
        {
            var calls = 0;
            var sdk = Sdk(() =>
            {
                calls++;
                if (mode == "over-budget")
                    return new EntityCollection(Enumerable.Range(1, 5001).Select(index =>
                        Row(new Guid(index, 0, 0, new byte[8]))).ToArray());
                return new EntityCollection(mode == "empty-page" ? Array.Empty<Entity>()
                    : new[] { Row(ContactCharacterizationTests.FirstId) })
                {
                    MoreRecords = mode != "duplicate" || calls == 1,
                    PagingCookie = mode == "large-cookie" ? new string('x', 65537) : "<cookie page='1' />"
                };
            });
            Assert.Throws<InvalidOperationException>(() =>
                ContactCharacterizationTests.Create(sdk.Object).RetrieveCollectionByFullName("synthetic"));
            Assert.InRange(calls, 1, 2);
        }

        /// <summary>前綴及六個 OR 值只能出現在 XML 屬性；特殊字元不能改變條件數或形成額外節點。</summary>
        [Fact]
        public void FetchInputs_RoundTripSpecialCharactersWithoutChangingStructure()
        {
            const string value = "a<&\"'";
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var count = 0;
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest input) =>
            {
                var query = Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(input).Query);
                var xml = XDocument.Parse(query.Query);
                var conditions = xml.Descendants("condition").ToArray();
                if (++count == 1)
                {
                    Assert.Equal(2, conditions.Length);
                    Assert.Equal(value + "%", (string?)conditions[0].Attribute("value"));
                }
                else
                {
                    Assert.Equal(8, conditions.Length);
                    Assert.Equal(new[] { value, "%" + value + "%", "%" + value + "%", "%" + value + "%",
                        "%" + value + "%", "%" + value + "%" },
                        conditions.Take(6).Select(condition => (string?)condition.Attribute("value")));
                }
                var response = new RetrieveMultipleResponse();
                response.Results["EntityCollection"] = ContactCharacterizationTests.Rows();
                return response;
            });
            var service = ContactCharacterizationTests.Create(sdk.Object);
            service.QueryContatsByStartedDedicationNumber(value);
            service.QueryDediccationContatsByFetchXml(value, value, value, value, value, value);
            Assert.Equal(2, count);
        }

        /// <summary>Fetch top是傳輸預算；SDK返回多於3/100列也必須拒絕，不是只在請求放top即可。</summary>
        [Theory]
        [InlineData(false, 4)]
        [InlineData(true, 101)]
        public void Fetch_RejectsSdkOverTop(bool dedication, int rows)
        {
            var sdk = Sdk(() => new EntityCollection(Enumerable.Range(1, rows).Select(index =>
                Row(new Guid(index, 0, 0, new byte[8]))).ToArray()));
            var service = ContactCharacterizationTests.Create(sdk.Object);
            Assert.Throws<InvalidOperationException>(() => dedication
                ? service.QueryDediccationContatsByFetchXml("D", "", "", "", "", "")
                : service.QueryContatsByStartedDedicationNumber("D"));
        }

        /// <summary>舊無效提示／空查詢不讀SDK，但Dispose後仍拒絕入口；空查詢不是可繞過生命週期的例外。</summary>
        [Fact]
        public void EmptySearch_SkipsIoButCannotBypassDispose()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = ContactCharacterizationTests.Create(sdk.Object);
            Assert.Empty(service.QueryDediccationContatsByFetchXml("", "請輸入姓名", " ", "", "", "").Entities);
            ((IDisposable)service).Dispose();
            Assert.Throws<ObjectDisposedException>(() =>
                service.QueryDediccationContatsByFetchXml("", "", "", "", "", ""));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>Dispose清除借用參照、不處置SDK；先前進入的同步I/O排空，新入口即使外部SDK多載也拒絕。</summary>
        [Fact]
        public async Task Dispose_AllowsExistingIoToDrainAndRejectsNewCalls()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var owner = sdk.As<IDisposable>();
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                return ContactCharacterizationTests.Rows();
            });
            var service = ContactCharacterizationTests.Create(sdk.Object);
            var operation = Task.Run(() => service.RetrieveByLineId("synthetic"));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                ((IDisposable)service).Dispose();
                Assert.Throws<ObjectDisposedException>(() => service.RetrieveByLineId("synthetic"));
                Assert.Throws<ObjectDisposedException>(() => service.RetrieveByFullName(sdk.Object, "synthetic"));
            }
            finally { release.Set(); await operation; }
            ((IDisposable)service).Dispose();
            owner.Verify(item => item.Dispose(), Times.Never);
        }

        /// <summary>1000次同名A/B交錯只回本SDK scope；caller修改不污染下一次，所有工作await後active歸零。</summary>
        [Fact]
        public async Task ParallelScopes_KeepDetachedOperationResultsAndDrain()
        {
            var active = 0;
            var a = ContactCharacterizationTests.Create(Source(ContactCharacterizationTests.FirstId).Object);
            var b = ContactCharacterizationTests.Create(Source(ContactCharacterizationTests.SecondId).Object);
            await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var useA = index % 2 == 0;
                var service = useA ? a : b;
                var row = Assert.Single(service.RetrieveCollectionByName("synthetic").Entities);
                Assert.Equal(useA ? ContactCharacterizationTests.FirstId : ContactCharacterizationTests.SecondId, row.Id);
                Assert.Equal("synthetic", row["fullname"]);
                row["fullname"] = "caller-mutation";
            })));
            Assert.Equal(0, active);
            ((IDisposable)a).Dispose();
            ((IDisposable)b).Dispose();

            Mock<IOrganizationService> Source(Guid id) => Sdk(() =>
            {
                Interlocked.Increment(ref active);
                try { return new EntityCollection(new[] { Row(id) }); }
                finally { Interlocked.Decrement(ref active); }
            });
        }

        /// <summary>存活服務不持有logger；Dispose後SDK可回收。GC僅作參照補證，不把記憶體瞬時數值當洩漏證明。</summary>
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

        /// <summary>避免JIT局部變數延長weak-reference目標生命；測試owner只把服務強參照交回。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (IContactService Service, WeakReference Logger, WeakReference Sdk) CreateWeakOwner()
        {
            var logger = new object();
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
#if NETFRAMEWORK
            IContactService service = new ContactDataService(logger, sdk);
#else
            IContactService service = new ContactService(logger, sdk);
#endif
            return (service, new WeakReference(logger), new WeakReference(sdk));
        }

        /// <summary>有限次完整GC觀測，不建立timer或無界等待。</summary>
        private static void Collect()
        {
            for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        }

        /// <summary>所有集合公開路徑共用同一故障矩陣，含Execute Fetch及四種型別化查詢。</summary>
        private static IEnumerable<Func<IContactService, EntityCollection>> Collections()
        {
            yield return service => service.RetrieveCollectionByLineId("synthetic");
            yield return service => service.RetrieveCollectionByName("synthetic");
            yield return service => service.RetrieveCollectionByNationId("synthetic");
            yield return service => service.RetrieveCollectionByFullName("synthetic");
            yield return service => service.QueryDediccationContatsByFetchXml("D", "", "", "", "", "");
            yield return service => service.QueryContatsByStartedDedicationNumber("D");
        }

        /// <summary>單筆與資訊入口仍應驗整頁；帳密候選補密碼以確保失敗由ID守門而不是密碼不符造成。</summary>
        private static IEnumerable<Func<IContactService, object?>> Singles()
        {
            yield return service => service.RetrieveByContactId("synthetic");
            yield return service => service.RetrieveByLineId("synthetic");
            yield return service => service.RetrieveByFullName("synthetic");
            yield return service => service.RetrieveByFullNameAndMobile("synthetic", "synthetic");
            yield return service => service.RetrieveByAccountNumber("synthetic", "synthetic");
            yield return service => service.RetrieveAccountEntity("synthetic");
            yield return service => service.GetContactInfoByContactId("synthetic");
            yield return service => service.GetContactInfoByFullName("synthetic");
            yield return service => service.RetrieveByLineIdForCollection("synthetic");
        }

        /// <summary>合成非法頁；null是刻意違約注入，錯表／重複／top超量不能混為查無資料。</summary>
        private static EntityCollection Invalid(string fault)
        {
            if (fault == "null") return null!;
            var row = Row(fault == "empty-id" ? Guid.Empty : ContactCharacterizationTests.FirstId);
            if (fault == "wrong-table") row.LogicalName = "account";
            var rows = new[] { row }.ToList();
            if (fault == "duplicate") rows.Add(Row(row.Id));
            if (fault == "over-top") rows.Add(Row(ContactCharacterizationTests.SecondId));
            return new EntityCollection(rows);
        }

        /// <summary>每次新建本操作資料列，避免測試自身共享可變Entity造成偽隔離結果。</summary>
        private static Entity Row(Guid id) => new Entity("contact", id)
            { ["fullname"] = "synthetic", ["new_app_pass"] = "synthetic" };

        /// <summary>同時替代兩個真SDK入口，產品仍必須自行驗證候選；不在替身執行產品守門。</summary>
        private static Mock<IOrganizationService> Sdk(Func<EntityCollection> result)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() => result());
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns(() =>
            {
                var response = new RetrieveMultipleResponse();
                response.Results["EntityCollection"] = result();
                return response;
            });
            return sdk;
        }
    }
}

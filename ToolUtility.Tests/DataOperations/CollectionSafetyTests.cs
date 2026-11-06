/*
 * 檔案整體說明
 *
 * 目的與定位：對真正的集合發布入口注入錯頁、跨表 ID、分頁故障及取消，不以 Mock 的空資料代表安全。 所有回傳候選只由案例持有；集合服務不應保存 logger 或結果，也不應處置借入 SDK。
 *
 * 主要功能與流程：
 * 本檔宣告 CollectionSafetyTests。
 * - 八個實際入口各注入三種識別衝突及 null 頁；漏驗任一列都會造成對應案例失敗。
 * - 失效頁不得以覆寫、去重或空集合發布；同步週報須保留原始失敗鏈供 host 告警。
 * - 字典完整批次須讀到第三頁、依正式身份組裝，不得只發第一頁或永遠送第二頁。
 * - 後頁重複身份／讀取失敗都拒絕整個候選；明確重試須重新 I/O，不能命中失敗或部分快照。
 * 測試案例入口包含 InvalidPages、EveryEntry_RejectsInvalidPageBeforePublication、
 * DictionaryBatch_ReadsAllPagesWithIncreasingNumbers、LaterPageFailure_DoesNotPublishAndNextCallRetries、
 * Cancellation_EmptyBatchAndInternalTimeoutAreClassified、Cancellation_DuringSdkStopsPublication、
 * LegacyPagingCookie_AdvancesToThirdPage、PagingCookie_RejectsUnsafeInput；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.CollectionOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 對真正的集合發布入口注入錯頁、跨表 ID、分頁故障及取消，不以 Mock 的空資料代表安全。
    /// 所有回傳候選只由案例持有；集合服務不應保存 logger 或結果，也不應處置借入 SDK。
    /// </summary>
    public sealed class CollectionSafetyTests
    {
        /// <summary>八個實際入口各注入三種識別衝突及 null 頁；漏驗任一列都會造成對應案例失敗。</summary>
        public static IEnumerable<object[]> InvalidPages()
        {
            for (var operation = 0; operation < 8; operation++)
                for (var defect = 0; defect < 4; defect++)
                    yield return new object[] { operation, defect };
        }

        /// <summary>失效頁不得以覆寫、去重或空集合發布；同步週報須保留原始失敗鏈供 host 告警。</summary>
        [Theory]
        [MemberData(nameof(InvalidPages))]
        public async Task EveryEntry_RejectsInvalidPageBeforePublication(int operation, int defect)
        {
            var name = operation == 3 ? "new_group_present_weekly_report" : "contact";
            var page = CollectionCharacterizationTests.Page(name);
            if (defect == 0) page.Entities[1].Id = page.Entities[0].Id;
            if (defect == 1) page.Entities[1].Id = Guid.Empty;
            if (defect == 2) page.Entities[1].LogicalName = "account";
            var sdk = Sdk(() => defect == 3 ? null! : page);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(
                new CollectionQueryService(new object(), sdk.Object), operation));
            Assert.DoesNotContain("same-name", exception.Message);
        }

        /// <summary>字典完整批次須讀到第三頁、依正式身份組裝，不得只發第一頁或永遠送第二頁。</summary>
        [Fact]
        public async Task DictionaryBatch_ReadsAllPagesWithIncreasingNumbers()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var calls = 0;
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                calls++;
                // 第一頁是相容探測；MoreRecords 後以穩定排序重啟，避免混合無排序與有排序的集合。
                if (calls == 1) return new EntityCollection { MoreRecords = true, Entities = { Row(1) } };
                Assert.Equal(calls - 1, query.PageInfo.PageNumber);
                Assert.Contains(query.Orders, order => order.AttributeName == "contactid" && order.OrderType == OrderType.Ascending);
                Assert.Equal(calls == 2 ? null : "<cookie page=\"" + (calls - 2) + "\" />", query.PageInfo.PagingCookie);
                return new EntityCollection
                {
                    Entities = { Row(calls - 1) },
                    MoreRecords = calls < 4,
                    PagingCookie = calls < 4 ? "<cookie page=\"" + (calls - 1) + "\" />" : null
                };
            });
            var result = await new CollectionQueryService(new object(), sdk.Object)
                .RetrieveBatchByIdsAsync("contact", new List<Guid> { Id(1), Id(2), Id(3) });
            Assert.Equal(3, result.Count);
            Assert.Equal(new[] { Id(1), Id(2), Id(3) }.OrderBy(id => id), result.Keys.OrderBy(id => id));
            Assert.Equal(4, calls);
        }

        /// <summary>後頁重複身份／讀取失敗都拒絕整個候選；明確重試須重新 I/O，不能命中失敗或部分快照。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LaterPageFailure_DoesNotPublishAndNextCallRetries(bool duplicate)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var calls = 0;
            var fault = new TimeoutException("synthetic-private-value");
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
            {
                calls++;
                if (calls <= 2) return new EntityCollection { MoreRecords = true, Entities = { Row(1) } };
                if (calls == 3)
                {
                    if (!duplicate) throw fault;
                    return new EntityCollection { Entities = { Row(1) } };
                }
                return new EntityCollection { Entities = { Row(2) } };
            });
            var service = new CollectionQueryService(new object(), sdk.Object);
            if (duplicate) await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RetrieveBatchByIdsAsync("contact", new List<Guid> { Id(1), Id(2) }));
            else Assert.Same(fault, await Assert.ThrowsAsync<TimeoutException>(
                () => service.RetrieveBatchByIdsAsync("contact", new List<Guid> { Id(1), Id(2) })));
            Assert.Equal(Id(2), Assert.Single(await service.RetrieveBatchByIdsAsync("contact", new List<Guid> { Id(2) })).Key);
            Assert.Equal(4, calls);
        }

        /// <summary>空查詢亦受 caller 取消；內部取消不是正常 caller 取消，必須保留真正故障。</summary>
        [Fact]
        public async Task Cancellation_EmptyBatchAndInternalTimeoutAreClassified()
        {
            var sdk = Sdk(() => throw new OperationCanceledException("synthetic-internal-timeout"));
            var service = new CollectionQueryService(new object(), sdk.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.RetrieveBatchByIdsAsync("contact", new List<Guid>(), cancellationToken: cancellation.Token));
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Never);
            var task = service.RetrieveEntityCollectionByFieldAsync("contact", "new_scope", "synthetic");
            await Assert.ThrowsAsync<OperationCanceledException>(() => task);
            Assert.True(task.IsFaulted);
            Assert.False(task.IsCanceled);
        }

        /// <summary>SDK 同步執行期間 caller 取消後不可發布成功結果，也不產生仍在執行的背景工作。</summary>
        [Fact]
        public async Task Cancellation_DuringSdkStopsPublication()
        {
            using var cancellation = new CancellationTokenSource();
            var sdk = Sdk(() =>
            {
                cancellation.Cancel();
                return CollectionCharacterizationTests.Page("contact");
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                new CollectionQueryService(new object(), sdk.Object).RetrieveEntityCollectionByFieldAsync(
                    "contact", "new_scope", "synthetic", cancellation.Token));
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>官方 cookie 帶上一頁頁碼；第三頁必須送 page=3，DTD／無效頁碼在 I/O 前拒絕。</summary>
        [Fact]
        public async Task LegacyPagingCookie_AdvancesToThirdPage()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(It.Is<QueryExpression>(q => q.PageInfo.PageNumber == 3)))
                .Returns(new EntityCollection { Entities = { Row(3) } });
            var result = await new CollectionQueryService(new object(), sdk.Object)
                .RetrievePagedEntitiesAsync("contact", pagingCookie: "<cookie page=\"2\"><contactid last=\"x\" /></cookie>");
            Assert.Equal(Id(3), Assert.Single(result.Entities).Id);
        }

        /// <summary>不合法 cookie 不得洩漏原值或進入 SDK；不得默默回到第二頁。</summary>
        [Theory]
        [InlineData("invalid")]
        [InlineData("<cookie page=\"-1\" />")]
        [InlineData("<cookie page=\"2147483647\" />")]
        [InlineData("<!DOCTYPE cookie [<!ENTITY x SYSTEM 'file:///synthetic'>]><cookie page=\"2\">&x;</cookie>")]
        public async Task PagingCookie_RejectsUnsafeInput(string cookie)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            await Assert.ThrowsAnyAsync<ArgumentException>(() => new CollectionQueryService(new object(), sdk.Object)
                .RetrievePagedEntitiesAsync("contact", pagingCookie: cookie));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>已 Dispose 服務拒絕新讀取但不處置借用 SDK；以行為而非私人欄位名稱驗證所有權。</summary>
        [Fact]
        public async Task Dispose_RejectsNewOperationsAndDoesNotDisposeBorrowedSdk()
        {
            var sdk = Sdk(() => CollectionCharacterizationTests.Page("contact"));
            var owner = sdk.As<IDisposable>();
            var service = new CollectionQueryService(new object(), sdk.Object);
            var disposable = Assert.IsAssignableFrom<IDisposable>(service);
            disposable.Dispose();
            disposable.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                service.RetrieveEntityCollectionByFieldAsync("contact", "new_scope", "synthetic"));
            owner.Verify(item => item.Dispose(), Times.Never);
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Never);
        }

        /// <summary>顯式分頁多載支援 SDK 不提供 cookie 的查詢，不新增介面必要成員而破壞外部實作者。</summary>
        [Fact]
        public void ExplicitPageNumberOverload_ExistsWithoutReplacingLegacyContract()
        {
            var method = typeof(CollectionQueryService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault(item => item.Name == "RetrievePagedEntitiesAsync"
                    && item.GetParameters().Length == 7 && item.GetParameters()[1].ParameterType == typeof(int));
            Assert.NotNull(method);
        }

        /// <summary>同一來源允許少於請求筆數，但禁止 SDK 回傳不在此次 ID 邊界中的記錄。</summary>
        [Fact]
        public async Task DictionaryBatch_RejectsUnrequestedId()
        {
            var sdk = Sdk(() => new EntityCollection { Entities = { Row(2) } });
            await Assert.ThrowsAsync<InvalidOperationException>(() => new CollectionQueryService(new object(), sdk.Object)
                .RetrieveBatchByIdsAsync("contact", new List<Guid> { Id(1) }));
        }

        /// <summary>只替代兩個網路讀取方法；全部非讀取操作保持 Strict 拒絕。</summary>
        private static Mock<IOrganizationService> Sdk(Func<EntityCollection> load)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(load);
            sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>()))
                .Returns(() => new RetrieveMultipleResponse { Results = { ["EntityCollection"] = load() } });
            return sdk;
        }

        /// <summary>透過各真實公開入口操作；統一轉為 Task 只供斷言，不更改產品執行模式。</summary>
        private static async Task Invoke(CollectionQueryService service, int operation)
        {
            switch (operation)
            {
                case 0: service.RetrieveEntityCollectionByField("contact", "new_scope", "synthetic"); break;
                case 1: await service.RetrieveEntityCollectionByConditionAsync("contact", "new_scope", ConditionOperator.Equal, "synthetic"); break;
                case 2: await service.RetrieveEntityCollectionByConditionsAsync("contact", new Dictionary<string, object>()); break;
                case 3: service.QueryWeeklyReportBeforeTowMonthOfSunday(new DateTime(2026, 11, 1), CollectionCharacterizationTests.FirstId); break;
                case 4: await service.RetrievePagedEntitiesAsync("contact"); break;
                case 5: await service.RetrieveBatchByIdsAsync("contact", "contactid", new[] { CollectionCharacterizationTests.FirstId, CollectionCharacterizationTests.SecondId }); break;
                case 6: await service.RetrieveBatchByIdsAsync("contact", new List<Guid> { CollectionCharacterizationTests.FirstId, CollectionCharacterizationTests.SecondId }); break;
                case 7: await service.RetrieveBatchByFieldValuesAsync("contact", "new_scope", new List<string> { "synthetic" }); break;
            }
        }

        /// <summary>有界合成資料庫 ID，正式產品不能以此產生記錄身份。</summary>
        private static Guid Id(int number) => new Guid(number, 0, 0, new byte[8]);
        /// <summary>單次操作專用列，不供共用快取或 SDK owner 保存。</summary>
        private static Entity Row(int number) => new Entity("contact", Id(number)) { ["fullname"] = "same-name" };
    }
}

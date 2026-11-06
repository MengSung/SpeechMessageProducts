/*
 * 檔案整體說明
 *
 * 目的與定位：十六入口逐一拒絕候選完整性故障；Mock只供SDK response，不代替產品身份／XML驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 ComplexQuerySafetyTests。
 * - 每入口分開測null、空／重複ID、錯表與budget，避免首入口失敗遮蔽後面的缺陷。
 * - 先驗整頁才回單筆或集合；metadata門檻不得讓錯誤正式資料列通過或被假空結果隱藏。
 * - 十四個帶ID入口在I/O前拒空；無ID的固定示例與LINE字串查詢另守根表，不偽裝為GUID流程。
 * - 動態表/欄位、週數及LINE空字串不能形成更寬查詢；錯誤不含原輸入。
 * 測試案例入口包含 Cases、AllEntrypoints_RejectInvalidPage、IdentifierInputs_RejectBeforeIo、UnsafeInputs_RejectBeforeIo、
 * Fetch_SafelyEncodesNameAndChecksResponse、Dispose_RejectsAllEntrypointsAndDoesNotDisposeClient、
 * Failure_RetainsOriginalExceptionAndAllowsExplicitRetry、Source。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>十六入口逐一拒絕候選完整性故障；Mock只供SDK response，不代替產品身份／XML驗證。</summary>
    public sealed class ComplexQuerySafetyTests
    {
        /// <summary>每入口分開測null、空／重複ID、錯表與budget，避免首入口失敗遮蔽後面的缺陷。</summary>
        public static IEnumerable<object[]> Cases() => ComplexQueryCharacterizationTests.Entries.SelectMany(entry =>
            new[] { "null", "empty", "duplicate", "table", "budget" }.Select(mode => new object[] { entry, mode }));

        /// <summary>先驗整頁才回單筆或集合；metadata門檻不得讓錯誤正式資料列通過或被假空結果隱藏。</summary>
        [Theory]
        [MemberData(nameof(Cases))]
        public void AllEntrypoints_RejectInvalidPage(string entry, string mode)
        {
            var client = Source(table =>
            {
                if (mode == "null") return null!;
                var row = new Entity(mode == "table" ? "unrelated" : table,
                    mode == "empty" ? Guid.Empty : ContactCharacterizationTests.FirstId);
                return new EntityCollection(mode == "budget"
                    ? Enumerable.Range(1, 5001).Select(index => new Entity(table, new Guid(index, 0, 0, new byte[8]))).ToArray()
                    : mode == "duplicate" ? new[] { row, row } : new[] { row }) { TotalRecordCount = 2 };
            });
            Assert.Throws<InvalidOperationException>(() => ComplexQueryCharacterizationTests.Read(
                new ComplexQueryDataService(new object(), client.Object), entry));
        }

        /// <summary>十四個帶ID入口在I/O前拒空；無ID的固定示例與LINE字串查詢另守根表，不偽裝為GUID流程。</summary>
        [Fact]
        public void IdentifierInputs_RejectBeforeIo()
        {
            foreach (var entry in ComplexQueryCharacterizationTests.Entries.Except(new[] { "task", "contact" }))
            {
                var client = new Mock<ICrmQueryExecutor>(MockBehavior.Strict);
                Assert.Throws<ArgumentException>(() => ComplexQueryCharacterizationTests.Read(
                    new ComplexQueryDataService(new object(), client.Object), entry, Guid.Empty));
                client.VerifyNoOtherCalls();
            }
        }

        /// <summary>動態表/欄位、週數及LINE空字串不能形成更寬查詢；錯誤不含原輸入。</summary>
        [Fact]
        public void UnsafeInputs_RejectBeforeIo()
        {
            var client = new Mock<ICrmQueryExecutor>(MockBehavior.Strict);
            var service = new ComplexQueryDataService(new object(), client.Object);
            Assert.Throws<ArgumentException>(() => service.QueryPresentRecordSortBySunday("bad<&", "contactid",
                ComplexQueryCharacterizationTests.InputId.ToString("D"), "new_parent", "new_present_record"));
            Assert.Throws<ArgumentException>(() => service.QueryListByContactId(ComplexQueryCharacterizationTests.InputId, "bad<&"));
            Assert.Throws<ArgumentOutOfRangeException>(() => service.QueryPresentRecordSortBySundayFetchXml(0, "Synthetic",
                ComplexQueryCharacterizationTests.InputId.ToString("D")));
            Assert.Throws<ArgumentException>(() => service.RetrieveContactCollectionByLineId(""));
            client.VerifyNoOtherCalls();
        }

        /// <summary>Fetch顯示文字必須完整XML跳脫；Execute錯型別fail closed而非cast或假空集合。</summary>
        [Fact]
        public void Fetch_SafelyEncodesNameAndChecksResponse()
        {
            var client = new Mock<ICrmQueryExecutor>(MockBehavior.Strict);
            client.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest input) =>
            {
                var xml = XElement.Parse(Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(input).Query).Query);
                Assert.Equal("a<&\"'", (string?)xml.Descendants("condition").First().Attribute("uiname"));
                Assert.Equal(2, xml.Descendants("condition").Count());
                return new OrganizationResponse();
            });
            Assert.Throws<InvalidOperationException>(() => new ComplexQueryDataService(new object(), client.Object)
                .QueryPresentRecordSortBySundayFetchXml(3, "a<&\"'", ComplexQueryCharacterizationTests.InputId.ToString("D")));
        }

        /// <summary>Dispose不可處置host連線；十六新入口停止，服務只解除借用executor，不保存logger。</summary>
        [Fact]
        public void Dispose_RejectsAllEntrypointsAndDoesNotDisposeClient()
        {
            var client = new Mock<ICrmQueryExecutor>(MockBehavior.Strict);
            var disposable = client.As<IDisposable>();
            var service = new ComplexQueryDataService(new object(), client.Object);
            ((IDisposable)service).Dispose();
            foreach (var entry in ComplexQueryCharacterizationTests.Entries)
                Assert.Throws<ObjectDisposedException>(() => ComplexQueryCharacterizationTests.Read(service, entry));
            disposable.Verify(item => item.Dispose(), Times.Never);
            client.VerifyNoOtherCalls();
        }

        /// <summary>每入口先故障後明確重試，原Exception完整傳到host；候選不被儲存或快取。</summary>
        [Fact]
        public void Failure_RetainsOriginalExceptionAndAllowsExplicitRetry()
        {
            foreach (var entry in ComplexQueryCharacterizationTests.Entries)
            {
                var calls = 0;
                var failure = new TimeoutException("synthetic-private");
                var client = Source(table =>
                {
                    if (++calls == 1) throw failure;
                    return new EntityCollection(new[] { new Entity(table, ContactCharacterizationTests.FirstId) }) { TotalRecordCount = 1 };
                });
                var service = new ComplexQueryDataService(new object(), client.Object);
                Assert.Same(failure, Assert.Throws<TimeoutException>(() => ComplexQueryCharacterizationTests.Read(service, entry)));
                Assert.NotNull(ComplexQueryCharacterizationTests.Read(service, entry));
                Assert.Equal(2, calls);
            }
        }

        /// <summary>替身依真實root回頁，動態list只回本測試合成Fetch；不替產品做ID驗證。</summary>
        internal static Mock<ICrmQueryExecutor> Source(Func<string, EntityCollection> read)
        {
            var client = new Mock<ICrmQueryExecutor>(MockBehavior.Strict);
            EntityCollection Page(QueryBase query) => read(query is QueryExpression expression ? expression.EntityName
                : query is QueryByAttribute attributes ? attributes.EntityName
                : (string)XElement.Parse(Assert.IsType<FetchExpression>(query).Query).Element("entity")!.Attribute("name")!);
            client.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) => Page(query));
            client.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
            {
                var response = new RetrieveMultipleResponse();
                response.Results["EntityCollection"] = Page(Assert.IsType<RetrieveMultipleRequest>(request).Query);
                return response;
            });
            client.Setup(item => item.Retrieve("list", It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                .Returns((string table, Guid id, ColumnSet _) =>
                    new Entity(table, id) { ["query"] = RepositoryQueryCharacterizationTests.Fetch });
            return client;
        }
    }
}

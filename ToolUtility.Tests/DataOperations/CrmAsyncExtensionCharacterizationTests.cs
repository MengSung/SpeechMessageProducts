using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.Extensions;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>原host非同步SDK facade三代表入口先綠；鎖定IN型別／欄位、既有paging及單筆傳遞，不碰網路。</summary>
    public sealed class CrmAsyncExtensionCharacterizationTests
    {
        /// <summary>原批次保留無statecode、IN Guid值、指定ColumnSet；回傳不同ID同名兩列完整字典。</summary>
        [Fact]
        public async Task Batch_PreservesCompleteQueryAndSameNameRecords()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var columns = new ColumnSet("fullname");
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                GoldenQueryAssertions.Equal("crm-async-batch.xml", RelationshipQueryCharacterizationTests.Describe(query));
                Assert.Same(columns, query.ColumnSet);
                return new EntityCollection(new[]
                {
                    new Entity("contact", ContactCharacterizationTests.FirstId) { ["fullname"] = "same" },
                    new Entity("contact", ContactCharacterizationTests.SecondId) { ["fullname"] = "same" }
                });
            });
            var result = await sdk.Object.BatchRetrieveAsync("contact",
                new[] { ContactCharacterizationTests.FirstId, ContactCharacterizationTests.SecondId }, columns);
            Assert.Equal(2, result.Count);
            Assert.All(result.Values, row => Assert.Equal("same", row["fullname"]));
        }

        /// <summary>單筆保持表／ID及同一ColumnSet，SDK列原樣返回，擴充不Dispose借入SDK。</summary>
        [Fact]
        public async Task Single_PreservesColumnSetAndBorrowedSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var columns = new ColumnSet("fullname");
            var row = new Entity("contact", ContactCharacterizationTests.FirstId);
            sdk.Setup(item => item.Retrieve("contact", row.Id, columns)).Returns(row);
            Assert.Same(row, await sdk.Object.RetrieveAsync("contact", row.Id, columns));
            disposable.Verify(item => item.Dispose(), Times.Never);
        }

        /// <summary>完整分頁保留root查詢、頁大小及下一頁cookie，只在最後才回完整兩筆。</summary>
        [Fact]
        public async Task All_PreservesPagingRequestAndReturnsBothPages()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var query = new QueryExpression("contact") { ColumnSet = new ColumnSet("fullname") };
            var calls = 0;
            sdk.Setup(item => item.RetrieveMultiple(query)).Returns(() =>
            {
                calls++;
                Assert.Equal(calls, query.PageInfo.PageNumber);
                Assert.Equal(2, query.PageInfo.Count);
                Assert.Equal(calls == 1 ? null : "synthetic", query.PageInfo.PagingCookie);
                return new EntityCollection(new[] { new Entity("contact",
                    calls == 1 ? ContactCharacterizationTests.FirstId : ContactCharacterizationTests.SecondId) })
                    { MoreRecords = calls == 1, PagingCookie = "synthetic" };
            });
            var result = await sdk.Object.RetrieveAllAsync(query, 2);
            Assert.Equal(2, result.Count);
            Assert.Equal(2, calls);
        }
    }
}

using Microsoft.Xrm.Sdk;
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
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.Object.ParallelRetrieveAsync(
                new[] { ("contact", ContactCharacterizationTests.FirstId) }, new ColumnSet(true), degree));
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
    }
}

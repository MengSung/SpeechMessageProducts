using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 五個非Fee查詢在原host先綠：golden從已記錄SHA的舊模板填合成常數後固定，不在測試執行中重生。
    /// 確切欄位、link、條件、top與排序都比較，另保留同名異ID與單頁metadata，不冒稱全表或實機驗證。
    /// </summary>
    public sealed class FetchQueryCharacterizationTests
    {
        /// <summary>同一張原host單頁含兩筆同名但不同ID；回應、metadata與所有XML結構都必須保留。</summary>
        [Theory]
        [InlineData("contact", "new_stor_lessons")]
        [InlineData("lesson", "new_stor_lessons")]
        [InlineData("meeting", "new_meeting_statistics")]
        [InlineData("list", "list")]
        [InlineData("smallgroup", "list")]
        public void OriginalQueries_PreserveFullGoldenAndSdkPage(string entry, string entityName)
        {
            var rows = new EntityCollection(new[]
            {
                new Entity(entityName, ContactCharacterizationTests.FirstId) { ["new_name"] = "synthetic" },
                new Entity(entityName, ContactCharacterizationTests.SecondId) { ["new_name"] = "synthetic" }
            }) { MoreRecords = true, PagingCookie = "synthetic-cookie", TotalRecordCount = 17 };
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest input) =>
            {
                var request = Assert.IsType<RetrieveMultipleRequest>(input);
                GoldenQueryAssertions.Equal("fetch-query-" + entry + ".xml", Assert.IsType<FetchExpression>(request.Query).Query);
                var response = new RetrieveMultipleResponse();
                response.Results["EntityCollection"] = rows;
                return response;
            });
            var service = new FetchXmlQueryService(new object(), sdk.Object);
            var result = Read(service, entry);
            Assert.Same(rows, result);
            Assert.Equal(2, result.Entities.Count);
            Assert.True(result.MoreRecords);
            Assert.Equal("synthetic-cookie", result.PagingCookie);
            Assert.Equal(17, result.TotalRecordCount);
            sdk.Verify(item => item.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>明確列出七個原API；固定合成GUID/日期不是授權或外部環境設定。</summary>
        internal static EntityCollection Read(IFetchXmlQueryService service, string entry, string name = "Synthetic",
            string id = "11111111-1111-4111-8111-111111111111") => entry switch
        {
            "contact" => service.RetrieveStorLessonsByFetchXml(name, id),
            "lesson" => service.RetrieveStorLessonsByDiscipleLessonsFetchXml(name, id),
            "meeting" => service.RetrieveMeetingStatisticsByFetchXml(new DateTime(2027, 3, 14)),
            "list" => service.RetrieveListByFetchXml(),
            "smallgroup" => service.RetrieveSmallGroupListCollectionByFetchXml(),
            "dedication" => service.RetrieveDedicationBookingByFetchXml(name, id),
            "fee" => service.RetrieveFeeByFetchXml(name, id, "3"),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
    }
}

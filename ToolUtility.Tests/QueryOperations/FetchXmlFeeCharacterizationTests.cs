/*
 * 檔案整體說明
 *
 * 目的與定位：鎖定目前有實作的認獻／收費查詢來源，作為 Fee 空方法修復與後續抽取的相容證據。 只使用每案獨立的 SDK 替身與合成資料，不將離線結果冒充 D365 整合成功。
 *
 * 主要功能與流程：
 * 本檔宣告 FetchXmlFeeCharacterizationTests。
 * - 保護完整欄位、上限、排序、啟用狀態及 lookup 條件；注入一筆固定 ID 的資料， 斷言實際送出的 SDK request 與 golden 相等，回傳集合不經名稱去重或改寫。
 * - 注入 SDK 故障；原始同一例外必須向最終 host 傳遞，不能以空集合掩蓋失敗。
 * 測試案例入口包含 ExistingFetchQuery_MatchesGoldenAndReturnsSdkRows、
 * ExistingFetchQuery_SdkFault_PropagatesOriginalException。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.QueryOperations
{
    /// <summary>
    /// 鎖定目前有實作的認獻／收費查詢來源，作為 Fee 空方法修復與後續抽取的相容證據。
    /// 只使用每案獨立的 SDK 替身與合成資料，不將離線結果冒充 D365 整合成功。
    /// </summary>
    public sealed class FetchXmlFeeCharacterizationTests
    {
        /// <summary>
        /// 保護完整欄位、上限、排序、啟用狀態及 lookup 條件；注入一筆固定 ID 的資料，
        /// 斷言實際送出的 SDK request 與 golden 相等，回傳集合不經名稱去重或改寫。
        /// </summary>
        /// <param name="fee">選擇收費或認獻查詢。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExistingFetchQuery_MatchesGoldenAndReturnsSdkRows(bool fee)
        {
            var expected = new EntityCollection(new[]
            {
                new Entity(fee ? "new_fee" : "new_dedication_booking",
                    new Guid("22222222-2222-4222-8222-222222222222"))
            });
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = expected;
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request => GoldenQueryAssertions.Equal(
                    fee ? "query-fee-by-booking.xml" : "query-dedication-booking.xml",
                    Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query))
                .Returns(response);
            var sut = new FetchXmlQueryService(new object(), client.Object);

            var result = fee
                ? sut.RetrieveFeeByFetchXml("Synthetic Booking", "11111111-1111-4111-8111-111111111111", "3")
                : sut.RetrieveDedicationBookingByFetchXml("Synthetic Contact", "11111111-1111-4111-8111-111111111111");

            Assert.Same(expected, result);
            Assert.Equal(expected.Entities[0].Id, Assert.Single(result.Entities).Id);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>
        /// 注入 SDK 故障；原始同一例外必須向最終 host 傳遞，不能以空集合掩蓋失敗。
        /// </summary>
        /// <param name="fee">選擇收費或認獻查詢。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExistingFetchQuery_SdkFault_PropagatesOriginalException(bool fee)
        {
            var failure = new InvalidOperationException("Synthetic SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Throws(failure);
            var sut = new FetchXmlQueryService(new object(), client.Object);

            var actual = Assert.Throws<InvalidOperationException>(() =>
            {
                if (fee)
                    sut.RetrieveFeeByFetchXml("Synthetic Booking", "11111111-1111-4111-8111-111111111111", "3");
                else
                    sut.RetrieveDedicationBookingByFetchXml("Synthetic Contact", "11111111-1111-4111-8111-111111111111");
            });

            Assert.Same(failure, actual);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }
    }
}

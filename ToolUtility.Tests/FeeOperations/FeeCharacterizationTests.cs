using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.FeeOperations;
using Xunit;

namespace ToolUtility.Tests.FeeOperations
{
    /// <summary>
    /// 在 FeeService 尚未搬移前鎖定現行有效查詢與結果傳遞。
    /// 全部使用合成教會資料與獨立 SDK 替身；不連 D365，不將空實作視為正確業務。
    /// </summary>
    public sealed class FeeCharacterizationTests
    {
        private const string ContactId = "11111111-1111-4111-8111-111111111111";

        /// <summary>
        /// 保護奉獻查詢的欄位、排序、聯絡人與分類條件；注入同名但不同 ID 的資料，
        /// 決定性斷言為實際 FetchXML 符合黃金檔且兩筆合法記錄均完整保留。
        /// </summary>
        [Fact]
        public void RetrieveDedicationFee_ExistingImplementation_MatchesGoldenAndPreservesSameNameRows()
        {
            var expected = CreateSameNameRows();
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => GoldenQueryAssertions.Equal(
                    "fee-dedication.xml", Assert.IsType<FetchExpression>(query).Query))
                .Returns(expected);
            var sut = new FeeService(new object(), client.Object);

            var result = sut.RetrieveDedicationFee("Synthetic Contact", ContactId);

            Assert.Same(expected, result);
            Assert.Equal(2, result.Entities.Count);
            Assert.NotEqual(result.Entities[0].Id, result.Entities[1].Id);
            Assert.Equal(result.Entities[0]["new_name"], result.Entities[1]["new_name"]);
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 保護日期區間查詢的既有付款狀態白名單與含端點日期條件；注入固定日期和空結果，
        /// 斷言完整 XML 而非只比對片段，且空集合來自 SDK 而非服務自行製造。
        /// </summary>
        [Fact]
        public void RetrieveDedicationFeeByDateRange_ExistingImplementation_MatchesGolden()
        {
            var expected = new EntityCollection();
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => GoldenQueryAssertions.Equal(
                    "fee-dedication-date-range.xml", Assert.IsType<FetchExpression>(query).Query))
                .Returns(expected);
            var sut = new FeeService(new object(), client.Object);

            // 原產品目前依 ambient culture 格式化；此特性基準固定西曆，避免測試機預設曆法改變 golden。
            // 僅設定當次執行脈絡，finally 還原；不可改 DefaultThreadCurrentCulture 污染並行案例。
            var previousCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var result = sut.RetrieveDedicationFeeByDateRange(
                    "Synthetic Contact", ContactId, new DateTime(2027, 1, 2), new DateTime(2027, 3, 4));
                Assert.Same(expected, result);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 保護 SDK 失敗不被改成空集合或成功；注入同一例外實例，
        /// 斷言原始例外與呼叫次數，讓最終 host 能以同一事件交給共用通知入口。
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RetrieveDedicationFee_SdkFails_PropagatesOriginalException(bool useDateRange)
        {
            var failure = new InvalidOperationException("Synthetic SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(failure);
            var sut = new FeeService(new object(), client.Object);

            var actual = Assert.Throws<InvalidOperationException>(() =>
            {
                if (useDateRange)
                    sut.RetrieveDedicationFeeByDateRange(
                        "Synthetic Contact", ContactId, new DateTime(2027, 1, 2), new DateTime(2027, 3, 4));
                else
                    sut.RetrieveDedicationFee("Synthetic Contact", ContactId);
            });

            Assert.Same(failure, actual);
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 建立無機密的兩筆合法同名資料；每案重新配置集合，防止測試間共享可變 Entity。
        /// </summary>
        private static EntityCollection CreateSameNameRows()
        {
            return new EntityCollection(new[]
            {
                new Entity("new_fee", new Guid("22222222-2222-4222-8222-222222222222"))
                {
                    ["new_name"] = "Synthetic Fee"
                },
                new Entity("new_fee", new Guid("33333333-3333-4333-8333-333333333333"))
                {
                    ["new_name"] = "Synthetic Fee"
                }
            });
        }
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P1-04 新增收費查詢及既有查詢安全契約。所有資料、ID與故障均合成； 每案獨立借入 SDK 替身，沒有實機連線、共享快取或持續背景工作。
 *
 * 主要功能與流程：
 * 本檔宣告 FeeQueryContractTests。
 * - 鎖定三個舊來源補回方法的完整條件及欄位；注入同名不同ID兩筆資料， 以完整golden及兩筆ID斷言證明沒有按名稱合併或丟失。
 * - 課程ID是查詢界線；注入空白、空GUID及XML注入字串均須在I/O前拒絕， 不可將XML編碼誤當成授權或合法識別碼驗證。
 * - 舊API保留但未使用的課程名稱可含XML字元，不能被加入查詢或拿來當識別／權限； 完整golden仍相等，證明兩個方法只按伺服器提供的課程ID查詢。
 * - 訂單號是文字而非GUID；含所有XML特殊字元仍須只產生原有兩個OR條件， 解碼後值逐字相等，不能新增節點或讓卡／ATM任一分支遺失。
 * 測試案例入口包含 NewQuery_MatchesGoldenAndPreservesSameNameRows、LessonQuery_InvalidId_RejectsBeforeIo、
 * LessonQuery_DisplayNameCannotChangeScope、OrderQuery_XmlSpecialCharacters_RoundTripWithoutInjection、
 * OrderQuery_EmptyInput_RejectsBeforeIo、NewQuery_SdkFault_PropagatesSameException、
 * FormerPlaceholder_UsesEstablishedQuery、Query_DuplicateStableId_RejectsPublication；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.FeeOperations;
using Xunit;

namespace ToolUtility.Tests.FeeOperations
{
    /// <summary>
    /// 保護 P1-04 新增收費查詢及既有查詢安全契約。所有資料、ID與故障均合成；
    /// 每案獨立借入 SDK 替身，沒有實機連線、共享快取或持續背景工作。
    /// </summary>
    public sealed class FeeQueryContractTests
    {
        private const string ScopeId = "11111111-1111-4111-8111-111111111111";
        private const string SpecialText = "Synthetic <&\"' value";

        /// <summary>
        /// 鎖定三個舊來源補回方法的完整條件及欄位；注入同名不同ID兩筆資料，
        /// 以完整golden及兩筆ID斷言證明沒有按名稱合併或丟失。
        /// </summary>
        /// <param name="operation">待新增的三個查詢之一。</param>
        /// <param name="golden">唯讀合成XML資源。</param>
        [Theory]
        [InlineData("Paid", "fee-paid.xml")]
        [InlineData("Unpaid", "fee-unpaid.xml")]
        [InlineData("Order", "fee-order.xml")]
        public void NewQuery_MatchesGoldenAndPreservesSameNameRows(string operation, string golden)
        {
            var expected = Rows();
            expected.MoreRecords = true;
            expected.PagingCookie = "Synthetic paging cookie";
            var client = Client(expected, xml => GoldenQueryAssertions.Equal(golden, xml));
            var sut = new FeeService(new object(), client.Object);

            var result = Invoke(sut, operation);

            Assert.Same(expected, result);
            Assert.True(result.MoreRecords);
            Assert.Equal("Synthetic paging cookie", result.PagingCookie);
            Assert.Equal(2, result.Entities.Count);
            Assert.NotEqual(result.Entities[0].Id, result.Entities[1].Id);
            Assert.Equal(result.Entities[0]["new_name"], result.Entities[1]["new_name"]);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>
        /// 課程ID是查詢界線；注入空白、空GUID及XML注入字串均須在I/O前拒絕，
        /// 不可將XML編碼誤當成授權或合法識別碼驗證。
        /// </summary>
        /// <param name="operation">已繳或未繳查詢。</param>
        /// <param name="invalidId">無效的合成識別值。</param>
        [Theory]
        [InlineData("Paid", "")]
        [InlineData("Unpaid", " ")]
        [InlineData("Paid", "00000000-0000-0000-0000-000000000000")]
        [InlineData("Unpaid", "<&\"'")]
        public void LessonQuery_InvalidId_RejectsBeforeIo(string operation, string invalidId)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(new object(), client.Object);

            Assert.Throws<ArgumentException>(() => Invoke(sut, operation, invalidId));

            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 舊API保留但未使用的課程名稱可含XML字元，不能被加入查詢或拿來當識別／權限；
        /// 完整golden仍相等，證明兩個方法只按伺服器提供的課程ID查詢。
        /// </summary>
        /// <param name="operation">已繳或未繳查詢。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        public void LessonQuery_DisplayNameCannotChangeScope(string operation)
        {
            var client = Client(Rows(), xml => GoldenQueryAssertions.Equal(
                operation == "Paid" ? "fee-paid.xml" : "fee-unpaid.xml", xml));
            var sut = new FeeService(new object(), client.Object);

            Assert.Equal(2, Invoke(sut, operation, ScopeId, SpecialText).Entities.Count);
        }

        /// <summary>
        /// 訂單號是文字而非GUID；含所有XML特殊字元仍須只產生原有兩個OR條件，
        /// 解碼後值逐字相等，不能新增節點或讓卡／ATM任一分支遺失。
        /// </summary>
        [Fact]
        public void OrderQuery_XmlSpecialCharacters_RoundTripWithoutInjection()
        {
            var client = Client(Rows(), xml =>
            {
                var entity = XDocument.Parse(xml).Root!.Element("entity")!;
                var conditions = entity.Element("filter")!.Element("filter")!.Elements("condition").ToArray();
                Assert.Equal("or", (string?)entity.Element("filter")!.Element("filter")!.Attribute("type"));
                Assert.Equal(2, conditions.Length);
                Assert.Equal(new[] { "new_q_pay_card_order_no", "new_q_pay_order_atm_no" },
                    conditions.Select(condition => (string?)condition.Attribute("attribute")));
                Assert.All(conditions, condition => Assert.Equal(SpecialText, (string?)condition.Attribute("value")));
            });
            var sut = new FeeService(new object(), client.Object);

            Assert.Equal(2, sut.RetrieveFeeByFetchXmlOrderNumber(SpecialText).Entities.Count);
        }

        /// <summary>
        /// 空訂單號不得擴張為廣泛查詢，並且拒絕前不碰SDK。
        /// </summary>
        /// <param name="orderNumber">合成無效輸入。</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void OrderQuery_EmptyInput_RejectsBeforeIo(string? orderNumber)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(new object(), client.Object);

            Assert.Throws<ArgumentException>(() => sut.RetrieveFeeByFetchXmlOrderNumber(orderNumber!));

            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 每個新增方法注入SDK故障，原始同一Exception必須傳回host，不能回空集合、
        /// 吞例外或包成含私密本文的新訊息。Core不是最終通知邊界。
        /// </summary>
        /// <param name="operation">新增查詢名稱。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        [InlineData("Order")]
        public void NewQuery_SdkFault_PropagatesSameException(string operation)
        {
            var fault = new InvalidOperationException("Synthetic private SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Throws(fault);
            var sut = new FeeService(new object(), client.Object);

            Assert.Same(fault, Assert.Throws<InvalidOperationException>(() => Invoke(sut, operation)));
        }

        /// <summary>
        /// 空殼認獻／收費方法必須沿用已鎖定的有效查詢，而不是自行回空集合。
        /// SDK request、上限及欄位與有效來源golden完全一致。
        /// </summary>
        /// <param name="operation">原有空方法。</param>
        [Theory]
        [InlineData("Booking")]
        [InlineData("Fee")]
        public void FormerPlaceholder_UsesEstablishedQuery(string operation)
        {
            var expected = operation == "Booking" ? new EntityCollection(new[]
            {
                new Entity("new_dedication_booking", new Guid("22222222-2222-4222-8222-222222222222"))
            }) : Rows();
            var client = Client(expected, xml => GoldenQueryAssertions.Equal(
                operation == "Booking" ? "query-dedication-booking.xml" : "query-fee-by-booking.xml", xml));
            var sut = new FeeService(new object(), client.Object);

            Assert.Same(expected, Invoke(sut, operation));
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
        }

        /// <summary>
        /// 注入同一非空資料庫ID兩次；所有受改查詢都必須拒絕整個候選，
        /// 不可保留一筆、名稱去重或將部分結果交給上層。
        /// </summary>
        /// <param name="operation">Fee全部公開查詢入口。</param>
        [Theory]
        [InlineData("Paid")]
        [InlineData("Unpaid")]
        [InlineData("Order")]
        [InlineData("Booking")]
        [InlineData("Fee")]
        [InlineData("Dedication")]
        [InlineData("DateRange")]
        public void Query_DuplicateStableId_RejectsPublication(string operation)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            candidate.Entities[1].Id = candidate.Entities[0].Id;
            var client = Client(candidate, _ => { });
            var sut = new FeeService(new object(), client.Object);

            var fault = Assert.Throws<InvalidOperationException>(() => Invoke(sut, operation));

            Assert.DoesNotContain(candidate.Entities[0].Id.ToString(), fault.Message);
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 欠缺真實資料庫 ID 的正式列或混入其他實體列均拒絕；不能產生draft key讓正式資料通過。
        /// </summary>
        /// <param name="operation">Fee全部查詢入口。</param>
        /// <param name="missingId">注入空ID或錯誤實體型別。</param>
        [Theory]
        [InlineData("Paid", true)]
        [InlineData("Unpaid", false)]
        [InlineData("Order", true)]
        [InlineData("Booking", false)]
        [InlineData("Fee", true)]
        [InlineData("Dedication", false)]
        [InlineData("DateRange", true)]
        public void Query_InvalidStableIdentity_RejectsPublication(string operation, bool missingId)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            if (missingId) candidate.Entities[1].Id = Guid.Empty;
            else candidate.Entities[1].LogicalName = "synthetic_wrong_entity";
            var client = Client(candidate, _ => { });

            Assert.Throws<InvalidOperationException>(() => Invoke(new FeeService(new object(), client.Object), operation));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 所有lookup顯示名稱及期數文字只可當XML值；含特殊字元解碼後須逐字相等，
        /// 條件數不增加，證明修復既有入口而非只保護新增訂單方法。
        /// </summary>
        /// <param name="operation">使用外部文字的既有查詢。</param>
        [Theory]
        [InlineData("Booking")]
        [InlineData("Fee")]
        [InlineData("Dedication")]
        [InlineData("DateRange")]
        public void ExistingQuery_SpecialDisplayName_IsOnlyAnAttributeValue(string operation)
        {
            var candidate = Rows(operation == "Booking" ? "new_dedication_booking" : "new_fee");
            var client = Client(candidate, xml =>
            {
                var lookups = XDocument.Parse(xml).Descendants("condition")
                    .Where(condition => condition.Attribute("uiname") != null).ToArray();
                Assert.Single(lookups);
                Assert.Equal(SpecialText, (string?)lookups[0].Attribute("uiname"));
                if (operation == "Fee")
                    Assert.Equal(SpecialText, (string?)XDocument.Parse(xml).Descendants("condition")
                        .Single(condition => (string?)condition.Attribute("attribute") == "new_paid_period").Attribute("value"));
            });

            var sut = new FeeService(new object(), client.Object);
            if (operation == "Fee")
                sut.RetrieveFee(SpecialText, ScopeId, SpecialText);
            else
                Invoke(sut, operation, ScopeId, SpecialText);
        }

        /// <summary>
        /// SDK每次提供獨立操作資料；第一個呼叫者修改集合不可污染下一次回應，
        /// 並以兩次真實SDK呼叫證明服務沒有偷偷快取前一範圍的可變結果。
        /// </summary>
        [Fact]
        public void Query_CallerMutation_DoesNotBecomeNextOperationState()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Returns(() =>
                {
                    var response = new RetrieveMultipleResponse();
                    response.Results["EntityCollection"] = Rows();
                    return response;
                });
            var sut = new FeeService(new object(), client.Object);
            var first = Invoke(sut, "Paid");
            first.Entities[0]["new_name"] = "Synthetic caller mutation";
            first.Entities.RemoveAt(1);

            var second = Invoke(sut, "Paid");

            Assert.NotSame(first, second);
            Assert.Equal(2, second.Entities.Count);
            Assert.Equal("Synthetic Fee", second.Entities[0]["new_name"]);
            client.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Exactly(2));
        }

        /// <summary>建構時拒絕沒有授權SDK的服務，不能延至呼叫才以NullReferenceException掩蓋設定失敗。</summary>
        [Fact]
        public void Constructor_NullService_RejectsImmediately()
        {
            Assert.Throws<ArgumentNullException>(() => new FeeService(null!));
        }

        /// <summary>
        /// 期數是必要業務條件；明確鎖定空白拒絕而非悄悄擴張查詢。
        /// 現有 ChurchReport 消費者固定傳入001，此邊界不改變其有效流程；拒絕前不碰SDK。
        /// </summary>
        /// <param name="period">無效的合成期數。</param>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void RetrieveFee_EmptyPeriod_RejectsBeforeIo(string period)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(client.Object);
            Assert.Throws<ArgumentException>(() => sut.RetrieveFee("Synthetic Booking", ScopeId, period));
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 非西曆環境不能改變資料庫日期條件；在本案局部注入泰國曆並finally還原，
        /// 應仍符合已核對的西曆golden，不能靠改主機全域文化換取通過。
        /// </summary>
        [Fact]
        public void DateRange_ThaiCulture_UsesGregorianQuery()
        {
            var client = Client(Rows(), xml => GoldenQueryAssertions.Equal("fee-dedication-date-range.xml", xml));
            var sut = new FeeService(new object(), client.Object);
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
                Invoke(sut, "DateRange");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        /// <summary>
        /// 反向日期區間是預期輸入拒絕，必須在 SDK I/O 前停止，不能擴張成不受限查詢。
        /// 此案例保護既有安全分支；不修改產品斷言或以空集合冒充合法結果。
        /// </summary>
        [Fact]
        public void DateRange_StartAfterEnd_RejectsBeforeIo()
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sut = new FeeService(client.Object);
            Assert.Throws<ArgumentException>(() => sut.RetrieveDedicationFeeByDateRange("Synthetic Contact", ScopeId,
                new DateTime(2027, 3, 4), new DateTime(2027, 1, 2)));
            client.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A/B各自借入SDK，交錯執行1000次不混查詢或資料，操作全部排空後不留活動工作；
        /// IDisposable借入服務不能被Fee關閉。固定八路排程避免測試本身無界fan-out。
        /// </summary>
        [Fact]
        public void Query_ParallelScopes_DoNotLeakOrDisposeBorrowedService()
        {
            var firstClient = Client(Rows(), xml => Assert.Contains(ScopeId, xml));
            var secondId = "44444444-4444-4444-8444-444444444444";
            var secondRows = Rows();
            secondRows.Entities[0].Id = new Guid("55555555-5555-4555-8555-555555555555");
            var secondClient = Client(secondRows, xml => Assert.Contains(secondId, xml));
            var firstDisposable = firstClient.As<IDisposable>();
            var secondDisposable = secondClient.As<IDisposable>();
            var first = new FeeService(new object(), firstClient.Object);
            var second = new FeeService(new object(), secondClient.Object);
            var active = 0;
            var completed = 0;

            Parallel.For(0, 1000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
            {
                Interlocked.Increment(ref active);
                try
                {
                    var isFirst = index % 2 == 0;
                    var result = Invoke(isFirst ? first : second, "Paid", isFirst ? ScopeId : secondId);
                    Assert.Equal(isFirst ? Rows().Entities[0].Id : secondRows.Entities[0].Id, result.Entities[0].Id);
                    Interlocked.Increment(ref completed);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });

            Assert.Equal(1000, completed);
            Assert.Equal(0, active);
            firstDisposable.Verify(service => service.Dispose(), Times.Never);
            secondDisposable.Verify(service => service.Dispose(), Times.Never);
        }

        /// <summary>
        /// 建立每案獨立SDK替身；只保留該案候選，callback收到的XML立即驗證，不保存請求或credential。
        /// </summary>
        private static Mock<IOrganizationService> Client(EntityCollection candidate, Action<string> assertXml)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = candidate;
            client.Setup(service => service.Execute(It.IsAny<OrganizationRequest>()))
                .Callback<OrganizationRequest>(request => assertXml(
                    Assert.IsType<FetchExpression>(Assert.IsType<RetrieveMultipleRequest>(request).Query).Query))
                .Returns(response);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => assertXml(Assert.IsType<FetchExpression>(query).Query))
                .Returns(candidate);
            return client;
        }

        /// <summary>合成的兩筆同名不同ID資料，沒有靜態可變資料或真實個資。</summary>
        private static EntityCollection Rows(string logicalName = "new_fee") => new EntityCollection(new[]
        {
            new Entity(logicalName, new Guid("22222222-2222-4222-8222-222222222222")) { ["new_name"] = "Synthetic Fee" },
            new Entity(logicalName, new Guid("33333333-3333-4333-8333-333333333333")) { ["new_name"] = "Synthetic Fee" }
        });

        /// <summary>依固定測試名稱呼叫真正服務，不在替身複製查詢或驗證邏輯。</summary>
        private static EntityCollection Invoke(FeeService sut, string operation, string id = ScopeId, string name = "Synthetic Contact")
        {
            switch (operation)
            {
                case "Paid": return sut.GetPaidFee(name, id);
                case "Unpaid": return sut.GetNotYetPaidFee(name, id);
                case "Order": return sut.RetrieveFeeByFetchXmlOrderNumber("Synthetic-Order");
                case "Booking": return sut.RetrieveDedicationBooking(name, id);
                case "Fee": return sut.RetrieveFee("Synthetic Booking", id, "3");
                case "Dedication": return sut.RetrieveDedicationFee(name, id);
                case "DateRange": return sut.RetrieveDedicationFeeByDateRange(name, id, new DateTime(2027, 1, 2), new DateTime(2027, 3, 4));
                default: throw new ArgumentException("Unknown synthetic operation.", nameof(operation));
            }
        }
    }
}

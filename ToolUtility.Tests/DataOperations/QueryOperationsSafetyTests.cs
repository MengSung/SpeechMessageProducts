/*
 * 檔案整體說明
 *
 * 目的與定位：三組純 SDK 資料操作的故障、XML 編碼、跨 scope 與借入資源生命週期契約。 測試在純化前先重現缺陷；SDK 全為合成替身，不能冒充實機資料或部署驗收。
 *
 * 主要功能與流程：
 * 本檔宣告 QueryOperationsSafetyTests。
 * - 對每一實際 XML 消費頁注入四種完整性故障，覆蓋全部公開查詢而非只測 helper。
 * - 重複 ID、空 ID、跨實體或缺候選都須整頁拒絕，不去重或回假空集合。 失敗後原候選筆數必須不變，避免以修改 SDK 結果掩蓋資料衝突。
 * - 舊 ByDate 是獨立型別化入口，即使目前與 XML 路徑共用驗頁也要直接故障注入， 防止日後改成直接回 SDK 而繞過完整發布；四種故障均不可回部分／假空結果。
 * - 會友、課程、週報、小組與排程文字包含全部特殊字元時，XML 解碼必須完整還原， 且只出現在預定屬性值，不能成為額外條件／節點；不以字串 Contains 當作安全證據。
 * 測試案例入口包含 InvalidPages、Query_InvalidPage_RejectsWithoutMutatingCandidate、
 * AppointmentByDate_InvalidPage_RejectsEntireCandidate、Query_XmlSpecialCharacters_RemainOnlyAttributeValues、
 * Query_ThaiCulture_UsesInvariantGregorianDates、Query_SdkFailure_PropagatesOriginalAndAllowsRetry、
 * Query_ReverseDateRange_RejectsBeforeIo、Query_InvalidLookup_RejectsBeforeIo；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 三組純 SDK 資料操作的故障、XML 編碼、跨 scope 與借入資源生命週期契約。
    /// 測試在純化前先重現缺陷；SDK 全為合成替身，不能冒充實機資料或部署驗收。
    /// </summary>
    public sealed class QueryOperationsSafetyTests
    {
        private const string SpecialText = "Synthetic <&\"' value";

        /// <summary>對每一實際 XML 消費頁注入四種完整性故障，覆蓋全部公開查詢而非只測 helper。</summary>
        public static IEnumerable<object[]> InvalidPages()
        {
            foreach (var item in QueryOperationsCharacterizationTests.Operations())
                foreach (var fault in new[] { "duplicate", "empty", "entity", "null" })
                    yield return new object[] { item[0], fault };
        }

        /// <summary>
        /// 重複 ID、空 ID、跨實體或缺候選都須整頁拒絕，不去重或回假空集合。
        /// 失敗後原候選筆數必須不變，避免以修改 SDK 結果掩蓋資料衝突。
        /// </summary>
        /// <param name="operation">真正服務入口。</param>
        /// <param name="fault">合成完整性故障。</param>
        [Theory]
        [MemberData(nameof(InvalidPages))]
        public void Query_InvalidPage_RejectsWithoutMutatingCandidate(string operation, string fault)
        {
            var candidate = DataOperationFixture.Rows(operation);
            if (fault == "duplicate") candidate.Entities[1].Id = candidate.Entities[0].Id;
            if (fault == "empty") candidate.Entities[1].Id = Guid.Empty;
            if (fault == "entity") candidate.Entities[1].LogicalName = "synthetic_wrong_entity";
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns(fault == "null" ? null! : candidate);
            Assert.Throws<InvalidOperationException>(() => DataOperationFixture.Run(operation, client.Object));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 舊 ByDate 是獨立型別化入口，即使目前與 XML 路徑共用驗頁也要直接故障注入，
        /// 防止日後改成直接回 SDK 而繞過完整發布；四種故障均不可回部分／假空結果。
        /// </summary>
        /// <param name="fault">重複、空 ID、錯實體或 null 頁。</param>
        [Theory]
        [InlineData("duplicate")]
        [InlineData("empty")]
        [InlineData("entity")]
        [InlineData("null")]
        public void AppointmentByDate_InvalidPage_RejectsEntireCandidate(string fault)
        {
            var candidate = DataOperationFixture.Rows("appointment-range");
            if (fault == "duplicate") candidate.Entities[1].Id = candidate.Entities[0].Id;
            if (fault == "empty") candidate.Entities[1].Id = Guid.Empty;
            if (fault == "entity") candidate.Entities[1].LogicalName = "synthetic_wrong_entity";
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns(fault == "null" ? null! : candidate);
            var service = new AppointmentService(new object(), client.Object);
            Assert.Throws<InvalidOperationException>(() => service.RetrieveByDate(new DateTime(2027, 1, 2)));
            Assert.Equal(2, candidate.Entities.Count);
        }

        /// <summary>
        /// 會友、課程、週報、小組與排程文字包含全部特殊字元時，XML 解碼必須完整還原，
        /// 且只出現在預定屬性值，不能成為額外條件／節點；不以字串 Contains 當作安全證據。
        /// </summary>
        /// <param name="operation">有使用者顯示文字或排程值的查詢。</param>
        [Theory]
        [InlineData("appointment-contact")]
        [InlineData("appointment-schedule")]
        [InlineData("lessons-enrolled")]
        [InlineData("lessons-stor")]
        [InlineData("meeting-weekly")]
        [InlineData("meeting-contact")]
        [InlineData("meeting-weekly-alt")]
        [InlineData("meeting-expired")]
        [InlineData("meeting-group")]
        public void Query_XmlSpecialCharacters_RemainOnlyAttributeValues(string operation)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            string? ordinaryXml = null;
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query =>
                {
                    var xml = Assert.IsType<FetchExpression>(query).Query;
                    var document = XDocument.Parse(xml);
                    if (ordinaryXml == null) ordinaryXml = xml;
                    else
                    {
                        var ordinary = XDocument.Parse(ordinaryXml);
                        Assert.Equal(ordinary.Descendants().Count(), document.Descendants().Count());
                        Assert.Contains(document.Descendants().Attributes(), value => value.Value == SpecialText);
                        Assert.Equal(ordinary.Descendants("condition").Count(), document.Descendants("condition").Count());
                    }
                }).Returns(DataOperationFixture.Rows(operation));
            DataOperationFixture.Run(operation, client.Object);
            DataOperationFixture.Run(operation, client.Object, text: SpecialText);
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(2));
        }

        /// <summary>非西曆環境下全部日期與選項仍須符合原西曆 golden，finally 還原當案文化。</summary>
        /// <param name="operation">全部固定合成操作。</param>
        [Theory]
        [MemberData(nameof(QueryOperationsCharacterizationTests.Operations), MemberType = typeof(QueryOperationsCharacterizationTests))]
        public void Query_ThaiCulture_UsesInvariantGregorianDates(string operation)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            client.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Callback<QueryBase>(query => GoldenQueryAssertions.Equal(operation + ".xml",
                    Assert.IsType<FetchExpression>(query).Query)).Returns(DataOperationFixture.Rows(operation));
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
                DataOperationFixture.Run(operation, client.Object);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        /// <summary>
        /// SDK 真正故障須原樣傳至最終 host 通知邊界；下一次可重新查詢，
        /// 不因失敗建立永久失敗 cache、隱藏 retry 或傳回部分候選。
        /// </summary>
        /// <param name="operation">全部查詢入口。</param>
        [Theory]
        [MemberData(nameof(QueryOperationsCharacterizationTests.Operations), MemberType = typeof(QueryOperationsCharacterizationTests))]
        public void Query_SdkFailure_PropagatesOriginalAndAllowsRetry(string operation)
        {
            var failure = new InvalidOperationException("Synthetic private SDK failure");
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            var candidate = DataOperationFixture.Rows(operation);
            client.SetupSequence(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(failure).Returns(candidate);
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => DataOperationFixture.Run(operation, client.Object)));
            Assert.Same(candidate, DataOperationFixture.Run(operation, client.Object));
            client.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(2));
        }

        /// <summary>反向日期區間不得進 SDK，也不能以交換端點改變 caller 的原始意圖。</summary>
        /// <param name="operation">含明確起訖日期的入口。</param>
        [Theory]
        [InlineData("appointment-range")]
        [InlineData("appointment-schedule")]
        [InlineData("lessons-enrolled")]
        [InlineData("lessons-month")]
        public void Query_ReverseDateRange_RejectsBeforeIo(string operation)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            Assert.Throws<ArgumentException>(() => DataOperationFixture.Run(operation, client.Object,
                start: new DateTime(2027, 3, 4), end: new DateTime(2027, 1, 2)));
            client.VerifyNoOtherCalls();
        }

        /// <summary>所有 lookup GUID 先驗證；含 XML 符號的偽 ID 不能成為條件或未授權廣泛查詢。</summary>
        /// <param name="operation">使用主或次要 lookup 的入口。</param>
        /// <param name="secondary">改壞第二個 lookup，避免只驗第一個 ID 的盲點。</param>
        [Theory]
        [InlineData("appointment-contact", false)]
        [InlineData("lessons-enrolled", false)]
        [InlineData("lessons-stor", false)]
        [InlineData("lessons-stor", true)]
        [InlineData("meeting-weekly", false)]
        [InlineData("meeting-weekly", true)]
        [InlineData("meeting-contact", false)]
        [InlineData("meeting-weekly-alt", false)]
        [InlineData("meeting-weekly-alt", true)]
        [InlineData("meeting-expired", false)]
        [InlineData("meeting-group", false)]
        [InlineData("meeting-group", true)]
        public void Query_InvalidLookup_RejectsBeforeIo(string operation, bool secondary)
        {
            var client = new Mock<IOrganizationService>(MockBehavior.Strict);
            foreach (var invalid in new[] { "", "not-a-guid", "<&\"'", Guid.Empty.ToString() })
                Assert.Throws<ArgumentException>(() => DataOperationFixture.Run(operation, client.Object,
                    primaryId: secondary ? DataOperationFixture.PrimaryId : invalid,
                    secondaryId: secondary ? invalid : DataOperationFixture.SecondaryId));
            client.VerifyNoOtherCalls();
        }

        /// <summary>不存在的借入 SDK 是設定錯誤，三個建構式都需立即拒絕，不保存任意 logger。</summary>
        [Fact]
        public void Constructors_NullSdk_RejectImmediately()
        {
            Assert.Throws<ArgumentNullException>(() => new AppointmentService(new object(), null!));
            Assert.Throws<ArgumentNullException>(() => new LessonsService(new object(), null!));
            Assert.Throws<ArgumentNullException>(() => new MeetingStatisticsService(new object(), null!));
        }

        /// <summary>
        /// 八路交錯 1200 次查詢獨立 A/B SDK，資料與 caller 修改不能跨 scope 或下一次操作。
        /// 全部同步 I/O drain 後 active=0；借入的 IDisposable SDK 不由純資料服務關閉。
        /// </summary>
        [Fact]
        public void Queries_ParallelScopesAndCallerMutation_IsolateAndDrain()
        {
            var operations = QueryOperationsCharacterizationTests.Operations().Select(item => (string)item[0]).ToArray();
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            foreach (var client in clients) client.As<IDisposable>();
            for (var scope = 0; scope < clients.Length; scope++)
            {
                var marker = scope == 0 ? "Synthetic A" : "Synthetic B";
                clients[scope].Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
                    .Returns<QueryBase>(query =>
                    {
                        var entityName = (string)XDocument.Parse(Assert.IsType<FetchExpression>(query).Query).Root!.Element("entity")!.Attribute("name")!;
                        return new EntityCollection(new[] { new Entity(entityName, Guid.NewGuid()) { ["name"] = marker } });
                    });
            }
            var active = 0;
            var completed = 0;
            Parallel.For(0, 1200, new ParallelOptions { MaxDegreeOfParallelism = 8 }, index =>
            {
                Interlocked.Increment(ref active);
                try
                {
                    var scope = index % 2;
                    var row = DataOperationFixture.Run(operations[index % operations.Length], clients[scope].Object).Entities[0];
                    Assert.Equal(scope == 0 ? "Synthetic A" : "Synthetic B", row["name"]);
                    row["name"] = "Synthetic caller mutation";
                    Interlocked.Increment(ref completed);
                }
                finally { Interlocked.Decrement(ref active); }
            });
            Assert.Equal(1200, completed);
            Assert.Equal(0, active);
            foreach (var client in clients) client.As<IDisposable>().Verify(value => value.Dispose(), Times.Never);
        }
    }
}

# CCG reviewer Task: integration-p1-04-review-evidence

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
P1-04增量審查結案證據，不擴讀其他檔、不執行/修改、不呼叫模型。前兩輪Fetch/CI20261105-133329與Operations20261105-134544均dualModelComplete=true無quota、Critical/High。這是窄範圍補證，不重開已審查過的整個repo，P1-04尚未全包完成。
請審：1. Claude說三介面無#nullable即oblivious：但strict專案全域Nullable=enable，附雙目標MSBuild實值及三介面實際NullabilityInfoContext反射測試均NotNull。故評估為誤報，不為此改公開API或重複pragma，請確認這個結論。防禦性null處理維持來源相容，不表示宣告必須nullable。2. ByDate四種直接故障注入已補；helper完整內容及6正反測試已附(過去20261105-124121局部審過)，請檢查不是字串或自動更新golden。3. 這輪只新測試，沒有產品行為修改。執行8/8：四ByDate故障、原1ByDate正常、三介面metadata。4. 整合全方案16TRX1825全過早於這次純測試增補，不冒充最新全部測試；待最後重跑。
請輸出Critical/High/Warning/Info；不要把限定快照外的既有已審區域都重新當缺口，若缺少本次新增邏輯必要證據才具體指出。

## ToolUtility.Tests/TestHelpers/GoldenQueryAssertions.cs
```
using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// 比對內嵌的合成查詢黃金資料，鎖定跨目標搬移前後的 XML 條件樹。
    /// 不比較無語義的引號與縮排；元素、順序、屬性及欄位值必須完全相同。
    /// </summary>
    internal static class GoldenQueryAssertions
    {
        /// <summary>
        /// 以固定資源名稱讀取預期查詢，拒絕 DTD 與過大 XML 後比較正規化內容。
        /// 每次讀取獨立擁有資源串流及 reader，成功或斷言失敗皆確定釋放，不保存查詢資料。
        /// </summary>
        /// <param name="fileName">測試作者指定的資源檔名，不接受使用者輸入。</param>
        /// <param name="actualXml">受測服務實際送到 SDK 替身的 XML。</param>
        internal static void Equal(string fileName, string actualXml)
        {
            using (var stream = typeof(GoldenQueryAssertions).Assembly.GetManifestResourceStream(
                "ToolUtility.Tests.Golden.ToolUtility." + fileName))
            {
                Assert.NotNull(stream);
                using (var reader = XmlReader.Create(stream!, CreateSettings()))
                {
                    var expected = XDocument.Load(reader).ToString(SaveOptions.DisableFormatting);
                    using (var text = new StringReader(actualXml))
                    using (var actual = XmlReader.Create(text, CreateSettings()))
                    {
                        Assert.Equal(expected, XDocument.Load(actual).ToString(SaveOptions.DisableFormatting));
                    }
                }
            }
        }

        /// <summary>
        /// 建立單次 reader 的安全設定；固定上限足以容納測試查詢，禁止外部解析。
        /// </summary>
        private static XmlReaderSettings CreateSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = true,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            };
        }
    }
}

```

## ToolUtility.Tests/TestHelpers/GoldenQueryAssertionsTests.cs
```
using System.Xml;
using Xunit;
using Xunit.Sdk;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// 驗證搬移守門自身確實拒絕語義變更，不會只因 XML 能解析就放行。
    /// 所有內容為固定合成字串；測試不使用檔案系統、網路、真實識別碼或共用可變狀態。
    /// </summary>
    public sealed class GoldenQueryAssertionsTests
    {
        /// <summary>
        /// 只改引號及縮排應通過；若 helper 又開始比較無語義空白，此案例會失敗。
        /// </summary>
        [Fact]
        public void Equal_OnlyWhitespaceAndQuoteStyleDiffer_Passes()
        {
            GoldenQueryAssertions.Equal("comparison-contract.xml",
                "<fetch><entity name='synthetic'><attribute name='syntheticid'/><order attribute='syntheticid' descending='false'/></entity></fetch>");
        }

        /// <summary>
        /// 注入欄位改名、遺漏排序或變更元素順序，均須斷言失敗，不能降低查詢黃金檔強度。
        /// </summary>
        /// <param name="actualXml">只有一個明確契約差異的合成查詢。</param>
        [Theory]
        [InlineData("<fetch><entity name='other'><attribute name='syntheticid'/><order attribute='syntheticid' descending='false'/></entity></fetch>")]
        [InlineData("<fetch><entity name='synthetic'><attribute name='syntheticid'/></entity></fetch>")]
        [InlineData("<fetch><entity name='synthetic'><order attribute='syntheticid' descending='false'/><attribute name='syntheticid'/></entity></fetch>")]
        public void Equal_QueryContractChanges_Rejects(string actualXml)
        {
            Assert.ThrowsAny<XunitException>(() => GoldenQueryAssertions.Equal("comparison-contract.xml", actualXml));
        }

        /// <summary>
        /// 注入 DTD，即使實體是內嵌文字也拒絕解析；防止日後無意啟用外部實體解析。
        /// </summary>
        [Fact]
        public void Equal_Dtd_RejectsBeforeComparison()
        {
            Assert.Throws<XmlException>(() => GoldenQueryAssertions.Equal("comparison-contract.xml",
                "<!DOCTYPE fetch [<!ENTITY value 'synthetic'>]><fetch><entity name='&value;'/></fetch>"));
        }

        /// <summary>
        /// 注入超過 1 MiB 的短期合成 XML；reader 必須以容量錯誤拒絕，不能保留無界輸入。
        /// </summary>
        [Fact]
        public void Equal_OversizedDocument_RejectsBeforeComparison()
        {
            var oversized = "<fetch>" + new string('x', 1024 * 1024) + "</fetch>";
            Assert.Throws<XmlException>(() => GoldenQueryAssertions.Equal("comparison-contract.xml", oversized));
        }
    }
}

```

## ToolUtility.Tests/DataOperations/QueryOperationsSafetyTests.cs
```
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

```

## ToolUtility.Tests/CoreTargetFrameworkTests.cs
```
using System.Reflection;
using System.Runtime.Versioning;
using ToolUtilityNameSpace.FeeOperations;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.AppointmentOperations;
using ToolUtilityNameSpace.LessonsOperations;
using ToolUtilityNameSpace.MeetingStatisticsOperations;
using Xunit;

namespace ToolUtility.Tests
{
    /// <summary>
    /// 反射實際載入產品組件，而非只讀csproj文字；Framework測試宿主可以net48，
    /// 但產品輸出必須仍是核定net462，不能誤載net48／host組件而假稱雙目標通過。
    /// </summary>
    public sealed class CoreTargetFrameworkTests
    {
        /// <summary>
        /// 比對FeeService所屬assembly的目標中繼資料；錯用任一產品目標即決定性失敗，
        /// 不接觸D365、不配置連線或其他需處置資源。
        /// </summary>
        [Fact]
        public void LoadedCore_UsesApprovedProductTarget()
        {
            var assembly = typeof(FeeService).Assembly;
            Assert.Same(assembly, typeof(EntityQueryService).Assembly);
            Assert.Same(assembly, typeof(AppointmentService).Assembly);
            Assert.Same(assembly, typeof(LessonsService).Assembly);
            Assert.Same(assembly, typeof(MeetingStatisticsService).Assembly);
            Assert.Equal("ToolUtility.Core", assembly.GetName().Name);
            var target = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(target);
#if NETFRAMEWORK
            Assert.Equal(".NETFramework,Version=v4.6.2", target!.FrameworkName);
#else
            Assert.Equal(".NETCoreApp,Version=v10.0", target!.FrameworkName);
#endif
        }

#if !NETFRAMEWORK
        /// <summary>
        /// 實際反射三個介面的公開 nullable 中繼資料，而非以檔頭是否有 pragma 判斷。
        /// Core strict 專案統一啟用 Nullable；若後續設定漂移成 oblivious，任一字串參數即決定性失敗。
        /// 不需修改產品公開 API 或新增重複 pragma 來配合靜態審查推測。
        /// </summary>
        [Theory]
        [InlineData(typeof(IAppointmentService))]
        [InlineData(typeof(ILessonsService))]
        [InlineData(typeof(IMeetingStatisticsService))]
        public void CoreInterfaces_EmitNonObliviousStringContracts(System.Type contract)
        {
            var context = new NullabilityInfoContext();
            var checkedParameters = 0;
            foreach (var method in contract.GetMethods())
                foreach (var parameter in method.GetParameters())
                    if (parameter.ParameterType == typeof(string))
                    {
                        Assert.Equal(NullabilityState.NotNull, context.Create(parameter).ReadState);
                        checkedParameters++;
                    }
            Assert.True(checkedParameters > 0);
        }
#endif
    }
}

```

## ToolUtility.Core/ToolUtility.Core.csproj
```
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net462;net10.0</TargetFrameworks>
    <RootNamespace>ToolUtilityNameSpace</RootNamespace>
    <SpeechMessageStrictCode>true</SpeechMessageStrictCode>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net462'">
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" PrivateAssets="all" />
    <!-- Framework SDK 傳遞引用 6.0.8，直接使用中央安全修補版；net10 則使用內建 JSON。 -->
    <PackageReference Include="System.Text.Json" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <!-- 僅取得 SDK 型別；純資料核心不建立 ServiceClient 或擁有連線。 -->
    <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" PrivateAssets="all" />
    <!-- 舊 SDK 帶入的傳遞版本有已知弱點；使用中央表的安全版本，維持完整 audit。 -->
    <PackageReference Include="System.Security.Cryptography.Pkcs" PrivateAssets="all" />
  </ItemGroup>
</Project>

```

## ToolUtility.Core/AppointmentOperations/IAppointmentService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.AppointmentOperations
{
    /// <summary>
    /// 已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。
    /// 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 host 共用例外通知邊界。
    /// 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
    /// </summary>
    public interface IAppointmentService
    {
        /// <summary>保留舊版未依日期過濾、全欄位 QueryByAttribute 的相容行為；如需日期過濾使用明確區間 API。整頁穩定 ID 驗證後才發布。</summary>
        /// <param name="selectedDate">舊版未使用的相容日期參數，不作授權或新增過濾。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByDate(DateTime selectedDate);

        /// <summary>依原 scheduledstart 含端點區間與投影查約會；拒絕反向區間，固定西曆日期，原分頁資訊完整保留。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByDateRange(DateTime startDate, DateTime endDate);

        /// <summary>依已授權聯絡人、今年與原三個簽核狀態查詢；顯示文字安全編碼，正式 GUID 驗證不能由名稱取代。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByContactWithinYear(string contactName, string contactId);

        /// <summary>在原日期區間追加既有排程值；文字只作 XML 屬性，不可注入或省略查詢條件。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <param name="scheduleType">原 SDK 排程值，安全編碼且不保存。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByDateRangeAndScheduleType(DateTime startDate, DateTime endDate, string scheduleType);
    }
}

```

## ToolUtility.Core/LessonsOperations/ILessonsService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.LessonsOperations
{
    /// <summary>
    /// 已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。
    /// 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 host 共用例外通知邊界。
    /// 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
    /// </summary>
    public interface ILessonsService
    {
        /// <summary>依課程起訖日期與原報名 join 查課程；保留 distinct、原投影及完整正式 ID，不以顯示名稱去重。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveEnrolledLessons(DateTime startDate, DateTime endDate, string contactName, string contactId);

        /// <summary>依原課程起訖日期與分類排序查課程；拒絕反向區間，不依程序文化改變曆法，不自行轉換時區。</summary>
        /// <param name="startDate">caller 已決定時區語義的含端點起日。</param>
        /// <param name="endDate">含端點迄日，不可早於起日。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveLessonsByMonth(DateTime startDate, DateTime endDate);

        /// <summary>保留原狀態排除、5筆上限及聯絡人 outer join；兩 lookup 均驗非空 GUID，全部列驗證後才回完整頁。</summary>
        /// <param name="lessonName">僅作 uiname 的課程顯示文字。</param>
        /// <param name="lessonId">server 已授權的非空課程資料庫 GUID。</param>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveStorLessons(string lessonName, string lessonId, string contactName, string contactId);
    }
}

```

## ToolUtility.Core/MeetingStatisticsOperations/IMeetingStatisticsService.cs
```
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtilityNameSpace.MeetingStatisticsOperations
{
    /// <summary>
    /// 已由 host 驗證組織與授權範圍的純 SDK 查詢契約；SDK 只借入，host 負責 timeout、drain 與 Dispose。
    /// 不保存 logger、憑證、查詢結果或共用可變資料；真實故障原樣交回 host 共用例外通知邊界。
    /// 每頁在完整穩定 ID 驗證後才回傳，原分頁資訊與同名不同 ID 資料均保留。
    /// </summary>
    public interface IMeetingStatisticsService
    {
        /// <summary>依已決定語義的主日日期查原欄位與排序；固定西曆，不自行換算時區，正式列 ID 不能為空或重複。</summary>
        /// <param name="sundayDate">caller 已決定語義的主日日期，不在此轉時區。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveBySunday(DateTime sundayDate);

        /// <summary>保留原週報／聯絡人條件及相容 uitype 提示值；lookup GUID 先驗證，不用顯示文字決定權限。</summary>
        /// <param name="weeklyReportName">只供 uiname 的週報顯示文字。</param>
        /// <param name="weeklyReportId">已由 host 授權的非空週報 GUID。</param>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByWeeklyReportAndContact(string weeklyReportName, string weeklyReportId, string contactName, string contactId);

        /// <summary>依聯絡人與主日日期條件查原出席頁；安全編碼且完整驗證正式 ID，同名不同 ID 資料全保留。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <param name="sundayDate">caller 已決定語義的主日日期，不在此轉時區。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveBySundayDateAndContact(string contactName, string contactId, DateTime sundayDate);

        /// <summary>保留替代入口的參數順序與週報 uitype；安全組裝條件、整頁驗證，不另造快取或變更原查詢語義。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <param name="weeklyReportName">只供 uiname 的週報顯示文字。</param>
        /// <param name="weeklyReportId">已由 host 授權的非空週報 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByWeeklyReportAndContactAlt(string contactName, string contactId, string weeklyReportName, string weeklyReportId);

        /// <summary>保留聯絡人與關懷到期日 not-null 條件；不能省略 lookup 而擴張範圍，故障不能變成空集合。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveWithExpiredDateByContact(string contactName, string contactId);

        /// <summary>依聯絡人、小組與主日三項原條件查詢；兩個正式 lookup GUID 均先驗證，不以姓名隔離或去重。</summary>
        /// <param name="contactName">僅作安全編碼的 uiname 顯示文字，不是身分或權限。</param>
        /// <param name="contactId">server 已授權的非空聯絡人資料庫 GUID。</param>
        /// <param name="smallGroupName">只供 uiname 的小組顯示文字。</param>
        /// <param name="smallGroupId">已由 host 授權的非空小組 GUID。</param>
        /// <param name="sundayDate">caller 已決定語義的主日日期，不在此轉時區。</param>
        /// <returns>本次操作擁有的已驗證 SDK 單頁；不共享、不快取，不將故障包裝為成功空頁。</returns>
        EntityCollection RetrieveByContactSmallGroupAndSundayDate(string contactName, string contactId, string smallGroupName, string smallGroupId, DateTime sundayDate);
    }
}

```

## 實際機器輸出 artifacts/p1-04-nullability-net462.json
{
  "Properties": {
    "Nullable": "enable",
    "SpeechMessageStrictCode": "true",
    "GenerateDocumentationFile": "true"
  }
}


## 實際機器輸出 artifacts/p1-04-nullability-net10.0.json
{
  "Properties": {
    "Nullable": "enable",
    "SpeechMessageStrictCode": "true",
    "GenerateDocumentationFile": "true"
  }
}


## 實際機器輸出 artifacts/p1-04-extracted-solution-summary.json
{
  "reports": 16,
  "totals": {
    "total": 1825,
    "executed": 1825,
    "passed": 1825,
    "failed": 0,
    "notExecuted": 0
  },
  "runs": [
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_12_net10.0.trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\LineMessagingProcessor.RichMenus.Tests\\bin\\Release\\net10.0\\LineMessagingProcessor.RichMenus.Tests.dll"
      ],
      "counters": {
        "total": "34",
        "executed": "34",
        "passed": "34",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_12_net10.0[1].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\SpeechMessage.Payments.Tests\\bin\\Release\\net10.0\\SpeechMessage.Payments.Tests.dll"
      ],
      "counters": {
        "total": "55",
        "executed": "55",
        "passed": "55",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_12_net10.0[2].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\SpeechMessage.Localization.Tests\\bin\\Release\\net10.0\\SpeechMessage.Localization.Tests.dll"
      ],
      "counters": {
        "total": "67",
        "executed": "67",
        "passed": "67",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_13_net10.0.trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\LineMessagingProcessor.Tests\\bin\\Release\\net10.0\\LineMessagingProcessor.Tests.dll"
      ],
      "counters": {
        "total": "79",
        "executed": "79",
        "passed": "79",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_13_net10.0[1].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\LineMessagingProcessor.Workflows.Tests\\bin\\Release\\net10.0\\LineMessagingProcessor.Workflows.Tests.dll"
      ],
      "counters": {
        "total": "33",
        "executed": "33",
        "passed": "33",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_13_net10.0[2].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\LineMessagingProcessor.AspNetCore.Tests\\bin\\Release\\net10.0\\LineMessagingProcessor.AspNetCore.Tests.dll"
      ],
      "counters": {
        "total": "4",
        "executed": "4",
        "passed": "4",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_13_net10.0[3].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\Line.Messaging.Tests\\bin\\Release\\net10.0\\Line.Messaging.Tests.dll"
      ],
      "counters": {
        "total": "101",
        "executed": "101",
        "passed": "101",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_13_net10.0[4].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\ToolUtility.Tests\\bin\\Release\\net10.0\\ToolUtility.Tests.dll"
      ],
      "counters": {
        "total": "282",
        "executed": "282",
        "passed": "282",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_13_net10.0[5].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\SpeechMessage.Dynamics.Deployment.Tests\\bin\\Release\\net10.0\\SpeechMessage.Dynamics.Deployment.Tests.dll"
      ],
      "counters": {
        "total": "62",
        "executed": "62",
        "passed": "62",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_13_net10.0[6].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\ToolUtility.Dataverse.Tests\\bin\\Release\\net10.0\\ToolUtility.Dataverse.Tests.dll"
      ],
      "counters": {
        "total": "79",
        "executed": "79",
        "passed": "79",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_13_net10.0[7].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\ChurchReport.MemberInfo.Tests\\bin\\Release\\net10.0\\ChurchReport.MemberInfo.Tests.dll"
      ],
      "counters": {
        "total": "612",
        "executed": "612",
        "passed": "612",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_14_net48.trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\SpeechMessage.Localization.Tests\\bin\\Release\\net48\\SpeechMessage.Localization.Tests.dll"
      ],
      "counters": {
        "total": "43",
        "executed": "43",
        "passed": "43",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_14_net48[1].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\Line.Messaging.Tests\\bin\\Release\\net48\\Line.Messaging.Tests.dll"
      ],
      "counters": {
        "total": "96",
        "executed": "96",
        "passed": "96",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_14_net48[2].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\LineMessagingProcessor.Tests\\bin\\Release\\net48\\LineMessagingProcessor.Tests.dll"
      ],
      "counters": {
        "total": "46",
        "executed": "46",
        "passed": "46",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_14_net48[3].trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\ToolUtility.Tests\\bin\\Release\\net48\\ToolUtility.Tests.dll"
      ],
      "counters": {
        "total": "207",
        "executed": "207",
        "passed": "207",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    },
    {
      "file": "Administrator_LENOVO-LEGION_2026-11-05_13_47_15_net48.trx",
      "assemblies": [
        "D:\\\u97f3\u8a0a\u79d1\u6280\u7522\u54c1\\\u7cfb\u7d71\u5e73\u53f0\\SpeechMessageProducts\\.worktrees\\1.0.0.10.IntegrateWebhook_SDK.worktree\\.worktrees\\1.0.0.11.Integration.P1.Worktree\\SpeechMessage.Dynamics.Plugins.Tests\\bin\\Release\\net48\\SpeechMessage.Dynamics.Plugins.Tests.dll"
      ],
      "counters": {
        "total": "25",
        "executed": "25",
        "passed": "25",
        "failed": "0",
        "error": "0",
        "timeout": "0",
        "aborted": "0",
        "inconclusive": "0",
        "passedButRunAborted": "0",
        "notRunnable": "0",
        "notExecuted": "0",
        "disconnected": "0",
        "warning": "0",
        "completed": "0",
        "inProgress": "0",
        "pending": "0"
      }
    }
  ]
}

## 實際TRX摘要
<ns0:Counters xmlns:ns0="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" total="8" executed="8" passed="8" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
    
ToolUtility.Tests.DataOperations.QueryOperationsSafetyTests.AppointmentByDate_InvalidPage_RejectsEntireCandidate(fault: "null") Passed
ToolUtility.Tests.CoreTargetFrameworkTests.CoreInterfaces_EmitNonObliviousStringContracts(contract: typeof(ToolUtilityNameSpace.LessonsOperations.ILessonsService)) Passed
ToolUtility.Tests.DataOperations.QueryOperationsSafetyTests.AppointmentByDate_InvalidPage_RejectsEntireCandidate(fault: "entity") Passed
ToolUtility.Tests.DataOperations.QueryOperationsSafetyTests.AppointmentByDate_InvalidPage_RejectsEntireCandidate(fault: "duplicate") Passed
ToolUtility.Tests.CoreTargetFrameworkTests.CoreInterfaces_EmitNonObliviousStringContracts(contract: typeof(ToolUtilityNameSpace.AppointmentOperations.IAppointmentService)) Passed
ToolUtility.Tests.DataOperations.QueryOperationsSafetyTests.AppointmentByDate_InvalidPage_RejectsEntireCandidate(fault: "empty") Passed
ToolUtility.Tests.DataOperations.QueryOperationsCharacterizationTests.AppointmentByDate_PreservesLegacyUnfilteredQuery Passed
ToolUtility.Tests.CoreTargetFrameworkTests.CoreInterfaces_EmitNonObliviousStringContracts(contract: typeof(ToolUtilityNameSpace.MeetingStatisticsOperations.IMeetingStatisticsService)) Passed


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.

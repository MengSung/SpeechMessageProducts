/*
 * 檔案整體說明
 *
 * 目的與定位：原Complex十六個入口完整特性；不同於Present/Relationship的字串Guid、root日期欄位、 TotalRecordCount門檻及Execute路徑各自保留，
 * 不能因同名方法而直接轉接成另一套語義。
 *
 * 主要功能與流程：
 * 本檔宣告 ComplexQueryCharacterizationTests。
 * - 十六入口明列，不能只測代表共用查詢而漏掉原本不同的入口；資料均由本測試建立。
 * - 完整投影、原SDK入口與頁metadata先在原host跑綠；動態UTC另驗界線，字串日期固定Invariant後finally還原。
 * - 原血液報告以TotalRecordCount門檻決定null，不能抽取時改成單看Entities.Count。
 * - 只在實際查詢完全相同時共用golden；舊DateTime/root filter/CLR字串差異有獨立檔。
 * 測試案例入口包含 Entrypoints、OriginalHost_PreservesEveryQueryAndResult、Blood_PreservesLegacyTotalCountGate、Golden、Read。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Globalization;
using System.Xml.Linq;
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
    /// 原Complex十六個入口完整特性；不同於Present/Relationship的字串Guid、root日期欄位、
    /// TotalRecordCount門檻及Execute路徑各自保留，不能因同名方法而直接轉接成另一套語義。
    /// </summary>
    public sealed class ComplexQueryCharacterizationTests
    {
        /// <summary>十六入口明列，不能只測代表共用查詢而漏掉原本不同的入口；資料均由本測試建立。</summary>
        public static IEnumerable<object[]> Entrypoints() => Entries.Select(entry => new object[] { entry });

        /// <summary>完整投影、原SDK入口與頁metadata先在原host跑綠；動態UTC另驗界線，字串日期固定Invariant後finally還原。</summary>
        [Theory]
        [MemberData(nameof(Entrypoints))]
        public void OriginalHost_PreservesEveryQueryAndResult(string entry)
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                var before = DateTime.UtcNow;
                var client = new Mock<ICrmQueryExecutor>(MockBehavior.Strict);
                client.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) => Response(query, false));
                client.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
                {
                    var response = new RetrieveMultipleResponse();
                    response.Results["EntityCollection"] = Response(Assert.IsType<RetrieveMultipleRequest>(request).Query, true);
                    return response;
                });
                client.Setup(item => item.Retrieve("list", InputId, It.IsAny<ColumnSet>())).Returns((string _, Guid id, ColumnSet columns) =>
                {
                    Assert.Equal(new[] { "query" }, columns.Columns);
                    return new Entity("list", id) { ["query"] = RepositoryQueryCharacterizationTests.Fetch };
                });
                var actual = Read(new ComplexQueryDataService(new object(), client.Object), entry);
                if (actual is EntityCollection page)
                {
                    Assert.Equal(2, page.Entities.Count);
                    Assert.True(page.MoreRecords); Assert.Equal("synthetic", page.PagingCookie); Assert.Equal(2, page.TotalRecordCount);
                    Assert.All(page.Entities, row => Assert.Equal("same-name", row["new_name"]));
                }
                else Assert.Equal(ContactCharacterizationTests.FirstId, Assert.IsType<Entity>(actual).Id);

                EntityCollection Response(QueryBase input, bool executed)
                {
                    Assert.Equal(entry != "task" && entry != "contact" && entry != "member" && entry != "dynamic", executed);
                    string table;
                    if (input is FetchExpression fetch)
                    {
                        GoldenQueryAssertions.Equal(entry == "fetch" ? "present-fetch.xml" : "complex-dynamic.xml", fetch.Query);
                        table = entry == "fetch" ? "new_present_record" : "contact";
                    }
                    else if (input is QueryByAttribute attributes)
                    {
                        table = attributes.EntityName;
                        Assert.True(attributes.ColumnSet.AllColumns);
                        Assert.Equal(entry == "contact" ? new[] { "new_lineid", "statecode" } : new[] { "listid" }, attributes.Attributes);
                        Assert.Equal(entry == "contact" ? new object[] { "synthetic", 0 } : new object[] { InputId }, attributes.Values);
                    }
                    else
                    {
                        var query = Assert.IsType<QueryExpression>(input);
                        table = query.EntityName;
                        var projection = XElement.Parse(RelationshipQueryCharacterizationTests.Describe(query));
                        if (entry == "sorted" || entry == "date")
                        {
                            var field = entry == "sorted" ? "new_sunday_date" : "new_class_end_date";
                            var value = Assert.IsType<DateTime>(Assert.Single(query.Criteria.Conditions[2].Values));
                            Assert.Equal(DateTimeKind.Utc, value.Kind);
                            var days = entry == "sorted" ? -32 : -7;
                            Assert.InRange(value, before.AddDays(days), DateTime.UtcNow.AddDays(days));
                            projection.Descendants("condition").Single(node => (string?)node.Attribute("field") == field)
                                .Element("value")!.Value = "CURRENT_UTC";
                        }
                        GoldenQueryAssertions.Equal(Golden(entry), projection.ToString());
                    }
                    return new EntityCollection(new[]
                    {
                        new Entity(table, ContactCharacterizationTests.FirstId) { ["new_name"] = "same-name" },
                        new Entity(table, ContactCharacterizationTests.SecondId) { ["new_name"] = "same-name" }
                    }) { MoreRecords = true, PagingCookie = "synthetic", TotalRecordCount = 2 };
                }
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        /// <summary>原血液報告以TotalRecordCount門檻決定null，不能抽取時改成單看Entities.Count。</summary>
        [Theory]
        [InlineData(-1)] [InlineData(0)]
        public void Blood_PreservesLegacyTotalCountGate(int total)
        {
            var client = new Mock<ICrmQueryExecutor>(MockBehavior.Strict);
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = new EntityCollection(new[]
                { new Entity("new_blood_report", ContactCharacterizationTests.FirstId) }) { TotalRecordCount = total };
            client.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns(response);
            Assert.Null(new ComplexQueryDataService(new object(), client.Object).QueryBloodReportByContactId(InputId));
        }

        /// <summary>固定合成ID僅供查詢，不作授權。</summary>
        internal static readonly Guid InputId = Guid.Parse("11111111-1111-4111-8111-111111111111");

        /// <summary>完整原介面十六入口。</summary>
        internal static readonly string[] Entries =
            { "task", "blood", "weeks", "plain", "sorted", "fetch", "in-weekly", "date", "linked-weekly",
                "weekly", "months", "ordered", "list", "contact", "member", "dynamic" };

        /// <summary>只在實際查詢完全相同時共用golden；舊DateTime/root filter/CLR字串差異有獨立檔。</summary>
        private static string Golden(string entry) => entry switch
        {
            "plain" => "relationship-plain.xml", "ordered" => "relationship-ordered.xml",
            "linked-weekly" => "relationship-weekly.xml",
            "sorted" or "in-weekly" or "weekly" or "months" or "list" => "present-" + entry + ".xml",
            _ => "complex-" + entry + ".xml"
        };

        /// <summary>呼叫真實舊入口；固定父名稱與子名稱隨golden差異選取，不在helper重建查詢。</summary>
        internal static object? Read(IComplexQueryService service, string entry, Guid? id = null) => entry switch
        {
            "task" => service.RetrieveManyToOneCollection(),
            "blood" => service.QueryBloodReportByContactId(id ?? InputId),
            "weeks" => service.QueryPresentRecordByContactIdAndSunday(id ?? InputId, ContactCharacterizationTests.SecondId, 3),
            "plain" => service.RetrieveManyToOneRelationship("contact", "contactid", (id ?? InputId).ToString("D"), "new_contact_family_leader_list", "list"),
            "sorted" => service.QueryPresentRecordSortBySunday("new_disciple_lessons", "new_disciple_lessonsid", (id ?? InputId).ToString("D"), "new_parent", "new_present_record"),
            "fetch" => service.QueryPresentRecordSortBySundayFetchXml(3, "Synthetic", (id ?? InputId).ToString("D")),
            "in-weekly" => service.QueryPresentRecordInWeeklyReportByContactId(id ?? InputId, ContactCharacterizationTests.SecondId),
            "date" => service.QueryEntityListByDate("new_disciple_lessons", "new_disciple_lessonsid", (id ?? InputId).ToString("D"), "new_parent", "new_present_record"),
            "linked-weekly" => service.QueryWeeklyReportBySunday(new DateTime(2027, 3, 14), "contact", "contactid", (id ?? InputId).ToString("D"), "new_contact_family_leader_list", "list"),
            "weekly" => service.QueryWeeklyReportBySunday(new DateTime(2027, 3, 14), id ?? InputId),
            "months" => service.QueryWeeklyReportBeforeTwoMonthsOfSunday(new DateTime(2027, 3, 14), id ?? InputId),
            "ordered" => service.QueryListsAndOrderedByListName("contact", "contactid", (id ?? InputId).ToString("D"), "new_contact_family_leader_list", "list"),
            "list" => service.QueryListByContactId(id ?? InputId, "new_parent"),
            "contact" => service.RetrieveContactCollectionByLineId("synthetic"),
            "member" => service.RetrieveMemberListCollectionByListIdDynamics365(id ?? InputId),
            "dynamic" => service.RetrieveDynamicMemberListDynamics365(id ?? InputId),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
    }
}

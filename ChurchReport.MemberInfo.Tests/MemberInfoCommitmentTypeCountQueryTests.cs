/*
 * 檔案整體說明
 *
 * 目的與定位：驗證委身類型aggregate查詢保留原篩選、正確讀取SDK別名與空值，並按選項值彙總計數。
 *
 * 主要功能與流程：
 * 本檔宣告 MemberInfoCommitmentTypeCountQueryTests。
 * 測試案例入口包含 CreateValueCountsFetch_PreservesFiltersAndAddsGroupedAggregate、
 * ReadValueCounts_HandlesOptionSetAndIntegerAliases、ReadValueCounts_SkipsMissingAliasesAndSumsDuplicateValues、
 * ReadValueCounts_NullRowsReturnsEmptyDictionary、CreateValueCountsFetch_BlankXmlThrowsArgumentException、
 * CreateValueCountsFetch_MalformedXmlThrowsXmlException、AggregateRow。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoCommitmentTypeCountQueryTests
{
    [Fact]
    public void CreateValueCountsFetch_PreservesFiltersAndAddsGroupedAggregate()
    {
        const string fetch =
            "<fetch mapping='logical' page='2' count='50' returntotalrecordcount='true'>" +
            "<entity name='contact'>" +
            "<attribute name='contactid'/>" +
            "<attribute name='fullname'/>" +
            "<order attribute='fullname'/>" +
            "<filter><condition attribute='statecode' operator='eq' value='0'/></filter>" +
            "</entity></fetch>";

        var result = MemberInfoCommitmentTypeCountQuery.CreateValueCountsFetch(fetch);
        var document = XDocument.Parse(result);

        document.Root.Should().NotBeNull();
        document.Root!.Attribute("aggregate")?.Value.Should().Be("true");
        document.Root.Attribute("page").Should().BeNull();
        document.Root.Attribute("count").Should().BeNull();
        document.Root.Attribute("returntotalrecordcount").Should().BeNull();
        document.Descendants("condition").Should().ContainSingle();
        document.Descendants("order").Should().BeEmpty();
        document.Descendants("attribute")
            .Single(node => node.Attribute("alias")?.Value == "commitmenttype")
            .Attribute("groupby")?.Value.Should().Be("true");
        document.Descendants("attribute")
            .Single(node => node.Attribute("alias")?.Value == "rowcount")
            .Attribute("aggregate")?.Value.Should().Be("countcolumn");
    }

    [Fact]
    public void ReadValueCounts_HandlesOptionSetAndIntegerAliases()
    {
        var rows = new EntityCollection();
        rows.Entities.Add(AggregateRow(new OptionSetValue(100000006), 7));
        rows.Entities.Add(AggregateRow(1, 11));

        var result = MemberInfoCommitmentTypeCountQuery.ReadValueCounts(rows);

        result.Should().BeEquivalentTo(new Dictionary<int, int>
        {
            [100000006] = 7,
            [1] = 11
        });
    }

    [Fact]
    public void ReadValueCounts_SkipsMissingAliasesAndSumsDuplicateValues()
    {
        var rows = new EntityCollection();
        rows.Entities.Add(AggregateRow(new OptionSetValue(1), 2L));
        rows.Entities.Add(AggregateRow(1, 3m));
        rows.Entities.Add(new Entity("contact"));

        var result = MemberInfoCommitmentTypeCountQuery.ReadValueCounts(rows);

        result.Should().ContainSingle();
        result[1].Should().Be(5);
    }

    [Fact]
    public void ReadValueCounts_NullRowsReturnsEmptyDictionary()
    {
        MemberInfoCommitmentTypeCountQuery.ReadValueCounts(null).Should().BeEmpty();
    }

    [Fact]
    public void CreateValueCountsFetch_BlankXmlThrowsArgumentException()
    {
        var act = () => MemberInfoCommitmentTypeCountQuery.CreateValueCountsFetch(" ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateValueCountsFetch_MalformedXmlThrowsXmlException()
    {
        var act = () => MemberInfoCommitmentTypeCountQuery.CreateValueCountsFetch("<fetch>");

        act.Should().Throw<XmlException>();
    }

    private static Entity AggregateRow(object value, object count)
    {
        var row = new Entity("contact");
        row["commitmenttype"] = new AliasedValue("contact", "customertypecode", value);
        row["rowcount"] = new AliasedValue("contact", "contactid", count);
        return row;
    }
}

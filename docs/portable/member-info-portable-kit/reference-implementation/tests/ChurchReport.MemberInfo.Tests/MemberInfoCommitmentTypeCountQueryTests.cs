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
        MemberInfoCommitmentTypeCountQuery.ReadValueCounts(null)
            .Should().BeEmpty();
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
        row["commitmenttype"] =
            new AliasedValue("contact", "customertypecode", value);
        row["rowcount"] =
            new AliasedValue("contact", "contactid", count);
        return row;
    }
}

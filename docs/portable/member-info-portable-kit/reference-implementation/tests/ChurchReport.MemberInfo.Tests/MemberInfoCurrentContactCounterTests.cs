using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class MemberInfoCurrentContactCounterTests
{
    [Fact]
    public void Count_UsesOneAggregateFetchAndReturnsTheServerCount()
    {
        var result = new EntityCollection();
        var aggregateRow = new Entity("contact");
        aggregateRow["currentcontactcount"] = new AliasedValue("contact", "contactid", 321);
        result.Entities.Add(aggregateRow);
        var service = new RecordingOrganizationService(result);

        var count = MemberInfoCurrentContactCounter.Count(service, 7);

        count.Should().Be(321);
        service.RetrieveMultipleCalls.Should().Be(1);
        var fetch = service.LastQuery.Should().BeOfType<FetchExpression>().Subject.Query;
        fetch.Should().Contain("aggregate=\"true\"");
        fetch.Should().Contain("aggregate=\"countcolumn\"");
        fetch.Should().Contain("attribute=\"statecode\" operator=\"eq\" value=\"0\"");
        fetch.Should().Contain("attribute=\"customertypecode\" operator=\"ne\" value=\"7\"");
    }

    private sealed class RecordingOrganizationService : IOrganizationService
    {
        private readonly EntityCollection result;

        public RecordingOrganizationService(EntityCollection result)
        {
            this.result = result;
        }

        public QueryBase? LastQuery { get; private set; }
        public int RetrieveMultipleCalls { get; private set; }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            LastQuery = query;
            RetrieveMultipleCalls++;
            return result;
        }

        public Guid Create(Entity entity) => throw new NotSupportedException();
        public void Update(Entity entity) => throw new NotSupportedException();
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

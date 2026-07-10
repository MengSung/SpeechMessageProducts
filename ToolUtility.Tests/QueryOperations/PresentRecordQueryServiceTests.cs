using System;
using System.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.QueryOperations;
using Xunit;

namespace ToolUtility.Tests.QueryOperations;

public sealed class PresentRecordQueryServiceTests
{
    [Fact]
    public void QueryListByContactId_UsesNarrowColumnsAndPaging()
    {
        QueryExpression capturedQuery = null!;
        var crm = new Mock<IOrganizationService>();
        crm.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
            .Callback<QueryBase>(query => capturedQuery = query.Should().BeOfType<QueryExpression>().Subject)
            .Returns(new EntityCollection());
        var service = new PresentRecordQueryService(MockLoggerFactory.CreateMock<object>().Object, crm.Object);

        service.QueryListByContactId(Guid.NewGuid(), "new_contact_family_leader_list");

        capturedQuery.Should().NotBeNull();
        capturedQuery.EntityName.Should().Be("list");
        capturedQuery.ColumnSet.AllColumns.Should().BeFalse();
        capturedQuery.ColumnSet.Columns.Should().Contain(new[]
        {
            "listid",
            "listname",
            "new_app_named",
            "new_contact_family_leader_list",
            "new_contact_race_leager_list",
            "new_contact_list_arealeader"
        });
        capturedQuery.PageInfo.Should().NotBeNull();
        capturedQuery.PageInfo.PageNumber.Should().Be(1);
        capturedQuery.PageInfo.Count.Should().Be(5000);
    }

    [Fact]
    public void QueryListByContactId_FiltersByRequestedAssociationAndActiveAppNamedLists()
    {
        QueryExpression capturedQuery = null!;
        var contactId = Guid.NewGuid();
        var crm = new Mock<IOrganizationService>();
        crm.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>()))
            .Callback<QueryBase>(query => capturedQuery = query.Should().BeOfType<QueryExpression>().Subject)
            .Returns(new EntityCollection());
        var service = new PresentRecordQueryService(MockLoggerFactory.CreateMock<object>().Object, crm.Object);

        service.QueryListByContactId(contactId, "new_contact_list_arealeader");

        var conditions = capturedQuery.Criteria.Conditions;
        conditions.Should().Contain(condition =>
            condition.AttributeName == "new_contact_list_arealeader" &&
            condition.Operator == ConditionOperator.Equal &&
            condition.Values.Cast<object>().Single().Equals(contactId));
        conditions.Should().Contain(condition =>
            condition.AttributeName == "statecode" &&
            condition.Operator == ConditionOperator.Equal &&
            condition.Values.Cast<object>().Single().Equals(0));
        conditions.Should().Contain(condition =>
            condition.AttributeName == "new_app_named" &&
            condition.Operator == ConditionOperator.Equal &&
            condition.Values.Cast<object>().Single().Equals(true));
    }
}

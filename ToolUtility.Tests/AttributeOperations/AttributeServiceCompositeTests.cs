using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtility.Tests.TestHelpers;
using Microsoft.Xrm.Sdk;

namespace ToolUtility.Tests.AttributeOperations
{
    public class AttributeServiceCompositeTests
    {
        [Fact]
        public void GetBoolAttribute_ShouldDelegateToBoolService()
        {
            var mockLogger = MockLoggerFactory.CreateNonGenericMock();
            var composite = new AttributeServiceComposite(mockLogger.Object);

            var entity = new Entity("contact");
            entity["new_ismember"] = true;

            var result = composite.GetBoolAttribute(entity, "new_ismember");

            result.Should().BeTrue();
        }

        [Fact]
        public void GetAllAttributeTypes_ShouldWork()
        {
            var mockLogger = MockLoggerFactory.CreateNonGenericMock();
            var composite = new AttributeServiceComposite(mockLogger.Object);

            var entity = new Entity("contact");
            entity["new_ismember"] = true;
            entity["new_count"] = 5;
            entity["new_note"] = "abc";
            entity["new_date"] = new System.DateTime(2020,1,1);
            entity["new_amount"] = new Money(12.34m);
            entity["parentcustomerid"] = new EntityReference("account", System.Guid.NewGuid());

            composite.GetBoolAttribute(entity, "new_ismember").Should().BeTrue();
            composite.GetIntAttribute(entity, "new_count").Should().Be(5);
            composite.GetStringAttribute(entity, "new_note").Should().Be("abc");
            composite.GetDateTimeAttribute(entity, "new_date").Should().Be(new System.DateTime(2020,1,1));
            composite.GetMoneyAttribute(entity, "new_amount").Value.Should().Be(12.34m);
            composite.GetLookupAttribute(entity, "parentcustomerid").Should().NotBe(System.Guid.Empty);
        }
    }
}

using Xunit;
using FluentAssertions;
using Moq;
using System;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Core;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.Interfaces;

namespace ToolUtility.Tests.Core
{
    public class ToolUtilityClassIntegrationTests
    {
        [Fact]
        public void RetrieveContactByLineId_ShouldDelegateToContactService()
        {
            // Arrange
            var expected = TestEntityFactory.CreateContact("U123", "ด๚ธี");
            var collection = new EntityCollection(new[] { expected });

            var mockCrm = MockCrmClientFactory.CreateMockWithCollection(collection);
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);

            // Act
            var result = facade.RetrieveContactByLineId("U123");

            // Assert
            result.Should().NotBeNull();
            result["new_lineid"].Should().Be("U123");
        }

        [Fact]
        public void SetEntityBoolAttribute_ShouldDelegateToAttributeService()
        {
            // Arrange
            var mockCrm = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);

            var entity = new Entity("contact");

            // Act
            facade.SetEntityBoolAttribute(ref entity, "new_testflag", true);

            // Assert
            facade.GetEntityBoolAttribute(entity, "new_testflag").Should().BeTrue();
        }
    }
}

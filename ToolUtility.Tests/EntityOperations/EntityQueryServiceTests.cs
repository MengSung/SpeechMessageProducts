using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.Interfaces;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using System;

namespace ToolUtility.Tests.EntityOperations
{
    public class EntityQueryServiceTests
    {
        [Fact]
        public void RetrieveEntity_WhenEntityExists_ShouldReturnEntity()
        {
            var expected = TestEntityFactory.CreateContact("U123", "ด๚ธี");
            var mockClient = MockCrmClientFactory.CreateMockWithEntity(expected);

            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var service = new EntityQueryService(mockLogger.Object, mockClient.Object);

            var result = service.RetrieveEntity("contact", expected.Id);

            result.Should().NotBeNull();
            result.Id.Should().Be(expected.Id);
        }

        [Fact]
        public void RetrieveMultiple_WhenQueryValid_ShouldReturnCollection()
        {
            var collection = new EntityCollection(new[]
            {
                TestEntityFactory.CreateContact("U123", "ด๚ธี1"),
                TestEntityFactory.CreateContact("U456", "ด๚ธี2")
            });

            var mockClient = MockCrmClientFactory.CreateMockWithCollection(collection);
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var service = new EntityQueryService(mockLogger.Object, mockClient.Object);

            var query = new QueryByAttribute("contact");

            var result = service.RetrieveMultiple(query);

            result.Entities.Count.Should().Be(2);
        }
    }
}

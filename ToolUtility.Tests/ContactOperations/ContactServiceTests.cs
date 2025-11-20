using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.ContactOperations;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.EntityOperations;
using Moq;
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtility.Tests.ContactOperations
{
    public class ContactServiceTests
    {
        [Fact]
        public void RetrieveByLineId_WhenContactExists_ShouldReturnEntity()
        {
            var expected = TestEntityFactory.CreateContact("U123456", "´ú¸ÕÁpµ¸¤H");

            var mockQueryService = new Mock<IEntityQueryService>();
            mockQueryService.Setup(x => x.RetrieveMultiple(It.IsAny<QueryByAttribute>()))
                .Returns(new EntityCollection(new[] { expected }));

            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var service = new ContactService(mockLogger.Object, mockQueryService.Object);

            var result = service.RetrieveByLineId("U123456");

            result.Should().NotBeNull();
            result["new_lineid"].Should().Be("U123456");
        }

        [Fact]
        public void RetrieveCollectionByName_ShouldReturnCollection()
        {
            var collection = new EntityCollection(new[]
            {
                TestEntityFactory.CreateContact("U123", "A"),
                TestEntityFactory.CreateContact("U456", "B")
            });

            var mockQueryService = new Mock<IEntityQueryService>();
            mockQueryService.Setup(x => x.RetrieveMultiple(It.IsAny<QueryByAttribute>()))
                .Returns(collection);

            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var service = new ContactService(mockLogger.Object, mockQueryService.Object);

            var result = service.RetrieveCollectionByName("A");

            result.Entities.Count.Should().Be(2);
        }
    }
}

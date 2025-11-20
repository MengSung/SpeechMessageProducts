using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.Interfaces;
using Microsoft.Xrm.Sdk;
using Moq;
using System;

namespace ToolUtility.Tests.EntityOperations
{
    public class EntityCrudServiceTests
    {
        [Fact]
        public void CreateEntity_ShouldReturnGuid()
        {
            var entity = TestEntityFactory.CreateEmpty("contact");
            var mockClient = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new EntityCrudService(mockLogger.Object, mockClient.Object);

            var id = service.CreateEntity(entity);

            id.Should().NotBe(Guid.Empty);
        }

        [Fact]
        public void UpdateEntity_ShouldCallClient()
        {
            var entity = TestEntityFactory.CreateEmpty("contact");
            entity["fullname"] = "new name";

            var mockClient = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new EntityCrudService(mockLogger.Object, mockClient.Object);

            service.UpdateEntity(entity);

            mockClient.Verify(x => x.Update(It.Is<Entity>(e => e == entity)), Times.Once);
        }

        [Fact]
        public void DeleteEntity_ShouldCallClient()
        {
            var id = Guid.NewGuid();
            var mockClient = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new EntityCrudService(mockLogger.Object, mockClient.Object);

            service.DeleteEntity("contact", id);

            mockClient.Verify(x => x.Delete("contact", id), Times.Once);
        }
    }
}

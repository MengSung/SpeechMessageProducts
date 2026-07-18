using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;

namespace ToolUtility.Tests.TestHelpers
{
    public static class MockOrganizationServiceFactory
    {
        public static Mock<IOrganizationService> CreateMock()
        {
            var mock = new Mock<IOrganizationService>();

            mock.Setup(x => x.Retrieve(
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<ColumnSet>()))
                .Returns((Entity)null!);

            mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns(new EntityCollection());

            mock.Setup(x => x.Create(It.IsAny<Entity>()))
                .Returns(Guid.NewGuid());

            mock.Setup(x => x.Update(It.IsAny<Entity>()));
            mock.Setup(x => x.Delete(It.IsAny<string>(), It.IsAny<Guid>()));
            mock.Setup(x => x.Execute(It.IsAny<OrganizationRequest>()))
                .Returns(new OrganizationResponse());

            return mock;
        }

        public static Mock<IOrganizationService> CreateMockWithEntity(Entity entity)
        {
            var mock = CreateMock();

            mock.Setup(x => x.Retrieve(
                entity.LogicalName,
                entity.Id,
                It.IsAny<ColumnSet>()))
                .Returns(entity);

            return mock;
        }

        public static Mock<IOrganizationService> CreateMockWithCollection(EntityCollection collection)
        {
            var mock = CreateMock();

            mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryBase>()))
                .Returns(collection);

            return mock;
        }
    }
}

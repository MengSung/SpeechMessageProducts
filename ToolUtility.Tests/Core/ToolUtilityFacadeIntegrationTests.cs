using Xunit;
using FluentAssertions;
using Moq;
using System;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Core;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.Interfaces;
using ToolUtilityNameSpace.ListOperations;
using ToolUtilityNameSpace.LineMessaging;

namespace ToolUtility.Tests.Core
{
    public class ToolUtilityFacadeIntegrationTests
    {
        [Fact]
        public void Create_Update_Delete_Entity_Via_Facade()
        {
            var mockCrm = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var createdId = Guid.NewGuid();
            mockCrm.Setup(x => x.Create(It.IsAny<Entity>())).Returns(createdId);

            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);

            var entity = new Entity("account") { ["name"] = "TDD Test" };

            // Create
            var id = facade.CreateEntity(entity);
            id.Should().Be(createdId);

            // Update
            entity.Id = id;
            facade.UpdateEntity(entity);
            mockCrm.Verify(x => x.Update(It.Is<Entity>(e => e.Id == id)), Times.Once);

            // Delete
            facade.DeleteEntity("account", id);
            mockCrm.Verify(x => x.Delete("account", id), Times.Once);
        }

        [Fact]
        public void UploadAttachment_ShouldCallCreateAnnotation()
        {
            var mockCrm = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);

            var crmService = (IOrganizationService)null;
            facade.UploadAnAttachment(ref crmService, "contact", "sub", "note", "file.txt", "text/plain", new byte[] { 1,2,3 }, Guid.NewGuid());

            mockCrm.Verify(x => x.Create(It.Is<Entity>(a => a.LogicalName == "annotation" && a["filename"].ToString() == "file.txt")), Times.Once);
        }

        [Fact]
        public void AddAndRemoveMembersToMarketingList_ShouldCallListService()
        {
            var mockCrm = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);

            var listId = Guid.NewGuid();
            var members = new System.Collections.Generic.List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

            facade.AddMembersToMarketingList(listId, members);

            // Verify create called for each member (ListService calls ICrmClient.Create)
            mockCrm.Verify(x => x.Create(It.Is<Entity>(e => e.LogicalName == "listmember")), Times.Exactly(members.Count));

            var memberToRemove = members[0];
            facade.RemoveMembersToMarketingList(listId, memberToRemove);

            // Removal in our simple impl calls Delete on list entity - verify Delete called
            mockCrm.Verify(x => x.Delete("list", It.IsAny<Guid>()), Times.AtLeastOnce);
        }

        [Fact]
        public void CreatePushLineMessage_ShouldCallCrudCreate()
        {
            var mockCrm = MockCrmClientFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var facade = new ToolUtilityFacade(mockLogger.Object, mockCrm.Object);

            facade.CreatePushLineMessage("U123", "sub", "hello");

            // LineMessageService creates an entity via IEntityCrudService which uses ICrmClient.Create
            mockCrm.Verify(x => x.Create(It.Is<Entity>(e => e.LogicalName == "linemessage" && e["userid"].ToString() == "U123")), Times.Once);
        }
    }
}

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/Core/ToolUtilityFacadeIntegrationTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class ToolUtilityFacadeIntegrationTests
// 主要成員：Create_Update_Delete_Entity_Via_Facade、UploadAttachment_ShouldCallCreateAnnotation、AddAndRemoveMembersToMarketingList_ShouldCallListService、CreatePushLineMessage_ShouldCallCrudCreate
// 引用命名空間：Xunit、FluentAssertions、Moq、System、Microsoft.Xrm.Sdk、ToolUtilityNameSpace.Core、ToolUtility.Tests.TestHelpers、ToolUtilityNameSpace.Interfaces
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

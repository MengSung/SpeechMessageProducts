/*
 * 檔案整體說明
 *
 * 目的與定位：以假SDK驗證Facade的實體讀寫、附件、名單成員及LINE待送記錄委派，保護薄外觀整合邊界。
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityFacadeIntegrationTests。
 * 測試案例入口包含 Create_Update_Delete_Entity_Via_Facade、UploadAttachment_ShouldCallCreateAnnotation、
 * AddAndRemoveMembersToMarketingList_ShouldCallListService、CreatePushLineMessage_ShouldCallCrudCreate。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Xunit;
using Microsoft.Crm.Sdk.Messages;
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
            var mockCrm = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var createdId = Guid.NewGuid();
            mockCrm.Setup(x => x.Create(It.IsAny<Entity>())).Returns(createdId);

            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);

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
            var mockCrm = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);

            // 同理：ref 參數必須是可用連線。
            var crmService = mockCrm.Object;
            facade.UploadAnAttachment(ref crmService, "contact", "sub", "note", "file.txt", "text/plain", new byte[] { 1,2,3 }, Guid.NewGuid());

            mockCrm.Verify(x => x.Create(It.Is<Entity>(a => a.LogicalName == "annotation" && a["filename"].ToString() == "file.txt")), Times.Once);
        }

        [Fact]
        public void AddAndRemoveMembersToMarketingList_ShouldCallListService()
        {
            var mockCrm = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);

            var listId = Guid.NewGuid();
            var members = new System.Collections.Generic.List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

            facade.AddMembersToMarketingList(listId, members);

            // 保護的契約：AddMembers 以單一 AddListMembersListRequest 批次新增全部成員，
            // 而非逐筆 Create listmember。一次 SDK 呼叫是刻意的效能設計，不可退回逐筆。
            mockCrm.Verify(x => x.Execute(It.IsAny<AddListMembersListRequest>()), Times.Once);

            var memberToRemove = members[0];
            facade.RemoveMembersToMarketingList(listId, memberToRemove);

            // 保護的契約：RemoveMember 以 RemoveMemberListRequest 單次完成，
            // 不先查詢再 Delete，避免多餘的往返。
            mockCrm.Verify(x => x.Execute(It.IsAny<RemoveMemberListRequest>()), Times.Once);
        }

        [Fact]
        public void CreatePushLineMessage_ShouldCallCrudCreate()
        {
            var mockCrm = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);

            facade.CreatePushLineMessage("U123", "sub", "hello");

            // LineMessageService creates an entity via IEntityCrudService which uses ICrmClient.Create
            mockCrm.Verify(x => x.Create(It.Is<Entity>(e => e.LogicalName == "linemessage" && e["userid"].ToString() == "U123")), Times.Once);
        }
    }
}

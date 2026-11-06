/*
 * 檔案整體說明
 *
 * 目的與定位：保護EntityCrudService的既有行為與相容性。受測元件的責任為：純 SDK 單次寫入轉接器；只借用 host 已驗證組織、使用者及授權範圍的服務，不建立連線或共用資料。 輸入與輸出只在同步呼叫存活，
 * 不保存 Entity、logger、Session、token 或 callback。
 *
 * 主要功能與流程：
 * 本檔宣告 EntityCrudServiceTests。
 * 測試案例入口包含 CreateEntity_ShouldReturnGuid、UpdateEntity_ShouldCallClient、DeleteEntity_ShouldCallClient。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtility.Tests.TestHelpers;
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
            var mockClient = MockOrganizationServiceFactory.CreateMock();
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

            var mockClient = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new EntityCrudService(mockLogger.Object, mockClient.Object);

            service.UpdateEntity(entity);

            mockClient.Verify(x => x.Update(It.Is<Entity>(e => e == entity)), Times.Once);
        }

        [Fact]
        public void DeleteEntity_ShouldCallClient()
        {
            var id = Guid.NewGuid();
            var mockClient = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new EntityCrudService(mockLogger.Object, mockClient.Object);

            service.DeleteEntity("contact", id);

            mockClient.Verify(x => x.Delete("contact", id), Times.Once);
        }
    }
}

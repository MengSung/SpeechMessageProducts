/*
 * 檔案整體說明
 *
 * 目的與定位：驗證舊ToolUtilityClass把聯絡人查詢及屬性寫入交給對應服務，避免外觀入口另建資料操作實作。
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityClassIntegrationTests。
 * 測試案例入口包含 RetrieveContactByLineId_ShouldDelegateToContactService、
 * SetEntityBoolAttribute_ShouldDelegateToAttributeService。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Xunit;
using FluentAssertions;
using Moq;
using System;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.Core;
using ToolUtility.Tests.TestHelpers;

namespace ToolUtility.Tests.Core
{
    public class ToolUtilityClassIntegrationTests
    {
        [Fact]
        public void RetrieveContactByLineId_ShouldDelegateToContactService()
        {
            // Arrange
            var expected = TestEntityFactory.CreateContact("U123", "測試");
            var collection = new EntityCollection(new[] { expected });

            var mockCrm = MockOrganizationServiceFactory.CreateMockWithCollection(collection);
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);

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
            var mockCrm = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var facade = new ToolUtilityFacade(mockCrm.Object, mockLogger.Object);

            var entity = new Entity("contact");

            // Act
            facade.SetEntityBoolAttribute(ref entity, "new_testflag", true);

            // Assert
            facade.GetEntityBoolAttribute(entity, "new_testflag").Should().BeTrue();
        }
    }
}

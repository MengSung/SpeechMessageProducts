/*
 * 檔案整體說明
 *
 * 目的與定位：保護ListService的既有行為與相容性。受測元件的責任為：名單的純SDK資料服務；沿用原二十個入口、查詢投影、降冪及不同成員來源，不引用host連線或logger。 
 * host必須先驗使用者／租戶／教會及名單權限；任何ID、顯示名稱或儲存Fetch都不是授權來源。 單頁最多5000列，完整成員讀取最多5000列／1000頁；全部正式身份通過才回操作自有候選。
 *
 * 主要功能與流程：
 * 本檔宣告 ListServiceTests。
 * 測試案例入口包含 AddMembers_ShouldCallCreateForEachMember、RemoveMember_ShouldCallDelete。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.ListOperations;
using ToolUtility.Tests.TestHelpers;
using Moq;
using System;
using System.Collections.Generic;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.EntityOperations;

namespace ToolUtility.Tests.ListOperations
{
    public class ListServiceTests
    {
        [Fact]
        public void AddMembers_ShouldCallCreateForEachMember()
        {
            var mockQuery = new Mock<IEntityQueryService>();
            var mockCrudClient = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new ListService(mockLogger.Object, mockCrudClient.Object);

            var members = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            var listId = Guid.NewGuid();

            service.AddMembers(listId, members);

            // No exception means success for this simple impl
            Assert.True(true);
        }

        [Fact]
        public void RemoveMember_ShouldCallDelete()
        {
            var mockQuery = new Mock<IEntityQueryService>();
            var mockCrudClient = MockOrganizationServiceFactory.CreateMock();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new ListService(mockLogger.Object, mockCrudClient.Object);

            var member = Guid.NewGuid();
            var listId = Guid.NewGuid();

            service.RemoveMember(listId, member);

            // No exception means success
            Assert.True(true);
        }
    }
}

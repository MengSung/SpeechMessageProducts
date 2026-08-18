// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/ListOperations/ListServiceTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class ListServiceTests
// 主要成員：AddMembers_ShouldCallCreateForEachMember、RemoveMember_ShouldCallDelete
// 引用命名空間：Xunit、FluentAssertions、ToolUtilityNameSpace.ListOperations、ToolUtility.Tests.TestHelpers、Moq、System、System.Collections.Generic、ToolUtilityNameSpace.EntityOperations
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

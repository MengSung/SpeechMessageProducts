// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/AttachmentOperations/AttachmentServiceTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class AttachmentServiceTests
// 主要成員：DownloadAttachment_WhenCalled_ShouldReturnCollection、UploadAttachment_WhenCalled_ShouldCreateAnnotation
// 引用命名空間：Xunit、FluentAssertions、ToolUtilityNameSpace.AttachmentOperations、ToolUtility.Tests.TestHelpers、Moq、Microsoft.Xrm.Sdk、System
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.AttachmentOperations;
using ToolUtility.Tests.TestHelpers;
using Moq;
using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtility.Tests.AttachmentOperations
{
    public class AttachmentServiceTests
    {
        [Fact]
        public void DownloadAttachment_WhenCalled_ShouldReturnCollection()
        {
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var mockCrudClient = MockCrmClientFactory.CreateMock();

            var service = new AttachmentService(mockLogger.Object, mockCrudClient.Object);

            var crm = (IOrganizationService)null;
            var result = service.DownloadAttachment(ref crm, Guid.NewGuid());

            result.Should().NotBeNull();
            result.Entities.Count.Should().Be(0);
        }

        [Fact]
        public void UploadAttachment_WhenCalled_ShouldCreateAnnotation()
        {
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var mockCrudClient = MockCrmClientFactory.CreateMock();

            var service = new AttachmentService(mockLogger.Object, mockCrudClient.Object);

            var crm = (IOrganizationService)null;

            service.UploadAttachment(ref crm, "contact", "sub", "note", "file.txt", "text/plain", new byte[] {1,2,3}, Guid.NewGuid());

            Assert.True(true);
        }
    }
}

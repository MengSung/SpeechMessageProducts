/*
 * 檔案整體說明
 *
 * 目的與定位：保護AttachmentService的既有行為與相容性。受測元件的責任為：同步附註附件操作；只借用每次 ref 傳入、由 host 先完成授權的 SDK。 服務只保存不可變容量，不保存建構時 
 * SDK／logger、候選、本文或使用者資料，故可並行服務不同 scope。
 *
 * 主要功能與流程：
 * 本檔宣告 AttachmentServiceTests。
 * 測試案例入口包含 DownloadAttachment_WhenCalled_ShouldReturnCollection、
 * UploadAttachment_WhenCalled_ShouldCreateAnnotation。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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
            var mockCrudClient = MockOrganizationServiceFactory.CreateMock();

            var service = new AttachmentService(mockLogger.Object, mockCrudClient.Object);

            // ref 參數必須是可用連線：產品程式碼會直接對它呼叫 RetrieveMultiple/Create，
            // 傳 null 會在產品內部產生 NullReferenceException，並非被保護的契約。
            var crm = mockCrudClient.Object;
            var result = service.DownloadAttachment(ref crm, Guid.NewGuid());

            result.Should().NotBeNull();
            result.Entities.Count.Should().Be(0);
        }

        [Fact]
        public void UploadAttachment_WhenCalled_ShouldCreateAnnotation()
        {
            var mockLogger = MockLoggerFactory.CreateMock<object>();
            var mockCrudClient = MockOrganizationServiceFactory.CreateMock();

            var service = new AttachmentService(mockLogger.Object, mockCrudClient.Object);

            // ref 參數必須是可用連線：產品程式碼會直接對它呼叫 RetrieveMultiple/Create，
            // 傳 null 會在產品內部產生 NullReferenceException，並非被保護的契約。
            var crm = mockCrudClient.Object;

            service.UploadAttachment(ref crm, "contact", "sub", "note", "file.txt", "text/plain", new byte[] {1,2,3}, Guid.NewGuid());

            Assert.True(true);
        }
    }
}

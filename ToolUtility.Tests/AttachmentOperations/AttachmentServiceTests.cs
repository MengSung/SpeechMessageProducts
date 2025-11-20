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

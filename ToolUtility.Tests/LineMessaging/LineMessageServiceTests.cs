using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.LineMessaging;
using ToolUtility.Tests.TestHelpers;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using System;
using Microsoft.Xrm.Sdk;

namespace ToolUtility.Tests.LineMessaging
{
    public class LineMessageServiceTests
    {
        [Fact]
        public void CreatePushMessage_ShouldCallCreateEntity()
        {
            var mockCrud = new Mock<IEntityCrudService>();
            var mockLogger = MockLoggerFactory.CreateMock<object>();

            var service = new LineMessageService(mockLogger.Object, mockCrud.Object);

            service.CreatePushMessage("U123", "sub", "hello");

            mockCrud.Verify(x => x.CreateEntity(It.IsAny<Entity>()), Times.Once);
        }
    }
}

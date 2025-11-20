using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtility.Tests.TestHelpers;
using Microsoft.Xrm.Sdk;
using Moq;

namespace ToolUtility.Tests.AttributeOperations
{
    public class BoolAttributeServiceTests
    {
        private readonly Mock<ILogger> _mockLogger;
        private readonly BoolAttributeService _service;

        public BoolAttributeServiceTests()
        {
            _mockLogger = MockLoggerFactory.CreateNonGenericMock();
            _service = new BoolAttributeService(_mockLogger.Object);
        }

        [Fact]
        public void GetAttribute_WhenAttributeExists_ShouldReturnValue()
        {
            var entity = new Entity("contact");
            entity["new_ismember"] = true;

            var result = _service.GetAttribute(entity, "new_ismember");

            result.Should().BeTrue();
        }

        [Fact]
        public void GetAttribute_WhenAttributeNotExists_ShouldReturnFalse()
        {
            var entity = new Entity("contact");

            var result = _service.GetAttribute(entity, "new_ismember");

            result.Should().BeFalse();
        }

        [Fact]
        public void SetAttribute_WhenAttributeExists_ShouldUpdateValue()
        {
            var entity = new Entity("contact");
            entity["new_ismember"] = false;

            _service.SetAttribute(ref entity, "new_ismember", true);

            entity["new_ismember"].Should().Be(true);
        }

        [Fact]
        public void SetAttribute_WhenAttributeNotExists_ShouldAddValue()
        {
            var entity = new Entity("contact");

            _service.SetAttribute(ref entity, "new_ismember", true);

            entity.Contains("new_ismember").Should().BeTrue();
            entity["new_ismember"].Should().Be(true);
        }
    }
}

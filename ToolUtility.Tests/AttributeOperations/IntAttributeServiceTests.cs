using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtility.Tests.TestHelpers;
using Microsoft.Xrm.Sdk;

namespace ToolUtility.Tests.AttributeOperations
{
    public class IntAttributeServiceTests
    {
        private readonly ToolUtilityNameSpace.AttributeOperations.IntAttributeService _service;

        public IntAttributeServiceTests()
        {
            var mockLogger = MockLoggerFactory.CreateNonGenericMock();
            _service = new ToolUtilityNameSpace.AttributeOperations.IntAttributeService(mockLogger.Object);
        }

        [Fact]
        public void GetAttribute_WhenAttributeExists_ShouldReturnValue()
        {
            var entity = new Entity("contact");
            entity["new_count"] = 42;

            var result = _service.GetAttribute(entity, "new_count");

            result.Should().Be(42);
        }

        [Fact]
        public void GetAttribute_WhenAttributeNotExists_ShouldReturnDefaultZero()
        {
            var entity = new Entity("contact");

            var result = _service.GetAttribute(entity, "new_count");

            result.Should().Be(0);
        }

        [Fact]
        public void SetAttribute_WhenAttributeExists_ShouldUpdateValue()
        {
            var entity = new Entity("contact");
            entity["new_count"] = 1;

            _service.SetAttribute(ref entity, "new_count", 10);

            entity["new_count"].Should().Be(10);
        }

        [Fact]
        public void SetAttribute_WhenAttributeNotExists_ShouldAddValue()
        {
            var entity = new Entity("contact");

            _service.SetAttribute(ref entity, "new_count", 7);

            entity.Contains("new_count").Should().BeTrue();
            entity["new_count"].Should().Be(7);
        }
    }
}

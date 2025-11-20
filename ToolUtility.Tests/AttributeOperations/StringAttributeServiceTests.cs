using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtility.Tests.TestHelpers;
using Microsoft.Xrm.Sdk;

namespace ToolUtility.Tests.AttributeOperations
{
    public class StringAttributeServiceTests
    {
        private readonly ToolUtilityNameSpace.AttributeOperations.StringAttributeService _service;

        public StringAttributeServiceTests()
        {
            var mockLogger = MockLoggerFactory.CreateNonGenericMock();
            _service = new ToolUtilityNameSpace.AttributeOperations.StringAttributeService(mockLogger.Object);
        }

        [Fact]
        public void GetAttribute_WhenAttributeExists_ShouldReturnValue()
        {
            var entity = new Entity("contact");
            entity["new_note"] = "hello";

            var result = _service.GetAttribute(entity, "new_note");

            result.Should().Be("hello");
        }

        [Fact]
        public void GetAttribute_WhenAttributeNotExists_ShouldReturnEmpty()
        {
            var entity = new Entity("contact");

            var result = _service.GetAttribute(entity, "new_note");

            result.Should().BeEmpty();
        }

        [Fact]
        public void SetAttribute_WhenAttributeExists_ShouldUpdateValue()
        {
            var entity = new Entity("contact");
            entity["new_note"] = "old";

            _service.SetAttribute(ref entity, "new_note", "newval");

            entity["new_note"].Should().Be("newval");
        }

        [Fact]
        public void SetAttribute_WhenAttributeNotExists_ShouldAddValue()
        {
            var entity = new Entity("contact");

            _service.SetAttribute(ref entity, "new_note", "val");

            entity.Contains("new_note").Should().BeTrue();
            entity["new_note"].Should().Be("val");
        }
    }
}

using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtility.Tests.TestHelpers;
using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtility.Tests.AttributeOperations
{
    public class DateTimeAttributeServiceTests
    {
        private readonly ToolUtilityNameSpace.AttributeOperations.DateTimeAttributeService _service;

        public DateTimeAttributeServiceTests()
        {
            var mockLogger = MockLoggerFactory.CreateNonGenericMock();
            _service = new ToolUtilityNameSpace.AttributeOperations.DateTimeAttributeService(mockLogger.Object);
        }

        [Fact]
        public void GetAttribute_WhenAttributeExists_ShouldReturnValue()
        {
            var entity = new Entity("contact");
            var now = new DateTime(2020,1,1);
            entity["new_date"] = now;

            var result = _service.GetAttribute(entity, "new_date");

            result.Should().Be(now);
        }

        [Fact]
        public void GetAttribute_WhenAttributeNotExists_ShouldReturnMinValue()
        {
            var entity = new Entity("contact");

            var result = _service.GetAttribute(entity, "new_date");

            result.Should().Be(DateTime.MinValue);
        }

        [Fact]
        public void SetAttribute_WhenAttributeExists_ShouldUpdateValue()
        {
            var entity = new Entity("contact");
            entity["new_date"] = new DateTime(2000,1,1);

            var newDate = new DateTime(2021,12,31);
            _service.SetAttribute(ref entity, "new_date", newDate);

            entity["new_date"].Should().Be(newDate);
        }

        [Fact]
        public void SetAttribute_WhenAttributeNotExists_ShouldAddValue()
        {
            var entity = new Entity("contact");

            var newDate = new DateTime(2022,6,15);
            _service.SetAttribute(ref entity, "new_date", newDate);

            entity.Contains("new_date").Should().BeTrue();
            entity["new_date"].Should().Be(newDate);
        }
    }
}

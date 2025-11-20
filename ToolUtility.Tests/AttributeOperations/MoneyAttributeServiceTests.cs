using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtility.Tests.TestHelpers;
using Microsoft.Xrm.Sdk;
using Moq;
using System;

namespace ToolUtility.Tests.AttributeOperations
{
    public class MoneyAttributeServiceTests
    {
        private readonly MoneyAttributeService _service;

        public MoneyAttributeServiceTests()
        {
            var mockLogger = MockLoggerFactory.CreateNonGenericMock();
            _service = new MoneyAttributeService(mockLogger.Object);
        }

        [Fact]
        public void GetAttribute_WhenAttributeExists_ShouldReturnMoneyValue()
        {
            var entity = new Entity("invoice");
            entity["new_amount"] = new Money(1234m);

            var result = _service.GetAttribute(entity, "new_amount");

            result.Should().NotBeNull();
            result.Value.Should().Be(1234m);
        }

        [Fact]
        public void GetAttribute_WhenAttributeNotExists_ShouldReturnZeroMoney()
        {
            var entity = new Entity("invoice");

            var result = _service.GetAttribute(entity, "new_amount");

            result.Should().NotBeNull();
            result.Value.Should().Be(0m);
        }

        [Fact]
        public void SetAttribute_WhenAttributeExists_ShouldUpdateValue()
        {
            var entity = new Entity("invoice");
            entity["new_amount"] = new Money(10m);

            _service.SetAttribute(ref entity, "new_amount", new Money(99.5m));

            ((Money)entity["new_amount"]).Value.Should().Be(99.5m);
        }

        [Fact]
        public void SetAttribute_WhenAttributeNotExists_ShouldAddValue()
        {
            var entity = new Entity("invoice");

            _service.SetAttribute(ref entity, "new_amount", new Money(7m));

            entity.Contains("new_amount").Should().BeTrue();
            ((Money)entity["new_amount"]).Value.Should().Be(7m);
        }
    }
}

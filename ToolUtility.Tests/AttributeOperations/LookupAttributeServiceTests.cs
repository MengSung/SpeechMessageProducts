using Xunit;
using FluentAssertions;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtility.Tests.TestHelpers;
using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtility.Tests.AttributeOperations
{
    public class LookupAttributeServiceTests
    {
        private readonly LookupAttributeService _service;

        public LookupAttributeServiceTests()
        {
            var mockLogger = MockLoggerFactory.CreateNonGenericMock();
            _service = new LookupAttributeService(mockLogger.Object);
        }

        [Fact]
        public void GetAttribute_WhenAttributeIsEntityReference_ShouldReturnGuid()
        {
            var entity = new Entity("contact");
            var id = Guid.NewGuid();
            entity["parentcustomerid"] = new EntityReference("account", id);

            var result = _service.GetAttribute(entity, "parentcustomerid");

            result.Should().Be(id);
        }

        [Fact]
        public void GetAttribute_WhenAttributeNotExists_ShouldReturnEmptyGuid()
        {
            var entity = new Entity("contact");

            var result = _service.GetAttribute(entity, "parentcustomerid");

            result.Should().Be(Guid.Empty);
        }

        [Fact]
        public void SetAttribute_WhenAttributeExists_ShouldUpdateValue()
        {
            var entity = new Entity("contact");
            var oldId = Guid.NewGuid();
            entity["parentcustomerid"] = new EntityReference("account", oldId);

            var newId = Guid.NewGuid();
            _service.SetAttribute(ref entity, "parentcustomerid", "account", newId);

            var er = entity["parentcustomerid"] as EntityReference;
            er.Should().NotBeNull();
            er.Id.Should().Be(newId);
            er.LogicalName.Should().Be("account");
        }

        [Fact]
        public void SetAttribute_WhenAttributeNotExists_ShouldAddValue()
        {
            var entity = new Entity("contact");

            var id = Guid.NewGuid();
            _service.SetAttribute(ref entity, "parentcustomerid", "account", id);

            entity.Contains("parentcustomerid").Should().BeTrue();
            var er = entity["parentcustomerid"] as EntityReference;
            er.Should().NotBeNull();
            er.Id.Should().Be(id);
            er.LogicalName.Should().Be("account");
        }
    }
}

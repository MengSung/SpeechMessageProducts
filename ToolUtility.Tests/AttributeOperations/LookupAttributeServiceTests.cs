/*
 * 檔案整體說明
 *
 * 目的與定位：保護LookupAttributeService的既有行為與相容性。受測元件的責任為：LookupAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存
 * logger、Entity、身分或跨請求狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 LookupAttributeServiceTests。
 * 測試案例入口包含 GetAttribute_WhenAttributeIsEntityReference_ShouldReturnGuid、
 * GetAttribute_WhenAttributeNotExists_ShouldReturnEmptyGuid、SetAttribute_WhenAttributeExists_ShouldUpdateValue、
 * SetAttribute_WhenAttributeNotExists_ShouldAddValue。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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

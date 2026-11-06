/*
 * 檔案整體說明
 *
 * 目的與定位：保護DateTimeAttributeService的既有行為與相容性。受測元件的責任為：DateTimeAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存
 * logger、Entity、身分或跨請求狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 DateTimeAttributeServiceTests。
 * 測試案例入口包含 GetAttribute_WhenAttributeExists_ShouldReturnValue、
 * GetAttribute_WhenAttributeNotExists_ShouldReturnMinValue、SetAttribute_WhenAttributeExists_ShouldUpdateValue、
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

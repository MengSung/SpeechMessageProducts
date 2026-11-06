/*
 * 檔案整體說明
 *
 * 目的與定位：保護StringAttributeService的既有行為與相容性。受測元件的責任為：StringAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存
 * logger、Entity、身分或跨請求狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 StringAttributeServiceTests。
 * 測試案例入口包含 GetAttribute_WhenAttributeExists_ShouldReturnValue、
 * GetAttribute_WhenAttributeNotExists_ShouldReturnEmpty、SetAttribute_WhenAttributeExists_ShouldUpdateValue、
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

/*
 * 檔案整體說明
 *
 * 目的與定位：保護BoolAttributeService的既有行為與相容性。受測元件的責任為：BoolAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存 logger、
 * Entity、身分或跨請求狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 BoolAttributeServiceTests。
 * 測試案例入口包含 GetAttribute_WhenAttributeExists_ShouldReturnValue、
 * GetAttribute_WhenAttributeNotExists_ShouldReturnFalse、SetAttribute_WhenAttributeExists_ShouldUpdateValue、
 * SetAttribute_WhenAttributeNotExists_ShouldAddValue。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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

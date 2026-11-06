/*
 * 檔案整體說明
 *
 * 目的與定位：保護MoneyAttributeService的既有行為與相容性。受測元件的責任為：MoneyAttributeService的無狀態屬性操作；只讀寫 caller 擁有的 Entity，不保存 logger、
 * Entity、身分或跨請求狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 MoneyAttributeServiceTests。
 * 測試案例入口包含 GetAttribute_WhenAttributeExists_ShouldReturnMoneyValue、
 * GetAttribute_WhenAttributeNotExists_ShouldReturnZeroMoney、SetAttribute_WhenAttributeExists_ShouldUpdateValue、
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

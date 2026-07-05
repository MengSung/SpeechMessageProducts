// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/AttributeOperations/MoneyAttributeServiceTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class MoneyAttributeServiceTests
// 主要成員：GetAttribute_WhenAttributeExists_ShouldReturnMoneyValue、GetAttribute_WhenAttributeNotExists_ShouldReturnZeroMoney、SetAttribute_WhenAttributeExists_ShouldUpdateValue、SetAttribute_WhenAttributeNotExists_ShouldAddValue
// 引用命名空間：Xunit、FluentAssertions、ToolUtilityNameSpace.AttributeOperations、ToolUtility.Tests.TestHelpers、Microsoft.Xrm.Sdk、Moq、System
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/AttributeOperations/DateTimeAttributeServiceTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class DateTimeAttributeServiceTests
// 主要成員：GetAttribute_WhenAttributeExists_ShouldReturnValue、GetAttribute_WhenAttributeNotExists_ShouldReturnMinValue、SetAttribute_WhenAttributeExists_ShouldUpdateValue、SetAttribute_WhenAttributeNotExists_ShouldAddValue
// 引用命名空間：Xunit、FluentAssertions、ToolUtilityNameSpace.AttributeOperations、ToolUtility.Tests.TestHelpers、Microsoft.Xrm.Sdk、System
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

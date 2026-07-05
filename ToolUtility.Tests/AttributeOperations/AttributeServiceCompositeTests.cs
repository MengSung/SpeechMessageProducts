// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Tests/AttributeOperations/AttributeServiceCompositeTests.cs
// 所屬區塊：ToolUtility 測試專案，驗證共用工具層的相容行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class AttributeServiceCompositeTests
// 主要成員：GetBoolAttribute_ShouldDelegateToBoolService、GetAllAttributeTypes_ShouldWork
// 引用命名空間：Xunit、FluentAssertions、ToolUtilityNameSpace.AttributeOperations、ToolUtility.Tests.TestHelpers、Microsoft.Xrm.Sdk
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

namespace ToolUtility.Tests.AttributeOperations
{
    public class AttributeServiceCompositeTests
    {
        [Fact]
        public void GetBoolAttribute_ShouldDelegateToBoolService()
        {
            var mockLogger = MockLoggerFactory.CreateNonGenericMock();
            var composite = new AttributeServiceComposite(mockLogger.Object);

            var entity = new Entity("contact");
            entity["new_ismember"] = true;

            var result = composite.GetBoolAttribute(entity, "new_ismember");

            result.Should().BeTrue();
        }

        [Fact]
        public void GetAllAttributeTypes_ShouldWork()
        {
            var mockLogger = MockLoggerFactory.CreateNonGenericMock();
            var composite = new AttributeServiceComposite(mockLogger.Object);

            var entity = new Entity("contact");
            entity["new_ismember"] = true;
            entity["new_count"] = 5;
            entity["new_note"] = "abc";
            entity["new_date"] = new System.DateTime(2020,1,1);
            entity["new_amount"] = new Money(12.34m);
            entity["parentcustomerid"] = new EntityReference("account", System.Guid.NewGuid());

            composite.GetBoolAttribute(entity, "new_ismember").Should().BeTrue();
            composite.GetIntAttribute(entity, "new_count").Should().Be(5);
            composite.GetStringAttribute(entity, "new_note").Should().Be("abc");
            composite.GetDateTimeAttribute(entity, "new_date").Should().Be(new System.DateTime(2020,1,1));
            composite.GetMoneyAttribute(entity, "new_amount").Value.Should().Be(12.34m);
            composite.GetLookupAttribute(entity, "parentcustomerid").Should().NotBe(System.Guid.Empty);
        }
    }
}

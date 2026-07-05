// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：Line.Messaging.Tests/LineMessageModelTests.cs
// 所屬區塊：LINE Messaging SDK 測試專案，驗證 API 端點、序列化與 Client 行為。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class LineMessageModelTests
// 主要成員：TextV2Message_rejects_null_text_with_clear_exception、CouponMessage_rejects_delivery_tag_longer_than_line_limit
// 引用命名空間：FluentAssertions、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using Xunit;

namespace Line.Messaging.Tests;

public sealed class LineMessageModelTests
{
    [Fact]
    public void TextV2Message_rejects_null_text_with_clear_exception()
    {
        var action = () => new TextV2Message(null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("text");
    }

    [Fact]
    public void CouponMessage_rejects_delivery_tag_longer_than_line_limit()
    {
        var longDeliveryTag = new string('a', 31);

        var action = () => new CouponMessage("coupon-001", longDeliveryTag);

        action.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("deliveryTag");
    }
}


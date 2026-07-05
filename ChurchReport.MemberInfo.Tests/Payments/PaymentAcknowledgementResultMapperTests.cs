// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentAcknowledgementResultMapperTests
// 主要成員：Map_returns_plain_text_content_result、Map_returns_json_content_result、Map_returns_redirect_result、Map_returns_status_code_result_for_none
// 引用命名空間：FluentAssertions、Microsoft.AspNetCore.Mvc、SpeechMessage.Payments.AspNetCore、SpeechMessage.Payments.Models、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class PaymentAcknowledgementResultMapperTests
{
    [Fact]
    public void Map_returns_plain_text_content_result()
    {
        var result = PaymentAcknowledgementResultMapper.Map(PaymentCallbackAcknowledgement.PlainText("8888"));

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.Content.Should().Be("8888");
        content.ContentType.Should().Be("text/plain");
        content.StatusCode.Should().Be(200);
    }

    [Fact]
    public void Map_returns_json_content_result()
    {
        var result = PaymentAcknowledgementResultMapper.Map(PaymentCallbackAcknowledgement.Json("{\"status\":\"success\"}"));

        var content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("application/json");
    }

    [Fact]
    public void Map_returns_redirect_result()
    {
        var result = PaymentAcknowledgementResultMapper.Map(PaymentCallbackAcknowledgement.Redirect("https://example.test/success"));

        result.Should().BeOfType<RedirectResult>()
            .Which.Url.Should().Be("https://example.test/success");
    }

    [Fact]
    public void Map_returns_status_code_result_for_none()
    {
        var result = PaymentAcknowledgementResultMapper.Map(PaymentCallbackAcknowledgement.None);

        result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(200);
    }
}

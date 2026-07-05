// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs
// 所屬區塊：ChurchReport 會員、付款與 LINE 共用流程的測試專案，用來固定產品層行為與回歸案例。
// 檔案責任：此檔案屬於測試範圍，註解重點在說明測試意圖、固定的回歸條件，以及避免未來重構時誤改既有契約。
// 主要型別：class PaymentHttpRequestMapperTests
// 主要成員：MapAsync_maps_get_query_values、MapAsync_maps_post_form_values、MapAsync_maps_json_raw_body_and_resets_stream_position
// 引用命名空間：System.Text、FluentAssertions、Microsoft.AspNetCore.Http、Microsoft.Extensions.Primitives、SpeechMessage.Payments.AspNetCore、SpeechMessage.Payments.Models、Xunit
// 閱讀路徑：閱讀此檔案時應先看測試名稱、Arrange/Act/Assert 結構與 mock/fake 設定，因為它們描述了被保護的產品規則與外部契約。
// 維護重點：測試註解應協助理解案例保護的規則，不應把斷言改成只配合目前實作的描述。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using SpeechMessage.Payments.AspNetCore;
using SpeechMessage.Payments.Models;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

public sealed class PaymentHttpRequestMapperTests
{
    [Fact]
    public async Task MapAsync_maps_get_query_values()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.QueryString = new QueryString("?ShopNo=S1&PayToken=T1");
        var mapper = new PaymentHttpRequestMapper();

        var request = await mapper.MapAsync(context.Request, "JesusTest", PaymentProviderKind.Sinopac);

        request.ProfileName.Should().Be("JesusTest");
        request.ProviderHint.Should().Be(PaymentProviderKind.Sinopac);
        request.Query["ShopNo"].Should().Be("S1");
        request.Query["PayToken"].Should().Be("T1");
    }

    [Fact]
    public async Task MapAsync_maps_post_form_values()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["order_id"] = "F1",
            ["prc"] = "250"
        });
        var mapper = new PaymentHttpRequestMapper();

        var request = await mapper.MapAsync(context.Request, "MyPayProduction", PaymentProviderKind.MyPay);

        request.Form["order_id"].Should().Be("F1");
        request.Form["prc"].Should().Be("250");
    }

    [Fact]
    public async Task MapAsync_maps_json_raw_body_and_resets_stream_position()
    {
        const string json = "{\"ret_code\":\"00\"}";
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var mapper = new PaymentHttpRequestMapper();

        var request = await mapper.MapAsync(context.Request, "TaishinSandbox", PaymentProviderKind.Taishin);

        request.RawBody.Should().Be(json);
        request.ContentType.Should().Be("application/json");
        context.Request.Body.Position.Should().Be(0);
    }
}

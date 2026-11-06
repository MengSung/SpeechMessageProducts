/*
 * 檔案整體說明
 *
 * 目的與定位：驗證GET query、POST form及JSON原始body映射，並確認讀取body後串流位置可供後續流程使用。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentHttpRequestMapperTests。
 * 測試案例入口包含 MapAsync_maps_get_query_values、MapAsync_maps_post_form_values、
 * MapAsync_maps_json_raw_body_and_resets_stream_position。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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

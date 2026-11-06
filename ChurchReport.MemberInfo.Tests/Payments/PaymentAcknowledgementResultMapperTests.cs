/*
 * 檔案整體說明
 *
 * 目的與定位：驗證文字、JSON、轉址及無本文狀態描述轉為正確MVC結果，保護核心與HTTP adapter的輸出邊界。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentAcknowledgementResultMapperTests。
 * 測試案例入口包含 Map_returns_plain_text_content_result、Map_returns_json_content_result、Map_returns_redirect_result、
 * Map_returns_status_code_result_for_none。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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

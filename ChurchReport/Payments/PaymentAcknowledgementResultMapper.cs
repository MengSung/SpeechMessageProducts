using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Models;

namespace ChurchReport.Payments;

/// <summary>
/// 將金流核心回傳的 acknowledgement descriptor 轉成 ASP.NET MVC <see cref="IActionResult"/>。
/// 核心只描述 provider 需要的回覆型態，不直接產生 Controller response；
/// 這個 mapper 是 ChurchReport 的 HTTP adapter，負責把協定回覆落地到 MVC。
/// </summary>
public sealed class PaymentAcknowledgementResultMapper
{
    /// <summary>
    /// 依照核心指定的 acknowledgement 型態產生 HTTP response。
    /// 注意這不是產品付款成功頁邏輯；它只處理 provider callback 是否已被主機系統接受。
    /// </summary>
    public static IActionResult Map(PaymentCallbackAcknowledgement acknowledgement)
    {
        return acknowledgement.Kind switch
        {
            PaymentAckKind.PlainText => new ContentResult
            {
                Content = acknowledgement.Content,
                ContentType = "text/plain",
                StatusCode = acknowledgement.StatusCode
            },
            PaymentAckKind.Json => new ContentResult
            {
                Content = acknowledgement.Content,
                ContentType = "application/json",
                StatusCode = acknowledgement.StatusCode
            },
            PaymentAckKind.Redirect => new RedirectResult(acknowledgement.Content),
            _ => new StatusCodeResult(acknowledgement.StatusCode)
        };
    }

    public IActionResult ToActionResult(PaymentCallbackAcknowledgement acknowledgement)
    {
        // 保留 instance method 供 DI 注入的 controller 使用；實作集中到 static Map，避免兩套轉換規則漂移。
        return Map(acknowledgement);
    }
}

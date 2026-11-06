/*
 * 檔案整體說明
 *
 * 目的與定位：將金流核心產生的acknowledgement描述轉成文字、JSON、重新導向或狀態碼等MVC結果；provider應答規則留在金流核心，這裡只負責HTTP輸出映射。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentAcknowledgementResultMapper。
 * 主要操作入口包含 Map、ToActionResult。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Payments.AspNetCore/DependencyInjection/PaymentAspNetCoreServiceCollectionExtensions.cs 
 * 追查目前工作樹中的合作／使用位置。
 */
using Microsoft.AspNetCore.Mvc;
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.AspNetCore;

/// <summary>
/// Converts payment-core acknowledgement descriptors into ASP.NET MVC results.
/// Provider acknowledgement rules remain in <c>SpeechMessage.Payments</c>; this
/// class only performs the host framework response mapping.
/// </summary>
public sealed class PaymentAcknowledgementResultMapper
{
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
        return Map(acknowledgement);
    }
}

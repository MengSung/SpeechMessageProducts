/*
 * 檔案整體說明
 *
 * 目的與定位：provider callback 所需的回應描述。 核心只描述「應回什麼」，實際 ASP.NET ContentResult/RedirectResult 由產品層轉換。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentCallbackAcknowledgement。
 * 主要操作入口包含 PlainText、Json、Redirect。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments/Models/PaymentCallbackResult.cs、
 * SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// provider callback 所需的回應描述。
/// 核心只描述「應回什麼」，實際 ASP.NET ContentResult/RedirectResult 由產品層轉換。
/// </summary>
public sealed record PaymentCallbackAcknowledgement
{
    public static PaymentCallbackAcknowledgement None { get; } = new();

    public PaymentAckKind Kind { get; init; } = PaymentAckKind.None;
    public string Content { get; init; } = string.Empty;
    public int StatusCode { get; init; } = 200;

    public static PaymentCallbackAcknowledgement PlainText(string content, int statusCode = 200)
    {
        return new PaymentCallbackAcknowledgement
        {
            Kind = PaymentAckKind.PlainText,
            Content = content,
            StatusCode = statusCode
        };
    }

    public static PaymentCallbackAcknowledgement Json(string content, int statusCode = 200)
    {
        return new PaymentCallbackAcknowledgement
        {
            Kind = PaymentAckKind.Json,
            Content = content,
            StatusCode = statusCode
        };
    }

    public static PaymentCallbackAcknowledgement Redirect(string url, int statusCode = 302)
    {
        return new PaymentCallbackAcknowledgement
        {
            Kind = PaymentAckKind.Redirect,
            Content = url,
            StatusCode = statusCode
        };
    }
}

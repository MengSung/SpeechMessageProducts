namespace SpeechMessage.Payments.Models;

/// <summary>
/// 描述金流 provider callback 完成後，主系統應回覆給 provider 的 HTTP 回應型態。
/// 這個 enum 是「協定層 acknowledgement」的抽象，不代表宿主產品的付款成功頁或失敗頁。
/// </summary>
public enum PaymentAckKind
{
    /// <summary>
    /// 不需要特殊內容，只依照狀態碼回覆；通常用於前端 return flow 或產品層自行接手的流程。
    /// </summary>
    None = 0,

    /// <summary>
    /// 回覆純文字內容，例如 MyPay callback 需要的固定文字 acknowledgement。
    /// </summary>
    PlainText = 1,

    /// <summary>
    /// 回覆 JSON 內容，例如台新 TSPG 後端通知所需的 JSON acknowledgement。
    /// </summary>
    Json = 2,

    /// <summary>
    /// 要求產品層轉址到指定 URL；核心只描述轉址需求，不直接依賴 ASP.NET。
    /// </summary>
    Redirect = 3
}

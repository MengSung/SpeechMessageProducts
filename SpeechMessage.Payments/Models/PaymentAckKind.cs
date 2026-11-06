/*
 * 檔案整體說明
 *
 * 目的與定位：描述金流 provider callback 完成後，主系統應回覆給 provider 的 HTTP 回應型態。 這個 enum 是「協定層 acknowledgement」的抽象，
 * 不代表宿主產品的付款成功頁或失敗頁。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentAckKind。
 * - 不需要特殊內容，只依照狀態碼回覆；通常用於前端 return flow 或產品層自行接手的流程。
 * - 回覆純文字內容，例如 MyPay callback 需要的固定文字 acknowledgement。
 * - 回覆 JSON 內容，例如台新 TSPG 後端通知所需的 JSON acknowledgement。
 * - 要求產品層轉址到指定 URL；核心只描述轉址需求，不直接依賴 ASP.NET。
 * 資料／選項欄位包含 PaymentAckKind，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs、
 * SpeechMessage.Payments/Models/PaymentCallbackAcknowledgement.cs 追查目前工作樹中的合作／使用位置。
 */
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

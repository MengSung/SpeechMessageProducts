/*
 * 檔案整體說明
 *
 * 目的與定位：查詢付款狀態的通用請求。 ProviderOrderRef 使用中立命名，不讓 QPay PayToken、MyPay uid 或台新 transaction_id 外漏為公開 API。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentQueryRequest。
 * 資料／選項欄位包含 PaymentQueryRequest、ProfileName、ProviderHint、ProductOrderId、ProviderOrderRef、Metadata，名稱、
 * 型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/DonationPaymentCreateGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/MyPayControllerAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/PaymentReturnControllerTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/TspgControllerAdapterTests.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 查詢付款狀態的通用請求。
/// ProviderOrderRef 使用中立命名，不讓 QPay PayToken、MyPay uid 或台新 transaction_id 外漏為公開 API。
/// </summary>
public sealed record PaymentQueryRequest
{
    public string ProfileName { get; init; } = string.Empty;
    public PaymentProviderKind? ProviderHint { get; init; }
    public string ProductOrderId { get; init; } = string.Empty;
    public string ProviderOrderRef { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

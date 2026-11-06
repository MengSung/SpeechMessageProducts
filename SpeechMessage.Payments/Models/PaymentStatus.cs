/*
 * 檔案整體說明
 *
 * 目的與定位：通用付款狀態。 Pending 表示建單成功或等待 provider 最終狀態，不等同已付款成功。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentStatus。
 * 資料／選項欄位包含 PaymentStatus，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/ChurchReportPaymentContextBuilderTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentCreateGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentReturnWorkflowTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentWorkflowTests.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 通用付款狀態。
/// Pending 表示建單成功或等待 provider 最終狀態，不等同已付款成功。
/// </summary>
public enum PaymentStatus
{
    Unknown = 0,
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}

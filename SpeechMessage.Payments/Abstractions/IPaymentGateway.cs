/*
 * 檔案整體說明
 *
 * 目的與定位：金流通用核心對外公開的主要入口。 宿主產品與未來其他產品只能透過這個 provider-neutral 介面建立付款、 查詢付款狀態與解析回呼；不得直接依賴永豐、高鉅、台新自己的 SDK 或封包格式。
 *
 * 主要功能與流程：
 * 本檔宣告 IPaymentGateway。
 * 資料／選項欄位包含 IPaymentGateway，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/DonationPaymentCreateGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorGatewayAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/MyPayControllerAdapterTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/PaymentReturnControllerTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/TspgControllerAdapterTests.cs 追查目前工作樹中的合作／使用位置。
 */
using SpeechMessage.Payments.Models;

namespace SpeechMessage.Payments.Abstractions;

/// <summary>
/// 金流通用核心對外公開的主要入口。
/// 宿主產品與未來其他產品只能透過這個 provider-neutral 介面建立付款、
/// 查詢付款狀態與解析回呼；不得直接依賴永豐、高鉅、台新自己的 SDK 或封包格式。
/// </summary>
public interface IPaymentGateway
{
    Task<PaymentCreateResult> CreatePaymentAsync(
        PaymentCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentStatusResult> QueryPaymentAsync(
        PaymentQueryRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentCallbackResult> ParseCallbackAsync(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken = default);
}

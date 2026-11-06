/*
 * 檔案整體說明
 *
 * 目的與定位：建立付款時可提供給 provider 的付款人資訊。 僅保存 provider 建單必要的基本資料，不承載宿主產品 contact/entity。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentCustomer。
 * 資料／選項欄位包含 PaymentCustomer、Name、Email、Phone，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/DonationPaymentCreateGatewayAdapterTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs、
 * SpeechMessage.Payments.AspNetCore/PaymentCreateRequestFactory.cs、
 * SpeechMessage.Payments.Workflows/PaymentOrderDraftMapper.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 建立付款時可提供給 provider 的付款人資訊。
/// 僅保存 provider 建單必要的基本資料，不承載宿主產品 contact/entity。
/// </summary>
public sealed record PaymentCustomer
{
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
}

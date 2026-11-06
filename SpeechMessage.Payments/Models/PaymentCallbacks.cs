/*
 * 檔案整體說明
 *
 * 目的與定位：付款 provider 需要使用的回呼網址集合。 網址由產品層提供，避免 reusable core 綁定宿主產品的部署網域。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentCallbacks。
 * 資料／選項欄位包含 PaymentCallbacks、ReturnUrl、BackendUrl、SuccessUrl、FailureUrl，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/TspgControllerAdapterTests.cs、
 * SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 付款 provider 需要使用的回呼網址集合。
/// 網址由產品層提供，避免 reusable core 綁定宿主產品的部署網域。
/// </summary>
public sealed record PaymentCallbacks
{
    public string ReturnUrl { get; init; } = string.Empty;
    public string BackendUrl { get; init; } = string.Empty;
    public string SuccessUrl { get; init; } = string.Empty;
    public string FailureUrl { get; init; } = string.Empty;
}

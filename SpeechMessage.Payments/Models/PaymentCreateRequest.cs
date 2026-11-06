/*
 * 檔案整體說明
 *
 * 目的與定位：建立付款的通用請求。 此 DTO 不使用任何 provider 專屬欄位名稱；必要的 provider 相容參數放在 Metadata， 由各 provider mapper 解讀，避免宿主產品綁死在
 * QPay/MyPay/TSPG 模型上。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentCreateRequest。
 * 資料／選項欄位包含 PaymentCreateRequest、ProfileName、ProviderHint、ProductOrderId、Amount、Currency、Description、
 * PaymentMethod、PaymentMethodSubType、Callbacks、Customer、Items、Metadata，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs、
 * SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs、
 * SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs、
 * SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 建立付款的通用請求。
/// 此 DTO 不使用任何 provider 專屬欄位名稱；必要的 provider 相容參數放在 Metadata，
/// 由各 provider mapper 解讀，避免宿主產品綁死在 QPay/MyPay/TSPG 模型上。
/// </summary>
public sealed record PaymentCreateRequest
{
    public string ProfileName { get; init; } = string.Empty;
    // ProviderHint 用於防呆：指定 provider 時，gateway 會檢查 profile.Provider 是否一致。
    public PaymentProviderKind? ProviderHint { get; init; }
    public string ProductOrderId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "TWD";
    public string Description { get; init; } = string.Empty;
    public string PaymentMethod { get; init; } = string.Empty;
    public string PaymentMethodSubType { get; init; } = string.Empty;
    public PaymentCallbacks Callbacks { get; init; } = new();
    public PaymentCustomer Customer { get; init; } = new();
    public IReadOnlyList<PaymentLineItem> Items { get; init; } = Array.Empty<PaymentLineItem>();
    // Metadata 承載舊流程必要但非通用的欄位，例如 QPay Param1/Param2 或 MyPay PFN。
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

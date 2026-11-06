/*
 * 檔案整體說明
 *
 * 目的與定位：產品層將 web request 攤平成這個 callback DTO 後交給金流核心。 核心因此不需要參考 ASP.NET web runtime 型別，保持可被其他產品重用。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentCallbackRequest。
 * 資料／選項欄位包含 PaymentCallbackRequest、ProfileName、ProviderHint、HttpMethod、ContentType、RawBody、Query、Form、Headers，名稱、
 * 型別與序列化規約須與消費端保持一致。
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
/// 產品層將 web request 攤平成這個 callback DTO 後交給金流核心。
/// 核心因此不需要參考 ASP.NET web runtime 型別，保持可被其他產品重用。
/// </summary>
public sealed record PaymentCallbackRequest
{
    public string ProfileName { get; init; } = string.Empty;
    public PaymentProviderKind? ProviderHint { get; init; }
    public string HttpMethod { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    // RawBody 保留給 JSON 或 form-urlencoded callback parser；產品層讀取後必須 rewind request body。
    public string RawBody { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Form { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}

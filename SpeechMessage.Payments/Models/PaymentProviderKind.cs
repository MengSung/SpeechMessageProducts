/*
 * 檔案整體說明
 *
 * 目的與定位：金流核心目前支援的 provider 種類。 此 enum 是跨專案設定與 runtime routing 的穩定識別，不應塞入 provider SDK 型別。
 *
 * 主要功能與流程：
 * 本檔宣告 PaymentProviderKind。
 * - 未指定或尚未解析；一般只用於輸入預設值，正式執行時應由 profile 決定實際 provider。
 * 資料／選項欄位包含 PaymentProviderKind，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 模型含巢狀集合或敏感欄位時，呼叫端須維持目前登入／操作的所有權；顯示文字不能取代正式資料庫ID。
 * 修改繫結欄位、預設值或複製路徑時，須同步核對序列化、表單、快照及上傳映射，避免局部更新污染其他讀者。
 * 可沿 ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/PaymentReturnControllerTests.cs、
 * ChurchReport.MemberInfo.Tests/Payments/TspgControllerAdapterTests.cs、
 * SpeechMessage.Payments.Tests/Configuration/PaymentOptionsTests.cs、
 * SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs 追查目前工作樹中的合作／使用位置。
 */
namespace SpeechMessage.Payments.Models;

/// <summary>
/// 金流核心目前支援的 provider 種類。
/// 此 enum 是跨專案設定與 runtime routing 的穩定識別，不應塞入 provider SDK 型別。
/// </summary>
public enum PaymentProviderKind
{
    /// <summary>
    /// 未指定或尚未解析；一般只用於輸入預設值，正式執行時應由 profile 決定實際 provider。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 永豐 QPay 金流。
    /// </summary>
    Sinopac = 1,

    /// <summary>
    /// 高鉅 MyPay 金流。
    /// </summary>
    MyPay = 2,

    /// <summary>
    /// 台新 TSPG 金流。
    /// </summary>
    Taishin = 3
}

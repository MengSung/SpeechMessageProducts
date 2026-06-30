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

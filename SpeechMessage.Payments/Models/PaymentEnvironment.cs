namespace SpeechMessage.Payments.Models;

/// <summary>
/// 表示商店 profile 使用的金流環境。
/// provider 實作會依據此值與 profile endpoint 判斷要呼叫測試站或正式站。
/// </summary>
public enum PaymentEnvironment
{
    /// <summary>
    /// 測試或沙盒環境；通常使用測試商店代號、測試金鑰與 sandbox API endpoint。
    /// </summary>
    Sandbox = 0,

    /// <summary>
    /// 正式環境；必須搭配正式商店代號、正式金鑰與 production API endpoint。
    /// </summary>
    Production = 1
}

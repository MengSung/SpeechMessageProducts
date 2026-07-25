// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/DynamicsAuthMode.cs
// 目的：定義 no-SDK Web API 連線器支援的驗證模式。
//
// 保母教學：
// - 這是「伺服器端 profile」用的驗證，不是終端使用者登入 CRM。
// - 不要把 LINE 使用者、帳號密碼登入 session 綁進這裡。
// - 模式是 tagged union：選一種，就只能填該模式允許的欄位。
// ============================================================================

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// Dynamics Web API 驗證模式。
/// </summary>
public enum DynamicsAuthMode
{
    /// <summary>
    /// Windows 整合驗證（on-prem AD / IWA）。
    /// </summary>
    Windows = 0,

    /// <summary>
    /// AD FS OAuth 服務工作負載流程（IFD）。
    /// </summary>
    AdfsOAuth = 1
}

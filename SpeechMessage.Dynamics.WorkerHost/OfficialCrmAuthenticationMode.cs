namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 定義版本隔離的官方 CRM Worker 可接受之 CE on-premises 驗證形狀。
/// 這個列舉只描述 Worker 內部如何建立 <c>CrmServiceClient</c>，不攜帶認證資料、
/// Token、Cookie 或呼叫端 Session，也不允許 Gateway 在要求期間切換驗證方式。
/// </summary>
public enum OfficialCrmAuthenticationMode
{
    /// <summary>
    /// 使用 Active Directory／整合式 Windows 驗證連線至非 IFD 的 CE 組織。
    /// </summary>
    ActiveDirectory = 1,

    /// <summary>
    /// 使用 Microsoft XRM tooling 支援的 IFD／Claims 連線形狀。
    /// </summary>
    Ifd = 2
}

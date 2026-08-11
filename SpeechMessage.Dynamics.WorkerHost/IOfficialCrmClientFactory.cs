namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 建立單一 Profile generation 專用 Official CRM client 的工廠契約。
/// Factory 不得快取 credential、token、endpoint、SDK client 或 request state；成功建立的 client
/// 由呼叫端獨占並負責 Dispose，建構失敗則由實作在例外離開前完成 rollback cleanup。
/// </summary>
public interface IOfficialCrmClientFactory
{
    /// <summary>
    /// 依 immutable profile generation 建立 client；不得在失敗時切換另一個 Profile、CE 版本或 Connector。
    /// </summary>
    /// <param name="profileGenerationId">Supervisor 已驗證的世代識別碼。</param>
    /// <returns>由呼叫端負責確定性釋放的 Official CRM client。</returns>
    IOfficialCrmClient Create(string profileGenerationId);
}

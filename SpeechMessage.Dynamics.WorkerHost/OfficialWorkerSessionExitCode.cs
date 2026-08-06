namespace SpeechMessage.Dynamics.WorkerHost;

public enum OfficialWorkerSessionExitCode
{
    CleanDrain = 0,
    ClientNotReady = 10,
    ProtocolFailure = 11,
    Cancelled = 12,
    UpstreamFailure = 13,
    ClientDisposeFailure = 14,
    UnexpectedFailure = 15,

    /// <summary>
    /// Official SDK client 已建立但 Worker-local 固定 WhoAmI identity probe 未通過。
    /// 此 code 不攜帶 CRM response、Organization、endpoint 或 credential；Supervisor 只能將其視為
    /// fail-closed startup outcome，清理目前 generation，且不得 fallback 或自動重試。
    /// </summary>
    IdentityProbeNotReady = 16,

    /// <summary>SDK startup 的安全分類為 authentication failure；不含原始身份或密碼。</summary>
    AuthenticationNotReady = 17,

    /// <summary>SDK startup 的安全分類為 TLS、憑證或 secure-channel failure。</summary>
    SecureChannelNotReady = 18,

    /// <summary>SDK startup 的安全分類為名稱解析、連線、逾時或 transport failure。</summary>
    TransportNotReady = 19,

    /// <summary>SDK 未提供可安全細分的 not-ready detail；維持 fail-closed。</summary>
    UnclassifiedSdkNotReady = 20,

    /// <summary>
    /// SDK client 未 ready 且未留下 startup exception；此分類只證明診斷來源不可用，
    /// 不得被解讀成帳密、TLS、傳輸或 CE 授權任一具體問題，仍必須 fail-closed。
    /// </summary>
    SdkDiagnosticUnavailable = 21,

    /// <summary>
    /// SDK 以 framework 初始化／設定類例外拒絕 ready；此 code 不帶回原始 exception 或 profile detail，
    /// 且不得被用於猜測 credential、重試、fallback 或更換 Connector。
    /// </summary>
    SdkInitializationNotReady = 22
}

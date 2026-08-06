using System;

namespace SpeechMessage.Dynamics.WorkerHost;

/// <summary>
/// 表示 Official CRM client 在 Worker 發布 READY 前的去識別化啟動狀態。
/// 此分類只區分 SDK client 本身未就緒與已建立 client 但固定 WhoAmI 身分驗證未通過；它不保存或
/// 傳遞 endpoint、組織識別碼、使用者名稱、Credential reference、密碼、token、cookie、SDK 例外或
/// 任何 CRM payload。狀態只屬於目前 Worker process generation，Worker 結束或 adapter Dispose 後不保留。
/// </summary>
public enum OfficialCrmClientStartupReadiness
{
    /// <summary>
    /// SDK client 已就緒，且 Worker-local 固定 WhoAmI 驗證已通過預期 Organization 與 CE 版本契約。
    /// </summary>
    Ready = 0,

    /// <summary>
    /// 官方 SDK client 無法宣告 ready；Worker 必須 fail closed，且不得發布 READY 或改用其他 transport。
    /// </summary>
    SdkClientNotReady = 1,

    /// <summary>
    /// SDK client 已就緒，但固定 WhoAmI 或其 Organization／CE-version 驗證未通過；這不是一般 SDK
    /// connection failure，且同樣不得發布 READY、重送或 fallback。
    /// </summary>
    IdentityProbeNotReady = 2
}

/// <summary>
/// 表示 SDK client 尚未 ready 時可安全公開的失敗邊界。分類只依例外型別與有限的
/// <see cref="System.Net.WebExceptionStatus"/>，不保留或輸出例外文字、endpoint、Organization、
/// user name、credential、token、cookie 或 CRM payload；未知 detail 一律維持 fail-closed。
/// </summary>
public enum OfficialCrmClientStartupFailureCategory
{
    /// <summary>SDK client 已 ready，或尚未有可分類的 SDK-not-ready failure。</summary>
    None = 0,

    /// <summary>SDK 或 STS 拒絕 authentication／security token 身分，未發布 READY。</summary>
    Authentication = 1,

    /// <summary>TLS、憑證信任或安全通道無法建立，未發布 READY。</summary>
    SecureChannel = 2,

    /// <summary>名稱解析、連線、逾時或 HTTP transport 無法建立，未發布 READY。</summary>
    Transport = 3,

    /// <summary>SDK 未提供可安全分類 detail；不得猜測、重試或 fallback。</summary>
    Unclassified = 4,

    /// <summary>
    /// SDK client 尚未 ready，但沒有提供可供目前 generation 即時投影的 startup exception。
    /// 這與已有 exception 但不屬於 allowlist family 的 <see cref="Unclassified"/> 不同；兩者都不得
    /// 觸發 retry、fallback 或輸出原始 SDK detail。
    /// </summary>
    DiagnosticUnavailable = 5,

    /// <summary>
    /// SDK client 建構或初始化層回報 framework 設定／格式／operation failure；此類別不含原始 detail，
    /// 也不能推論為特定帳密、TLS 或 CE server 問題。Worker 必須停止目前 generation 並 fail-closed。
    /// </summary>
    SdkInitialization = 6
}

/// <summary>
/// 將官方 SDK 的短生命週期 startup exception 投影為固定安全 enum。此方法只在 Worker-local
/// startup scope 內巡覽最多八層 InnerException，回傳後不保留 exception reference；因此不會讓
/// stack trace、endpoint、credential 或 server 回覆進入 IPC、Gateway、cache 或跨 profile state。
/// </summary>
public static class OfficialCrmClientStartupFailureClassifier
{
    private const int MaximumInnerExceptionDepth = 8;

    /// <summary>
    /// 依 exception family 分類 SDK-not-ready 原因。優先序固定為 authentication、secure channel、
    /// transport；除標準網路型別外，也以固定的 framework full-name allowlist 辨識 WCF／HTTP family，
    /// 避免 WorkerHost 為了共用分類器而載入特定 CE worker 的 SDK assembly。不識別的類型回傳
    /// <see cref="OfficialCrmClientStartupFailureCategory.Unclassified"/>。
    /// 這個分類只改善去識別化診斷，不能授權 connector fallback、額外登入重試或 request-time routing。
    /// </summary>
    /// <param name="exception">由目前 SDK client 暫時提供的 startup exception；可為 null。</param>
    /// <returns>只含有限類別、沒有原始例外資料的 startup failure category。</returns>
    public static OfficialCrmClientStartupFailureCategory Classify(Exception? exception)
    {
        if (exception is null)
        {
            return OfficialCrmClientStartupFailureCategory.DiagnosticUnavailable;
        }

        var current = exception;
        for (var depth = 0; current is not null && depth < MaximumInnerExceptionDepth; depth++)
        {
            if (current is System.Security.Authentication.AuthenticationException or
                System.Security.SecurityException)
            {
                return OfficialCrmClientStartupFailureCategory.Authentication;
            }

            if (current is System.Net.WebException webException)
            {
                switch (webException.Status)
                {
                    case System.Net.WebExceptionStatus.TrustFailure:
                    case System.Net.WebExceptionStatus.SecureChannelFailure:
                        return OfficialCrmClientStartupFailureCategory.SecureChannel;
                    case System.Net.WebExceptionStatus.NameResolutionFailure:
                    case System.Net.WebExceptionStatus.ConnectFailure:
                    case System.Net.WebExceptionStatus.Timeout:
                    case System.Net.WebExceptionStatus.ProtocolError:
                        return OfficialCrmClientStartupFailureCategory.Transport;
                }
            }

            // WebException 繼承 InvalidOperationException，故必須先完成 WebExceptionStatus 的精確
            // TLS／transport 分類，才能再處理泛用 SDK 初始化例外；反轉順序會把 secure-channel
            // failure 誤導成設定問題，且會使 operator 對錯誤邊界採取錯誤的修正方向。
            if (current is InvalidOperationException or
                ArgumentException or
                FormatException or
                NotSupportedException)
            {
                return OfficialCrmClientStartupFailureCategory.SdkInitialization;
            }

            // WorkerHost 不引用 net48 專屬 WCF assembly；只比較 framework exception 的固定完整名稱，
            // 不保存 Type、Assembly、訊息或 stack graph。此 allowlist 只改善去識別化分類，不會把未知
            // 型別當成成功，也不會把分類結果用於 retry、fallback 或 connector 選擇。
            var typeName = current.GetType().FullName;
            if (typeName is
                "System.ServiceModel.Security.MessageSecurityException" or
                "System.ServiceModel.Security.SecurityNegotiationException" or
                "System.ServiceModel.Security.SecurityAccessDeniedException" or
                "System.IdentityModel.Tokens.SecurityTokenException")
            {
                return OfficialCrmClientStartupFailureCategory.Authentication;
            }

            if (typeName is
                "System.TimeoutException" or
                "System.Net.Http.HttpRequestException" or
                "System.Net.Sockets.SocketException" or
                "System.ServiceModel.EndpointNotFoundException" or
                "System.ServiceModel.CommunicationException" or
                "System.ServiceModel.CommunicationObjectFaultedException" or
                "System.ServiceModel.ProtocolException" or
                "System.ServiceModel.ServerTooBusyException")
            {
                return OfficialCrmClientStartupFailureCategory.Transport;
            }

            current = current.InnerException;
        }

        return OfficialCrmClientStartupFailureCategory.Unclassified;
    }
}

/// <summary>
/// 由 Worker-local Official CRM adapter 選擇性實作的啟動狀態投影。
/// <see cref="OfficialWorkerSession"/> 只使用此固定 enum 決定 sanitized process exit code；介面沒有
/// CRM SDK 型別、敏感診斷文字或可變連線資料，亦不跨越 IPC、Gateway HTTP 或產品邊界。
/// 未實作本介面的既有 test client 仍依 <see cref="IOfficialCrmClient.IsReady"/> 走既有 fail-closed 行為。
/// </summary>
public interface IOfficialCrmClientStartupDiagnostics
{
    /// <summary>
    /// 取得目前 generation 的無敏感啟動狀態。呼叫端不得快取此結果或以它選擇替代 Connector；
    /// process disposal 後的狀態一律視為不可使用。
    /// </summary>
    OfficialCrmClientStartupReadiness StartupReadiness { get; }

    /// <summary>
    /// 取得 SDK-not-ready 的固定、去識別化失敗分類。僅當 <see cref="StartupReadiness"/> 是
    /// <see cref="OfficialCrmClientStartupReadiness.SdkClientNotReady"/> 時才可用；呼叫端不得保存
    /// 此值、把它當成秘密診斷，或用它決定替代 Connector／CE version／credential。
    /// </summary>
    OfficialCrmClientStartupFailureCategory StartupFailureCategory { get; }
}

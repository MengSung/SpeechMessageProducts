using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// Resolver 成功後輸出的不可變 Profile snapshot。它是後續 Admission、Connector Router 與
/// Pool 的共同邊界，必須以 <see cref="ProfileAlias"/> 與 <see cref="GenerationId"/> 隔離；
/// 不可包含明碼憑證、Token、Cookie、使用者身分或任何可變連線/Session 物件。
/// </summary>
public sealed record ResolvedProfile(
    string ProfileAlias,
    string OrganizationAlias,
    Guid OrganizationId,
    CeVersion CeVersion,
    ConnectorKind ConnectorKind,
    string CredentialReference,
    ResolvedPoolPolicy Pool,
    ResolvedOperationPolicy Operation,
    long GenerationId);

/// <summary>
/// 經過界限驗證後的不可變 Pool 政策。此型別只包含 scalar 值；Pool 的連線、計時器與
/// Lease 仍由 generation owner 建立與釋放，不能被設定快照長期保留。
/// </summary>
public sealed record ResolvedPoolPolicy(
    int MinSize,
    int MaxSize,
    TimeSpan IdleTimeout,
    TimeSpan AcquireTimeout,
    bool HealthCheckOnAcquire);

/// <summary>
/// 經過界限驗證後的不可變 operation 政策。後續實作者必須把 timeout 套用到一次性 CTS，
/// 在完成、取消或失敗時釋放該 CTS 與所有 retry delay 資源。
/// </summary>
public sealed record ResolvedOperationPolicy(
    TimeSpan Timeout,
    int MaxRetries,
    TimeSpan RetryBaseDelay);

using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Abstractions.Connectors;

/// <summary>
/// 表示已通過 RequestGuard 與部署端 Profile 路由的 SDK-free Connector 作業。
/// 此物件不包含 CRM SDK 型別、OrganizationId、端點、Credential、Token、Cookie 或可變 Session；
/// 這些部署機密只能由 Profile 與 Connector Factory 在受控邊界內解析，避免請求竄改路由或跨租戶洩漏。
/// </summary>
public sealed record ConnectorOperation
{
    /// <summary>取得已由服務端登錄並授權的能力作業識別碼。</summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// 取得經過 Guard 與 Operation Registry 驗證的有界 typed 作業參數。Pool 與 Lease 不得保存此字典
    /// 超出單次執行的生命週期；Official Worker adapter 只在同步 prepare 階段讀取它，之後只保留
    /// normalized scalar copy，避免 JsonElement、請求資料或使用者輸入被 idle Worker、快取或後續請求重用。
    /// </summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// 取得本次作業的絕對截止時間。Lease 必須以此時間建立短生命週期取消來源，並在執行結束後立即釋放。
    /// </summary>
    public required DateTimeOffset DeadlineUtc { get; init; }

    /// <summary>取得供 Admission 預算使用的已驗證封包大小估計值。</summary>
    public int EstimatedBytes { get; init; } = 256;

    /// <summary>
    /// 取得由已驗證工作負載推導的主體識別碼。它僅供 Admission 公平性與配額使用，
    /// 不得被 Pool 當作 Client、Session 或 Credential 的共用索引鍵。
    /// </summary>
    public required string WorkloadSubjectId { get; init; }
}

/// <summary>
/// 表示 Connector 的 SDK-free 執行結果。結果只允許已投影且有界的產品資料；
/// 不得承載 CRM 原始回應、絕對端點、Credential、Token、Cookie、Session 或 SDK 物件。
/// </summary>
public sealed record ConnectorOperationResult(bool Succeeded, string? ErrorCode = null)
{
    /// <summary>
    /// 取得已由 Connector 在 request scope 完成投影的封閉回應資料。Official Worker 必須直接交回
    /// <see cref="OperationResponseData"/> discriminated union，而不是把 SDK object、Entity、FetchXML、
    /// endpoint、credential、token、pipe 或原始 IPC frame 降級成字典。此純值不擁有 stream、process、
    /// timer、CTS 或 lease；那些資源仍由 Connector Lease 在 Dispose 前確定釋放。
    /// </summary>
    public OperationResponseData? Data { get; init; }

    /// <summary>
    /// 取得 connector 在同一次成功 metadata projection 中，由 CRM 回應的 <c>UserLocalizedLabel.LanguageCode</c>
    /// 所證實的 locale identifier。此欄位只允許 metadata OptionSet operation 使用，不能由 ProductClient、Gateway
    /// JSON 或 caller 指定；executor 只將其與已解析的 profile/generation/target 組成 private cache key，絕不把
    /// locale、profile、generation、SDK LocalizedLabel 或 cache key 放進 <see cref="Data"/>、產品回應或日誌。
    /// 值為 <see langword="null"/> 時代表 locale 無法被伺服器證實，executor 必須維持 request-local projection。
    /// </summary>
    public int? ServerResolvedMetadataLocale { get; init; }

    /// <summary>取得已投影的結果欄位；呼叫端必須依能力作業契約解讀，不得作為任意 CRM 欄位通道。</summary>
    public IReadOnlyDictionary<string, string?> Values { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);
}

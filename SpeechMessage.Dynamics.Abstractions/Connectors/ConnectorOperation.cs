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
    /// 取得經過 Guard 驗證的有界作業參數。Pool 與 Lease 不得保存此字典超出單次執行的生命週期，
    /// 避免請求資料或使用者輸入被 idle Client、快取或後續請求重用。
    /// </summary>
    public IReadOnlyDictionary<string, string?> Parameters { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);

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
    /// <summary>取得已投影的結果欄位；呼叫端必須依能力作業契約解讀，不得作為任意 CRM 欄位通道。</summary>
    public IReadOnlyDictionary<string, string?> Values { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);
}

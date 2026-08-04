namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 指示 Catalog 中的 Organization 是否可供新的 Profile generation 使用。狀態不是暫存的
/// request/session flag；變更需建立新的已驗證組態快照，讓舊 generation 能依其 drain 規則
/// 釋放既有資源，而新的工作 fail-closed。
/// </summary>
public enum OrganizationState
{
    /// <summary>禁止新解析與新 outbound work。</summary>
    Disabled = 0,

    /// <summary>允許已通過完整 Profile 驗證的 Connector 使用。</summary>
    Enabled = 1
}

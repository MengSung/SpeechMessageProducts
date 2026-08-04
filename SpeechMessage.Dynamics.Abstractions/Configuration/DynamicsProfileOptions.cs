using SpeechMessage.Dynamics.Abstractions.Execution;

namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 部署端 Profile 的可繫結設定。此物件只能存在於組態繫結階段；Resolver 建構完成後會
/// 複製必要 scalar 值至 <see cref="ResolvedProfile"/>，因此後續組態熱更新或外部 Dictionary
/// 修改不會改寫運行中 Pool generation、Credential 參考或跨 Organization 路由。
/// </summary>
public sealed class DynamicsProfileOptions
{
    /// <summary>對應 <see cref="OrganizationCatalogEntry"/> 的固定 Organization Alias。</summary>
    public string OrganizationAlias { get; set; } = string.Empty;

    /// <summary>目標 CE 版本，必須與 ConnectorKind 相容。</summary>
    public CeVersion CeVersion { get; set; }

    /// <summary>部署端固定 Connector；產品請求不得覆寫。</summary>
    public ConnectorKind ConnectorKind { get; set; }

    /// <summary>由受信任 CredentialProvider 解讀的參考名稱，不含密碼、Token 或私鑰。</summary>
    public string CredentialReference { get; set; } = string.Empty;

    /// <summary>這個 Profile generation 專屬的有界 Pool 政策。</summary>
    public PoolPolicy Pool { get; set; } = new();

    /// <summary>這個 Profile 專屬的有界 operation 政策。</summary>
    public OperationPolicy Operation { get; set; } = new();
}

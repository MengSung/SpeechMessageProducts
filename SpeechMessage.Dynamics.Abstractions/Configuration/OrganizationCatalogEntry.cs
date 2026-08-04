namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 已由管理者確認的 Dynamics Organization Catalog 資料。Catalog 是 Profile 到實體
/// Organization 的唯一對照表；它不含 Credential 或 Session，且 Resolver 必須拒絕停用、
/// 缺失或 placeholder 身分，防止 Pool 與容量預算被建立在未知目標上。
/// </summary>
public sealed class OrganizationCatalogEntry
{
    /// <summary>供人員辨識的名稱，僅作部署端設定說明，不參與路由或容量 key。</summary>
    public string FriendlyName { get; set; } = string.Empty;

    /// <summary>Dynamics Organization 的唯一名稱，必須與 Catalog Alias 對應。</summary>
    public string UniqueName { get; set; } = string.Empty;

    /// <summary>已確認的 Dynamics Organization GUID；不可使用全零或全 f placeholder。</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Organization 是否允許被 Profile 解析。</summary>
    public OrganizationState State { get; set; } = OrganizationState.Disabled;

    /// <summary>部署端 Organization Service URI；產品請求與公開回應不得暴露或覆寫此值。</summary>
    public string ServiceUri { get; set; } = string.Empty;
}

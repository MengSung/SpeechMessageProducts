// ============================================================================
// 檔案：SpeechMessage.Dynamics.ProductClient/RuntimeHealth/RuntimeHealthWhoAmIIdentityDto.cs
// 目的：提供 runtime.health.whoami 唯一允許跨 ProductClient 邊界的 immutable GUID identity snapshot。
//
// 安全與生命週期邊界：
// - DTO 只含 Connector 已投影的三個 GUID，不含使用者名稱、組織 URL、endpoint、credential、token、cookie、
//   CRM Entity、HTTP response 或 connector reference。
// - 建構後所有值都是 get-only scalar；DTO 不擁有 stream、lease、timer、subscription、cancellation registration
//   或任何需 Dispose 的資源，因此不會延長 executor request scope。
// - 空 GUID 視為契約違反並立即拒絕，避免 partial identity 被 cache、log 或另一個 request 使用。
// ============================================================================

namespace SpeechMessage.Dynamics.ProductClient.RuntimeHealth;

/// <summary>
/// runtime health WhoAmI 的最小產品公開 identity DTO。
/// 每一個 instance 都由 ProductClient 從已驗證的封閉 response branch 建立；三個 GUID 僅代表此次
/// deployment-owned runtime 的健康投影，不能回送作為 profile、endpoint、credential、owner、connector、
/// authorization 或 Organization routing selector，也不得寫入共享 cache 或診斷輸出。
/// </summary>
public sealed class RuntimeHealthWhoAmIIdentityDto
{
    /// <summary>
    /// 建立完整、不可變的 WhoAmI identity snapshot。所有 scalar 必須在 ProductClient 的 exact response validation
    /// 後傳入；建構式仍重複拒絕空 GUID，確保日後其他 composition code 無法建立 partial result 來繞過健康契約。
    /// 這個型別不保存呼叫端 request、profile、workload 或任何外部資源，生命週期只隨目前回傳值而存在。
    /// </summary>
    /// <param name="userId">Connector 投影的非空 CRM system-user GUID。</param>
    /// <param name="businessUnitId">Connector 投影的非空 business-unit GUID。</param>
    /// <param name="organizationId">Connector 投影的非空 organization GUID。</param>
    public RuntimeHealthWhoAmIIdentityDto(Guid userId, Guid businessUnitId, Guid organizationId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        if (businessUnitId == Guid.Empty)
        {
            throw new ArgumentException("BusinessUnitId is required.", nameof(businessUnitId));
        }

        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("OrganizationId is required.", nameof(organizationId));
        }

        UserId = userId;
        BusinessUnitId = businessUnitId;
        OrganizationId = organizationId;
    }

    /// <summary>
    /// 取得固定 WhoAmI branch 中的非空 system-user GUID。它只識別本次健康回應的投影，不能成為使用者登入、
    /// profile 或 credential authority，且 DTO 不保存任何可變 CRM SDK graph。
    /// </summary>
    public Guid UserId { get; }

    /// <summary>
    /// 取得固定 WhoAmI branch 中的非空 business-unit GUID。它是 bounded scalar，不含 business-unit 名稱、
    /// Entity reference、query、endpoint 或可釋放 transport 資源。
    /// </summary>
    public Guid BusinessUnitId { get; }

    /// <summary>
    /// 取得固定 WhoAmI branch 中的非空 organization GUID。它僅作 runtime health identity correlation，
    /// 不授權 caller 選擇 Organization、connector 或租用另一個 profile 的 client/session。
    /// </summary>
    public Guid OrganizationId { get; }
}

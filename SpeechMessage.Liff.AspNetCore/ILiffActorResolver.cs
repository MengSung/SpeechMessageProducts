/*
檔案整體說明
目的與定位：跨產品F6的登入身分轉租戶角色介面，讓各產品在自己的組織內實作授權查詢，共用層不參考產品。
主要功能與流程：filter先驗證ID Token，再將單次LiffIdentity交給scoped resolver；resolver從部署固定的產品及Channel查權威資料，缺身分或沒有有效目前選擇時回null。
使用邊界與維護：呼叫端不能以query或路由決定actor，實作者不得跨請求快取身分、token、角色或連線。真正查詢故障往外傳至共用診斷，不能假裝無資料；caller取消須排空並釋放操作資源。本介面不自行建立任何資源。
*/
namespace SpeechMessage.Liff.AspNetCore;

/// <summary>由產品scoped服務每次重新驗證Contact、租戶與Channel；沒有有效選擇回null，真正故障保留例外。</summary>
public interface ILiffActorResolver
{
    /// <summary>只使用已由官方驗證的身分及伺服器部署範圍，不能將登入Channel直接當成Messaging Channel。</summary>
    /// <param name="identity">本次token驗證結果，不能跨請求重用。</param>
    /// <param name="cancellationToken">原HTTP caller取消，操作完成清理後才回傳。</param>
    /// <returns>當次獨立actor，或無候選／無有效選擇時null。</returns>
    Task<LiffActor?> ResolveAsync(LiffIdentity identity, CancellationToken cancellationToken);
}

/// <summary>角色來源的產品接點；F8前的預設實作回空權限，未來必須每次重新查詢而不跨請求快取。</summary>
public interface ILiffRoleResolver
{
    /// <summary>在已驗Contact及Tenant範圍內重新取得角色；不能由caller提供角色或擴張租戶。</summary>
    /// <param name="contactId">已驗的Contact權威ID。</param>
    /// <param name="tenantId">已驗的租戶權威ID。</param>
    /// <param name="cancellationToken">原caller取消，故障與取消需保留。</param>
    /// <returns>當次角色集合，actor會複製後發布。</returns>
    Task<IReadOnlyCollection<string>> ResolveRolesAsync(Guid contactId, Guid tenantId, CancellationToken cancellationToken);
}

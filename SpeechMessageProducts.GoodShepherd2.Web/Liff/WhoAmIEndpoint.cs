/*
檔案整體說明
目的與定位：F6好牧人2.0的LIFF身分、租戶隔離ping及公開設定端點，由startup filter的固定/api/liff分支映射。
主要功能與流程：只有公開config免驗身分，回LIFF app ID與Development旗標；whoami及ping都經共用LiffAuthenticationFilter，在本次Items讀actor。whoami名稱只取同一scoped resolver完整發布的候選，ping先比對租戶ID，不讀其他租戶資料。
使用邊界與維護：路由tenantId及前端probe都是未信任目標，不可決定actor或角色。回應沒有token、userId、Contact ID、Channel秘密或錯誤細節；名稱及角色只活本次HTTP，由filter finally解除Items。端點沒有自行保留request、Session或快取，不建立資源；所有真正故障由分支共用診斷入口處理，離線HTTP測試不能代替LINE及手機驗收。
*/
using SpeechMessage.Liff.AspNetCore;

namespace SpeechMessageProducts.GoodShepherd2.Web.Liff;

/// <summary>F6固定API集合；所有資料端點共用同一個驗證filter，config只公開部署的非秘密資訊。</summary>
internal static class WhoAmIEndpoint
{
    /// <summary>在已固定的/api/liff分支映射相對路由，不能由request選擇產品或Channel。</summary>
    /// <param name="endpoints">本分支唯一routing owner。</param>
    internal static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/config", (LiffOptions options, IWebHostEnvironment environment) =>
            environment.IsDevelopment()
                ? Results.Json(new { liffId = options.LiffId, isDevelopment = true })
                : Results.NotFound());
        endpoints.MapGet("/whoami", (HttpContext context, GoodShepherd2LiffActorResolver resolver) =>
        {
            if (context.Items[LiffAuthenticationFilter.ActorItemKey] is not LiffActor actor) return Results.StatusCode(403);
            return Results.Json(new { tenantId = actor.TenantId, tenantName = resolver.GetTenantName(actor), roles = actor.RoleCodes });
        }).AddEndpointFilter<LiffAuthenticationFilter>();
        endpoints.MapGet("/tenant/{tenantId:guid}/ping", (Guid tenantId, HttpContext context) =>
            context.Items[LiffAuthenticationFilter.ActorItemKey] is LiffActor actor && tenantId == actor.TenantId
                ? Results.Ok() : Results.StatusCode(403)).AddEndpointFilter<LiffAuthenticationFilter>();
    }
}

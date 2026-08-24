// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs
// 檔案責任：供 legacy ToolUtilityFactory 程序級單例延後解析目前 DI scope 的
//           IOrganizationService；絕不把 HttpContext、scope、lease、raw CRM client 或
//           使用者／租戶資料保存到單例，避免跨 request、跨使用者與跨租戶洩漏。
// 量測不變量：必須解析 IOrganizationService，而非直接解析 IDataverseGateway。前者是
//             Host 組合根唯一可插入計時、安全或稽核裝飾器的入口；直接取 Gateway 會繞過
//             裝飾器，使 JSONL 有 CRM 資料但同一 request 的 [Perf] 歸因為零。
// 量測單一所有者：Ambient 僅負責解析並委派；解析後的 GatewayOrganizationService 是 JSONL
//                   crm.op 的唯一量測者，而 Host 的 TimedOrganizationService 是 [Perf] 的唯一
//                   量測者。Ambient 再包 CrmOperationTrace.Measure 會把同一 SDK 呼叫寫入兩次，
//                   使 request.end.crmCount 與 [Perf] crm.n 失去逐請求可比性。
// 資源生命週期：有 HTTP request 時只借用其既有 scope；無 request 的背景相容路徑建立一個
//               短命 scope，且由 using 在 work 結束（包括例外）時確定釋放其 scoped gateway
//               與可能持有的 Dataverse lease。此型別本身不擁有或快取任何上述資源。
// 編碼要求：本檔案維持 UTF-8 無 BOM、CRLF，並以 final CRLF 結尾。
// ============================================================================
using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace ToolUtilityNameSpace.Dataverse;

/// <summary>
/// 為 legacy session／Factory 持有者提供的過渡性 <see cref="IOrganizationService"/> 代理。
/// </summary>
/// <remarks>
/// <para>
/// 此型別可被程序級 legacy Factory 保存，原因在於它只保存兩個無狀態的解析能力；每一次
/// CRM 操作才讀取當前 request 的 <see cref="IServiceProvider"/>。因此 A request 絕不會把
/// 它的 scope、租約、身分、cookie、credential 或 CRM client 交給 B request，亦不會因為
/// Factory 單例長存而延長任何 request 資源的生命週期。
/// </para>
/// <para>
/// 解析目標必須是 <see cref="IOrganizationService"/>。Host 可在這個 DI 邊界以 scoped
/// decorator 加入 request profiling；decorator 的內層仍會透過 <see cref="IDataverseGateway"/>
/// 取得 per-operation lease。若本類別直接解析 Gateway，便會越過 Host 的 decorator，破壞
/// 診斷歸因而且未來也容易越過其他同樣掛在介面邊界的安全策略。
/// </para>
/// <para>
/// 本類別不是 CRM 操作量測 owner。成功解析後，內層 <see cref="GatewayOrganizationService"/>
/// 會在真正發出 CRM 操作的唯一位置寫入 <c>crm.op</c>；Host 的 decorator 則對同一次介面
/// 呼叫更新 <c>[Perf]</c>。因此下列八個介面方法只能直接委派，絕不可再包
/// <c>CrmOperationTrace.Measure</c>；否則 JSONL 的 <c>request.end</c> 會把同一工作計算兩次，
/// 破壞效能分析與跨檔案歸因契約。
/// </para>
/// <para>
/// 非 HTTP 背景相容路徑沒有可借用的 request scope，因此每次呼叫建立一個短命 scope。該 scope
/// 的唯一擁有者是本類別的 <see cref="Run{T}"/>；<c>using</c> 確保成功、取消或例外都會釋放
/// scoped service 與尚待 container 回收的資源，避免背景作業留下 client 或 lease。
/// </para>
/// </remarks>
public sealed class AmbientGatewayOrganizationService : IOrganizationService
{
    /// <summary>
    /// 背景作業目前明確指定的 DI 服務提供者。AsyncLocal 讓巢狀 Task.Run 繼承同一個
    /// 背景 scope，但不把 request services 寫入程序級單例；每個 scope 結束時都會還原前值。
    /// </summary>
    private readonly AsyncLocal<IServiceProvider> _backgroundServiceProvider = new();

    /// <summary>
    /// 取得目前執行流程所屬 request 的服務提供者；回傳值只在本次同步操作中使用，絕不儲存。
    /// </summary>
    private readonly Func<IServiceProvider> _requestServicesAccessor;

    /// <summary>
    /// 建立無 request fallback scope 的根 factory；本欄位不代表或持有任何已建立的 scope。
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// 建立可安全由 legacy 程序級 Factory 保存的 ambient 解析代理。
    /// </summary>
    /// <param name="requestServicesAccessor">
    /// 只回傳目前 request 的服務提供者之委派；實作不得把已過期 HttpContext 或 scope 快取起來。
    /// </param>
    /// <param name="scopeFactory">
    /// 建立背景 fallback 的短命 scope 之 factory；每個建立出的 scope 都會在同一操作結束時釋放。
    /// </param>
    /// <exception cref="ArgumentNullException">任一解析能力缺失時擲出，避免在未知資源邊界上失敗開放。</exception>
    public AmbientGatewayOrganizationService(
        Func<IServiceProvider> requestServicesAccessor,
        IServiceScopeFactory scopeFactory)
    {
        _requestServicesAccessor = requestServicesAccessor ?? throw new ArgumentNullException(nameof(requestServicesAccessor));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <summary>
    /// 建立目前非同步流程的背景服務解析覆蓋範圍。
    /// </summary>
    /// <param name="serviceProvider">
    /// 由背景工作建立且擁有的 DI scope provider。呼叫端必須讓回傳的 scope override
    /// 在同一背景 scope Dispose 前釋放；本方法不接管 provider 的 Dispose 責任。
    /// </param>
    /// <returns>離開時還原上一層背景 provider 的一次性 scope。</returns>
    /// <remarks>
    /// <para>
    /// ASP.NET Core 的 HttpContextAccessor 以 AsyncLocal 流動 request services。若背景工作
    /// 只建立新 scope 卻不設定此覆蓋，legacy Factory 會在 request 結束後仍解析原 scope，形成
    /// disposed-scope race。覆蓋優先於 request accessor，確保每個 CRM 呼叫都由背景 scope 擁有。
    /// </para>
    /// <para>
    /// 此 API 只保存目前流程的 provider 參考，生命週期由回傳的 disposable 與呼叫端 using
    /// 綁定；不會把使用者、Session、HttpContext 或 provider 提升到另一個流程。
    /// </para>
    /// </remarks>
    public IDisposable BeginBackgroundScope(IServiceProvider serviceProvider)
    {
        if (serviceProvider == null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        var previous = _backgroundServiceProvider.Value;
        _backgroundServiceProvider.Value = serviceProvider;
        return new BackgroundScopeOverride(_backgroundServiceProvider, previous);
    }

    /// <summary>
    /// 在目前隔離 scope 的已裝飾組織服務上建立關聯。
    /// </summary>
    /// <remarks>此代理只委派；內層 Gateway 是 JSONL 唯一量測 owner，避免重複記錄同一關聯操作。</remarks>
    public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => Run(service => service.Associate(entityName, entityId, relationship, relatedEntities));

    /// <summary>
    /// 在目前隔離 scope 的已裝飾組織服務上建立資料列。
    /// </summary>
    /// <remarks>欄位值與建立結果不會被本代理保留；JSONL 與 [Perf] 分別由內層唯一 owner 建立。</remarks>
    public Guid Create(Entity entity)
        => Run(service => service.Create(entity));

    /// <summary>
    /// 在目前隔離 scope 的已裝飾組織服務上刪除資料列。
    /// </summary>
    /// <remarks>資料列 GUID 不被本代理保存；內層 Gateway 只記 schema 層級資訊且只記一次。</remarks>
    public void Delete(string entityName, Guid id)
        => Run(service => service.Delete(entityName, id));

    /// <summary>
    /// 在目前隔離 scope 的已裝飾組織服務上移除關聯，並維持既有例外傳遞語意。
    /// </summary>
    /// <remarks>本方法不保存關聯或參考集合；底層 Gateway 既是 crm.op owner 也是 lease 歸還 owner。</remarks>
    public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        => Run(service => service.Disassociate(entityName, entityId, relationship, relatedEntities));

    /// <summary>
    /// 在目前隔離 scope 的已裝飾組織服務上執行 SDK 組織要求。
    /// </summary>
    /// <remarks>不檢視 request 參數或 CRM 回應；計時 decorator 與 Gateway 各自對同一呼叫量測一次。</remarks>
    public OrganizationResponse Execute(OrganizationRequest request)
        => Run(service => service.Execute(request));

    /// <summary>
    /// 在目前隔離 scope 的已裝飾組織服務上讀取單一資料列。
    /// </summary>
    /// <remarks>本代理不讀取 GUID、欄位集合或回傳內容；內層 Gateway 對一次 Retrieve 只建立一筆 crm.op。</remarks>
    public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        => Run(service => service.Retrieve(entityName, id, columnSet));

    /// <summary>
    /// 在目前隔離 scope 的已裝飾組織服務上執行查詢。
    /// </summary>
    /// <remarks>查詢條件與資料列內容不會保留；回傳筆數只由內層唯一的 Gateway 測量點取得。</remarks>
    public EntityCollection RetrieveMultiple(QueryBase query)
        => Run(service => service.RetrieveMultiple(query));

    /// <summary>
    /// 在目前隔離 scope 的已裝飾組織服務上更新資料列。
    /// </summary>
    /// <remarks>本代理不快取 entity 或欄位值；更新的 JSONL 量測只由內層 Gateway 執行一次。</remarks>
    public void Update(Entity entity)
        => Run(service => service.Update(entity));

    /// <summary>
    /// 取得目前 scope 的完整 <see cref="IOrganizationService"/> 裝飾鏈，並在其上執行一次操作。
    /// </summary>
    /// <typeparam name="T">操作成功時要回傳的結果型別。</typeparam>
    /// <param name="work">只能使用本次呼叫提供的 service；不得保存它或由背景工作在 scope 釋放後使用。</param>
    /// <returns>底層組織服務的操作結果。</returns>
    /// <remarks>
    /// HTTP request 存在時借用其 scope，因此解析結果包含 Host 針對此 request 安裝的
    /// <see cref="IOrganizationService"/> decorator。這裡刻意不直接取
    /// <see cref="IDataverseGateway"/>，否則會避開 decorator，造成 RequestProfiler 漏記 CRM 時間。
    /// 沒有 request 時，<c>using</c> 是 fallback scope 的唯一 owner，保證例外時亦能 Dispose。
    /// </remarks>
    private T Run<T>(Func<IOrganizationService, T> work)
    {
        var backgroundServices = _backgroundServiceProvider.Value;
        if (backgroundServices != null)
        {
            return work(backgroundServices.GetRequiredService<IOrganizationService>());
        }

        var requestServices = _requestServicesAccessor();
        if (requestServices != null)
        {
            return work(requestServices.GetRequiredService<IOrganizationService>());
        }

        // 只有非 HTTP、且呼叫端未建立明確背景 scope 的 legacy 保底路徑才會到此；
        // scope 僅涵蓋單次同步 CRM 操作，using 釋放 scoped service，避免把 fallback
        // 資源提升為程序級或跨使用者狀態。SaveIntegrate 等正式背景流程必須使用上方 override。
        using var scope = _scopeFactory.CreateScope();
        return work(scope.ServiceProvider.GetRequiredService<IOrganizationService>());
    }

    /// <summary>
    /// 將無回傳值的 CRM 操作轉交給泛型 scope 解析路徑，維持所有介面方法共用同一資源邊界。
    /// </summary>
    /// <param name="work">只可在目前解析 service 的有效生命週期內執行的操作。</param>
    private void Run(Action<IOrganizationService> work)
        => Run<object>(service => { work(service); return null; });

    /// <summary>
    /// 還原建立背景覆蓋前的 provider；Dispose 冪等且不釋放 provider 本身。
    /// </summary>
    private sealed class BackgroundScopeOverride : IDisposable
    {
        private readonly AsyncLocal<IServiceProvider> _owner;
        private readonly IServiceProvider _previous;
        private int _disposed;

        /// <summary>建立一個只負責還原 AsyncLocal 值的 scope。</summary>
        public BackgroundScopeOverride(AsyncLocal<IServiceProvider> owner, IServiceProvider previous)
        {
            _owner = owner;
            _previous = previous;
        }

        /// <summary>還原上一層 provider；scope 的實際 Dispose 仍由呼叫端負責。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Value = _previous;
            }
        }
    }
}

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Diagnostics/Profiling/TimedToolUtilityProvider.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 TimedToolUtilityProvider 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class TimedToolUtilityProvider
// 主要成員：GetToolUtility
// 引用命名空間：Microsoft.AspNetCore.Http、ToolUtilityNameSpace、ToolUtilityNameSpace.DependencyInjection
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if DEBUG
using Microsoft.AspNetCore.Http;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.DependencyInjection;

namespace ChurchReport.Diagnostics.Profiling
{
    /// <summary>
    /// 裝飾 IToolUtilityProvider：確保取回的 ToolUtilityClass 之 m_Crm2011OrganizationService
    /// 為計時版（冪等 + 鎖，防 singleton 共用欄位並發首呼造成雙重包裝）。
    /// 涵蓋所有經 ToolUtility 的 CRM 呼叫（僅 Debug，且需 ProfilingSwitch 開啟）。
    /// </summary>
    public sealed class TimedToolUtilityProvider : IToolUtilityProvider
    {
        private static readonly object _wrapLock = new object();
        private readonly IToolUtilityProvider _inner;
        private readonly IHttpContextAccessor _http;

        public TimedToolUtilityProvider(IToolUtilityProvider inner, IHttpContextAccessor http)
        {
            _inner = inner;
            _http = http;
        }

        public ToolUtilityClass GetToolUtility()
        {
            var tu = _inner.GetToolUtility();
            if (ProfilingSwitch.Enabled && tu != null
                && tu.m_Crm2011OrganizationService != null
                && tu.m_Crm2011OrganizationService is not TimedOrganizationService)
            {
                lock (_wrapLock)
                {
                    var svc = tu.m_Crm2011OrganizationService;
                    if (svc != null && svc is not TimedOrganizationService)
                        tu.m_Crm2011OrganizationService = new TimedOrganizationService(svc, _http);
                }
            }
            return tu;
        }
    }
}
#endif

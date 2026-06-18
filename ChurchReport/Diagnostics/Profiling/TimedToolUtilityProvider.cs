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

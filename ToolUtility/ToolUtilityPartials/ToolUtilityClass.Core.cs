/*
 * 檔案整體說明
 *
 * 目的與定位：ToolUtilityClass - 核心部分 (Partial Class 1/10) 包含：欄位、常數、建構式、Dispose Pattern
 *
 * 主要功能與流程：
 * 本檔宣告 ToolUtilityClass。
 * - 建立由 DI 管理的 request-scoped ToolUtilityClass。 服務是 GatewayOrganizationService 或 
 * AmbientGatewayOrganizationService；兩者都只在 單次操作期間取得 lease。本型別與 legacy Factory 都只借用此代理，絕不在 Dispose 路徑釋放它，避免短命工具、
 * Facade 或程序級單例重複釋放 scope 所擁有的資源。
 * - 依層級輸出追蹤紀錄。簽章維持不變，既有 160 個呼叫點無須修改。
 * 主要操作入口包含 Dispose、TraceByLevel。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 */
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using ToolUtilityNameSpace.Core;

using ToolUtilityNameSpace.Diagnostics;

namespace ToolUtilityNameSpace
{
    /// <summary>
    /// ToolUtilityClass - 核心部分 (Partial Class 1/10)
    /// 包含：欄位、常數、建構式、Dispose Pattern
    /// </summary>
    public partial class ToolUtilityClass : IDisposable
    {
        #region 私有欄位
        public IOrganizationService m_Crm2011OrganizationService;

        private bool _disposed = false;
        private readonly ToolUtilityFacade _facade;
        private readonly IConfiguration _configuration;
        #endregion

        #region 設定屬性
        private string SERVER => _configuration?["CrmConnection:Server"] ?? "speechmessage.com.tw";
        private string PORT => _configuration?["CrmConnection:Port"] ?? "7777";
        private string ORGANIZATION => _configuration?["CrmConnection:Organization"] ?? "jesus";
        private string USERNAME => _configuration?["CrmConnection:Username"] ?? "Administrator@speechmessage.com.tw";
        private string PASSWORD => _configuration?["CrmConnection:Password"] ?? "hu9840";
        private string DOMAIN => _configuration?["CrmConnection:Domain"] ?? "DYNAMICS-365";
        #endregion

        #region 常數
        private DateTime ExpireDate = new DateTime(2013, 3, 30);
        private const String FILTERED_PROJECT = "";
        private const int EMPTY_VALUE = -999999999;
        protected const bool EXCUTION_FLAG = true;
        protected const bool EXCUTION_TRACE_LINE = true;

        protected const int TOTAL_LEVEL = 5;
        protected const int LEVEL_1 = 1;
        protected const int LEVEL_2 = 2;
        protected const int LEVEL_3 = 3;
        protected const int LEVEL_4 = 4;
        protected const int LEVEL_5 = 5;
        #endregion

        #region 追蹤
        /// <summary>
        /// 程序級的追蹤資源擁有者。
        /// </summary>
        /// <remarks>
        /// 本型別「不再」自行持有 FileStream、StreamWriter 或 TraceListener。
        /// 原因：那些是程序級資源（Trace.Listeners 為行程內的靜態集合），若與本型別
        /// 共用生命週期，本型別就無法安全地改為 request 範圍 —— 每建立一個實例都會
        /// 再向全域集合加入一個 listener，造成無界成長與日誌重複。
        /// 追蹤職責已移至 IToolUtilityTracer，其實作必須註冊為 Singleton。
        /// </remarks>
        private readonly IToolUtilityTracer _tracer;
        #endregion

        #region 建構式

        /// <summary>
        /// 建立由 DI 管理的 request-scoped ToolUtilityClass。
        /// 服務是 GatewayOrganizationService 或 AmbientGatewayOrganizationService；兩者都只在
        /// 單次操作期間取得 lease。本型別與 legacy Factory 都只借用此代理，絕不在
        /// Dispose 路徑釋放它，避免短命工具、Facade 或程序級單例重複釋放 scope 所擁有的資源。
        /// </summary>
        /// <param name="organizationService">Gateway 支撐的 Dataverse 操作代理，不持有 raw client。</param>
        /// <param name="tracer">程序級 Singleton 追蹤器；由 DI 在應用程式關閉時釋放。</param>
        /// <param name="configuration">CRM 設定；此路徑不會用它自行建立連線。</param>
        public ToolUtilityClass(
            IOrganizationService organizationService,
            IToolUtilityTracer tracer,
            IConfiguration configuration)
        {
            m_Crm2011OrganizationService = organizationService
                ?? throw new ArgumentNullException(nameof(organizationService));
            _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _facade = new ToolUtilityFacade(m_Crm2011OrganizationService);
        }

        public ToolUtilityClass(ref bool ValidFlag)
        {
            _facade = null;
            _configuration = null;
            _tracer = null;
            if (ExpireDate >= DateTime.Today)
            {
                ValidFlag = false;
            }
        }

        ~ToolUtilityClass() => Dispose(false);
        #endregion

        #region Dispose Pattern
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try { _facade?.Dispose(); } catch (ObjectDisposedException) { }

                // 組織服務是 gateway 代理：它不屬於 ToolUtilityClass，且可能是程序級
                // Factory 長期持有的 ambient 代理。lease、raw client 與 fallback scope
                // 分別由 Gateway、pool 與 AmbientGatewayOrganizationService 決定性釋放；
                // 此處若 Dispose 代理，會讓其他 request 使用已失效服務，形成跨 request 泄漏。
                // 追蹤資源亦由 DI Singleton 擁有，生命週期等同應用程式。
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region 工具方法
        /// <summary>
        /// 依層級輸出追蹤紀錄。簽章維持不變，既有 160 個呼叫點無須修改。
        /// </summary>
        /// <remarks>
        /// 實際輸出委派給程序級的 <see cref="IToolUtilityTracer"/>。
        /// 此處以 <c>new StackFrame(1, true)</c> 擷取「本方法的呼叫者」並往下傳，
        /// 使輸出的 StackTrace 與重構前完全一致；若改由 tracer 內部擷取，
        /// 框架深度會因多一層委派而位移，導致既有日誌內容改變。
        /// </remarks>
        public void TraceByLevel(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            _tracer.Write(TotalLevel, QualifiedLevel, StringToProcess, new StackFrame(1, true));
        }

        static public void TraceByLevelStatic(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            try
            {
                if (TotalLevel >= QualifiedLevel)
                {
                    Trace.WriteLine("Time            =" + DateTime.Now.ToString() + Environment.NewLine);
                    Trace.WriteLine("StringToProcess =" + StringToProcess + Environment.NewLine);
                    Trace.WriteLine("StackTrace      =" + new StackTrace(new StackFrame(1, true)).ToString() + Environment.NewLine);
                    Trace.WriteLine("================================================================== " + Environment.NewLine);
                }
            }
            catch (Exception e)
            {
                throw e;
            }
        }
        #endregion
    }
}

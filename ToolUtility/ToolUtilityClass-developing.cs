using Line.Messaging.Webhooks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.SqlServer.Server;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using PowerPlatform.Dataverse.Client;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel.Description;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using TraceNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Core;
using static System.Net.WebRequestMethods;

namespace ToolUtility_Developing_NameSpace
{
    /// <summary>
    /// ToolUtilityClass 開發版本 - 作為 ToolUtilityFacade 的包裝類別
    /// 保持向後兼容性,同時委派業務邏輯到新的服務架構
    /// 所有方法都已重構為委派到 ToolUtilityFacade
    /// </summary>
    public class ToolUtilityClass : IDisposable
    {
        #region 資料區
        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365-9.0";

        String m_DiscoveryServiceType = "";

        public IOrganizationService m_Crm2011OrganizationService;
        private bool _disposed = false;

        public OrganizationServiceProxy m_OrganizationService;

        // 連接服務專責處理
        private readonly ICrmConnectionService _crmConnectionService;
        
        // 新架構的 Facade (用於委派複雜業務邏輯)
        private readonly ToolUtilityFacade _facade;

        #region Dynamics 365 組織設定
        #region 聖谷行道會(雲端機房)
        private const String SERVER = "speechmessage.com.tw";
        private const String PORT = "7777";
        private const String ORGANIZATION = "sunnyvalech";
        private const String USERNAME = "Administrator@speechmessage.com.tw";
        private const String PASSWORD = "hu9840";
        private const String DOMAIN = "DYNAMICS-365";
        #endregion

        private String BASE_DISCOVERY_SERVICE_ADDRESS = "/XRMServices/2011/Discovery.svc";
        #endregion

        #region 有效截止日期
        private DateTime ExpireDate = new DateTime(2013, 3, 30);
        #endregion

        #region 常數參數
        private const String FILTERED_PROJECT = "";
        private const int EMPTY_VALUE = -999999999;
        private const bool EXCUTION_FLAG = true;
        private const bool EXCUTION_TRACE_LINE = true;
        
        // 除錯用參數
        private const int TOTAL_LEVEL = 5;
        private const int LEVEL_1 = 1;
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5;
        #endregion

        #region 追蹤專用變數
        private String m_TraceLogFile = "";
        private BugslayerTextWriterTraceListener m_Listener = new BugslayerTextWriterTraceListener();
        private FileStream m_XmlFileStream;
        private StreamWriter m_XmlFileStreamWriter;
        private const String TRACE_DIRECTOR = @"D:\除錯追蹤\" + "CHURCH_REPORT_TRACE.TXT";
        #endregion

        #endregion

        #region 建構式
        public ToolUtilityClass()
        {
            // 初始化連接服務
            _crmConnectionService = new CrmConnectionService();

            #region 追蹤專用變數
            m_TraceLogFile = TRACE_DIRECTOR;
            m_XmlFileStream = new FileStream(m_TraceLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            m_XmlFileStreamWriter = new StreamWriter(m_XmlFileStream, Encoding.GetEncoding("big5"));
            m_Listener = new BugslayerTextWriterTraceListener(m_XmlFileStreamWriter);

            Debug.AutoFlush = true;
            Debug.Listeners.Add(m_Listener);
            #endregion

            // 使用連接服務建立 CRM 連接
            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"SPEECHMESSAGE\Administrator";
            var adPassword = "hu9840";

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);
            
            // 初始化 Facade (用於委派業務邏輯)
            _facade = new ToolUtilityFacade();
        }

        public ToolUtilityClass(String DiscoveryServiceType)
        {
            _crmConnectionService = new CrmConnectionService();
            m_DiscoveryServiceType = DiscoveryServiceType;

            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"Administrator@speechmessage.com.tw";
            var adPassword = "hu9840";

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);
            _facade = new ToolUtilityFacade();
        }

        public ToolUtilityClass(ref bool ValidFlag)
        {
            _crmConnectionService = new CrmConnectionService();

            if (ExpireDate >= DateTime.Today)
            {
                ValidFlag = false;
            }
            
            _facade = new ToolUtilityFacade();
        }

        ~ToolUtilityClass()
        {
        }
        #endregion

        #region 解構式
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _facade?.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region 透過屬性取得實體 - 完全委派到 Facade


        #endregion

        #region 除錯追蹤區
        public void TraceByLevel(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            try
            {
                if (TotalLevel >= QualifiedLevel)
                {
                    Debug.WriteLine("Time            =" + DateTime.Now.ToString());
                    Debug.WriteLine("StringToProcess =" + StringToProcess);
                    StackTrace aStackTraceNextLevel = new StackTrace(new StackFrame(1, true));
                    Debug.WriteLine("StackTrace      =" + aStackTraceNextLevel.ToString());
                    Debug.WriteLine("==================================================================");
                }
            }
            catch (Exception e)
            {
                // Swallow trace errors
            }
        }

        static public void TraceByLevelStatic(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            try
            {
                if (TotalLevel >= QualifiedLevel)
                {
                    Debug.WriteLine("Time            =" + DateTime.Now.ToString());
                    Debug.WriteLine("StringToProcess =" + StringToProcess);
                    StackTrace aStackTraceNextLevel = new StackTrace(new StackFrame(1, true));
                    Debug.WriteLine("StackTrace      =" + aStackTraceNextLevel.ToString());
                    Debug.WriteLine("==================================================================");
                }
            }
            catch
            {
                // Swallow trace errors
            }
        }
        #endregion
    }
}

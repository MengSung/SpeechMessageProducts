using Line.Messaging.Webhooks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.SqlServer.Server;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
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
    /// ToolUtilityClass 開發版本 - 完全委派到 ToolUtilityFacade
    /// 保持向後兼容性,所有業務邏輯委派到新的服務架構
    /// </summary>
    public class ToolUtilityClass : IDisposable
    {
        #region 資料區
        private const String CRM_TYPE = "DYNAMICS365-9.0";

        String m_DiscoveryServiceType = "";
        public IOrganizationService m_Crm2011OrganizationService;
        private bool _disposed = false;
        public OrganizationServiceProxy m_OrganizationService;

        private readonly ICrmConnectionService _crmConnectionService;
        private readonly ToolUtilityFacade _facade;

        #region Dynamics 365 組織設定
        private const String SERVER = "speechmessage.com.tw";
        private const String PORT = "7777";
        private const String ORGANIZATION = "sunnyvalech";
        private const String USERNAME = "Administrator@speechmessage.com.tw";
        private const String PASSWORD = "hu9840";
        private const String DOMAIN = "DYNAMICS-365";
        private String BASE_DISCOVERY_SERVICE_ADDRESS = "/XRMServices/2011/Discovery.svc";
        #endregion

        private DateTime ExpireDate = new DateTime(2013, 3, 30);

        #region 常數參數
        private const String FILTERED_PROJECT = "";
        private const int EMPTY_VALUE = -999999999;
        private const bool EXCUTION_FLAG = true;
        private const bool EXCUTION_TRACE_LINE = true;
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
            _crmConnectionService = new CrmConnectionService();

            #region 追蹤專用變數
            m_TraceLogFile = TRACE_DIRECTOR;
            m_XmlFileStream = new FileStream(m_TraceLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            m_XmlFileStreamWriter = new StreamWriter(m_XmlFileStream, Encoding.GetEncoding("big5"));
            m_Listener = new BugslayerTextWriterTraceListener(m_XmlFileStreamWriter);
            Debug.AutoFlush = true;
            Debug.Listeners.Add(m_Listener);
            #endregion

            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"SPEECHMESSAGE\Administrator";
            var adPassword = "hu9840";

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);
            
            _facade = new ToolUtilityFacade(m_Crm2011OrganizationService);
        }

        public ToolUtilityClass(String DiscoveryServiceType)
        {
            _crmConnectionService = new CrmConnectionService();
            m_DiscoveryServiceType = DiscoveryServiceType;

            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"Administrator@speechmessage.com.tw";
            var adPassword = "hu9840";

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);
            
            _facade = new ToolUtilityFacade(m_Crm2011OrganizationService);
        }

        public ToolUtilityClass(ref bool ValidFlag)
        {
            _crmConnectionService = new CrmConnectionService();

            if (ExpireDate >= DateTime.Today)
            {
                ValidFlag = false;
            }

            // 初始化 organizationService (即使可能為 null)
            _facade = new ToolUtilityFacade(m_Crm2011OrganizationService);
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
                m_XmlFileStreamWriter?.Dispose();
                m_XmlFileStream?.Dispose();
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region 完全委派到 Facade 的方法
        #region 基本實體操作 (委派到 Facade)
        public Entity RetrieveEntity(string entityName, Guid entityId)
            => _facade.RetrieveEntity(entityName, entityId);

        public Guid CreateEntity(Entity entityToCreate)
            => _facade.CreateEntity(entityToCreate);
        #endregion

        #region 處理字串 ( 委派到 Facade，委派到 StringUtility)
        static public void DeleteLastComma(ref String StringToProcess)
        {
            try
            {
                ToolUtilityFacade.DeleteLastComma(ref StringToProcess);
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        static public void DeleteLastChar(ref String StringToProcess)
        {
            try
            {
                ToolUtilityNameSpace.Utilities.StringUtility.DeleteLastChar(ref StringToProcess);
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        static public String DeletePresentRate(String StringToProcess)
        {
            try
            {
                return ToolUtilityNameSpace.Utilities.StringUtility.DeletePresentRate(StringToProcess);
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        public String TrimPresentRate(String StringToProcess)
        {
            try
            {
                return ToolUtilityNameSpace.Utilities.StringUtility.TrimPresentRate(StringToProcess);
            }
            catch (System.Exception e)
            {
                throw e;
            }
        }

        #region 過濾出數字字串
        public String FilterDigit(String aFilteredString)
        {
            try
            {
                return _facade.FilterDigit(aFilteredString);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion

        #endregion


        #region 除錯追蹤區 (委派到 Facade)
        public void TraceByLevel(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
            => _facade.TraceByLevel(TotalLevel, QualifiedLevel, StringToProcess);

        static public void TraceByLevelStatic(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
            => ToolUtilityFacade.TraceByLevelStatic(TotalLevel, QualifiedLevel, StringToProcess);
        #endregion

        #endregion
    }
}


using Line.Messaging.Webhooks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.SqlServer.Server;
// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
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
using ToolUtilityNameSpace.ConnectionOperations;
using ToolUtilityNameSpace.Core;
using TraceNameSpace;
using static System.Net.WebRequestMethods;

namespace ToolUtilityNameSpace
{
    public class ToolUtilityClass
    {
        #region 資料區
        //private const String CRM_TYPE = "CRM2011";
        private const String CRM_TYPE = "DYNAMICS365-9.0";

        String m_DiscoveryServiceType = "";

        public IOrganizationService m_Crm2011OrganizationService;
        private bool _disposed = false;

        // public OrganizationServiceProxy m_OrganizationProxy;
        public OrganizationServiceProxy m_OrganizationService;

        // 連接服務專責處理
        private readonly ICrmConnectionService _crmConnectionService;

        // 新架構的 Facade (用於委派複雜業務邏輯)
        private readonly ToolUtilityFacade _facade;

        #region Dynamics 365 新增組織修改區

        // 客製化
        #region 聖谷行道會(雲端機房)
        private const String SERVER = "speechmessage.com.tw";
        private const String PORT = "7777";
        private const String ORGANIZATION = "sunnyvalech";
        private const String USERNAME = "Administrator@speechmessage.com.tw";
        private const String PASSWORD = "hu9840";
        private const String DOMAIN = "DYNAMICS-365";
        #endregion

        #region 聖谷行道會(公司內部發展)
        //private const String SERVER = "speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "sunnyvalechback";
        //private const String USERNAME = "Administrator@speechmessage.com.tw";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        #region 僅供參考區塊
        //private String _discoveryServiceAddress = "https://tpehoc.speechmessage.com.tw/XRMServices/2011/Discovery.svc";
        private String BASE_DISCOVERY_SERVICE_ADDRESS = "/XRMServices/2011/Discovery.svc";
        //private String _organizationUniqueName = "tpehoc";
        // Provide your user name and password.
        //private String _userName = "administrator@speechmessage.com.tw";
        //private String _password = "hu9840";

        // Provide domain name for the On-Premises org.
        //private String _domain = "DYNAMICS-365";
        #endregion

        #endregion Class Level Members

        #region 有效截止日期
        private DateTime ExpireDate = new DateTime(2013, 3, 30);
        //private DateTime ExpireDate = new DateTime( 2012, 1, 28 );
        #endregion
        #region 常數參數
        #region 一般常數參數

        private const String FILTERED_PROJECT = ""; // 不過濾建案

        private const int EMPTY_VALUE = -999999999;

        // 是否真的執行 CRM 2011 的 新增、修改、刪除
        private const bool EXCUTION_FLAG = true;
        // 是否真的執行 追蹤Line訊息量
        private const bool EXCUTION_TRACE_LINE = true;

        #endregion
        #region 除錯用參數
        private const int TOTAL_LEVEL = 5;//改變這個值，就會改追蹤的階層，若是 TOTAL_LEVEL = 3 ，則大於 3 的 LEVEL，例如 : LEVEL_4、LEVEL_5 就不會被追蹤
        private const int LEVEL_1 = 1; // 比較容易被看到的，可能是比較大範圍的部分
        private const int LEVEL_2 = 2;
        private const int LEVEL_3 = 3;
        private const int LEVEL_4 = 4;
        private const int LEVEL_5 = 5; // 比較不會被看到的，可能是比較細節的部分
                                       // 如果 TRACE_LEVEL >= TRACE_LEVEL_GROUND 就會進行追蹤
                                       // 如果 TRACE_LEVEL < TRACE_LEVEL_GROUND 就不會進行追蹤
                                       //int TRACE_LEVEL = 5;
                                       //int TRACE_LEVEL_GROUND = 3;
        #endregion

        #endregion
        #region 追蹤專用變數
        private String m_TraceLogFile = "";
        private BugslayerTextWriterTraceListener m_Listener = new BugslayerTextWriterTraceListener();
        private FileStream m_XmlFileStream;
        private StreamWriter m_XmlFileStreamWriter;
        private const String TRACE_DIRECTOR = @"D:\除錯追蹤\" + "CHURCH_REPORT_TRACE.TXT";
        //private const String TRACE_DIRECTOR = @"C:\除錯追蹤\" + "TRACE.TXT";
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
            var adUrl = $"https://{SERVER}:{PORT}/{ORGANIZATION}/XRMServices/2011/Organization.svc";
            var adUsername = @"SPEECHMESSAGE\Administrator";
            var adPassword = PASSWORD;

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);

            // 初始化 Facade,傳入 IOrganizationService
            _facade = new ToolUtilityFacade(null, m_Crm2011OrganizationService);
        }

        public ToolUtilityClass(String DiscoveryServiceType)
        {
            // 初始化連接服務
            _crmConnectionService = new CrmConnectionService();

            m_DiscoveryServiceType = DiscoveryServiceType;

            // 使用連接服務建立 CRM 連接
            var adUrl = $"https://{SERVER}:{PORT}/{ORGANIZATION}/XRMServices/2011/Organization.svc";
            var adUsername = @"Administrator@speechmessage.com.tw";
            var adPassword = PASSWORD;

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);

            // 初始化 Facade,傳入 IOrganizationService
            _facade = new ToolUtilityFacade(null, m_Crm2011OrganizationService);
        }

        public ToolUtilityClass(ref bool ValidFlag)
        {
            // 初始化連接服務
            _crmConnectionService = new CrmConnectionService();

            // 先建立連線後再初始化 Facade
            var adUrl = $"https://{SERVER}:{PORT}/{ORGANIZATION}/XRMServices/2011/Organization.svc";
            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, DOMAIN + "\\Administrator", PASSWORD);

            // 初始化 Facade,傳入 IOrganizationService
            _facade = new ToolUtilityFacade(null, m_Crm2011OrganizationService);

            if (ExpireDate >= DateTime.Today)
            {
                ValidFlag = false;
            }
        }

        ~ToolUtilityClass()
        {
        }
        #endregion
        #region 解構式
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            // Free any unmanaged objects here.
            //this.m_OrganizationService.Dispose();

            _disposed = true;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region 連接 CRM 2011 服務
        /// <summary>
        /// 取得 Windows 認證憑證 (委派給 CrmConnectionService)
        /// </summary>
        public ClientCredentials GetClientCredentials(String Domain, String UserName, String Password)
        {
            return _crmConnectionService.GetClientCredentials(Domain, UserName, Password);
        }

        /// <summary>
        /// 取得 CRM Organization Service (委派給 CrmConnectionService)
        /// </summary>
        public IOrganizationService GetOrganizationService(String Server, String Port, String Organization, String Domain, String UserName, String Password)
        {
            return _crmConnectionService.GetOrganizationService(Server, Port, Organization, Domain, UserName, Password);
        }

        /// <summary>
        /// 取得預設的 Windows 認證憑證 (委派給 CrmConnectionService)
        /// </summary>
        public ClientCredentials GetClientCredentials()
        {
            return _crmConnectionService.GetClientCredentials(DOMAIN, USERNAME, PASSWORD);
        }

        /// <summary>
        /// 設定 CRM 2011 Organization Service (委派給 CrmConnectionService)
        /// </summary>
        public IOrganizationService SetOrganizationService()
        {
            m_Crm2011OrganizationService = _crmConnectionService.SetOrganizationService(
                SERVER, PORT, ORGANIZATION, DOMAIN, USERNAME, PASSWORD);
            return m_Crm2011OrganizationService;
        }

        /// <summary>
        /// 設定 Claims-Based 認證的 Organization Service (委派給 CrmConnectionService)
        /// </summary>
        public IOrganizationService SetClaimsBasedAuthenticationOrganizationService()
        {
            m_Crm2011OrganizationService = _crmConnectionService.SetClaimsBasedAuthenticationOrganizationService(
                ORGANIZATION, SERVER, DOMAIN, USERNAME, PASSWORD);
            return m_Crm2011OrganizationService;
        }

        /// <summary>
        /// 除錯用的 Claims-Based 認證設定 (委派給 CrmConnectionService)
        /// </summary>
        public String SetClaimsBasedAuthenticationOrganizationService_DEBUG()
        {
            String DebugString = "";

            try
            {
                DebugString += "001 - 開始建立連接" + Environment.NewLine;

                m_Crm2011OrganizationService = _crmConnectionService.SetClaimsBasedAuthenticationOrganizationService(
                    ORGANIZATION, SERVER, DOMAIN, USERNAME, PASSWORD);

                DebugString += "002 - 連接成功建立" + Environment.NewLine;

                // 驗證連接
                if (_crmConnectionService.ValidateConnection(m_Crm2011OrganizationService))
                {
                    DebugString += "003 - 連接驗證成功" + Environment.NewLine;
                }
                else
                {
                    DebugString += "003 - 連接驗證失敗" + Environment.NewLine;
                }

                DebugString += "004 - 完成" + Environment.NewLine;
            }
            catch (Exception ex)
            {
                DebugString += $"ERROR - 連接失敗: {ex.Message}" + Environment.NewLine;
            }

            return DebugString;
        }
        #endregion

        #region 連接 Dynamics 365 服務
        /// <summary>
        /// 設定 Federated Organization Proxy (委派給 CrmConnectionService)
        /// 用於 Dynamics 365 Online 和 On-Premise IFD 環境
        /// </summary>
        public OrganizationServiceProxy SetFederatedOrganizationProxy(String DiscoveryServiceType)
        {
            m_OrganizationService = _crmConnectionService.SetFederatedOrganizationProxy(
                DiscoveryServiceType,
                ORGANIZATION,
                SERVER,
                PORT,
                BASE_DISCOVERY_SERVICE_ADDRESS,
                USERNAME,
                PASSWORD,
                DOMAIN);

            return m_OrganizationService;
        }

        /// <summary>
        /// 探索使用者所屬的組織 (委派給 CrmConnectionService)
        /// </summary>
        public OrganizationDetailCollection DiscoverOrganizations(IDiscoveryService service)
        {
            return _crmConnectionService.DiscoverOrganizations(service);
        }

        /// <summary>
        /// 在組織列表中尋找特定組織 (委派給 CrmConnectionService)
        /// </summary>
        public OrganizationDetail FindOrganization(string orgUniqueName, OrganizationDetail[] orgDetails)
        {
            return _crmConnectionService.FindOrganization(orgUniqueName, orgDetails);
        }

        #endregion
        #region 透過屬性取得實體
        #region 取得一般實體
        public Entity RetrieveEntityByField(String EntityName, String FieldName, String FieldValue)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute(EntityName);
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange(FieldName, "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(FieldValue, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    return retrieved.Entities[0];
                }
                else { return null; }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveEntityCollectionByField(String EntityName, String FieldName, String FieldValue)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute(EntityName);
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange(FieldName, "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(FieldValue, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    return retrieved;
                }
                else { return null; }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion        
        #region 取得聯絡人
        //private readonly object m_RetrieveContactLocker = new object();
        public String RetrieveContactByContactId(String ContactId)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("build_customer_id", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(ContactId, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                //Console.WriteLine("除錯 003");

                String ContactInformation = "";

                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    if (retrieved.Entities[0].Attributes.Contains("fullname"))
                    {
                        ContactInformation += "姓名:" + retrieved.Entities[0].Attributes["fullname"].ToString() + Environment.NewLine;
                    }
                    if (retrieved.Entities[0].Attributes.Contains("build_customer_id"))
                    {
                        ContactInformation += "身分證字號:" + retrieved.Entities[0].Attributes["build_customer_id"].ToString() + Environment.NewLine;
                    }
                    if (retrieved.Entities[0].Attributes.Contains("telephone1"))
                    {
                        ContactInformation += "電話號碼:" + retrieved.Entities[0].Attributes["telephone1"].ToString() + Environment.NewLine;
                    }
                    if (retrieved.Entities[0].Attributes.Contains("emailaddress1"))
                    {
                        ContactInformation += "電子郵件:" + retrieved.Entities[0].Attributes["emailaddress1"].ToString() + Environment.NewLine;
                    }
                }
                ContactInformation += Environment.NewLine;
                //Console.WriteLine("除錯 004");

                return ContactInformation;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity RetrieveContactByContactId(ref IOrganizationService aOrganizationService, String ContactId, ref int Count)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("build_customer_id", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(ContactId, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved = aOrganizationService.RetrieveMultiple(querybyexpression);


                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    Count = retrieved.Entities.Count;
                    return retrieved.Entities[0];
                }
                else
                {
                    Count = retrieved.Entities.Count;
                    return null;
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public String RetrieveContactByName(String ContactFullName)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("fullname", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(ContactFullName, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                String ContactInformation = "";

                //Console.WriteLine("除錯 003");
                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    Entity aEntity = retrieved.Entities[0];

                    if (retrieved.Entities[0].Attributes.Contains("fullname"))
                    {
                        ContactInformation += "姓名:" + retrieved.Entities[0].Attributes["fullname"].ToString() + Environment.NewLine;
                    }
                    if (retrieved.Entities[0].Attributes.Contains("build_customer_id"))
                    {
                        ContactInformation += "身分證字號:" + retrieved.Entities[0].Attributes["build_customer_id"].ToString() + Environment.NewLine;
                    }
                    if (retrieved.Entities[0].Attributes.Contains("telephone1"))
                    {
                        ContactInformation += "電話號碼:" + retrieved.Entities[0].Attributes["telephone1"].ToString() + Environment.NewLine;
                    }
                    if (retrieved.Entities[0].Attributes.Contains("emailaddress1"))
                    {
                        ContactInformation += "電子郵件:" + retrieved.Entities[0].Attributes["emailaddress1"].ToString() + Environment.NewLine;
                    }
                }
                ContactInformation += Environment.NewLine;
                //Console.WriteLine("除錯 004");

                return ContactInformation;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity RetrieveContactEntityByName(String ContactFullName)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("fullname", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(ContactFullName, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                String ContactInformation = "";

                //Console.WriteLine("除錯 003");
                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    return retrieved.Entities[0];
                }
                else
                {
                    return null;
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity RetrieveContactByName(ref IOrganizationService aOrganizationService, String ContactFullName)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute

                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("fullname", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(ContactFullName, 0);

                //  Query passed to the service proxy
                EntityCollection retrieved = aOrganizationService.RetrieveMultiple(querybyexpression);


                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    return retrieved.Entities[0];
                }
                else
                {
                    return null;
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public String RetrieveContactByName_ReturnString(ref IOrganizationService aOrganizationService, String ContactFullName)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{

                //  Create query using querybyattribute
                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("fullname", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(ContactFullName, 0);

                //  Query passed to the service proxy
                EntityCollection retrieved = aOrganizationService.RetrieveMultiple(querybyexpression);


                String ContactInformation = "";

                foreach (var c in retrieved.Entities)
                {
                    if (c.Attributes["fullname"] != null)
                    {
                        ContactInformation += "姓名:" + c.Attributes["fullname"] + Environment.NewLine;
                    }
                    if (c.Attributes["telephone1"] != null)
                    {
                        ContactInformation += "電話號碼:" + c.Attributes["telephone1"] + Environment.NewLine;
                    }
                    if (c.Attributes["emailaddress1"] != null)
                    {
                        ContactInformation += "電子郵件:" + c.Attributes["emailaddress1"] + Environment.NewLine;
                    }
                }
                ContactInformation += Environment.NewLine;

                return ContactInformation;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveContactCollectionByName(String ContactFullName)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("fullname", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(ContactFullName, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    return retrieved;
                }
                else { return null; }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveContactCollectionByNationId(String ContactFullName)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("new_personal_id", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(ContactFullName, 0);

                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }


                return retrieved;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity RetrieveContactByLineId(String LineId)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("new_lineid", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(LineId, 0);

                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (this.m_DiscoveryServiceType == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                if (retrieved.Entities.Count > 0)
                {
                    return retrieved.Entities[0];
                }
                else
                {
                    return null;
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public String RetrieveContactByAccountNumber(String AccountNumber, String aPassword)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                //querybyexpression.Attributes.AddRange("new_account", "statecode");
                //聖谷行道會小組長帳號
                querybyexpression.Attributes.AddRange("new_app_acount", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(AccountNumber, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (this.m_DiscoveryServiceType == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                //Console.WriteLine("除錯 003");
                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    Entity aEntity = retrieved.Entities[0];

                    //if (retrieved.Entities[0].Attributes.Contains("new_password"))
                    if (retrieved.Entities[0].Attributes.Contains("new_app_pass"))
                    {
                        //String aContactPassword = retrieved.Entities[0].Attributes["new_password"].ToString();
                        String aContactPassword = retrieved.Entities[0].Attributes["new_app_pass"].ToString();
                        if (aContactPassword == aPassword)
                        {
                            this.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "小組長:" + retrieved.Entities[0].Attributes["fullname"].ToString());
                            //return retrieved.Entities[0].Attributes["fullname"].ToString();
                            return retrieved.Entities[0].Attributes["contactid"].ToString();
                        }
                        else
                        {
                            this.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "密碼錯誤");
                            return "密碼錯誤";
                        }
                    }
                    else
                    {
                        this.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "系統沒有設定密碼");
                        return "系統沒有設定密碼";
                    }
                }
                else
                {
                    this.TraceByLevel(TOTAL_LEVEL, LEVEL_1, "帳號錯誤");
                    return "帳號錯誤";
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity DoesAccountExist(String AccountNumber)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                //querybyexpression.Attributes.AddRange("new_account", "statecode");
                //聖谷行道會小組長帳號
                querybyexpression.Attributes.AddRange("new_app_acount", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(AccountNumber, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (this.m_DiscoveryServiceType == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                //Console.WriteLine("除錯 003");
                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    // 已經有帳號
                    return retrieved[0];
                }
                else
                {
                    // 帳號還不存在
                    return null;
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity RetrieveContactEntityByAccountNumber(String AccountNumber, String aPassword)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                //querybyexpression.Attributes.AddRange("new_account", "statecode");
                //聖谷行道會小組長帳號
                querybyexpression.Attributes.AddRange("new_app_acount", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(AccountNumber, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                //Console.WriteLine("除錯 003");
                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    Entity aEntity = retrieved.Entities[0];

                    //if (retrieved.Entities[0].Attributes.Contains("new_password"))
                    if (retrieved.Entities[0].Attributes.Contains("new_app_pass"))
                    {
                        //String aContactPassword = retrieved.Entities[0].Attributes["new_password"].ToString();
                        String aContactPassword = retrieved.Entities[0].Attributes["new_app_pass"].ToString();
                        if (aContactPassword == aPassword)
                        {
                            return retrieved.Entities[0];
                        }
                        else
                        {
                            return null;
                        }
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity RetrieveContactEntityByLineUserId(String LineUserId)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                //querybyexpression.Attributes.AddRange("new_account", "statecode");
                //聖谷行道會小組長帳號
                querybyexpression.Attributes.AddRange("new_lineid", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(LineUserId, 0);

                //Console.WriteLine("除錯 002");
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                //Console.WriteLine("除錯 003");
                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    Entity aEntity = retrieved.Entities[0];

                    return retrieved.Entities[0];
                    //if (retrieved.Entities[0].Attributes.Contains("new_password"))
                }
                else
                {
                    return null;
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity RetrieveContactEntityByFullNameAndMobileNumber(String FullName, String MobileNumber)
        {   // 依據全名及行動電話找尋連絡人
            try
            {
                //lock (m_RetrieveContactLocker)
                //{

                //  Create query using querybyattribute
                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                //querybyexpression.Attributes.AddRange("new_account", "statecode");
                // 
                querybyexpression.Attributes.AddRange("fullname", "mobilephone", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(FullName, MobileNumber, 0);
                //  Query passed to the service proxy
                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    retrieved = this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }

                if (retrieved.Entities.Count > 0 && retrieved != null)
                {
                    return retrieved.Entities[0];
                }
                else
                {
                    return null;
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveContactEntityByFullNameCollection(String FullName)
        {   // 依據全名及行動電話找尋連絡人
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                //querybyexpression.Attributes.AddRange("new_account", "statecode");
                // 
                querybyexpression.Attributes.AddRange("fullname", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(FullName, 0);
                //  Query passed to the service proxy
                if (CRM_TYPE == "DYNAMICS365")
                {
                    return this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                }
                else
                {
                    return this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveMemberListCollectionByListId(Guid aListId)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{

                QueryByAttribute query = new QueryByAttribute("listmember");
                query.AddAttributeValue("listid", aListId);
                query.ColumnSet = new ColumnSet(true);

                #region// 根據建立時間排序後傳回來
                //OrderExpression OrderBySunday = new OrderExpression();
                //OrderBySunday.AttributeName = "new_sunday_date";
                ////OrderBySunday.OrderType = OrderType.Ascending;
                //OrderBySunday.OrderType = OrderType.Descending;
                //////OrderBySerial.OrderType = OrderType.Descending;
                //query.Orders.Add(OrderBySunday);
                #endregion

                EntityCollection entityCollection;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    entityCollection = this.m_OrganizationService.RetrieveMultiple(query);
                }
                else
                {
                    entityCollection = this.m_Crm2011OrganizationService.RetrieveMultiple(query);

                }
                return entityCollection;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveDynamicMemberListDynamics365(Guid aListId)
        {
            ColumnSet cols = new ColumnSet(new string[] { "query" });

            // GUID of the Dynamic Marketing List
            Entity entity;
            if (CRM_TYPE == "DYNAMICS365")
            {
                entity = this.m_OrganizationService.Retrieve("list", aListId, cols);

            }
            else
            {
                entity = this.m_Crm2011OrganizationService.Retrieve("list", aListId, cols);
            }

            var dynamicQuery = entity.Attributes["query"].ToString();

            EntityCollection dynamicmemberec;
            if (CRM_TYPE == "DYNAMICS365")
            {
                dynamicmemberec = this.m_OrganizationService.RetrieveMultiple(new FetchExpression(dynamicQuery));
            }
            else
            {
                dynamicmemberec = this.m_Crm2011OrganizationService.RetrieveMultiple(new FetchExpression(dynamicQuery));
            }
            return dynamicmemberec;
        }
        #endregion
        #region 透過FetchXml取得實體或是集合
        #region 取得學員上課記錄
        public EntityCollection RetrieveStorLessonsByFetchXml(String ContactName, String ContactId)
        {
            try
            {
                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false>
                          <entity name='new_stor_lessons'>
                            <attribute name='createdon' />
                            <attribute name='new_contact_new_stor_lessons' />
                            <attribute name='new_fee' />
                            <attribute name='new_pay_date' />
                            <attribute name='new_current_complete' />
                            <attribute name='new_new_disciple_lessons_new_stor_les' />
                            <attribute name='new_stor_lessonsid' />
                            <order attribute='new_new_disciple_lessons_new_stor_les' descending='false' />
                            <order attribute='new_contact_new_stor_lessons' descending='false' />
                            <filter type='and'>
                                <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
                            </filter>
                            <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='a_45d999afd4cc4001b091647bb91668ef'>
                              <attribute name='telephone2' />
                              <attribute name='address2_line1' />
                              <attribute name='parentcustomerid' />
                              <attribute name='mobilephone' />
                              <attribute name='emailaddress1' />
                            </link-entity>
                            <link-entity name='new_disciple_lessons' from='new_disciple_lessonsid' to='new_new_disciple_lessons_new_stor_les' alias='ab'>
                              <filter type='and'>
                                <condition attribute='new_classification' operator='in'>
                                  <value>100000000</value>
                                  <value>100000001</value>
                                </condition>
                              </filter>
                            </link-entity>
                          </entity>
                        </fetch>";

                //<condition attribute = 'new_contact_new_stor_lessons' operator= 'eq' uiname = " + ContactName + @" uitype = 'contact' value = " + ContactId + @
                //< condition attribute = 'new_contact_new_stor_lessons' operator= 'eq' uiname = " + ContactName + @" uitype = 'contact' value = " + ContactId + @" />
                //<condition attribute='new_contact_new_stor_lessons' operator='eq' uiname='林寬仁' uitype='contact' value='{36E57E1C-900B-F011-8143-00155D006608}' />


                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                if (CRM_TYPE == "DYNAMICS365")
                {
                    return ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    return ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveStorLessonsByDiscipleLessonsFetchXml(String LessonName, String LessonId)
        {
            try
            {
                LessonName = @"'" + LessonName + @"'";
                LessonId = @"'{" + LessonId + @"}'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false>
                      <entity name='new_stor_lessons'>
                        <attribute name='createdon' />
                        <attribute name='new_contact_new_stor_lessons' />
                        <attribute name='new_fee' />
                        <attribute name='new_pay_date' />
                        <attribute name='new_current_complete' />
                        <attribute name='new_new_disciple_lessons_new_stor_les' />
                        <attribute name='new_stor_lessonsid' />
                        <order attribute='new_new_disciple_lessons_new_stor_les' descending='false' />
                        <order attribute='new_contact_new_stor_lessons' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_enroll_status' operator='not-in'>
                            <value>100000007</value>
                            <value>100000009</value>
                            <value>100000003</value>
                          </condition>
                          <condition attribute='new_new_disciple_lessons_new_stor_les' operator='eq' uiname=" + LessonName + @" uitype='new_disciple_lessons' value=" + LessonId + @" />
                          <condition attribute='statuscode' operator='ne' value='2' />
                          <condition attribute='statecode' operator='eq' value='0' />
                        </filter>
                        <link-entity name='contact' from='contactid' to='new_contact_new_stor_lessons' visible='false' link-type='outer' alias='a_45d999afd4cc4001b091647bb91668ef'>
                          <attribute name='telephone2' />
                          <attribute name='address2_line1' />
                          <attribute name='parentcustomerid' />
                          <attribute name='mobilephone' />
                          <attribute name='emailaddress1' />
                        </link-entity>
                      </entity>
                    </fetch>";


                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                if (CRM_TYPE == "DYNAMICS365")
                {
                    return ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    return ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #region 取得認獻
        public EntityCollection RetrieveDedicationBookingByFetchXml(String ContactName, String ContactId)
        {
            try
            {
                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";


                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false>
                          <entity name='new_dedication_booking'>
                            <attribute name='new_dedication_bookingid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_contact_new_dedication_booking' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
                              <condition attribute='new_dedication_booking_status' operator='eq' value='100000001' />
                            </filter>
                          </entity>
                        </fetch>";


                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                if (CRM_TYPE == "DYNAMICS365")
                {
                    return ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    return ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #region 取得聚會統計紀錄
        /// <summary>
        /// 特定連絡人已報名的課程
        /// </summary>
        /// <param name="StartDate"></param>
        /// <param name="EndDate"></param>
        /// <returns></returns>
        public EntityCollection RetrieveMeetingStatisticsByFetchXml(DateTime SundayDate)
        {
            try
            {
                string SundayDateString = @"'" + SundayDate.Year + "-" + SundayDate.Month + "-" + SundayDate.Day + @"'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false>
                          <entity name='new_meeting_statistics'>
                            <attribute name='new_meeting_statisticsid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='statuscode' operator='eq' value='1' />
                             <condition attribute='new_sunday_date' operator='on' value=" + SundayDateString + @" />
                            </filter>
                          </entity>
                        </fetch>";

                RetrieveMultipleRequest fetchRequest = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest)).EntityCollection;
                }
                else
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest)).EntityCollection;
                }


                return retrieved;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #region 取得收費單
        public EntityCollection RetrieveFeeByFetchXml(String DedicationBookingName, String DedicationBookingId, String PaidPeriod)
        {
            try
            {
                DedicationBookingName = @"'" + DedicationBookingName + @"'";
                DedicationBookingId = @"'{" + DedicationBookingId + @"}'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false>
                          <entity name='new_fee'>
                            <attribute name='new_feeid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_dedication_booking_new_fee' operator='eq' uiname=" + DedicationBookingName + @" uitype ='new_dedication_booking' value=" + DedicationBookingId + @" />
                              <condition attribute='new_paid_period' operator='eq' value='" + PaidPeriod + @"' />
                            </filter>
                          </entity>
                        </fetch>";

                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                if (CRM_TYPE == "DYNAMICS365")
                {
                    return ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    return ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #region 取得各類名單
        public EntityCollection RetrieveListByFetchXml()
        {
            try
            {
                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='true' />
                        <filter type='and'>
                          <condition attribute='statuscode' operator='eq' value='0' />
                          <condition attribute='purpose' operator='eq' value='小組名單' />
                          <condition attribute='new_app_named' operator='eq' value='1' />
                        </filter>
                      </entity>
                    </fetch>";

                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                if (CRM_TYPE == "DYNAMICS365")
                {
                    return ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    return ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveSmallGroupListCollectionByFetchXml()
        {
            try
            {
                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='new_contact_race_leager_list' />
                        <attribute name='new_contact_family_leader_list' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='true' />
                        <filter type='and'>
                          <condition attribute='new_app_named' operator='eq' value='1' />
                          <condition attribute='statuscode' operator='eq' value='0' />
                          <condition attribute='purpose' operator='eq' value='小組名單' />
                          <condition attribute='listname' operator='not-like' value='%幸福%' />
                        </filter>
                      </entity>
                    </fetch>";


                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                if (CRM_TYPE == "DYNAMICS365")
                {
                    return ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    return ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #region 搜尋 N:1 的集合
        //private readonly object m_QueryManyToOneLocker = new object();
        public EntityCollection RetrieveManyToOneCollection()
        {
            try
            {
                //lock (m_QueryManyToOneLocker)
                //{
                Guid acctId = new Guid("B2071325-B861-E011-9E82-001D60789032");
                // Condition where task attribute equals account id. 
                ConditionExpression condition = new ConditionExpression();
                condition.AttributeName = "regardingobjectid";
                condition.Operator = ConditionOperator.Equal;
                condition.Values.Add(acctId.ToString());

                //Create a column set.
                ColumnSet columns = new ColumnSet("subject");

                // Create query expression.
                QueryExpression query1 = new QueryExpression();
                query1.ColumnSet = columns;
                query1.EntityName = "task";
                query1.Criteria.AddCondition(condition);

                EntityCollection result1;

                if (CRM_TYPE == "DYNAMICS365")
                {
                    result1 = this.m_OrganizationService.RetrieveMultiple(query1);
                }
                else
                {
                    result1 = this.m_Crm2011OrganizationService.RetrieveMultiple(query1);
                }


                return result1;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity QueryBloodReportByContactId(Guid ContactId)
        {
            try
            {
                // Create a ConditionExpression.
                ConditionExpression ContactConditionPrincipal = new ConditionExpression();

                // Set the ConditionExpressions properties so that the condition is true when the 
                // ownerid of the account equals the principalId.
                ContactConditionPrincipal.AttributeName = "objectid";
                ContactConditionPrincipal.Operator = ConditionOperator.Equal;

                ContactConditionPrincipal.Values.Add(ContactId.ToString());


                // 建立 Filter
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(ContactConditionPrincipal);
                
                // 建立 QueryExpression
                QueryExpression query = new QueryExpression();
                query.EntityName = "annotation";
                query.ColumnSet.AllColumns = true;

                query.Criteria = filter;
                
                // 建立 Retrieve Multiple Request
                // 使用正確的 OrganizationService
                if (CRM_TYPE == "DYNAMICS365")
                {
                    return this.m_OrganizationService.RetrieveMultiple(query).Entities.FirstOrDefault();
                }
                else
                {
                    return this.m_Crm2011OrganizationService.RetrieveMultiple(query).Entities.FirstOrDefault();
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #region 處理字串
        static public void DeleteLastComma(ref String StringToProcess)
        {
            try
            {
                // 去掉最後一個逗號
                int Length = StringToProcess.LastIndexOf("，");
                if (Length > 0)
                {
                    StringToProcess = StringToProcess.Substring(0, Length);
                }
            }
            catch (System.Exception e)
            {
                //String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        static public void DeleteLastChar(ref String StringToProcess)
        {
            try
            {
                // 去掉最後一個逗號
                int Length = StringToProcess.Length;
                if (Length > 0)
                {
                    StringToProcess = StringToProcess.Substring(0, Length - 1);
                }
            }
            catch (System.Exception e)
            {
                //String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        static public String DeletePresentRate(String StringToProcess)
        {
            try
            {
                String SpotKeyString = "-主日出席率:";

                int StartPosition = StringToProcess.IndexOf(SpotKeyString);

                String LeftString = "";
                if (StartPosition > 0)
                {
                    LeftString = StringToProcess.Substring(0, StartPosition);
                    return LeftString;
                }
                else
                {
                    return StringToProcess;
                }
            }
            catch (System.Exception e)
            {
                //String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public String TrimPresentRate(String StringToProcess)
        {
            try
            {
                String SpotKeyString = "-主日出席率:";

                int StartPosition = StringToProcess.IndexOf(SpotKeyString);

                String LeftString = "";
                if (StartPosition > 0)
                {
                    LeftString = StringToProcess.Substring(0, StartPosition);
                    return LeftString;
                }
                else
                {
                    return StringToProcess;
                }
            }
            catch (System.Exception e)
            {
                //String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #region 過濾出數字字串
        public String FilterDigit(String aFilteredString)
        {
            try
            {
                //lock (m_OtherLocker)
                {
                    Regex DigitsOnly = new Regex(@"[^\d]");

                    return DigitsOnly.Replace(aFilteredString, "");
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion

        #endregion
        #region 除錯追蹤區
        public void TraceByLevel(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            try
            {
                if (TotalLevel >= QualifiedLevel)
                {
                    Debug.WriteLine("Time            =" + DateTime.Now.ToString() + Environment.NewLine);
                    Debug.WriteLine("StringToProcess =" + StringToProcess + Environment.NewLine);
                    StackTrace aStackTraceNextLevel = new StackTrace(new StackFrame(1, true));
                    Debug.WriteLine("StackTrace      =" + aStackTraceNextLevel.ToString() + Environment.NewLine);
                    Debug.WriteLine("================================================================== " + Environment.NewLine);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        static public void TraceByLevelStatic(Int32 TotalLevel, Int32 QualifiedLevel, String StringToProcess)
        {
            try
            {
                if (TotalLevel >= QualifiedLevel)
                {
                    Debug.WriteLine("Time            =" + DateTime.Now.ToString() + Environment.NewLine);
                    Debug.WriteLine("StringToProcess =" + StringToProcess + Environment.NewLine);
                    StackTrace aStackTraceNextLevel = new StackTrace(new StackFrame(1, true));
                    Debug.WriteLine("StackTrace      =" + aStackTraceNextLevel.ToString() + Environment.NewLine);
                    Debug.WriteLine("================================================================== " + Environment.NewLine);
                }
            }
            catch (System.Exception e)
            {
                //String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #endregion

        #region LINE 推播相容包裝方法
        /// <summary>
        /// 相容舊程式：建立單一使用者推播紀錄並委派 Facade 實際寫入 CRM
        /// </summary>
        public void CreatePushLineMessage(string UserId, string Subject, string Message)
        {
            _facade?.CreatePushLineMessage(UserId, Subject, Message);
        }

        /// <summary>
        /// 相容舊程式：多位使用者推播，逐一委派 Facade
        /// </summary>
        public void CreatePushLineMessage(IList<string> To, string Subject, string Message)
        {
            if (To == null || To.Count == 0) return;
            foreach (var userId in To)
            {
                _facade?.CreatePushLineMessage(userId, Subject, Message);
            }
        }
        #endregion // LINE 推播相容包裝方法
    }
}

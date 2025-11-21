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
using TraceNameSpace;
using ToolUtilityNameSpace.ConnectionOperations;
using static System.Net.WebRequestMethods;

namespace ToolUtility_Developing_NameSpace
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
            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"SPEECHMESSAGE\Administrator";
            var adPassword = "hu9840";

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);
        }

        public ToolUtilityClass(String DiscoveryServiceType)
        {
            // 初始化連接服務
            _crmConnectionService = new CrmConnectionService();

            m_DiscoveryServiceType = DiscoveryServiceType;

            // 使用連接服務建立 CRM 連接
            var adUrl = "https://" + ORGANIZATION + ".speechmessage.com.tw/XRMServices/2011/Organization.svc";
            var adUsername = @"Administrator@speechmessage.com.tw";
            var adPassword = "hu9840";

            m_Crm2011OrganizationService = _crmConnectionService.CreateOnPremiseClient(adUrl, adUsername, adPassword);
        }

        public ToolUtilityClass(ref bool ValidFlag)
        {
            // 初始化連接服務
            _crmConnectionService = new CrmConnectionService();

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
                QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("fullname", "statecode");
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
                    entityCollection = m_OrganizationService.RetrieveMultiple(query);
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
        public EntityCollection RetrieveMemberListCollectionByListId(ref IOrganizationService aOrganizationService, Guid aListId)
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

                EntityCollection entityCollection = aOrganizationService.RetrieveMultiple(query);

                return entityCollection;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(ref OrganizationServiceProxy aOrganizationService, Guid aListId)
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

                EntityCollection entityCollection = aOrganizationService.RetrieveMultiple(query);
                return entityCollection;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveMemberListCollectionByListIdCrm2011(ref IOrganizationService aOrganizationService, Guid aListId)
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

                EntityCollection entityCollection = aOrganizationService.RetrieveMultiple(query);
                return entityCollection;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveDynamicMemberList(string strList)
        {
            ColumnSet cols = new ColumnSet(new string[] { "query" });

            // GUID of the Dynamic Marketing List
            Entity entity;
            String dynamicQuery;
            EntityCollection dynamicmemberec;
            if (CRM_TYPE == "DYNAMICS365")
            {
                entity = this.m_OrganizationService.Retrieve("list", new Guid(strList), cols);
                dynamicQuery = entity.Attributes["query"].ToString();
                dynamicmemberec = this.m_OrganizationService.RetrieveMultiple(new FetchExpression(dynamicQuery));
            }
            else
            {
                entity = this.m_Crm2011OrganizationService.Retrieve("list", new Guid(strList), cols);
                dynamicQuery = entity.Attributes["query"].ToString();
                dynamicmemberec = this.m_Crm2011OrganizationService.RetrieveMultiple(new FetchExpression(dynamicQuery));
            }

            return dynamicmemberec;
        }
        public EntityCollection RetrieveDynamicMemberList(IOrganizationService service, string strList)
        {
            ColumnSet cols = new ColumnSet(new string[] { "query" });

            // GUID of the Dynamic Marketing List
            var entity = service.Retrieve("list", new Guid(strList), cols);
            var dynamicQuery = entity.Attributes["query"].ToString();
            EntityCollection dynamicmemberec = service.RetrieveMultiple(new FetchExpression(dynamicQuery));
            return dynamicmemberec;
        }
        public EntityCollection RetrieveDynamicMemberListDynamics365(OrganizationServiceProxy service, string strList)
        {
            ColumnSet cols = new ColumnSet(new string[] { "query" });

            // GUID of the Dynamic Marketing List
            var entity = service.Retrieve("list", new Guid(strList), cols);
            var dynamicQuery = entity.Attributes["query"].ToString();
            EntityCollection dynamicmemberec = service.RetrieveMultiple(new FetchExpression(dynamicQuery));
            return dynamicmemberec;
        }
        public EntityCollection RetrieveDynamicMemberListCrm2011(IOrganizationService service, string strList)
        {
            ColumnSet cols = new ColumnSet(new string[] { "query" });

            // GUID of the Dynamic Marketing List
            var entity = service.Retrieve("list", new Guid(strList), cols);
            var dynamicQuery = entity.Attributes["query"].ToString();
            EntityCollection dynamicmemberec = service.RetrieveMultiple(new FetchExpression(dynamicQuery));
            return dynamicmemberec;
        }
        public EntityCollection RetrieveDynamicMemberList(Guid aListId)
        {
            ColumnSet cols = new ColumnSet(new string[] { "query" });

            // GUID of the Dynamic Marketing List
            Entity entity;
            String dynamicQuery;

            EntityCollection dynamicmemberec;

            if (CRM_TYPE == "DYNAMICS365")
            {
                entity = m_OrganizationService.Retrieve("list", aListId, cols);
                dynamicQuery = entity.Attributes["query"].ToString();
                dynamicmemberec = m_OrganizationService.RetrieveMultiple(new FetchExpression(dynamicQuery));
            }
            else
            {
                entity = this.m_Crm2011OrganizationService.Retrieve("list", aListId, cols);
                dynamicQuery = entity.Attributes["query"].ToString();
                dynamicmemberec = this.m_Crm2011OrganizationService.RetrieveMultiple(new FetchExpression(dynamicQuery));
            }

            return dynamicmemberec;
        }
        public EntityCollection RetrieveDynamicMemberList(ref IOrganizationService service, Guid aListId)
        {
            ColumnSet cols = new ColumnSet(new string[] { "query" });

            // GUID of the Dynamic Marketing List
            var entity = service.Retrieve("list", aListId, cols);
            var dynamicQuery = entity.Attributes["query"].ToString();

            EntityCollection dynamicmemberec = service.RetrieveMultiple(new FetchExpression(dynamicQuery));
            return dynamicmemberec;
        }
        public EntityCollection RetrieveDynamicMemberListDynamics365(ref OrganizationServiceProxy service, Guid aListId)
        {
            ColumnSet cols = new ColumnSet(new string[] { "query" });

            // GUID of the Dynamic Marketing List
            var entity = service.Retrieve("list", aListId, cols);
            var dynamicQuery = entity.Attributes["query"].ToString();

            EntityCollection dynamicmemberec = service.RetrieveMultiple(new FetchExpression(dynamicQuery));
            return dynamicmemberec;
        }
        public EntityCollection RetrieveDynamicMemberListCrm2011(ref IOrganizationService service, Guid aListId)
        {
            ColumnSet cols = new ColumnSet(new string[] { "query" });

            // GUID of the Dynamic Marketing List
            var entity = service.Retrieve("list", aListId, cols);
            var dynamicQuery = entity.Attributes["query"].ToString();

            EntityCollection dynamicmemberec = service.RetrieveMultiple(new FetchExpression(dynamicQuery));
            return dynamicmemberec;
        }
        public EntityCollection QueryDediccationContatsByFetchXml(String DedicationNumber, String ContactName, String HomePhone, String Mobile, String NationId, String LastSixDigit)
        {
            try
            {
                DedicationNumber = "'" + DedicationNumber + "'";
                ContactName = "'%" + ContactName + "%'";
                HomePhone = "'%" + HomePhone + "%'";
                Mobile = "'%" + Mobile + "%'";
                NationId = "'%" + NationId + "%'";
                LastSixDigit = "'%" + LastSixDigit + "%'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                              <entity name='contact'>
                                <attribute name='fullname' />
                                <attribute name='telephone2' />
                                <attribute name='address2_line1' />
                                <attribute name='parentcustomerid' />
                                <attribute name='new_church_jobtitle' />
                                <attribute name='mobilephone' />
                                <attribute name='emailaddress1' />
                                <attribute name='pager' />
                                <attribute name='new_cell_list_contact' />
                                <attribute name='new_personal_id' />
                                <attribute name='new_last_six_digit' />
                                <attribute name='contactid' />
                                <order attribute='fullname' descending='false' />
                                <filter type='and'>
                                  <filter type='or'>
                                    <condition attribute='pager' operator='eq' value=" + DedicationNumber + @" />
                                    <condition attribute='fullname' operator='like' value=" + ContactName + @"/>
                                    <condition attribute='telephone2' operator='like' value=" + HomePhone + @" />
                                    <condition attribute='mobilephone' operator='like' value=" + Mobile + @" />
                                    <condition attribute='new_personal_id' operator='like' value=" + NationId + @" />
                                    <condition attribute='new_last_six_digit' operator='like' value=" + LastSixDigit + @" />
                                  </filter>
                                    <condition attribute='statuscode' operator='eq' value='1' />
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


                //if (CRM_TYPE == "DYNAMICS365")
                //{
                //    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                //}
                //else
                //{
                //    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
                //}

                //return retrieved;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection QueryContatsByStartedDedicationNumber(String DedicationStartNumber)
        {
            try
            {
                DedicationStartNumber = "'" + DedicationStartNumber + "%'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='3'>
                              <entity name='contact'>
                                <attribute name='fullname' />
                                <attribute name='pager' />
                                <attribute name='telephone2' />
                                <attribute name='address2_line1' />
                                <attribute name='parentcustomerid' />
                                <attribute name='new_church_jobtitle' />
                                <attribute name='mobilephone' />
                                <attribute name='emailaddress1' />
                                <attribute name='contactid' />
                                <order attribute='pager' descending='true' />
                                <filter type='and'>
                                  <condition attribute='pager' operator='like' value=" + DedicationStartNumber + @" />
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
        #region 取得客戶(Account)組織
        public Guid RetrieveAccountCollectionByName(String AccountName)
        {
            try
            {
                //  Create query using querybyattribute
                QueryByAttribute querybyexpression = new QueryByAttribute("account");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("name", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(AccountName, 0);

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


                return (Guid)retrieved.Entities[0].Id;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #region 取得約會
        public EntityCollection RetrieveAppointmentsByDate(DateTime aSelectedDate)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                QueryByAttribute querybyexpression = new QueryByAttribute("appointment");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("fullname", "statecode");
                //  Value of queried attribute to return
                //querybyexpression.Values.AddRange(ContactFullName, 0);

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
        public EntityCollection RetrieveAppointmentsByFetchXml(DateTime StartDate, DateTime EndDate)
        {
            try
            {
                //DateTime StartDate = DateTime.Now.AddDays(-24);
                string StartDateString = @"'" + StartDate.Year + "-" + StartDate.Month + "-" + StartDate.Day + @"'";

                //DateTime EndDate = DateTime.Now.AddDays(24);
                string EndDateString = @"'" + EndDate.Year + "-" + EndDate.Month + "-" + EndDate.Day + @"'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after'  value=" + StartDateString + @" />
                          <condition attribute='scheduledstart' operator='on-or-before' value=" + EndDateString + @" />
                        </filter>
                      </entity>
                    </fetch>";


                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
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
        public EntityCollection RetrieveAppointmentsByFetchXml(String ContactName, String ContactId)
        {
            try
            {
                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='new_leave_signing_status' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <attribute name='new_hours' />
                        <attribute name='new_days' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_applier_appointment' operator='eq' uiname='" + ContactName + @"' uitype='contact' value='{" + ContactId + @"}' />
                          <condition attribute='scheduledstart' operator='this-year' />
                          <condition attribute='new_leave_signing_status' operator='in'>
                                <value> 100000004 </value >
                                <value> 100000001 </value >
                                <value> 100000007 </value >
                          </condition >
                        </filter>
                      </entity>
                    </fetch>";

                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }



                //if (CRM_TYPE == "DYNAMICS365")
                //{
                //    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                //}
                //else
                //{
                //    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
                //}

                return retrieved;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }

        public EntityCollection RetrieveAppointmentsByFetchXmlAndScheduleType(DateTime StartDate, DateTime EndDate, String ScheduleType)
        {
            try
            {
                //DateTime StartDate = DateTime.Now.AddDays(-24);
                string StartDateString = @"'" + StartDate.Year + "-" + StartDate.Month + "-" + StartDate.Day + @"'";

                //DateTime EndDate = DateTime.Now.AddDays(24);
                string EndDateString = @"'" + EndDate.Year + "-" + EndDate.Month + "-" + EndDate.Day + @"'";

                //DateTime EndDate = DateTime.Now.AddDays(24);
                string ScheduleTypeString = @"'" + ScheduleType + @"'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='appointment'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='scheduledstart' />
                        <attribute name='scheduledend' />
                        <attribute name='regardingobjectid' />
                        <attribute name='ownerid' />
                        <attribute name='new_meeting_kind' />
                        <attribute name='new_leave_kind' />
                        <attribute name='new_location_kind' />
                        <attribute name='activityid' />
                        <attribute name='requiredattendees' />
                        <attribute name='optionalattendees' />
                        <attribute name='new_list_appointment' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='scheduledstart' operator='on-or-after'  value=" + StartDateString + @" />
                          <condition attribute='scheduledstart' operator='on-or-before' value=" + EndDateString + @" />
                          <condition attribute='new_meeting_kind' operator='eq' value=" + ScheduleTypeString + @" />
                        </filter>
                      </entity>
                    </fetch>";


                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
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

        #endregion
        #region 取得課程
        /// <summary>
        /// 特定連絡人已報名的課程
        /// </summary>
        /// <param name="StartDate"></param>
        /// <param name="EndDate"></param>
        /// <returns></returns>
        public EntityCollection RetrieveEnrolledLessonsByFetchXml(DateTime StartDate, DateTime EndDate, String ContactName, String ContactId)
        {
            try
            {
                //DateTime StartDate = DateTime.Now.AddDays(-24);
                string StartDateString = @"'" + StartDate.Year + "-" + StartDate.Month + "-" + StartDate.Day + @"'";

                //DateTime EndDate = DateTime.Now.AddDays(24);
                string EndDateString = @"'" + EndDate.Year + "-" + EndDate.Month + "-" + EndDate.Day + @"'";

                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                      <entity name='new_disciple_lessons'>
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_class_start_date' />
                        <attribute name='new_class_end_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_disciple_lessonsid' />
                        <order attribute='new_classification' descending='false' />
                        <filter type='and'>
                            <condition attribute='new_class_start_date' operator='on-or-after'  value=" + StartDateString + @" />
                            <condition attribute='new_class_end_date' operator='on-or-before' value=" + EndDateString + @" />
                        </filter>
                        <link-entity name='new_stor_lessons' from='new_new_disciple_lessons_new_stor_les' to='new_disciple_lessonsid' alias='ab'>
                          <filter type='and'>
                            <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname=" + ContactName + @" uitype ='contact' value=" + ContactId + @" />
                          </filter>
                        </link-entity>
                      </entity>
                    </fetch>";


                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
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
        public EntityCollection RetrieveLessonsByMonth(DateTime StartDate, DateTime EndDate)
        {
            try
            {
                //DateTime StartDate = DateTime.Now.AddDays(-24);
                string StartDateString = @"'" + StartDate.Year + "-" + StartDate.Month + "-" + StartDate.Day + @"'";

                //DateTime EndDate = DateTime.Now.AddDays(24);
                string EndDateString = @"'" + EndDate.Year + "-" + EndDate.Month + "-" + EndDate.Day + @"'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                      <entity name='new_disciple_lessons'>
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_class_start_date' />
                        <attribute name='new_class_end_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_disciple_lessonsid' />
                        <order attribute='new_classification' descending='false' />
                        <filter type='and'>
                            <condition attribute='new_class_start_date' operator='on-or-after'  value=" + StartDateString + @" />
                            <condition attribute='new_class_end_date' operator='on-or-before' value=" + EndDateString + @" />
                        </filter>
                      </entity>
                    </fetch>";


                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
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
        #endregion
        #region 取得上課紀錄單
        /// <summary>
        /// 特定連絡人已報名的課程
        /// </summary>
        /// <param name="StartDate"></param>
        /// <param name="EndDate"></param>
        /// <returns></returns>
        public EntityCollection RetrieveStorLessonsByFetchXml(String LessonName, String LessonId, String ContactName, String ContactId)
        {
            try
            {
                LessonName = @"'" + LessonName + @"'";
                LessonId = @"'{" + LessonId + @"}'";

                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_stor_lessons'>
                        <attribute name='createdon' />
                        <attribute name='new_contact_new_stor_lessons' />
                        <attribute name='new_absence_record' />
                        <attribute name='new_fee' />
                        <attribute name='new_pay_date' />
                        <attribute name='new_classification' />
                        <attribute name='new_new_disciple_lessons_new_stor_les' />
                        <attribute name='new_stor_lessonsid' />
                        <order attribute='new_new_disciple_lessons_new_stor_les' descending='false' />
                        <order attribute='new_contact_new_stor_lessons' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_new_disciple_lessons_new_stor_les' operator='eq' uiname=" + LessonName + @" uitype ='new_disciple_lessons' value=" + LessonId + @" />
                          <condition attribute='new_contact_new_stor_lessons' operator='eq' uiname=" + ContactName + @" uitype ='contact' value=" + ContactId + @" />
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

                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
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
        #endregion
        #region 取得工作
        /// <summary>
        /// 特定連絡人已報名的課程
        /// </summary>
        /// <param name="StartDate"></param>
        /// <param name="EndDate"></param>
        /// <returns></returns>
        public EntityCollection RetrieveTaskByFetchXml(String Subject)
        {
            try
            {
                Subject = @"'" + Subject + @"'";
                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='task'>
                        <attribute name='subject' />
                        <attribute name='statecode' />
                        <attribute name='prioritycode' />
                        <attribute name='scheduledend' />
                        <attribute name='createdby' />
                        <attribute name='regardingobjectid' />
                        <attribute name='activityid' />
                        <attribute name='description' />
                        <order attribute='subject' descending='false' />
                        <filter type='and'>
                          <condition attribute='subject' operator='eq' value=" + Subject + @" />
                        </filter>
                      </entity>
                    </fetch>";


                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
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
        #endregion
        #region 取得個人聚會與靈修記錄
        /// <summary>
        /// 特定連絡人已報名的課程
        /// </summary>
        /// <param name="StartDate"></param>
        /// <param name="EndDate"></param>
        /// <returns></returns>
        public EntityCollection RetrievePresentRecordByFetchXml(String WeeklyReportName, String WeeklyReportId, String ContactName, String ContactId)
        {
            try
            {
                WeeklyReportName = @"'" + WeeklyReportName + @"'";
                WeeklyReportId = @"'{" + WeeklyReportId + @"}'";

                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";


                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_group_present_weekly_report_prese' operator='eq' uiname=" + WeeklyReportName + @" uitype ='new_disciple_lessons' value=" + WeeklyReportId + @" />
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
                        </filter>
                      </entity>
                    </fetch>";


                RetrieveMultipleRequest fetchRequest1 = new RetrieveMultipleRequest
                {
                    Query = new FetchExpression(fetchXml)
                };

                EntityCollection retrieved;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;
                }
                else
                {
                    retrieved = ((RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(fetchRequest1)).EntityCollection;
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
        public EntityCollection RetrievePresentRecordByFetchXmlAndSundayDate(String ContactName, String ContactId, DateTime SundayDate)
        {
            try
            {
                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";

                string SundayDateString = @"'" + SundayDate.Year + "-" + SundayDate.Month + "-" + SundayDate.Day + @"'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                        <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                             <condition attribute='new_contact_new_present_record' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
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
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrievePresentRecordByFetchXmlAndWeeklyReport(String ContactName, String ContactId, String WeeklyReportNmae, String WeeklyReportId)
        {
            try
            {
                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";

                WeeklyReportNmae = @"'" + WeeklyReportNmae + @"'";
                WeeklyReportId = @"'{" + WeeklyReportId + @"}'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_group_present_weekly_report_prese' operator='eq' uiname=" + WeeklyReportNmae + @" uitype='new_group_present_weekly_report' value=" + WeeklyReportId + @" />
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
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
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrievePresentRecordByFetchXmlAndContainEpiredDate(String ContactName, String ContactId)
        {
            try
            {
                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
                          <condition attribute='new_care_expire_date' operator='not-null' />
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
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrievePresentRecordByFetchXmlAndContact_SmallGroup_SundayDate(String ContactName, String ContactId, String SmallGroupName, String SmallGroupId, DateTime SundayDate)
        {
            try
            {
                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";

                SmallGroupName = @"'" + SmallGroupName + @"'";
                SmallGroupId = @"'{" + SmallGroupId + @"}'";

                string SundayDateString = @"'" + SundayDate.Year + "-" + SundayDate.Month + "-" + SundayDate.Day + @"'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_present_record'>
                            <attribute name='new_present_recordid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_list_new_present_record' operator='eq' uiname=" + SmallGroupName + @" uitype='list' value=" + SmallGroupId + @" />
                              <condition attribute='new_contact_new_present_record' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
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
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #region 取得名單
        public Entity RetrieveListEntityByName(String ListName)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                //{
                //  Create query using querybyattribute
                //Console.WriteLine("除錯 001");

                QueryByAttribute querybyexpression = new QueryByAttribute("list");
                querybyexpression.ColumnSet = new ColumnSet();
                querybyexpression.ColumnSet.AllColumns = true;
                //  Attribute to query
                querybyexpression.Attributes.AddRange("listname", "statecode");
                //  Value of queried attribute to return
                querybyexpression.Values.AddRange(ListName, 0);

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
        public EntityCollection RetrieveListByFetchXmlContact(String ContactName)
        {
            try
            {
                #region 取得聯絡人的
                ContactName = @"'" + ContactName + @"'";
                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                          <entity name='list'>
                            <attribute name='listname' />
                            <attribute name='createdfromcode' />
                            <attribute name='lastusedon' />
                            <attribute name='purpose' />
                            <attribute name='listid' />
                            <order attribute='listname' descending='true' />
                            <filter type='and'>
                              <condition attribute='new_app_named' operator='eq' value='1' />
                              <condition attribute='purpose' operator='eq' value='小組名單' />
                            </filter>
                            <link-entity name='listmember' from='listid' to='listid' visible='false' intersect='true'>
                              <link-entity name='contact' from='contactid' to='entityid' alias='af'>
                                <filter type='and'>
                                  <condition attribute='fullname' operator='eq' value=" + ContactName + @" />
                                </filter>
                              </link-entity>
                            </link-entity>
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
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveListByFetchXmlRacerLeader(String ContactName, String ContactId)
        {
            try
            {
                #region 取得聯絡人的
                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='list'>
                        <attribute name='listname' />
                        <attribute name='createdfromcode' />
                        <attribute name='lastusedon' />
                        <attribute name='purpose' />
                        <attribute name='listid' />
                        <order attribute='listname' descending='true' />
                        <filter type='and'>
                            <condition attribute='new_contact_race_leager_list' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
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
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #region 取得收費單
        /// <summary>
        /// 特定連絡人已報名的課程
        /// </summary>
        /// <param name="StartDate"></param>
        /// <param name="EndDate"></param>
        /// <returns></returns>
        public EntityCollection RetrieveDedicationFeeByFetchXml(String ContactName, String ContactId)
        {
            try
            {
                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";


                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                          <entity name='new_fee'>
                            <attribute name='new_feeid' />
                            <attribute name='new_name' />
                            <attribute name='createdon' />
                            <attribute name='new_pay_date' />
                            <attribute name='new_fee_shoud_pay' />
                            <attribute name='new_fee_really_paid' />
                            <attribute name='new_pay_way' />
                            <attribute name='new_category' />
                            <attribute name='new_others' />
                            <order attribute='new_name' descending='false' />
                            <filter type='and'>
                              <condition attribute='new_contact_new_fee' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
                              <condition attribute='new_category' operator='not-null' />
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
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveDedicationFeeByDateFetchXml(String ContactName, String ContactId, DateTime StartDate, DateTime EndDate)
        {
            try
            {
                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";

                //DateTime StartDate = DateTime.Now.AddDays(-24);
                string StartDateString = @"'" + StartDate.Year + "-" + StartDate.Month + "-" + StartDate.Day + @"'";

                //DateTime EndDate = DateTime.Now.AddDays(24);
                string EndDateString = @"'" + EndDate.Year + "-" + EndDate.Month + "-" + EndDate.Day + @"'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                                  <entity name='new_fee'>
                                    <attribute name='new_feeid' />
                                    <attribute name='new_name' />
                                    <attribute name='createdon' />
                                    <attribute name='new_pay_date' />
                                    <attribute name='new_fee_shoud_pay' />
                                    <attribute name='new_fee_really_paid' />
                                    <attribute name='new_pay_way' />
                                    <attribute name='new_category' />
                                    <attribute name='new_others' />
                                    <attribute name='new_paid_period' />
                                    <order attribute='new_name' descending='false' />
                                    <filter type='and'>
                                      <condition attribute='new_contact_new_fee' operator='eq' uiname=" + ContactName + @" uitype='contact' value=" + ContactId + @" />
                                      <condition attribute='new_category' operator='not-null' />
                                      <condition attribute='new_pay_status' operator='in'>
                                        <value>100000001</value>
                                        <value>100000002</value>
                                        <value>100000003</value>
                                        <value>100000004</value>
                                        <value>100000006</value>
                                      </condition>
                                      <condition attribute='new_pay_date' operator='on-or-after'  value=" + StartDateString + @" />
                                      <condition attribute='new_pay_date' operator='on-or-before' value=" + EndDateString + @" />
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
                //}
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


    }
}

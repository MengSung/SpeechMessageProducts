using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.ServiceModel.Description;
using System.Net;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using TraceNameSpace;

// These namespaces are found in the Microsoft.Xrm.Sdk.dll assembly
// located in the SDK\bin folder of the SDK download.
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Messages;

namespace ToolUtilityNameSpace
{
    public class ToolUtilityClass
    {
        #region 資料區
        //private String CRM_TYPE = "CRM2011";
        private String CRM_TYPE = "DYNAMICS365";

        String m_DiscoveryServiceType = "";

        public IOrganizationService m_Crm2011OrganizationService;
        private bool _disposed = false;

        // public OrganizationServiceProxy m_OrganizationProxy;
        public OrganizationServiceProxy m_OrganizationService;

        #region CRM 2011 新增組織修改區

        #region 永和禮拜堂組織
        //private const String SERVER = "crm2011"; // 機房雲端要用此網址
        //private const String SERVER = "system.speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "yangmeillc";
        //private const String USERNAME = "Administrator";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        #region 林口靈糧堂組織
        //private const String SERVER = "crm2011"; // 機房雲端要用此網址
        //private const String SERVER = "system.speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "lkllc";
        //private const String USERNAME = "Administrator";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        #region 內壢得勝靈糧堂組織
        ////private const String SERVER = "crm2011";
        //private const String SERVER = "system.speechmessage.com.tw";
        ////private const String SERVER = "speechmessage.com.tw";
        ////private const String SERVER = "202.153.204.60";
        //
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "nvllc";
        //private const String USERNAME = "Administrator";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        #region 中壢靈糧堂組織
        //private const String SERVER = "crm2011"; // 機房雲端要用此網址
        //private const String SERVER = "system.speechmessage.com.tw";
        //
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "blccjl";
        //private const String USERNAME = "Administrator";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        #region 音訊科技公司內部發展系統
        //private const String SERVER = "speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "speechmessage";
        //private const String USERNAME = "Administrator";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        #region 永和禮拜堂組織(公司內部發展)
        //private const String SERVER = "speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "yangmeillc";
        //private const String USERNAME = "Administrator";
        ////private const String USERNAME = "Administrator@speechmessage.com.tw";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        #region 音訊科技組織(公司內部發展)
        //private const String SERVER = "speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "linemessage";
        //private const String USERNAME = "Administrator";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        #region 永和禮拜堂(公司內部發展)
        //private const String SERVER = "speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "tpehocback";
        //private const String USERNAME = "Administrator";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "SPEECHMESSAGE";
        #endregion

        #region 永和禮拜堂(雲端機房)
        //private const String SERVER = "speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "tpehoc";
        //private const String USERNAME = "Administrator@speechmessage.com.tw";
        //private const String PASSWORD = "hu9840";
        ////private const String DOMAIN = "DYNAMICS-365";
        #endregion

        #endregion

        #region Dynamics 365 新增組織修改區

        // 客製化
        #region 永和禮拜堂(雲端機房)
        //private const String SERVER = "speechmessage.com.tw";
        //private const String PORT = "7777";
        //private const String ORGANIZATION = "abundance";
        //private const String USERNAME = "Administrator@speechmessage.com.tw";
        //private const String PASSWORD = "hu9840";
        //private const String DOMAIN = "DYNAMICS-365";
        #endregion

        #region 永和禮拜堂(公司內部發展)
        private const String SERVER = "speechmessage.com.tw";
        private const String PORT = "7777";
        private const String ORGANIZATION = "yhchurchback";
        private const String USERNAME = "Administrator@speechmessage.com.tw";
        private const String PASSWORD = "hu9840";
        private const String DOMAIN = "SPEECHMESSAGE";
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
        //private const bool EXCUTION_FLAG = false;

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
            #region 追蹤專用變數
            //m_TraceLogFile = TRACE_DIRECTOR;
            //m_XmlFileStream = new FileStream(m_TraceLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            //m_XmlFileStreamWriter = new StreamWriter(m_XmlFileStream, Encoding.GetEncoding("big5"));
            //m_Listener = new BugslayerTextWriterTraceListener(m_XmlFileStreamWriter);

            //Debug.AutoFlush = true;
            //Debug.Listeners.Add(m_Listener);
            #endregion

            //SetOrganizationService();

            //SetClaimsBasedAuthenticationOrganizationService();

            //SetFederatedOrganizationProxy();
        }
        public ToolUtilityClass(String DiscoveryServiceType)
        {
            #region 追蹤專用變數
            //m_TraceLogFile = TRACE_DIRECTOR;
            //m_XmlFileStream = new FileStream(m_TraceLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            //m_XmlFileStreamWriter = new StreamWriter(m_XmlFileStream, Encoding.GetEncoding("big5"));
            //m_Listener = new BugslayerTextWriterTraceListener(m_XmlFileStreamWriter);

            //Debug.AutoFlush = true;
            //Debug.Listeners.Add(m_Listener);
            #endregion

            //SetOrganizationService();

            //SetClaimsBasedAuthenticationOrganizationService();

            //SetFederatedOrganizationProxy(DiscoveryServiceType);
            m_DiscoveryServiceType = DiscoveryServiceType;

            if (DiscoveryServiceType == "DYNAMICS365")
            {
                SetFederatedOrganizationProxy(DiscoveryServiceType);
            }
            else
            {
                SetOrganizationService();
            }

            CRM_TYPE = DiscoveryServiceType;
        }
        public ToolUtilityClass(ref bool ValidFlag)
        {
            #region 追蹤專用變數
            //m_TraceLogFile = TRACE_DIRECTOR;
            //m_XmlFileStream = new FileStream(m_TraceLogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            //m_XmlFileStreamWriter = new StreamWriter(m_XmlFileStream, Encoding.GetEncoding("big5"));
            //m_Listener = new BugslayerTextWriterTraceListener(m_XmlFileStreamWriter);

            //Debug.AutoFlush = true;
            //Debug.Listeners.Add(m_Listener);
            #endregion

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
            this.m_OrganizationService.Dispose();

            _disposed = true;
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
        #region 連接 CRM 2011 服務
        public ClientCredentials GetClientCredentials(String Domain, String UserName, String Password)
        {
            NetworkCredential loCredentials = new NetworkCredential();
            ClientCredentials loClientCredentials = new ClientCredentials();

            loCredentials.Domain = Domain;
            loCredentials.UserName = UserName;
            loCredentials.Password = Password;

            loClientCredentials.Windows.ClientCredential = loCredentials;

            return loClientCredentials;
        }
        /// <summary>
        /// Method to create an isntance of the
        /// CRM Organization Service to the 
        /// invoking method using the criteria
        /// entered by the program user
        /// </summary>
        /// <returns>IOrganizationService</returns>
        public IOrganizationService GetOrganizationService(String Server, String Port, String Organization, String Domain, String UserName, String Password)
        {
            IOrganizationService loService;
            OrganizationServiceProxy loServiceProxy;
            Uri loURL = new Uri("http://" + Server + ":" + Port + "/" + Organization + "/XRMServices/2011/Organization.svc");

            IServiceConfiguration<IOrganizationService> loOrgConfigInfo = ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(loURL);
            var loCreds = GetClientCredentials(Domain, UserName, Password);

            using (loServiceProxy = new OrganizationServiceProxy(loOrgConfigInfo, loCreds))
            {
                // This statement is required to enable early-bound type support.
                loServiceProxy.ServiceConfiguration.CurrentServiceEndpoint.Behaviors.Add(new ProxyTypesBehavior());

                loService = (IOrganizationService)loServiceProxy;
            }

            return loService;
        }
        public ClientCredentials GetClientCredentials()
        {
            NetworkCredential loCredentials = new NetworkCredential();
            ClientCredentials loClientCredentials = new ClientCredentials();

            loCredentials.Domain = DOMAIN;
            loCredentials.UserName = USERNAME;
            loCredentials.Password = PASSWORD;

            loClientCredentials.Windows.ClientCredential = loCredentials;

            return loClientCredentials;
        }
        public IOrganizationService SetOrganizationService()
        {
            OrganizationServiceProxy loServiceProxy;

            Uri loURL = new Uri("http://" + SERVER + ":" + PORT + "/" + ORGANIZATION + "/XRMServices/2011/Organization.svc");
            //Uri loURL = new Uri(http://win2008r2:6666/lkllc/XRMServices/2011/Organization.svc");

            // http://win2008r2:6666/lkllc/XRMServices/2011/Organization.svc
            IServiceConfiguration<IOrganizationService> loOrgConfigInfo = ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(loURL);
            var loCreds = GetClientCredentials();

            using (loServiceProxy = new OrganizationServiceProxy(loOrgConfigInfo, loCreds))
            {
                // This statement is required to enable early-bound type support.
                loServiceProxy.ServiceConfiguration.CurrentServiceEndpoint.Behaviors.Add(new ProxyTypesBehavior());

                m_Crm2011OrganizationService = (IOrganizationService)loServiceProxy;
            }

            return m_OrganizationService;
        }
        public IOrganizationService SetClaimsBasedAuthenticationOrganizationService()
        {
            OrganizationServiceProxy loServiceProxy;

            Uri loURL = new Uri("https://" + ORGANIZATION + "." + SERVER + "/XRMServices/2011/Organization.svc");
            //Uri loURL = new Uri(https://speechmessage.speechmessage.com.tw/XRMServices/2011/Organization.svc");

            IServiceConfiguration<IOrganizationService> loOrgConfigInfo = ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(loURL);
            var loCreds = GetClientCredentials();

            // Get a reference to the organization service.
            using (loServiceProxy = new OrganizationServiceProxy(loOrgConfigInfo, loCreds))
            {
                // This statement is required to enable early-bound type support.
                loServiceProxy.ServiceConfiguration.CurrentServiceEndpoint.Behaviors.Add(new ProxyTypesBehavior());

                m_Crm2011OrganizationService = (IOrganizationService)loServiceProxy;
            }

            return m_OrganizationService;
        }
        public String SetClaimsBasedAuthenticationOrganizationService_DEBUG()
        {
            String DebugStriing = "";

            OrganizationServiceProxy loServiceProxy;

            Uri loURL = new Uri("https://" + ORGANIZATION + "." + SERVER + "/XRMServices/2011/Organization.svc");
            //Uri loURL = new Uri(https://speechmessage.speechmessage.com.tw/XRMServices/2011/Organization.svc");

            DebugStriing += "001" + Environment.NewLine;

            IServiceConfiguration<IOrganizationService> loOrgConfigInfo = ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(loURL);
            DebugStriing += "002" + Environment.NewLine;

            var loCreds = GetClientCredentials();

            DebugStriing += "003" + Environment.NewLine;
            //// Get a reference to the organization service.
            using (loServiceProxy = new OrganizationServiceProxy(loOrgConfigInfo, loCreds))
            {
                //    // This statement is required to enable early-bound type support.
                //    loServiceProxy.ServiceConfiguration.CurrentServiceEndpoint.Behaviors.Add(new ProxyTypesBehavior());
                //
                m_Crm2011OrganizationService = (IOrganizationService)loServiceProxy;
                DebugStriing += "004" + Environment.NewLine;
            }

            DebugStriing += "005" + Environment.NewLine;

            //return m_OrganizationService;
            return DebugStriing;
        }
        #endregion
        #region 連接 Dynamics 365 服務
        public OrganizationServiceProxy SetFederatedOrganizationProxy(String DiscoveryServiceType)
        {
            String aDiscoveryServiceAddress = "";
            if (DiscoveryServiceType == "DYNAMICS365")
            {
                aDiscoveryServiceAddress = "https://" + ORGANIZATION + "." + SERVER + BASE_DISCOVERY_SERVICE_ADDRESS;
            }
            else
            {
                aDiscoveryServiceAddress = "http://" + SERVER + ":" + PORT + "/" + ORGANIZATION + "/XRMServices/2011/Organization.svc";
            }

            IServiceManagement<IDiscoveryService> serviceManagement = ServiceConfigurationFactory.CreateManagement<IDiscoveryService>(new Uri(aDiscoveryServiceAddress));

            AuthenticationProviderType endpointType = serviceManagement.AuthenticationType;

            // Set the credentials.
            AuthenticationCredentials authCredentials = GetCredentials(serviceManagement, endpointType);


            String organizationUri = String.Empty;
            // Get the discovery service proxy.
            using (DiscoveryServiceProxy discoveryProxy = GetProxy<IDiscoveryService, DiscoveryServiceProxy>(serviceManagement, authCredentials))
            {
                // Obtain organization information from the Discovery service. 
                if (discoveryProxy != null)
                {
                    // Obtain information about the organizations that the system user belongs to.
                    OrganizationDetailCollection orgs = DiscoverOrganizations(discoveryProxy);
                    // Obtains the Web address (Uri) of the target organization.
                    organizationUri = FindOrganization(ORGANIZATION, orgs.ToArray()).Endpoints[EndpointType.OrganizationService];
                }
            }


            if (!String.IsNullOrWhiteSpace(organizationUri))
            {
                IServiceManagement<IOrganizationService> orgServiceManagement =
                    ServiceConfigurationFactory.CreateManagement<IOrganizationService>(
                    new Uri(organizationUri));

                // Set the credentials.
                AuthenticationCredentials credentials = GetCredentials(orgServiceManagement, endpointType);

                // Get the organization service proxy.
                using (this.m_OrganizationService = GetProxy<IOrganizationService, OrganizationServiceProxy>(orgServiceManagement, credentials))
                {
                    // This statement is required to enable early-bound type support.
                    this.m_OrganizationService.EnableProxyTypes();

                    TimeSpan aInterval = new TimeSpan(3, 0, 0);
                    this.m_OrganizationService.Timeout = aInterval;

                    // Now make an SDK call with the organization service proxy.
                    // Display information about the logged on user.
                    //Guid userid = ((WhoAmIResponse)this.m_OrganizationService.Execute(new WhoAmIRequest())).UserId;

                    //String FullName = RetrieveContactByAccountNumber("123", "123");
                    //SystemUser systemUser = organizationProxy.Retrieve("systemuser", userid,
                    //    new ColumnSet(new string[] { "firstname", "lastname" })).ToEntity<SystemUser>();
                    //Console.WriteLine("Logged on user is {0} {1}.",
                    //    systemUser.FirstName, systemUser.LastName);
                }

                return m_OrganizationService;
            }
            else
            {
                return null;
            }


        }
        /// <summary>
        /// Obtain the AuthenticationCredentials based on AuthenticationProviderType.
        /// </summary>
        /// <param name="service">A service management object.</param>
        /// <param name="endpointType">An AuthenticationProviderType of the CRM environment.</param>
        /// <returns>Get filled credentials.</returns>
        private AuthenticationCredentials GetCredentials<TService>(IServiceManagement<TService> service, AuthenticationProviderType endpointType)
        {
            AuthenticationCredentials authCredentials = new AuthenticationCredentials();

            switch (endpointType)
            {
                case AuthenticationProviderType.ActiveDirectory:
                    authCredentials.ClientCredentials.Windows.ClientCredential = new System.Net.NetworkCredential(USERNAME, PASSWORD, DOMAIN);
                    break;
                case AuthenticationProviderType.LiveId:
                    authCredentials.ClientCredentials.UserName.UserName = USERNAME;
                    authCredentials.ClientCredentials.UserName.Password = PASSWORD;
                    authCredentials.SupportingCredentials = new AuthenticationCredentials();
                    //authCredentials.SupportingCredentials.ClientCredentials = Microsoft.Crm.Services.Utility.DeviceIdManager.LoadOrRegisterDevice();
                    break;
                default: // For Federated and OnlineFederated environments.                    
                    authCredentials.ClientCredentials.UserName.UserName = USERNAME;
                    authCredentials.ClientCredentials.UserName.Password = PASSWORD;
                    // For OnlineFederated single-sign on, you could just use current UserPrincipalName instead of passing user name and password.
                    // authCredentials.UserPrincipalName = UserPrincipal.Current.UserPrincipalName;  // Windows Kerberos

                    // The service is configured for User Id authentication, but the user might provide Microsoft
                    // account credentials. If so, the supporting credentials must contain the device credentials.
                    if (endpointType == AuthenticationProviderType.OnlineFederation)
                    {
                        //IdentityProvider provider = service.GetIdentityProvider(authCredentials.ClientCredentials.UserName.UserName);
                        //if (provider != null & amp; &amp; provider.IdentityProviderType == IdentityProviderType.LiveId)
                        //{
                        //    authCredentials.SupportingCredentials = new AuthenticationCredentials();
                        //    authCredentials.SupportingCredentials.ClientCredentials =
                        //        Microsoft.Crm.Services.Utility.DeviceIdManager.LoadOrRegisterDevice();
                        //}
                    }

                    break;
            }

            return authCredentials;
        }

        /// <summary>
        /// Discovers the organizations that the calling user belongs to.
        /// </summary>
        /// <param name="service">A Discovery service proxy instance.</param>
        /// <returns>Array containing detailed information on each organization that 
        /// the user belongs to.</returns>
        public OrganizationDetailCollection DiscoverOrganizations(IDiscoveryService service)

        {
            if (service == null) throw new ArgumentNullException("service");
            RetrieveOrganizationsRequest orgRequest = new RetrieveOrganizationsRequest();
            RetrieveOrganizationsResponse orgResponse = (RetrieveOrganizationsResponse)service.Execute(orgRequest);

            return orgResponse.Details;
        }

        /// <summary>
        /// Finds a specific organization detail in the array of organization details
        /// returned from the Discovery service.
        /// </summary>
        /// <param name="orgUniqueName">The unique name of the organization to find.</param>
        /// <param name="orgDetails">Array of organization detail object returned from the discovery service.</param>
        /// <returns>Organization details or null if the organization was not found.</returns>
        /// <seealso cref="DiscoveryOrganizations"/>
        public OrganizationDetail FindOrganization(string orgUniqueName, OrganizationDetail[] orgDetails)
        {
            if (String.IsNullOrWhiteSpace(orgUniqueName))
            {
                throw new ArgumentNullException("orgUniqueName");
            }
            if (orgDetails == null)
            {
                throw new ArgumentNullException("orgDetails");
            }
            OrganizationDetail orgDetail = null;

            foreach (OrganizationDetail detail in orgDetails)
            {
                if (String.Compare(detail.UniqueName, orgUniqueName, StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    orgDetail = detail;
                    break;
                }
            }
            return orgDetail;
        }

        /// <summary>
        /// Generic method to obtain discovery/organization service proxy instance.
        /// </summary>
        /// <typeparam name="TService">
        /// Set IDiscoveryService or IOrganizationService type to request respective service proxy instance.
        /// </typeparam>
        /// <typeparam name="TProxy">
        /// Set the return type to either DiscoveryServiceProxy or OrganizationServiceProxy type based on TService type.
        /// </typeparam>
        /// <param name="serviceManagement">An instance of IServiceManagement</param>
        /// <param name="authCredentials">The user's Microsoft Dynamics CRM logon credentials.</param>
        /// <returns></returns>
        private TProxy GetProxy<TService, TProxy>(
            IServiceManagement<TService> serviceManagement,
            AuthenticationCredentials authCredentials)
            where TService : class
            where TProxy : ServiceProxy<TService>
        {
            Type classType = typeof(TProxy);

            if (serviceManagement.AuthenticationType !=
                AuthenticationProviderType.ActiveDirectory)
            {
                AuthenticationCredentials tokenCredentials =
                    serviceManagement.Authenticate(authCredentials);
                // Obtain discovery/organization service proxy for Federated, LiveId and OnlineFederated environments. 
                // Instantiate a new class of type using the 2 parameter constructor of type IServiceManagement and SecurityTokenResponse.
                return (TProxy)classType
                    .GetConstructor(new Type[] { typeof(IServiceManagement<TService>), typeof(SecurityTokenResponse) })
                    .Invoke(new object[] { serviceManagement, tokenCredentials.SecurityTokenResponse });
            }

            // Obtain discovery/organization service proxy for ActiveDirectory environment.
            // Instantiate a new class of type using the 2 parameter constructor of type IServiceManagement and ClientCredentials.
            return (TProxy)classType
                .GetConstructor(new Type[] { typeof(IServiceManagement<TService>), typeof(ClientCredentials) })
                .Invoke(new object[] { serviceManagement, authCredentials.ClientCredentials });
        }



        #endregion
        #region 透過屬性取得實體
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
        public Entity RetrieveContactByNameAndMobile(ref IOrganizationService aOrganizationService, String ContactFullName, String Mobile)
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

                    Regex DigitsOnly = new Regex(@"[^\d]");

                    if (retrieved.Entities.Count > 0 && retrieved != null)
                    {
                        foreach (Entity aContactEntity in retrieved.Entities)
                        {
                            String MobilePhone = this.GetEntityStringAttribute(aContactEntity, "mobilephone");
                            if (DigitsOnly.Replace(MobilePhone, "") == DigitsOnly.Replace(Mobile, ""))
                            {
                                return aContactEntity;
                            }
                        }
                        return null;
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
                    //永和禮拜堂小組長帳號
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
                    //永和禮拜堂小組長帳號
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
                    //永和禮拜堂小組長帳號
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
                    //永和禮拜堂小組長帳號
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
                          <condition attribute='scheduledstart' operator='on-or-before' value=" + EndDateString   + @" />
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

                EntityCollection retrieved = ((RetrieveMultipleResponse)this.m_OrganizationService.Execute(fetchRequest1)).EntityCollection;

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
        public EntityCollection RetrievePresentRecordByFetchXmlAndSundayDate( String ContactName, String ContactId, DateTime SundayDate )
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
        #endregion
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
                //lock (m_QueryManyToOneLocker)
                //{
                    //Monitor.Enter(this);
                    // The following code example demonstrates how to use RetrieveMultiple 
                    // to carry out a RetrieveByPrincipal.

                    // Create a column set holding the names of the columns to be retrieved.
                    //ColumnSet colsPrincipal = new ColumnSet();

                    // Set the properties of the column set.
                    //colsPrincipal.Attributes.Add("new_blood_reportid");

                    // Create a ConditionExpression.
                    ConditionExpression ContactConditionPrincipal = new ConditionExpression();

                    // Set the ConditionExpressions properties so that the condition is true when the 
                    // ownerid of the account equals the principalId.
                    ContactConditionPrincipal.AttributeName = "new_blood_contact_relation";
                    ContactConditionPrincipal.Operator = ConditionOperator.Equal;

                    ContactConditionPrincipal.Values.Add(ContactId.ToString());

                    // Create a ConditionExpression.
                    ConditionExpression DateTimeConditionPrincipal = new ConditionExpression();

                    // Create the FilterExpression.
                    FilterExpression filterPrincipal = new FilterExpression();
                    // Set the properties of the FilterExpression.
                    filterPrincipal.FilterOperator = LogicalOperator.And;
                    filterPrincipal.AddCondition(ContactConditionPrincipal);

                    OrderExpression OrderByDate = new OrderExpression();
                    OrderByDate.AttributeName = "createdon";
                    OrderByDate.OrderType = OrderType.Descending;

                    // Create the QueryExpression.
                    QueryExpression queryPrincipal = new QueryExpression();

                    // Set the properties of the QueryExpression.
                    queryPrincipal.EntityName = @"new_blood_report";
                    queryPrincipal.ColumnSet.AllColumns = true;
                    //queryPrincipal.ColumnSet = colsPrincipal;
                    queryPrincipal.Criteria = filterPrincipal;
                    queryPrincipal.Orders.Add(OrderByDate);

                    // Create the request object.
                    RetrieveMultipleRequest retrievePrincipal = new RetrieveMultipleRequest();
                    // Set the properties of the request object.
                    retrievePrincipal.Query = queryPrincipal;
                    //retrievePrincipal.ReturnDynamicEntities = true;

                    // Execute the request.
                    RetrieveMultipleResponse principalResponse;
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        principalResponse = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrievePrincipal);
                    }
                    else
                    {
                        principalResponse = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrievePrincipal);
                    }

                    //BusinessEntityCollection BloodReportCollection = aCrmService.RetrieveMultiple(queryPrincipal);

                    if (principalResponse.EntityCollection.TotalRecordCount > 0)
                    {
                        //Monitor.Exit(this);
                        return (Entity)principalResponse.EntityCollection.Entities[0];
                    }
                    else
                    {
                        //Monitor.Exit(this);
                        return null;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        // 搜尋主日日期是最近2個月的靈修單
        public EntityCollection QueryPresentRecordByContactIdAndSunday(Guid aListEntityId, Guid ContactId, int MonthPeriod)
        {
            try
            {
                //lock (m_QueryManyToOneLocker)
                //{
                    ConditionExpression WeeklyReportConditionPrincipal = new ConditionExpression();

                    // Set the ConditionExpressions properties so that the condition is true when the 
                    // ownerid of the account equals the principalId.
                    WeeklyReportConditionPrincipal.AttributeName = "new_list_new_present_record";
                    WeeklyReportConditionPrincipal.Operator = ConditionOperator.Equal;

                    WeeklyReportConditionPrincipal.Values.Add(aListEntityId.ToString());



                    ConditionExpression ContactConditionPrincipal = new ConditionExpression();

                    // Set the ConditionExpressions properties so that the condition is true when the 
                    // ownerid of the account equals the principalId.

                    ContactConditionPrincipal.AttributeName = "new_contact_new_present_record";
                    ContactConditionPrincipal.Operator = ConditionOperator.Equal;

                    ContactConditionPrincipal.Values.Add(ContactId.ToString());

                    // Create a ConditionExpression.
                    ConditionExpression DateTimeConditionPrincipal = new ConditionExpression();

                    //DateTimeConditionPrincipal.AttributeName = "createdon";

                    // 主日日期
                    DateTimeConditionPrincipal.AttributeName = "new_sunday_date";

                    // 過去　MonthPeriod　個月
                    //DateTimeConditionPrincipal.Operator = ConditionOperator.LastXMonths;
                    //DateTimeConditionPrincipal.Values.Add(MonthPeriod);//主日日期是最近MonthPeriod個月的靈修單

                    // 過去　MonthPeriod　個週
                    DateTimeConditionPrincipal.Operator = ConditionOperator.LastXWeeks;
                    DateTimeConditionPrincipal.Values.Add(MonthPeriod);//主日日期是最近幾MonthPeriod週的靈修單

                    // Create the FilterExpression.
                    FilterExpression filterPrincipal = new FilterExpression();
                    // Set the properties of the FilterExpression.
                    filterPrincipal.FilterOperator = LogicalOperator.And;
                    filterPrincipal.AddCondition(WeeklyReportConditionPrincipal);
                    filterPrincipal.AddCondition(ContactConditionPrincipal);
                    filterPrincipal.AddCondition(DateTimeConditionPrincipal);

                    OrderExpression OrderByDate = new OrderExpression();
                    OrderByDate.AttributeName = "new_sunday_date";
                    OrderByDate.OrderType = OrderType.Descending;

                    // Create the QueryExpression.
                    QueryExpression queryPrincipal = new QueryExpression();

                    // Set the properties of the QueryExpression.
                    queryPrincipal.EntityName = @"new_present_record";
                    //queryPrincipal.ColumnSet.AllColumns = true;
                    queryPrincipal.ColumnSet = new ColumnSet("new_sunday_present_this_week", "new_group_present_this_week");
                    queryPrincipal.Criteria = filterPrincipal;
                    queryPrincipal.Orders.Add(OrderByDate);

                    // Create the request object.
                    RetrieveMultipleRequest retrievePrincipal = new RetrieveMultipleRequest();
                    // Set the properties of the request object.
                    retrievePrincipal.Query = queryPrincipal;
                    //retrievePrincipal.ReturnDynamicEntities = true;

                    // Execute the request.
                    RetrieveMultipleResponse principalResponse;
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        principalResponse = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrievePrincipal);
                    }
                    else
                    {
                        principalResponse = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrievePrincipal);
                    }
                    //BusinessEntityCollection BloodReportCollection = aCrmService.RetrieveMultiple(queryPrincipal);

                    return principalResponse.EntityCollection;

                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection RetrieveManyToOneRelationship(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
        {
            try
            {
                //lock (m_QueryManyToOneLocker)
                //{
                    #region // Create the ConditionExpression.
                    ConditionExpression condition = new ConditionExpression();

                    // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                    condition.AttributeName = ParentEntityIdName;
                    condition.Operator = ConditionOperator.Equal;
                    condition.Values.Add(ParentEntityId);

                    ConditionExpression StateCondidtion = new ConditionExpression();
                    // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                    //StateCondidtion.AttributeName = "statuscode";
                    StateCondidtion.AttributeName = "statecode";
                    StateCondidtion.Operator = ConditionOperator.Equal;
                    //StateCondidtion.Values.Add("Inactive");
                    //StateCondidtion.Values.Add("Active");
                    StateCondidtion.Values.Add(0);
                    //StateCondidtion.Values.Add("使用中");

                    // Build the filter that is based on the condition.
                    FilterExpression filter = new FilterExpression();
                    filter.FilterOperator = LogicalOperator.And;
                    filter.Conditions.Add(condition);
                    filter.Conditions.Add(StateCondidtion);
                    #endregion

                    #region // Create a LinkEntity to link the owner's information to the account.
                    LinkEntity link = new LinkEntity()
                    {
                        // Set the LinkEntity properties.
                        LinkCriteria = filter,

                        // Set the linking entity to account.
                        LinkFromEntityName = ChildEntityName,

                        // Set the linking attribute to owninguser.
                        LinkFromAttributeName = AssociationName,

                        // The attribute being linked to is systemuserid.
                        LinkToAttributeName = ParentEntityIdName,

                        // The entity being linked to is systemuser.
                        LinkToEntityName = ParentEntityName
                    };
                    #endregion

                    #region// Create an instance of the query expression class.
                    QueryExpression query = new QueryExpression();

                    // Set the query properties.
                    query.EntityName = ChildEntityName;
                    query.ColumnSet.AllColumns = true;
                    query.LinkEntities.Add(link);
                    #endregion

                    #region// 根據數字排序後傳回來
                    //OrderExpression OrderBySerial = new OrderExpression();
                    //OrderBySerial.AttributeName = "new_order_serial";
                    //OrderBySerial.OrderType = OrderType.Ascending;
                    ////OrderBySerial.OrderType = OrderType.Descending;
                    //query.Orders.Add(OrderBySerial);
                    #endregion

                    #region // 執行 Query 的Request
                    // Create the request.
                    RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                    // Set the request properties.
                    retrieve.Query = query;
                    //retrieve.ReturnDynamicEntities = true;

                    // Execute the request.
                    RetrieveMultipleResponse request;
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                    }
                    else
                    {
                        request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                    }

                    #endregion

                    return request.EntityCollection;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection QueryPresentRecordSortBySunday(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
        {
            try
            {
                #region // Create the ConditionExpression.
                #region ParentEntityIdName Condition
                ConditionExpression condition = new ConditionExpression();
                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                condition.AttributeName = AssociationName;
                condition.Operator = ConditionOperator.Equal;
                condition.Values.Add(ParentEntityId);
                #endregion
                #region StateCondidtion Condition
                ConditionExpression StateCondidtion = new ConditionExpression();
                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                //StateCondidtion.AttributeName = "statuscode";
                StateCondidtion.AttributeName = "statecode";
                StateCondidtion.Operator = ConditionOperator.Equal;
                //StateCondidtion.Values.Add("Inactive");
                //StateCondidtion.Values.Add("Active");
                StateCondidtion.Values.Add(0);
                //StateCondidtion.Values.Add("使用中");
                #endregion

                // Create a ConditionExpression.
                //ConditionExpression StartDateCondidtion = new ConditionExpression("new_join_start_date", ConditionOperator.LessEqual, DateTime.UtcNow);
                //ConditionExpression EndDateCondidtion = new ConditionExpression("new_join_end_date", ConditionOperator.GreaterEqual, DateTime.UtcNow);
                //ConditionExpression StartDateCondidtion = new ConditionExpression("new_class_start_date", ConditionOperator.LessEqual, DateTime.UtcNow);
                ConditionExpression EndDateCondidtion = new ConditionExpression("new_sunday_date", ConditionOperator.GreaterEqual, DateTime.UtcNow.AddDays(-32));


                // Build the filter that is based on the condition.
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(condition);
                filter.Conditions.Add(StateCondidtion);
                //filter.Conditions.Add(StartDateCondidtion);
                filter.Conditions.Add(EndDateCondidtion);
                #endregion

                #region// Create an instance of the query expression class.
                QueryExpression query = new QueryExpression();

                // Set the query properties.
                query.EntityName = ChildEntityName;
                query.ColumnSet.AllColumns = true;

                query.Criteria = filter;
                #endregion

                #region// 根據數字排序後傳回來
                OrderExpression OrderBySunday = new OrderExpression();
                OrderBySunday.AttributeName = "new_sunday_date";
                OrderBySunday.OrderType = OrderType.Ascending;
                ////OrderBySerial.OrderType = OrderType.Descending;
                query.Orders.Add(OrderBySunday);
                #endregion

                #region // 執行 Query 的Request
                // Create the request.
                RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                // Set the request properties.
                retrieve.Query = query;
                //retrieve.ReturnDynamicEntities = true;

                // Execute the request.
                RetrieveMultipleResponse request;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                }
                else
                {
                    request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                }

                #endregion

                return request.EntityCollection;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection QueryPresentRecordSortBySundayFetchXml( int LastWeeks, String ContactName, String ContactId)
        {
            try
            {
                String LastWeeksString = @"'" + LastWeeks.ToString() + @"'";

                ContactName = @"'" + ContactName + @"'";
                ContactId = @"'{" + ContactId + @"}'";

                var fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                      <entity name='new_present_record'>
                        <attribute name='new_present_recordid' />
                        <attribute name='new_name' />
                        <attribute name='createdon' />
                        <attribute name='new_sunday_date' />
                        <attribute name='new_groupleader_present_record' />
                        <attribute name='new_followup_ways' />
                        <attribute name='new_follow_up' />
                        <attribute name='new_conclusion_choise' />
                        <attribute name='new_next_step' />
                        <attribute name='new_explanation' />
                        <order attribute='new_name' descending='false' />
                        <filter type='and'>
                          <condition attribute='new_contact_new_present_record' operator='eq' uiname=" + ContactName + @" uitype ='contact' value=" + ContactId + @" />
                          <condition attribute='new_sunday_date' operator='last-x-weeks' value="  + LastWeeksString + @" />
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
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection QueryPresentRecordSortBySunday_BACKUP(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
        {
            try
            {
                //lock (m_QueryManyToOneLocker)
                //{
                #region // Create the ConditionExpression.
                ConditionExpression condition = new ConditionExpression();

                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                condition.AttributeName = ParentEntityIdName;
                condition.Operator = ConditionOperator.Equal;
                condition.Values.Add(ParentEntityId);

                ConditionExpression StateCondidtion = new ConditionExpression();
                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                //StateCondidtion.AttributeName = "statuscode";
                StateCondidtion.AttributeName = "statecode";
                StateCondidtion.Operator = ConditionOperator.Equal;
                //StateCondidtion.Values.Add("Inactive");
                //StateCondidtion.Values.Add("Active");
                StateCondidtion.Values.Add(0);
                //StateCondidtion.Values.Add("使用中");

                // Build the filter that is based on the condition.
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(condition);
                filter.Conditions.Add(StateCondidtion);
                #endregion

                #region // Create a LinkEntity to link the owner's information to the account.
                LinkEntity link = new LinkEntity()
                {
                    // Set the LinkEntity properties.
                    LinkCriteria = filter,

                    // Set the linking entity to account.
                    LinkFromEntityName = ChildEntityName,

                    // Set the linking attribute to owninguser.
                    LinkFromAttributeName = AssociationName,

                    // The attribute being linked to is systemuserid.
                    LinkToAttributeName = ParentEntityIdName,

                    // The entity being linked to is systemuser.
                    LinkToEntityName = ParentEntityName
                };
                #endregion

                #region// Create an instance of the query expression class.
                QueryExpression query = new QueryExpression();

                // Set the query properties.
                query.EntityName = ChildEntityName;
                query.ColumnSet.AllColumns = true;
                query.LinkEntities.Add(link);
                #endregion

                #region// 根據數字排序後傳回來
                OrderExpression OrderBySunday = new OrderExpression();
                OrderBySunday.AttributeName = "new_sunday_date";
                OrderBySunday.OrderType = OrderType.Ascending;
                ////OrderBySerial.OrderType = OrderType.Descending;
                query.Orders.Add(OrderBySunday);
                #endregion

                #region // 執行 Query 的Request
                // Create the request.
                RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                // Set the request properties.
                retrieve.Query = query;
                //retrieve.ReturnDynamicEntities = true;

                // Execute the request.
                RetrieveMultipleResponse request;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                }
                else
                {
                    request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                }

                #endregion

                return request.EntityCollection;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection QueryPresentRecordInWeeklyReportByContactId(Guid aContactId, Guid aWeeklyReportEntityId)
        {
            try
            {
                #region // Create the ConditionExpression.
                ConditionExpression condition = new ConditionExpression();

                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                condition.AttributeName = "new_group_present_weekly_report_prese";
                condition.Operator = ConditionOperator.Equal;
                condition.Values.Add(aWeeklyReportEntityId);

                ConditionExpression StateCondidtion = new ConditionExpression();
                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                //StateCondidtion.AttributeName = "statuscode";
                StateCondidtion.AttributeName = "statecode";
                StateCondidtion.Operator = ConditionOperator.Equal;
                //StateCondidtion.Values.Add("Inactive");
                //StateCondidtion.Values.Add("Active");
                StateCondidtion.Values.Add(0);
                //StateCondidtion.Values.Add("使用中");

                //ConditionExpression DateTimeConditionPrincipal = new ConditionExpression("new_sunday_date", ConditionOperator.Equal, aSunday);
                //ConditionExpression DateTimeConditionPrincipal = new ConditionExpression("new_sunday_date", ConditionOperator.Equal, aSunday.ToShortDateString());
                ConditionExpression ContactCondition = new ConditionExpression();
                ContactCondition.AttributeName = "new_contact_new_present_record";
                ContactCondition.Operator = ConditionOperator.Equal;
                ContactCondition.Values.Add(aContactId);



                // Build the filter that is based on the condition.
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(condition);
                filter.Conditions.Add(StateCondidtion);
                filter.Conditions.Add(ContactCondition);
                #endregion

                #region// Create an instance of the query expression class.

                QueryExpression query = new QueryExpression();

                // Set the query properties.
                query.EntityName = "new_present_record";
                query.ColumnSet.AllColumns = true;
                query.Criteria = filter;
                #endregion

                #region // 執行 Query 的Request
                // Create the request.
                RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                // Set the request properties.
                retrieve.Query = query;
                //retrieve.ReturnDynamicEntities = true;

                // Execute the request.
                RetrieveMultipleResponse request;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                }
                else
                {
                    request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                }

                #endregion

                return request.EntityCollection;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection QueryEntityListByDate(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
        {
            try
            {
                //lock (m_QueryManyToOneLocker)
                {
                    #region // Create the ConditionExpression.
                    #region ParentEntityIdName Condition
                    ConditionExpression condition = new ConditionExpression();
                    // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                    condition.AttributeName = AssociationName;
                    condition.Operator = ConditionOperator.Equal;
                    condition.Values.Add(ParentEntityId);
                    #endregion
                    #region StateCondidtion Condition
                    ConditionExpression StateCondidtion = new ConditionExpression();
                    // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                    //StateCondidtion.AttributeName = "statuscode";
                    StateCondidtion.AttributeName = "statecode";
                    StateCondidtion.Operator = ConditionOperator.Equal;
                    //StateCondidtion.Values.Add("Inactive");
                    //StateCondidtion.Values.Add("Active");
                    StateCondidtion.Values.Add(0);
                    //StateCondidtion.Values.Add("使用中");
                    #endregion

                    // Create a ConditionExpression.
                    //ConditionExpression StartDateCondidtion = new ConditionExpression("new_join_start_date", ConditionOperator.LessEqual, DateTime.UtcNow);
                    //ConditionExpression EndDateCondidtion = new ConditionExpression("new_join_end_date", ConditionOperator.GreaterEqual, DateTime.UtcNow);
                    //ConditionExpression StartDateCondidtion = new ConditionExpression("new_class_start_date", ConditionOperator.LessEqual, DateTime.UtcNow);
                    //ConditionExpression EndDateCondidtion = new ConditionExpression("new_class_end_date", ConditionOperator.OnOrAfter, DateTime.UtcNow);//課程報名後7天仍然可以繳費
                    //ConditionExpression EndDateCondidtion = new ConditionExpression("new_class_end_date", ConditionOperator.OnOrAfter, DateTime.UtcNow.AddDays(-7));//課程報名後7天仍然可以繳費
                    //ConditionExpression EndDateCondidtion = new ConditionExpression("new_class_end_date", ConditionOperator.OnOrBefore, DateTime.UtcNow.AddDays(7));//課程報名後7天仍然可以繳費
                    //ConditionExpression EndDateCondidtion = new ConditionExpression("new_class_end_date", ConditionOperator.OnOrBefore, DateTime.UtcNow.AddDays(7));//課程報名後7天仍然可以繳費

                    ConditionExpression DateTimeAfterConditionPrincipal = new ConditionExpression();
                    DateTimeAfterConditionPrincipal.AttributeName = "new_class_end_date";//課程開始日期
                    DateTimeAfterConditionPrincipal.Operator = ConditionOperator.OnOrAfter;
                    //DateTimeAfterConditionPrincipal.Operator = ConditionOperator.OnOrBefore;
                    DateTimeAfterConditionPrincipal.Values.Add(DateTime.UtcNow.AddDays(-7));//課程開始日期後7天仍然可以繳費

                    // Build the filter that is based on the condition.
                    FilterExpression filter = new FilterExpression();
                    filter.FilterOperator = LogicalOperator.And;
                    filter.Conditions.Add(condition);
                    filter.Conditions.Add(StateCondidtion);
                    filter.Conditions.Add(DateTimeAfterConditionPrincipal);
                    //filter.Conditions.Add(StartDateCondidtion);
                    //filter.Conditions.Add(EndDateCondidtion);
                    #endregion

                    #region// Create an instance of the query expression class.
                    QueryExpression query = new QueryExpression();

                    // Set the query properties.
                    query.EntityName = ChildEntityName;
                    query.ColumnSet.AllColumns = true;
                    query.Criteria = filter;
                    #endregion

                    #region// 根據數字排序後傳回來
                    //OrderExpression OrderBySerial = new OrderExpression();
                    //OrderBySerial.AttributeName = "listname";
                    //OrderBySerial.OrderType = OrderType.Ascending;
                    //query.Orders.Add(OrderBySerial);
                    #endregion

                    #region // 執行 Query 的Request
                    // Create the request.
                    RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                    // Set the request properties.
                    retrieve.Query = query;
                    //retrieve.ReturnDynamicEntities = true;

                    // Execute the request
                    RetrieveMultipleResponse request;
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                    }
                    else
                    {
                        request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                    }
                    #endregion

                    return request.EntityCollection;
                }

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection RetrieveManyToOneRelationship()
        {
            try
            {
                //lock (m_QueryManyToOneLocker)
                //{
                    var query = new QueryExpression("contact");

                    var columnNames = new[] { "fullname", "address1_city" };

                    query.ColumnSet = new ColumnSet(columnNames);

                    // ‘Account’ as LinkEntity

                    var colsAccount = new[] { "accountnumber" };

                    LinkEntity linkEntityAccount = new LinkEntity()
                    {
                        #region LinkEntity 屬性
                        LinkFromEntityName = "contact",

                        LinkFromAttributeName = "parentcustomerid",

                        LinkToEntityName = "account",

                        LinkToAttributeName = "accountid",

                        JoinOperator = JoinOperator.Inner,

                        Columns = new ColumnSet(colsAccount),

                        EntityAlias = "aliasAccount"
                        #endregion
                    };

                    query.LinkEntities.Add(linkEntityAccount);

                    // Execute Query using RetrieveMultiple

                    EntityCollection contacts = this.m_OrganizationService.RetrieveMultiple(query);
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        contacts = this.m_OrganizationService.RetrieveMultiple(query);
                    }
                    else
                    {
                        contacts = this.m_Crm2011OrganizationService.RetrieveMultiple(query);
                    }

                    if (contacts != null)
                    {

                        foreach (var targetEntity in contacts.Entities)
                        {

                            // Read “Account Number” along with Alias

                            var accountNumber = getAttributeValue(targetEntity, "aliasAccount.accountnumber");

                            var contactFullname = getAttributeValue(targetEntity, "fullname");

                        }
                    }// if

                    return contacts;

                //}// lock
            }//try
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection QueryWeeklyReportBySunday(DateTime aSunday, String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
        {
            try
            {
                //lock (m_QueryManyToOneLocker)
                //{
                    #region // Create the ConditionExpression.
                    ConditionExpression condition = new ConditionExpression();

                    // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                    condition.AttributeName = ParentEntityIdName;
                    condition.Operator = ConditionOperator.Equal;
                    condition.Values.Add(ParentEntityId);

                    ConditionExpression StateCondidtion = new ConditionExpression();
                    // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                    //StateCondidtion.AttributeName = "statuscode";
                    StateCondidtion.AttributeName = "statecode";
                    StateCondidtion.Operator = ConditionOperator.Equal;
                    //StateCondidtion.Values.Add("Inactive");
                    //StateCondidtion.Values.Add("Active");
                    StateCondidtion.Values.Add(0);
                    //StateCondidtion.Values.Add("使用中");


                    // Set the ConditionExpressions properties so that the condition is true when the 
                    // ownerid of the account equals the principalId.
                    ConditionExpression DateTimeConditionPrincipal = new ConditionExpression();
                    DateTimeConditionPrincipal.AttributeName = @"new_sunday_date";
                    DateTimeConditionPrincipal.Operator = ConditionOperator.Equal;
                    DateTimeConditionPrincipal.Values.Add(aSunday.ToString());


                    // Build the filter that is based on the condition.
                    FilterExpression filter = new FilterExpression();
                    filter.FilterOperator = LogicalOperator.And;
                    filter.Conditions.Add(condition);
                    filter.Conditions.Add(StateCondidtion);
                    filter.Conditions.Add(DateTimeConditionPrincipal);
                    #endregion

                    #region // Create a LinkEntity to link the owner's information to the account.
                    LinkEntity link = new LinkEntity()
                    {
                        // Set the LinkEntity properties.
                        LinkCriteria = filter,

                        // Set the linking entity to account.
                        LinkFromEntityName = ChildEntityName,

                        // Set the linking attribute to owninguser.
                        LinkFromAttributeName = AssociationName,

                        // The attribute being linked to is systemuserid.
                        LinkToAttributeName = ParentEntityIdName,

                        // The entity being linked to is systemuser.
                        LinkToEntityName = ParentEntityName,

                    };
                    #endregion

                    #region// Create an instance of the query expression class.
                    OrderExpression OrderByDate = new OrderExpression();
                    OrderByDate.AttributeName = "new_sunday_date";
                    OrderByDate.OrderType = OrderType.Descending;

                    QueryExpression query = new QueryExpression();

                    // Set the query properties.
                    query.EntityName = ChildEntityName;
                    query.ColumnSet.AllColumns = true;
                    query.LinkEntities.Add(link);
                    query.Orders.Add(OrderByDate);
                    #endregion

                    #region// 根據數字排序後傳回來
                    //OrderExpression OrderBySerial = new OrderExpression();
                    //OrderBySerial.AttributeName = "new_order_serial";
                    //OrderBySerial.OrderType = OrderType.Ascending;
                    ////OrderBySerial.OrderType = OrderType.Descending;
                    //query.Orders.Add(OrderBySerial);
                    #endregion

                    #region // 執行 Query 的Request
                    // Create the request.
                    RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                    // Set the request properties.
                    retrieve.Query = query;
                    //retrieve.ReturnDynamicEntities = true;

                    // Execute the request.
                    RetrieveMultipleResponse request;
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                    }
                    else
                    {
                        request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                    }

                    #endregion

                    return request.EntityCollection;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection QueryListsAndOrderedByListName(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
        {
            try
            {
                //lock (m_QueryManyToOneLocker)
                //{
                #region // Create the ConditionExpression.
                #region ParentEntityIdName Condition
                ConditionExpression condition = new ConditionExpression();
                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                condition.AttributeName = ParentEntityIdName;
                condition.Operator = ConditionOperator.Equal;
                condition.Values.Add(ParentEntityId);
                #endregion
                #region StateCondidtion Condition
                ConditionExpression StateCondidtion = new ConditionExpression();
                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                //StateCondidtion.AttributeName = "statuscode";
                StateCondidtion.AttributeName = "statecode";
                StateCondidtion.Operator = ConditionOperator.Equal;
                //StateCondidtion.Values.Add("Inactive");
                //StateCondidtion.Values.Add("Active");
                StateCondidtion.Values.Add(0);
                //StateCondidtion.Values.Add("使用中");
                #endregion

                // Build the filter that is based on the condition.
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(condition);
                filter.Conditions.Add(StateCondidtion);
                #endregion

                #region // Create a LinkEntity to link the owner's information to the account.
                LinkEntity link = new LinkEntity()
                {
                    // Set the LinkEntity properties.
                    LinkCriteria = filter,

                    // Set the linking entity to account.
                    LinkFromEntityName = ChildEntityName,

                    // Set the linking attribute to owninguser.
                    LinkFromAttributeName = AssociationName,

                    // The attribute being linked to is systemuserid.
                    LinkToAttributeName = ParentEntityIdName,

                    // The entity being linked to is systemuser.
                    LinkToEntityName = ParentEntityName
                };
                #endregion

                #region// Create an instance of the query expression class.
                QueryExpression query = new QueryExpression();

                // Set the query properties.
                query.EntityName = ChildEntityName;
                query.ColumnSet.AllColumns = true;
                query.LinkEntities.Add(link);
                #endregion

                #region// 根據數字排序後傳回來
                OrderExpression OrderBySerial = new OrderExpression();
                OrderBySerial.AttributeName = "listname";
                OrderBySerial.OrderType = OrderType.Ascending;
                ////OrderBySerial.OrderType = OrderType.Descending;
                query.Orders.Add(OrderBySerial);
                #endregion

                #region // 執行 Query 的Request
                // Create the request.
                RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                // Set the request properties.
                retrieve.Query = query;
                //retrieve.ReturnDynamicEntities = true;

                // Execute the request
                RetrieveMultipleResponse request;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                }
                else
                {
                    request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                }
                #endregion

                return request.EntityCollection;
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection QueryListByContactId(Guid aContactId, String AssociationName)
        {
            try
            {
                #region // Create the ConditionExpression.
                ConditionExpression ContactCondition = new ConditionExpression();
                ContactCondition.AttributeName = AssociationName;
                ContactCondition.Operator = ConditionOperator.Equal;
                ContactCondition.Values.Add(aContactId);

                // 狀態是使用中
                ConditionExpression StateCondidtion = new ConditionExpression();
                StateCondidtion.AttributeName = "statecode";
                StateCondidtion.Operator = ConditionOperator.Equal;
                StateCondidtion.Values.Add(0);
                //StateCondidtion.Values.Add("使用中");

                // 點名有打勾
                ConditionExpression AppCondidtion = new ConditionExpression();
                AppCondidtion.AttributeName = "new_app_named";
                AppCondidtion.Operator = ConditionOperator.Equal;
                AppCondidtion.Values.Add(true);


                // Build the filter that is based on the condition.
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(ContactCondition);
                filter.Conditions.Add(StateCondidtion);
                filter.Conditions.Add(AppCondidtion);
                #endregion

                #region// Create an instance of the query expression class.

                QueryExpression query = new QueryExpression();

                // Set the query properties.
                query.EntityName = "list";
                query.ColumnSet.AllColumns = true;
                query.Criteria = filter;
                #endregion

                #region// 根據名稱排序後傳回來
                OrderExpression OrderBySerial = new OrderExpression();
                OrderBySerial.AttributeName = "listname";
                OrderBySerial.OrderType = OrderType.Ascending;
                ////OrderBySerial.OrderType = OrderType.Descending;
                query.Orders.Add(OrderBySerial);
                #endregion

                #region // 執行 Query 的Request
                // Create the request.
                RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                // Set the request properties.
                retrieve.Query = query;
                //retrieve.ReturnDynamicEntities = true;

                // Execute the request.
                RetrieveMultipleResponse request;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                }
                else
                {
                    request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                }

                #endregion

                return request.EntityCollection;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection QueryWeeklyReportBySunday(DateTime aSunday, Guid aListEntityId)
        {
            try
            {
                #region // Create the ConditionExpression.
                ConditionExpression condition = new ConditionExpression();

                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                condition.AttributeName = "new_list_group_present_weekly_report";
                condition.Operator = ConditionOperator.Equal;
                condition.Values.Add(aListEntityId);

                ConditionExpression StateCondidtion = new ConditionExpression();
                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                //StateCondidtion.AttributeName = "statuscode";
                StateCondidtion.AttributeName = "statecode";
                StateCondidtion.Operator = ConditionOperator.Equal;
                //StateCondidtion.Values.Add("Inactive");
                //StateCondidtion.Values.Add("Active");
                StateCondidtion.Values.Add(0);
                //StateCondidtion.Values.Add("使用中");

                //ConditionExpression DateTimeConditionPrincipal = new ConditionExpression("new_sunday_date", ConditionOperator.Equal, aSunday);
                //ConditionExpression DateTimeConditionPrincipal = new ConditionExpression("new_sunday_date", ConditionOperator.Equal, aSunday.ToShortDateString());
                ConditionExpression DateTimeConditionPrincipal = new ConditionExpression();
                DateTimeConditionPrincipal.AttributeName = "new_sunday_date";
                DateTimeConditionPrincipal.Operator = ConditionOperator.On;
                DateTimeConditionPrincipal.Values.Add(aSunday);



                // Build the filter that is based on the condition.
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(condition);
                filter.Conditions.Add(StateCondidtion);
                filter.Conditions.Add(DateTimeConditionPrincipal);
                #endregion

                #region// Create an instance of the query expression class.
                OrderExpression OrderByDate = new OrderExpression();
                OrderByDate.AttributeName = "new_sunday_date";
                OrderByDate.OrderType = OrderType.Descending;

                QueryExpression query = new QueryExpression();

                // Set the query properties.
                query.EntityName = "new_group_present_weekly_report";
                //query.ColumnSet.AllColumns = true;
                query.ColumnSet = new ColumnSet("new_small_group_member_number", "new_sunday_present_number", "new_sunday_present_rate", "new_small_group_number", "new_small_group_rate", "new_memo");
                //query.ColumnSet = new ColumnSet();

                query.Criteria = filter;
                query.Orders.Add(OrderByDate);
                #endregion

                #region // 執行 Query 的Request
                // Create the request.
                RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                // Set the request properties.
                retrieve.Query = query;
                //retrieve.ReturnDynamicEntities = true;

                // Execute the request.
                RetrieveMultipleResponse request;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                }
                else
                {
                    request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                }

                #endregion

                return request.EntityCollection;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection QueryWeeklyReportBeforeTowMonthOfSunday(DateTime aSunday, Guid aListEntityId)
        {
            try
            {
                #region // Create the ConditionExpression.
                ConditionExpression condition = new ConditionExpression();

                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                condition.AttributeName = "new_list_group_present_weekly_report";
                condition.Operator = ConditionOperator.Equal;
                condition.Values.Add(aListEntityId);

                ConditionExpression StateCondidtion = new ConditionExpression();
                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                //StateCondidtion.AttributeName = "statuscode";
                StateCondidtion.AttributeName = "statecode";
                StateCondidtion.Operator = ConditionOperator.Equal;
                //StateCondidtion.Values.Add("Inactive");
                //StateCondidtion.Values.Add("Active");
                StateCondidtion.Values.Add(0);
                //StateCondidtion.Values.Add("使用中");

                //ConditionExpression DateTimeConditionPrincipal = new ConditionExpression("new_sunday_date", ConditionOperator.Equal, aSunday);
                //ConditionExpression DateTimeConditionPrincipal = new ConditionExpression("new_sunday_date", ConditionOperator.Equal, aSunday.ToShortDateString());
                ConditionExpression DateTimeAfterConditionPrincipal = new ConditionExpression();
                DateTimeAfterConditionPrincipal.AttributeName = "new_sunday_date";
                DateTimeAfterConditionPrincipal.Operator = ConditionOperator.OnOrAfter;
                DateTimeAfterConditionPrincipal.Values.Add(aSunday.AddMonths(-2));


                ConditionExpression DateTimeBeforeConditionPrincipal = new ConditionExpression();
                DateTimeBeforeConditionPrincipal.AttributeName = "new_sunday_date";
                DateTimeBeforeConditionPrincipal.Operator = ConditionOperator.OnOrBefore;
                DateTimeBeforeConditionPrincipal.Values.Add(aSunday);

                // Build the filter that is based on the condition.
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(condition);
                filter.Conditions.Add(StateCondidtion);
                filter.Conditions.Add(DateTimeAfterConditionPrincipal);
                filter.Conditions.Add(DateTimeBeforeConditionPrincipal);
                #endregion

                #region// Create an instance of the query expression class.
                OrderExpression OrderByDate = new OrderExpression();
                OrderByDate.AttributeName = "new_sunday_date";
                //OrderByDate.OrderType = OrderType.Descending;
                OrderByDate.OrderType = OrderType.Ascending;

                QueryExpression query = new QueryExpression();

                // Set the query properties.
                query.EntityName = "new_group_present_weekly_report";
                query.ColumnSet.AllColumns = true;
                query.Criteria = filter;
                query.Orders.Add(OrderByDate);
                #endregion

                #region // 執行 Query 的Request
                // Create the request.
                RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                // Set the request properties.
                retrieve.Query = query;
                //retrieve.ReturnDynamicEntities = true;

                // Execute the request.
                RetrieveMultipleResponse request;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                }
                else
                {
                    request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                }

                #endregion

                return request.EntityCollection;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }
        public EntityCollection RetrieveContactCollectionByLineId(String LineId)
        {
            try
            {
                //lock (m_RetrieveContactLocker)
                {
                    //  Create query using querybyattribute
                    QueryByAttribute querybyexpression = new QueryByAttribute("contact");
                    querybyexpression.ColumnSet = new ColumnSet();
                    querybyexpression.ColumnSet.AllColumns = true;
                    //  Attribute to query
                    querybyexpression.Attributes.AddRange("new_lineid", "statecode");
                    //  Value of queried attribute to return
                    querybyexpression.Values.AddRange(LineId, 0);

                    //  Query passed to the service proxy
                    //EntityCollection retrieved = aOrganizationService.RetrieveMultiple(querybyexpression);

                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        return this.m_OrganizationService.RetrieveMultiple(querybyexpression);
                    }
                    else
                    {
                        return this.m_Crm2011OrganizationService.RetrieveMultiple(querybyexpression);
                    }

                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public EntityCollection RetrieveMemberListCollectionByListIdDynamics365(Guid aListId)
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

                EntityCollection entityCollection = this.m_OrganizationService.RetrieveMultiple(query);
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
            var entity = this.m_OrganizationService.Retrieve("list", aListId, cols);
            var dynamicQuery = entity.Attributes["query"].ToString();

            EntityCollection dynamicmemberec = this.m_OrganizationService.RetrieveMultiple(new FetchExpression(dynamicQuery));
            return dynamicmemberec;
        }

        #endregion
        #region 搜尋 N:N( ManyToMany) 的集合

        public EntityCollection QueryManyToMany(String ConditionAttributeName, String EntityNameToSearch, String LinkFromEntityName, String LinkFromAttributeName, String LinkToEntityName, String LinkToAttributeName, String AttributeName, Guid EntityIdValue)
        {
            try
            {
                #region 說明:例如我要找連絡人的關聯的小組名單
                // String ConditionAttributeName    : 名單的是否要點名，值是 Boolean
                //String EntityNameToSearch         : 要搜尋出來的實體名稱，名單的 Entity Name = "list"
                //String LinkFromEntityName         : 兒子的實體名稱 => 名單 = "list"
                //String LinkFromAttributeName      : 兒子實體的 Id 名稱 => 名單的 Id 名稱 = "listid"
                //String LinkToEntityName           : Intersect 的名稱，也就是 N:N 的名稱 => "listname" 
                //String LinkToAttributeName        : Intersect 連到的 id名稱 => "listid"
                //String AttributeName              : Intersect或是父的 Id 名稱 => 連絡人的 Id名稱  
                //Guid EntityIdValue                : Intersect或是父的 Id 值 => 連絡人的 Id 值 
                #endregion

                #region // 過濾條件
                ConditionExpression condition = new ConditionExpression();

                // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                condition.AttributeName = ConditionAttributeName;
                condition.Operator = ConditionOperator.Equal;
                condition.Values.Add(true);

                ConditionExpression StateCondidtion = new ConditionExpression();
                StateCondidtion.AttributeName = "statecode";
                StateCondidtion.Operator = ConditionOperator.Equal;
                StateCondidtion.Values.Add(0);

                // Build the filter that is based on the condition.
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(condition);
                filter.Conditions.Add(StateCondidtion);
                #endregion

                //Create Query Expression.
                QueryExpression query = new QueryExpression()
                {
                    Criteria = filter,
                    EntityName = EntityNameToSearch,
                    LinkEntities =
                        {
                            new LinkEntity
                            {
                                LinkFromEntityName = LinkFromEntityName,
                                LinkFromAttributeName = LinkFromAttributeName,
                                LinkToEntityName = LinkToEntityName,
                                LinkToAttributeName = LinkToAttributeName,


                                LinkCriteria = new FilterExpression
                                {
                                    FilterOperator = LogicalOperator.And,
                                    Conditions =
                                    {
                                        new ConditionExpression
                                        {
                                            //AttributeName = "systemuserid",
                                            //AttributeName = "contactid",
                                            AttributeName = AttributeName,
                                            Operator = ConditionOperator.Equal,
                                            Values = { EntityIdValue }
                                        }
                                    }
                                }
                            }
                        }

                };

                query.ColumnSet.AllColumns = true;

                // Execute the request.
                EntityCollection request;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    request = (EntityCollection)this.m_OrganizationService.RetrieveMultiple(query);
                }
                else
                {
                    request = (EntityCollection)this.m_Crm2011OrganizationService.RetrieveMultiple(query);
                }

                return request;

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }

        // 連絡人相關的各類名單:
        // 過濾條件:需要點名的各類名單
        public EntityCollection QueryListOfContactManyToMany(Guid ContactId)
        {
            try
            {
                #region // 過濾條件:需要點名的各類名單及使用中的
                ConditionExpression condition = new ConditionExpression();

                // 需要點名的
                condition.AttributeName = "new_app_named";
                condition.Operator = ConditionOperator.Equal;
                condition.Values.Add(true);

                // 使用中的
                ConditionExpression StateCondidtion = new ConditionExpression();
                StateCondidtion.AttributeName = "statecode";
                StateCondidtion.Operator = ConditionOperator.Equal;
                StateCondidtion.Values.Add(0);

                // 合併過濾條件
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(condition);
                filter.Conditions.Add(StateCondidtion);
                #endregion

                //Create Query Expression.
                QueryExpression query = new QueryExpression()
                {
                    Criteria = filter,
                    EntityName = "list",
                    LinkEntities =
                        {
                            new LinkEntity
                            {
                                LinkFromEntityName = "list",
                                LinkFromAttributeName = "listid",
                                LinkToEntityName = "listmember",
                                LinkToAttributeName = "listid",

                                //LinkFromEntityName = "role",
                                //LinkFromAttributeName = "roleid",
                                //LinkToEntityName = "systemuserroles",
                                //LinkToAttributeName = "roleid",
 
                                LinkCriteria = new FilterExpression
                                {
                                    FilterOperator = LogicalOperator.And,
                                    Conditions =
                                    {
                                        new ConditionExpression
                                        {
                                            AttributeName = "entityid",
                                            Operator = ConditionOperator.Equal,
                                            Values = { ContactId }
                                        }
                                    }
                                }
                            }
                        }

                };

                query.ColumnSet.AllColumns = true;

                // Execute the request.
                EntityCollection request;
                if (CRM_TYPE == "DYNAMICS365")
                {
                    request = (EntityCollection)this.m_OrganizationService.RetrieveMultiple(query);
                }
                else
                {
                    request = (EntityCollection)this.m_Crm2011OrganizationService.RetrieveMultiple(query);
                }

                return request;

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }


        // 搜尋的正式版本，因為可以搜尋子實體的屬性
        public EntityCollection QueryEntityList(String ParentEntityName, String ParentEntityIdName, String ParentEntityId, String AssociationName, String ChildEntityName)
        {
            try
            {
                //lock (m_QueryManyToOneLocker)
                {
                    #region // Create the ConditionExpression.
                    #region ParentEntityIdName Condition
                    ConditionExpression condition = new ConditionExpression();
                    // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                    condition.AttributeName = AssociationName;
                    condition.Operator = ConditionOperator.Equal;
                    condition.Values.Add(ParentEntityId);
                    #endregion
                    #region StateCondidtion Condition
                    ConditionExpression StateCondidtion = new ConditionExpression();
                    // Set the condition to be when the account owner's last name is not Cannon. new_new_receive_drugs_prescribed_new_
                    //StateCondidtion.AttributeName = "statuscode";
                    StateCondidtion.AttributeName = "statecode";
                    StateCondidtion.Operator = ConditionOperator.Equal;
                    //StateCondidtion.Values.Add("Inactive");
                    //StateCondidtion.Values.Add("Active");
                    StateCondidtion.Values.Add(0);
                    //StateCondidtion.Values.Add("使用中");
                    #endregion

                    // Create a ConditionExpression.
                    //ConditionExpression StartDateCondidtion = new ConditionExpression("new_join_start_date", ConditionOperator.LessEqual, DateTime.UtcNow);
                    //ConditionExpression EndDateCondidtion = new ConditionExpression("new_join_end_date", ConditionOperator.GreaterEqual, DateTime.UtcNow);


                    // Build the filter that is based on the condition.
                    FilterExpression filter = new FilterExpression();
                    filter.FilterOperator = LogicalOperator.And;
                    filter.Conditions.Add(condition);
                    filter.Conditions.Add(StateCondidtion);
                    //filter.Conditions.Add(StartDateCondidtion);
                    //filter.Conditions.Add(EndDateCondidtion);
                    #endregion

                    #region// Create an instance of the query expression class.
                    QueryExpression query = new QueryExpression();

                    // Set the query properties.
                    query.EntityName = ChildEntityName;
                    query.ColumnSet.AllColumns = true;
                    query.Criteria = filter;
                    #endregion

                    #region// 根據數字排序後傳回來
                    //OrderExpression OrderBySerial = new OrderExpression();
                    //OrderBySerial.AttributeName = "listname";
                    //OrderBySerial.OrderType = OrderType.Ascending;
                    //query.Orders.Add(OrderBySerial);
                    #endregion

                    #region // 執行 Query 的Request
                    // Create the request.
                    RetrieveMultipleRequest retrieve = new RetrieveMultipleRequest();

                    // Set the request properties.
                    retrieve.Query = query;
                    //retrieve.ReturnDynamicEntities = true;

                    // Execute the request
                    RetrieveMultipleResponse request;
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        request = (RetrieveMultipleResponse)this.m_OrganizationService.Execute(retrieve);
                    }
                    else
                    {
                        request = (RetrieveMultipleResponse)this.m_Crm2011OrganizationService.Execute(retrieve);
                    }
                    #endregion

                    return request.EntityCollection;
                }

            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                //Monitor.Exit(this);
                throw e;
            }
        }

        #endregion
        #region 實體操作區
        #region 新增、修改、刪除實體
        //private readonly object m_EntityLocker = new object();
        public Entity RetrieveEntity(String EntityName, Guid EntityId)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        return this.m_OrganizationService.Retrieve(EntityName, EntityId, new ColumnSet(true));
                    }
                    else
                    {
                        return this.m_Crm2011OrganizationService.Retrieve(EntityName, EntityId, new ColumnSet(true));
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity RetrieveEntityDynamics365(String EntityName, Guid EntityId)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    return this.m_OrganizationService.Retrieve(EntityName, EntityId, new ColumnSet(true));
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Entity RetrieveEntityCrm2011(String EntityName, Guid EntityId)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    return this.m_Crm2011OrganizationService.Retrieve(EntityName, EntityId, new ColumnSet(true));
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Guid CreateEntity(Entity aEntityTobeToCreate)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            return this.m_OrganizationService.Create(aEntityTobeToCreate);
                        }
                        else
                        {
                            return this.m_Crm2011OrganizationService.Create(aEntityTobeToCreate);
                        }
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Guid CreateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, Entity aEntityTobeToCreate)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        return aOrganizationService.Create(aEntityTobeToCreate);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Guid CreateEntityCrm2011(ref IOrganizationService aCrm2011OrganizationService, Entity aEntityTobeToCreate)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        return aCrm2011OrganizationService.Create(aEntityTobeToCreate);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public async Task<Guid> CreateEntityAsync(IOrganizationService aOrganizationService, Entity aEntityTobeToCreate)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        return aOrganizationService.Create(aEntityTobeToCreate);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void UpdateEntity(ref Entity aEntityTobeUpdated)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            this.m_OrganizationService.Update(aEntityTobeUpdated);
                        }
                        else
                        {
                            this.m_Crm2011OrganizationService.Update(aEntityTobeUpdated);
                        }
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void UpdateEntity(Entity aEntityTobeUpdated)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        if (CRM_TYPE == "DYNAMICS365")
                        {
                            this.m_OrganizationService.Update(aEntityTobeUpdated);
                        }
                        else
                        {
                            this.m_Crm2011OrganizationService.Update(aEntityTobeUpdated);
                        }
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void UpdateEntity(ref IOrganizationService aOrganizationService, ref Entity aEntityTobeUpdated)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        aOrganizationService.Update(aEntityTobeUpdated);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void UpdateEntity(ref IOrganizationService aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        aOrganizationService.Update(aEntityTobeUpdated);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void UpdateEntityCrm2011(ref IOrganizationService aOrganizationService, ref Entity aEntityTobeUpdated)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        aOrganizationService.Update(aEntityTobeUpdated);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void UpdateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, ref Entity aEntityTobeUpdated)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        aOrganizationService.Update(aEntityTobeUpdated);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void UpdateEntityCrm2011(ref IOrganizationService aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        aOrganizationService.Update(aEntityTobeUpdated);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void UpdateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        aOrganizationService.Update(aEntityTobeUpdated);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public async Task UpdateEntityAsync(IOrganizationService aOrganizationService, Entity aEntityTobeUpdated)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        aOrganizationService.Update(aEntityTobeUpdated);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void DeleteEntity(ref IOrganizationService aOrganizationService, String aEntityName, Guid aEntityId)
        {
            try
            {
                //lock (m_EntityLocker)
                //{
                    if (EXCUTION_FLAG == true)
                    {
                        aOrganizationService.Delete(aEntityName, aEntityId);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public void DeleteEntity(String aEntityName, Guid aEntityId)
        {
            try
            {
                if (EXCUTION_FLAG == true)
                {
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        this.m_OrganizationService.Delete(aEntityName, aEntityId);
                    }
                    else
                    {
                        this.m_Crm2011OrganizationService.Delete(aEntityName, aEntityId);
                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        public Guid GetEntityId(Entity aEntity)
        {
            try
            {
                return aEntity.Id;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();
                throw e;
            }
        }
        #endregion
        #endregion
        #region 屬性操作區
        #region 布林屬性
        //private readonly object m_BooleanAttributeLocker = new object();

        public bool GetEntityBoolAttribute(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_BooleanAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (bool)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return false;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public bool GetEntityBoolAttribute(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_BooleanAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (bool)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return false;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityBoolAttribute(ref Entity aEntity, string PropertyName, bool PropertyValue)
        {
            try
            {
                //lock (m_BooleanAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = PropertyValue;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, PropertyValue);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityBoolAttributeToNull(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_BooleanAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = null;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, null);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 整數屬性
        //private readonly object m_IntAttributeLocker = new object();

        public int GetEntityIntAttribute(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_IntAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (int)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return -9999;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public int GetEntityIntAttribute(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_IntAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (int)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return -9999;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityIntAttribute(ref Entity aEntity, string PropertyName, int PropertyValue)
        {
            try
            {
                //lock (m_IntAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = PropertyValue;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, PropertyValue);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityIntAttributeToNull(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_IntAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = null;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, null);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 浮點屬性
        //private readonly object m_FloatAttributeLocker = new object();

        public float GetEntityFloatAttribute(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_FloatAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (float)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return -9999;
                    //}
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public float GetEntityFloatAttribute(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_FloatAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (float)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return -9999;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityFloatAttribute(ref Entity aEntity, string PropertyName, float PropertyValue)
        {
            try
            {
                //lock (m_FloatAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = PropertyValue;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, PropertyValue);
                    //}
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityFloatAttribute(Entity aEntity, string PropertyName, float PropertyValue)
        {
            try
            {
                //lock (m_FloatAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = PropertyValue;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, PropertyValue);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityFloatAttributeToNull(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_FloatAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = null;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, null);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 金額屬性
        //private readonly object m_MoneyAttributeLocker = new object();

        public Money GetEntityMoneyAttribute(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_MoneyAttributeLocker)
                {
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (Money)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return new Money(-9999);
                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public Money GetEntityMoneyAttribute(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_MoneyAttributeLocker)
                {
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (Money)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return new Money(-9999);
                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityMoneyAttribute(ref Entity aEntity, string PropertyName, Money PropertyValue)
        {
            try
            {
                //lock (m_MoneyAttributeLocker)
                {
                    if (PropertyValue.Value != -9999)
                    {
                        if (aEntity.Attributes.Contains(PropertyName))
                        {
                            aEntity.Attributes[PropertyName] = PropertyValue;
                        }
                        else
                        {
                            aEntity.Attributes.Add(PropertyName, PropertyValue);
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityMoneyAttribute(Entity aEntity, string PropertyName, Money PropertyValue)
        {
            try
            {
                //lock (m_MoneyAttributeLocker)
                {
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = PropertyValue;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, PropertyValue);
                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityMoneyAttributeToNull(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_MoneyAttributeLocker)
                {

                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = null;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, null);
                    }
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 小數點屬性
        //private readonly object m_DoubleAttributeLocker = new object();

        public Double GetEntityDoubleAttribute(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_FloatAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (Double)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return -9999;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public Double GetEntityDoubleAttribute(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_FloatAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (Double)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return -9999;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityDoubleAttribute(ref Entity aEntity, string PropertyName, Double PropertyValue)
        {
            try
            {
                //lock (m_FloatAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = PropertyValue;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, PropertyValue);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityDoubleAttribute(Entity aEntity, string PropertyName, Double PropertyValue)
        {
            try
            {
                //lock (m_FloatAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = PropertyValue;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, PropertyValue);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityDoubleAttributeToNull(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_FloatAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = null;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, null);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 時間屬性
        //private readonly object m_DateTimeAttributeLocker = new object();
        public DateTime GetEntityDateTimeAttribute(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_DateTimeAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (DateTime)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return new DateTime(1, 1, 1);
                        //return DateTime.Now.AddYears(-9999);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public DateTime GetEntityDateTimeAttribute(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_DateTimeAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (DateTime)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return new DateTime(1, 1, 1);
                        //return DateTime.Now.AddYears(-9999);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                return new DateTime(1, 1, 1);
                //return DateTime.Now.AddYears(-9999);

                //throw e;
            }
        }
        public void SetEntityDateTimeAttribute(ref Entity aEntity, string PropertyName, DateTime PropertyValue)
        {
            try
            {
                //lock (m_DateTimeAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = PropertyValue;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, PropertyValue);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityDateTimeAttributeToNull(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_DateTimeAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = null;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, null);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 文字屬性
        //private readonly object m_StringAttributeLocker = new object();

        public String GetEntityStringAttribute(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_StringAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (String)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return "";
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public String GetEntityStringAttribute(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_StringAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        return (String)aEntity.Attributes[PropertyName];
                    }
                    else
                    {
                        return "";
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public void SetEntityStringAttribute(ref Entity aEntity, string PropertyName, String PropertyValue)
        {
            try
            {
                //lock (m_StringAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = PropertyValue;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, PropertyValue);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityStringAttribute(Entity aEntity, string PropertyName, String PropertyValue)
        {
            try
            {
                //lock (m_StringAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = PropertyValue;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, PropertyValue);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 選項屬性
        //private readonly object m_OptionSetAttributeLocker = new object();
        public int GetOptionSetAttribute(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_OptionSetAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        OptionSetValue aOptionSetValue = new OptionSetValue();

                        aOptionSetValue = (OptionSetValue)aEntity.Attributes[PropertyName];

                        return aOptionSetValue.Value;
                    }
                    else
                    {
                        return EMPTY_VALUE;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public int GetOptionSetAttribute(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_OptionSetAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        OptionSetValue aOptionSetValue = new OptionSetValue();

                        aOptionSetValue = (OptionSetValue)aEntity.Attributes[PropertyName];

                        return aOptionSetValue.Value;
                    }
                    else
                    {
                        return EMPTY_VALUE;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public void SetOptionSetAttribute(ref Entity aEntity, string PropertyName, int PropertyValue)
        {
            try
            {
                //lock (m_OptionSetAttributeLocker)
                //{
                OptionSetValue aOptionSetValue = new OptionSetValue(PropertyValue);

                if (aEntity.Attributes.Contains(PropertyName))
                {
                    aEntity.Attributes[PropertyName] = aOptionSetValue;
                }
                else
                {
                    aEntity.Attributes.Add(PropertyName, aOptionSetValue);
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetOptionSetAttribute(Entity aEntity, string PropertyName, int PropertyValue)
        {
            try
            {
                //lock (m_OptionSetAttributeLocker)
                //{
                OptionSetValue aOptionSetValue = new OptionSetValue(PropertyValue);

                if (aEntity.Attributes.Contains(PropertyName))
                {
                    aEntity.Attributes[PropertyName] = aOptionSetValue;
                }
                else
                {
                    aEntity.Attributes.Add(PropertyName, aOptionSetValue);
                }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetOptionSetAttributeNull(ref Entity aEntity, string PropertyName)
        {
            try
            {
                if (aEntity.Attributes.Contains(PropertyName))
                {
                    aEntity.Attributes[PropertyName] = null;
                }
                else
                {
                    aEntity.Attributes.Add(PropertyName, null);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetOptionSetAttributeNull(Entity aEntity, string PropertyName)
        {
            try
            {
                if (aEntity.Attributes.Contains(PropertyName))
                {
                    aEntity.Attributes[PropertyName] = null;
                }
                else
                {
                    aEntity.Attributes.Add(PropertyName, null);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region LookUp 屬性

        //private readonly object m_LookupAttributeLocker = new object();

        public Guid GetEntityLookupAttribute(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_LookupAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        EntityReference aEntityReference = (EntityReference)aEntity.Attributes[PropertyName];

                        return aEntityReference.Id;
                    }
                    else
                    {
                        return Guid.Empty;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public Guid GetEntityLookupAttribute(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_LookupAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        EntityReference aEntityReference = (EntityReference)aEntity.Attributes[PropertyName];

                        return aEntityReference.Id;
                    }
                    else
                    {
                        return Guid.Empty;
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public String GetEntityLookupDisplayName(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_LookupAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        EntityReference aEntityReference = (EntityReference)aEntity.Attributes[PropertyName];

                        return aEntityReference.Name;
                    }
                    else
                    {
                        return "";
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public String GetEntityLookupDisplayName(Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_LookupAttributeLocker)
                //{
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        EntityReference aEntityReference = (EntityReference)aEntity.Attributes[PropertyName];

                        return aEntityReference.Name;
                    }
                    else
                    {
                        return "";
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityLookUpAttribute(ref Entity aEntity, string PropertyName, String LookupEntityName, Guid GuidValue)
        {
            try
            {
                //lock (m_LookupAttributeLocker)
                //{
                    if (GuidValue != null && GuidValue != Guid.Empty)
                    {
                        EntityReference aEntityReference = new EntityReference(LookupEntityName, GuidValue);
                        if (aEntity.Attributes.Contains(PropertyName))
                        {
                            aEntity.Attributes[PropertyName] = aEntityReference;
                        }
                        else
                        {
                            aEntity.Attributes.Add(PropertyName, aEntityReference);
                        }
                    }
                    else { return; }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityLookUpAttribute(Entity aEntity, string PropertyName, String LookupEntityName, Guid GuidValue)
        {
            try
            {
                //lock (m_LookupAttributeLocker)
                //{
                    if (GuidValue != null && GuidValue != Guid.Empty)
                    {
                        EntityReference aEntityReference = new EntityReference(LookupEntityName, GuidValue);
                        if (aEntity.Attributes.Contains(PropertyName))
                        {
                            aEntity.Attributes[PropertyName] = aEntityReference;
                        }
                        else
                        {
                            aEntity.Attributes.Add(PropertyName, aEntityReference);
                        }
                    }
                    else { return; }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityLookUpAttribute(ref Entity aEntity, string PropertyName, ref EntityReference aEntityReference)
        {
            try
            {
                //lock (m_LookupAttributeLocker)
                //{

                    // 呼叫者必須透過 : ToEntityReference();
                    // 如下例 :
                    // sample.Attributes["new_projectlocation1"]=projectLoc.ToEntityReference();
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = aEntityReference;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, aEntityReference);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetEntityLookUpToNull(ref Entity aEntity, string PropertyName)
        {
            try
            {
                //lock (m_LookupAttributeLocker)
                //{

                    // 呼叫者必須透過 : ToEntityReference();
                    // 如下例 :
                    // sample.Attributes["new_projectlocation1"]=projectLoc.ToEntityReference();
                    if (aEntity.Attributes.Contains(PropertyName))
                    {
                        aEntity.Attributes[PropertyName] = null;
                    }
                    else
                    {
                        aEntity.Attributes.Add(PropertyName, null);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 一般化屬性
        private string getAttributeValue(Entity targetEntity, string attributeName)
        {

            if (string.IsNullOrEmpty(attributeName))
            {
                return string.Empty;
            }

            if (targetEntity[attributeName] is AliasedValue)
            {
                return (targetEntity[attributeName] as AliasedValue).Value.ToString();
            }
            else
            {
                return targetEntity[attributeName].ToString();
            }

        }

        public void RemoveAttribute(ref Entity aEntity, string PropertyName)
        {
            try
            {
                aEntity.Attributes.Remove(PropertyName);
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public void SetEntityAttributeToNull(ref Entity aEntity, string PropertyName)
        {
            try
            {
                if (aEntity.Attributes.Contains(PropertyName))
                {
                    aEntity.Attributes[PropertyName] = null;
                }
                else
                {
                    aEntity.Attributes.Add(PropertyName, null);
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
        #region 負責人管理
        public Guid GetOwnerId(Entity aEntity)
        {
            try
            {
                return aEntity.GetAttributeValue<EntityReference>("ownerid").Id;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void AssignOwner(String EntityName, Entity aEntity, Guid OwnerId)
        {
            try
            {
                AssignRequest assign = new AssignRequest
                {
                    Assignee = new EntityReference("systemuser", OwnerId),
                    Target = new EntityReference(EntityName, aEntity.Id)
                };


                // Execute the Request
                if (CRM_TYPE == "DYNAMICS365")
                {
                    this.m_OrganizationService.Execute(assign);
                }
                else
                {
                    this.m_Crm2011OrganizationService.Execute(assign);
                }
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public String GetOwnerName(Entity aEntity)
        {
            try
            {
                return aEntity.GetAttributeValue<EntityReference>("ownerid").Name;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        #endregion
        #region 將連絡人加入或移除至名單

        //private readonly object m_MembersToMarketingListLocker = new object();
        public void AddMembersToMarketingList(Guid thisListGuid, List<Guid> memberGuidList, ref IOrganizationService gCRMService)
        {
            try
            {
                //lock (m_MembersToMarketingListLocker)
                //{
                    AddListMembersListRequest orgServiceRequest = new AddListMembersListRequest();
                    orgServiceRequest.ListId = thisListGuid;
                    orgServiceRequest.MemberIds = memberGuidList.ToArray();
                    gCRMService.Execute(orgServiceRequest);
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void RemoveMembersToMarketingList(Guid aListGuid, Guid MemberGuid, ref IOrganizationService gCRMService)
        {
            try
            {
                //lock (m_MembersToMarketingListLocker)
                //{
                    RemoveMemberListRequest orgServiceRequest = new RemoveMemberListRequest();
                    orgServiceRequest.ListId = aListGuid;
                    orgServiceRequest.EntityId = MemberGuid;
                    gCRMService.Execute(orgServiceRequest);
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void AddMembersToMarketingList(Guid thisListGuid, List<Guid> memberGuidList)
        {
            try
            {
                //lock (m_MembersToMarketingListLocker)
                //{
                    AddListMembersListRequest orgServiceRequest = new AddListMembersListRequest();
                    orgServiceRequest.ListId = thisListGuid;
                    orgServiceRequest.MemberIds = memberGuidList.ToArray();
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        this.m_OrganizationService.Execute(orgServiceRequest);
                    }
                    else
                    {
                        this.m_Crm2011OrganizationService.Execute(orgServiceRequest);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void RemoveMembersToMarketingList(Guid aListGuid, Guid MemberGuid)
        {
            try
            {
                //lock (m_MembersToMarketingListLocker)
                //{
                    RemoveMemberListRequest orgServiceRequest = new RemoveMemberListRequest();
                    orgServiceRequest.ListId = aListGuid;
                    orgServiceRequest.EntityId = MemberGuid;
                    if (CRM_TYPE == "DYNAMICS365")
                    {
                        this.m_OrganizationService.Execute(orgServiceRequest);
                    }
                    else
                    {
                        this.m_Crm2011OrganizationService.Execute(orgServiceRequest);
                    }
                //}
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public ArrayList GetAllMemberDataFromList(Guid ListEntityId)
        {
            #region // 處理每個小組名單
            //搜尋名單的組員
            //EntityCollection Contacts = m_ToolUtilityClass.RetrieveManyToOneRelationship("list", "listid", ListEntityId.ToString(), "new_cell_list_contact", "contact");

            Entity ListEntity = this.RetrieveEntity("list", ListEntityId);

            bool ListType = this.GetEntityBoolAttribute(ListEntity, "type");
            EntityCollection MemberCollection;
            if (ListType == false)
            {
                // 靜態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.RetrieveMemberListCollectionByListIdDynamics365(ref this.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.RetrieveMemberListCollectionByListIdCrm2011(ref this.m_Crm2011OrganizationService, ListEntityId);
                }
            }
            else
            {
                // 動態名單
                if (CRM_TYPE == "DYNAMICS365")
                {
                    MemberCollection = this.RetrieveDynamicMemberListDynamics365(ref this.m_OrganizationService, ListEntityId);
                }
                else
                {
                    MemberCollection = this.RetrieveDynamicMemberListCrm2011(ref this.m_Crm2011OrganizationService, ListEntityId);
                }
            }

            int PresentRecordIdCounter = 0;
            ArrayList MemberEntityIdList = new ArrayList();
            foreach (Entity MemberEntity in MemberCollection.Entities)
            {
                // 每個組員
                Entity ContactEntity;

                if (ListType == false)
                {
                    // 靜態名單
                    MemberEntityIdList.Add(((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                    //ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", ((EntityReference)MemberEntity.Attributes["entityid"]).Id);
                }
                else
                {
                    // 動態名單
                    MemberEntityIdList.Add((Guid)MemberEntity.Attributes["contactid"]);
                    //ContactEntity = m_ToolUtilityClass.RetrieveEntity("contact", (Guid)MemberEntity.Attributes["contactid"]);
                }

            }
            #endregion

            return MemberEntityIdList;
        }

        #endregion
        #region 活動相關的收件人或寄件人
        public void GetActivityPartyList(Entity ActivityEntity, String FromOrTo, ArrayList aFromOrToList, ArrayList aFromOrToTypeList)
        {
            try
            {
                EntityCollection aFromEntityCollection = ActivityEntity.GetAttributeValue<EntityCollection>(FromOrTo);

                for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)
                {
                    #region 取得活動寄送者
                    EntityReference aFromOrToEntityReference = (EntityReference)aFromEntityCollection.Entities[i]["partyid"];

                    Guid aFromOrToEntityId = aFromOrToEntityReference.Id;

                    String EntityName = aFromOrToEntityReference.LogicalName;

                    aFromOrToTypeList.Add(EntityName);

                    Entity aRetrievedFromOrToEntity = this.RetrieveEntity(EntityName, aFromOrToEntityId);

                    aFromOrToList.Add(aRetrievedFromOrToEntity);


                    #endregion
                }

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void GetActivityPartyIdList(Entity ActivityEntity, String FromOrTo, ArrayList aFromOrToIdList, ArrayList aFromOrToTypeList)
        {
            try
            {
                EntityCollection aFromEntityCollection = ActivityEntity.GetAttributeValue<EntityCollection>(FromOrTo);

                if ( aFromEntityCollection != null)
                {
                    for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)
                    {
                        #region 取得活動寄送者
                        EntityReference aFromOrToEntityReference = (EntityReference)aFromEntityCollection.Entities[i]["partyid"];

                        aFromOrToTypeList.Add(aFromOrToEntityReference.LogicalName);

                        aFromOrToIdList.Add(aFromOrToEntityReference.Id);


                        #endregion
                    }
                }

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public void SetActivityStatusToCompleted(String ActivityName, Guid aActivityId)
        {
            try
            {
                // Create the request object.
                SetStateRequest aSetStateActivityRequest = new SetStateRequest();

                // Set the properties of the request object.
                aSetStateActivityRequest.State = new OptionSetValue(1);
                //aSetStatePhoneActivityRequest.Status = new OptionSetValue(2);
                aSetStateActivityRequest.Status = new OptionSetValue(4);

                // EntityId is the GUID of the account whose state is being changed.
                EntityReference EntityMoniker = new EntityReference(ActivityName, aActivityId);
                aSetStateActivityRequest.EntityMoniker = EntityMoniker;

                // Execute the request.
                SetStateResponse StateSetResponse = (SetStateResponse)this.m_OrganizationService.Execute(aSetStateActivityRequest);

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }
        public void SetAppointmentStatusToScheduled( Guid aActivityId)
        {
            try
            {
                // Create the request object.
                SetStateRequest aSetStateActivityRequest = new SetStateRequest();

                // Set the properties of the request object.
                aSetStateActivityRequest.State = new OptionSetValue(3);
                //aSetStatePhoneActivityRequest.Status = new OptionSetValue(2);
                aSetStateActivityRequest.Status = new OptionSetValue(5);

                // EntityId is the GUID of the account whose state is being changed.
                EntityReference EntityMoniker = new EntityReference("appointment", aActivityId);
                aSetStateActivityRequest.EntityMoniker = EntityMoniker;

                // Execute the request.
                SetStateResponse StateSetResponse = (SetStateResponse)this.m_OrganizationService.Execute(aSetStateActivityRequest);

                return;
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        #endregion
        #region 處理附加檔
        public EntityCollection DownloadAnAttachment(ref IOrganizationService aCrmService, Guid AnEntityId)
        {
            try
            {
                #region How to download attachment from activitymimeattachment record
                #region 建立 Condidtion

                ConditionExpression ContactConditionPrincipal = new ConditionExpression();

                // Set the ConditionExpressions properties so that the condition is true when the 
                // ownerid of the account equals the principalId.
                ContactConditionPrincipal.AttributeName = "objectid";
                ContactConditionPrincipal.Operator = ConditionOperator.Equal;

                ContactConditionPrincipal.Values.Add(AnEntityId.ToString());


                #endregion
                #region 建立 Filter
                FilterExpression filter = new FilterExpression();
                filter.FilterOperator = LogicalOperator.And;
                filter.Conditions.Add(ContactConditionPrincipal);
                #endregion
                #region 建立 QueryExpression
                QueryExpression query = new QueryExpression();
                //query.EntityName = EntityName.activitymimeattachment.ToString();
                //queryPrincipal.EntityName = @"new_blood_report";
                query.EntityName = "annotation";
                query.ColumnSet.AllColumns = true;

                query.Criteria = filter;
                #endregion
                #region 建立 Retrieve Multiple Request

                return aCrmService.RetrieveMultiple(query);
                //BusinessEntityCollection AnnotationsCollection = aCrmService.RetrieveMultiple(query);
                //return AnnotationsCollection;
                #endregion
                #region 執行搜尋後驗證而已

                //foreach (annotation Annotation in attachments.BusinessEntities)
                //{
                //Guid attachid = Annotation.annotationid.Value;
                // Retrieve the activitymimeattachment record.
                //annotation AnnotationAttchment = (annotation)aCrmService.Retrieve(EntityName.annotation.ToString(), attachid, new AllColumns());
                // Download the attachment in the current execution folder.
                //using (FileStream fileStream = new FileStream(@"D:\客戶資料\" + AnnotationAttchment.filename, FileMode.OpenOrCreate))
                //{
                //byte[] fileContent = new UTF8Encoding(true).GetBytes(AnnotationAttchment.documentbody);
                //byte[] fileContent = Convert.FromBase64String(AnnotationAttchment.documentbody);

                //fileStream.Write(fileContent, 0, fileContent.Length);
                //}
                //}
                #endregion
                #endregion
            }
            catch (System.Exception e)
            {
                String ErrorString = "ERROR : FullName = " + this.GetType().FullName.ToString() + " , Time = " + DateTime.Now.ToString() + " , Description = " + e.ToString();

                throw e;
            }
        }

        public void UploadAnAttachment(ref IOrganizationService aCrmService, String EntityName, String Subject, String NoteText, String FileName, String MimeType, byte[] DocumentBody, Guid ToBeAttachedEntityId)
        {
            try
            {
                #region 附加一個檔案到實體裡

                #region Mime Type
                //jpg->image / jpeg
                //doc->application / octet - stream
                //docx->application / octet - stream
                //pdf->application / pdf
                //msg->application / octet - stream
                //htm->text / html
                //png->image / png
                //gif->image / png
                //xls->application / octet - stream


                //case ".3dm": mimeType = "x-world/x-3dmf"; break;
                //case ".3dmf": mimeType = "x-world/x-3dmf"; break;
                //case ".a": mimeType = "application/octet-stream"; break;
                //case ".aab": mimeType = "application/x-authorware-bin"; break;
                //case ".aam": mimeType = "application/x-authorware-map"; break;
                //case ".aas": mimeType = "application/x-authorware-seg"; break;
                //case ".abc": mimeType = "text/vnd.abc"; break;
                //case ".acgi": mimeType = "text/html"; break;
                //case ".afl": mimeType = "video/animaflex"; break;
                //case ".ai": mimeType = "application/postscript"; break;
                //case ".aif": mimeType = "audio/aiff"; break;
                //case ".aifc": mimeType = "audio/aiff"; break;
                //case ".aiff": mimeType = "audio/aiff"; break;
                //case ".aim": mimeType = "application/x-aim"; break;
                //case ".aip": mimeType = "text/x-audiosoft-intra"; break;
                //case ".ani": mimeType = "application/x-navi-animation"; break;
                //case ".aos": mimeType = "application/x-nokia-9000-communicator-add-on-software"; break;
                //case ".aps": mimeType = "application/mime"; break;
                //case ".arc": mimeType = "application/octet-stream"; break;
                //case ".arj": mimeType = "application/arj"; break;
                //case ".art": mimeType = "image/x-jg"; break;
                //case ".asf": mimeType = "video/x-ms-asf"; break;
                //case ".asm": mimeType = "text/x-asm"; break;
                //case ".asp": mimeType = "text/asp"; break;
                //case ".asx": mimeType = "video/x-ms-asf"; break;
                //case ".au": mimeType = "audio/basic"; break;
                //case ".avi": mimeType = "video/avi"; break;
                //case ".avs": mimeType = "video/avs-video"; break;
                //case ".bcpio": mimeType = "application/x-bcpio"; break;
                //case ".bin": mimeType = "application/octet-stream"; break;
                //case ".bm": mimeType = "image/bmp"; break;
                //case ".bmp": mimeType = "image/bmp"; break;
                //case ".boo": mimeType = "application/book"; break;
                //case ".book": mimeType = "application/book"; break;
                //case ".boz": mimeType = "application/x-bzip2"; break;
                //case ".bsh": mimeType = "application/x-bsh"; break;
                //case ".bz": mimeType = "application/x-bzip"; break;
                //case ".bz2": mimeType = "application/x-bzip2"; break;
                //case ".c": mimeType = "text/plain"; break;
                //case ".c++": mimeType = "text/plain"; break;
                //case ".cat": mimeType = "application/vnd.ms-pki.seccat"; break;
                //case ".cc": mimeType = "text/plain"; break;
                //case ".ccad": mimeType = "application/clariscad"; break;
                //case ".cco": mimeType = "application/x-cocoa"; break;
                //case ".cdf": mimeType = "application/cdf"; break;
                //case ".cer": mimeType = "application/pkix-cert"; break;
                //case ".cha": mimeType = "application/x-chat"; break;
                //case ".chat": mimeType = "application/x-chat"; break;
                //case ".class": mimeType = "application/java"; break;
                //case ".com": mimeType = "application/octet-stream"; break;
                //case ".conf": mimeType = "text/plain"; break;
                //case ".cpio": mimeType = "application/x-cpio"; break;
                //case ".cpp": mimeType = "text/x-c"; break;
                //case ".cpt": mimeType = "application/x-cpt"; break;
                //case ".crl": mimeType = "application/pkcs-crl"; break;
                //case ".crt": mimeType = "application/pkix-cert"; break;
                //case ".csh": mimeType = "application/x-csh"; break;
                //case ".css": mimeType = "text/css"; break;
                //case ".cxx": mimeType = "text/plain"; break;
                //case ".dcr": mimeType = "application/x-director"; break;
                //case ".deepv": mimeType = "application/x-deepv"; break;
                //case ".def": mimeType = "text/plain"; break;
                //case ".der": mimeType = "application/x-x509-ca-cert"; break;
                //case ".dif": mimeType = "video/x-dv"; break;
                //case ".dir": mimeType = "application/x-director"; break;
                //case ".dl": mimeType = "video/dl"; break;
                //case ".doc": mimeType = "application/msword"; break;
                //case ".dot": mimeType = "application/msword"; break;
                //case ".dp": mimeType = "application/commonground"; break;
                //case ".drw": mimeType = "application/drafting"; break;
                //case ".dump": mimeType = "application/octet-stream"; break;
                //case ".dv": mimeType = "video/x-dv"; break;
                //case ".dvi": mimeType = "application/x-dvi"; break;
                //case ".dwf": mimeType = "model/vnd.dwf"; break;
                //case ".dwg": mimeType = "image/vnd.dwg"; break;
                //case ".dxf": mimeType = "image/vnd.dwg"; break;
                //case ".dxr": mimeType = "application/x-director"; break;
                //case ".el": mimeType = "text/x-script.elisp"; break;
                //case ".elc": mimeType = "application/x-elc"; break;
                //case ".env": mimeType = "application/x-envoy"; break;
                //case ".eps": mimeType = "application/postscript"; break;
                //case ".es": mimeType = "application/x-esrehber"; break;
                //case ".etx": mimeType = "text/x-setext"; break;
                //case ".evy": mimeType = "application/envoy"; break;
                //case ".exe": mimeType = "application/octet-stream"; break;
                //case ".f": mimeType = "text/plain"; break;
                //case ".f77": mimeType = "text/x-fortran"; break;
                //case ".f90": mimeType = "text/plain"; break;
                //case ".fdf": mimeType = "application/vnd.fdf"; break;
                //case ".fif": mimeType = "image/fif"; break;
                //case ".fli": mimeType = "video/fli"; break;
                //case ".flo": mimeType = "image/florian"; break;
                //case ".flx": mimeType = "text/vnd.fmi.flexstor"; break;
                //case ".fmf": mimeType = "video/x-atomic3d-feature"; break;
                //case ".for": mimeType = "text/x-fortran"; break;
                //case ".fpx": mimeType = "image/vnd.fpx"; break;
                //case ".frl": mimeType = "application/freeloader"; break;
                //case ".funk": mimeType = "audio/make"; break;
                //case ".g": mimeType = "text/plain"; break;
                //case ".g3": mimeType = "image/g3fax"; break;
                //case ".gif": mimeType = "image/gif"; break;
                //case ".gl": mimeType = "video/gl"; break;
                //case ".gsd": mimeType = "audio/x-gsm"; break;
                //case ".gsm": mimeType = "audio/x-gsm"; break;
                //case ".gsp": mimeType = "application/x-gsp"; break;
                //case ".gss": mimeType = "application/x-gss"; break;
                //case ".gtar": mimeType = "application/x-gtar"; break;
                //case ".gz": mimeType = "application/x-gzip"; break;
                //case ".gzip": mimeType = "application/x-gzip"; break;
                //case ".h": mimeType = "text/plain"; break;
                //case ".hdf": mimeType = "application/x-hdf"; break;
                //case ".help": mimeType = "application/x-helpfile"; break;
                //case ".hgl": mimeType = "application/vnd.hp-hpgl"; break;
                //case ".hh": mimeType = "text/plain"; break;
                //case ".hlb": mimeType = "text/x-script"; break;
                //case ".hlp": mimeType = "application/hlp"; break;
                //case ".hpg": mimeType = "application/vnd.hp-hpgl"; break;
                //case ".hpgl": mimeType = "application/vnd.hp-hpgl"; break;
                //case ".hqx": mimeType = "application/binhex"; break;
                //case ".hta": mimeType = "application/hta"; break;
                //case ".htc": mimeType = "text/x-component"; break;
                //case ".htm": mimeType = "text/html"; break;
                //case ".html": mimeType = "text/html"; break;
                //case ".htmls": mimeType = "text/html"; break;
                //case ".htt": mimeType = "text/webviewhtml"; break;
                //case ".htx": mimeType = "text/html"; break;
                //case ".ice": mimeType = "x-conference/x-cooltalk"; break;
                //case ".ico": mimeType = "image/x-icon"; break;
                //case ".idc": mimeType = "text/plain"; break;
                //case ".ief": mimeType = "image/ief"; break;
                //case ".iefs": mimeType = "image/ief"; break;
                //case ".iges": mimeType = "application/iges"; break;
                //case ".igs": mimeType = "application/iges"; break;
                //case ".ima": mimeType = "application/x-ima"; break;
                //case ".imap": mimeType = "application/x-httpd-imap"; break;
                //case ".inf": mimeType = "application/inf"; break;
                //case ".ins": mimeType = "application/x-internett-signup"; break;
                //case ".ip": mimeType = "application/x-ip2"; break;
                //case ".isu": mimeType = "video/x-isvideo"; break;
                //case ".it": mimeType = "audio/it"; break;
                //case ".iv": mimeType = "application/x-inventor"; break;
                //case ".ivr": mimeType = "i-world/i-vrml"; break;
                //case ".ivy": mimeType = "application/x-livescreen"; break;
                //case ".jam": mimeType = "audio/x-jam"; break;
                //case ".jav": mimeType = "text/plain"; break;
                //case ".java": mimeType = "text/plain"; break;
                //case ".jcm": mimeType = "application/x-java-commerce"; break;
                //case ".jfif": mimeType = "image/jpeg"; break;
                //case ".jfif-tbnl": mimeType = "image/jpeg"; break;
                //case ".jpe": mimeType = "image/jpeg"; break;
                //case ".jpeg": mimeType = "image/jpeg"; break;
                //case ".jpg": mimeType = "image/jpeg"; break;
                //case ".jps": mimeType = "image/x-jps"; break;
                //case ".js": mimeType = "application/x-javascript"; break;
                //case ".jut": mimeType = "image/jutvision"; break;
                //case ".kar": mimeType = "audio/midi"; break;
                //case ".ksh": mimeType = "application/x-ksh"; break;
                //case ".la": mimeType = "audio/nspaudio"; break;
                //case ".lam": mimeType = "audio/x-liveaudio"; break;
                //case ".latex": mimeType = "application/x-latex"; break;
                //case ".lha": mimeType = "application/octet-stream"; break;
                //case ".lhx": mimeType = "application/octet-stream"; break;
                //case ".list": mimeType = "text/plain"; break;
                //case ".lma": mimeType = "audio/nspaudio"; break;
                //case ".log": mimeType = "text/plain"; break;
                //case ".lsp": mimeType = "application/x-lisp"; break;
                //case ".lst": mimeType = "text/plain"; break;
                //case ".lsx": mimeType = "text/x-la-asf"; break;
                //case ".ltx": mimeType = "application/x-latex"; break;
                //case ".lzh": mimeType = "application/octet-stream"; break;
                //case ".lzx": mimeType = "application/octet-stream"; break;
                //case ".m": mimeType = "text/plain"; break;
                //case ".m1v": mimeType = "video/mpeg"; break;
                //case ".m2a": mimeType = "audio/mpeg"; break;
                //case ".m2v": mimeType = "video/mpeg"; break;
                //case ".m3u": mimeType = "audio/x-mpequrl"; break;
                //case ".man": mimeType = "application/x-troff-man"; break;
                //case ".map": mimeType = "application/x-navimap"; break;
                //case ".mar": mimeType = "text/plain"; break;
                //case ".mbd": mimeType = "application/mbedlet"; break;
                //case ".mc$": mimeType = "application/x-magic-cap-package-1.0"; break;
                //case ".mcd": mimeType = "application/mcad"; break;
                //case ".mcf": mimeType = "text/mcf"; break;
                //case ".mcp": mimeType = "application/netmc"; break;
                //case ".me": mimeType = "application/x-troff-me"; break;
                //case ".mht": mimeType = "message/rfc822"; break;
                //case ".mhtml": mimeType = "message/rfc822"; break;
                //case ".mid": mimeType = "audio/midi"; break;
                //case ".midi": mimeType = "audio/midi"; break;
                //case ".mif": mimeType = "application/x-mif"; break;
                //case ".mime": mimeType = "message/rfc822"; break;
                //case ".mjf": mimeType = "audio/x-vnd.audioexplosion.mjuicemediafile"; break;
                //case ".mjpg": mimeType = "video/x-motion-jpeg"; break;
                //case ".mm": mimeType = "application/base64"; break;
                //case ".mme": mimeType = "application/base64"; break;
                //case ".mod": mimeType = "audio/mod"; break;
                //case ".moov": mimeType = "video/quicktime"; break;
                //case ".mov": mimeType = "video/quicktime"; break;
                //case ".movie": mimeType = "video/x-sgi-movie"; break;
                //case ".mp2": mimeType = "audio/mpeg"; break;
                //case ".mp3": mimeType = "audio/mpeg"; break;
                //case ".mpa": mimeType = "audio/mpeg"; break;
                //case ".mpc": mimeType = "application/x-project"; break;
                //case ".mpe": mimeType = "video/mpeg"; break;
                //case ".mpeg": mimeType = "video/mpeg"; break;
                //case ".mpg": mimeType = "video/mpeg"; break;
                //case ".mpga": mimeType = "audio/mpeg"; break;
                //case ".mpp": mimeType = "application/vnd.ms-project"; break;
                //case ".mpt": mimeType = "application/vnd.ms-project"; break;
                //case ".mpv": mimeType = "application/vnd.ms-project"; break;
                //case ".mpx": mimeType = "application/vnd.ms-project"; break;
                //case ".mrc": mimeType = "application/marc"; break;
                //case ".ms": mimeType = "application/x-troff-ms"; break;
                //case ".mv": mimeType = "video/x-sgi-movie"; break;
                //case ".my": mimeType = "audio/make"; break;
                //case ".mzz": mimeType = "application/x-vnd.audioexplosion.mzz"; break;
                //case ".nap": mimeType = "image/naplps"; break;
                //case ".naplps": mimeType = "image/naplps"; break;
                //case ".nc": mimeType = "application/x-netcdf"; break;
                //case ".ncm": mimeType = "application/vnd.nokia.configuration-message"; break;
                //case ".nif": mimeType = "image/x-niff"; break;
                //case ".niff": mimeType = "image/x-niff"; break;
                //case ".nix": mimeType = "application/x-mix-transfer"; break;
                //case ".nsc": mimeType = "application/x-conference"; break;
                //case ".nvd": mimeType = "application/x-navidoc"; break;
                //case ".o": mimeType = "application/octet-stream"; break;
                //case ".oda": mimeType = "application/oda"; break;
                //case ".omc": mimeType = "application/x-omc"; break;
                //case ".omcd": mimeType = "application/x-omcdatamaker"; break;
                //case ".omcr": mimeType = "application/x-omcregerator"; break;
                //case ".p": mimeType = "text/x-pascal"; break;
                //case ".p10": mimeType = "application/pkcs10"; break;
                //case ".p12": mimeType = "application/pkcs-12"; break;
                //case ".p7a": mimeType = "application/x-pkcs7-signature"; break;
                //case ".p7c": mimeType = "application/pkcs7-mime"; break;
                //case ".p7m": mimeType = "application/pkcs7-mime"; break;
                //case ".p7r": mimeType = "application/x-pkcs7-certreqresp"; break;
                //case ".p7s": mimeType = "application/pkcs7-signature"; break;
                //case ".part": mimeType = "application/pro_eng"; break;
                //case ".pas": mimeType = "text/pascal"; break;
                //case ".pbm": mimeType = "image/x-portable-bitmap"; break;
                //case ".pcl": mimeType = "application/vnd.hp-pcl"; break;
                //case ".pct": mimeType = "image/x-pict"; break;
                //case ".pcx": mimeType = "image/x-pcx"; break;
                //case ".pdb": mimeType = "chemical/x-pdb"; break;
                //case ".pdf": mimeType = "application/pdf"; break;
                //case ".pfunk": mimeType = "audio/make"; break;
                //case ".pgm": mimeType = "image/x-portable-greymap"; break;
                //case ".pic": mimeType = "image/pict"; break;
                //case ".pict": mimeType = "image/pict"; break;
                //case ".pkg": mimeType = "application/x-newton-compatible-pkg"; break;
                //case ".pko": mimeType = "application/vnd.ms-pki.pko"; break;
                //case ".pl": mimeType = "text/plain"; break;
                //case ".plx": mimeType = "application/x-pixclscript"; break;
                //case ".pm": mimeType = "image/x-xpixmap"; break;
                //case ".pm4": mimeType = "application/x-pagemaker"; break;
                //case ".pm5": mimeType = "application/x-pagemaker"; break;
                //case ".png": mimeType = "image/png"; break;
                //case ".pnm": mimeType = "application/x-portable-anymap"; break;
                //case ".pot": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".pov": mimeType = "model/x-pov"; break;
                //case ".ppa": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".ppm": mimeType = "image/x-portable-pixmap"; break;
                //case ".pps": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".ppt": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".ppz": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".pre": mimeType = "application/x-freelance"; break;
                //case ".prt": mimeType = "application/pro_eng"; break;
                //case ".ps": mimeType = "application/postscript"; break;
                //case ".psd": mimeType = "application/octet-stream"; break;
                //case ".pvu": mimeType = "paleovu/x-pv"; break;
                //case ".pwz": mimeType = "application/vnd.ms-powerpoint"; break;
                //case ".py": mimeType = "text/x-script.phyton"; break;
                //case ".pyc": mimeType = "applicaiton/x-bytecode.python"; break;
                //case ".qcp": mimeType = "audio/vnd.qcelp"; break;
                //case ".qd3": mimeType = "x-world/x-3dmf"; break;
                //case ".qd3d": mimeType = "x-world/x-3dmf"; break;
                //case ".qif": mimeType = "image/x-quicktime"; break;
                //case ".qt": mimeType = "video/quicktime"; break;
                //case ".qtc": mimeType = "video/x-qtc"; break;
                //case ".qti": mimeType = "image/x-quicktime"; break;
                //case ".qtif": mimeType = "image/x-quicktime"; break;
                //case ".ra": mimeType = "audio/x-pn-realaudio"; break;
                //case ".ram": mimeType = "audio/x-pn-realaudio"; break;
                //case ".ras": mimeType = "application/x-cmu-raster"; break;
                //case ".rast": mimeType = "image/cmu-raster"; break;
                //case ".rexx": mimeType = "text/x-script.rexx"; break;
                //case ".rf": mimeType = "image/vnd.rn-realflash"; break;
                //case ".rgb": mimeType = "image/x-rgb"; break;
                //case ".rm": mimeType = "application/vnd.rn-realmedia"; break;
                //case ".rmi": mimeType = "audio/mid"; break;
                //case ".rmm": mimeType = "audio/x-pn-realaudio"; break;
                //case ".rmp": mimeType = "audio/x-pn-realaudio"; break;
                //case ".rng": mimeType = "application/ringing-tones"; break;
                //case ".rnx": mimeType = "application/vnd.rn-realplayer"; break;
                //case ".roff": mimeType = "application/x-troff"; break;
                //case ".rp": mimeType = "image/vnd.rn-realpix"; break;
                //case ".rpm": mimeType = "audio/x-pn-realaudio-plugin"; break;
                //case ".rt": mimeType = "text/richtext"; break;
                //case ".rtf": mimeType = "text/richtext"; break;
                //case ".rtx": mimeType = "text/richtext"; break;
                //case ".rv": mimeType = "video/vnd.rn-realvideo"; break;
                //case ".s": mimeType = "text/x-asm"; break;
                //case ".s3m": mimeType = "audio/s3m"; break;
                //case ".saveme": mimeType = "application/octet-stream"; break;
                //case ".sbk": mimeType = "application/x-tbook"; break;
                //case ".scm": mimeType = "application/x-lotusscreencam"; break;
                //case ".sdml": mimeType = "text/plain"; break;
                //case ".sdp": mimeType = "application/sdp"; break;
                //case ".sdr": mimeType = "application/sounder"; break;
                //case ".sea": mimeType = "application/sea"; break;
                //case ".set": mimeType = "application/set"; break;
                //case ".sgm": mimeType = "text/sgml"; break;
                //case ".sgml": mimeType = "text/sgml"; break;
                //case ".sh": mimeType = "application/x-sh"; break;
                //case ".shar": mimeType = "application/x-shar"; break;
                //case ".shtml": mimeType = "text/html"; break;
                //case ".sid": mimeType = "audio/x-psid"; break;
                //case ".sit": mimeType = "application/x-sit"; break;
                //case ".skd": mimeType = "application/x-koan"; break;
                //case ".skm": mimeType = "application/x-koan"; break;
                //case ".skp": mimeType = "application/x-koan"; break;
                //case ".skt": mimeType = "application/x-koan"; break;
                //case ".sl": mimeType = "application/x-seelogo"; break;
                //case ".smi": mimeType = "application/smil"; break;
                //case ".smil": mimeType = "application/smil"; break;
                //case ".snd": mimeType = "audio/basic"; break;
                //case ".sol": mimeType = "application/solids"; break;
                //case ".spc": mimeType = "text/x-speech"; break;
                //case ".spl": mimeType = "application/futuresplash"; break;
                //case ".spr": mimeType = "application/x-sprite"; break;
                //case ".sprite": mimeType = "application/x-sprite"; break;
                //case ".src": mimeType = "application/x-wais-source"; break;
                //case ".ssi": mimeType = "text/x-server-parsed-html"; break;
                //case ".ssm": mimeType = "application/streamingmedia"; break;
                //case ".sst": mimeType = "application/vnd.ms-pki.certstore"; break;
                //case ".step": mimeType = "application/step"; break;
                //case ".stl": mimeType = "application/sla"; break;
                //case ".stp": mimeType = "application/step"; break;
                //case ".sv4cpio": mimeType = "application/x-sv4cpio"; break;
                //case ".sv4crc": mimeType = "application/x-sv4crc"; break;
                //case ".svf": mimeType = "image/vnd.dwg"; break;
                //case ".svr": mimeType = "application/x-world"; break;
                //case ".swf": mimeType = "application/x-shockwave-flash"; break;
                //case ".t": mimeType = "application/x-troff"; break;
                //case ".talk": mimeType = "text/x-speech"; break;
                //case ".tar": mimeType = "application/x-tar"; break;
                //case ".tbk": mimeType = "application/toolbook"; break;
                //case ".tcl": mimeType = "application/x-tcl"; break;
                //case ".tcsh": mimeType = "text/x-script.tcsh"; break;
                //case ".tex": mimeType = "application/x-tex"; break;
                //case ".texi": mimeType = "application/x-texinfo"; break;
                //case ".texinfo": mimeType = "application/x-texinfo"; break;
                //case ".text": mimeType = "text/plain"; break;
                //case ".tgz": mimeType = "application/x-compressed"; break;
                //case ".tif": mimeType = "image/tiff"; break;
                //case ".tiff": mimeType = "image/tiff"; break;
                //case ".tr": mimeType = "application/x-troff"; break;
                //case ".tsi": mimeType = "audio/tsp-audio"; break;
                //case ".tsp": mimeType = "application/dsptype"; break;
                //case ".tsv": mimeType = "text/tab-separated-values"; break;
                //case ".turbot": mimeType = "image/florian"; break;
                //case ".txt": mimeType = "text/plain"; break;
                //case ".uil": mimeType = "text/x-uil"; break;
                //case ".uni": mimeType = "text/uri-list"; break;
                //case ".unis": mimeType = "text/uri-list"; break;
                //case ".unv": mimeType = "application/i-deas"; break;
                //case ".uri": mimeType = "text/uri-list"; break;
                //case ".uris": mimeType = "text/uri-list"; break;
                //case ".ustar": mimeType = "application/x-ustar"; break;
                //case ".uu": mimeType = "application/octet-stream"; break;
                //case ".uue": mimeType = "text/x-uuencode"; break;
                //case ".vcd": mimeType = "application/x-cdlink"; break;
                //case ".vcs": mimeType = "text/x-vcalendar"; break;
                //case ".vda": mimeType = "application/vda"; break;
                //case ".vdo": mimeType = "video/vdo"; break;
                //case ".vew": mimeType = "application/groupwise"; break;
                //case ".viv": mimeType = "video/vivo"; break;
                //case ".vivo": mimeType = "video/vivo"; break;
                //case ".vmd": mimeType = "application/vocaltec-media-desc"; break;
                //case ".vmf": mimeType = "application/vocaltec-media-file"; break;
                //case ".voc": mimeType = "audio/voc"; break;
                //case ".vos": mimeType = "video/vosaic"; break;
                //case ".vox": mimeType = "audio/voxware"; break;
                //case ".vqe": mimeType = "audio/x-twinvq-plugin"; break;
                //case ".vqf": mimeType = "audio/x-twinvq"; break;
                //case ".vql": mimeType = "audio/x-twinvq-plugin"; break;
                //case ".vrml": mimeType = "application/x-vrml"; break;
                //case ".vrt": mimeType = "x-world/x-vrt"; break;
                //case ".vsd": mimeType = "application/x-visio"; break;
                //case ".vst": mimeType = "application/x-visio"; break;
                //case ".vsw": mimeType = "application/x-visio"; break;
                //case ".w60": mimeType = "application/wordperfect6.0"; break;
                //case ".w61": mimeType = "application/wordperfect6.1"; break;
                //case ".w6w": mimeType = "application/msword"; break;
                //case ".wav": mimeType = "audio/wav"; break;
                //case ".wb1": mimeType = "application/x-qpro"; break;
                //case ".wbmp": mimeType = "image/vnd.wap.wbmp"; break;
                //case ".web": mimeType = "application/vnd.xara"; break;
                //case ".wiz": mimeType = "application/msword"; break;
                //case ".wk1": mimeType = "application/x-123"; break;
                //case ".wmf": mimeType = "windows/metafile"; break;
                //case ".wml": mimeType = "text/vnd.wap.wml"; break;
                //case ".wmlc": mimeType = "application/vnd.wap.wmlc"; break;
                //case ".wmls": mimeType = "text/vnd.wap.wmlscript"; break;
                //case ".wmlsc": mimeType = "application/vnd.wap.wmlscriptc"; break;
                //case ".word": mimeType = "application/msword"; break;
                //case ".wp": mimeType = "application/wordperfect"; break;
                //case ".wp5": mimeType = "application/wordperfect"; break;
                //case ".wp6": mimeType = "application/wordperfect"; break;
                //case ".wpd": mimeType = "application/wordperfect"; break;
                //case ".wq1": mimeType = "application/x-lotus"; break;
                //case ".wri": mimeType = "application/mswrite"; break;
                //case ".wrl": mimeType = "application/x-world"; break;
                //case ".wrz": mimeType = "x-world/x-vrml"; break;
                //case ".wsc": mimeType = "text/scriplet"; break;
                //case ".wsrc": mimeType = "application/x-wais-source"; break;
                //case ".wtk": mimeType = "application/x-wintalk"; break;
                //case ".xbm": mimeType = "image/x-xbitmap"; break;
                //case ".xdr": mimeType = "video/x-amt-demorun"; break;
                //case ".xgz": mimeType = "xgl/drawing"; break;
                //case ".xif": mimeType = "image/vnd.xiff"; break;
                //case ".xl": mimeType = "application/excel"; break;
                //case ".xla": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlb": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlc": mimeType = "application/vnd.ms-excel"; break;
                //case ".xld": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlk": mimeType = "application/vnd.ms-excel"; break;
                //case ".xll": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlm": mimeType = "application/vnd.ms-excel"; break;
                //case ".xls": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlt": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlv": mimeType = "application/vnd.ms-excel"; break;
                //case ".xlw": mimeType = "application/vnd.ms-excel"; break;
                //case ".xm": mimeType = "audio/xm"; break;
                //case ".xml": mimeType = "application/xml"; break;
                //case ".xmz": mimeType = "xgl/movie"; break;
                //case ".xpix": mimeType = "application/x-vnd.ls-xpix"; break;
                //case ".xpm": mimeType = "image/xpm"; break;
                //case ".x-png": mimeType = "image/png"; break;
                //case ".xsr": mimeType = "video/x-amt-showrun"; break;
                //case ".xwd": mimeType = "image/x-xwd"; break;
                //case ".xyz": mimeType = "chemical/x-pdb"; break;
                //case ".z": mimeType = "application/x-compressed"; break;
                //case ".zip": mimeType = "application/zip"; break;
                //case ".zoo": mimeType = "application/octet-stream"; break;
                //case ".zsh": mimeType = "text/x-script.zsh"; break;
                //default: mimeType = "application/octet-stream"; break;
                #endregion
                //string strMessage =”this is a demo”;
                //byte[] filename = Encoding.ASCII.GetBytes(strMessage);
                //string encodedData = System.Convert.ToBase64String(filename);
                //Entity Annotation = new Entity(“annotation”);
                //Annotation.Attributes["objectid"] = new EntityReference(“EntityName”, GUID);
                //Annotation.Attributes["objecttypecode"] = “EntityNAME”;
                //Annotation.Attributes["subject"] = “Demo”;
                //Annotation.Attributes["documentbody"] = encodedData;
                //Annotation.Attributes["mimetype"] = @”text / plain”;
                //Annotation.Attributes["notetext"] = “Sample attachment.”;
                //Annotation.Attributes["filename"] = “Demo.txt”;
                //Service.Create(Annotation);


                Entity aAnnotationEntity = new Entity("annotation");
                aAnnotationEntity.Attributes["objectid"] = new EntityReference(EntityName, ToBeAttachedEntityId);
                aAnnotationEntity.Attributes["objecttypecode"] = EntityName;
                aAnnotationEntity.Attributes["subject"] = Subject;
                aAnnotationEntity.Attributes["notetext"] = NoteText;

                string aEncodedData = System.Convert.ToBase64String(DocumentBody);

                aAnnotationEntity.Attributes["documentbody"] = aEncodedData;
                aAnnotationEntity.Attributes["mimetype"] = MimeType;
                aAnnotationEntity.Attributes["filename"] = FileName;

                CreateEntity(aAnnotationEntity);
                #endregion
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
    }

}

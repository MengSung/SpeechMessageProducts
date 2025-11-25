using System;
using System.Linq;
using System.Net;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;
using System.ServiceModel.Description;
using PowerPlatform.Dataverse.Client;

namespace ToolUtilityNameSpace.ConnectionOperations
{
    /// <summary>
    /// CRM 連線服務實作
    /// 提供多種連線方式支援 CRM 2011、Dynamics 365 On-Premise 和 Online
    /// </summary>
    public class CrmConnectionService : ICrmConnectionService
    {
        #region 常數定義
        private const string CRM_TYPE_DYNAMICS365 = "DYNAMICS365";
        private const int DEFAULT_TIMEOUT_HOURS = 3;
        #endregion

        #region 基本憑證方法

        /// <summary>
        /// 取得 Windows 認證憑證（三參數版本）
        /// </summary>
        /// <param name="domain">網域名稱</param>
        /// <param name="userName">使用者名稱</param>
        /// <param name="password">密碼</param>
        /// <returns>ClientCredentials 物件</returns>
        public ClientCredentials GetClientCredentials(string domain, string userName, string password)
        {
            try
            {
                var credentials = new NetworkCredential(userName, password, domain);
                var clientCredentials = new ClientCredentials();
                clientCredentials.Windows.ClientCredential = credentials;
                return clientCredentials;
            }
            catch (Exception ex)
            {
                throw new Exception($"建立 ClientCredentials 時發生錯誤: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 取得預設 Windows 認證憑證（無參數版本）
        /// </summary>
        /// <returns>ClientCredentials 物件</returns>
        public ClientCredentials GetClientCredentials()
        {
            try
            {
                var clientCredentials = new ClientCredentials();
                clientCredentials.Windows.ClientCredential = CredentialCache.DefaultNetworkCredentials;
                return clientCredentials;
            }
            catch (Exception ex)
            {
                throw new Exception($"建立預設 ClientCredentials 時發生錯誤: {ex.Message}", ex);
            }
        }

        #endregion

        #region CRM 2011 連線方法

        /// <summary>
        /// 取得 CRM 2011 組織服務（HTTP 連線）
        /// </summary>
        /// <param name="server">伺服器位址</param>
        /// <param name="port">連接埠</param>
        /// <param name="organization">組織名稱</param>
        /// <param name="domain">網域</param>
        /// <param name="userName">使用者名稱</param>
        /// <param name="password">密碼</param>
        /// <returns>IOrganizationService 實例</returns>
        public IOrganizationService GetOrganizationService(
            string server,
            string port,
            string organization,
            string domain,
            string userName,
            string password)
        {
            try
            {
                Uri serviceUrl = new Uri($"http://{server}:{port}/{organization}/XRMServices/2011/Organization.svc");
                IServiceConfiguration<IOrganizationService> orgConfigInfo =
                    ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(serviceUrl);

                var credentials = GetClientCredentials(domain, userName, password);

                using (var serviceProxy = new OrganizationServiceProxy(orgConfigInfo, credentials))
                {
                    // 啟用早期繫結類型支援
                    serviceProxy.ServiceConfiguration.CurrentServiceEndpoint.EndpointBehaviors.Add(new ProxyTypesBehavior());
                    return serviceProxy;
                }
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"建立組織服務連線時發生錯誤 (Server: {server}, Port: {port}, Org: {organization}): {ex.Message}",
                    ex);
            }
        }

        /// <summary>
        /// 設定 CRM 2011 組織服務（HTTP 連線）
        /// </summary>
        public IOrganizationService SetOrganizationService(
            string server,
            string port,
            string organization,
            string domain,
            string userName,
            string password)
        {
            return GetOrganizationService(server, port, organization, domain, userName, password);
        }

        #endregion

        #region Claims-Based Authentication 連線方法

        /// <summary>
        /// 設定 Claims-Based 驗證的組織服務（HTTPS 連線）
        /// 用於內部部署的 Dynamics 365
        /// </summary>
        /// <param name="organization">組織名稱</param>
        /// <param name="server">伺服器位址</param>
        /// <param name="domain">網域</param>
        /// <param name="userName">使用者名稱</param>
        /// <param name="password">密碼</param>
        /// <returns>IOrganizationService 實例</returns>
        public IOrganizationService SetClaimsBasedAuthenticationOrganizationService(
            string organization,
            string server,
            string domain,
            string userName,
            string password)
        {
            try
            {
                Uri serviceUrl = new Uri($"https://{organization}.{server}/XRMServices/2011/Organization.svc");
                IServiceConfiguration<IOrganizationService> orgConfigInfo =
                    ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(serviceUrl);

                var credentials = GetClientCredentials(domain, userName, password);

                using (var serviceProxy = new OrganizationServiceProxy(orgConfigInfo, credentials))
                {
                    serviceProxy.ServiceConfiguration.CurrentServiceEndpoint.EndpointBehaviors.Add(new ProxyTypesBehavior());
                    return serviceProxy;
                }
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"設定 Claims-Based 驗證組織服務時發生錯誤 (Org: {organization}, Server: {server}): {ex.Message}",
                    ex);
            }
        }

        #endregion

        #region Federated 連線方法

        /// <summary>
        /// 設定 Federated (聯盟) 組織服務 Proxy
        /// 支援 Dynamics 365 Online 和 On-Premise IFD 環境
        /// </summary>
        /// <param name="discoveryServiceType">服務類型 (DYNAMICS365 或其他)</param>
        /// <param name="organization">組織名稱</param>
        /// <param name="server">伺服器位址</param>
        /// <param name="port">連接埠</param>
        /// <param name="baseDiscoveryServiceAddress">Discovery 服務基礎路徑</param>
        /// <param name="userName">使用者名稱</param>
        /// <param name="password">密碼</param>
        /// <param name="domain">網域</param>
        /// <returns>OrganizationServiceProxy 實例</returns>
        public OrganizationServiceProxy SetFederatedOrganizationProxy(
            string discoveryServiceType,
            string organization,
            string server,
            string port,
            string baseDiscoveryServiceAddress,
            string userName,
            string password,
            string domain)
        {
            try
            {
                // 建立 Discovery Service 位址
                string discoveryAddress = discoveryServiceType == CRM_TYPE_DYNAMICS365
                    ? $"https://{organization}.{server}{baseDiscoveryServiceAddress}"
                    : $"http://{server}:{port}/{organization}/XRMServices/2011/Organization.svc";

                // 建立 Discovery Service 管理物件
                IServiceManagement<IDiscoveryService> serviceManagement =
                    ServiceConfigurationFactory.CreateManagement<IDiscoveryService>(new Uri(discoveryAddress));

                AuthenticationProviderType endpointType = serviceManagement.AuthenticationType;
                AuthenticationCredentials authCredentials =
                    GetAuthCredentials(serviceManagement, endpointType, userName, password, domain);

                string organizationUri = string.Empty;

                // 透過 Discovery Service 取得組織端點
                using (var discoveryProxy = GetProxy<IDiscoveryService, DiscoveryServiceProxy>(
                    serviceManagement, authCredentials))
                {
                    if (discoveryProxy != null)
                    {
                        var orgs = DiscoverOrganizations(discoveryProxy);
                        var orgDetail = FindOrganization(organization, orgs.ToArray());

                        if (orgDetail != null)
                        {
                            organizationUri = orgDetail.Endpoints[EndpointType.OrganizationService];
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(organizationUri))
                {
                    throw new Exception($"找不到組織 '{organization}' 的服務端點");
                }

                // 建立組織服務管理物件
                IServiceManagement<IOrganizationService> orgServiceManagement =
                    ServiceConfigurationFactory.CreateManagement<IOrganizationService>(new Uri(organizationUri));

                AuthenticationCredentials credentials =
                    GetAuthCredentials(orgServiceManagement, endpointType, userName, password, domain);

                // 建立組織服務 Proxy
                var orgProxy = GetProxy<IOrganizationService, OrganizationServiceProxy>(
                    orgServiceManagement, credentials);

                if (orgProxy != null)
                {
                    orgProxy.EnableProxyTypes();
                    orgProxy.Timeout = new TimeSpan(DEFAULT_TIMEOUT_HOURS, 0, 0);
                }

                return orgProxy;
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"設定 Federated 組織服務時發生錯誤 (Type: {discoveryServiceType}, Org: {organization}): {ex.Message}",
                    ex);
            }
        }

        #endregion

        #region Discovery Service 方法

        /// <summary>
        /// 探索使用者所屬的所有組織
        /// </summary>
        /// <param name="service">Discovery Service 實例</param>
        /// <returns>組織詳細資訊集合</returns>
        public OrganizationDetailCollection DiscoverOrganizations(IDiscoveryService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service), "Discovery Service 不可為 null");
            }

            try
            {
                var orgRequest = new RetrieveOrganizationsRequest();
                var orgResponse = (RetrieveOrganizationsResponse)service.Execute(orgRequest);
                return orgResponse.Details;
            }
            catch (Exception ex)
            {
                throw new Exception($"探索組織時發生錯誤: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 在組織列表中尋找特定組織
        /// </summary>
        /// <param name="orgUniqueName">組織唯一名稱（不區分大小寫）</param>
        /// <param name="orgDetails">組織詳細資訊陣列</param>
        /// <returns>找到的組織詳細資訊，若找不到則返回 null</returns>
        public OrganizationDetail FindOrganization(string orgUniqueName, OrganizationDetail[] orgDetails)
        {
            if (string.IsNullOrWhiteSpace(orgUniqueName))
            {
                throw new ArgumentNullException(nameof(orgUniqueName), "組織名稱不可為空");
            }

            if (orgDetails == null)
            {
                throw new ArgumentNullException(nameof(orgDetails), "組織詳細資訊不可為 null");
            }

            try
            {
                return orgDetails.FirstOrDefault(detail =>
                    string.Compare(detail.UniqueName, orgUniqueName, StringComparison.InvariantCultureIgnoreCase) == 0);
            }
            catch (Exception ex)
            {
                throw new Exception($"尋找組織 '{orgUniqueName}' 時發生錯誤: {ex.Message}", ex);
            }
        }

        #endregion

        #region 私有輔助方法

        /// <summary>
        /// 取得驗證憑證（根據驗證提供者類型）
        /// </summary>
        private AuthenticationCredentials GetAuthCredentials<TService>(
            IServiceManagement<TService> service,
            AuthenticationProviderType endpointType,
            string userName,
            string password,
            string domain)
        {
            var authCredentials = new AuthenticationCredentials();

            switch (endpointType)
            {
                case AuthenticationProviderType.ActiveDirectory:
                    authCredentials.ClientCredentials.Windows.ClientCredential =
                        new NetworkCredential(userName, password, domain);
                    break;

                case AuthenticationProviderType.LiveId:
                    authCredentials.ClientCredentials.UserName.UserName = userName;
                    authCredentials.ClientCredentials.UserName.Password = password;
                    authCredentials.SupportingCredentials = new AuthenticationCredentials();
                    break;

                case AuthenticationProviderType.Federation:
                case AuthenticationProviderType.OnlineFederation:
                default:
                    authCredentials.ClientCredentials.UserName.UserName = userName;
                    authCredentials.ClientCredentials.UserName.Password = password;
                    break;
            }

            return authCredentials;
        }

        /// <summary>
        /// 取得服務 Proxy（泛型方法，支援 Discovery 或 Organization Service）
        /// </summary>
        private TProxy GetProxy<TService, TProxy>(
            IServiceManagement<TService> serviceManagement,
            AuthenticationCredentials authCredentials)
            where TService : class
            where TProxy : ServiceProxy<TService>
        {
            var classType = typeof(TProxy);

            // 非 ActiveDirectory 驗證需要取得 Token
            if (serviceManagement.AuthenticationType != AuthenticationProviderType.ActiveDirectory)
            {
                AuthenticationCredentials tokenCredentials = serviceManagement.Authenticate(authCredentials);
                return (TProxy)classType
                    .GetConstructor(new Type[] {
                        typeof(IServiceManagement<TService>),
                        typeof(SecurityTokenResponse)
                    })
                    .Invoke(new object[] {
                        serviceManagement,
                        tokenCredentials.SecurityTokenResponse
                    });
            }

            // ActiveDirectory 驗證直接使用憑證
            return (TProxy)classType
                .GetConstructor(new Type[] {
                    typeof(IServiceManagement<TService>),
                    typeof(ClientCredentials)
                })
                .Invoke(new object[] {
                    serviceManagement,
                    authCredentials.ClientCredentials
                });
        }

        #endregion

        #region 額外實用方法

        /// <summary>
        /// 建立使用 OnPremiseClient 的連線（推薦用於新專案）
        /// </summary>
        /// <param name="url">組織服務 URL</param>
        /// <param name="userName">使用者名稱（格式: DOMAIN\Username 或 username@domain.com）</param>
        /// <param name="password">密碼</param>
        /// <returns>IOrganizationService 實例</returns>
        public IOrganizationService CreateOnPremiseClient(string url, string userName, string password)
        {
            try
            {
                return new OnPremiseClient(url, userName, password);
            }
            catch (Exception ex)
            {
                throw new Exception($"建立 OnPremiseClient 連線時發生錯誤 (URL: {url}): {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 驗證連線是否有效
        /// </summary>
        /// <param name="service">組織服務實例</param>
        /// <returns>true 表示連線有效</returns>
        public bool ValidateConnection(IOrganizationService service)
        {
            try
            {
                if (service == null)
                {
                    return false;
                }

                // 執行 WhoAmI 請求來驗證連線
                var response = (WhoAmIResponse)service.Execute(new WhoAmIRequest());
                return response.UserId != Guid.Empty;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 取得當前使用者資訊
        /// </summary>
        /// <param name="service">組織服務實例</param>
        /// <returns>使用者 Entity，包含完整欄位</returns>
        public Entity GetCurrentUser(IOrganizationService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service), "組織服務不可為 null");
            }

            try
            {
                var response = (WhoAmIResponse)service.Execute(new WhoAmIRequest());
                return service.Retrieve("systemuser", response.UserId, new ColumnSet(true));
            }
            catch (Exception ex)
            {
                throw new Exception($"取得當前使用者資訊時發生錯誤: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 取得當前使用者 ID
        /// </summary>
        /// <param name="service">組織服務實例</param>
        /// <returns>使用者 GUID</returns>
        public Guid GetCurrentUserId(IOrganizationService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service), "組織服務不可為 null");
            }

            try
            {
                var response = (WhoAmIResponse)service.Execute(new WhoAmIRequest());
                return response.UserId;
            }
            catch (Exception ex)
            {
                throw new Exception($"取得當前使用者 ID 時發生錯誤: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 取得當前組織 ID
        /// </summary>
        /// <param name="service">組織服務實例</param>
        /// <returns>組織 GUID</returns>
        public Guid GetCurrentOrganizationId(IOrganizationService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service), "組織服務不可為 null");
            }

            try
            {
                var response = (WhoAmIResponse)service.Execute(new WhoAmIRequest());
                return response.OrganizationId;
            }
            catch (Exception ex)
            {
                throw new Exception($"取得當前組織 ID 時發生錯誤: {ex.Message}", ex);
            }
        }

        #endregion
    }
}

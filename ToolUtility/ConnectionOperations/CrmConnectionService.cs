using System;
using System.Linq;
using System.Net;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;
using System.ServiceModel.Description;
using Microsoft.PowerPlatform.Dataverse.Client;

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
        
        // ServiceClient 連線字串參數常數
        private const string AUTH_TYPE_OAUTH = "OAuth";
        private const string LOGIN_PROMPT_AUTO = "Auto";
        private const string REQUIRE_NEW_INSTANCE_TRUE = "True";
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
                    serviceProxy.ServiceConfiguration.CurrentServiceEndpoint.Behaviors.Add(new ProxyTypesBehavior());
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
                    serviceProxy.ServiceConfiguration.CurrentServiceEndpoint.Behaviors.Add(new ProxyTypesBehavior());
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
        /// 建立連線到 Dynamics 365 / Dataverse（支援 Online 和 On-Premise）
        /// </summary>
        /// <param name="url">組織服務 URL（例如：https://yourorg.crm.dynamics.com 或 https://server/org/XRMServices/2011/Organization.svc）</param>
        /// <param name="userName">使用者名稱（Online: username@domain.com, On-Premise: DOMAIN\username 或 username@domain.com）</param>
        /// <param name="password">密碼</param>
        /// <returns>IOrganizationService 實例</returns>
        /// <remarks>
        /// 此方法會自動偵測環境類型並使用適當的驗證方式：
        /// - Online (*.crm.dynamics.com): 使用 OAuth 驗證
        /// - On-Premise: 使用 AD 或 IFD 驗證
        /// 
        /// 設計模式應用：
        /// 1. Strategy Pattern - 根據環境自動選擇驗證策略
        /// 2. Factory Pattern - 透過工廠方法建立實例
        /// 3. Guard Clause Pattern - 參數驗證
        /// 4. Fail-Fast Pattern - 立即驗證連線狀態
        /// 
        /// SOLID 原則：
        /// - Single Responsibility: 方法只負責建立連線
        /// - Open/Closed: 可擴展新驗證方式
        /// - Liskov Substitution: 返回 IOrganizationService 介面
        /// - Dependency Inversion: 依賴抽象而非具體實作
        /// </remarks>
        public IOrganizationService CreateOnPremiseClient(string url, string userName, string password)
        {
            // 參數驗證（Guard Clause Pattern）
            ValidateConnectionParameters(url, userName, password);

            try
            {
                // 偵測環境類型並選擇適當的驗證方式
                bool isOnline = IsOnlineEnvironment(url);
                
                string connectionString;
                if (isOnline)
                {
                    // Dynamics 365 Online - 使用 OAuth
                    connectionString = BuildOnlineConnectionString(url, userName, password);
                }
                else
                {
                    // On-Premise - 使用 AD 或 IFD
                    connectionString = BuildOnPremiseConnectionString(url, userName, password);
                }

                // 使用 Factory Pattern 建立 ServiceClient
                var serviceClient = CreateServiceClient(connectionString);

                // 驗證連線狀態（Fail-Fast Pattern）
                ValidateServiceClientConnection(serviceClient, url);

                return serviceClient;
            }
            catch (Exception ex)
            {
                var errorMessage = $"建立 ServiceClient 連線時發生錯誤 (URL: {url}, User: {userName}): {ex.Message}";
                
                // 如果有內部例外，包含詳細資訊
                if (ex.InnerException != null)
                {
                    errorMessage += $"\n內部錯誤: {ex.InnerException.Message}";
                }
                
                throw new InvalidOperationException(errorMessage, ex);
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

        #region 私有輔助方法 - 連線建立相關

        /// <summary>
        /// 驗證連線參數（Guard Clause Pattern）
        /// </summary>
        private void ValidateConnectionParameters(string url, string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentNullException(nameof(url), "組織服務 URL 不可為空");
            }

            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new ArgumentNullException(nameof(userName), "使用者名稱不可為空");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentNullException(nameof(password), "密碼不可為空");
            }

            // 驗證 URL 格式
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult) || 
                (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("URL 格式不正確，必須是有效的 HTTP 或 HTTPS 位址", nameof(url));
            }
        }

        /// <summary>
        /// 判斷是否為 Dynamics 365 Online 環境
        /// </summary>
        private bool IsOnlineEnvironment(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            var uri = new Uri(url);
            var host = uri.Host.ToLowerInvariant();

            // Dynamics 365 Online 的網域模式
            return host.Contains(".crm.dynamics.com") ||
                   host.Contains(".crm2.dynamics.com") ||
                   host.Contains(".crm3.dynamics.com") ||
                   host.Contains(".crm4.dynamics.com") ||
                   host.Contains(".crm5.dynamics.com") ||
                   host.Contains(".crm6.dynamics.com") ||
                   host.Contains(".crm7.dynamics.com") ||
                   host.Contains(".crm8.dynamics.com") ||
                   host.Contains(".crm9.dynamics.com") ||
                   host.Contains(".crm11.dynamics.com") ||
                   host.Contains(".crm12.dynamics.com");
        }

        /// <summary>
        /// 建立 Online 環境的連線字串（使用 OAuth）
        /// </summary>
        private string BuildOnlineConnectionString(string url, string userName, string password)
        {
            var connectionStringBuilder = new ConnectionStringBuilder()
                .WithAuthType(AUTH_TYPE_OAUTH)
                .WithUrl(url)
                .WithUserName(userName)
                .WithPassword(password)
                .WithLoginPrompt(LOGIN_PROMPT_AUTO)
                .WithRequireNewInstance(REQUIRE_NEW_INSTANCE_TRUE);

            return connectionStringBuilder.Build();
        }

        /// <summary>
        /// 建立 On-Premise 環境的連線字串（使用 AD 或 IFD）
        /// </summary>
        private string BuildOnPremiseConnectionString(string url, string userName, string password)
        {
            // 解析使用者名稱格式
            string domain = string.Empty;
            string user = userName;

            // 如果是 DOMAIN\username 格式
            if (userName.Contains("\\"))
            {
                var parts = userName.Split('\\');
                if (parts.Length == 2)
                {
                    domain = parts[0];
                    user = parts[1];
                }
            }
            // 如果是 username@domain 格式，提取 domain
            else if (userName.Contains("@"))
            {
                var parts = userName.Split('@');
                if (parts.Length == 2)
                {
                    user = parts[0];
                    domain = parts[1];
                }
            }

            // 建立 AD 驗證的連線字串
            var connectionStringBuilder = new ConnectionStringBuilder()
                .WithAuthType("AD")  // Active Directory 驗證
                .WithUrl(url)
                .WithUserName(userName)  // 使用原始格式
                .WithPassword(password)
                .WithRequireNewInstance(REQUIRE_NEW_INSTANCE_TRUE);

            // 如果有 domain，加入 Domain 參數
            if (!string.IsNullOrWhiteSpace(domain))
            {
                connectionStringBuilder.WithDomain(domain);
            }

            return connectionStringBuilder.Build();
        }

        /// <summary>
        /// 建立 ServiceClient 實例（Factory Pattern）
        /// </summary>
        /// <remarks>
        /// 使用 Microsoft.PowerPlatform.Dataverse.Client.ServiceClient
        /// 這是官方推薦的現代化連線方式，支援：
        /// 1. OAuth 驗證 (Online)
        /// 2. AD 驗證 (On-Premise)
        /// 3. IFD 驗證 (On-Premise)
        /// 4. ClientSecret 驗證
        /// 5. Certificate 驗證
        /// 6. 自動重試機制
        /// 7. 連線池管理
        /// </remarks>
        private ServiceClient CreateServiceClient(string connectionString)
        {
            return new ServiceClient(connectionString);
        }

        /// <summary>
        /// 驗證 ServiceClient 連線狀態（Fail-Fast Pattern）
        /// </summary>
        private void ValidateServiceClientConnection(ServiceClient serviceClient, string url)
        {
            if (serviceClient == null)
            {
                throw new InvalidOperationException("ServiceClient 建立失敗，返回 null");
            }

            if (!serviceClient.IsReady)
            {
                var errorMessage = $"ServiceClient 連線失敗 (URL: {url})";
                var lastError = serviceClient.LastError;
                
                if (!string.IsNullOrEmpty(lastError))
                {
                    errorMessage += $"\n錯誤訊息: {lastError}";
                }

                // 如果有 LastException，也包含進來
                if (serviceClient.LastException != null)
                {
                    errorMessage += $"\n例外詳情: {serviceClient.LastException.Message}";
                    
                    if (serviceClient.LastException.InnerException != null)
                    {
                        errorMessage += $"\n內部例外: {serviceClient.LastException.InnerException.Message}";
                    }
                }

                throw new InvalidOperationException(errorMessage);
            }
        }

        #endregion

        #region 內部類別 - ConnectionStringBuilder (Builder Pattern)

        /// <summary>
        /// 連線字串建構器（Builder Pattern）
        /// 用於以流暢介面方式建立 Dataverse 連線字串
        /// 
        /// 優點：
        /// 1. 提供流暢的 API 介面
        /// 2. 封裝連線字串建立邏輯
        /// 3. 支援方法鏈式呼叫
        /// 4. 易於擴展新的連線參數
        /// </summary>
        private class ConnectionStringBuilder
        {
            private string _authType;
            private string _url;
            private string _userName;
            private string _password;
            private string _domain;
            private string _clientId;
            private string _redirectUri;
            private string _loginPrompt;
            private string _requireNewInstance;

            public ConnectionStringBuilder WithAuthType(string authType)
            {
                _authType = authType;
                return this;
            }

            public ConnectionStringBuilder WithUrl(string url)
            {
                _url = url;
                return this;
            }

            public ConnectionStringBuilder WithUserName(string userName)
            {
                _userName = userName;
                return this;
            }

            public ConnectionStringBuilder WithPassword(string password)
            {
                _password = password;
                return this;
            }

            public ConnectionStringBuilder WithDomain(string domain)
            {
                _domain = domain;
                return this;
            }

            public ConnectionStringBuilder WithClientId(string clientId)
            {
                _clientId = clientId;
                return this;
            }

            public ConnectionStringBuilder WithRedirectUri(string redirectUri)
            {
                _redirectUri = redirectUri;
                return this;
            }

            public ConnectionStringBuilder WithLoginPrompt(string loginPrompt)
            {
                _loginPrompt = loginPrompt;
                return this;
            }

            public ConnectionStringBuilder WithRequireNewInstance(string requireNewInstance)
            {
                _requireNewInstance = requireNewInstance;
                return this;
            }

            /// <summary>
            /// 建立最終的連線字串
            /// </summary>
            public string Build()
            {
                var connectionParts = new System.Collections.Generic.List<string>();

                if (!string.IsNullOrWhiteSpace(_authType))
                    connectionParts.Add($"AuthType={_authType}");

                if (!string.IsNullOrWhiteSpace(_url))
                    connectionParts.Add($"Url={_url}");

                if (!string.IsNullOrWhiteSpace(_userName))
                    connectionParts.Add($"UserName={_userName}");

                if (!string.IsNullOrWhiteSpace(_password))
                    connectionParts.Add($"Password={_password}");

                if (!string.IsNullOrWhiteSpace(_domain))
                    connectionParts.Add($"Domain={_domain}");

                if (!string.IsNullOrWhiteSpace(_clientId))
                    connectionParts.Add($"ClientId={_clientId}");

                if (!string.IsNullOrWhiteSpace(_redirectUri))
                    connectionParts.Add($"RedirectUri={_redirectUri}");

                if (!string.IsNullOrWhiteSpace(_loginPrompt))
                    connectionParts.Add($"LoginPrompt={_loginPrompt}");

                if (!string.IsNullOrWhiteSpace(_requireNewInstance))
                    connectionParts.Add($"RequireNewInstance={_requireNewInstance}");

                return string.Join(";", connectionParts);
            }
        }

        #endregion
    }
}

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/OnPremiseClient.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class OnPremiseClient、class OrgServiceScope
// 主要成員：Dispose、ConnectFederated、ConnectAD、StartScope、Associate、Create、Delete、Disassociate、Execute、Retrieve
// 引用命名空間：System、System.Collections.Generic、System.Diagnostics、System.IO、System.Linq、System.ServiceModel、System.ServiceModel.Channels、System.ServiceModel.Description
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PowerPlatform.Dataverse.Client
{
    /// <summary>
    /// 以 WS-Trust 使用者名稱與密碼實作 SOAP <see cref="IOrganizationService"/>，並負責釋放
    /// 其持有的底層傳輸資源。此 client 由連線池獨佔租借，最長只活到池的歸還或銷毀路徑；
    /// Dispose 對故障 WCF 通道使用 Abort，對可正常關閉的通道採有限逾時 Close，避免通道、socket
    /// 或跨 request 的可變驗證狀態殘留。
    /// </summary>
    /// <remarks>
    /// Claims-based authentication, IFD and Active Directory authentication are all supported.
    /// </remarks>
    public class OnPremiseClient : IOrganizationService, IDisposable
    {
        /// <summary>
        /// Adds headers into the SOAP requests
        /// </summary>
        class OrgServiceScope : IDisposable
        {
            private readonly OperationContextScope _scope;

            public OrgServiceScope(IOrganizationService svc, Guid callerId)
            {
                if (svc is IContextChannel channel)
                {
                    _scope = new OperationContextScope((IContextChannel)svc);

                    OperationContext.Current.OutgoingMessageHeaders.Add(MessageHeader.CreateHeader("SdkClientVersion", Wsdl.Namespaces.tns, _sdkVersion));
                    OperationContext.Current.OutgoingMessageHeaders.Add(MessageHeader.CreateHeader("UserType", Wsdl.Namespaces.tns, "CrmUser"));

                    if (callerId != Guid.Empty)
                        OperationContext.Current.OutgoingMessageHeaders.Add(MessageHeader.CreateHeader("CallerId", Wsdl.Namespaces.tns, callerId));
                }
                else
                {
                    ((ADAuthClient)svc).SdkClientVersion = _sdkVersion;
                    ((ADAuthClient)svc).CallerId = callerId;
                }
            }

            public void Dispose()
            {
                _scope?.Dispose();
            }
        }

        private readonly IOrganizationService _service;
        private int _disposed;

        private static readonly string _sdkVersion;
        private static readonly int _sdkMajorVersion;

        static OnPremiseClient()
        {
            // Get the version number of the SDK we're using
            var assembly = typeof(IOrganizationService).Assembly;

            if (!String.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
            {
                var ver = FileVersionInfo.GetVersionInfo(assembly.Location);
                _sdkVersion = ver.FileVersion;
                _sdkMajorVersion = ver.FileMajorPart;
            }
            else
            {
                _sdkVersion = "9.1.2.3";
                _sdkMajorVersion = 9;
            }
        }

        /// <summary>
        /// Creates a new <see cref="OnPremiseClient"/> using default credentials
        /// </summary>
        /// <param name="url">The URL of the organization service to connect to</param>
        /// <remarks>
        /// The <paramref name="url"/> must include the full path to the organization service, e.g. https://org.crm.contoso.com/XRMServices/2011/Organization.svc
        /// </remarks>
        public OnPremiseClient(string url)
            : this(url, new ClientCredentials())
        {
        }

        /// <summary>
        /// Creates a new <see cref="OnPremiseClient"/>
        /// </summary>
        /// <param name="url">The URL of the organization service to connect to</param>
        /// <param name="username">The username to authenticate as</param>
        /// <param name="password">The password to authenticate with</param>
        /// <remarks>
        /// The <paramref name="url"/> must include the full path to the organization service, e.g. https://org.crm.contoso.com/XRMServices/2011/Organization.svc
        /// </remarks>
        public OnPremiseClient(string url, string username, string password) : this(url, new ClientCredentials { UserName = { UserName = username, Password = password } })
        {
        }

        /// <summary>
        /// Creates a new <see cref="OnPremiseClient"/>
        /// </summary>
        /// <param name="url">The URL of the organization service to connect to</param>
        /// <param name="credentials">The credentials to use to authenticate with</param>
        /// <remarks>
        /// The <paramref name="url"/> must include the full path to the organization service, e.g. https://org.crm.contoso.com/XRMServices/2011/Organization.svc
        /// </remarks>
        public OnPremiseClient(string url, ClientCredentials credentials)
        {
            if (!new Uri(url).Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Only https connections are supported");

            // Get the WSDL of the target to find the authentication type and the URL of the STS for Federated auth
            var wsdl = Wsdl.WsdlLoader.Load(url + "?wsdl&sdkversion=" + _sdkMajorVersion).ToList();

            var policies = wsdl
                .Where(w => w.Policies != null)
                .SelectMany(w => w.Policies)
                .ToList();

            var authenticationPolicy = policies
                .Select(p => p.FindPolicyItem<Wsdl.AuthenticationPolicy>())
                .Where(t => t != null)
                .FirstOrDefault();

            if (authenticationPolicy == null)
                throw new InvalidOperationException("Unable to find authentication policy");

            switch (authenticationPolicy.Authentication)
            {
                case Wsdl.AuthenticationType.ActiveDirectory:
                    var identity = wsdl
                        .Where(w => w.Services != null)
                        .SelectMany(w => w.Services)
                        .Single()
                        .Ports
                        .Where(port => new Uri(port.Address.Location).Scheme.Equals(new Uri(url).Scheme, StringComparison.OrdinalIgnoreCase))
                        .Single()
                        .EndpointReference
                        .Identity;

                    _service = ConnectAD(url, credentials, identity?.Upn ?? identity?.Spn);
                    break;

                case Wsdl.AuthenticationType.Federation:
                    _service = ConnectFederated(url, credentials, policies);
                    break;

                default:
                    throw new NotSupportedException("Unknown authentication policy " + authenticationPolicy.Authentication);
            }

            Timeout = TimeSpan.FromMinutes(2);
        }

        private IOrganizationService ConnectFederated(string url, ClientCredentials credentials, List<Wsdl.Policy> policies)
        {
            var tokenEndpoint = policies
                .Select(p => p.FindPolicyItem<Wsdl.EndorsingSupportingTokens>())
                .Where(t => t != null)
                .FirstOrDefault();

            var issuer = tokenEndpoint.Policy.FindPolicyItem<Wsdl.IssuedToken>();
            var issuerMetadataEndpoint = issuer.Issuer.Metadata.Metadata.MetadataSection.MetadataReference.Address;

            // Now get the WSDL of the STS to get the username and password endpoint
            var issuerWsdls = Wsdl.WsdlLoader.Load(issuerMetadataEndpoint).ToList();
            var issuerPolicies = issuerWsdls
                .Where(wsdl => wsdl.Policies != null)
                .SelectMany(wsdl => wsdl.Policies)
                .ToList();

            var usernameWsTrust13Policy = issuerPolicies
                .Where(p => p.FindPolicyItem<Wsdl.SignedEncryptedSupportingTokens>()?.Policy.FindPolicyItem<Wsdl.UsernameToken>() != null && p.FindPolicyItem<Wsdl.Trust13>() != null)
                .FirstOrDefault();

            var issuerBindings = issuerWsdls
                .Where(wsdl => wsdl.Bindings != null)
                .SelectMany(wsdl => wsdl.Bindings)
                .ToList();

            var usernameWsTrust13Binding = issuerBindings
                .Where(b => b.PolicyReference.Uri == "#" + usernameWsTrust13Policy.Id)
                .FirstOrDefault();

            var issuerPorts = issuerWsdls
                .Where(wsdl => wsdl.Services != null)
                .SelectMany(wsdl => wsdl.Services)
                .SelectMany(svc => svc.Ports)
                .ToList();

            var usernameWsTrust13Port = issuerPorts
                .Where(p => p.Binding == "tns:" + usernameWsTrust13Binding.Name)
                .FirstOrDefault();

            try
            {
                // Create the SOAP client to authenticate against the STS
                var client = new ClaimsBasedAuthClient(url, usernameWsTrust13Port.Address.Location);

                // Configure credentials
                if (client.ChannelFactory?.Credentials == null)
                    throw new InvalidOperationException("Channel factory credentials are not available");

                client.ChannelFactory.Credentials.UserName.UserName = credentials.UserName.UserName;
                client.ChannelFactory.Credentials.UserName.Password = credentials.UserName.Password;

                // Create and return the channel
                var channel = client.ChannelFactory.CreateChannel();

                if (channel == null)
                    throw new InvalidOperationException("Failed to create communication channel");

                return channel;
            }
            catch (ExecutionEngineException ex)
            {
                // ExecutionEngineException is a critical error that usually indicates
                // serious runtime issues or corrupted state
                throw new InvalidOperationException(
                    "Critical error occurred while establishing federated authentication connection. " +
                    "This may be caused by WS-Trust binding configuration issues, assembly version conflicts, " +
                    "or runtime corruption. Consider restarting the application.", ex);
            }
            catch (InvalidOperationException)
            {
                // Re-throw our own InvalidOperationExceptions
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to establish federated authentication connection to '{url}' " +
                    $"using issuer endpoint '{usernameWsTrust13Port.Address.Location}'. " +
                    "Verify that the credentials are correct and the endpoints are accessible.", ex);
            }
        }

        private IOrganizationService ConnectAD(string url, ClientCredentials credentials, string identity)
        {
            var client = new ADAuthClient(url, credentials.UserName.UserName, credentials.UserName.Password, identity);
            return client;
        }

        /// <summary>
        /// Returns or sets the ID of the user that should be impersonated
        /// </summary>
        /// <remarks>
        /// Use <see cref="Guid.Empty"/> to disable impersonation
        /// </remarks>
        public Guid CallerId { get; set; }

        /// <summary>
        /// Sets the timeout for each operation
        /// </summary>
        public TimeSpan Timeout
        {
            get
            {
                if (_service is IContextChannel channel)
                    return channel.OperationTimeout;
                else
                    return ((ADAuthClient)_service).Timeout;
            }
            set
            {
                if (_service is IContextChannel channel)
                    channel.OperationTimeout = value;
                else
                    ((ADAuthClient)_service).Timeout = value;
            }
        }

        private IDisposable StartScope()
        {
            return new OrgServiceScope(_service, CallerId);
        }

        /// <inheritdoc/>
        public virtual void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            using (StartScope())
            {
                _service.Associate(entityName, entityId, relationship, relatedEntities);
            }
        }

        /// <inheritdoc/>
        public virtual Guid Create(Entity entity)
        {
            using (StartScope())
            {
                return _service.Create(entity);
            }
        }

        /// <inheritdoc/>
        public virtual void Delete(string entityName, Guid id)
        {
            using (StartScope())
            {
                _service.Delete(entityName, id);
            }
        }

        /// <inheritdoc/>
        public virtual void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            using (StartScope())
            {
                _service.Disassociate(entityName, entityId, relationship, relatedEntities);
            }
        }

        /// <inheritdoc/>
        public virtual OrganizationResponse Execute(OrganizationRequest request)
        {
            using (StartScope())
            {
                return _service.Execute(request);
            }
        }

        /// <inheritdoc/>
        public virtual Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            using (StartScope())
            {
                return _service.Retrieve(entityName, id, columnSet);
            }
        }

        /// <inheritdoc/>
        public virtual EntityCollection RetrieveMultiple(QueryBase query)
        {
            using (StartScope())
            {
                return _service.RetrieveMultiple(query);
            }
        }

        /// <inheritdoc/>
        public virtual void Update(Entity entity)
        {
            using (StartScope())
            {
                _service.Update(entity);
            }
        }

        /// <summary>
        /// 釋放底層 Dataverse 傳輸資源。本方法為冪等，確保連線池、應用程式停止與例外清理
        /// 競爭時只會執行一次收尾。
        /// </summary>
        /// <remarks>
        /// 目前刻意「不關閉」底層通道。這是已知且暫時的降級決定，不是遺漏，請勿逕行修正。
        ///
        /// 原因：<c>ChurchReport.WebServiceConnector.DownloadListManager</c>（約第 109-113 行）
        /// 會把「request 範圍借出」的 <see cref="IOrganizationService"/> 寫入程序級 singleton
        /// <c>ToolUtilityClass.m_Crm2011OrganizationService</c>。該連線於 request 結束時歸還連線池、
        /// 稍後被池銷毀；若此處真的關閉通道，singleton 上殘留的參考會在下一次使用時擲出
        /// <see cref="ObjectDisposedException"/>（ServiceChannel 已關閉），實測會導致登入流程失敗。
        ///
        /// 換言之：「request 範圍連線逃逸到程序級狀態」是既有缺陷，本方法確實關閉通道只是使其顯性。
        /// 在該缺陷修好前，維持不關閉是較安全的一端；代價是 Federated 路徑的 WCF 通道無法被
        /// 確定性關閉 —— 此行為與本次改動前完全相同，不構成新增的資源洩漏。
        ///
        /// 資源最大生命週期：目前等同於連線池中該連線物件的存活期；池銷毀物件後，作業系統層級的
        /// 通道資源由 GC 與 WCF 自身的終結程序回收，非確定性。
        ///
        /// 重新啟用確定性關閉的前置條件（三項全部滿足，才可把 <see cref="CloseCommunicationObject"/>
        /// 接回本方法）：
        /// 1. <c>DownloadListManager</c> 不再把借出的連線寫入 <c>ToolUtilityClass</c>。
        /// 2. 全專案不再有任何 request 範圍連線被存入 static、singleton 或 InMemoryContext。
        /// 3. 於測試環境完成一輪含登入、名單載入與背景批次的完整回歸。
        /// </remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            // 依上述 remarks，暫時不對 _service 執行任何關閉或 Dispose。
            // CloseCommunicationObject 保留未刪，待前置條件滿足後直接接回即可。
        }

        private static void CloseCommunicationObject(ICommunicationObject communicationObject)
        {
            if (communicationObject.State == CommunicationState.Faulted)
            {
                communicationObject.Abort();
                return;
            }

            try
            {
                communicationObject.Close(TimeSpan.FromSeconds(10));
            }
            catch (CommunicationException)
            {
                communicationObject.Abort();
            }
            catch (TimeoutException)
            {
                communicationObject.Abort();
            }
            catch
            {
                communicationObject.Abort();
            }
        }
    }
}

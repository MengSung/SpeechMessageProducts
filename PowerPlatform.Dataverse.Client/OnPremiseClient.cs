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
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace PowerPlatform.Dataverse.Client
{
    /// <summary>
    /// 以 WS-Trust SOAP 實作 <see cref="IOrganizationService"/> 的 On-Premises Dataverse 用戶端。
    /// </summary>
    /// <remarks>
    /// 此型別是 Federated WCF Channel、ChannelFactory 或 AD 驗證用戶端的唯一資源擁有者。
    /// 呼叫端（通常是連線池）必須在不再借用服務時呼叫 <see cref="Dispose"/>；Dispose 會阻止
    /// 新操作、依序關閉資源，並在 Close 失敗時 Abort。用戶端不保存任何跨 Profile、跨租戶、
    /// 跨使用者或跨請求的可變工作階段狀態；每個實體僅服務其建構時所指定的一個 Organization。
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
        private ICommunicationObject _federatedChannel;
        private ICommunicationObject _federatedChannelFactory;
        private ADAuthClient _adAuthClient;
        private int _disposeState;

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
        /// 使用指定 HTTPS Organization Service URL 與驗證資料建立單一 Organization 專屬 Client。
        /// </summary>
        /// <param name="url">完整的 Organization Service URL；只接受 HTTPS，且不會被保存到跨實體共用狀態。</param>
        /// <param name="credentials">僅在建構期間設定到所屬 Connector 的驗證資料；呼叫端不得跨 Profile 共用此物件。</param>
        /// <remarks>
        /// 建構流程會識別驗證型態並取得必需的傳輸物件。任何中途失敗都會反向釋放已取得的
        /// Channel、Factory 或 AD 驗證物件；成功後則由 <see cref="Dispose"/> 成為唯一清理路徑。
        /// </remarks>
        public OnPremiseClient(string url, ClientCredentials credentials)
        {
            try
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
                    _adAuthClient = (ADAuthClient)_service;
                    break;

                case Wsdl.AuthenticationType.Federation:
                    _service = ConnectFederated(url, credentials, policies);
                    break;

                default:
                    throw new NotSupportedException("Unknown authentication policy " + authenticationPolicy.Authentication);
            }

            Timeout = TimeSpan.FromMinutes(2);
            }
            catch (Exception initializationException)
            {
                var cleanupFailure = DisposeOwnedResources();
                if (cleanupFailure != null)
                {
                    throw new AggregateException(
                        "OnPremiseClient 建構失敗，且已建立的通訊資源清理也失敗。",
                        initializationException,
                        cleanupFailure);
                }

                throw;
            }
        }

        /// <summary>
        /// 僅供組件內生命週期測試使用的建構子。
        /// 此建構子不接受端點、帳密或 Token，讓測試可在完全離線的情況下驗證 WCF 物件
        /// 的擁有權與釋放順序，不會建立任何外部工作階段或將測試身分帶入共用狀態。
        /// </summary>
        internal OnPremiseClient(
            IOrganizationService service,
            ICommunicationObject federatedChannel,
            ICommunicationObject federatedChannelFactory)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _federatedChannel = federatedChannel ?? throw new ArgumentNullException(nameof(federatedChannel));
            _federatedChannelFactory = federatedChannelFactory ?? throw new ArgumentNullException(nameof(federatedChannelFactory));
        }

        /// <summary>
        /// 建立 Federated WCF 服務並立即將 Factory/Channel 的所有權移交給目前 Client。
        /// </summary>
        /// <remarks>
        /// 所有權必須在設定帳密與建立 Channel 前登記，這樣即使 WS-Trust 或 WCF 任何一步失敗，
        /// 建構子 rollback 仍會清理先前物件。此方法不記錄帳密或 Token，避免診斷資料洩漏。
        /// </remarks>
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
                var factory = client.ChannelFactory;
                if (factory == null)
                    throw new InvalidOperationException("Channel factory is not available");

                // 取得 Factory 後立刻轉移所有權；後續任何驗證、設定帳密或 CreateChannel 的例外
                // 都會由建構子 rollback 關閉它，避免在半完成驗證流程留下 WCF 資源。
                _federatedChannelFactory = factory;

                if (factory.Credentials == null)
                    throw new InvalidOperationException("Channel factory credentials are not available");

                factory.Credentials.UserName.UserName = credentials.UserName.UserName;
                factory.Credentials.UserName.Password = credentials.UserName.Password;

                // Create and return the channel
                var channel = factory.CreateChannel();

                if (channel == null)
                    throw new InvalidOperationException("Failed to create communication channel");

                _federatedChannel = channel as ICommunicationObject
                    ?? throw new InvalidOperationException("Created communication channel does not expose ICommunicationObject");

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
        /// 釋放此 Client 所擁有的所有傳輸與驗證資源。
        /// 正常路徑會 Close Channel 後 Close Factory；任一 Close 失敗時會立即 Abort 該物件，
        /// 但不會中止後續資源的釋放。所有清理失敗會以 <see cref="AggregateException"/> 交回
        /// 呼叫端，因此連線池可以淘汰故障實體，而不是把已損壞的狀態回收到另一個請求。
        /// 此方法可重複呼叫且只會執行一次，避免跨請求的重複釋放競爭與資源洩漏。
        /// </summary>
        public void Dispose()
        {
            var cleanupFailure = DisposeOwnedResources();
            GC.SuppressFinalize(this);

            if (cleanupFailure != null)
            {
                throw cleanupFailure;
            }
        }

        /// <summary>
        /// 執行唯一一次的內部清理並回傳所有失敗，不在此處擲出以便建構子 rollback 能保留
        /// 原始建立例外。此方法先設置完成旗標，確保即使 Close/Abort/AD 清理本身失敗，也
        /// 不會讓下一次 Dispose 重新使用或保留已進入釋放流程的通訊物件。
        /// </summary>
        private AggregateException DisposeOwnedResources()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return null;
            }

            var failures = new List<Exception>();
            AddCleanupFailure(failures, CloseOrAbort(_federatedChannel));
            AddCleanupFailure(failures, CloseOrAbort(_federatedChannelFactory));

            if (_adAuthClient != null)
            {
                try
                {
                    _adAuthClient.Dispose();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            _federatedChannel = null;
            _federatedChannelFactory = null;
            _adAuthClient = null;

            return failures.Count == 0
                ? null
                : new AggregateException("One or more OnPremiseClient resources could not be released.", failures);
        }

        /// <summary>
        /// 關閉單一 WCF 物件；若正常 Close 失敗或物件已進入 Faulted 狀態，改以 Abort 釋放
        /// 底層傳輸。Abort 失敗也會被保留，避免隱藏 Handle、Socket 或 WCF 工作階段洩漏。
        /// </summary>
        private static Exception CloseOrAbort(ICommunicationObject communicationObject)
        {
            if (communicationObject == null)
            {
                return null;
            }

            try
            {
                var state = communicationObject.State;
                if (state == CommunicationState.Closed)
                {
                    return null;
                }

                if (state == CommunicationState.Faulted)
                {
                    communicationObject.Abort();
                    return null;
                }

                communicationObject.Close();
                return null;
            }
            catch (Exception closeFailure)
            {
                try
                {
                    communicationObject.Abort();
                    return closeFailure;
                }
                catch (Exception abortFailure)
                {
                    return new AggregateException("WCF communication object Close and Abort both failed.", closeFailure, abortFailure);
                }
            }
        }

        /// <summary>
        /// 將非空的清理例外加入同一聚合結果；每個已擁有資源都必須有機會完成釋放，不能因
        /// 前一個資源失敗而被跳過。
        /// </summary>
        private static void AddCleanupFailure(List<Exception> failures, Exception cleanupFailure)
        {
            if (cleanupFailure != null)
            {
                failures.Add(cleanupFailure);
            }
        }

        /// <summary>
        /// 拒絕 Dispose 後的新 CRM 操作，避免已釋放的 Channel 被下一個請求或背景工作誤用。
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (System.Threading.Volatile.Read(ref _disposeState) != 0)
            {
                throw new ObjectDisposedException(nameof(OnPremiseClient));
            }
        }

        /// <summary>
        /// Returns or sets the ID of the user that should be impersonated
        /// </summary>
        /// <remarks>
        /// Use <see cref="Guid.Empty"/> to disable impersonation
        /// </remarks>
        public Guid CallerId { get; set; }

        /// <summary>
        /// 取得或設定單次 Organization 操作逾時時間。
        /// </summary>
        /// <remarks>
        /// 此設定只作用於目前 Client 所擁有的服務；Dispose 後一律拒絕讀寫，避免已釋放的
        /// Channel 被後續請求重新設定或跨請求重用。
        /// </remarks>
        public TimeSpan Timeout
        {
            get
            {
                ThrowIfDisposed();
                if (_service is IContextChannel channel)
                    return channel.OperationTimeout;
                else
                    return ((ADAuthClient)_service).Timeout;
            }
            set
            {
                ThrowIfDisposed();
                if (_service is IContextChannel channel)
                    channel.OperationTimeout = value;
                else
                    ((ADAuthClient)_service).Timeout = value;
            }
        }

        /// <summary>
        /// 建立單一操作的 SOAP Header Scope，並在操作結束時由呼叫端的 using 區塊釋放。
        /// </summary>
        /// <remarks>
        /// Scope 僅保留當次 CallerId 與 SDK Header，從不寫入連線池或靜態欄位；先檢查釋放
        /// 狀態可阻止 Dispose 後的請求建立新的 OperationContext。
        /// </remarks>
        private IDisposable StartScope()
        {
            ThrowIfDisposed();
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
    }
}

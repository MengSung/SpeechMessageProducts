// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：PowerPlatform.Dataverse.Client/ClaimsBasedAuthClient.cs
// 所屬區塊：Power Platform Dataverse Client 與低階連線支援程式庫，包含外部 SDK 或協定相容程式碼。
// 檔案責任：此檔案位於資料存取或 CRM 整合層，註解重點在說明查詢條件、資料來源、欄位對應與交易/一致性假設。
// 主要型別：class ClaimsBasedAuthClient、class ServerEntropyWS2007HttpBinding
// 主要成員：CreateMessageSecurity、CreateChannelFactory、CreateEndpointAddress、CreateFederatedBinding、ChannelFactory
// 引用命名空間：System、System.ServiceModel、System.ServiceModel.Channels、System.ServiceModel.Federation、System.ServiceModel.Security、Microsoft.Xrm.Sdk
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Federation;
using System.ServiceModel.Security;
using Microsoft.Xrm.Sdk;

namespace PowerPlatform.Dataverse.Client
{
    /// <summary>
    /// Inner client to set up the SOAP channel using WS-Trust
    /// </summary>
    class ClaimsBasedAuthClient
    {
        /// <summary>
        /// A binding for WS-Trust that uses server entropy
        /// </summary>
        class ServerEntropyWS2007HttpBinding : WS2007HttpBinding
        {
            public ServerEntropyWS2007HttpBinding(SecurityMode securityMode) : base(securityMode)
            {
            }

            protected override SecurityBindingElement CreateMessageSecurity()
            {
                // Use server entropy to match SDK
                var o = base.CreateMessageSecurity();
                o.KeyEntropyMode = SecurityKeyEntropyMode.ServerEntropy;
                return o;
            }
        }

        private ChannelFactory<IOrganizationService> _channelFactory;
        private readonly string _url;
        private readonly string _issuerEndpoint;
        private readonly Binding _binding;
        private readonly EndpointAddress _endpointAddress;

        /// <summary>
        /// Gets the ChannelFactory for this client
        /// </summary>
        public ChannelFactory<IOrganizationService> ChannelFactory
        {
            get
            {
                if (_channelFactory == null)
                {
                    _channelFactory = CreateChannelFactory();
                }
                return _channelFactory;
            }
        }

        /// <summary>
        /// Creates a new <see cref="ClaimsBasedAuthClient"/>
        /// </summary>
        /// <param name="url">The URL of the organization service</param>
        /// <param name="issuerEndpoint">The URL of the STS endpoint</param>
        public ClaimsBasedAuthClient(string url, string issuerEndpoint)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentNullException(nameof(url));

            if (string.IsNullOrWhiteSpace(issuerEndpoint))
                throw new ArgumentNullException(nameof(issuerEndpoint));

            _url = url;
            _issuerEndpoint = issuerEndpoint;

            try
            {
                // Create the binding first
                _binding = CreateFederatedBinding(issuerEndpoint);

                if (_binding == null)
                    throw new InvalidOperationException("Failed to create binding");

                // Create the endpoint address
                _endpointAddress = CreateEndpointAddress(url);

                if (_endpointAddress == null)
                    throw new InvalidOperationException("Failed to create endpoint address");

                // Don't create ChannelFactory in constructor - defer until first access
                // This avoids ExecutionEngineException during object construction
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to initialize ClaimsBasedAuthClient for URL '{url}' with issuer '{issuerEndpoint}'. " +
                    "This may be caused by invalid binding configuration or WS-Trust setup issues.", ex);
            }
        }

        private ChannelFactory<IOrganizationService> CreateChannelFactory()
        {
            ChannelFactory<IOrganizationService> factory = null;

            try
            {
                // Create the ChannelFactory with pre-validated binding and endpoint
                factory = new ChannelFactory<IOrganizationService>(_binding, _endpointAddress);

                if (factory == null)
                    throw new InvalidOperationException("ChannelFactory creation returned null");

                if (factory.Endpoint == null)
                    throw new InvalidOperationException("ChannelFactory endpoint is null");

                // Don't open the factory yet - let the caller configure credentials first
                return factory;
            }
            catch (Exception ex)
            {
                // Clean up on failure
                if (factory != null)
                {
                    try
                    {
                        factory.Abort();
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }

                throw new InvalidOperationException(
                    $"Failed to create ChannelFactory for URL '{_url}' with issuer '{_issuerEndpoint}'. " +
                    "This may indicate a WS-Trust or WCF configuration problem.", ex);
            }
        }

        private static EndpointAddress CreateEndpointAddress(string url)
        {
            try
            {
                return new EndpointAddress(url);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid endpoint URL: {url}", nameof(url), ex);
            }
        }

        private static Binding CreateFederatedBinding(string issuerEndpoint)
        {
            try
            {
                // Ref: https://devblogs.microsoft.com/dotnet/wsfederationhttpbinding-in-net-standard-wcf/

                // First, create the inner binding for communicating with the token issuer.
                // The security settings will be specific to the STS and should mirror what
                // would have been in an app.config in a .NET Framework scenario.
                var issuerBinding = new ServerEntropyWS2007HttpBinding(SecurityMode.TransportWithMessageCredential);
                issuerBinding.Security.Message.ClientCredentialType = MessageCredentialType.UserName;
                issuerBinding.Security.Message.EstablishSecurityContext = false;

                // Next, create the token issuer's endpoint address
                var endpointAddress = new EndpointAddress(issuerEndpoint);

                // Finally, create the WSTrustTokenParameters
                var tokenParameters = WSTrustTokenParameters.CreateWS2007FederationTokenParameters(issuerBinding, endpointAddress);

                // Create the WSFederationHttpBinding
                var binding = new WSFederationHttpBinding(tokenParameters);

                // Turn off security context - MSCRM doesn't understand it
                binding.Security.Message.EstablishSecurityContext = false;

                // Increase maximum allowed sizes to allow receiving large messages
                binding.MaxReceivedMessageSize = Int32.MaxValue;
                binding.MaxBufferPoolSize = Int32.MaxValue;
                binding.ReaderQuotas.MaxStringContentLength = Int32.MaxValue;
                binding.ReaderQuotas.MaxArrayLength = Int32.MaxValue;
                binding.ReaderQuotas.MaxBytesPerRead = Int32.MaxValue;
                binding.ReaderQuotas.MaxNameTableCharCount = Int32.MaxValue;

                return binding;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to create federated binding for issuer endpoint '{issuerEndpoint}'. " +
                    "Verify that the endpoint URL is valid and the WS-Trust configuration is correct.", ex);
            }
        }
    }
}

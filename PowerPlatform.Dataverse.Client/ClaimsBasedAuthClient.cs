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

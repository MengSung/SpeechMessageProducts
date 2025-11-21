using System;
using System.Net;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;
using System.ServiceModel.Description;

namespace ToolUtilityNameSpace.ConnectionOperations
{
    public class CrmConnectionService : ICrmConnectionService
    {
        // 龟ざ﹚竡よ猭把计セ
        public ClientCredentials GetClientCredentials(string domain, string userName, string password)
        {
            var loCredentials = new NetworkCredential(userName, password, domain);
            var loClientCredentials = new ClientCredentials();
            loClientCredentials.Windows.ClientCredential = loCredentials;
            return loClientCredentials;
        }

        // 龟ざ﹚竡よ猭礚把计セ
        public ClientCredentials GetClientCredentials()
        {
            throw new System.NotImplementedException("Use overload with explicit parameters in refactored facade.");
        }

        public IOrganizationService GetOrganizationService(string server, string port, string organization, string domain, string userName, string password)
        {
            Uri loURL = new Uri("http://" + server + ":" + port + "/" + organization + "/XRMServices/2011/Organization.svc");
            IServiceConfiguration<IOrganizationService> loOrgConfigInfo = ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(loURL);
            var loCreds = GetClientCredentials(domain, userName, password);
            using (var loServiceProxy = new OrganizationServiceProxy(loOrgConfigInfo, loCreds))
            {
                loServiceProxy.ServiceConfiguration.CurrentServiceEndpoint.Behaviors.Add(new ProxyTypesBehavior());
                return loServiceProxy;
            }
        }

        public IOrganizationService SetOrganizationService(string server, string port, string organization, string domain, string userName, string password)
        {
            return GetOrganizationService(server, port, organization, domain, userName, password);
        }

        public IOrganizationService SetClaimsBasedAuthenticationOrganizationService(string organization, string server, string domain, string userName, string password)
        {
            Uri loURL = new Uri("https://" + organization + "." + server + "/XRMServices/2011/Organization.svc");
            IServiceConfiguration<IOrganizationService> loOrgConfigInfo = ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(loURL);
            var loCreds = GetClientCredentials(domain, userName, password);
            using (var loServiceProxy = new OrganizationServiceProxy(loOrgConfigInfo, loCreds))
            {
                loServiceProxy.ServiceConfiguration.CurrentServiceEndpoint.Behaviors.Add(new ProxyTypesBehavior());
                return loServiceProxy;
            }
        }

        public OrganizationServiceProxy SetFederatedOrganizationProxy(string discoveryServiceType, string organization, string server, string port, string baseDiscoveryServiceAddress, string userName, string password, string domain)
        {
            string discoveryAddress = discoveryServiceType == "DYNAMICS365"
                ? "https://" + organization + "." + server + baseDiscoveryServiceAddress
                : "http://" + server + ":" + port + "/" + organization + "/XRMServices/2011/Organization.svc";

            IServiceManagement<IDiscoveryService> serviceManagement = ServiceConfigurationFactory.CreateManagement<IDiscoveryService>(new Uri(discoveryAddress));
            AuthenticationProviderType endpointType = serviceManagement.AuthenticationType;
            AuthenticationCredentials authCredentials = GetAuthCredentials(serviceManagement, endpointType, userName, password, domain);

            string organizationUri = string.Empty;
            using (var discoveryProxy = GetProxy<IDiscoveryService, DiscoveryServiceProxy>(serviceManagement, authCredentials))
            {
                if (discoveryProxy != null)
                {
                    var orgs = DiscoverOrganizations(discoveryProxy);
                    organizationUri = FindOrganization(organization, orgs.ToArray()).Endpoints[EndpointType.OrganizationService];
                }
            }

            if (string.IsNullOrWhiteSpace(organizationUri)) return null;

            IServiceManagement<IOrganizationService> orgServiceManagement = ServiceConfigurationFactory.CreateManagement<IOrganizationService>(new Uri(organizationUri));
            AuthenticationCredentials credentials = GetAuthCredentials(orgServiceManagement, endpointType, userName, password, domain);

            var orgProxy = GetProxy<IOrganizationService, OrganizationServiceProxy>(orgServiceManagement, credentials);
            orgProxy.EnableProxyTypes();
            orgProxy.Timeout = new System.TimeSpan(3, 0, 0);
            return orgProxy;
        }

        public OrganizationDetailCollection DiscoverOrganizations(IDiscoveryService service)
        {
            var orgRequest = new RetrieveOrganizationsRequest();
            var orgResponse = (RetrieveOrganizationsResponse)service.Execute(orgRequest);
            return orgResponse.Details;
        }

        public OrganizationDetail FindOrganization(string orgUniqueName, OrganizationDetail[] orgDetails)
        {
            foreach (var detail in orgDetails)
            {
                if (string.Compare(detail.UniqueName, orgUniqueName, System.StringComparison.InvariantCultureIgnoreCase) == 0)
                    return detail;
            }
            return null;
        }

        private AuthenticationCredentials GetAuthCredentials<TService>(IServiceManagement<TService> service, AuthenticationProviderType endpointType, string userName, string password, string domain)
        {
            var authCredentials = new AuthenticationCredentials();
            switch (endpointType)
            {
                case AuthenticationProviderType.ActiveDirectory:
                    authCredentials.ClientCredentials.Windows.ClientCredential = new NetworkCredential(userName, password, domain);
                    break;
                case AuthenticationProviderType.LiveId:
                case AuthenticationProviderType.OnlineFederation:
                case AuthenticationProviderType.Federation:
                default:
                    authCredentials.ClientCredentials.UserName.UserName = userName;
                    authCredentials.ClientCredentials.UserName.Password = password;
                    break;
            }
            return authCredentials;
        }

        private TProxy GetProxy<TService, TProxy>(IServiceManagement<TService> serviceManagement, AuthenticationCredentials authCredentials)
            where TService : class
            where TProxy : ServiceProxy<TService>
        {
            var classType = typeof(TProxy);
            if (serviceManagement.AuthenticationType != AuthenticationProviderType.ActiveDirectory)
            {
                AuthenticationCredentials tokenCredentials = serviceManagement.Authenticate(authCredentials);
                return (TProxy)classType
                    .GetConstructor(new System.Type[] { typeof(IServiceManagement<TService>), typeof(SecurityTokenResponse) })
                    .Invoke(new object[] { serviceManagement, tokenCredentials.SecurityTokenResponse });
            }
            return (TProxy)classType
                .GetConstructor(new System.Type[] { typeof(IServiceManagement<TService>), typeof(ClientCredentials) })
                .Invoke(new object[] { serviceManagement, authCredentials.ClientCredentials });
        }
    }
}

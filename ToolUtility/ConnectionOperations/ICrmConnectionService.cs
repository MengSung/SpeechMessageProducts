using System;
using System.ServiceModel.Description;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;

namespace ToolUtilityNameSpace.ConnectionOperations
{
    public interface ICrmConnectionService
    {
        ClientCredentials GetClientCredentials(string domain, string userName, string password);
        ClientCredentials GetClientCredentials();
        IOrganizationService GetOrganizationService(string server, string port, string organization, string domain, string userName, string password);
        IOrganizationService SetOrganizationService(string server, string port, string organization, string domain, string userName, string password);
        IOrganizationService SetClaimsBasedAuthenticationOrganizationService(string organization, string server, string domain, string userName, string password);
        OrganizationServiceProxy SetFederatedOrganizationProxy(string discoveryServiceType, string organization, string server, string port, string baseDiscoveryServiceAddress, string userName, string password, string domain);
        OrganizationDetailCollection DiscoverOrganizations(IDiscoveryService service);
        OrganizationDetail FindOrganization(string orgUniqueName, OrganizationDetail[] orgDetails);
    }
}

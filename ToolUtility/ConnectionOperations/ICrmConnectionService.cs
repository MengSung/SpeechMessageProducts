using System;
using System.ServiceModel.Description;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Discovery;

namespace ToolUtilityNameSpace.ConnectionOperations
{
    public interface ICrmConnectionService
    {
        // 基本認證方法
        ClientCredentials GetClientCredentials(string domain, string userName, string password);
        ClientCredentials GetClientCredentials();
        
        // CRM 2011 連線方法
        IOrganizationService GetOrganizationService(string server, string port, string organization, string domain, string userName, string password);
        IOrganizationService SetOrganizationService(string server, string port, string organization, string domain, string userName, string password);
        
        // Claims-Based 認證方法
        IOrganizationService SetClaimsBasedAuthenticationOrganizationService(string organization, string server, string domain, string userName, string password);
        
        // Federated 連線方法
        OrganizationServiceProxy SetFederatedOrganizationProxy(string discoveryServiceType, string organization, string server, string port, string baseDiscoveryServiceAddress, string userName, string password, string domain);
        
        // Discovery Service 方法
        OrganizationDetailCollection DiscoverOrganizations(IDiscoveryService service);
        OrganizationDetail FindOrganization(string orgUniqueName, OrganizationDetail[] orgDetails);
        
        // 現代連線方法
        IOrganizationService CreateOnPremiseClient(string url, string userName, string password);
        
        // 連線驗證方法
        bool ValidateConnection(IOrganizationService service);
        
        // 使用者資訊方法
        Entity GetCurrentUser(IOrganizationService service);
        Guid GetCurrentUserId(IOrganizationService service);
        Guid GetCurrentOrganizationId(IOrganizationService service);
    }
}

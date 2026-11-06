/*
 * 檔案整體說明
 *
 * 目的與定位：定義舊CRM連線操作的抽象契約，讓呼叫端透過相同介面取得所需服務；實際連線建立、隔離、故障淘汰及Dispose責任由實作與主機owner決定，介面本身不保存連線。
 *
 * 主要功能與流程：
 * 本檔宣告 ICrmConnectionService。
 * 資料／選項欄位包含 ICrmConnectionService，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility.Dataverse.Tests/BoundedClientPoolTests.cs、
 * ToolUtility.Dataverse.Tests/GatewayArchitectureTests.cs、ToolUtility.Dataverse.Tests/RunCServiceGraphTests.cs、
 * ToolUtility.Dataverse.Tests/StartupOrganizationServiceProfilingTests.cs、
 * ToolUtility.Dataverse.Tests/ToolUtilityClassScopedLifetimeTests.cs 追查目前工作樹中的合作／使用位置。
 */
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

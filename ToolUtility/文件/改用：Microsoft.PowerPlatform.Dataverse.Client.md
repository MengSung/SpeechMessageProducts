Microsoft.PowerPlatform.Dataverse.Client（ServiceClient）完全取代過時的 ServiceConfigurationFactory.CreateConfiguration<IOrganizationService> 舊 SDK。

✅ 1. 安裝 NuGet
Install-Package Microsoft.PowerPlatform.Dataverse.Client

✅ 2. 使用 ServiceClient（取代 OrganizationServiceProxy）
✔ Client Secret（最常用）
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

string connectionString = @"
AuthType=ClientSecret;
Url=https://yourorg.crm.dynamics.com;
ClientId=YOUR-CLIENT-ID;
ClientSecret=YOUR-SECRET;
TenantId=YOUR-TENANT-ID;
";

using var svc = new ServiceClient(connectionString);

// 呼叫範例
var response = svc.Execute(new Microsoft.Crm.Sdk.Messages.WhoAmIRequest());
Console.WriteLine($"UserId: {response.UserId}");

✅ 3. 使用 OAuth (Interactive Browser Login)

適合 MFA / 個人帳號登入

string connectionString = @"
AuthType=OAuth;
Url=https://yourorg.crm.dynamics.com;
LoginPrompt=Auto;
ClientId=YOUR-CLIENT-ID;
RedirectUri=http://localhost;
";

using var svc = new ServiceClient(connectionString);

✅ 4. 使用 Azure Managed Identity（最現代）

如果程式跑在：

Azure Function

App Service

VM

可以使用 Managed Identity：

string connectionString = @"
AuthType=MSI;
Url=https://yourorg.crm.dynamics.com;
";

using var svc = new ServiceClient(connectionString);

✅ 5. 使用 ServiceClient 取代舊式組態初始化
🟥 舊式（不要再用）
var serviceConfiguration =
    ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(new Uri(url));

var orgProxy = new OrganizationServiceProxy(serviceConfiguration, credentials);

🟩 新式（唯一建議用法）
using var svc = new ServiceClient(connectionString);


ServiceClient 自動處理：

OAuth Token

重試機制

Discovery

Web API & SOAP API 混用能力（自動偵測最佳 API）

支援 .NET 6 / 7 / 8

✅ 6. 你如果要「完全等價」於 IOrganizationService 的使用方式

ServiceClient 本身已經實作 IOrganizationService，所以舊 code 幾乎不用改：

IOrganizationService crm = svc;

// 仍然可以用 Create、Retrieve、Update
var account = new Entity("account");
account["name"] = "Test";
var id = crm.Create(account);

ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>
是 Dynamics CRM / Dataverse (舊稱: CRM SDK) 中早期使用的 API，用來建立連線組態 (IServiceConfiguration<T>) 的工廠方法。

不過 它已經被標記為過時（從 CRM 2016、Dynamics 365 以後），新式程式碼不再使用它。

✔ 這段程式做什麼？

它原本用於：

透過連線的 URL（例如 https://org.crm.dynamics.com/XRMServices/2011/Organization.svc）

建立 IServiceConfiguration<IOrganizationService>

再從這個設定產生 OrganizationServiceProxy 或 IOrganizationService

✔ 舊式範例（已過時 API）
var serviceConfiguration =
    ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(
        new Uri("https://yourorg.crm.dynamics.com/XRMServices/2011/Organization.svc"));

var credentials = new ClientCredentials();
credentials.UserName.UserName = "xxx@xxx.onmicrosoft.com";
credentials.UserName.Password = "password";

using (var orgService = new OrganizationServiceProxy(serviceConfiguration, credentials))
{
    orgService.EnableProxyTypes();
    // orgService.DoSomething...
}

❗ 為什麼不建議再用？

因為 Dynamics 365 已經全面改用 CRM Web API / Dataverse Service Client。

舊 API 有以下問題：

僅支援 SOAP

需要 OrganizationServiceProxy（已棄用）

不支援 MFA / OAuth2 互動式登入

不支援 Power Platform 新功能

✔ 建議改用：Microsoft.PowerPlatform.Dataverse.Client

（NuGet：Microsoft.PowerPlatform.Dataverse.Client）

新版寫法：

using Microsoft.PowerPlatform.Dataverse.Client;

var connectionString =
    "AuthType=OAuth;" +
    "Url=https://yourorg.crm.dynamics.com;" +
    "ClientId=xxxxxx;" +
    "ClientSecret=yyyyyy;" +
    "TenantId=zzzzzzzz;";

using var serviceClient = new ServiceClient(connectionString);

// 使用 Web API 方式呼叫
var who = serviceClient.Execute(new WhoAmIRequest());
Console.WriteLine(who.UserId);

✔ 如果你正在維護舊專案

你可能會看到：

IOrganizationService

ServiceConfigurationFactory

OrganizationServiceProxy

如果目標是 遷移到 .NET 6+ 或 支援 OAuth2 / Client Secret / Managed Identity，我可以協助轉換成新版 ServiceClient 寫法。

如果你願意，我可以提供：

✅ 舊程式碼 → 新 SDK 的對照表
✅ 你目前的連線程式碼改寫成新版
✅ 完整可運作的 .NET 6/7/8 範例


在 Microsoft.PowerPlatform.Dataverse.Client (Dataverse SDK Core) 中，不再使用舊版 WCF 的 ServiceConfigurationFactory 來手動建立設定。

新的 SDK 設計將連線邏輯封裝在 ServiceClient 類別中。此類別直接實作了 IOrganizationService 介面，因此您可以直接實作並替換。

以下是具體的移轉方式與程式碼範例。

1. 安裝 NuGet 套件
首先，請確保您已移除舊的 SDK 參考（如 Microsoft.CrmSdk.CoreAssemblies），並安裝新的套件：

Package: Microsoft.PowerPlatform.Dataverse.Client

2. 程式碼改寫對照
❌ 舊寫法 (Legacy WCF Style)
以前您可能需要建立 Configuration 再產生 Proxy：

C#

// 舊版 SDK 寫法，現已過時
var config = ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(serviceUri);
// ... 接著建立 OrganizationServiceProxy ...
✅ 新寫法 (Dataverse Client)
現在您只需實例化 ServiceClient。它會自動處理設定、OAuth 驗證與重試邏輯。

方式 A：使用連線字串 (最推薦) 這是最簡單且最現代化的方式。

C#

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk; // IOrganizationService 仍在或是相容於此命名空間

// 設定連線字串 (支援 OAuth, Secret, Certificate 等)
string connectionString = "AuthType=ClientSecret;Url=https://yourorg.crm.dynamics.com;ClientId={AppId};ClientSecret={Secret}";

// 建立 ServiceClient (它實作了 IOrganizationService)
IOrganizationService service = new ServiceClient(connectionString);

// 檢查是否連線成功
if (((ServiceClient)service).IsReady)
{
    // 您的商業邏輯
    var userId = ((ServiceClient)service).GetMyUserId();
}
else
{
    // 處理連線錯誤
    throw new Exception(((ServiceClient)service).LastError);
}
方式 B：使用 URI 和 TokenProvider (適用於 Azure Function 或特殊驗證) 如果您已經有 Token 或者需要更細緻的設定：

C#

using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;

var serviceUri = new Uri("https://yourorg.crm.dynamics.com");

// 範例：使用外部函式取得 Token
Func<string, Task<string>> tokenProvider = async (authority) => {
    return await GetMyAccessTokenAsync(); // 您的自訂 Token 邏輯
};

IOrganizationService service = new ServiceClient(serviceUri, tokenProvider);
3. 重要差異與注意事項
通訊協定 (Protocol):

ServiceClient 預設優先使用 Web API (REST)，如果失敗或不支援才會退回到 SOAP (WCF)。舊版 ServiceConfigurationFactory 純粹是基於 WCF。這意味著新版效能通常更好且防火牆友善。

生命週期管理:

ServiceClient 實作了 IDisposable。建議使用 using 區塊或依賴注入 (Dependency Injection) 來管理其生命週期。

在舊版中，開發者常需手動管理 OrganizationServiceProxy 的 EnableProxyTypes。在 ServiceClient 中，這通常是自動處理的。

IOrganizationService 相容性:

您現有的商業邏輯（例如 service.Create(...), service.Retrieve(...)）不需要修改，因為 ServiceClient 完美支援 IOrganizationService 介面。

總結
您不再需要「建立設定」，而是直接「建立客戶端」。

原本的: ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(...)

改為: new ServiceClient(connectionString)

在新的 Microsoft.PowerPlatform.Dataverse.Client（即新的 Dataverse ServiceClient）中，已經完全取代了舊的 Xrm Tooling 和 ServiceConfigurationFactory.CreateConfiguration<IOrganizationService> 的寫法。
以下是從舊寫法遷移到新 ServiceClient 的正確做法（支援 .NET Framework / .NET Core / .NET 5+ 都適用）：
推薦寫法（2024~2025 最新官方方式）
C#using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Identity.Client; // 需要這個來建 IConfidentialClientApplication (應用程式註冊方式)

// 1. 使用連線字串（最簡單、最推薦）
string connectionString = 
    "AuthType=ClientSecret;" +
    "Url=https://yourorg.crm.dynamics.com;" +
    "ClientId=your-app-id;" +
    "ClientSecret=your-client-secret;";

using var serviceClient = new ServiceClient(connectionString);

// 檢查是否連線成功
if (!serviceClient.IsReady)
{
    throw new Exception($"連線失敗: {serviceClient.LastError}");
}

var whoAmI = serviceClient.Execute(new WhoAmIRequest()) as WhoAmIResponse;
Console.WriteLine($"已連線，使用者: {whoAmI.UserId}");
其他常見 AuthType 範例






























需求AuthType連線字串範例應用程式使用者 (App Registration)ClientSecret上面的範例OAuth (互動式登入，適合工具)OAuthAuthType=OAuth; Url=https://yourorg.crm.dynamics.com; ClientId=51f81489-12ee-4a9e-aaae-a2591f45987d; RedirectUri=http://localhost; LoginPrompt=Auto;目前 Windows 使用者 (AD)Office365AuthType=Office365; Url=https://yourorg.crm.dynamics.com; UserName=user@contoso.com; Password=pass;證書驗證CertificateAuthType=Certificate; ... CertificateThumbprint=...
完全取代舊的寫法對照表





















舊寫法 (已過時)新寫法 (ServiceClient)CrmServiceClient client = new CrmServiceClient(connectionString);→ 改用 ServiceClient (兩者目前還能共存，但官方已標記 CrmServiceClient 為 Deprecated)ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(serviceUri);→ 完全不需要，直接用 new ServiceClient(connectionString)client.OrganizationServiceProxy 或 client.OrganizationWebProxyClient→ 直接使用 serviceClient 即可，它已實作 IOrganizationService
NuGet 套件（務必安裝最新版）
XML<PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.3.*" />
<!-- 如果用 ClientSecret 或 Certificate 還需要 -->
<PackageReference Include="Microsoft.Identity.Client" Version="4.*" />
結論
不要再用：
C#ServiceConfigurationFactory.CreateConfiguration<IOrganizationService>(uri);
一律改用：
C#var client = new ServiceClient(connectionString);  // 或其他建構子
// client 本身就是 IOrganizationService，直接拿來 Execute 就好
這樣不僅程式碼更簡潔、效能更好，也完全支援 Dataverse、Dynamics 365 CE、Power Apps 的最新功能與安全性機制。

https://learn.microsoft.com/en-us/dotnet/api/microsoft.powerplatform.dataverse.client?view=dataverse-sdk-latest
https://learn.microsoft.com/en-us/dotnet/api/microsoft.powerplatform.dataverse.client.serviceclient?view=dataverse-sdk-latest
https://learn.microsoft.com/en-us/dotnet/api/microsoft.crm.sdk?view=dataverse-sdk-latest
https://learn.microsoft.com/en-us/dotnet/api/microsoft.crm.sdk.messages?view=dataverse-sdk-latest
https://learn.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/use-service-client-sdk



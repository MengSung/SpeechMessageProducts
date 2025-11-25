# CreateOnPremiseClient 使用指南

## 快速開始

### 基本用法

```csharp
using ToolUtilityNameSpace.ConnectionOperations;
using Microsoft.Xrm.Sdk;

// 建立連線服務
var connectionService = new CrmConnectionService();

// 建立 OnPremise 連線
IOrganizationService service = connectionService.CreateOnPremiseClient(
    url: "https://org.crm.contoso.com/XRMServices/2011/Organization.svc",
    userName: "DOMAIN\\username",  // 或 "username@domain.com"
    password: "yourPassword"
);

// 使用服務
var account = new Entity("account");
account["name"] = "測試帳戶";
Guid accountId = service.Create(account);
```

## URL 格式

### 完整 URL（推薦）
```csharp
string url = "https://org.crm.contoso.com/XRMServices/2011/Organization.svc";
```

### 組織 URL + 路徑
```csharp
string orgUrl = "https://org.crm.contoso.com";
string fullUrl = $"{orgUrl}/XRMServices/2011/Organization.svc";
```

## 使用者名稱格式

### 格式 1: 網域\使用者名稱
```csharp
string userName = "CONTOSO\\john.doe";
```

### 格式 2: UPN 格式
```csharp
string userName = "john.doe@contoso.com";
```

## 支援的驗證類型

OnPremiseClient 會自動偵測並使用適當的驗證方式：

### 1. Active Directory (AD) 驗證
```csharp
// AD 驗證範例
var service = connectionService.CreateOnPremiseClient(
    url: "https://org.crm.contoso.com/XRMServices/2011/Organization.svc",
    userName: "CONTOSO\\admin",
    password: "P@ssw0rd"
);
```

### 2. Claims-Based 驗證
```csharp
// Claims-Based 驗證範例（IFD）
var service = connectionService.CreateOnPremiseClient(
    url: "https://org.crm.contoso.com/XRMServices/2011/Organization.svc",
    userName: "admin@contoso.com",
    password: "P@ssw0rd"
);
```

### 3. Internet-Facing Deployment (IFD)
```csharp
// IFD 環境連線
var service = connectionService.CreateOnPremiseClient(
    url: "https://crm.contoso.com/XRMServices/2011/Organization.svc",
    userName: "user@contoso.com",
    password: "P@ssw0rd"
);
```

## 錯誤處理

### 基本錯誤處理
```csharp
try
{
    var service = connectionService.CreateOnPremiseClient(url, userName, password);
    Console.WriteLine("連線成功！");
}
catch (ArgumentNullException ex)
{
    Console.WriteLine($"參數錯誤: {ex.Message}");
}
catch (ArgumentException ex)
{
    Console.WriteLine($"URL 格式錯誤: {ex.Message}");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"連線失敗: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"未預期的錯誤: {ex.Message}");
}
```

### 詳細錯誤處理
```csharp
try
{
    var service = connectionService.CreateOnPremiseClient(url, userName, password);
    
    // 驗證連線
    if (!connectionService.ValidateConnection(service))
    {
        throw new Exception("連線驗證失敗");
    }
    
    // 取得使用者資訊
    var userId = connectionService.GetCurrentUserId(service);
    Console.WriteLine($"成功連線，使用者 ID: {userId}");
}
catch (Exception ex)
{
    Console.WriteLine($"錯誤: {ex.Message}");
    Console.WriteLine($"堆疊追蹤: {ex.StackTrace}");
    
    if (ex.InnerException != null)
    {
        Console.WriteLine($"內部錯誤: {ex.InnerException.Message}");
    }
}
```

## 連線驗證

### 驗證連線是否有效
```csharp
var service = connectionService.CreateOnPremiseClient(url, userName, password);

if (connectionService.ValidateConnection(service))
{
    Console.WriteLine("連線有效");
}
else
{
    Console.WriteLine("連線無效");
}
```

### 取得當前使用者資訊
```csharp
try
{
    // 取得使用者 ID
    Guid userId = connectionService.GetCurrentUserId(service);
    Console.WriteLine($"使用者 ID: {userId}");
    
    // 取得完整使用者資訊
    Entity user = connectionService.GetCurrentUser(service);
    string fullName = user.GetAttributeValue<string>("fullname");
    Console.WriteLine($"使用者姓名: {fullName}");
    
    // 取得組織 ID
    Guid orgId = connectionService.GetCurrentOrganizationId(service);
    Console.WriteLine($"組織 ID: {orgId}");
}
catch (Exception ex)
{
    Console.WriteLine($"取得使用者資訊失敗: {ex.Message}");
}
```

## 實用範例

### 範例 1: 簡單的 CRUD 操作
```csharp
var service = connectionService.CreateOnPremiseClient(url, userName, password);

// Create
var account = new Entity("account");
account["name"] = "Contoso Ltd";
account["telephone1"] = "555-1234";
Guid accountId = service.Create(account);

// Retrieve
var retrievedAccount = service.Retrieve("account", accountId, new ColumnSet("name", "telephone1"));
Console.WriteLine($"帳戶名稱: {retrievedAccount["name"]}");

// Update
retrievedAccount["telephone1"] = "555-5678";
service.Update(retrievedAccount);

// Delete
service.Delete("account", accountId);
```

### 範例 2: 查詢資料
```csharp
using Microsoft.Xrm.Sdk.Query;

var service = connectionService.CreateOnPremiseClient(url, userName, password);

// 使用 QueryExpression
var query = new QueryExpression("account")
{
    ColumnSet = new ColumnSet("name", "telephone1"),
    Criteria = new FilterExpression
    {
        Conditions =
        {
            new ConditionExpression("name", ConditionOperator.Equal, "Contoso")
        }
    }
};

EntityCollection results = service.RetrieveMultiple(query);

foreach (var entity in results.Entities)
{
    Console.WriteLine($"名稱: {entity["name"]}");
}
```

### 範例 3: 執行訊息請求
```csharp
using Microsoft.Crm.Sdk.Messages;

var service = connectionService.CreateOnPremiseClient(url, userName, password);

// WhoAmI 請求
var whoAmIRequest = new WhoAmIRequest();
var whoAmIResponse = (WhoAmIResponse)service.Execute(whoAmIRequest);
Console.WriteLine($"使用者 ID: {whoAmIResponse.UserId}");
Console.WriteLine($"業務單位 ID: {whoAmIResponse.BusinessUnitId}");
Console.WriteLine($"組織 ID: {whoAmIResponse.OrganizationId}");
```

### 範例 4: 從設定檔讀取連線資訊
```csharp
using System.Configuration;

// App.config 或 Web.config
// <appSettings>
//   <add key="CrmUrl" value="https://org.crm.contoso.com/XRMServices/2011/Organization.svc" />
//   <add key="CrmUserName" value="CONTOSO\admin" />
//   <add key="CrmPassword" value="P@ssw0rd" />
// </appSettings>

string url = ConfigurationManager.AppSettings["CrmUrl"];
string userName = ConfigurationManager.AppSettings["CrmUserName"];
string password = ConfigurationManager.AppSettings["CrmPassword"];

var service = connectionService.CreateOnPremiseClient(url, userName, password);
```

### 範例 5: 在 ASP.NET 應用程式中使用
```csharp
public class CrmController : Controller
{
    private readonly ICrmConnectionService _connectionService;
    
    public CrmController()
    {
        _connectionService = new CrmConnectionService();
    }
    
    public ActionResult GetAccounts()
    {
        try
        {
            var service = _connectionService.CreateOnPremiseClient(
                ConfigurationManager.AppSettings["CrmUrl"],
                ConfigurationManager.AppSettings["CrmUserName"],
                ConfigurationManager.AppSettings["CrmPassword"]
            );
            
            var query = new QueryExpression("account")
            {
                ColumnSet = new ColumnSet("name", "telephone1"),
                TopCount = 10
            };
            
            var results = service.RetrieveMultiple(query);
            
            return View(results.Entities);
        }
        catch (Exception ex)
        {
            ViewBag.Error = ex.Message;
            return View("Error");
        }
    }
}
```

### 範例 6: 依賴注入模式
```csharp
// 在 Startup.cs 或 Program.cs
public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<ICrmConnectionService, CrmConnectionService>();
    
    // 註冊 IOrganizationService
    services.AddScoped<IOrganizationService>(provider =>
    {
        var connectionService = provider.GetRequiredService<ICrmConnectionService>();
        var configuration = provider.GetRequiredService<IConfiguration>();
        
        return connectionService.CreateOnPremiseClient(
            configuration["CrmUrl"],
            configuration["CrmUserName"],
            configuration["CrmPassword"]
        );
    });
}

// 在控制器中使用
public class AccountController : Controller
{
    private readonly IOrganizationService _orgService;
    
    public AccountController(IOrganizationService orgService)
    {
        _orgService = orgService;
    }
    
    public IActionResult Index()
    {
        var accounts = _orgService.RetrieveMultiple(
            new QueryExpression("account") 
            { 
                ColumnSet = new ColumnSet(true),
                TopCount = 100
            }
        );
        
        return View(accounts.Entities);
    }
}
```

## 常見問題

### Q1: URL 格式錯誤怎麼辦？
**A:** 確保 URL 包含完整路徑：
```
正確: https://org.crm.contoso.com/XRMServices/2011/Organization.svc
錯誤: https://org.crm.contoso.com
錯誤: http://org.crm.contoso.com/XRMServices/2011/Organization.svc (必須用 HTTPS)
```

### Q2: 連線逾時怎麼處理？
**A:** 可以設定 Timeout 屬性：
```csharp
var service = connectionService.CreateOnPremiseClient(url, userName, password);

// OnPremiseClient 支援設定 Timeout
if (service is OnPremiseClient client)
{
    client.Timeout = TimeSpan.FromMinutes(5);
}
```

### Q3: 如何在多執行緒環境中使用？
**A:** IOrganizationService 是執行緒安全的，但建議為每個執行緒建立獨立的實例：
```csharp
Parallel.ForEach(items, item =>
{
    var service = connectionService.CreateOnPremiseClient(url, userName, password);
    // 執行操作
});
```

### Q4: 如何測試連線？
**A:** 使用 ValidateConnection 方法：
```csharp
var service = connectionService.CreateOnPremiseClient(url, userName, password);

if (connectionService.ValidateConnection(service))
{
    // 連線正常，可以進行操作
}
```

### Q5: 支援哪些 Dynamics 365 版本？
**A:** 支援：
- Dynamics CRM 2011 或更新版本
- Dynamics 365 On-Premise
- Dynamics 365 IFD

## 效能優化建議

### 1. 重用連線
```csharp
// 不好的做法 - 每次操作都建立新連線
for (int i = 0; i < 100; i++)
{
    var service = connectionService.CreateOnPremiseClient(url, userName, password);
    // 執行操作
}

// 好的做法 - 重用連線
var service = connectionService.CreateOnPremiseClient(url, userName, password);
for (int i = 0; i < 100; i++)
{
    // 執行操作
}
```

### 2. 使用 ColumnSet 指定欄位
```csharp
// 不好 - 取得所有欄位
var entity = service.Retrieve("account", id, new ColumnSet(true));

// 好 - 只取得需要的欄位
var entity = service.Retrieve("account", id, new ColumnSet("name", "telephone1"));
```

### 3. 批次操作
```csharp
using Microsoft.Xrm.Sdk.Messages;

// 使用 ExecuteMultiple 進行批次操作
var requestCollection = new OrganizationRequestCollection();

for (int i = 0; i < 100; i++)
{
    var createRequest = new CreateRequest { Target = new Entity("account") };
    requestCollection.Add(createRequest);
}

var multipleRequest = new ExecuteMultipleRequest
{
    Requests = requestCollection,
    Settings = new ExecuteMultipleSettings
    {
        ContinueOnError = true,
        ReturnResponses = true
    }
};

var response = (ExecuteMultipleResponse)service.Execute(multipleRequest);
```

## 安全性建議

### 1. 不要硬編碼密碼
```csharp
// 不好
var service = connectionService.CreateOnPremiseClient(url, "admin", "Password123");

// 好 - 從設定檔讀取
var password = ConfigurationManager.AppSettings["CrmPassword"];

// 更好 - 使用 Azure Key Vault 或安全的設定提供者
var password = await keyVaultClient.GetSecretAsync("CrmPassword");
```

### 2. 使用最小權限原則
確保連線使用的帳戶只有必要的權限。

### 3. 加密連線字串
在 Web.config 中加密敏感資訊：
```
aspnet_regiis -pef "connectionStrings" . -prov "RSAProtectedConfigurationProvider"
```

## 疑難排解

### 問題: "找不到類型或命名空間名稱 'OnPremiseClient'"
**解決方案:** 確保已加入 PowerPlatform.Dataverse.Client 專案參考

### 問題: "URL 必須使用 HTTPS 協定"
**解決方案:** 將 URL 從 http:// 改為 https://

### 問題: "連線驗證失敗：無法取得有效的使用者 ID"
**解決方案:** 
1. 檢查使用者名稱和密碼是否正確
2. 確認使用者有權限存取該組織
3. 檢查網路連線

### 問題: "URL 必須包含完整的組織服務路徑"
**解決方案:** 確保 URL 包含 `/XRMServices/2011/Organization.svc`

## 總結

使用重構後的 `CreateOnPremiseClient` 方法：

? 遵循 SOLID 原則
? 自動驗證參數和連線
? 支援多種驗證方式
? 提供清晰的錯誤訊息
? 易於測試和維護

有任何問題，請參考完整的重構報告或查看程式碼註解。

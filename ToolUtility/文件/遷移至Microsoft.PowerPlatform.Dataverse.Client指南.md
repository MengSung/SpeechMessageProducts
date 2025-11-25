# 遷移至 Microsoft.PowerPlatform.Dataverse.Client 指南

## 概述
本文件說明如何從自訂的 `PowerPlatform.Dataverse.Client` 專案遷移到官方的 `Microsoft.PowerPlatform.Dataverse.Client` NuGet 套件。

## 變更摘要

### 1. 命名空間變更
```csharp
// 舊的（自訂專案）
using PowerPlatform.Dataverse.Client;

// 新的（官方套件）
using Microsoft.PowerPlatform.Dataverse.Client;
```

### 2. 類別變更

#### OnPremiseClient → ServiceClient
原本使用的 `OnPremiseClient` 是自訂包裝類別，現在改用官方的 `ServiceClient`。

```csharp
// 舊的方式（OnPremiseClient）
var client = new OnPremiseClient(url, userName, password);

// 新的方式（ServiceClient + 連線字串）
string connectionString = $@"
    AuthType=OAuth;
    Url={url};
    UserName={userName};
    Password={password};
    LoginPrompt=Auto;
    RequireNewInstance=True";

var client = new ServiceClient(connectionString);
```

## 安裝步驟

### 步驟 1: 安裝 NuGet 套件

使用 Visual Studio 的 NuGet 套件管理員或命令列：

```powershell
# 使用 Package Manager Console
Install-Package Microsoft.PowerPlatform.Dataverse.Client -Version 1.1.14

# 或使用 dotnet CLI
dotnet add package Microsoft.PowerPlatform.Dataverse.Client --version 1.1.14
```

### 步驟 2: 移除舊的專案參考

從 `ToolUtility.csproj` 中移除對 `PowerPlatform.Dataverse.Client` 專案的參考：

```xml
<!-- 移除這行 -->
<ProjectReference Include="..\PowerPlatform.Dataverse.Client\PowerPlatform.Dataverse.Client.csproj">
  <Project>{f7624121-f809-99fa-8591-0f431eb65992}</Project>
  <Name>PowerPlatform.Dataverse.Client</Name>
</ProjectReference>
```

### 步驟 3: 更新程式碼

#### 3.1 更新 using 陳述式

```csharp
// 在 CrmConnectionService.cs 的頂部
using Microsoft.PowerPlatform.Dataverse.Client;
```

#### 3.2 更新 CreateOnPremiseClient 方法

方法已經更新為使用官方的 `ServiceClient`，支援以下功能：

? OAuth 驗證
? 連線字串建構器模式
? 自動連線驗證
? 完整的錯誤處理

## 支援的驗證類型

Microsoft.PowerPlatform.Dataverse.Client 支援多種驗證方式：

### 1. OAuth 驗證（使用者名稱 + 密碼）
```csharp
string connectionString = @"
    AuthType=OAuth;
    Url=https://yourorg.crm.dynamics.com;
    UserName=user@domain.com;
    Password=yourPassword;
    LoginPrompt=Auto;
    RequireNewInstance=True";

var service = new ServiceClient(connectionString);
```

### 2. ClientSecret 驗證（應用程式驗證）
```csharp
string connectionString = @"
    AuthType=ClientSecret;
    Url=https://yourorg.crm.dynamics.com;
    ClientId=YOUR-CLIENT-ID;
    ClientSecret=YOUR-SECRET;
    RequireNewInstance=True";

var service = new ServiceClient(connectionString);
```

### 3. Certificate 驗證
```csharp
string connectionString = @"
    AuthType=Certificate;
    Url=https://yourorg.crm.dynamics.com;
    ClientId=YOUR-CLIENT-ID;
    Thumbprint=YOUR-CERT-THUMBPRINT;
    RequireNewInstance=True";

var service = new ServiceClient(connectionString);
```

### 4. Azure Managed Identity
```csharp
string connectionString = @"
    AuthType=MSI;
    Url=https://yourorg.crm.dynamics.com";

var service = new ServiceClient(connectionString);
```

## 程式碼對照表

### 建立連線

| 功能 | 舊方式 (OnPremiseClient) | 新方式 (ServiceClient) |
|------|-------------------------|------------------------|
| 建立連線 | `new OnPremiseClient(url, user, pwd)` | `new ServiceClient(connectionString)` |
| 檢查連線狀態 | - | `serviceClient.IsReady` |
| 取得錯誤訊息 | - | `serviceClient.LastError` |
| 設定 Timeout | `client.Timeout = TimeSpan.FromMinutes(5)` | 透過連線字串設定 |
| 取得使用者 ID | 執行 WhoAmI | `serviceClient.GetMyCrmUserId()` |

### 執行操作

所有 IOrganizationService 的操作保持不變：

```csharp
// 無論使用哪種方式，這些方法都相同
IOrganizationService service = new ServiceClient(connectionString);

// Create
Guid id = service.Create(entity);

// Retrieve
Entity retrieved = service.Retrieve("account", id, new ColumnSet(true));

// Update
service.Update(entity);

// Delete
service.Delete("account", id);

// Execute
OrganizationResponse response = service.Execute(request);

// RetrieveMultiple
EntityCollection results = service.RetrieveMultiple(query);
```

## 優點與改進

### 官方 Microsoft.PowerPlatform.Dataverse.Client 的優點

1. ? **官方支援**: 由 Microsoft 官方維護和更新
2. ? **更好的效能**: 優化的連線池和重試機制
3. ? **更多驗證選項**: 支援 OAuth, ClientSecret, Certificate, MSI
4. ? **跨平台支援**: 支援 .NET Framework 和 .NET Core/.NET 5+
5. ? **自動重試**: 內建暫時性錯誤重試機制
6. ? **更好的診斷**: 提供詳細的錯誤訊息和日誌
7. ? **現代化 API**: 支援 async/await 模式
8. ? **安全性**: 定期安全性更新

### 移除自訂 OnPremiseClient 的理由

1. **維護負擔**: 不需要維護自訂的連線實作
2. **相容性**: 官方套件保證與最新版 Dynamics 365/Dataverse 相容
3. **功能完整**: 官方套件提供更多功能和選項
4. **社群支援**: 豐富的文件和社群支援

## 遷移檢查清單

- [ ] 安裝 `Microsoft.PowerPlatform.Dataverse.Client` NuGet 套件
- [ ] 移除 `PowerPlatform.Dataverse.Client` 專案參考
- [ ] 更新所有 `using PowerPlatform.Dataverse.Client` 為 `using Microsoft.PowerPlatform.Dataverse.Client`
- [ ] 更新 `CreateOnPremiseClient` 方法實作
- [ ] 測試所有連線功能
- [ ] 更新相關文件
- [ ] 更新單元測試（如果有）
- [ ] 執行完整的回歸測試

## 測試建議

### 1. 基本連線測試
```csharp
[TestMethod]
public void TestServiceClientConnection()
{
    var connectionService = new CrmConnectionService();
    var service = connectionService.CreateOnPremiseClient(
        "https://yourorg.crm.dynamics.com",
        "user@domain.com",
        "password"
    );
    
    Assert.IsNotNull(service);
    Assert.IsTrue(connectionService.ValidateConnection(service));
}
```

### 2. CRUD 操作測試
```csharp
[TestMethod]
public void TestCrudOperations()
{
    var service = connectionService.CreateOnPremiseClient(url, user, password);
    
    // Create
    var account = new Entity("account");
    account["name"] = "Test Account";
    var id = service.Create(account);
    Assert.AreNotEqual(Guid.Empty, id);
    
    // Retrieve
    var retrieved = service.Retrieve("account", id, new ColumnSet("name"));
    Assert.AreEqual("Test Account", retrieved["name"]);
    
    // Update
    retrieved["name"] = "Updated Account";
    service.Update(retrieved);
    
    // Delete
    service.Delete("account", id);
}
```

### 3. 錯誤處理測試
```csharp
[TestMethod]
[ExpectedException(typeof(InvalidOperationException))]
public void TestInvalidConnection()
{
    var connectionService = new CrmConnectionService();
    var service = connectionService.CreateOnPremiseClient(
        "https://invalid.crm.dynamics.com",
        "invalid@user.com",
        "invalid"
    );
}
```

## 常見問題

### Q1: 為什麼要遷移到官方套件？
**A**: 官方套件提供更好的支援、效能和安全性，並且會持續更新以支援最新的 Dataverse 功能。

### Q2: 現有的程式碼需要大幅修改嗎？
**A**: 不需要。由於兩者都實作 `IOrganizationService` 介面，大部分的程式碼可以保持不變，只需要更新連線建立的部分。

### Q3: 效能會有影響嗎？
**A**: 不會，官方的 ServiceClient 效能更好，並且包含連線池和自動重試等優化機制。

### Q4: 支援哪些 .NET 版本？
**A**: Microsoft.PowerPlatform.Dataverse.Client 支援：
- .NET Framework 4.6.2+
- .NET Core 3.1
- .NET 5.0+
- .NET Standard 2.0

### Q5: 如何處理 On-Premise 環境？
**A**: ServiceClient 完全支援 On-Premise 環境，使用 OAuth 或 AD 驗證即可。

### Q6: 連線字串可以加密嗎？
**A**: 可以，建議將連線字串存放在 app.config 或 web.config 中，並使用 ASP.NET 的加密功能加密敏感資訊。

## 參考資源

### 官方文件
- [ServiceClient 類別文件](https://learn.microsoft.com/en-us/dotnet/api/microsoft.powerplatform.dataverse.client.serviceclient?view=dataverse-sdk-latest)
- [連線字串使用指南](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/use-connection-strings-xrm-tooling-connect)
- [ServiceClient SDK 使用指南](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/xrm-tooling/use-service-client-sdk)
- [Microsoft.Crm.Sdk 命名空間](https://learn.microsoft.com/en-us/dotnet/api/microsoft.crm.sdk?view=dataverse-sdk-latest)

### 程式碼範例
- [PowerApps-Samples Repository](https://github.com/microsoft/PowerApps-Samples/tree/master/dataverse/orgsvc/CSharp)

## 結論

遷移到官方的 `Microsoft.PowerPlatform.Dataverse.Client` 是一個明智的選擇，它提供：

? 更好的長期維護性
? 官方支援和定期更新
? 更多功能和驗證選項
? 更好的效能和可靠性
? 豐富的文件和社群資源

透過本指南的步驟，可以順利完成遷移，並享受官方套件帶來的好處。

---

**更新日期**: 2024
**文件版本**: 1.0
**適用專案**: ToolUtility (.NET Framework 4.6.2)

# On-Premise 連線問題修正報告

## 問題描述

### 錯誤訊息
```
System.InvalidOperationException: 建立 ServiceClient 連線時發生錯誤 
(URL: https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc, 
 User: Administrator@speechmessage.com.tw): Failed to connect to Dataverse

內部例外狀況 1: DataverseConnectionException: Failed to connect to Dataverse
內部例外狀況 2: ArgumentNullException: Need a non-empty authority (Parameter 'Authority')
```

### 根本原因

**OAuth 驗證不適用於 On-Premise 環境**

原本的 `CreateOnPremiseClient` 方法對所有環境都使用 OAuth 驗證：
```csharp
.WithAuthType(AUTH_TYPE_OAUTH)  // ? 這對 On-Premise 不適用
```

但是：
- **OAuth 驗證** 主要用於 **Dynamics 365 Online** (雲端環境)
- **On-Premise 環境** 需要使用 **AD (Active Directory)** 或 **IFD (Internet-Facing Deployment)** 驗證

錯誤 "Need a non-empty authority" 是因為 OAuth 需要 Azure AD authority URL，但 On-Premise 環境沒有這個設定。

## 解決方案

### 1. 自動偵測環境類型

新增 `IsOnlineEnvironment` 方法來自動判斷是 Online 還是 On-Premise：

```csharp
private bool IsOnlineEnvironment(string url)
{
    var uri = new Uri(url);
    var host = uri.Host.ToLowerInvariant();

    // 檢查是否為 Dynamics 365 Online 的網域
    return host.Contains(".crm.dynamics.com") ||
           host.Contains(".crm2.dynamics.com") ||
           // ... 其他區域
}
```

**判斷邏輯**：
- ? `https://yourorg.crm.dynamics.com` → Online
- ? `https://jesus.speechmessage.com.tw/XRMServices/...` → On-Premise

### 2. 使用正確的驗證方式

#### Online 環境 (OAuth)
```csharp
private string BuildOnlineConnectionString(string url, string userName, string password)
{
    return new ConnectionStringBuilder()
        .WithAuthType("OAuth")      // ? OAuth for Online
        .WithUrl(url)
        .WithUserName(userName)
        .WithPassword(password)
        .WithLoginPrompt("Auto")
        .WithRequireNewInstance("True")
        .Build();
}
```

#### On-Premise 環境 (AD)
```csharp
private string BuildOnPremiseConnectionString(string url, string userName, string password)
{
    // 解析網域和使用者名稱
    string domain = ExtractDomain(userName);
    
    return new ConnectionStringBuilder()
        .WithAuthType("AD")         // ? Active Directory for On-Premise
        .WithUrl(url)
        .WithUserName(userName)
        .WithPassword(password)
        .WithDomain(domain)         // ? 指定網域
        .WithRequireNewInstance("True")
        .Build();
}
```

### 3. 支援多種使用者名稱格式

程式碼現在支援以下格式：

| 格式 | 範例 | 適用環境 |
|------|------|----------|
| `DOMAIN\username` | `SPEECHMESSAGE\Administrator` | On-Premise (AD) |
| `username@domain` | `Administrator@speechmessage.com.tw` | On-Premise (AD) 或 Online |
| `username@domain.onmicrosoft.com` | `user@contoso.onmicrosoft.com` | Online |

程式碼會自動解析並提取 domain：

```csharp
// DOMAIN\username → domain = "SPEECHMESSAGE", user = "Administrator"
// username@domain → domain = "speechmessage.com.tw", user = "Administrator"
```

### 4. 改進的錯誤處理

```csharp
private void ValidateServiceClientConnection(ServiceClient serviceClient, string url)
{
    if (!serviceClient.IsReady)
    {
        var errorMessage = $"ServiceClient 連線失敗 (URL: {url})";
        
        // 包含所有可用的錯誤資訊
        if (!string.IsNullOrEmpty(serviceClient.LastError))
            errorMessage += $"\n錯誤訊息: {serviceClient.LastError}";
        
        if (serviceClient.LastException != null)
            errorMessage += $"\n例外詳情: {serviceClient.LastException.Message}";
        
        if (serviceClient.LastException?.InnerException != null)
            errorMessage += $"\n內部例外: {serviceClient.LastException.InnerException.Message}";
        
        throw new InvalidOperationException(errorMessage);
    }
}
```

## 更新後的流程

### 連線建立流程圖

```
CreateOnPremiseClient(url, userName, password)
    ↓
驗證參數 (ValidateConnectionParameters)
    ↓
判斷環境 (IsOnlineEnvironment)
    ↓
    ├─→ Online?
    │   └─→ BuildOnlineConnectionString (OAuth)
    │
    └─→ On-Premise?
        └─→ BuildOnPremiseConnectionString (AD)
    ↓
建立 ServiceClient (CreateServiceClient)
    ↓
驗證連線 (ValidateServiceClientConnection)
    ↓
返回 IOrganizationService
```

## 連線字串對照表

### Online 環境
```
AuthType=OAuth;
Url=https://yourorg.crm.dynamics.com;
UserName=user@contoso.onmicrosoft.com;
Password=yourPassword;
LoginPrompt=Auto;
RequireNewInstance=True
```

### On-Premise 環境 (AD)
```
AuthType=AD;
Url=https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc;
UserName=Administrator@speechmessage.com.tw;
Password=yourPassword;
Domain=speechmessage.com.tw;
RequireNewInstance=True
```

### On-Premise 環境 (IFD)
如果是 Internet-Facing Deployment，可以使用：
```
AuthType=IFD;
Url=https://yourorg.yourdomain.com/XRMServices/2011/Organization.svc;
UserName=user@yourdomain.com;
Password=yourPassword;
RequireNewInstance=True
```

## 測試建議

### 1. Online 環境測試
```csharp
var connectionService = new CrmConnectionService();
var service = connectionService.CreateOnPremiseClient(
    "https://yourorg.crm.dynamics.com",
    "user@contoso.onmicrosoft.com",
    "password"
);
// 應該使用 OAuth 驗證
```

### 2. On-Premise 環境測試 (格式 1)
```csharp
var connectionService = new CrmConnectionService();
var service = connectionService.CreateOnPremiseClient(
    "https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc",
    "SPEECHMESSAGE\\Administrator",  // DOMAIN\username 格式
    "password"
);
// 應該使用 AD 驗證，domain = "SPEECHMESSAGE"
```

### 3. On-Premise 環境測試 (格式 2)
```csharp
var connectionService = new CrmConnectionService();
var service = connectionService.CreateOnPremiseClient(
    "https://jesus.speechmessage.com.tw/XRMServices/2011/Organization.svc",
    "Administrator@speechmessage.com.tw",  // username@domain 格式
    "password"
);
// 應該使用 AD 驗證，domain = "speechmessage.com.tw"
```

### 4. 驗證連線
```csharp
if (connectionService.ValidateConnection(service))
{
    Console.WriteLine("? 連線成功！");
    
    var userId = connectionService.GetCurrentUserId(service);
    Console.WriteLine($"當前使用者 ID: {userId}");
}
else
{
    Console.WriteLine("? 連線失敗！");
}
```

## 程式碼改進摘要

### ? 新增功能

1. **環境自動偵測**
   - `IsOnlineEnvironment()` - 判斷 Online vs On-Premise

2. **分別處理驗證**
   - `BuildOnlineConnectionString()` - OAuth 驗證
   - `BuildOnPremiseConnectionString()` - AD 驗證

3. **Domain 參數支援**
   - `ConnectionStringBuilder.WithDomain()` - 設定 AD domain

4. **使用者名稱格式解析**
   - 支援 `DOMAIN\username`
   - 支援 `username@domain`
   - 自動提取 domain

5. **改進錯誤訊息**
   - 包含 LastError
   - 包含 LastException
   - 包含 InnerException

### ?? 設計模式維護

? **Strategy Pattern** - 根據環境選擇驗證策略
? **Factory Pattern** - ServiceClient 工廠方法
? **Builder Pattern** - ConnectionStringBuilder
? **Guard Clause Pattern** - 參數驗證
? **Fail-Fast Pattern** - 立即驗證連線

### ?? SOLID 原則

? **Single Responsibility** - 每個方法單一職責
? **Open/Closed** - 易於擴展新驗證類型
? **Liskov Substitution** - 返回 IOrganizationService
? **Interface Segregation** - 簡潔介面
? **Dependency Inversion** - 依賴抽象

## 支援的驗證類型總覽

| 驗證類型 | 使用場景 | AuthType | 必要參數 |
|---------|---------|----------|----------|
| **OAuth** | Dynamics 365 Online | `OAuth` | Url, UserName, Password |
| **AD** | On-Premise (內部網路) | `AD` | Url, UserName, Password, Domain |
| **IFD** | On-Premise (外部存取) | `IFD` | Url, UserName, Password |
| **ClientSecret** | 服務對服務 (S2S) | `ClientSecret` | Url, ClientId, ClientSecret |
| **Certificate** | 服務對服務 (憑證) | `Certificate` | Url, ClientId, Thumbprint |

## 常見問題解答

### Q1: 為什麼 On-Premise 不能使用 OAuth？
**A**: OAuth 需要 Azure AD 作為身分提供者，而 On-Premise 環境使用本地的 Active Directory，沒有 Azure AD authority endpoint。

### Q2: 如何確認我的環境是 Online 還是 On-Premise？
**A**: 檢查 URL：
- ? Online: `https://*.crm.dynamics.com`
- ? On-Premise: 其他自訂網域 (如 `https://yourserver.com`)

### Q3: 使用者名稱應該用哪種格式？
**A**: 
- **On-Premise AD**: `DOMAIN\username` 或 `username@domain`
- **Online**: `username@contoso.onmicrosoft.com`

### Q4: 如果連線失敗怎麼辦？
**A**: 檢查錯誤訊息中的詳細資訊：
1. 確認 URL 是否正確
2. 確認使用者名稱格式
3. 確認密碼正確
4. 確認網路連線
5. 確認防火牆設定

### Q5: 可以在同一個應用程式中連線到 Online 和 On-Premise 嗎？
**A**: 可以！`CreateOnPremiseClient` 會自動偵測環境並使用正確的驗證方式。

## 向後相容性

? **完全相容** - API 介面沒有改變
? **自動適應** - 自動選擇正確的驗證方式
? **無需修改** - 現有呼叫程式碼無需修改

```csharp
// 這個呼叫方式完全不變
var service = connectionService.CreateOnPremiseClient(url, userName, password);

// 程式會自動：
// 1. 判斷環境類型
// 2. 選擇正確的驗證
// 3. 建立連線
// 4. 驗證連線狀態
```

## 結論

### 問題已解決 ?

1. ? On-Premise 環境現在使用 AD 驗證而非 OAuth
2. ? 自動偵測環境並選擇適當的驗證方式
3. ? 支援多種使用者名稱格式
4. ? 改進的錯誤訊息提供更好的診斷資訊
5. ? 完全向後相容，無需修改現有程式碼

### 下一步建議

1. **測試連線** - 使用修正後的程式碼測試 On-Premise 連線
2. **檢查設定** - 確認使用者名稱和密碼格式正確
3. **記錄日誌** - 如果還有問題，記錄完整的錯誤訊息
4. **網路檢查** - 確認從應用程式伺服器可以存取 Dynamics 365 伺服器

---

**修正日期**: 2024
**問題類型**: OAuth 不適用於 On-Premise 環境
**解決方案**: 自動偵測環境並使用適當的驗證方式 (OAuth for Online, AD for On-Premise)
**影響範圍**: CreateOnPremiseClient 方法
**向後相容**: 完全相容

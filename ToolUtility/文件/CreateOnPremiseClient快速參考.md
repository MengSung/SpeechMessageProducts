# CreateOnPremiseClient 快速參考卡

## 問題與解決方案

### ? 原始問題
```
ArgumentNullException: Need a non-empty authority
→ OAuth 不適用於 On-Premise 環境
```

### ? 解決方案
自動偵測環境並使用正確的驗證方式

## 使用方式（無需修改）

```csharp
var connectionService = new CrmConnectionService();
var service = connectionService.CreateOnPremiseClient(url, userName, password);
```

程式會**自動處理**：
1. 偵測環境類型（Online vs On-Premise）
2. 選擇正確的驗證方式
3. 建立並驗證連線

## 環境判斷

| URL 格式 | 環境類型 | 驗證方式 |
|----------|----------|----------|
| `https://*.crm.dynamics.com` | Online | OAuth |
| `https://yourserver.com/...` | On-Premise | AD |

## 使用者名稱格式

### On-Premise (兩種格式都支援)

**格式 1: DOMAIN\username**
```csharp
connectionService.CreateOnPremiseClient(
    "https://server/org/XRMServices/2011/Organization.svc",
    "SPEECHMESSAGE\\Administrator",  // ? 使用雙反斜線
    "password"
);
```

**格式 2: username@domain**
```csharp
connectionService.CreateOnPremiseClient(
    "https://server/org/XRMServices/2011/Organization.svc",
    "Administrator@speechmessage.com.tw",
    "password"
);
```

### Online

```csharp
connectionService.CreateOnPremiseClient(
    "https://yourorg.crm.dynamics.com",
    "user@contoso.onmicrosoft.com",
    "password"
);
```

## 連線字串範例

### Online (自動使用)
```
AuthType=OAuth;
Url=https://yourorg.crm.dynamics.com;
UserName=user@contoso.onmicrosoft.com;
Password=***;
LoginPrompt=Auto;
RequireNewInstance=True
```

### On-Premise (自動使用)
```
AuthType=AD;
Url=https://server/org/XRMServices/2011/Organization.svc;
UserName=Administrator@speechmessage.com.tw;
Password=***;
Domain=speechmessage.com.tw;
RequireNewInstance=True
```

## 錯誤診斷

### 檢查連線
```csharp
if (connectionService.ValidateConnection(service))
{
    Console.WriteLine("? 連線成功");
}
else
{
    Console.WriteLine("? 連線失敗");
}
```

### 取得使用者資訊
```csharp
try
{
    var userId = connectionService.GetCurrentUserId(service);
    var user = connectionService.GetCurrentUser(service);
    
    Console.WriteLine($"使用者 ID: {userId}");
    Console.WriteLine($"使用者名稱: {user["fullname"]}");
}
catch (Exception ex)
{
    Console.WriteLine($"錯誤: {ex.Message}");
}
```

## 常見錯誤與解決

| 錯誤訊息 | 原因 | 解決方法 |
|---------|------|----------|
| `Need a non-empty authority` | 對 On-Premise 使用 OAuth | ? 已修正：自動使用 AD |
| `Failed to connect` | 網路問題或認證失敗 | 檢查 URL、使用者名稱、密碼 |
| `401 Unauthorized` | 認證失敗 | 檢查密碼是否正確 |
| `404 Not Found` | URL 不正確 | 檢查組織 URL 是否正確 |

## 測試清單

- [ ] 確認 NuGet 套件已安裝：`Microsoft.PowerPlatform.Dataverse.Client`
- [ ] 測試 Online 連線（如果有）
- [ ] 測試 On-Premise 連線
- [ ] 驗證使用者名稱格式（DOMAIN\user 或 user@domain）
- [ ] 確認密碼正確
- [ ] 檢查網路連線
- [ ] 執行 ValidateConnection 測試

## 重要提醒

### ? 向後相容
- API 介面沒有改變
- 現有程式碼無需修改
- 自動適應不同環境

### ?? 安全建議
- 不要在程式碼中硬編碼密碼
- 使用設定檔或 Key Vault
- 考慮使用 Certificate 或 ClientSecret 驗證

### ?? 最佳實務
1. 使用 try-catch 處理連線錯誤
2. 使用 ValidateConnection 確認連線
3. 記錄錯誤訊息以便診斷
4. 考慮實作連線池重用連線

## 相關文件

- ?? **詳細說明**: `On-Premise連線問題修正報告.md`
- ?? **遷移指南**: `遷移至Microsoft.PowerPlatform.Dataverse.Client指南.md`
- ?? **使用指南**: `CreateOnPremiseClient使用指南.md`
- ?? **重構報告**: `CreateOnPremiseClient重構報告.md`

## 支援

如有問題，請參考：
1. 錯誤訊息中的詳細資訊
2. ServiceClient.LastError 屬性
3. ServiceClient.LastException 屬性
4. 官方文件：https://learn.microsoft.com/en-us/dotnet/api/microsoft.powerplatform.dataverse.client.serviceclient

---

**更新日期**: 2024
**版本**: 2.0 (支援自動環境偵測)
**狀態**: ? 已修正 On-Premise 連線問題

# Connection Methods Refactoring Plan

## 目標
將 `ToolUtilityClass` 中的連線相關方法轉發到 `CrmConnectionService`

## 需要重構的方法清單

### 1. 連接 CRM 2011 服務區塊
- `GetClientCredentials(String Domain, String UserName, String Password)` ? 已在 Service
- `GetOrganizationService(...)` ? 已在 Service
- `GetClientCredentials()` - 需要轉發（使用常數參數）
- `SetOrganizationService()` ? 已在 Service
- `SetClaimsBasedAuthenticationOrganizationService()` ? 已在 Service  
- `SetClaimsBasedAuthenticationOrganizationService_DEBUG()` - 需要保留在 Facade 並轉發

### 2. 連接 Dynamics 365 服務區塊
- `SetFederatedOrganizationProxy(String DiscoveryServiceType)` ? 已在 Service
- `DiscoverOrganizations(IDiscoveryService service)` ? 已在 Service
- `FindOrganization(string orgUniqueName, OrganizationDetail[] orgDetails)` ? 已在 Service
- `GetCredentials<TService>(...)` - 私有方法，已在 Service 內部實作
- `GetProxy<TService, TProxy>(...)` - 私有方法，已在 Service 內部實作

## 實作策略

1. 在 `ToolUtilityClass` 新增 `Lazy<ICrmConnectionService>` 欄位
2. 在建構式初始化 service
3. 將所有 public 連線方法改為轉發到 service
4. 保留舊的欄位（SERVER, PORT, ORGANIZATION 等）供轉發使用
5. 更新 Dispose 方法以釋放 service

## 已完成項目
- [x] `CrmConnectionService` 實作完成
- [x] `ICrmConnectionService` 介面定義完成
- [ ] `ToolUtilityClass` 轉發實作
- [ ] 單元測試
- [ ] 整合測試

## 下一步
按文件要求，逐步修改 `ToolUtilityClass.cs`，使其成為薄 Facade 層

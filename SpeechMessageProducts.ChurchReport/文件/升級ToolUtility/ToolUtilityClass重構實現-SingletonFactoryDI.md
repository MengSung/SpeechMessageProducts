# ToolUtilityClass 重構實現 - Singleton、Factory、Dependency Injection 模式

## 概述
本文檔說明如何將整個 Solution 中使用 `new ToolUtilityClass()` 的地方改為使用 Factory 模式和 Dependency Injection 模式，確保 ToolUtilityClass 只能通過 Factory 創建唯一實例。

## 已完成的重構

### 1. 創建 Factory 模式實現
**文件**: `ToolUtility\Factory\ToolUtilityFactory.cs`

```csharp
// Thread-Safe Singleton Factory
public sealed class ToolUtilityFactory
{
    public static ToolUtilityClass GetInstance() 
    public static ToolUtilityClass GetInstance(string discoveryServiceType)
}
```

**特點**:
- 使用 Double-Check Locking 確保線程安全
- 遵循 Singleton 模式
- 提供重置實例方法（僅供測試使用）

### 2. 更新 ToolUtilityClass 建構函數
**文件**: `ToolUtility\ToolUtilityClass.cs`

```csharp
// 建構函數改為 internal，只能通過 Factory 創建
internal ToolUtilityClass()
internal ToolUtilityClass(String DiscoveryServiceType)
```

**重要**: 原本的 `public` 建構函數改為 `internal`，確保外部代碼無法直接 `new ToolUtilityClass()`。

### 3. 創建 Dependency Injection 支援
**文件**: `ToolUtility\DependencyInjection\IToolUtilityProvider.cs`
```csharp
public interface IToolUtilityProvider
{
    ToolUtilityClass GetToolUtility();
}
```

**文件**: `ToolUtility\DependencyInjection\ToolUtilityProvider.cs`
```csharp
public class ToolUtilityProvider : IToolUtilityProvider
{
    public ToolUtilityClass GetToolUtility() 
        => ToolUtilityFactory.GetInstance();
}
```

**文件**: `ToolUtility\DependencyInjection\ServiceCollectionExtensions.cs`
```csharp
public static IServiceCollection AddToolUtility(this IServiceCollection services)
{
    services.AddSingleton<IToolUtilityProvider, ToolUtilityProvider>();
    return services;
}
```

## 需要修改的文件列表

根據搜尋結果，以下文件需要將 `new ToolUtilityClass()` 改為使用 Factory:

1. **ChurchReport\WebServiceConnector\DownloadIntegrateData.cs**
   ```csharp
   // 修改前:
   private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass("DYNAMICS365-9.0");
   
   // 修改後:
   using ToolUtilityNameSpace.Factory;
   private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance("DYNAMICS365-9.0");
   ```

2. **ChurchReport\Tools\QPayToolkit.cs**
   - 此文件不使用 ToolUtilityClass，無需修改

3. **ChurchReport\WebServiceConnector\LineBindingUtility.cs**
4. **ChurchReport\WebServiceConnector\QPayProcessor.cs**
5. **ChurchReport\WebServiceConnector\LineNotifyUtility.cs**
6. **ChurchReport\Controllers\HomeController.cs**
7. **ChurchReport\Models\QpayManager.cs**
8. **ChurchReport\Tools\LineUtilityClass.cs**
9. **ChurchReport\Tools\QPayDedicationBookingProcessor.cs**
10. **ChurchReport\Tools\PersonalQrCodeUtility.cs**
11. **ChurchReport\WebServiceConnector\NewPerson.cs**
12. **ChurchReport\Controllers\AuthenticationController.cs**
13. **ChurchReport\Controllers\EquipmentController.cs**

## 修改模式

### 模式 A: 在類別成員中直接使用
```csharp
// 修改前
using ToolUtilityNameSpace;

public class SomeClass
{
    private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass();
}

// 修改後
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Factory;

public class SomeClass
{
    private ToolUtilityClass m_ToolUtilityClass = ToolUtilityFactory.GetInstance();
}
```

### 模式 B: 在方法中使用
```csharp
// 修改前
public void SomeMethod()
{
    ToolUtilityClass tool = new ToolUtilityClass();
    tool.DoSomething();
}

// 修改後
public void SomeMethod()
{
    ToolUtilityClass tool = ToolUtilityFactory.GetInstance();
    tool.DoSomething();
}
```

### 模式 C: 在 ASP.NET Core Controllers 中使用 DI
```csharp
// 修改前
public class HomeController : Controller
{
    private ToolUtilityClass m_ToolUtilityClass = new ToolUtilityClass();
}

// 修改後
using ToolUtilityNameSpace.DependencyInjection;

public class HomeController : Controller
{
    private readonly IToolUtilityProvider _toolUtilityProvider;
    private ToolUtilityClass m_ToolUtilityClass => _toolUtilityProvider.GetToolUtility();
    
    public HomeController(IToolUtilityProvider toolUtilityProvider)
    {
        _toolUtilityProvider = toolUtilityProvider;
    }
}
```

## 在 Program.cs 或 Startup.cs 中註冊服務

### .NET 10 (minimal API) 方式
**文件**: `ChurchReport\Program.cs`
```csharp
using ToolUtilityNameSpace.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 註冊 ToolUtility 服務
builder.Services.AddToolUtility();

// ...existing code...
```

### ASP.NET Core (Startup.cs) 方式
```csharp
public void ConfigureServices(IServiceCollection services)
{
    // 註冊 ToolUtility 服務
    services.AddToolUtility();
    
    // ...existing code...
}
```

## 設計模式遵循的 LINUS 原則

### 1. **Single Responsibility Principle (單一職責原則)**
- `ToolUtilityFactory`: 只負責創建和管理 ToolUtilityClass 實例
- `IToolUtilityProvider`: 只負責提供 ToolUtilityClass 實例的接口定義
- `ToolUtilityProvider`: 只負責實現提供者接口

### 2. **Open/Closed Principle (開閉原則)**
- 通過 `IToolUtilityProvider` 接口，可以在不修改現有代碼的情況下擴展新的提供者實現

### 3. **Liskov Substitution Principle (里氏替換原則)**
- 任何實現 `IToolUtilityProvider` 的類別都可以替換使用

### 4. **Interface Segregation Principle (接口隔離原則)**
- `IToolUtilityProvider` 只定義了一個方法，保持接口最小化

### 5. **Dependency Inversion Principle (依賴反轉原則)**
- Controllers 依賴於 `IToolUtilityProvider` 接口，而不是具體實現

## 設計模式使用

### 1. **Singleton Pattern (單例模式)**
- 確保 ToolUtilityClass 在整個應用程式生命週期中只有一個實例
- 使用 Double-Check Locking 確保線程安全

### 2. **Factory Pattern (工廠模式)**
- `ToolUtilityFactory` 負責創建 ToolUtilityClass 實例
- 隱藏創建邏輯，客戶端只需調用 `GetInstance()`

### 3. **Dependency Injection Pattern (依賴注入模式)**
- 通過 `IToolUtilityProvider` 接口和 ASP.NET Core DI 容器實現依賴注入
- 降低代碼耦合度，提高可測試性

## 優勢

1. **記憶體效率**: 整個應用程式只有一個 ToolUtilityClass 實例
2. **線程安全**: 使用 lock 和 volatile 確保多線程環境下的安全性
3. **易於測試**: 通過 DI 可以輕鬆注入 mock 物件進行單元測試
4. **集中管理**: 所有實例創建邏輯集中在 Factory 中
5. **遵循最佳實踐**: 符合 SOLID 原則和常見設計模式

## 注意事項

1. **不要**直接使用 `new ToolUtilityClass()`，編譯器會阻止（因為建構函數是 internal）
2. **不要**在 Factory 外部嘗試創建實例
3. **確保** Program.cs 中已註冊 `AddToolUtility()` 服務
4. **測試時**可以使用 `ToolUtilityFactory.ResetInstance()` 重置實例

## 驗證步驟

1. 編譯整個 Solution，確保沒有編譯錯誤
2. 檢查是否還有任何 `new ToolUtilityClass()` 的使用（應該無法編譯）
3. 執行應用程式，確認功能正常
4. 執行單元測試，確認 DI 正常工作

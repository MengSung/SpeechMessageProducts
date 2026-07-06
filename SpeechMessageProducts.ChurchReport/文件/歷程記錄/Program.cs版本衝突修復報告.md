# Program.cs 版本衝突修復報告

## 問題描述

應用程式啟動時出現 `System.MethodAccessException` 錯誤：

```
方法 'Microsoft.Extensions.Logging.Configuration.LoggerProviderConfigurationFactory.GetConfiguration(System.Type)' 
嘗試存取方法 'Microsoft.Extensions.Logging.ProviderAliasUtilities.GetAlias(System.Type)' 失敗。
```

## 根本原因

**NuGet 套件版本衝突**：

1. **Microsoft.Extensions.Logging** - 版本 **3.1.8**
   - 來源：`PowerPlatform.Dataverse.Client` 的傳遞依賴

2. **Microsoft.Extensions.Logging.Configuration** - 版本 **2.2.0**
   - 來源：ASP.NET Core 2.2.0 框架

這兩個版本的內部 API 不相容，導致：
- `LoggerProviderConfigurationFactory` (2.2.0) 嘗試呼叫
- `ProviderAliasUtilities.GetAlias` (3.1.8) 方法
- 但方法簽章或可見性在不同版本間發生變化

## 解決方案

### ? 失敗的嘗試

1. **明確指定套件版本** - 失敗
   - 原因：`PowerPlatform.Dataverse.Client` 強制需要 3.1.8 版本

2. **使用 `ConfigureLogging` 清除提供者** - 失敗
   - 原因：`CreateDefaultBuilder` 在建構時就已經觸發了版本衝突

### ? 成功的解決方案

**手動建構 WebHost，避免使用 `CreateDefaultBuilder`**

```csharp
public static IWebHost BuildWebHost(string[] args)
{
    // 手動建立配置
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args ?? new string[0])
        .Build();

    // 直接使用 WebHostBuilder，不使用 CreateDefaultBuilder
    return new WebHostBuilder()
        .UseKestrel(options =>
        {
            options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(30);
            options.Limits.MaxRequestBufferSize = null;
            options.Limits.MaxConcurrentConnections = 1000;
            options.Limits.MaxConcurrentUpgradedConnections = 1000;
        })
        .UseContentRoot(Directory.GetCurrentDirectory())
        .UseConfiguration(config)
        .UseIISIntegration()
        .UseStartup<Startup>()
        .Build();
}
```

## 修改的檔案

### 1. `Program.cs`
- ? 移除 `WebHost.CreateDefaultBuilder(args)`
- ? 改用 `new WebHostBuilder()` 手動建構
- ? 手動配置 Configuration
- ? 移除 `ConfigureKestrel` 重複配置

### 2. `Startup.cs`
- ? 移除 `loggerFactory.AddConsole()` 和 `loggerFactory.AddDebug()`
- ? 保留文件追蹤監聽器 (Trace.log)

### 3. `appsettings.json`
- ? 簡化 Logging 配置
- ? 移除 `IncludeScopes` 選項
- ? 將預設日誌級別改為 "Warning"

### 4. `ChurchReport.csproj`
- ? 添加 `<NoWarn>NU1605</NoWarn>` 抑制套件降級警告

## 技術說明

### 為什麼 CreateDefaultBuilder 會導致問題？

`CreateDefaultBuilder` 內部會自動：
1. 載入預設的日誌配置
2. 註冊 Console 和 Debug 日誌提供者
3. 使用 `Microsoft.Extensions.Logging.Configuration` 套件
4. 在依賴注入容器建構時觸發版本衝突

### 手動建構的優勢

1. **完全控制** - 只註冊必要的服務
2. **避免衝突** - 不會自動載入有問題的日誌配置
3. **向下相容** - 與 ASP.NET Core 2.2.0 完全相容
4. **效能優化** - 移除不必要的預設服務

## 驗證步驟

1. ? 編譯成功 - 無錯誤和警告（除了預期的 MVC 警告）
2. ? 應用程式可以正常啟動
3. ? 日誌系統仍然可用（透過 Trace.log）
4. ? 所有中間件正常運作

## 注意事項

### 遺失的 CreateDefaultBuilder 功能

手動建構 WebHost 後，以下 `CreateDefaultBuilder` 提供的功能需要自行處理：

1. **日誌系統** - 已透過 Trace.log 處理
2. **環境變數** - 已手動添加 `AddEnvironmentVariables()`
3. **命令列參數** - 已手動添加 `AddCommandLine()`
4. **用戶密鑰** - 如需要，可手動添加 `AddUserSecrets()`

### 如果需要日誌輸出

如果未來需要啟用 Console 日誌，可以在 `Startup.cs` 中手動配置：

```csharp
public void Configure(IApplicationBuilder app, IHostingEnvironment env)
{
    // 手動配置日誌（如果需要）
    var loggerFactory = app.ApplicationServices.GetService<ILoggerFactory>();
    // 不使用 AddConsole/AddDebug 避免版本衝突
}
```

## 相關檔案

- `ChurchReport\Program.cs`
- `ChurchReport\Startup.cs`
- `ChurchReport\appsettings.json`
- `ChurchReport\ChurchReport.csproj`

## 修復時間

- **修復日期**: 2025-01-26
- **修復工程師**: GitHub Copilot
- **影響範圍**: 應用程式啟動流程

## 結論

透過手動建構 WebHost 並避免使用 `CreateDefaultBuilder`，成功解決了 Microsoft.Extensions.Logging 套件版本衝突問題。應用程式現在可以正常啟動並運行，同時保持了所有必要的功能。

---

**狀態**: ? 已解決並驗證
**優先級**: ?? 高 (阻擋應用程式啟動)
**複雜度**: ??? (需要深入理解 ASP.NET Core 內部機制)

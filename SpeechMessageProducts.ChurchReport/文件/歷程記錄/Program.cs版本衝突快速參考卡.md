# Program.cs 版本衝突快速參考卡

## ?? 問題特徵

```
System.MethodAccessException: 
方法 'Microsoft.Extensions.Logging.Configuration.LoggerProviderConfigurationFactory.GetConfiguration(System.Type)' 
嘗試存取方法 'Microsoft.Extensions.Logging.ProviderAliasUtilities.GetAlias(System.Type)' 失敗。
```

## ?? 快速診斷

```powershell
# 檢查套件版本衝突
cd ChurchReport
dotnet list package --include-transitive | Select-String "Microsoft.Extensions.Logging"
```

**預期輸出問題**:
```
> Microsoft.Extensions.Logging                    3.1.8    ?
> Microsoft.Extensions.Logging.Configuration      2.2.0    ?
```

## ? 快速修復

### Program.cs - 使用手動建構

```csharp
public static IWebHost BuildWebHost(string[] args)
{
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args ?? new string[0])
        .Build();

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

### 所需 using 語句

```csharp
using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Configuration;
```

## ?? 檢查清單

- [ ] 移除 `WebHost.CreateDefaultBuilder(args)`
- [ ] 改用 `new WebHostBuilder()`
- [ ] 手動建立 `ConfigurationBuilder`
- [ ] 添加 `UseConfiguration(config)`
- [ ] 編譯並測試應用程式啟動

## ?? 常見陷阱

| 問題 | 解決方案 |
|------|----------|
| 仍然使用 `CreateDefaultBuilder` | 完全移除，改用手動建構 |
| 忘記添加 Configuration | 使用 `ConfigurationBuilder` 手動建構 |
| 重複的 Kestrel 配置 | 只在 `UseKestrel` 中配置一次 |

## ?? 相關檔案

- `ChurchReport\Program.cs` - 主要修改檔案
- `ChurchReport\Startup.cs` - 移除過時的日誌配置
- `ChurchReport\appsettings.json` - 簡化 Logging 設定

## ?? 驗證命令

```powershell
# 清理並重建
dotnet clean
dotnet build

# 執行應用程式
dotnet run --project ChurchReport\ChurchReport.csproj
```

## ? 成功指標

- ? 編譯無錯誤
- ? 應用程式正常啟動
- ? 無 MethodAccessException 錯誤
- ? Trace.log 正常寫入

## ?? 如果仍然失敗

1. **清理方案**
   ```powershell
   # 刪除 bin 和 obj 資料夾
   Get-ChildItem -Path . -Include bin,obj -Recurse | Remove-Item -Recurse -Force
   dotnet clean
   dotnet restore
   dotnet build
   ```

2. **檢查 Startup.cs**
   - 確保移除了 `loggerFactory.AddConsole()` 和 `loggerFactory.AddDebug()`

3. **檢查 appsettings.json**
   - 確保 Logging 配置已簡化

## ?? 支援資訊

- **完整報告**: `ChurchReport\文件\Program.cs版本衝突修復報告.md`
- **修復日期**: 2025-01-26
- **測試狀態**: ? 已驗證

---

**記住**: 不要使用 `CreateDefaultBuilder`！手動建構是唯一可靠的解決方案。

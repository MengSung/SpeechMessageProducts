# 階段0 第6批審查：Development 防護 P0-SEC／P0-ENV

範圍：`73b6e6cf4..ae29cb40a`（單模型 ClaudeOnly，唯讀審查，未執行任何測試／連線／寫入）

## Critical 🔴

### 1. `DefaultOrganization` 防護在目前基底狀態下形同虛設
**檔案**：`SpeechMessageProducts.ChurchReport/Configuration/ChurchReportDevelopmentConfiguration.cs:64-65, 76-78`

```csharp
var baseChannels = baseline.GetSection("LineMessaging").GetChildren()
    .Where(s => !string.IsNullOrWhiteSpace(s["ChannelAccessToken"])).ToArray();   // 65
...
var defaultOrganization = configuration["LineMessaging:DefaultOrganization"];     // 76
if (string.IsNullOrWhiteSpace(defaultOrganization) || baseChannels.Any(s => string.Equals(s.Key, defaultOrganization, StringComparison.OrdinalIgnoreCase)))  // 77
    throw UnsafeKey("LineMessaging:DefaultOrganization", "必須選擇獨立的測試別名。");  // 78
```

- **觸發條件**：規格明確指出「基底正式 LINE token 被清空」。因此實際 `appsettings.json` 的 `Jesus`／`JesusBack` 這兩個 `ChannelAccessToken` 都是空白，`baseChannels`（第65行的過濾條件只收非空白 token）在正式基底上必為空陣列。
- **影響**：`baseChannels.Any(...)` 永遠為 `false`，使第77行的 OR 條件只剩「`defaultOrganization` 是否空白」這個檢查。也就是說，只要 `appsettings.Development.json` 或 User Secrets 把 `LineMessaging:DefaultOrganization` 設成正式別名 `Jesus`（或 `JesusBack`），只要不恰好等於基底 token 的字面值（第70行另一條單獨檢查），本防護**不會拒絕**。Development／測試環境就可能以正式教會別名發出 LINE 推播，直接違反本批審查重點「跨使用者／跨教會隔離」。
- **可重現測試缺口**：現有測試（`DevelopmentGuard_UnsafeSetting_RejectsWithoutSecret` 中 `DefaultOrganization="Jesus"` 案例）之所以會通過，是因為測試用的 `SettingsFixture` 在基底檔刻意塞了非空白的假 token（見 `DevelopmentSafetyTests.cs` 的 `SettingsFixture` 建構子）。沒有任何測試使用「基底 token 為空白」（即目前 repo 真實狀態）這個情境去驗證 `DefaultOrganization=Jesus` 會被擋下——而這正是正式部署會遇到的狀態。
- **最小修法**：比對 `defaultOrganization` 是否命中基底 `LineMessaging` 下**任何既有別名鍵**，不應以該別名目前是否有非空白 token 為篩選條件（即第77行應改為對照 `baseline.GetSection("LineMessaging").GetChildren()` 的全部 `Key`，而非只比對 `baseChannels`），或改用固定的正式別名白名單（`Jesus`、`JesusBack`）做排除比對。

### 2. `ValidateTestHost` 未驗證 Scheme／路徑，允許 HTTP 明文連線
**檔案**：`SpeechMessageProducts.ChurchReport/Configuration/ChurchReportDevelopmentConfiguration.cs:116-121`（呼叫點：81、87、88 行）

```csharp
private static void ValidateTestHost(IConfiguration configuration, string key)
{
    if (!Uri.TryCreate(configuration[key], UriKind.Absolute, out var uri) ||
        !string.Equals(uri.Host, "goodshepherdback.speechmessage.com.tw", StringComparison.OrdinalIgnoreCase))
        throw UnsafeKey(key, "開發模式只能使用指定測試組織的主機。");
}
```

- **觸發條件**：此方法同時用來驗證 `CrmConnection:ServerUrl`（第81行）、`DynamicsAccess:Embedded:OrganizationWebApiBaseUri`（第87行）、`ResourceUri`（第88行）。它只比對 `uri.Host`，完全沒有檢查 `uri.Scheme` 是否為 `https`，也未檢查路徑（例如 CRM 應為固定的 `/XRMServices/2011/Organization.svc`）。
- **影響**：若 `appsettings.Development.json` 或 User Secrets 誤填（或被竄改）成 `http://goodshepherdback.speechmessage.com.tw/...`，主機名稱比對仍會通過，守門不會拒絕啟動。這會讓 `CrmConnection:Domain/Username/Password` 與後續 Dynamics Web API 存取以明文 HTTP 傳輸，正是本批審查重點「機密外洩」要防堵的情境，且與規格要求「goodshepherdback 完整 https Organization.svc」不一致。
- **可重現測試缺口**：`DevelopmentGuard_UnsafeSetting_RejectsWithoutSecret` 的 `InlineData` 只測試「主機名不同」（`sunnyvalechback`）會被拒絕，從未測試「主機名相同、但 Scheme 為 http」是否被拒絕；`Development_CrmServerUrl_UsesHttpsAndFullOrganizationServicePath` 這個測試只驗證實際 JSON 檔「目前」寫的值正確，並未呼叫 `ValidateTestHost`／`ValidateDevelopmentConfiguration` 本體去驗證守門邏輯本身有無強制 Scheme。因此守門函式對 Scheme／路徑的缺漏目前無任何測試能偵測到。
- **最小修法**：在 `ValidateTestHost` 內加入 `string.Equals(uri.Scheme, "https", StringComparison.Ordinal)` 的強制檢查（CRM ServerUrl 建議再比對固定路徑 `/XRMServices/2011/Organization.svc`），並補一個「同主機、錯誤 Scheme」的拒絕測試案例。

## Warning 🟡

### 3. 11＋1 個獨立讀取點各自建構組態，僅由單一入口驗證
**檔案**：`SpeechMessageProducts.ChurchReport/Program.cs`（新增的 `ValidateDevelopmentConfiguration` 呼叫）對照 `DonationPaymentManager.cs`、`LineUtilityClass.cs`、`QrCodeUtility.cs` 等 11 處 Tools 類別

- Program.cs 只對 `builder.Configuration`（ASP.NET Core Host 標準組態鏈）呼叫一次 `ValidateDevelopmentConfiguration`。但 `DonationPaymentManager`、`LineUtilityClass`、`QrCodeUtility` 等 11 個類別各自用 `new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())...AddChurchReportDevelopmentOverrides()` 建出**各自獨立**的 `IConfiguration` 執行個體（static／Lazy，延遲到第一次存取時才建構），這些執行個體本身從未被 `ValidateDevelopmentConfiguration` 檢查過。
- 目前的安全性完全依賴「這些獨立組態與 Host 組態會讀到相同的檔案與相同結果」這個假設。只要其中一個讀取點在實際部署環境下（例如 IIS 宿主時 `Directory.GetCurrentDirectory()` 不等於 `ContentRootPath`）解析出與 Host 不同的結果，就會在完全沒有守門檢查的情況下載入設定。這屬於架構脆弱點而非目前可直接證明的漏洞，建議至少在測試中覆蓋「CWD ≠ ContentRootPath」情境，或讓這些讀取點共用同一個已驗證過的組態來源。

### 4. 共用的免參數 `AddChurchReportDevelopmentOverrides()` 以原始環境變數獨立判斷環境
**檔案**：`SpeechMessageProducts.ChurchReport/Configuration/ChurchReportDevelopmentConfiguration.cs:30-33`

```csharp
internal static IConfigurationBuilder AddChurchReportDevelopmentOverrides(this IConfigurationBuilder builder)
    => builder.AddChurchReportDevelopmentOverrides(Directory.GetCurrentDirectory(),
        ResolveEnvironmentName(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")),
        typeof(global::ChurchReport.Program).Assembly);
```

此方法直接讀取程序環境變數來判斷環境，與 Program.cs 中 `builder.Environment.EnvironmentName`（由 ASP.NET Core Host 依其自身規則解析，可能還考慮命令列參數等其他來源）是兩條獨立的判斷路徑。兩者在多數情況下會一致，但若環境名稱來源不是單純環境變數（例如以命令列 `--environment` 指定），兩條路徑可能給出不同結果，使 11 個讀取點與 Program.cs 的驗證基準不同步。建議讓這些讀取點改為接受呼叫端傳入已解析好的環境名稱，而非各自重新解析。

## Info ⚪

- 資源釋放（`ReadBaseline` 的 `using var stream`／`ConfigurationRoot` dispose、各測試 fixture 的清理防護）、例外訊息不外洩機密值、環境變數 provider 原始優先順序保留（`LineMessagingProcessorClass.ResolveDefaultChannelAccessToken`）、守門呼叫時機置於建立例外 LINE sender／CRM 服務之前，經檢視均符合規格要求，未發現缺陷。
- `LineMessagingProcessor` 共用預設路徑刻意不接 `ChurchReport` 的 User Secrets，符合規格「不得引用 ChurchReport 或讀取其機密」。
- `appsettings.Development.json` 中帳密欄位屬使用者已核定之例外（2026-11-03），本審查未對其存放方式提出異議。

## Summary

**請求變更（Request Changes）**。本輪發現 2 項 Critical：(1) `DefaultOrganization` 防護因基底 token 已被清空而在現狀下失效，可能讓 Development 環境誤用正式教會別名發送 LINE 訊息；(2) `ValidateTestHost` 未驗證 Scheme／路徑，可能讓 CRM／Embedded 連線在主機名正確但協定錯誤（HTTP）時仍通過守門，導致帳密明文傳輸風險。兩者皆為新增程式碼中的邏輯缺口，且現有測試套件均未覆蓋對應情境，建議修正後補測試再放行。另有 2 項架構層級 Warning 建議納入後續批次處理。本份審查僅涵蓋本批次差異，依規格需等同批其他分份一併完成後才算完整。

---
SESSION_ID: b953b507-3aed-4ee3-a2ea-4ac51314477a

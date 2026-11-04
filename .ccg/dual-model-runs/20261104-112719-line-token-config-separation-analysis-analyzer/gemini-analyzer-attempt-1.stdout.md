# 結構化分析報告：ChurchReport LINE 憑證配置分離與環境變數防護

本報告針對 **ChurchReport LINE 憑證配置分離與環境防護** 變更提案進行資安、架構與 .NET 組態提供者（Configuration Provider）行為的深度分析。

---

## 1. UX Analysis / 使用者與營運影響評估 (User Impact Assessment)
- **開發環境隔離性提升**：透過將基底 `appsettings.json` 中的生產環境 LINE Channel Token 清空，並在 Development 環境下強制拒絕任何殘留或直接對應生產別名的 Channel Token，有效防止開發人員在本地測試或除錯時誤用正式環境的 LINE 訊息 API 額度或造成訊息廣播至正式會友。
- **維運與部署體驗**：正式環境（Production/Staging）透過加入 `LINE__`-前綴的環境變數提供者（Environment Variable Provider），使雲端部署或容器化部署（如 Docker / Azure App Service）能夠直接注入憑證，而不必依賴被清除的實體 JSON 檔案。
- **預設別名行為一致性**：在 Development 環境下，預設別名（DefaultOrganization）被嚴格限縮為 `DevTest`，並結合 User Secrets 進行隔離，確保本地開發行為與正式環境徹底切離。

---

## 2. Design Evaluation / 設計與組態架構評估 (Design System Evaluation)
- **基底設定與覆寫機制**：
  - 目前 tracked 的 `appsettings.json` 清空兩個生產別名 Token 值。
  - `appsettings.Development.json` 保持清空狀態，將實際測試用的 `DevTest` 憑證交由開發者的 `User Secrets` 進行保管（不入版控）。
- **環境變數提供者納入時機（關鍵架構設計）**：
  - 提案要求：在 **non-Development** 環境中加入 `LINE__`-前綴的環境變數提供者，且**必須在 Host 進行驗證之前**加入，同時涵蓋 11 個舊版 ChurchReport 組態建立器（legacy builders）以及共用的預設解析器。
  - 對應前綴：`LINE__LineMessaging__Jesus__ChannelAccessToken` 與 `LINE__LineMessaging__JesusBack__ChannelAccessToken`（對應 .NET 巢狀組態的雙底線 `__` 轉換規則）。

---

## 3. Technical Considerations / 前端與後端架構考量 (Frontend Architecture)

### 3.1  `.NET` 組態提供者排序與覆寫陷阱 (Configuration Provider Ordering Pitfalls)
- **陷阱描述**：在 .NET `IConfigurationBuilder` 中，**後加入的提供者（Provider）優先級高於先加入的提供者**。
- **風險分析**：
  - 若在 Non-Development 環境中，將環境變數提供者（`AddEnvironmentVariables(prefix: "LINE__")`）加在 JSON 檔的**前面**，則 JSON 檔中的值會覆蓋環境變數，導致部署時透過環境變數傳入的正式憑證失效。
  - 必須確保組態鏈的順序為：`appsettings.json` $\rightarrow$ `appsettings.{Environment}.json` $\rightarrow$ `AddEnvironmentVariables(prefix: "LINE__")`（或更後端的高優先級來源），確保環境變數能夠正確覆寫基底設定。

### 3.2 驗證 User Secrets 提供者來源／路徑（Provenance）的可靠性評估
- **提案要求**：驗證任何非空的 `DevTest` 值是否確實由主機的 User Secrets JSON 提供者（Provider Source / Path）所提供。
- **技術可行性與風險**：
  - **可靠性判定：中度偏低（需謹慎處理）**。
  - 在 .NET 的 `IConfigurationRoot` 結構中，`ConfigurationRoot.Providers` 雖然是一個包含所有註冊 Provider 的 `IList<IConfigurationProvider>`，但標準的 `IConfigurationProvider` 介面（如 `JsonConfigurationProvider`、`EnvironmentVariablesConfigurationProvider`、`MemoryConfigurationProvider`）**並沒有暴露公開的檔案路徑（Path）或來源識別屬性**。
  - 若要檢查某個 Key 是否來自 User Secrets，若僅檢查 Provider 的型別（例如 `JsonConfigurationProvider`），由於 User Secrets 底層也是透過 `JsonConfigurationProvider` 實作（指向 `%APPDATA%\Microsoft\UserSecrets\{AppId}\secrets.json`），單純從型別無法區分它是 `appsettings.Development.json` 還是 `secrets.json`，除非檢查 `FileConfigurationProvider.Source.Path` 或透過自訂的 Guard 邏輯來比對來源檔案路徑。
  - **建議對策**：不要依賴底層 Provider 內省來驗證來源，而是透過**組態載入順序與白名單測試**（如現有的 `DevelopmentSafetyTests`）來確保 `DevTest` 只能從 User Secrets 載入，或在驗證時確保 Development 環境下不允許來自基底 `appsettings.json` 的非空 Token。

---

## 4. Findings & Classification / 發現與風險分級

| 分類 | 發現摘要 | 影響範圍 | 具體檔案 / 測試合約 |
| :--- | :--- | :--- | :--- |
| **Critical** | **環境變數提供者順序錯誤風險**：若 `AddEnvironmentVariables(prefix: "LINE__")` 加入時機晚於 Host 驗證或順序在 JSON 之前，將導致生產環境無法讀取環境變數憑證。 | 正統部署與運作 | `Program.cs`, `ChurchReportDevelopmentConfiguration.cs` |
| **Warning** | **User Secrets 來源辨識的型別限制**：直接透過 Provider 內省檢查 User Secrets 來源路徑在標準 .NET API 中缺乏直接支援（底層皆為 `JsonConfigurationProvider`）。 | 單元測試與啟動防護 | `ChurchReportDevelopmentConfiguration.cs`, `DevelopmentSafetyTests.cs` |
| **Info** | **11 個舊版組態建立器的一致性**：所有獨立的 `ConfigurationBuilder` 實例（如 `DonationPaymentManager`, `LineUtilityClass` 等）必須同步套用相同的環境變數與安全防護。 | 輔助工具與背景處理 | 11 個舊版工具類別的建構子 |

---

## 5. Options & Trade-offs / 方案與取捨
- **方案 A（嚴格提供者路徑檢查）**：嘗試透過反射或強制轉型 `FileConfigurationProvider` 來檢查 `Path` 是否包含 `secrets.json`。
  - *貿易對結（Trade-off）*：實作複雜且相依於 .NET 內部實作細節（Internal implementation details），未來 .NET 版本升級可能失效。
- **方案 B（透過組態合成順序與防護斷言，推薦）**：不直接檢查 Provider 實例的內部檔案路徑，而是透過 **啟動期驗證（Startup Guard）** 確保在 Development 下：
  1. 基底 Token 必須為空。
  2. 若有提供 `DevTest` 憑證，必須透過明確的組態分層（Base $\rightarrow$ Development JSON $\rightarrow$ User Secrets）載入，並在測試中以 `SettingsFixture` 驗證 User Secrets 能夠成功注入。

---

## 6. Recommendation / 建議與實作方針 (Preferred Approach)

1. **組態鏈順序硬化**：
   在 `Program.cs` 與共用組態擴充方法中，確保 `AddEnvironmentVariables(prefix: "LINE__")` 放置於 JSON 載入之後、驗證執行之前：
   ```csharp
   builder.Configuration
       .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
       .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
       .AddEnvironmentVariables(prefix: "LINE__"); // 確保非 Development 環境優先讀取環境變數
   ```
2. **避免脆弱的 Provider 來源內省**：
   放棄直接透過組態 Provider 實例來判定檔案路徑的作法，改用 **「基底設定絕對清空 + 測試合約（`DevelopmentSafetyTests`）強制驗證各層級覆寫行為」**。
3. **擴大測試涵蓋範圍**：
   新增針對 `LINE__` 前綴環境變數在 Non-Development 環境下正確解析的單元測試，並確保 11 個舊版組態建立器皆通過對應的安全性合約檢查。

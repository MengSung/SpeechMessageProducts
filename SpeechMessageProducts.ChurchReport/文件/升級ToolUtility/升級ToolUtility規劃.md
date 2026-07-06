# 升級計劃：將 `ToolUtility` 從 .NET Framework 4.6.2 升級到 .NET 10

目的
- 將 `ToolUtility` 現有專案現代化為 .NET 10 類庫，以便與其他專案（例如 `ChurchReport`）在現代 .NET 平台上良好互通與維護。
- 最小化功能變更，同時改善可測試性、可擴展性與可維護性。

範圍
- 針對位於 `ToolUtility` 專案（目前目標為 .NET Framework 4.6.2）進行全面遷移。
- 不改變公開 API 的行為（盡可能維持向後相容），但允許在必要時新增 interface 或重構以支援 DI 與現代用法。

前提與假設
- 專案使用 SDK-style 或可轉為 SDK-style 的 csproj（升級時會以 SDK-style 重建專案檔）。
- 任何第三方相依套件若未支援 .NET 10，須找替代或以多目標（multi-targeting）策略過渡。
- 所有平台依賴（如 Windows-only APIs）會標註並用條件式編譯或抽象化處理。

高層次步驟
1. 事前準備
   - 建立分支（短期小而頻繁 commit，符合 Linus 原則）。
   - 撰寫單元測試覆蓋現有重要行為（如尚未存在，優先補足），以便升級後驗證行為一致性。
2. 專案檔轉換（csproj）
   - 將原有非 SDK-style csproj 轉為 SDK-style，設定 `TargetFramework` 為 `net10.0`。
   - 如需暫時保留兼容，考慮 `TargetFrameworks`（例如 `net462;net10.0`）進行多目標打包，逐步移除舊目標。
3. 代碼層面修改
   - 啟用 `nullable` 與 `LangVersion` 適配（視需要逐步修正 Nullable 引發的編譯警告）。
   - 將同步 I/O 或已棄用的 API（如 `WebClient`、`BinaryFormatter`、`AppDomain` 特殊使用）替換為現代 API（`HttpClient`、`System.Text.Json`、無 BinaryFormatter 的序列化方案）。
   - 移除或替換 `System.Configuration` 直接依賴，改用 `Microsoft.Extensions.Configuration`（若為 library，提供配置抽象而非強綁實作）。
   - 支援 `Task`/`async` 模式，將 IO 與網路呼叫改為非同步 API（逐步重構，保持行為一致）。
   - 若有使用 `ConfigurationManager` 或 `Registry` 等平台特定呼叫，則通過 Adapter/Facade 抽象化並提供可測試的替代實作。
4. 相依套件
   - 檢查 NuGet 套件是否支援 net10；若不支援，尋找替代或升級版本。
   - 移除不再安全或被棄用之套件（例如依賴 BinaryFormatter 的套件）。
5. 建構與 CI
   - 更新 CI pipeline（若有）以使用 .NET 10 SDK 映像/Agent。
   - 在 CI 中加入測試、可選的靜態分析（StyleCop/EditorConfig）、與安全掃描（OWASP/依賴掃描）。
6. 驗證
   - 執行單元測試與整合測試，驗證舊有功能一致性。
   - 在本機與目標環境進行採樣部署測試，確保在目標 runtime 上行為正常。
7. 發佈與回滾計畫
   - 發佈新版本並標註 breaking changes（若有）。
   - 若回滾必要，保留舊版 artefact 與分支以便快速回復。

如何堅守 Linus 代碼原則（在此以原則性做法說明）
- 小而頻繁的變更集（small, incremental commits）
  - 將升級拆成多個小步驟：先新增測試 → 轉換 csproj → 修正編譯錯誤 → 替換危險 API → 清理與優化。
- 保持簡單（Keep it simple）
  - 優先簡單直接的實作，避免過度抽象；僅在必要時引入抽象與模式。
- 明確的注釋與設計決策（Document design trade-offs）
  - 在 PR 與變更說明中記錄每一項重大修改的原因與替代方案。
- 嚴格的 code review 與 test-first 思維
  - 所有改動經過 code review，重要行為有單元/整合測試保護。
- 不要在預設情況下追求過度優化（Premature optimization is the root of all evil）
  - 先以正確與可讀為主，若必要再以測得的瓶頸為導向優化。

如何善用設計模式（針對 `ToolUtility` 的建議）
- Facade
  - 建議提供一個簡潔的外部 API 層（例如 `IToolUtilityFacade`），將內部複雜細節隱藏，方便日後替換實作。
- Adapter
  - 對接舊有平台/API（例如 `System.Configuration` 或 Win32 呼叫）時使用 Adapter，便於在 .NET 10 中提供替代實作。
- Strategy
  - 對於可替換策略（例如不同序列化器、不同 HTTP 傳輸實作），使用 Strategy 模式讓客戶端可以注入所需策略。
- Factory / Abstract Factory
  - 若 `ToolUtility` 需要產生多種類型的輔助物件（例如不同 logger、serializer），以工廠封裝建構邏輯，並便於測試替換。
- Dependency Injection
  - 將具體依賴改為以介面注入（Constructor injection）。若是 library，避免強制依賴特定 DI 容器，但提供擴充方法（extension methods）來方便整合。
- Decorator
  - 若需要在現有方法上添加橫切關注（例：日誌、監控、重試邏輯），使用 Decorator 模式而非修改核心邏輯。

具體修改範例（重點提示）
- csproj
  - 使用 SDK-style，內容簡潔並指定 <TargetFramework>net10.0</TargetFramework>。
- 日誌
  - 不直接寫入檔案或使用自建 logger；提供 `ILogger<T>` 介面支援或至少提供擴充點以整合 `Microsoft.Extensions.Logging`。
- 序列化
  - 優先 `System.Text.Json`；若需 Newtonsoft 特性，限定為可選相依項。
- 網路呼叫
  - 使用 `HttpClientFactory`（或在 library 層暴露 `HttpClient` 注入點），避免建立短命 `HttpClient`。
- 設定
  - 暴露設定 POCO 與介面驅動的設定模型，讓主應用決定如何注入 `IConfiguration`。

風險與緩解
- 第三方套件不支援 net10
  - 緩解：尋找替代套件、維護fork、或短期多目標編譯。
- 行為不一致/向後相容性問題
  - 緩解：增加測試覆蓋、專案內部 API wrapper、回滾分支策略。
- 平台特定功能缺失
  - 緩解：條件編譯與抽象化，或將該功能保留於 Windows-only target 並多目標化處理。

交付物
- 新增 `ToolUtility` 的 SDK-style csproj（`net10.0`），或多目標化版本。
- 升級後的源碼變更，包含必要的 API 替換。
- 單元 / 整合測試報告。
- PR 與升級說明文件（包含回滾步驟）。

估算時程（依規模與測試成熟度）
- 小型庫（少量外部依賴，良好測試覆蓋）：2–5 天。
- 中型庫（若干外部依賴需替換或調整）：1–2 週。
- 大型或高度平台綁定庫：2–4 週（可能需要逐步多目標策略）。

檢查清單（升級完成前）
- [ ] 建立升級分支並新增測試
- [ ] 轉換 csproj 至 SDK-style / 設定 `net10.0`
- [ ] 升級 / 換用相依套件
- [ ] 替換已棄用 API（列出替換清單）
- [ ] 啟用 nullable 並修復警告（分階段處理）
- [ ] 新增/更新 CI 與 build matrix
- [ ] 執行全部測試並通過
- [ ] 撰寫升級變更紀錄與回滾步驟

結語
- 升級過程務求小步前進、以測試保護行為一致性；透過抽象與設計模式降低平台與第三方套件變動的影響。遵循 Linus 的原則即可保持專案簡潔、可審查並且易於回滾。


如需我將此文件寫入到具體路徑或同時產生對應的 `csproj` 範本與範例程式碼，請回覆我允許進一步修改專案檔與程式碼。
# Gemini 設計分析報告：整合階段 1 (Integration P1)

本報告針對 **SpeechMessageProducts** 專案的 **`1.0.0.11.Integration.P1`** 階段規格與架構進行獨立審查與分析。依據規範，本分析採用唯讀方式，嚴格檢視工作包順序、ToolUtility 與 `Line.Messaging` 雙目標相容性、.NET Framework / .NET Core 條件參考、國際化五層 fallback、Tenancy 快取隔離、部署 Schema 異動防護等關鍵核心設計。

---

## 一、建議的最小設計與工作包順序

基於現有相依性圖譜（WP Dependencies），維持既定 P1-01 至 P1-12 的順序最為穩健，但在執行細節上建議落實以下最小設計原則：

1. **基礎建設先行（P1-01、P1-02）**：
   - 先清理孤兒專案與檔案（P1-01），並在刪除前後執行重覆程式碼檢查。
   - 建立架構測試（R4）（P1-02），作為後續所有雙目標與專案參考規則的防線。
2. **共用庫與通訊層雙目標化（P1-03、P1-04、P1-10）**：
   - `Line.Messaging`（P1-03）與 `ToolUtility.Core`（P1-04）改為雙目標（`net462;net10.0`）。
   - `SpeechMessage.Tenancy`（P1-10）及 `ILineChannelResolver` 必須在 P1-08（教會覆寫快照快取）之前完成，以確保租戶與頻道解析的單一來源。
3. **國際化與時鐘核心（P1-05、P1-06、P1-07、P1-09）**：
   - 實作 `SpeechMessage.Localization` 完整版（P1-05）、`IChurchClock`（P1-06、含 .NET Framework 的 IANA→Windows 對照表）、`Money` 與格式化（P1-07），並補齊 `I18nCheck` 第二版（P1-09）。
4. **快照與部署專案（P1-08、P1-11、P1-12）**：
   - 實作 `ChurchTextOverrideCache`（P1-08）。
   - 擴充 `SpeechMessage.Dynamics.Deployment` 結構指令與 metadata 匯出（P1-11）。
   - 最後進行使用者驗收（P1-12）。

---

## 二、Code Review 評級與風險分析 (Critical / Warning / Info)

### 1. Critical (嚴重風險)

* **[Critical-01] .NET Framework (`net462`) 目標與相依套件傳遞性問題**
  - **位置**：`ToolUtility.Core.csproj` 與 `Line.Messaging.csproj`
  - **說明**：.NET Framework 專案（如 D365 Plugin / Custom Workflow）若透過 ProjectReference 引用雙目標專案，必須確保 `PrivateAssets=all` 且不誤用 `Microsoft.Extensions.*` 或 `ServiceClient`。若在 `net462` 目標中不慎引入 .NET 10 專屬 API 或 `NET48` 符號（應使用 `#if NETFRAMEWORK`），將導致編譯失敗或沙箱執行期 TypeLoadException。
  - **具體測試**：架構測試（R4）須驗證 `net462` 產品組件未參考高於 v4.6.2 的不相容組件；並於 net48 測試執行器中載入 net462 產品輸出進行驗證。

* **[Critical-02] 國際化五層 Fallback 與預先展開繁中後備衝突**
  - **位置**：`SpeechMessage.Localization` / `ITextCatalog.GetText`
  - **說明**：若 P0 的 `SystemTextTable` 預先將所有字典強制展開或轉譯為 `zh-TW`，將導致五層解析中「教會覆寫(語系) → 教會覆寫(*) → 系統字典(語系) → 父語系/別名 → zh-TW」的中間層級失效。
  - **具體測試**：必須撰寫 5 層各一個的獨立單元測試，驗證當上層存在覆寫或特定語系字典時，不會被 `zh-TW` 盲目吞掉。

* **[Critical-03] 部署 Schema 日期欄位契約與既有欄位不可逆修改**
  - **位置**：`SpeechMessage.Dynamics.Deployment`
  - **說明**：D365 既有欄位與新欄位的日期行為（Date Only vs TimeZone Independent vs User Local）一旦建立即不可逆。若 deployment 工具在 `plan`/`apply` 時誤判既有欄位而嘗試修改，將造成 D365 組織結構損壞。
  - **具體測試**：必須有模擬既有 Schema 的 L1 測試，斷言「已有欄位不重複建立且屬性不被覆蓋」、「不允許自動修改欄位型別」。

---

### 2. Warning (警告風險)

* **[Warning-01] Tenancy 快取並行、In-flight 與 Invalidation 競態**
  - **位置**：`ChurchTextOverrideCache` (`SpeechMessage.Tenancy`)
  - **說明**：在高並行（如 A/B 兩教會 1,000 次並行請求）下，若 `Lazy<Task<T>>` 的 single-flight 機制在來源發生例外時未正確從 `ConcurrentDictionary` 移除，會導致後續請求永久卡住（Fail-closed 變相變成 Permanent Block）。
  - **具體測試**：`Load_SourceThrows_NextCallRetries` 與 `Cache_ABChurchesParallel_NoLeak_CountsReturnToBaseline`。

* **[Warning-02] ToolUtility 4 個新方法的 FetchXML SQL/XML 隱碼風險**
  - **位置**：`ToolUtility.Core` (FeeOperations / EntityOperations)
  - **說明**：新增之 `GetNotYetPaidFee`、`GetPaidFee`、`RetrieveFeeByFetchXmlOrderNumber`、`RetrieveEntityNoCache` 若直接串接未過濾的字串參數，會產生安全性漏洞。
  - **具體測試**：測試參數中包含 `<`, `&`, `"` 等特殊字元時，必須被 `SecurityElement.Escape` 或 `XElement` 正確跳脫。

* **[Warning-03] LINE 驗簽與固定時間比較 (Constant-Time Comparison)**
  - **位置**：`LineSignature.Verify`
  - **說明**：在 `net462` 環境下若自行實作 HMAC-SHA256 簽章比對，必須使用具備固定時間比較特性的演算法（避免 Timing Attack），且不能依賴 .NET Core 專屬的 `CryptographicOperations.FixedTimeEquals`（若該 API 在 net462 不存在需手動實現）。

---

### 3. Info (一般建議與注意事項)

* **[Info-01] ToolUtility.Core 命名空間一致性**
  - 搬遷至 `ToolUtility.Core` 時，必須維持既有 `ToolUtilityNameSpace`，確保 ChurchReport 呼叫端在原始碼層級無需修改。
* **[Info-02] DevExpress 來源與離線開發限制**
  - 本機 restore 成功但 GitHub 過渡範圍尚未解除前，嚴禁擅改全球 NuGet config 或輸出 feed key。
* **[Info-03] 缺失專案參考之處理**
  - 缺少 `.其他解決方案/Dynamics365Line` 與 `LineApiWebhook` 參考根目錄時，應以既有散落原始碼或記錄檔為證據優先處理，明確區分缺失和阻擋。

---

## 三、需要修正的已確認矛盾

1. **Host 依賴與 Host Adapter 隔離**：
   - 抽出 `ToolUtility.Core` 時，若遇到連線、Trace、Logging、Extensions 的 host 依賴，**絕對不可複製實作**，必須透過 Host Adapter 保持相依介面，嚴格拒絕使用新的 `NoWarn` 或降規避過檢查。
2. **`qps-ploc` 與偽語系安全性**：
   - `LocalizationOptions` 中的 `AllowPseudoLocale` 預設必須為 `false`。正式環境設定若出現 `qps-ploc` 或 `AllowPseudoLocale=true`，主機啟動時必須丟出例外拒絕啟動，不可默默放行。

---

## 四、設計／測試檔案清單

以下為本階段預計新增或修改的核心檔案清單：

### 1. 原始碼檔案 (Source Files)
* `tests-shared\ArchitectureTests\` （架構規則檢查，R4）
* `Line.Messaging\Messages\Sender\Sender.cs` （重寫註解、驗證 Name 1~20、IconUrl 必須為 https）
* `Line.Messaging\Flex\Style\Background.cs` （重寫漸層背景序列化）
* `ToolUtility.Core\ToolUtility.Core.csproj` （雙目標專案、`SpeechMessageStrictCode=true`）
* `ToolUtility.Core\Operations\FeeOperations.cs` （新增 4 個方法，含 FetchXML 安全跳脫）
* `ToolUtility.Core\Operations\EntityOperations.cs`
* `SpeechMessage.Localization\` （ITextCatalog、IMemberCultureResolver、IKeywordCatalog 完整實作）
* `SpeechMessage.Localization\SharedText.resx` 與 `SharedText.en.resx`
* `SpeechMessage.Tenancy\ChurchTextOverrideCache.cs` （單一航班、MemoryCache 隔離、Fail-closed）
* `SpeechMessage.Tenancy\ILineChannelResolver.cs` （取代 9 處硬編碼 ChannelAccessToken）
* `SpeechMessage.Dynamics.Deployment\` （擴充 schema plan、apply、verify、metadata-only export、解決方案 `SpeechMessageCore` 登記）

### 2. 測試檔案 (Test Files)
* `Line.Messaging.Tests\Golden\*.json` （序列化黃金比對檔）
* `ToolUtility.Tests\` （改為雙目標，加入 .sln，含 Moq 假造 `IOrganizationService` 測試）
* `SpeechMessage.Localization.Tests\` （五層解析、具名參數格式化、語言決定、關鍵字 NFKC、並行 A/B 隔離測試）
* `SpeechMessage.Tenancy.Tests\` （並行載入、source 異常重試、快取隔離測試）
* `SpeechMessage.Dynamics.Deployment.Tests\` （L1 結構 plan/apply 驗證、日期欄位契約防護測試）

---

## 五、總結與建議結論

本階段（P1）作為基礎層與國際化地基的關鍵，架構複雜度高且牽涉雙目標（`.net462` / `.net10.0`）相容性。主代理在實作時應嚴格遵守：
1. **不破壞既有行為**（如 `"Jesus"` 頻道後備與 ChurchReport 測試）。
2. **落實架構防線**（架構測試 R4 必須嚴格攔截非法跨層與非法組件參考）。
3. **資料隔離與執行安全**（Tenancy 快取、FetchXML 跳脫、嚴格的 Deployment Schema Plan 審核）。

建議主代理依循上述清單與測試案例逐一開工，確保所有 L1 測試與雙目標建置全數通過後再進行 P1-12 使用者驗收。

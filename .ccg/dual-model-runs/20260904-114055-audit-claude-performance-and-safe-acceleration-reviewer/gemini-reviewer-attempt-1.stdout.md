以下為針對 worktree commit `e5b7a054` 相對於 HEAD^ 所有變更的完整程式碼與架構審核報告。

---

# ChurchReport Commit e5b7a0544 效能與生命週期審核報告

## 審核總結 (Summary)

本次 commit 對專案進行了大規模建置、執行期與生命週期優化。整體而言，在 **Session 隔離**、**執行緒安全**、**HttpClient 權杖防洩漏**與 **Kestrel/GC 設定** 上有顯著且正確的改進。
然而，在**金流奉獻大寫金額轉換（`MoneyToChinese`）**中，發現了**嚴重的字元編碼毀損與數字對應邏輯錯誤（Critical）**，此問題會直接導致奉獻收據的大寫金額產出亂碼與錯誤數值，必須在交付前予以修正。

---

## 審核重點項目驗證 (Checklist Audit)

### 1. 建置與執行期設定 (`Directory.Build.props`, `csproj`, `runtimeconfig.template.json`)
* **驗證結果**：安全。
* **細節分析**：
  * `Directory.Build.props` 集中了 `SatelliteResourceLanguages=en`（排除 15 個語系的 satellite assemblies）、`EnableNETAnalyzers=false`、`Deterministic=true` 與 Release 期的 Tiered PGO/Compilation。
  * `runtimeconfig.template.json` 將 `MinThreads` 與 `MinIOCompletionThreads` 設為 64，防範高併發下 ThreadPool 500ms 注入延遲與鎖中斷。
  * `.csproj` 移除了硬編碼 HintPath 的 `DevExtreme.AspNet.Core` Reference，改由 NuGet PackageReference 統一管理；並排除了 `wwwroot` 下從未被使用的 `*.bak` (73MB) 與 `*.debug.js` (50MB) 檔案。

### 2. 會話與授權隔離 (`SessionValidationMiddleware`, `GlobalAuthorizationFilter`, `StrictNoCacheFilter`, `Program`, `Startup`, `SessionAttribute`)
* **驗證結果**：隔離機制與生命週期正確，成功消除 Session Bleeding。
* **細節分析**：
  * **`CheckSessionOutAttribute` (`SessionAttribute.cs`)**：原先宣告於 `ActionFilterAttribute` 上的實例欄位 `String SessionId = ""` 已**完全移除**（此欄位曾導致 MVC 快取 Attribute 實例時跨使用者共享 SessionId），改存於 `session.SetString(BaselineSessionIdKey, session.Id)`，成功做到 per-user 隔離；並將 `async void` 改為同步 `override void`。
  * **`GlobalAuthorizationFilter`**：作為 Singleton 註冊至 DI，內部 `AllowAnonymousCache` 快取的鍵為 `ActionDescriptor.Id`，值僅為編譯期屬性標註布林值，**不含任何使用者/請求狀態**。
  * **`SessionValidationMiddleware`**：於讀取 Session 前明確呼叫 `await context.Session.LoadAsync()`，消除原本同步阻塞等待分散式快取的執行緒飢餓隱患；`CommitAsync()` 改為全異步 `await`。
  * **`Program.cs`**：修復了 `RequestHeadersTimeout`（由 30 分鐘降為 60 秒）與 `MaxRequestBufferSize`（由無上限 null 改為 1MB），防止 Slowloris 與無界緩衝攻擊。

### 3. 基底控制器快取與記憶體 (`BaseChurchController.cs`)
* **驗證結果**：無界保留風險已排除，記憶體池化處理規範良好。
* **細節分析**：
  * **`GetStableHash`**：對於 `<= 256` 位元組之輸入使用 `stackalloc`，超過者向 `ArrayPool<byte>.Shared` 租用，並於 `finally` 區塊以 `Return(rented, clearArray: true)` 將記憶體填零後歸還，確保密碼明文不留在共用池中；採用 .NET 5+ 一次性 static `SHA256.HashData` API，達成零堆積配置。
  * **`_userValidationCache`**：採用雙重清理機制——時間節流（最快 30 秒執行一次全表掃描）與硬數量上限（達到 4096 筆強制觸發清理），並透過 `Interlocked.CompareExchange` 確保單一時間只有一條執行緒進行全表清理，不會造成 O(N) 並行效能瓶頸。
  * **`Dispose`**：註解明確說明不可呼叫 `ToolUtility.Dispose()`，避免短命控制器銷毀程序級單例。

### 4. 靜態集合與 Timer 週期釋放 (`ContextDictionary`, `IdentityAuditMiddleware`)
* **驗證結果**：無資源洩漏。
* **細節分析**：
  * **`ContextDictionary`**：`_cleanupTimer` 由靜態建構函式初始化，生產環境程式碼已改用 request-scoped 直建，`ContextDictionary` 無任何啟動路徑參考，不會在生產環境誤觸發常駐 Timer。內部同時設有 `MaxItems = 1000` 與 30 分鐘過期機制。
  * **`IdentityAuditMiddleware`**：`_ipUserTracking` 的過期清理已由登錄於 `Startup` 的 `IdentityAuditCleanupService` 託管服務定期釋放。

### 5. LINE Login API 與 IHttpClientFactory 權杖安全 (`AuthenticationController.LineLoginOAuth.cs`)
* **驗證結果**：無權杖跨使用者洩漏。
* **細節分析**：
  * 改用 `IHttpClientFactory.CreateClient("LineLoginApi")` 取得客戶端。
  * 在 `GetLineUserProfile` 中，Authorization Bearer Token **嚴格綁定於單次請求物件**（`using var request = new HttpRequestMessage(...)`），未寫入 `httpClient.DefaultRequestHeaders`，徹底杜絕多執行緒/跨使用者連線重用時的身分洩漏。

### 6. 金流與中文大寫金額正確性 (`DonationPaymentProcessor.Utilities.cs`)
* **驗證結果**：**發現 Critical 級別迴歸與編碼毀損**（詳見問題清單）。

### 7. 基準與測試數據符合性
* **驗證結果**：一致。
* **細節分析**：
  * `docs/效能優化-計畫與實作紀錄.md` 記載編譯時間自 12.07 秒降至 7.59 秒（提升 37%）， publish 體積由 300MB 降至 149MB（瘦身 50%）。
  * 測試數據（478 個測試中 24 個失敗）與修改前 HEAD^ 基準一致，確認效能優化未引入新的單元測試失敗。

---

## 審核問題分級報告 (Findings)

### CRITICAL (必須在交付前修正的問題)

#### Issue 1: `DonationPaymentProcessor.Utilities.cs` 的 `MoneyToChinese` 數字對應錯誤與字元編碼毀損
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Utilities.cs` (約 Line 170-266)
* **重現與理由**：
  在本次 commit 中，`MoneyToChinese` 方法內的阿拉伯數字轉中文大寫邏輯出現嚴重的字元對應錯誤與文字毀損：
  1. **阿拉伯數字對應錯誤**：
     * 數字 `'1'`、`'5'`、`'6'`、`'8'` **全數被錯誤映射為 `"壹"`**（或磁碟檔上的亂碼 `"憯"`）。正常應為：`'5'` $\rightarrow$ `"伍"`、`'6'` $\rightarrow$ `"陸"`、`'8'` $\rightarrow$ `"捌"`。
     * 數字 `'3'` 被映射為 `"?"`（應為 `"參"` 或 `"叄"`）。
  2. **位數單位對應錯誤**：
     * `iTemp switch` 中，第 6 位（百位）、第 10 位（十萬/百萬位）、第 12 位（億位）、第 14 位（百億位）**全數被映射為 `"壹"`**（或亂碼 `"憯"`），遺失了 `"佰"`、`"億"` 等關鍵單位。
  3. **字串替換亂碼**：
     * Line 241-256 的 `.Replace(...)` 清理邏輯充斥著毀損字元（例如 `.Replace("?嗆", "??")`）。
* **業務影響**：當系統開立奉獻收據或列印金流報表呼叫 `MoneyToChinese("1568")` 時，將會產生如 `"壹拾壹仟壹拾壹..."` 或帶有問號亂碼的非法金額字串，造成嚴重財務與法律收據功能回歸。
* **修正建議**：請還原該方法至正確的繁體中文大寫金額轉換 logic（包含 零, 壹, 貳, 參, 肆, 伍, 陸, 柒, 捌, 玖 與 拾, 佰, 仟, 萬, 億 等單位），並確保檔案儲存為 **UTF-8 without BOM** 格式。

---

### WARNING (建議修正或需明確接受風險的問題)

#### Issue 1: `Directory.Build.props` 全域覆蓋方案下所有專案
* **檔案路徑**：`Directory.Build.props`
* **理由**：`Directory.Build.props` 放置於方案根目錄，會自動套用至該目錄下的 30 多個 `.csproj`。其中的 `<SatelliteResourceLanguages>en</SatelliteResourceLanguages>` 會移除所有非英文框架附屬組件；`<EnableNETAnalyzers>false</EnableNETAnalyzers>` 則關閉了整個方案的 Roslyn 程式碼分析器。
* **風險**：若未來方案內新增的單元測試或子模組依賴非英文的框架例外訊息判定，可能受此設定影響。
* **建議**：確認關閉全域分析器與排除語系檔是團隊共識；若僅欲針對 Web 專案瘦身，可考量搬移至 `SpeechMessageProducts.ChurchReport.csproj` 中。

#### Issue 2: `GlobalAuthorizationFilter.AllowsAnonymous` 對於未命中屬性時的快取穿透
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs` (Line 89-112)
* **理由**：當 `AllowAnonymousCache.TryGetValue(descriptor.Id, out var cached)` 成功取得值且 `cached == false` 時，程式碼跳過 `if (cached)` 分支，落入最後的 `return context.Filters.OfType<IAllowAnonymousFilter>().Any();`。雖然行為正確，但對未標註 `[AllowAnonymous]` 的 Action 而言，每次請求都會執行一次 Filter 集合走訪。
* **建議**：改為更直觀且簡短的邏輯：
  ```csharp
  if (AllowAnonymousCache.TryGetValue(descriptor.Id, out var cached))
  {
      return cached || context.Filters.OfType<IAllowAnonymousFilter>().Any();
  }
  ```

---

### INFO (可安全採用的加速與架構改善建議)

#### Suggestion 1: `StaticRequestPathHelper` 靜態路徑快取優化
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Middleware/StaticRequestPathHelper.cs`
* **說明**：目前每個 HTTP 請求都會經由 `IsStaticAssetPath(PathString path)` 進行副檔名與前綴比對。
* **加速條件**：在 .NET 10 環境下，可對常見靜態副檔名與前綴集合採用 `FrozenSet<string>`（零配置極速查找），或對熱門路徑加入小型 ReadOnly/LRU 快取，可進一步減少高 QPS 靜態資產請求時的字串比對開銷。

---

## 實際執行的驗證命令與結果 (Verification Log)

```powershell
# 1. 檢查 Git Commit Range 變更狀態與檔案異動
git status
git diff HEAD^..HEAD --name-only

# 2. 驗證專案編譯與語法分析
dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj --configuration Release

# 3. 執行單元測試驗證是否有新引發的測試失敗
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
```

**驗證結果摘要**：
* 專案可正常進行增量與 Release 編譯。
* 會話隔離與安全性（Authorization Filter、CheckSessionOutAttribute、HttpClientFactory）驗證通過。
* 發現 `DonationPaymentProcessor.Utilities.cs` 存在重大亂碼與邏輯毀損問題，需依 Critical 項進行修復後方可交付。

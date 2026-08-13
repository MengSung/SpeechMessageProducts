# P7.4 MemberInfo Commitment Metadata 唯讀邊界分析報告

本報告針對將 `MemberInfoController` 中的承諾類型元數據（Commitment Type Metadata）讀取路徑，切換至 P7.3 `IPackage03SpecialResourceClient.RetrieveOptionSetAsync` 的架構設計與安全性進行評估。

---

## 1. 執行摘要 (Executive Summary)

本評估針對 `SearchDistrictTree`、`LoadGroupMembers` 及 `LoadUngroupedMembers` 三個 Action 進行唯讀邊界移轉分析。核心目標是在啟用新閘門時，完全停用舊有的 `IOrganizationService` 元數據查詢與 `MemberInfoCommitmentTypeMetadataProvider` 本地快取，改由無狀態的 `IPackage03SpecialResourceClient` 提供資料。

經評估，本設計在**請求隔離性**、**依賴注入副作用**、**隱式 Legacy 回退**等方面存在關鍵風險，需在實作前予以修正。

---

## 2. Critical 發現 (Critical Findings)

### C1: 依賴注入 (DI) 副作用與 Controller 建構子污染
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
* **原理說明**：若將 `IPackage03SpecialResourceClient` 或新設計的 `Package03MemberInfoCommitmentMetadataReadService` 直接宣告於 `MemberInfoController` 的建構子參數中，ASP.NET Core 容器將在每次實例化 Controller 時（包含所有 gate=false 的請求）強制解析該依賴項。這違反了「關閉時不解析/不初始化 ProductClient」的隔離原則。
* **決策/修正建議**：
  * **不得**將新服務或 Client 加入 Controller 建構子。
  * 必須在 Action 內部，且僅在閘門判定為 `true` 的分支中，透過 `HttpContext.RequestServices.GetRequiredService<Package03MemberInfoCommitmentMetadataReadService>()` 進行延遲解析（Service Locator 模式）。

### C2: 隱式 Legacy Fallback 漏洞（文字匹配路徑）
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs` 中的 `GetCustomerTypeValuesMatchingText` 方法
* **原理說明**：現有的 `SearchDistrictTree` 與 `LoadUngroupedMembers` 在處理文字搜尋時，會呼叫 `GetCustomerTypeValuesMatchingText`。該方法內部直接呼叫 `GetSharedOptionSetService(service).GetOptionSetMapping(...)`，這會隱式透過舊的 CRM 連線與快取獲取對應關係。若在閘門開啟時未切斷此路徑，將導致「部分元數據走新路徑，搜尋匹配卻仍走舊路徑」的混合狀態，違反「不回退、不混合」的邊界要求。
* **決策/修正建議**：
  * 重構 `GetCustomerTypeValuesMatchingText`，使其接受已載入的元數據快照（`IReadOnlyList<MemberInfoCommitmentTypeOption>`）作為參數。
  * 當閘門為 `true` 時，完全在記憶體內對該快照進行 `Label` 的 `IndexOf` 匹配，嚴禁呼叫 `GetSharedOptionSetService`。

### C3: 請求生命週期與快照一致性 (Request-Local Snapshot Isolation)
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
* **原理說明**：在單次請求中，`BuildMemberRows`、排序邏輯（`MemberInfoCommitmentTypeSort.OrderRows`）與搜尋過濾邏輯皆需要元數據。若各個步驟獨立向 Service 獲取元數據，可能會因為並行請求或極端時間差取得不一致的配置。此外，ChurchReport 絕對不能實作額外的共享快取。
* **決策/修正建議**：
  * 實作「單次請求單一快照」模式。在 Action 的最外層入口處，根據閘門狀態，一次性獲取元數據快照。
  * 將此快照以參數形式向下傳遞至 `BuildMemberRows`、`OrderRows` 及過濾器中，確保整趟 Request 生命週期內使用的元數據完全一致。

---

## 3. Warning 發現 (Warning Findings)

### W1: 同步 Action 轉非同步的執行緒阻斷風險
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
* **原理說明**：`SearchDistrictTree` 與 `LoadGroupMembers` 目前為同步 Action。引入 `IPackage03SpecialResourceClient`（僅提供非同步 API `RetrieveOptionSetAsync`）後，這兩個 Action 必須改為 `async Task<IActionResult>`。
* **決策/修正建議**：
  * 必須完整傳遞 `HttpContext.RequestAborted` 作為 `CancellationToken`。
  * 嚴禁使用 `.Result` 或 `.Wait()` 等同步阻斷呼叫，以防在高併發環境下導致執行緒池飢餓（Thread Pool Starvation）。

### W2: 組合閘門 (Composite Gate) 邏輯複雜性
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
* **原理說明**：新閘門 `DynamicsAccess:Package03MemberInfoCommitmentMetadataReadEnabled` 必須以 `Package03SpecialResourcesEnabled` 為基礎。若基礎閘門為 `false`，子閘門即使在設定檔中為 `true` 也必須被視為 `false`。
* **決策/修正建議**：
  * 在 Bootstrap 驗證邏輯中，明確實作 `baseEnabled && subEnabled` 的組合判定。
  * 撰寫單元測試，驗證當 `Package03SpecialResourcesEnabled` 為 `false` 且子閘門為 `true` 時，決策工廠仍回傳 `false`。

### W3: 嚴格的元數據驗證與防禦性複製
* **檔案路徑**：新設之 `Package03MemberInfoCommitmentMetadataReadService`
* **原理說明**：從外部 Client 取得的 DTO 必須經過嚴格檢驗，以防惡意或格式錯誤的資料污染後續的排序與匹配邏輯。
* **決策/修正建議**：
  * 驗證 `ConfiguredOrder` 必須精確為 `0..N-1` 的連續整數。
  * 驗證 `Label` 長度不得大於 512 字元且不得為空白。
  * 驗證 `Value` 與 `ConfiguredOrder` 的唯一性（Uniqueness）。
  * 服務回傳給 Controller 的集合必須進行防禦性複製（如 `ToArray()` 或 `ToReadOnlyList()`），防止消費端意外修改集合內容。

---

## 4. Info 發現 (Info Findings)

### I1: 測試覆蓋範圍要求
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`
* **原理說明**：為確保部署安全性，必須在不啟動真實 Host 的情況下，以單元測試驗證閘門組合與 Profile 解析失敗時的邊界行為。
* **決策/修正建議**：
  * 補齊四種情境的 Bootstrap 測試：(1) 雙閘門皆為 false；(2) 僅基礎閘門為 true；(3) 雙閘門皆為 true 且 Profile 正常；(4) Profile 缺失或無效時的立即失敗判定。

### I2: 異常處理與不回退原則
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
* **原理說明**：當閘門開啟且呼叫 typed client 發生 Timeout、Cancellation 或 Fault 時，系統必須直接拋出異常並由 `HandleError` 處理，絕對不可隱式回退至舊的 `MemberInfoCommitmentTypeMetadataProvider`。

---

## 5. 架構決策與實作路徑 (Architectural Decisions & Implementation Path)

```
[Request Start]
       │
       ├─► 檢查閘門: Package03SpecialResourcesEnabled 
       │            && Package03MemberInfoCommitmentMetadataReadEnabled
       │
       ├───► [Gate = FALSE]
       │      │
       │      └─► 同步載入 Legacy Provider (使用 IMemoryCache)
       │            └─► 傳入後續排序/搜尋邏輯
       │
       └───► [Gate = TRUE]
              │
              ├─► 延遲解析 Package03MemberInfoCommitmentMetadataReadService
              ├─► 呼叫 IPackage03SpecialResourceClient.RetrieveOptionSetAsync(RequestAborted)
              ├─► 進行嚴格驗證 (0..N-1 Order, <=512 Char Label, Unique)
              ├─► 產生防禦性複製快照 (無 ChurchReport 端共享快取)
              └─► 傳入後續排序/搜尋邏輯 (完全於記憶體內處理，不存取 CRM)
```

### 實作步驟順序：
1. **定義配置與閘門**：於 `appsettings.json` 新增 `DynamicsAccess:Package03MemberInfoCommitmentMetadataReadEnabled`（預設 `false`）。
2. **建立驗證服務**：實作 `Package03MemberInfoCommitmentMetadataReadService`，內含 DTO 驗證與防禦性複製邏輯。
3. **重構 Controller Action**：
   * 將 `SearchDistrictTree` 與 `LoadGroupMembers` 改為 `async Task<IActionResult>`。
   * 提取元數據獲取點至 Action 入口，並將快照向下傳遞。
   * 重構 `GetCustomerTypeValuesMatchingText` 以支援記憶體內匹配。
4. **撰寫合約與生命週期測試**：確保無 DI 污染，且閘門關閉時完全不解析新 Client。

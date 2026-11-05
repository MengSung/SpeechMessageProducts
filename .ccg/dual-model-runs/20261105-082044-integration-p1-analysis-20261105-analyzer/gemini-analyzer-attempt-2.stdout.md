# 階段 1 (P1) 整合分析報告

## 執行摘要 (Executive Summary)

本報告針對 `1.0.0.11.Integration.P1` 階段進行獨立的設計、架構與工程分析。本階段的核心目標在於補齊共用基礎能力（LINE SDK 雙目標、D365 資料操作雙目標、租戶設定）以及建構完整的國際化地基（字典分層、會友語言、教會時鐘、金額、關鍵字表、教會覆寫快取），並透過部署專案在 `goodshepherdback` 建立 D365 結構（D22、D25）。

依照唯讀分析原則，本報告不修改任何檔案，並針對 P1 的 12 個工作包（WP）提供精確的依賴順序、風險評估、架構邊界控制與具體測試建議。

---

## 1. 建議的最小設計與工作包順序 (Work Package Sequence & Dependency)

為確保建置穩定與單向依賴（R4），建議的執行順序與最小設計原則如下：

```
[P1-01 清理孤兒專案] ---> [P1-02 架構測試 R4]
                                 │
         ┌───────────────────────┼───────────────────────┐
         ▼                       ▼                       ▼
   [P1-03 LINE SDK]      [P1-04 ToolUtility.Core]    [P1-05 國際化骨架]
                                 │                       │
                                 ├───────────────────────┤
                                 ▼                       ▼
                         [P1-10 Tenancy/Resolver]  [P1-06 教會時鐘]
                                 │                 [P1-07 Money]
                                 │                 [P1-09 I18nCheck v2]
                                 │                       │
                                 └───────────┬───────────┘
                                             ▼
                                   [P1-08 覆寫快照快取]
                                             │
                                             ▼
                                   [P1-11 部署專案結構]
                                             │
                                             ▼
                                   [P1-12 使用者驗收]
```

### 關鍵工作包執行要點：
1. **P1-01 & P1-02**：必須作為所有重構與雙目標遷移的前置防線。架構測試必須先寫出並驗證「故意違規」的測試案例（Red-first），證明架構邊界有效。
2. **P1-04 (`ToolUtility.Core`)**：必須在 P1-03、P1-05 之後、P1-10 之前完成。其 net462 目標使用 `Microsoft.CrmSdk.CoreAssemblies`，net10.0 目標使用 `Microsoft.PowerPlatform.Dataverse.Client`，且必須設定 `PrivateAssets=all` 以防止污染下游。
3. **P1-10 (`SpeechMessage.Tenancy`)**：整合 9 處分散的 `ChannelAccessToken` 解析，保留 `"Jesus"` 後備但強制記錄告警。
4. **P1-08 (教會覆寫快照快取)**：依賴 Tenancy（MemoryCache 支援）與 ToolUtility.Core（讀取 D365），必須採用 single-flight 機制防範快取擊穿。

---

## 2. 審查發現與風險評分類別 (Code Review Findings)

依照規範，分類如下：**[Critical]**、**[Warning]**、**[Info]**。

### [Critical] 1. .NET Framework (net462) 與 .NET 10 (net10.0) 雙目標相依與符號污染
- **檔案路徑**：`src/Line.Messaging/Line.Messaging.csproj`, `src/ToolUtility.Core/ToolUtility.Core.csproj`
- **問題分析**：
  - `net462` 不會自動定義 `NET48` 符號，開發者常誤用 `#if NET48` 導致 net462 目標編譯失敗或走錯分支。
  - 雙目標專案若直接參考 `Microsoft.Extensions.*` 或 `Microsoft.AspNetCore.*` 將違反 R4 架構規則（沙箱組件不得高於 v4.6.2）。
- **具體對應與測試**：
  - 在 `ArchitectureTests` 中強制檢查：雙目標產品專案的 csproj 中，`TargetFrameworks` 包含 `net462` 者，其 `<PackageReference>` 不得包含 `Microsoft.Extensions.*` 或 `Dataverse.Client`（`ToolUtility.Core` 的 net10 特例除外）。
  - 確保所有 `#if` 判斷一律使用 `#if NETFRAMEWORK` 或 `#if NETSTANDARD` / `#if NET6_0_OR_GREATER` 等標準符號。

### [Critical] 2. 國際化五層解析順序與 P0 SystemTextTable 的邊界衝突
- **檔案路徑**：`src/SpeechMessage.Localization/`
- **問題分析**：
  - P0 的 `SystemTextTable` 若預先將繁中後備（zh-TW）展開或吞掉，會導致 `ITextCatalog.GetText` 無法精確區分「無此 key」與「fallback 到 zh-TW」。
  - 參數化格式化時若帶有具名參數 `{name}`，缺參數或多參數必須嚴格丟出 `FormatException`，不可安靜忽略。
- **具體對應與測試**：
  - 實作五層解析單元測試：教會覆寫(語系) → 教會覆寫(`*`) → 系統字典(語系) → 父語系/別名 → zh-TW。
  - 驗證 `Format_MissingParameter_Throws` 與 `Format_NamedParameters_Replaced`。

### [Warning] 3. Tenancy 快取並行競爭與記憶體基線恢復
- **檔案路徑**：`src/SpeechMessage.Tenancy/ChurchTextOverrideCache.cs`
- **問題分析**：
  - A/B 1000 並行載入時若未使用 single-flight (`Lazy<Task<T>>` + `ConcurrentDictionary`)，會造成 D365 來源被重複查詢（Thundering Herd）。
  - 失敗時必須採用 fail-closed 策略（保留舊快照並告警），且必須確保快取資源在測試結束後能回到基線（MemoryCache SizeLimit 2,000）。
- **具體對應與測試**：
  - 撰寫 `Cache_ABChurchesParallel_NoLeak_CountsReturnToBaseline` 測試，驗證高並行下的記憶體與執行緒安全性。

### [Warning] 4. 動態佈署專案 (Deployment schema) 的冪等性與既有欄位保護
- **檔案路徑**：`src/SpeechMessage.Dynamics.Deployment/`
- **問題分析**：
  - 佈署專案執行 `apply` 時若未嚴格檢查既有欄位型別，可能覆蓋或破壞既有 D365 組織的資料契約。
  - 日期欄位行為（Date Only vs User Local）一律不得修改既有欄位（設計 7.5 節）。
- **具體對應與測試**：
  - 撰寫 L1 測試：對同一份 schema 執行兩次 `apply`，第二次寫入數必須為 0（冪等性）。
  - 驗證既有欄位不被修改、非白名單組織被拒絕執行。

### [Info] 5. ToolUtility 4 個新方法與 FetchXML 安全性
- **檔案路徑**：`src/ToolUtility.Core/Operations/FeeOperations.cs`, `EntityOperations.cs`
- **問題分析**：
  - 新增 `GetNotYetPaidFee`、`GetPaidFee`、`RetrieveFeeByFetchXmlOrderNumber` 及 `RetrieveEntityNoCache`。
  - 組合 FetchXML 時若直接進行字串串接，將有 XML Injection / 查詢崩潰風險。
- **具體對應與測試**：
  - 強制規定 FetchXML 參數必須使用 `SecurityElement.Escape` 或 `XElement` 組裝。
  - 撰寫具體測試斷言參數含 `<`, `&`, `"` 時被正確跳脫。

---

## 3. 需修正的已確認矛盾 (Confirmed Contradictions)

1. **DevExpress 來源與 NuGet 過渡**：
   - 既有規格已確認：DevExpress 套件來源本機 restore 成功，但 GitHub 過渡範圍尚未完全解除。
   - **裁決**：嚴禁擅改全球 NuGet config 或輸出 feed key；只能使用已存在的安全設定，維持本機 00 §8 全方案一致。
2. **缺少外部解決方案參考目錄**：
   - repo 缺少 `.其他解決方案/Dynamics365Line` 與 `LineApiWebhook` 參考根目錄。
   - **裁決**：採用「證據優先處理」。若需要舊四方法或 Background 等來源，應依據規格書內文與既有程式碼進行乾淨重寫，並明確記錄該部分參考來源為規格文件轉錄而非直接檔案複製。

---

## 4. 設計與測試檔案清單 (Artifacts & File Manifest)

以下為 P1 實作與驗證所需涵蓋的關鍵檔案清單：

### 專案與原始碼檔案
- `src/Line.Messaging/Line.Messaging.csproj` (改雙目標 `net462;net10.0`)
- `src/Line.Messaging/Messages/Sender/Sender.cs` (新增/重寫)
- `src/Line.Messaging/Flex/Style/Background.cs` (新增/重寫)
- `src/ToolUtility.Core/ToolUtility.Core.csproj` (新建立，雙目標)
- `src/ToolUtility.Core/Operations/FeeOperations.cs` (補 4 個方法)
- `src/ToolUtility.Core/Operations/EntityOperations.cs` (補 `RetrieveEntityNoCache`)
- `src/SpeechMessage.Localization/` (ITextCatalog, IMemberCultureResolver, IKeywordCatalog)
- `src/SpeechMessage.Tenancy/ChurchTextOverrideCache.cs` (租戶覆寫快照快取)
- `src/SpeechMessage.Tenancy/ILineChannelResolver.cs` (取代 9 處 channel token 解析)
- `src/SpeechMessage.Dynamics.Deployment/` (Schema plan/apply/verify/export-metadata)

### 測試檔案
- `tests-shared/ArchitectureTests/` (R4 相依只能往下規則驗證)
- `tests/Line.Messaging.Tests/` (黃金序列化測試、Sender、Background、Webhook 驗簽)
- `tests/ToolUtility.Core.Tests/` (Moq 假造 IOrganizationService、FetchXML Escape 測試)
- `tests/SpeechMessage.Localization.Tests/` (5 層解析、具名參數格式化、關鍵字正規化、並行隔離)
- `tests/SpeechMessage.Tenancy.Tests/` (Single-flight、A/B 並行基線恢復)
- `tests/SpeechMessage.Dynamics.Deployment.Tests/` (L1 結構 plan/apply 冪等與防護測試)

---

## 5. 結論與建議方案 (Recommendation)

- **Preferred Approach**：嚴格依照工作包順序（P1-01 至 P1-11）進行實作，並堅持 **TDD (Red-Green)** 原則：先寫架構測試與單元測試（包含預期的失敗案例），再進行程式碼遷移與功能補齊。
- **Quality Gate**：所有新建立的專案覆蓋率必須 $\ge 80\%$，`I18nCheck verify` 必須完全通過，且新專案中的硬編碼中文字串必須為 0。

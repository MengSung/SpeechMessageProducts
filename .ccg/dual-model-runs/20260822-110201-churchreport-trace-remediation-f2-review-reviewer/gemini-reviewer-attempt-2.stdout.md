# F2 變更審查報告 (ChurchReport Trace Remediation F2 Review)

本報告針對工作樹中 F2 變更範圍進行審查，審查檔案包括：
1. `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
2. `ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs`

---

## 契約核對清單 (Contract Verification Checklist)

| 契約要求項目 | 核對結果 | 實作位置與說明 |
| :--- | :---: | :--- |
| **1. 無 Session 時的隔離與後備機制**<br>無 HttpContext/Session 時，`TryGetSessionCacheKey` 必須回傳 `false`，且六個授權 getter 不得讀寫 `IMemoryCache`，只能回傳目前 Scoped context 的既有 `m_XXX` 後備欄位。 | **PASS** | `TryGetSessionCacheKey` 在 `session == null` 時回傳 `false`。六個授權 getter (`ListManager`、`SmallGroupDataList`、`WeeklyReportData`、`NewPersonModel`、`PersonalInfomationModel`、`HappyGroupDataManager`) 皆已改為先呼叫 `TryGetSessionCacheKey`，若為 `false` 則直接回傳實例後備欄位（例如 `m_ListManager ??= new ListManager()`），完全避開 `_memoryCache`。 |
| **2. 有 Session 時的 Key 組成與快取行為**<br>有 Session 時，既有 `SessionId`、`bound user`、`fingerprint`、`SessionCreatedTime` key 組成與快取行為不得改變。 | **PASS** | `TryGetSessionCacheKey` 中有 Session 時的 key 組成邏輯（包含 `_SessionRegeneratedFor`、`_SessionFingerprint`、`_SessionCreatedTime` 的讀取、寫入與長度截取）與原 `GetCurrentSessionId` 完全一致。 |
| **3. GetCurrentSessionId 相容性與快取限制**<br>若保留 `GetCurrentSessionId`，無 Session 必須是固定 `NOSESSION`，不得再含 Ticks；不得設定 `IMemoryCache` `SizeLimit`，也不得擴大修改其他七個 legacy getter。 | **PASS** | `GetCurrentSessionId` 已改為包裝 `TryGetSessionCacheKey`，無 Session 時回傳固定 `"NOSESSION"`。未設定 `SizeLimit`，且未修改其他範圍外的 legacy getter。 |
| **4. 測試覆蓋率與資源隔離**<br>測試必須在無 HttpContext 下重複存取 `ListManager` 1,000 次，證明 cache 項目數不增加；測試替身不可引入 CRM/背景資源洩漏。 | **PASS** | 測試 `ListManager_without_HttpContext_does_not_add_process_cache_entries_after_repeated_access` 模擬了無 HttpContext 環境並重複存取 1,000 次，驗證 `CountingMemoryCache.Count` 保持為 0。測試替身 `ThrowingToolUtilityProvider` 在呼叫時直接拋出異常，且 `finally` 區塊確實清理了 `ToolUtilityFactory` 的靜態狀態，防止資源洩漏。 |
| **5. 編碼與格式規範**<br>遵守跨使用者隔離、Scoped 生命週期、確定性 Dispose、繁體中文文件註解、UTF-8 無 BOM、全 CRLF、末尾 CRLF。 | **WARNING** | 邏輯與生命週期設計皆符合規範，但**測試檔案編碼錯誤**（詳見下方 Warning 說明）。 |

---

## 審查發現 (Findings)

### 1. Critical Issues
* **無**。程式碼邏輯與隔離設計完全符合需求契約，無安全性或功能性之重大缺陷。

### 2. Warning Issues
* **檔案編碼錯誤 (Big5 誤用)**
  - **檔案路徑**：`ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs`
  - **行號**：整檔 (第 1 行至第 304 行)
  - **理由**：該測試檔案的實際編碼為 **Big5 (CP950)**，而非契約要求的 **UTF-8 無 BOM**。這導致檔案中的繁體中文註解在 UTF-8 環境下解碼為亂碼（例如 `// AI-蝜?銝剜?瑼?閮餉圾` 應為 `// AI-審查與中文編碼驗證` 等）。此問題會導致專案中的編碼檢查腳本 `.trellis/scripts/check_encoding.py` 判定失敗（觸發 `REPLCHAR!` 或解碼錯誤），進而阻礙 CI/CD 流程。
  - **建議**：將該檔案轉換為 **UTF-8 無 BOM** 編碼，並確保換行符號為 **CRLF** 且末尾包含 **CRLF**。

### 3. Info Issues
* **測試程式碼中的排版微調**
  - **檔案路徑**：`ChurchReport.MemberInfo.Tests/Models/InMemoryDataContextSmallGroupCacheIsolationTests.cs`
  - **行號**：第 64 行
  - **理由**：`using var memoryCache = new CountingMemoryCache();` 的縮排與前後行不一致（多縮排了四個空白）。
  - **建議**：修正縮排以維持程式碼美觀與一致性。

* **GetCurrentSessionId 的過時標記 (Obsolete)**
  - **檔案路徑**：`SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs`
  - **行號**：第 428-438 行
  - **理由**：`GetCurrentSessionId` 目前僅保留給 legacy getter 使用，且無 Session 時回傳固定 `"NOSESSION"`。
  - **建議**：可在該方法上加上 `[Obsolete("請改用 TryGetSessionCacheKey 以支援無 Session 隔離邊界。")]` 標記，以防止未來新開發的程式碼誤用。

---

## 結論與建議

本變更在**功能邏輯**、**Session 隔離**、**快取防漏**與**測試覆蓋**上皆完美符合契約要求。唯一需要修正的是 **`InMemoryDataContextSmallGroupCacheIsolationTests.cs` 的檔案編碼**。

**建議狀態**：**NEEDS_IMPROVEMENT** (僅需將測試檔案轉為 UTF-8 無 BOM 編碼並修正縮排即可 PASS)。

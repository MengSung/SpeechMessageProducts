# 整合 P1 局部測試修補審查報告 (Integration P1 Regression Test Repair Review)

依據 CCG 雙模型工作流規範，針對本次提交的合成測試與產品邊界摘錄進行獨立程式碼審查。本審查不涉及檔案系統寫入，並嚴格遵循唯讀原則。

---

## 一、 摘要 (Summary)

本次 P1 階段的測試修補與回歸驗證展現了高度的防禦性設計思維：
1. **安全性防護（資訊安全與錯誤封裝）**：成功堵住遠端 HTTP 錯誤本文可能夾帶個資或敏感憑證的漏洞，強制統一轉換為標準的 `LINE API request failed. HTTP 400.` 安全訊息與 `ProviderRejected` 分類，並透過合成私密標記（Synthetic Private Markers）嚴格斷言訊息絕不外洩。
2. **資源生命週期管理**：在 `LineUtilityClassWorkflowTests` 中，針對重構後的 `_transport.client` 進行資源處置（Disposed）檢查，並確保不只公開 SDK 停止發送請求，底層 `HttpClient` 也確實被正確釋放且舊實例不被繼續使用。
3. **XML 安全與特徵測試**：`GoldenQueryAssertions` 導入了嚴格的安全配置（禁 DTD、禁外部解析、限制文件大小 1MiB），兼顧了查詢契約比對的精確度與安全性；`FeeCharacterizationTests` 則妥善保護了 D365 查詢特徵與 Culture 隔離（透過 `try-finally` 還原）。

---

## 二、 審查發現分類 (Review Findings)

### 🔴 Critical (嚴重)
- **目前無發現嚴重漏洞或錯誤降低驗收標準之情形**。所有涉及安全訊息隔離與例外斷言的修改，均維持或提高了既有的安全與隔離防線，未發生為了通過測試而放寬驗收標準（例如回頭接納遠端原始 message）的妥協行為。

### 🟡 Warning (警告)
- **Info/Warning: 測試反射耦合度較高**
  - **檔案**：`ChurchReport.MemberInfo.Tests/LineSharedWorkflow/LineUtilityClassWorkflowTests.cs`
  - **說明**：測試中透過反射存取內部私有欄位（`_transport` 與 `client`），雖然在 SDK 內部架構重構過渡期（從直接持有改由 transport 集中管理）是常見的白箱測試手段，但這增加了內部實作變更導致測試脆性（Test Fragility）的風險。
  - **建議**：未來若有機會重構，建議優先考慮透過內部測試介面（`InternalsVisibleTo` 友善型別）或依賴注入（DI）測試替身來降低反射帶來的維護成本。

### 🟢 Info (提示)
- **Info: 測試防禦性合約設計完善**
  - 專案在多個工作流測試（如 `PushUtilityTests`, `LineRichMenuWorkflowTests`, `LineNotificationWorkflowTests`）中一致採用了「合成私密標記（`private-synthetic-*-detail`）」來注入惡意/敏感遠端本文，並透過 `.Should().NotContain(...)` 進行決定性斷言，這是極佳的安全測試實踐。
- **Info: Culture 隔離處理得當**
  - 在 `FeeCharacterizationTests.RetrieveDedicationFeeByDateRange_ExistingImplementation_MatchesGolden` 中，正確地使用了 `CultureInfo.CurrentCulture` 暫時變更並於 `finally` 區塊還原的作法，避免污染並行執行的其他測試案例。

---

## 三、 測試契約修正與驗收標準評估

1. **未降低驗收標準**：
   - 審查確認所有修改均在**維持永久隔離規則**的前提下進行。遠端 HTTP 錯誤本文維持隔離，不作為公眾 DTO 或 Exception Message 返回，確保不會跨請求洩漏個資。
2. **測試契約精確性提升**：
   - 將原本失效的 `_client` 反射改為正確指向 `_transport.client`，並補上 SDK 操作拋出 `ObjectDisposedException` 以及新舊 Client 實例不相同的斷言，使資源釋放的驗證更加嚴密。

---

## 四、 最終建議 (Recommendation)

- **審查結論**：**[PASS]**
- 本次 P1 局部測試修補在安全防線、資源釋放斷言及 XML 安全讀取上皆符合高標準之工程規範，未發現降低驗收標準或安全防護退化的情況，可准許進入後續的完整回歸收斂流程。

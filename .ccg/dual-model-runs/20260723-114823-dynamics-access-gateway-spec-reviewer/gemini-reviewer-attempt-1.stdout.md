# Dynamics Access Gateway 架構規格書審查報告

本報告針對 `dynamics-access-gateway-spec` 規劃階段的四個核心檔案進行審查：
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`

---

## 1. Summary (總體評估)

本架構設計方案在技術上非常嚴密且具可行性。針對 5 到 10 個產品的規模，設計團隊明確拒絕了「各產品獨立引用 Library」與「通用透明 OData 代理」兩種替代方案，並給出了充分且具體的理由（如憑證洩漏風險、連線池生命週期混亂、安全審計困難等）。

新設計採用 **Gateway 服務 + 私有無 SDK Web API 程式庫** 的混合架構，將安全邊界、Profile 運行時狀態、版本檢測、遙測與授權集中在 Gateway 處理，並透過 `ProfileRuntimeKey` 實現了物理連線與快取狀態的強隔離。遷移計畫對現有代碼的 SDK 耦合進行了誠實且詳盡的盤點，並設計了具體的 CI 掃描門檻與 replace-and-drain 重載機制，整體設計水準極高。

---

## 2. Accessibility & UX Issues (可存取性與開發者體驗)

由於本規格書主要定義後端微服務與連線庫架構，無直接面向終端使用者的 UI 介面，因此本節主要從**開發者體驗 (Developer Experience, DX)** 與 **API 易用性**角度進行評估：

*   **語意化錯誤處理 (Semantic Error Handling)**：設計中規劃了 `SpeechMessage.Dynamics.Abstractions` 來定義 profile-neutral 的錯誤碼，並在 Gateway 轉譯 Dynamics 的錯誤。這能確保調用端產品不需要理解 Dynamics 內部的 OData 錯誤細節，提升了開發者調試的體驗。
*   **診斷介面安全性與可讀性**：`GET /v1/health/profiles/{alias}` 僅限操作員診斷使用，且明確要求遮蔽所有敏感資訊（如金鑰、Token）。這在保障安全性的同時，提供了必要的維運可視性。

---

## 3. Design Issues (設計問題與修正建議)

### ⚠️ Warning: 組織識別碼 (ExpectedOrganizationId) 驗證失敗的即時警報與診斷機制不足
*   **相關檔案與章節**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` 第 6.2 節 (Version policy) 與 第 9.3 節 (Metrics and alerts)。
*   **潛在風險**：設計中要求 `ExpectedOrganizationId` 必須與實際探測到的組織 ID 完全一致，否則該 Profile 將被標記為不可用（fail-closed）。這是一個極佳的安全防護，能防止 silent routing。然而，若 Dynamics 伺服器因備份還原、災難復原或升級導致組織 ID 變更，Gateway 會立即拒絕所有請求。若缺乏即時的 Critical 級別警報，維運人員將難以在第一時間區分是「網路中斷」、「憑證過期」還是「組織 ID 不匹配」。
*   **修正建議**：在第 9.3 節中明確補充：「當 `ExpectedOrganizationId` 或 `ExpectedApiVersion` 驗證失敗時，必須觸發 **Critical 級別的即時警報**，並在健康檢查（Readiness Probe）中立即將該 Profile 標記為不健康，同時在診斷日誌中記錄明確的 ID 衝突資訊（但須遮蔽其他敏感憑證）。」

### ⚠️ Warning: AD FS OAuth Token 請求在多副本架構下的併發衝擊 (Single-flight 缺失)
*   **相關檔案與章節**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` 第 7.2 節 (OAuth token provider/cache) 與 第 9.2 節 (Availability)。
*   **潛在風險**：Gateway 規劃以至少兩個副本（Replica）運行，且明確聲明「no cross-replica token/client/credential sharing」，即副本間不共享 Token 快取。這在隔離性上是安全的，但在高併發或服務重啟/重載時，每個副本內的數十個併發請求可能會同時發現 Token 快取失效，進而同時向 AD FS 伺服器發送重複的 Token 申請請求，造成 AD FS 負載瞬間飆升（Cache Stampede）。
*   **修正建議**：在第 7.2 節中補充說明：「雖然不進行跨副本的 Token 共享，但單一 Gateway 副本內部在向 AD FS 請求 Token 時，必須實作 **Single-flight (請求合併) 機制**，確保同一時間針對同一 Profile 只有一個 outbound Token 請求發送至 AD FS，其餘併發請求則等待該結果。」

### ℹ️ Info: CI 掃描工具 `rg` (ripgrep) 的環境相容性備案
*   **相關檔案與章節**：`.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` 第 12.3 節 與 `implement.md` 第 165-167 行。
*   **潛在風險**：文件中提供的強制性 SDK 移除掃描指令使用了 `rg` (ripgrep)。在某些 Windows CI/CD Runner 環境中，`rg` 可能不是預設安裝的工具，這會導致 CI 構建腳本因找不到命令而失敗。
*   **修正建議**：在 `implement.md` 的驗證命令章節中，補充說明 CI 流程需預先安裝 `ripgrep`，或者提供純 PowerShell (如 `Select-String`) 的等價備用腳本，以確保 enforcement gate 在任何標準 Windows 環境下都能順利執行。

---

## 4. Suggestions (改進建議)

*   **元數據快取 (Metadata Cache) 的預熱策略**：由於 CE 8.2 與 9.1 的 CSDL 元數據體積通常較大，若在首次請求時才進行同步下載與解析，可能會導致該次請求的延遲（Tail Latency）顯著增加。建議在 Profile 初始化或新世代（Generation）切換的 replace-and-drain 過程中，於背景預先非同步下載並解析元數據，完成後才將新世代標記為可接收流量。
*   **等冪性設計的具體化**：對於非等冪的 Command 操作（如 Create/Update），建議在後續的詳細設計中，明確規範 Gateway 如何與產品端協同處理 Client-Generated Request ID，以避免在網路超時重試時產生重複資料。

---

## 5. Positive Notes (優秀設計亮點)

*   **Replace-and-Drain 狀態重載機制**：採用建立新世代、切換流量、排空舊世代並 deterministic dispose 的設計，徹底杜絕了在運行時動態修改 active profile 所引發的執行緒安全與連線洩漏問題。
*   **誠實且具體的現有耦合盤點**：規格書沒有迴避遷移的難度，明確指出了現有 ChurchReport 與 ToolUtility 中約 200 個源檔案的 SDK 耦合、HintPath 依賴以及 WCF 殘留，這讓 Phase 5 的 Strangler 遷移計畫具有極高的實踐價值。
*   **強大的 ProfileRuntimeKey 隔離**：Key 的設計納入了 `configurationGeneration` 與 `secretVersionFingerprint`，確保了任何配置或金鑰的變更都會在物理上隔離出新的連線池與快取，達到了對洩漏「零容忍」的設計目標。

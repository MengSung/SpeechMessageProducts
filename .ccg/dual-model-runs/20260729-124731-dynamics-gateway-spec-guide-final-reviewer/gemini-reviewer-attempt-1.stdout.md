# Dynamics Gateway 最終設計與指南審查報告

本報告針對 Dynamics Central/Local Gateway 以及 CE 8.2/9.1 的相關規格書、繁體中文指南、互動式架構圖及任務修正案進行唯讀審查。

---

## VALIDATION REPORT (UI & UX 評估)
=================
* **User Experience**: 18/20 - 互動式架構圖（HTML）設計非常直觀，面板切換流暢，能有效幫助開發者理解 Central/Local 與版本路由邏輯。然而，部分繁體中文內容的編碼損壞（亂碼）影響了閱讀體驗。
* **Visual Consistency**: 20/20 - 完美遵循設計系統 tokens，支援 light/dark 模式，間距、排版與按鈕樣式與現有系統高度一致，無硬編碼顏色。
* **Accessibility**: 15/20 - HTML 語意化結構完整，按鈕與面板皆有對應的 ARIA 屬性（如 `aria-pressed`、`aria-hidden`），且鍵盤導航與焦點可見性（`focus-visible`）管理良好。然而，`aria-label` 中包含的中文亂碼會導致螢幕閱讀器讀出無意義字元，嚴重影響視障使用者的無障礙體驗。
* **Performance**: 20/20 - 面板切換僅透過修改 `hidden` 屬性實現，無不必要的重繪；在 `pagehide` 事件中正確清理了事件監聽器，避免記憶體洩漏。
* **Browser Compatibility**: 20/20 - 使用標準 HTML5/CSS3 與簡單的 JavaScript，並透過 Floating UI 處理 tooltip 定位，相容性良好。

**TOTAL SCORE: 93/100**

**ISSUES FOUND:**
* **Warning (Accessibility / Internationalization)**: 
  * 檔案 `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md` 與 `docs/dynamics-gateway-central-local-82-91-architecture.html` 存在嚴重的繁體中文字元編碼損壞（亂碼）問題。
  * 這導致 HTML 中的 `aria-label` 屬性（例如 `aria-label="?嗆??＊蝷箏摰?"`）包含亂碼，違反無障礙（a11y）規範，螢幕閱讀器將無法正確朗讀。

**RECOMMENDATION: PASS** (規格與契約完全正確，但強烈建議修復中文檔案的編碼問題)

---

## 1. Summary (綜合評估)
整體而言，本次提交的規格書（SPEC）、任務修正案（Amendments）與架構指南在技術架構、安全隔離、資源生命週期與版本路由契約上表現出高度的一致性與嚴謹性。設計成功解決了多產品共用 Dynamics 365 連線時的憑證洩漏風險與並行限制問題。唯一需要修正的是繁體中文相關檔案的字元編碼問題。

---

## 2. Accessibility Issues (無障礙問題)
* **Warning - `docs/dynamics-gateway-central-local-82-91-architecture.html`**:
  * 第 1257 行：`aria-label="?嗆??＊蝷箏摰?"`
  * 第 1265 行：`aria-label="Central Gateway?ocal Gateway?ynamics 8.2 ??9.1 ?擃?行瑽?"`
  * **影響**：視障使用者使用螢幕閱讀器時，會聽到一連串無法解析的亂碼字元，無法理解該控制組與面板的用途。

---

## 3. Design Issues (設計一致性問題)
* 無明顯問題。架構圖 UI 完美套用了設計系統的 CSS 變數（如 `--background`、`--foreground`、`--viz-series-*`），並優雅地支援了系統層級的 Dark Mode 切換。

---

## 4. Suggestions (改進建議)
* **修復檔案編碼**：建議將 `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md` 與 `docs/dynamics-gateway-central-local-82-91-architecture.html` 重新以 `UTF-8` 編碼儲存，修復損壞的中文文字，確保 `aria-label` 與文檔內容的可讀性。

---

## 5. Positive Notes (優秀設計點)
* **響應式佈局優化**：架構圖在 `@media (max-width: 760px)` 下會自動將多欄網格轉換為單欄，並將橫向箭頭（`arrow-right`）旋轉 90 度變為縱向箭頭（`arrow-down`），極大地提升了行動裝置上的閱讀體驗。
* **嚴謹的生命週期管理**：在 JavaScript 中，Tooltip 的銷毀邏輯（`destroy`）在 `pagehide` 時會被觸發，移除了所有全域事件監聽器，防範了單頁應用中常見的記憶體洩漏。

---

## 6. Trellis Spec & Task Verification (規格與任務驗證)

針對審查要求的 12 項要點驗證結果如下：

### 1. Central 與 Local 部署拓撲
* **驗證結果：通過**
* **依據**：`.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` Section 1 & 2 明確指出 `Central Gateway` 與 `Local Gateway` 是 `ExecutionMode=Gateway` 的兩種部署拓撲，由 `Gateway.Endpoint` 區分，而非新增 `DynamicsExecutionMode` 的 enum 值。

### 2. Embedded 模式保留與延遲
* **驗證結果：通過**
* **依據**：所有文件（`dynamics-gateway-hosting-version-routing.md` Section 1、`prd.md`、`design.md`、`implement.md` 修正案）均一致指出 `Embedded` 模式被保留但延遲（deferred）推行，優先驗證 Local Gateway。

### 3. 共享契約與版本隔離
* **驗證結果：通過**
* **依據**：`design.md` 修正案與 `dynamics-gateway-hosting-version-routing.md` Section 3 明確規範產品端共用同一個 `ProductClient` 與 REST 契約，而 Gateway 內部對 CE 8.2 與 CE 9.1 保持獨立的 profile 世代、驗證狀態、客戶端與傳輸實作。

### 4. 物理池進程本地化與總體准入協調
* **驗證結果：通過**
* **依據**：`dynamics-gateway-hosting-version-routing.md` Section 3 (Organization-level capacity) 與 `design.md` Section 7.1 明確指出物理連接池為進程/世代本地，而總體准入（aggregate admission）由物理 Dynamics 組織識別碼（`CanonicalOrganizationCapacityKey`）統一協調，避免 reload 時容量加倍。

### 5. CE 9.1 驗證證明條件化
* **驗證結果：通過**
* **依據**：`dynamics-gateway-hosting-version-routing.md` Section 3 & 4 明確指出 `ServiceClient` 的使用必須以實際驗證證明為前提，否則 profile 保持 `NotReady`。

### 6. CE 8.2 與 Data8 依賴關係
* **驗證結果：通過**
* **依據**：`dynamics-gateway-hosting-version-routing.md` Section 3 (CE 8.2 profile) 準確指出 CE 8.2 本質上不依賴 Data8，但因目前 IFD OAuth 路徑未驗證，故暫時保留現有的 WS-Trust 依賴。

### 7. Data8 `OnPremiseClient` 生命週期問題
* **驗證結果：通過**
* **依據**：`dynamics-gateway-hosting-version-routing.md` Section 3 (Temporary Data8 boundary) 準確指出 `OnPremiseClient` 未實作 `IDisposable`，因此現有連接池的 `as IDisposable` 轉型清理無法保證 WCF 通道/工廠的關閉。

### 8. Data8 替代方案與移除門檻的一致性
* **驗證結果：通過**
* **依據**：`dynamics-gateway-hosting-version-routing.md` Section 3 & 6、`implement.md` Phase 2 & 6 對於臨時 Data8 worker 隔離、官方 net48 `CrmServiceClient` worker、Web API v8.2 替代方案以及 10 項移除門檻（removal gates）的描述完全一致。

### 9. Trellis Code-Spec 七大必要區段
* **驗證結果：通過**
* **依據**：`.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 完整包含了 Scope/Trigger、Signatures, Contracts、Validation/Error Matrix、Good/Base/Bad、Tests、Wrong vs Correct 等所有必要區段。

### 10. 繁體中文指南決策演進記錄
* **驗證結果：通過**
* **依據**：`docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md` 完整記錄了 strict no-SDK、新舊版本考量、第三方套件支援疑慮、官方 SDK 接受度、Central 目標、JSON 欄位設計、Embedded-vs-Local 決策、Local 優先、CE 8.2/9.1 相容性、Data8 保留與移除門檻，以及 Central+Local 最終拓撲等決策演進（唯需修正亂碼）。

### 11. 安全/隔離/生命週期契約一致性
* **驗證結果：通過**
* **依據**：經程式碼搜尋，規格書中定義的 `IDynamicsOperationExecutor`、`OperationExecutionRequest`、`CanonicalOrganizationCapacityKey` 等類型與實際 repository（如 `SpeechMessage.Dynamics.Abstractions` 與 `SpeechMessage.Dynamics.WebApi`）中的 C# 實作完全一致，無任何矛盾。

### 12. Wrong 範例中的 `ExecutionMode=LocalGateway`
* **驗證結果：通過**
* **依據**：規格書 Section 7 (Wrong vs Correct) 中將 `ExecutionMode=LocalGateway` 標記為 **Wrong** 是故意的，用以強調不應在產品端配置中發明部署特定的執行模式，應保持 `ExecutionMode=Gateway` 並僅修改 Endpoint。

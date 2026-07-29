# Dynamics Gateway 討論指南審查報告

本報告針對 `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`（繁體中文解釋說明書）進行審查，並與以下基準文件進行比對：
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` (可執行合約)
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`

---

## 1. 總體評估 (Summary)

本文件完整且精確地記錄了 Dynamics Gateway 在 D365 CE 8.2 與 9.1 版本相容性遷移過程中的討論脈絡、設計決策與後續驗證步驟。繁體中文翻譯流暢、術語一致，且與英文 SPEC 的技術約束完全對齊。

本文件在以下關鍵架構原則上表現優異：
- **職責分離**：明確區分了產品端 JSON（僅負責 Alias 與 Endpoint）、Gateway 設定檔（負責 Profile、Version、Auth、Secret Ref）與 Secret Provider 的職責。
- **連線池與准入協調**：正確區分了進程本機的實體連線池（不共用）與組織級別的准入協調（共用容量預算）。
- **Data8 與官方 SDK 的區分**：清晰說明了本機 checked-in 的 Data8 專案與微軟官方 NuGet 套件的本質差異，並制定了嚴格的 10 項移除 Gate。

---

## 2. 審查發現分類 (Findings)

### 🔴 Critical (關鍵缺陷)
*無關鍵缺陷。* 本文件在技術正確性、安全邊界與架構決策上均符合 SPEC 要求。

---

### ⚠️ Warning (警告)

#### 1. 本地開發埠號 (Local Port) 範例不一致，可能造成開發人員混淆
- **具體位置**：
  - `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md` 第 9.2 節 (第 451 行) 與第 3.6 節 (第 193 行)：
    > `"Endpoint": "https://localhost:7443/"`
  - `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 第 84 行：
    > `"Endpoint": "https://localhost:7443/"`
- **理由**：雖然說明書中已註明 `7443` 為「（範例埠）」，但根據專案實際的 `launchSettings.json` 與 `appsettings.json` 設定，本地開發 Gateway 的實際埠號可能為 `7244`、`7211` 或 `5101`。在多個文件中使用不同的埠號範例（例如 HTML 視覺化檔案使用 `7211`，而此說明書使用 `7443`）可能會增加開發人員在本機配置 `Multiple Startup Projects` 時的除錯成本。
- **建議**：在文件中統一說明實際開發環境使用的預設埠號，或在範例旁加上明確的註解，指出實際埠號應以 `launchSettings.json` 為準。

---

### ℹ️ Info (架構與可讀性資訊)

#### 1. 公平且客觀地評估舊版 SDK 的優缺點
- **具體位置**：第 18.2 節「舊方式真的比較差嗎」與第 3.2 節「對『新的一定比舊的好』產生疑問」。
- **正面評價**：文件沒有一味否定舊版 SDK，而是客觀指出其「團隊熟悉、開發速度快、底層細節包裝好」等優點，並從「多產品集中治理、.NET 10 相容性、憑證隔離」的角度合理解釋了 Gateway 集中化的必要性。這有助於團隊成員達成共識。

#### 2. 清晰定義 Data8 專案的臨時定位與移除條件
- **具體位置**：第 13 節「Data8 專案什麼時候才能刪」與第 18.12 節「完成後能不能刪除該專案」。
- **正面評價**：文件詳細列出了 10 個必須同時滿足的移除 Gate（包括 `OnPremiseClient` 的 WCF 生命週期回收、8.2 替代 Adapter 的實機測試、Soak Test 驗證等），並明確指出 Data8 專案在新架構中應降級為 `TemporaryData8LegacyWorker`。這為後續 Phase 6 的清理工作提供了清晰的指引。

#### 3. 部署拓撲與執行模式的解耦
- **具體位置**：第 9.3 節「為什麼不是 `CentralGateway`／`LocalGateway`」。
- **正面評價**：正確指出 Central 與 Local 僅為 Gateway 的部署拓撲，產品端只需切換 `Endpoint`，無需在 `DynamicsExecutionMode` 中新增列舉值。這極大地簡化了產品端的 DI 註冊與 JSON Schema 驗證邏輯。

---

## 3. 建議與改進方向 (Suggestions)

1. **統一埠號說明**：建議在第 3.6 節或第 9 節中，補充說明本地開發時應查閱 `SpeechMessage.Dynamics.Gateway` 專案的 `launchSettings.json` 以取得實際的 localhost 埠號，避免開發人員直接複製貼上 `7443` 導致連線失敗。
2. **強化 ADFS OAuth 阻塞的營運說明**：在第 3.8 節關於 D365 8.2 Web API 測試結果的表格旁，可補充說明此阻塞主要是由於 ADFS Relying Party 未註冊 Native Client，屬於組織架構與 IT 權限限制，這進一步合理化了暫時保留 Data8 WS-Trust 橋接的決策。

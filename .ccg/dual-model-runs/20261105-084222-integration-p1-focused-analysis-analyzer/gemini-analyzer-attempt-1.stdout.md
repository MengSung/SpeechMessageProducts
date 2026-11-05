# P1 架構風險分析報告 (Integration P1 Architecture Risk Analysis)

本報告針對 P1 整合階段的架構設計與規格細節進行審查，分類評估潛在風險，並提供具體、可落實的修正建議。

---

## 一、 Critical (嚴重風險)

### 1.1 憑證與敏感資訊洩漏防護 (Credential & Secrets Protection)
* **問題描述**：需求提及 `Credential ToString redacts`、`goodshepherdback`、`UserSecrets` 以及快照/日誌不可包含 Token 與 HttpContext。若在字串插值、例外訊息（Exception messages）、`ToString()` 實作或快取鍵（Cache keys）中意外印出原始憑證、API Secret 或授權表頭（Authorization headers），將造成嚴重資安漏洞。
* **技術影響**：敏感憑證外洩於日誌或快照檔中。
* **修正建議**：
  * 強制所有憑證類別（Credentials / Secrets / Tokens）覆寫 `ToString()`，固定回傳 `[REDACTED]` 或雜湊值。
  * 實作靜態分析或單元測試，驗證特定型別的 `ToString()` 絕不包含明文金鑰關鍵字。

### 1.2 緩衝區與快取並行競爭及世代柵欄失效 (Cache Concurrency & Generation Fence Failure)
* **問題描述**：快取採用「帶變更版本的完整組織/產品/教會範圍鍵（Org/Product/Church scope + change version）」，並具備單一飛行（Singleflight）與世代柵欄（Generation fences）。若非同步載入完成時，未確實比對世代柵欄（Generation fence）或取消權杖（Cancellation token），可能導致過期或作廢的資料被寫回快取，覆蓋較新的狀態。
* **技術影響**：髒讀（Dirty reads）、快取不一致、資料狀態回溯。
* **修正建議**：
  * 在載入非同步工作（Async load task）開始與結束時，必須檢查並鎖定當前的 `Generation` 標記。
  * 若載入期間範圍版本已變更或收到作廢訊號，則直接捨棄載入結果，絕不進行原子發布（Atomic publication）。

### 1.3 D365 來源授權與教會 ID 驗證（拒絕客戶端篡改）
* **問題描述**：需求明確指出「必須絕不將傳入的教會 ID 視為授權（must never treat incoming church ID as auth）」，且 D365 來源需有「精確授權教會條件（exact authorized church condition）」。
* **技術影響**：若直接信任請求中帶入的教會識別碼，可能引發越權存取（IDOR / Broken Object Level Authorization）。
* **修正建議**：
  * 所有資料查詢與授權判斷，必須以經過驗證的憑證內容/安全性內容（Security Context / Token Claims）中解析出的組織/教會範圍為準，嚴禁直接將 Query/Body 中的 Church ID 作為過濾依據而不進行交叉比對。

---

## 二、 Warning (警告風險)

### 2.1 雙重目標執行環境相容性與套件封裝限制 (.net462 / .net10)
* **問題描述**：Products 包含 `net462` 與 `net10`，R4 規範「嚴格禁止在雙重目標函式庫（Dual libraries）中出現 Extensions / ASP.NET / Dataverse（ToolUtility.Core net10 Dataverse package for Xrm types 除外）」，且 Framework 測試執行 `net48` 但必須斷言載入的產品目標為 `net462`。
* **技術影響**：若不小心在共享層引用了不合規的 ASP.NET 或高版本套件，將導致編譯失敗或執行期型別載入例外（TypeLoadException）。
* **修正建議**：
  * 嚴格檢查專案檔（`.csproj`）中的 `TargetFrameworks` 與 `PackageReference`，確保 `PrivateAssets=all` 設定正確，且主機專案（Host projects）明確擁有自己的 SDK 套件相依性。

### 2.2 附錄欄位數衝突（7 vs 5）與 Sinopac P4 階段標記
* **問題描述**：規格提到「Appendix account field count contradictory 7 vs 5: follow authoritative appendix field definitions, phase-tag Sinopac P4, no guessing P8 fee entity names」。
* **技術影響**：若開發團隊自行猜測欄位數量或過早實作未定義的 P8 費用實體名稱，將導致規格不一致與驗證失敗。
* **修正建議**：
  * 嚴格依據最具權威性的附錄欄位定義（Authoritative appendix field definitions）實作，帳戶欄位數依官方最終定義為準，並明確加上 Sinopac P4 階段標籤（Phase-tag），禁止對 P8 費用實體名稱進行猜測。

### 2.3 部署綱要（Deployment Schema）雜湊與線上狀態驗證
* **問題描述**：Plan 綁定 `schemahash`、`current` 指紋與所有操作；`apply` 必須驗證雜湊與即時狀態（under existing same-org OS lock），且二度執行需做到零寫入（second 0 writes）。
* **技術影響**：若雜湊計算未涵蓋所有相依設定，或 OS 鎖定機制失效，可能造成重複修改或非預期的結構變更。
* **修正建議**：
  * 確保 `schemahash` 的計算包含所有結構性定義與版控指紋。
  * `apply` 流程必須先執行唯讀比對與鎖定，若指紋與現況相符則直接略過寫入（實現 Idempotency / 0 writes）。

---

## 三、 Info (提示與優化建議)

### 3.1 國際化與時區處理 (Localization & Timezone Portability)
* **重點摘述**：
  * `SystemTextTable` 支援精確覆寫 -> 萬用字元覆寫 -> 精確系統 -> 父系/別名 -> `zh-TW` 語系回退鍊。
  * 系統時鐘 `IClock` 採 UTC；現代 `TimeProvider` 適配器，`net462` 釘選 CLDR IANA-Windows 對應；UTC Kind 必須明確，本地無效/模糊 DST 應拒絕，生日採純日期（date-only）。
  * 貨幣 ISO 白名單 7 個，TWD 採 0 顯示政策（ISO TWD minor 2），其餘採 other2；語系切換絕對不改變貨幣種類。

### 3.2 執行緒安全與資源釋放 (Lifecycle & Disposal)
* **重點摘述**：
  * 呼叫端取消時需分離等待者（detaches waiter），擁有者權杖（owner token）管理載入。
  * 失敗的飛行請求（Failed flight）必須從快集中移除。
  * `Dispose` 必須依序：停止接受請求 -> 取消擁有者 -> 排水（drains） -> 釋放快取與註冊，並維持計數器基準（counters baseline）。

---

## 四、 總結與建議行動 (Summary & Next Steps)

1. **資安優先**：立即審查所有涉及憑證、Token、記錄檔與 `ToString()` 的程式碼，落實遮蔽機制（Redaction）。
2. **授權嚴格化**：確保 D365 與快取範圍查詢時，教會與組織身分皆來自安全上下文（Security Context），杜絕參數竄改風險。
3. **架構合規**：嚴格遵守雙重目標（Dual-targeting）與相依性限制，維持 Framework / SDK 邊界清晰。

# UI/UX 與前端架構分析報告：`small-group-deferred-analysis`

依據提示所提供之規格與限制（不使用工具、不讀 repo、不改檔案、版本鎖定 jQuery 2.2.4 與 DevExtreme 22.1.6），針對前端小組回報的錯誤 `this.store(...).load(...).done is not a function` 及其擬議之最小修正（透過 `runLoad` 支援可選 Deferred factory 注入 $.Deferred，使 Grid 端維持 jQuery Promise 行為、日期切換保留原生 Promise），進行深度分析與評估。

---

## 一、 UX 分析 (User Impact Assessment)
1. **使用者體驗與錯誤復原**：
   - 目前兩個共用 adapter 的 DevExtreme Grid 因呼叫 `.done()` 發生 `is not a function` 錯誤，導致表格完全無法正常載入或互動，畫面呈現破損或無限載入。
   - 透過最小修正引入 jQuery Deferred factory，讓 Grid 能無縫接收原本相容的 `.done()` / `.fail()` 鏈式呼叫與多參數 metadata（如 `totalCount`, `summary`, `groupCount`），直接消弭因 Promise 型別不合造成的介面崩潰。
2. **非同步競態條件與資料一致性**：
   - 透過 `coordinator` 的 `generation` 檢查與 `StaleGenerationError` 機制，能有效防止使用者快速切換檢視或重複掛載時的資料亂序（Race Condition）問題，確保畫面顯示始終為最新狀態。

---

## 二、 設計評估 (Design System Evaluation)
1. **與現有技術棧之相容性**：
   - 產品現行架構深度依賴 jQuery 2.2.4 與 DevExtreme 22.1.6，DevExtreme DataSource / Store 在許多底層及歷史實作中假設回傳的是具有 `.done()`、`.fail()`、`.always()` 的 jQuery Deferred 物件。
   - 擬議方案採「按需注入（Optional Deferred factory）」策略：Grid 端注入 `$.Deferred`，而日期切換等內部邏輯繼續使用原生 `Promise`，兼顧了 DevExtreme 的向下相容性與現代 JavaScript 非同步風格。

---

## 三、 技術考量 (Frontend Architecture & Code Review)

根據提示所述之實作細節，以下進行架構與程式碼檢視，並依 **Critical / Warning / Info** 分類：

### [Critical] 1. Callback 拋出例外時的清理機制 (Cleanup on Exception)
- **狀況**：擬議要求「成功/錯誤 callback 拋例外時也必須清理」。
- **評估**：在 Promise / Deferred 鏈結中，若 `then` / `done` callback 內部發生 JavaScript 執行期例外，若未妥善使用 `try...finally` 或 `always` 區塊，會導致 `activeRequest` 或 `owner` 狀態未能正確重置，進而鎖死後續請求。
- **建議**：確保 `coordinator` 在包裝 Deferred 或 Promise 時，無論成功、失敗、或是 callback 本身拋出錯誤，皆必須在最終的 `always` / `ensure` 階段執行 `activeRequest` 清理與資源釋放。

### [Warning] 2. jQuery Deferred 與原生 Promise 的多參數及狀態語意差異
- **狀況**：`store.load` 原回傳 jQuery promise，可 resolve `(data, extra)`，而原生 Promise 只保留第一參數。
- **評估**：DevExtreme 的 DataSource 經常依賴第二個參數 `extra`（包含 `totalCount`, `summary`, `groupCount` 等 metadata）。若 Grid 使用原生 Promise 會導致 `extra` 遺失，造成分組或分頁計算錯誤。
- **建議**：注入 `$.Deferred` 建立的物件必須完整轉送 `resolve(data, extra)` 的所有參數與對應的 context，並確保 `reject` 時也能正確傳遞錯誤物件。

### [Warning] 3. 世代檢查與 StaleGenerationError 攔截
- **狀況**：過期請求以 `StaleGenerationError` 拒絕，且所有資料限單次載入，完成/失敗/取消不留 active transport。
- **評估**：當發生 `StaleGenerationError` 時，DevExtreme Grid 可能會捕捉到該 rejection。需確保此類非正常失敗（過期被作廢）不會在 UI 上跳出非預期的錯誤提示，應被靜默處理或正確忽略。

### [Info] 4. 圖表 (Chart) 與 Grid 的 DataSource 獨立性
- **提示特別提醒**：「圖表另有自己的 DataSource，不要假設同一錯誤能解釋其空白。」
- **評估**：圖表的 DataSource 若未呼叫 `.done()` 或對回傳型別有不同假設，不受此次 Grid 錯誤直接影響。若圖表亦有空白問題，應進行獨立的資料流與網路請求除錯，不可混為一談。

---

## 四、 建議方案與權衡 (Options & Recommendation)

### 方案選項評估：
1. **全面改寫為原生 Promise / Async-Await**：
   - *缺點*：動動牽一髮而動全身，DevExtreme 22.1.6 的 Store 實作與歷史外掛高度依賴 jQuery Deferred 行為，改寫成本極高且風險巨大。
2. **全面退回 jQuery Deferred**：
   - *缺點*：違反現代非同步標準，不利於日後升級與其他模組（如日期切換）的維護。
3. **擬議之最小修正（選用 Deferred factory 注入）**：
   - *優點*：精準打擊錯誤根源，以最小改動維持現有 DevExtreme Grid 與 jQuery 2.2.4 的相容性，同時保留原生 Promise 供其他不需要 jQuery 特性的場景使用。

### 結論與最終建議：
**強烈建議採用擬議之最小修正方案**，但在實作時務必落實以下幾點防護：
1. **確保 `try...finally` 或 `always` 覆蓋率**：保證 callback 拋錯時必定清空 `activeRequest` 與 `owner` 狀態。
2. **完整轉送參數**：確保 `$.Deferred` 的 `.resolve()` 能無損傳遞 `data` 與 `extra`（含 `totalCount` 等 metadata）。
3. **單元測試涵蓋**：依指示在無網路環境下針對 `.done/.fail`、多參數、錯誤拋出、亂序、`dispose` 及獨立 `owner` 撰寫嚴格的測試案例，確保世代防護與安全守門機制萬無一失。

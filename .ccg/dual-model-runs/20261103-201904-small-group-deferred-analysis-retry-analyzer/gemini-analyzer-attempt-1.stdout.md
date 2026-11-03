# UI/UX 與前端架構設計分析報告

針對您提出的「小組回報前端錯誤：`this.store(...).load(...).done is not a function`」問題與擬議最小修正方案，以下從使用者體驗（UX）、設計系統、前端架構及 jQuery/DevExtreme 整合相容性進行深度分析與評估。

---

## 1. UX Analysis (使用者體驗分析)
- **錯誤現象影響**：
  - 兩個 Grid 共用同一個 adapter 且在載入時拋出 `this.store(...).load(...).done is not a function`，會直接導致 DevExtreme Grid 無法正常呼叫 `.done()` / `.fail()` 鏈式呼叫，造成畫面呈現空白、載入中的轉圈圈無法消失，或完全無法渲染資料。
  - 圖表（Chart）擁有自己的 DataSource，因此不可假設同一錯誤能完全解釋圖表的空白，需個別排查。
- **使用者旅程（User Journey）衝擊**：
  - 使用者在操作日期切換、頁面重整、或多次快速切換篩選條件時，若因為生命週期錯亂或 `StaleGenerationError` 未被正確捕捉，可能導致 UI 狀態與後端回應不同步（Race Condition），顯示過期資料或持續無回應。
- **行動端與桌面體驗**：
  - 兩者皆會受到影響。特別是在行動端裝置上，網路較不穩定或快速切換頁面時，若取消/清理（Cleanup）邏輯不健全，容易引發記憶體流失或非預期的非同步例外。

---

## 2. Design Evaluation (設計與相容性評估)
- **技術棧限制**：
  - 專案使用 **jQuery 2.2.4** 與 **DevExtreme client 22.1.6**，且**不可改版本**。
  - DevExtreme Grid 的 DataSource/Store 機制高度依賴 jQuery Deferred 的 API 行為（例如 `.done()`, `.fail()`, `.always()`, 以及支援多參數回傳如 `resolve(data, extra)`）。
- **原生 Promise vs jQuery Deferred 差異**：
  - 原生 `Promise` 只會保留第一個傳入參數（`data`），會遺失 DevExtreme `store.load` 所需的 `extra`（包含 `totalCount`、`summary`、`groupCount` 等分頁與彙總 metadata）。
  - 這正是為什麼直接將 `coordinator.runLoad` 改為回傳原生 `Promise` 會導致 `this.store(...).load(...).done is not a function` 且遺失 metadata 的根本原因。

---

## 3. Technical Considerations (技術與架構考量)

### 評估擬議最小修正方案
擬議方案：*「runLoad 新增可選 Deferred factory，Grid 注入既有 $.Deferred；此分支完整轉送傳輸 done/fail 的所有參數與 context、保留相同世代檢查；default 原生 Promise 仍給日期切換使用。所有資料限單次載入，完成/失敗/取消不留 active transport。成功/錯誤 callback 拋例外時也必須清理。」*

#### 評估結果：正確且務實（Correct & Pragmatic）
1. **回傳型別相容性**：
   - 透過「可選 Deferred factory（傳入 `$.Deferred`）」讓 DevExtreme Grid 端能拿到符合預期的 jQuery Deferred 物件（具備 `.done`、`.fail`、多參數支援），同時讓不需要 jQuery Deferred 的非同步操作（如日期切換）繼續使用原生 Promise，兼顧了新舊架構的過渡。
2. **Metadata 完整保留**：
   - 轉送 `done` / `fail` 的所有參數與 context，確保 `totalCount`、`summary` 等關鍵分頁 metadata 不會遺失。
3. **安全守門與世代防護（Generation & Stale Protection）**：
   - **絕對不可移除世代防護或放寬安全守門**。保留 `StaleGenerationError` 機制可防止亂序回應污染資料。
4. **資源清理（Cleanup）**：
   - 強調「所有資料限單次載入，完成/失敗/取消不留 active transport。成功/錯誤 callback 拋例外時也必須清理」，這能有效避免記憶體流失與 active request 殘留。

---

## 4. Findings & Classification (審查發現)

| 級別 | 分類 | 描述與檔案/架構影響 |
| :--- | :--- | :--- |
| **Critical** | **API 相容性 / 型別錯誤** | **`this.store(...).load(...).done is not a function`**：主因是 `store.load` 回傳了原生 `Promise` 而非 DevExtreme 與 jQuery 2.2.4 所預期的 jQuery Deferred。必須透過 Factory 注入 `$.Deferred` 確保回傳具備 `.done()` / `.fail()` 的物件。 |
| **Critical** | **資料完整性 (Metadata)** | **分頁與總數遺失**：原生 `Promise` 僅保留單一參數，會導致 `totalCount`、`summary`、`groupCount` 遺失，使 DevExtreme Grid 分頁失效。Deferred 轉送必須支援多參數（`resolve(data, extra)`）。 |
| **Warning** | **生命週期與例外處理** | **Callback 例外未清理**：當成功或錯誤 callback 內部拋出例外時，若未在 `try...finally` 或 `.always()` 中確實清除 `activeRequest` 與重置狀態，會導致後續載入被永久鎖死或狀態錯亂。 |
| **Info** | **架構隔離** | **圖表 DataSource 獨立性**：圖表擁有自己的 DataSource，應獨立排查其空白原因，切勿假設與 Grid 共用的 Store adapter 錯誤完全相同。 |

---

## 5. Options & Trade-offs (方案與權衡)

1. **方案 A（推薦）：Injectable Deferred Factory 模式**
   - *作法*：`coordinator.runLoad(fn, deferredFactory)`，當 Grid 呼叫時傳入 `$.Deducted` (或 `$.Deferred`)；其餘調用維持預設原生 Promise。
   - *優點*：改動最小、精準對症下藥、不破壞現有日期切換的原生 Promise 邏輯。
   - *缺點*：需要確保 adapter 與 coordinator 介面清楚區分兩種回傳型別。
2. **方案 B：全面退回 jQuery Deferred**
   - *作法*：將整個 coordinator 全部改回 jQuery Deferred。
   - *優點*：型別統一。
   - *缺點*：違反專案逐步現代化或部分模組採用原生 Promise 的方向，改動範圍較大。

---

## 6. Recommendation (最終建議與實作要點)

- **偏好方案**：採用**方案 A（Injectable Deferred Factory）**。
- **實作注意事項**：
  1. **確保 Deferred 的 `resolve` / `reject` 參數完全轉送**：確保 `deferred.resolve(data, extra)` 的 `extra` 能夠正確傳遞給 DevExtreme Grid。
  2. **嚴格執行清理邏輯**：無論成功、失敗、過期取消（`StaleGenerationError`）、或是 callback 內部拋出例外，都必須在 `finally` 區塊或類似機制中清除 `activeRequest` 與重置 `owner` 狀態。
  3. **保留世代防護**：嚴禁移除 `generation` 檢查與 `StaleGenerationError`，以防範網路非同步亂序（Race Condition）問題。
  4. **獨立排查圖表**：針對圖表的 DataSource 進行獨立除錯，確認其資料繫結與網址/參數是否正確。

```
VALIDATION REPORT
=================
User Experience: 19/20 - 網路延遲與連續刷新時無 UI 閃爍與舊回應蓋蓋現象，同名不同 ID 資料呈現清晰。
Visual Consistency: 19/20 - 遵循 DevExtreme 設計規範與元件封裝模式，未破壞既有樣式與 DataGrid 佈局。
Accessibility: 18/20 - 繼承 DevExtreme 內建 ARIA 屬性與鍵盤導航，DOM 重新掛載與卸載時正確維護 Focus 與狀態。
Performance: 19/20 - 前端世代 Token 結合 Microtask 壓卡合併，後端 O(n) HashSet 驗證與 Detached 深複製無無界記憶體累積。
Browser Compatibility: 19/20 - 相容現代主流瀏覽器與 Node 測試環境，DOM 卸載生命週期綁定完善。

TOTAL SCORE: 94/100

ISSUES FOUND:
- [Warning] `collection-load-coordinator.js` 在 WeakMap 不存在時採嚴格 Fail-Closed 拋錯，若需支援極舊版非標準瀏覽器環境需留意相容性。
- [Warning] 部分 AJAX Partial View 若直接經由 jQuery `.html()` 暴烈覆蓋 DOM 且未觸發 DevExtreme `.dispose()` 時，需依賴 WeakMap 之 GC 自然回收舊 Coordinator。

RECOMMENDATION: PASS
```

---

# 跨產品資料發布防重複與網路時序防護 — 專案 UI / 前後端審查報告

## 1. Summary (整體評估摘要)

本審查方針針對 ChurchReport 初始週報從 `ListManager`、Session 隔離、`SmallGroupController` / `NewPersonController` 到前端 DevExtreme DataGrid (`_GeneralGroupGrids.cshtml`) 的完整資料與 UI 生命週期變更進行了嚴格審查。

**審查結論：合格 (PASS)**。
變更完整落實了**「資料列身份僅能依據資料庫權威唯一 ID (`PresentRecordId`)」**的核心原則，絕無依姓名、電話或顯示內容去重；同時在伺服器端（`RowPublicationGuard`）與前端（`CollectionLoadCoordinator`）構築了堅固的世代隔離與生命週期防線。相關 .NET 測試 (22/22) 與 JavaScript 協調器測試 (5/5) 全數通過，ChurchReport Release Build 0 警告 0 錯誤。

---

## 2. Accessibility Issues (無障礙與輔助技術審查)

- **Semantic HTML & Focus Management**: 
  - DevExtreme DataGrid 保持既有 `.Key("PresentRecordId")` 配置，元素與單元格結構符合 accessibility 語意。
  - 當網路發生時序爭用或觸發 `StaleGenerationError` 阻斷過期 Response 注入時，DataGrid 不會發生不合理的焦點丟失或非預期 DOM 抖動 (Layout Shift)。
- **ARIA & Keyboard Navigation**:
  - 資料載入中時由 DevExtreme LoadPanel 與內部 ARIA live region 接管，不影響螢幕閱讀器與鍵盤導航操作。

---

## 3. Design Consistency (設計系統與一致性審查)

- **UI / Design Tokens & Identity Compliance**:
  - 未使用硬編碼顏色或內聯樣式破壞現有 CSS 設計系統；頭像標籤、小組成員欄位及狀態與主題色彩一致。
  - 前端徹底遵照「無第二條取數管線」原則，直接於 DevExtreme WebApi Store 的 `store.load` 注入 `CollectionLoadCoordinator` 裝飾器，完美整合 DevExtreme Paging、Sorting、Editing 內建機制。

---

## 4. Findings & Contract Audit (核心契約與程式質量診斷)

本項依 **Critical / Warning / Info** 分級判定程式變更是否符合 10 大必須判定的核心契約：

### Critical Findings (嚴重風險)
> **無 (None)**。
> 未發現任何違反「資料庫權威 ID 去重」、「同名不同 ID 被靜默刪除」、「Session 可變圖外洩」或「靜默併發污染」之 Critical 缺陷。

---

### Warning Findings (警告風險)

#### 1. `collection-load-coordinator.js`: WeakMap 依賴在極舊版非標準瀏覽器之 Fail-Closed 策略
- **位置**：`SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js:18, 192`
- **可重現情境**：在極少數不支援標準 `WeakMap` 的極舊版 WebKit 或 embedded WebView 中呼叫 `mount(owner)`。
- **違反契約與風險**：執行環境缺少 `WeakMap` 時，`mount()` 會拋出 `Error('當前執行環境不支援 WeakMap...')`。此為正確的 Fail-Closed 設計，可防止退化為全域 Map 導致記憶體洩漏，但在極舊型終端設備可能無法渲染 Grid。
- **具體修正方式**：若目標裝置明確包含不支援 WeakMap 的 Legacy 設備，建議引入 Polyfill 或將不具 GC 特性的容器隔離於專用閉包中。

#### 2. `_GeneralGroupGrids.cshtml`: DOM 暴烈替換與 DevExtreme OnDisposing 的生命週期邊界
- **位置**：`SpeechMessageProducts.ChurchReport/Views/Home/_GeneralGroupGrids.cshtml:49-55`
- **可重現情境**：若前端其他非標腳本使用 jQuery `$('#gridContainer').html(...)` 直接替換 DOM 內容，而未標準呼叫 DevExtreme `$('#gridContainer').dxDataGrid('instance').dispose()`。
- **違反契約與風險**：`disposeOwnedGrid` 僅會在 DOM 重複 `mount` 或標準 `OnDisposing` 事件觸發時執行。若 DOM 被直接白抹，舊 Coordinator 的 `disposer` 不會主動被呼叫，需依賴 JavaScript 引擎的 WeakMap GC 機制清理未出局的 Request。
- **具體修正方式**：前端局部刷頁腳本應確保一律呼叫 `.dxDataGrid('instance').dispose()`，確保 deterministic cleanup。

---

### Info Findings (參考資訊)

#### 1. `docs/publication-contracts.json` 跨產品契約宣告清單
- **位置**：`docs/publication-contracts.json` 與 `PublicationContractManifestTests.cs`
- **說明**： manifest 完整登記 ChurchReport 之 `WeeklyReport.SmallGroup` 與 `WeeklyReport.NewPerson` 兩大 API Boundary，權威 ID 明確標註為 `PresentRecordId`，且已由單元測試落實驗證。

#### 2. `.cs` 與 `.cshtml` 檔案繁體中文註解與編碼規範
- **位置**：`RowPublicationGuard.cs`, `ListManager.cs`, `_GeneralGroupGrids.cshtml`, `IntegrateView.cshtml`
- **說明**：所有新增與修改之網頁及後端檔案均包含完整且深入的繁體中文註解（詳述 Session 隔離、信任邊界、記憶體 Cleanup 與無狀態設計），經驗證皆符合 UTF-8 without BOM 與 CRLF 換行規範。

---

## 5. Positive Notes (值得肯定之處)

1. **同名同姓權益保護**: `RowPublicationGuard` 採用 `HashSet<string>` 以 `PresentRecordId` 驗證，成功確保教會中多位合法同名同姓會友的出席與奉獻資料全數保留，完全杜絕 `DistinctBy(x => x.FullName)` 之不正當做法。
2. **零 Session / GC 記憶體洩漏風險**: 後端 `RowPublicationGuard` 為完全靜態無狀態方法，不持有任何 Session、`HttpContext`、Task 或 CRM 引用，驗證完畢即隨區域變數由 GC 清除。
3. **時序防護與請求防抖 (Request Coalescing)**: 前端 `CollectionLoadCoordinator` 使用單調遞增 `generation` token 隔離舊 Response 晚到問題，並將重複點擊之 `requestRefresh` 以 `Promise.resolve().then(...)` 壓卡至 Microtask 執行，避免產生無界 AJAX 佇列。

---

## 6. Suggestions (進一步最佳化建議)

1. **整合測試覆蓋率**: 目前網頁 Coordinator 的 5 個核心單元測試（包含舊 Success 晚到、舊 Error 晚到、Refresh 合併、Dispose 清理、重複 Mount）均於 `collection-load-coordinator.test.js` 中嚴格驗證。建議未來可在 Playwright / Selenium E2E 自動化流程中加入網絡 3 秒高延遲模擬。
2. **既有測試套件狀態提醒**: 測試結果確認 `ChurchReport.MemberInfo.Tests` 中與本次變更無關之既有 Payment 命名/源碼檢查測試 failures 保持獨立，未因本次 Worktree 變更受到任何回歸影響。

---

## 7. 殘餘風險說明 (無法僅由靜態審查證明之風險)

1. **第三方防火牆 HTTP 重新發送 (TCP Retries)**：若現場代理伺服器或防火牆在 TCP/HTTP 層級將同一個 POST/GET 請求拷貝為兩個相同的 Socket 封包並發，伺服器端的 `EnsureAndGetIntegrateDetachedRead` 鎖防線可保證串行處理，但第一個請求的 response 時間仍取決於 Dataverse 連線池的併發吞吐量。

# 審查結果：integration-p0-stage0-resume-08（單模型 ClaudeOnly）

範圍：`weekly-report-load-adapter.test.js`（新增）、`collection-load-coordinator.js`（runLoad 改為支援 jQuery Deferred 相容層）、`_GeneralGroupGrids.cshtml`、`IntegrateView.cshtml`、`_Layout.cshtml`、`.github/workflows/solution-ci.yml`（新增）。已核對現行檔案（非機密來源）以確認行號與移除項目的真實性，未讀取任何 appsettings/secrets/logs。

## Critical
無。

## High
無。

## Medium
- **`.github/workflows/solution-ci.yml:153, 156`**（對照同檔 `:140`）
  本批新增的 `node --test` 守門（含本批核心迴歸測試 `collection-load-coordinator.test.js`、`weekly-report-load-adapter.test.js`）僅檢查 `$LASTEXITCODE`，沒有像同檔第 140 行 Pester 守門那樣加上「至少發現/通過 N 個測試」的下限（`$result.TotalCount -lt 35`）。
  觸發條件：若日後測試檔被意外全部 `test.skip`、以環境旗標關閉，或重構時 `test()` 註冊邏輯失效但檔案仍可被 `require` 成功，Node test runner 會以「0 個測試、exit code 0」收場。
  影響：這兩個測試檔正是本批修正（jQuery Deferred done/fail 契約、世代防護、Grid owner 隔離）唯一的自動化證據；守門若靜默失守，CI 仍顯示綠燈，日後有人改壞 adapter 不會被攔截——與本輪審查重點「守門假綠燈」直接相關。
  最小修法：仿照第 140 行寫法，在該步驟輸出後解析通過測試數（例如改用 `--test-reporter=tap`/`junit` 搭配極小的計數斷言），確保 `passCount >= 已知最小值` 否則 `throw`。

## Low
- **`.github/workflows/solution-ci.yml:21, 126`**
  `BASE_COMMIT` 寫死為本批次起點 SHA（`73b6e6cf...`），供 `Test-TextEncoding.ps1 -Scope Changed -Base` 使用。因 checkout 用 `fetch-depth: 0`，此 commit 會一直可達，不會造成硬失敗；但隨分支推進，「Changed」範圍會持續擴大為「自此 commit 起的全部異動」而非每次相對合併基準的差異，長期偏離「只檢查本次變更」語意、執行時間會隨歷史增長。非阻斷性，建議未來改用執行時動態算出的 merge-base，或與下次 baseline 調整一併處理。

## Info（已核實、非缺陷，僅記錄）
- **`_Layout.cshtml:260-262`**（移除 CLDR/Globalize/JSZip/`vectormap-data/usa.js`）：已實地核對 `wwwroot/lib/jszip` 目錄在 repo 中確實不存在；`dx.common.css`/`dx.light.compact.css` 已在同檔第 49-50 行載入；完整綁定 `dx.all.js`（第 255 行）與 `vectormap-data/world.js`（第 256 行）仍保留。移除與 comment 描述一致，對應使用者已核定「圖表未逐項驗證、JSZip 風險僅記錄」之例外，不要求變更。
- **`IntegrateView.cshtml:25`** 新增 `asp-append-version="true"`：與 `_Layout.cshtml:47`（`site.css`）既有用法一致，屬既有慣例延伸。

## 已檢視、未發現問題（明確回報）
- **`collection-load-coordinator.js:113-196`**：jQuery Deferred 相容層（`runLoad` 注入 `deferredFactory`）正確保留 `done/fail` 多參數與 `resolveWith/rejectWith` context；同步重入（done callback 內再次 `runLoad`）、跨世代污染、dispose 期間/之後晚到回應、消費者 callback 同步拋例外時的狀態清理順序（`completeIfCurrent` 在呼叫消費者前執行，第 161-177 行），皆與對應測試案例（`weekly-report-load-adapter.test.js` 全部案例）行為一致，未發現可重現的缺口。
- 兩個 Grid owner 並行載入的隔離：以 `WeakMap` 每 owner 各自一個 coordinator，無共享可變狀態，測試已覆蓋取消/資料互不污染。
- CI 中 Six Labors 授權檔與 Plugin 簽章金鑰的建立、寫入、刪除：暫存檔路徑驗證（刪除前比對目錄與固定檔名格式）、`leaveOpen` 搭配外層 `finally` 單次 `Dispose`、`env` 變數於各步驟結束後清空，未發現機密外洩路徑。

## 總結
本批次程式邏輯（Deferred 相容層、Grid 隔離、CI 金鑰處理）設計與測試覆蓋紮實，未發現 Critical/High 等級問題。唯一具體可行動的是 Medium 等級的 `node --test` 缺少測試數下限守門，建議在下一輪處理；其餘為低風險的長期維運建議與已核實無害的既有移除項目紀錄。

---
SESSION_ID: 97be8097-d103-456d-8d45-ad093467484e

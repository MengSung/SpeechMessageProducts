# 小組回報空白：修復設計

## 已確認邊界

瀏覽器錯誤與實際 Grid adapter 相符：WebApi store 原回傳 jQuery Deferred，coordinator 經原生 Promise 轉送後失去 `.done/.fail` 與多參數。兩個 Grid 共用同一 adapter。圖表另使用自己的 DataSource，因此不得直接把 Grid 修正當作圖表根因。

## 最小修正

讓載入協調器可選擇注入 Deferred factory；只有 Grid adapter 選用既有 jQuery Deferred。日期切換仍使用原生 Promise。協調器以同一 generation 驗證成功與失敗，完整轉送原始 Deferred 的多個完成參數，保存 totalCount/summary；不保存長期資料列或更動傳輸管線。

Deferred、transport 與 callback 只由單次載入擁有，owner 仍由 WeakMap 管理。完成、失敗或 dispose 均釋放 active transport；舊世代不得影響新 owner。不能取消 generation 檢查、不能按姓名去重、不能增加第二條 AJAX。

## 靜態資源與圖表

先重現再決定。既有缺失資源不能只因磁碟缺檔就宣稱圖表根因。對照實際 formatter、樣式與資料契約；不升降版本、不下載未核定前端套件。若有需修改的證據，先記錄到本設計再擴充最小修正。

## 回復與限制

修改僅在工作區，測試全部使用合成資料。可單獨回復本任務差異，不改既有歷史或 P0-12A。沒有真實網站驗證時明確列出限制。

## 已重現的靜態資源修正

新增部署來源測試確認主版面有 14 個不存在的 JS/CSS 引用，與使用者 Console 的 404/MIME 拒絕吻合。移除這些無法載入的舊範本引用；保留上方既有有效的 dx.common/dx.light.compact 樣式、實際 dx.all 與 ASP.NET adapter，沒有換版本或本地化引擎。此修正消除已證實的缺失引用，不等同證明圖表資料非空。

IntegrateView 的 coordinator script 加 asp-append-version，避免長期 immutable 快取讓新版 adapter 使用舊版 JS。CI 加入兩份 Node 回歸測試，沿用既有 Node，不增加套件。

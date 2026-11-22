# P8-04a 設計

## 邊界

`tools/PageAcceptance/Shared/` 提供由 P6 與 P8-04 共用的有限生命週期能力：載入受限的測試組態、User Secrets 暫存／還原租約、goodshepherdback SDK fixture bridge、localhost Development Host 及 Edge 啟停。P6 保留原有情境與對照邏輯，只改由 Shared 匯入共同 owner。

`tools/PseudoLocaleAudit/` 僅負責稽核政策、頁面量測、生命週期協調、結果投影與執行入口。頁面清單固定為 ChurchSettings 的名詞、類別清單、修改紀錄和 403 頁；不得從任意使用者 URL 執行瀏覽器導覽。既有 ChurchReport 舊頁與 LIFF 明確列為 I6 排除項。

## 資料流程與信任邊界

固定頁面 manifest → localhost origin 驗證 → Edge 開啟頁面 → 讀取可見文字及 DOM 幾何 → 純值規則判定 → 僅輸出路徑、計數、固定錯誤碼與必要合成截圖。規則模組不讀秘密、不連線、不保存 DOM／HTML 全文。稽核輸出不得包含 cookie、token、帳密、測試人員個資或任意頁面原文。

如驗收需要登入／合成資料，Shared 的單一 acceptance owner 借用 User Secrets 與 SDK bridge，僅對核准測試組織及本輪自建 ID 操作。原秘密透過 repo 外 DPAPI 備份後才能暫存，完成或失敗時還原並逐位元組驗證；Browser、Context、Host、SDK process 依 owner 反向清理。任何清理狀態未知均為失敗。

## 規則契約

- 未包覆中文：只計算文字節點中的 CJK 字元；若字元不在偽語系 `［...］` 邊界內即列出元素路徑及字元計數，不保存完整原文。
- 截斷：檢查可見文字元素的 scroll extent、client extent、computed overflow 與可見矩形；允許 `overflow:auto|scroll` 的正常捲動容器。只將實際裁切文字的元素列為截斷。
- 測試可直接呼叫純規則函式，並以合成 DOM 幾何物件驗證差異；不需真實瀏覽器或外部服務。

## 失敗與相容

登入拒絕、頁面逾時、Browser／Context 關閉錯誤及 fixture 清理失敗都回固定診斷碼。每一失敗路徑仍執行 owner cleanup；如清理失敗，以非零結束並標記殘留／秘密還原未知。P6 原有四項 lifecycle fault injection 必須繼續通過。安裝的 `playwright-core` 與 Edge 僅從既有 repo 外位置載入。

# P0-ENV　指定開發測試組織（安全調查停止回報）

## 範圍
- 新增：research/p0-env-development-token-blocker.md — 靜態設定讀取路徑與不能只靠 Development 覆寫的原因。
- 新增：wp-evidence/P0-ENV/local-findings.json — 只記錄鍵名、布林狀態、程式位置與停止條件，無 token／帳密／個資。
- 修改：任務狀態、implement.md 與實作進度.html 的核定 DATA 欄位 — 記錄 P0-05 提交與本包阻擋。
- 未做：未修改任何產品 .cs／appsettings／csproj；未修改 00／01／02／04 的組織操作計畫文字；未開始 P0-06。發現停止條件後，只保存回報及狀態。

## TDD 紀錄
- 原定先寫 Development 合併後 Organization=goodshepherdback、ServerUrl 主機一致的設定測試，及設定即可阻止正式 token 的測試。
- 本包在動手修改設定前的安全調查即遇到停止條件，尚未開始上述設定紅燈／綠燈；沒有刪除測試項目，也不把源碼分析冒充已執行的測試。
- LineUtilityClass 只讀 appsettings.json 的靜態設定，不讀 Development／User Secrets；所以僅測合併後的設定值，會漏掉真正的舊推播路徑。
- 黃金檔變更：無。

## 驗證
- 僅做本機源碼與設定結構核對；沒有建立 CRM／LINE client、啟動網站、查詢 D365、讀取 User Secrets 或使用真實 token。
- 實際找到 16 處 Jesus 退回邏輯，分布於 14 個檔案；不是原提示的 9 處。
- 最近完整驗證是 P0-05：Release 建置 0 錯誤、77 警告；Release 741／741、Debug tracer 7／7；編碼測試 PS 5.1／7 各 21／21，進度頁 3／3，Changed 113 檔且 0 問題。
- 本包未完成、未提交，不宣稱通過獨立 P0-ENV 全套驗證；遇停止條件後沒有重跑 Release 全套建置與測試。
- 本次停止紀錄驗證：進度頁 Node 測試 3／3；全階段 Changed 編碼守門（Base=73b6e6cf）檢查 113 檔、排除 74 檔、0 問題；7 個修改／新增檔皆通過 UTF-8 無 BOM、CRLF、檔尾 CRLF 位元組檢查，JSON 可解析，git diff --check 通過。未追蹤的停止證據由位元組檢查逐檔涵蓋，沒有暫存或冒稱已被 Changed 掃入。

## AGENTS.md 檢查
- 未修改產品 C# 或 Razor，不改隔離、通知、資源生命週期或正式行為。
- 不在回報／證據／進度頁保存 token、帳密、收件人 ID 或查詢所得個資；只記錄必要結構及程式位置。
- 沒有連線任何 D365 組織，goodshepherdback 仍視為需正式個資保護的副本；使用者回報清理完成前禁止連線。

## 風險與待決
- 只改 Development 設定，無法限制繞過 Host 設定的靜態 LINE 讀取路徑；需要產品程式的安全防護，屬使用者指定的 P2-01 範圍。
- 本包不藉移除正式基底 token、停用檢查或只驗證設定值來假稱已安全。
- goodshepherdback 舊 Plugin／Workflow、等候作業與 LINE token 清理尚未回報完成，任何程式／工具／測試均不得連線。

## 需要使用者做的事
- 決定 P2-01 安全啟動及 LINE token 來源防護如何安排。取得新指示前維持停止，暫勿啟動 Development 網站；本包不在階段 0 擅自修改產品程式。

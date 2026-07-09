## 審查結論(Review: CCG Dual-Model 修復 — CLAUDE_MODEL 預設值 / Gemini 配額分類 / 降級回報)

已改用實際 `git diff` 內容進行審查(原任務內嵌的 diff 欄位序列化成字串 `System.Object[]`,內容遺失,詳見文末「阻塞說明」)。同時交叉比對了 `.ccg/tasks/fix-dual-model-operation/findings.md` 與實際跑出的 `summary.json` / `ccg-health-*.json` 產出,確認 Claude `CLAUDE_MODEL=sonnet` 修復與 Gemini 403/餘額不足偵測已經過實機驗證(非僅靜態推論)。

---

### Critical 🔴

- **`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:224`(同步影響 `Test-CcgDualModelHealth.ps1:151`)**
  `Test-QuotaBlockedText` 新增的正則把 `billing`、`payment required` 當成**裸字關鍵字**,沒有要求同時出現 HTTP 4xx / `error` / `exceeded` 之類的錯誤語境(對照同一條正則裡其他項目都有限定,如 `gemini api.*status\s*:?\s*403`)。
  - **失效情境**:若 `codeagent-wrapper` 或本機工具鏈因為其他原因(例如環境變數名稱、log 訊息、代理伺服器維護通知)在 stderr 印出含有 "billing" 字樣但**與供應商配額無關**的文字,`Test-BackendQuotaBlocked`(自癒腳本第 227-239 行)會誤判為 `quotaBlocked = $true`。
  - 依目前流程(第 583-593 行),一旦誤判為 `quotaBlocked`,只要另一個 backend 成功且有帶 `-AllowSingleModelWhenQuotaBlocked`,系統會直接 `degradedFallback = $true` 並以 exit code 0 結束 — **完全跳過「修復本機工具鏈後重試」這個必要行為**,把真正可修復的本機錯誤誤報成「外部配額無法修復」而接受降級成功。這正是任務要求重點檢查的「provider 失敗被誤報成功」風險。
  - **建議**:把 `billing`、`payment required` 限定在需同時匹配 4xx 狀態碼或 `error`/`insufficient`/`exceeded`/`blocked` 等詞的複合條件內,不要單獨作為觸發字。

### Warning 🟡

- **`docs/scripts/Test-CcgDualModelHealth.ps1:150-152`**
  健康檢查腳本的 `quotaBlocked` 判斷是掃描 **`StdOut + StdErr` 合併文字**,而自癒腳本 `Test-BackendQuotaBlocked`(`Invoke-CcgDualModelWithSelfHealing.ps1:237`)只掃描 **`StdErr + Diagnostic`**。兩腳本判斷範圍不一致:目前的 smoke prompt 內容簡單("Reply with exactly: ...")風險較低,但未來若 smoke prompt 或角色任務內容變得複雜(例如本次審查任務本身就會讓模型輸出大量含有 "quota"、"billing"、"429" 字眼的審查內容),健康檢查腳本會比自癒腳本更容易把**正常模型輸出**誤判為配額阻擋。建議統一只掃描 stderr(+ 明確的診斷探測輸出),不要納入模型的正式回覆內容。

- **`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:628-634`(非本次 diff 修改,但屬於本次審查明確要求的檢查項目)**
  `summary.ok = $true`(完整雙模型成功)與 `summary.degradedFallback = $true`(單模型降級)兩種情況的**process exit code 都是 0**。`Start-CcgDualModelRun.ps1` 有靠 `Write-Warning` 與解析 `summary.json` 欄位來區分(該腳本註解也明確寫了這個限制),但任何只檢查 `$LASTEXITCODE` 而不解析 `summary.json` / 不擷取 `Write-Warning` 輸出的呼叫端(常見於 CI shell 腳本),仍會把「降級成功」誤當成「完整雙模型成功」。建議至少讓 degradedFallback 使用不同的 exit code(例如沿用既有的 3,或新增一個專屬代碼),不要與完整成功共用 0。

- **`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:533` 與 `docs/scripts/Test-CcgDualModelHealth.ps1:171,193`**
  直接探測(`Invoke-GeminiDirectQuotaProbe` / `Invoke-ClaudeDirectQuotaProbe` / health 腳本內對應區塊)取得的 `diagnostic` 是**未經 `Get-ShortDiagnostic` 截斷/摘要的原始輸出**,只有在探測結果為空字串時才會 fallback 到 500 字元截斷邏輯(第 554-556 行)。Gemini `-o stream-json` 模式在真的發生錯誤時可能吐出完整 JSON 串流,會被整段塞進 `summary.json` 的 `diagnostic` 欄位,造成該檔案不可預期地膨脹,且與其他失敗路徑的診斷格式不一致。建議直接探測完的輸出也統一經過 `Get-ShortDiagnostic` 處理。

### Info 🟢

- **`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:527`**
  觸發 Gemini 直接探測的條件只排除 `-not $quotaBlocked`,沒有排除 `$result.TimedOut`。當 gemini 透過 wrapper 呼叫本身就已經 timeout(`ExitCode=124`)時,仍會再花費最多 120 秒做一次直接探測,對「已知是逾時」的案例意義不大,徒增整體 timeout 時長。可考慮加上 `-not $result.TimedOut` 條件。
- **`CLAUDE_MODEL=sonnet` 預設值**(`Invoke-CcgDualModelWithSelfHealing.ps1:74-76`、`Test-CcgDualModelHealth.ps1:252-254`)本身沒有問題 — `findings.md` 記載已用 `--model sonnet` 直接探測驗證可行,且實際 run 產出的 `health-attempt-1.json`/`summary.json` 都顯示 Claude backend 轉為 `Ok=true`。兩份腳本都用 `IsNullOrWhiteSpace` 判斷式保護,不會覆蓋使用者已自訂的 `CLAUDE_MODEL`,設計正確。
- 新增的 `failureReason` 欄位(`Get-BackendFailureReason`,兩份檔案皆有對應版本)資訊粒度合理,對除錯很有幫助,沒有發現邏輯錯誤。
- `degradedFallback` 只在 `AllowSingleModelWhenQuotaBlocked` 為真**且**至少一個 backend 產出可用輸出(`completedBackends.Count -gt 0`)時才會設為 true(第 585-591 行),完全符合任務要求的「Required Recovery Behavior」第 4 點,實作正確。

---

## 阻塞說明

任務內嵌的 `diff` 區塊實際內容是字串 `System.Object[]`(PowerShell 陣列被轉成字串時常見的序列化遺失問題),並非真正的差異內容。本次審查改為直接執行 `git diff -- docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1 docs/scripts/Test-CcgDualModelHealth.ps1` 取得真實變更並完成審查;若之後要重跑此類 CCG review 任務,建議在組裝 `<TASK>` 內容時,對 diff 物件先轉成字串(如 `($diff -join "`n")` 或 `Out-String`)再寫入任務檔,避免再次遺失差異內容。

---
SESSION_ID: 1c5d23ef-25a5-44e6-a03c-ec9c41b034aa

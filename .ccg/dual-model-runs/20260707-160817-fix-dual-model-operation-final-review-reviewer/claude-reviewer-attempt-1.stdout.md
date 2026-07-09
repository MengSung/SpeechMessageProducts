## Code Review：CCG Dual-Model 自我修復腳本（regex 收斂後）

### Critical 🔴
無。已對兩支腳本做 `Parser::ParseFile` 語法檢查，皆無語法錯誤；`summary.ok` / `degradedFallback` 判斷邏輯（`Invoke-CcgDualModelWithSelfHealing.ps1:546,570-587`）在 `quotaBlocked` 為 true 時一律強制 `backendOk=$false`，沒有發現真正後端失敗會被誤判為「完整雙模型成功」（`summary.ok=$true`）的路徑。

### Warning 🟡

- **`Test-QuotaBlockedText`（`Invoke-CcgDualModelWithSelfHealing.ps1:224`、`Test-CcgDualModelHealth.ps1:151`）** — `payment required.*(quota|balance|billing|account)` 這段仍然過寬。實測驗證：

  ```
  "Error: payment required to activate premium theme. Please update your account settings before continuing." -match $pattern  # => True
  ```

  只要同一行同時出現 "payment required" 與 "account"（即使語意完全與 provider 配額/帳務無關，例如工具本身的授權/設定錯誤），就會被判定為 `quotaBlocked`。在 `-AllowSingleModelWhenQuotaBlocked` 開啟時，這會讓一個真正的 backend/CLI bug 被誤分類為 `provider-quota-or-billing-blocked`，進而以 `degradedFallback` 之姿用 `exit 0` 回報「可接受的降級成功」，掩蓋了真正需要修的問題。建議把 `account` 這個詞從該分支移除，或要求更具體的片語（例如 `billing account`、`payment required.*balance`），縮小比對範圍。

- **初次配額判斷只看 StdErr，等於忽略 StdOut（`Invoke-CcgDualModelWithSelfHealing.ps1:517-519`，未在本次 diff 變動但與審查主題直接相關）** — 

  ```powershell
  $combined = (($result.StdOut + "`n" + $result.StdErr) -replace "`r", "")
  $diagnostic = $null
  $quotaBlocked = Test-BackendQuotaBlocked -Result $result -Diagnostic $diagnostic
  ```

  `$combined` 算出來後沒有被使用；傳給 `Test-BackendQuotaBlocked` 的 `$Diagnostic` 永遠是 `$null`，而該函式內部只檢查 `$Result.StdErr + $Diagnostic`（`Test-BackendQuotaBlocked` 函式本體），完全没检查 `$Result.StdOut`。目前靠新增的 direct probe（`Invoke-GeminiDirectQuotaProbe` / `Invoke-ClaudeDirectQuotaProbe`）補洞，但那只在 `ExitCode -ne 0` 時才觸發第二次呼叫。若 wrapper 把配額訊息印到 stdout 而非 stderr，初次判斷會漏掉，需要多等一次 direct probe 才能抓到，徒增延遲。建議直接把 `$combined` 傳進 `Test-BackendQuotaBlocked`。

- **`Test-CcgDualModelHealth.ps1` 的 `failureReason` 在「ExitCode=0 但輸出文字不符期望」時會誤標為 `backend-exit-0`（`Test-CcgDualModelHealth.ps1:200-211`）** — 該腳本判斷 `$ok` 同時要求 `ExitCode -eq 0` *且* 輸出內容符合 `ExpectedText`；但 `failureReason` 的 fallback 只用 `backend-exit-$($result.ExitCode)`，並沒有像 `Invoke-CcgDualModelWithSelfHealing.ps1` 那樣區分「沒有可用輸出」（`no-usable-output`）。結果是 CLI 明明正常結束（exit 0）卻只是回錯內容時，healthReport 會顯示 `backend-exit-0`，容易誤導成「行程失敗」而非「輸出內容不符」。建議補一個對應的 `output-mismatch` 分類，讓兩支腳本的 `failureReason` 語意一致。

### Info 🟢

- **`summary.md`（`Invoke-CcgDualModelWithSelfHealing.ps1:600-618`）沒有輸出每個 backend 的 `failureReason` / `diagnostic`**，只有 `completedBackends` / `failedBackends` 名稱陣列。人工排查時仍得打開 `summary.json` 或個別 `*-attempt-*.stderr.md`。既然這次已經加了結構化的 `failureReason` 與精簡過的 `diagnostic`，建議一併寫進 markdown（例如針對 `failedBackends` 逐項附上 `failureReason`），減少排查成本。
- **quota/billing 正則字串在兩支腳本中完整重複**（`Invoke-CcgDualModelWithSelfHealing.ps1:224` 與 `Test-CcgDualModelHealth.ps1:151`），且這次修改後長度已相當可觀。日後任何調整都要記得同步兩處，建議抽成共用的 `.psm1`／dot-source 檔案。
- **`Get-ShortDiagnostic` 與 `Test-BackendSmoke` 內的 priorityLines 篩選（`quota|billing|payment|required` 等單字，`Invoke-CcgDualModelWithSelfHealing.ps1:314`、`Test-CcgDualModelHealth.ps1:217`）** 使用了非常寬鬆的單字（尤其是裸字 `required`），僅影響「挑哪幾行當診斷訊息顯示」，不影響 `quotaBlocked` 布林值本身，風險較低，但可能把不相關、含 `required` 字樣的雜訊行擠進前 3 行，蓋掉真正有用的行。可考慮之後微調優先序（把真正的 quota/billing 詞放最前面比對）。
- **Direct quota probe 對已逾時的呼叫仍會再等 120 秒**（`Invoke-CcgDualModelWithSelfHealing.ps1:521,533` 的判斷是 `$result.ExitCode -ne 0`，涵蓋 `TimedOut` 情形，因為逾時時 `ExitCode=124`）。若原始呼叫已經是因為卡住而逾時（非配額問題），還會再花到 120 秒做二次探測，讓一次 attempt 的總時間明顯拉長。可考慮加上 `-and -not $result.TimedOut`，逾時直接標記 `timeout`，不需要二次探測。

### Summary
`CLAUDE_MODEL=sonnet` 的預設值與傳遞機制（`Initialize-CcgToolchainEnvironment` → `Invoke-ProcessCapture`/`Invoke-CommandCapture` 的 `$startInfo.Environment`）已核對正確，且與 `.ccg/tasks/fix-dual-model-operation/findings.md` 記載的實測結果一致。整體 `quotaBlocked` → `degradedFallback` → `exit 0/2/3` 的狀態機沒有讓真正失敗被誤報為「完整雙模型成功」的路徑。主要風險落在配額/帳務判斷仍偏寬鬆（`payment required.*account` 已用實測字串證實會誤判），以及 health script 的 `failureReason` 標籤在「輸出內容不符」情境下具有誤導性。建議在合併前至少收斂 `account` 這個字，其餘為非阻斷性建議。

---
SESSION_ID: 307e1ac5-7a7d-4c56-8faf-3c63e9d41d44

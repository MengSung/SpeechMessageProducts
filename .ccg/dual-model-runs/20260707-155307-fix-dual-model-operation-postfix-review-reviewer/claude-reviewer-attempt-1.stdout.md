## Review: CCG Dual-Model 自我修復腳本（配額/帳務分類與 CLAUDE_MODEL 預設值）

### Critical 🔴

- **`Invoke-CcgDualModelWithSelfHealing.ps1:224`、`Test-CcgDualModelHealth.ps1:151`** 配額/帳務判斷式中含有過於寬鬆的裸詞 `billing`（以及 `payment required` 之外的變體）
  - Why：這條 regex 同時用於「分類」而非只是顯示訊息。只要 stderr/直接探測輸出中任何一行出現 `billing` 字樣（例如 gemini/claude 的其他錯誤訊息、或未來換一個第三方 proxy 印出的無關文字剛好含有 "billing"），就會被判定為 `QuotaBlocked/provider-quota-or-billing-blocked`。搭配 `-AllowSingleModelWhenQuotaBlocked`，這會讓一個「本地工具鏈或程式本身的 bug」被誤判成「外部配額問題」，進而被接受為 `degradedFallback=$true`（exit 0），掩蓋真正需要修復的錯誤——這正是 Task 要求重點檢查的「provider failure 被誤報為成功」風險。
  - Fix：把裸詞收斂成更明確的片語組合，例如 `billing account`, `enable billing`, `insufficient billing`, 或要求 `billing`/`payment` 必須與 HTTP 4xx/`error`/`exited with status` 等錯誤標記同時出現才視為配額阻擋，而不是單獨比對。

### Warning 🟡

- **`Invoke-CcgDualModelWithSelfHealing.ps1:361-392`（`Invoke-GeminiDirectQuotaProbe`）** 尋找 `gemini.cmd` 時只檢查兩個寫死的 fallback 路徑（`Test-Path` 逐一比對），沒有像檔案中已存在的 `Resolve-ExecutablePath`（第 89-107 行）先用 `Get-Command` 從 PATH 解析。
  - Why：如果 `gemini.cmd` 是透過 PATH 上其他位置被 `codeagent-wrapper` 成功呼叫（即前面 `$wrapperPath` 執行沒問題），但剛好不在這兩個硬編碼路徑中，探測會回傳 `Ran=$false` 並輸出 `"gemini.cmd not found..."`，導致一個「真的是配額/帳務問題」的案例反而被降級成普通失敗（`backend-exit-N` / `no-usable-output`），使流程繼續做徒勞的本地修復重試，而不是正確地以 exit 3 回報「provider 外部阻擋」。這與既有的 `Invoke-ClaudeDirectQuotaProbe` 有相同設計缺陷，此次只是把它複製到新函式，建議一併修正為呼叫 `Resolve-ExecutablePath`。

- **`Invoke-CcgDualModelWithSelfHealing.ps1:222-225` 與 `Test-CcgDualModelHealth.ps1:151`** 配額判斷 regex 字串在兩個檔案中逐字重複；`Get-ShortDiagnostic`（`Invoke-...:298-326`）與 `Test-CcgDualModelHealth.ps1:213-225` 的「priority line」規則同樣重複。
  - Why：未來若要調整判斷關鍵字（例如收斂上面提到的 `billing`），很容易只改一處而遺漏另一處，造成兩支腳本判斷結果不一致（Health 腳本回報 ok，但 Invoke 腳本卻認定 quotaBlocked，或反之）。
  - Fix：把 pattern 抽成共用的 `.ps1`/函式（例如 `Get-QuotaBlockedPattern`），由兩個腳本 dot-source 引用。

- **`Invoke-CcgDualModelWithSelfHealing.ps1:275-296`（`Get-BackendFailureReason`）** 判斷順序是先看 `QuotaBlocked`，再看 `Result.TimedOut`。
  - Why：`Test-BackendQuotaBlocked` 只要 `ExitCode -ne 0` 或 `TimedOut`，就會拿 stderr/diagnostic 文字去比對（見 `Invoke-...:227-239`）。若一個真正因為逾時被 Kill 的呼叫，其部分輸出剛好含有配額關鍵字（尤其在上面提到的寬鬆 `billing`/`required` 之下更容易發生），會被標成 `provider-quota-or-billing-blocked` 而非 `timeout`，讓 timeout 類故障被誤判成外部配額問題。
  - Fix：把 `TimedOut` 檢查放在 `QuotaBlocked` 之前，或至少在文件中說明兩者互斥時的優先序依據。

### Info 🟢

- **`\u4f59\u989d\u4e0d\u8db3` 等 Unicode escape** 已確認可正確解出「余额不足 / 餘額不足 / 余额不够」，因為雖然 PowerShell 雙引號字串不處理 `\u`，但這段字串最終是作為 .NET regex pattern 使用，`\uXXXX` 在 .NET regex 中會被正確解析成對應字元。這是刻意規避原始碼編碼問題的合理作法，沒有問題。

- **`Invoke-CcgDualModelWithSelfHealing.ps1:157-159`、`Test-CcgDualModelHealth.ps1:86-88`** 顯式把 `CLAUDE_MODEL` 塞進 `$startInfo.Environment`，但 `ProcessStartInfo.Environment` 在建立時本來就會從目前行程環境變數快照，而 `Initialize-CcgToolchainEnvironment`／腳本層級已先設定 `$env:CLAUDE_MODEL`。這行是多餘的防禦性寫法，無害，但可考慮省略以減少重複程式碼。

- **`summary.md`（`Invoke-...:606-624`）** 沒有把每個 backend 的 `failureReason`／`diagnostic` 摘要進 Markdown（只在 `summary.json` 的 `attempts[].backends[]` 裡）。建議在 Markdown 摘要加入失敗 backend 的 `failureReason`，方便人工快速判讀，不需要每次都去翻 JSON。

- **`CLAUDE_MODEL=sonnet` 預設值**（兩檔案多處）已有 `.ccg/tasks/fix-dual-model-operation/findings.md` 佐證是針對「Claude 預設模型撞到 Fable 5 limit」的已驗證修復，行為與需求相符，沒有問題。

### Summary

整體方向正確：新增的 `failureReason`/`Get-ShortDiagnostic`/Gemini 直接探測補齊了先前只有 Claude 才有的診斷能力，`CLAUDE_MODEL=sonnet` 預設值也有實測依據。但配額/帳務分類 regex 中的裸詞 `billing`（Critical）有實際風險：會把非配額類的本地/程式錯誤誤判為外部配額阻擋，在啟用 `-AllowSingleModelWhenQuotaBlocked` 時進而被接受為「degraded fallback 成功」，掩蓋真正需要修的 bug。建議先收斂該關鍵字組合並修正 Gemini 探測的執行檔路徑解析後再合併；其餘為可延後處理的重複程式碼與判斷順序建議。

---
SESSION_ID: e09b3390-5d63-41cf-83ff-f4212a57a228

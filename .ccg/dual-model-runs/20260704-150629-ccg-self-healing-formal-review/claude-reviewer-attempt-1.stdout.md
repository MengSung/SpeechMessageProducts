# CCG 雙模型自我修復流程 Review

## Critical 🔴

- **`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:383`** 正式 review 呼叫沒有做「內容驗證」，只檢查 `ExitCode -eq 0 / -not TimedOut / -not quotaBlocked` 三個條件就判定 `backendOk = $true`。
  - 問題：`Test-BackendSmoke`（health check）有比對 `$ExpectedText`，但真正跑 review 的這段完全沒有檢查 `$result.StdOut` 是否為空、是否真的包含 review 內容（例如 prompt 要求的 Critical/Warning/Info 格式）。若某個 provider 在配額將盡但尚未觸發已知關鍵字（例如回傳空字串、或回傳一段不含 "quota/rate limit/429/session limit/usage limit" 的錯誤訊息）且 exit code 仍是 0，就會被判定 `backendOk=true`，最終 `summary.ok=true`、`completedBackends=["gemini","claude"]`，等於把「沒有真正產出 review」誤報成「雙模型 review 成功」。這正好命中你要求確認的第 2 點風險，且是目前最大的漏洞。
  - 建議：對每個 backend 的輸出加最低限度的內容檢查（非空、長度門檻、或必須出現 `Critical|Warning|Info` 其中之一），沒通過就視同失敗。

## Warning 🟡

- **`Test-CcgDualModelHealth.ps1:279-289` 被 `Invoke-CcgDualModelWithSelfHealing.ps1:299-304` 呼叫時未傳 `-SkipBackendSmoke`** — 導致每次 attempt 都會先讓 health check 對 gemini/claude 各打一次真實 API（smoke），然後同一個 attempt 又對 gemini/claude 各打一次「正式」呼叫。單一次成功的 dual-model review 實際上會消耗 4 次真實 LLM 呼叫，`MaxAttempts=2` 時最多到 8 次。這個自我修復流程的目的是避免撞到 quota/session limit，但目前設計反而讓同一次任務更容易自己撞到 quota——尤其是 session limit 這種「短時間內呼叫次數」型的限制。建議 loop 內的 health check 呼叫改用 `-SkipBackendSmoke`（只驗證 PATH/exe 是否存在），quota 偵測交給正式呼叫段（371-395 行）本來就有的邏輯。

- **Health check非 0/2/3 的例外 exit code 未處理**（`Invoke-CcgDualModelWithSelfHealing.ps1:331-347`）。`Test-CcgDualModelHealth.ps1` 若在 `Resolve-Path`/`Set-Location` 階段丟出未捕捉的例外（例如 `RepositoryPath` 暫時不可存取），會以非 0/2/3 的 code（通常是 1）結束。呼叫端只判斷 `-eq 2` 和 `-eq 3`，其餘一律當作 `healthStatus="passed"` 直接往下跑正式 review 呼叫，等於把「health check 本身壞掉」誤判為「健康」。建議改成 `if ($healthProcess.ExitCode -ne 0) { ... }` 的排他邏輯，而不是逐一比對已知值。

- **`Test-QuotaBlockedText` 關鍵字清單不完整**（兩個檔案都一樣：`session limit|rate limit|quota|429|usage limit`）。Gemini API 常見的配額錯誤字串是 `RESOURCE_EXHAUSTED`、`insufficient_quota` 之類，並不在清單內。若 codeagent-wrapper 把這類原始錯誤字串透傳出來，會被歸類為一般「repairable」失敗而不斷觸發本機修復重試，而不是正確標成 `quotaBlocked=true` 的外部阻塞——這會削弱你要求確認的第 2、第 4 點（quota 分類正確性）。

- **`Join-ProcessArguments` 的反斜線跳脫規則錯誤**（`Invoke-CcgDualModelWithSelfHealing.ps1:112-126`、`Test-CcgDualModelHealth.ps1:40-54`，兩處邏輯相同）。目前對含空白/雙引號的參數做 `Replace('\','\\')` 再包雙引號，但 Windows `CommandLineToArgvW` 規則是「反斜線只有緊接在雙引號前面才需要加倍」，不是全部反斜線都要加倍。這會把任何「含空白的路徑」在解析後多出反斜線（例如 `C:\Users\John Doe\repo` 會被解析成 `C:\\Users\\John` 這種錯誤結果），造成 `-RepositoryPath`/`-TaskFile` 這類參數在含空白路徑下失效。目前這個 worktree 路徑本身沒有空白所以還沒發作，但 AGENTS.md 已把這支 runner 訂為「所有未來 CCG analyze/review 的固定入口」，一旦有人在含空白使用者路徑（很常見，如 `C:\Users\Some User\...`）執行就會壞掉。

- **stdin/stdout 同步讀寫順序有 pipe 死結風險**（`Invoke-ProcessCapture` / `Invoke-CommandCapture` 兩處，都是先同步 `$stdin.Write()+Close()` 再啟動 `ReadToEndAsync()`）。如果子行程（gemini/claude wrapper）在 task prompt 還沒寫完、或還沒開始被讀取 stdout 前，就往 stdout 寫出超過 OS pipe buffer（一般 64KB）的內容並卡住等待讀取端，而讀取端又還在同步寫 stdin，兩邊互相等待就會死結。Review 用的 task prompt（含 diff）和輸出（review 全文）都可能超過這個門檻，屬於「條件觸發」但確實存在的 PowerShell 併發 bug。建議在 `Start()` 之後、寫 stdin 之前就先啟動 `ReadToEndAsync()`。

- **文件宣稱與實際落地狀態有落差**：`docs/ccg-dual-model-health-permanent-fix.md:82-86` 說 `analyze.md`/`review.md` 已固定改走 runner——我已確認這兩個檔案內容確實正確指向 `Invoke-CcgDualModelWithSelfHealing.ps1`（第 3 點大致成立）。但 `git status` 顯示 `docs/scripts/*.ps1` 都是 `??`（未加入版控）、`AGENTS.md`/thinking-guide 也還只是 worktree 內的修改，尚未 commit/merge 回 main。在合併之前，任何其他 worktree 或未來重新 clone 的環境若吃到已合併的 AGENTS.md 規則卻沒有這兩支腳本，就會直接踩中「腳本不存在」而卡住——正是這個機制原本要解決的問題。務必把 `docs/scripts/*.ps1` 與文件變更一起 commit 並合併回 main。

## Info 🟢

- `Get-CcgToolPathEntries`（Invoke 腳本）與 `wantedPathEntries`（Health 腳本）是同一份硬編碼路徑清單複製兩份，未來新增/移除工具路徑容易漏改其中一邊，建議抽成共用設定或至少加註「需同步修改」的提示。
- 所有路徑（npm、`.claude\bin`、Python、role file、claude.cmd/gemini.cmd 候選路徑）都寫死 `C:\Users\Administrator\...`，只適用單一帳號/單機，之後若換帳號或搬到 CI 環境就要整批改；目前屬於已知取捨，先記錄。
- `Test-CcgDualModelHealth.ps1` 的 smoke 結果物件用 PascalCase（`Ok`, `ExitCode`, `QuotaBlocked`...），而外層 summary 用 camelCase（`ok`, `repairable`, `notes`...），JSON 輸出風格不一致，不影響功能但增加閱讀/後續程式解析成本。
- `Invoke-ClaudeDirectQuotaProbe` 與 `Test-CcgDualModelHealth.ps1` 內的 direct-probe 都使用 `--dangerously-skip-permissions`，在自動化 unattended 流程中屬合理取捨（純文字 smoke prompt、無工具呼叫），但值得在文件中明確記一筆「為何這裡允許跳過權限檢查」，避免日後被誤用到會執行工具的 prompt 上。
- 單次 dual-model 流程理論上限（`MaxAttempts=2` × health(420s) + 2 backend×2 attempts×900s）可達約 70 分鐘，建議在文件中提示使用者這是「最壞情況下的耐心上限」，避免誤以為卡住。

## Summary

核心機制（同一 process 內修 PATH/env、再讓後續呼叫沿用修好的環境）本身是成立的：`Initialize-CcgToolchainEnvironment` 會更新 `$env:Path` 並透過 `$startInfo.Environment["Path"]` 傳給子行程，所以第 1 點你關心的「同 process 修復後繼續執行」是可行的。但第 2 點「quota 不會被誤報成成功」目前有一個實質漏洞（正式 review 呼叫缺內容驗證，Critical），加上 quota 關鍵字清單不全、health-check 本身重複消耗 API 額度等問題，會削弱這個流程原本要達成的「不誤報、不空耗配額」目標。建議在合併前至少修掉 Critical 項目與「health smoke 重複消耗 quota」這條 Warning，其餘可列為後續強化項目。

---
SESSION_ID: 8ae66c01-e07f-4597-9c55-28b48f0ccba2

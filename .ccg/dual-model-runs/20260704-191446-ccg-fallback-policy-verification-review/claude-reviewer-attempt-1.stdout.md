# CCG Fallback Policy Verification Review

### Critical
* **本次驗證從未真正觸發 quota/session blocked**：兩個 run（`20260704-191410`、`20260704-191446`）的 health check smoke 測試皆回報 `ok:true`、`QuotaBlocked:false`，gemini backend 也正常完成並自行下結論「驗證成功、主 backend 失效時輸出被保留」——但這是沒有實際失敗事件佐證的臆測，不應被當作「已驗證」。
* **偵測到針對同一 taskFile 的多重並行呼叫**：目前系統上至少有 3 個 `Invoke-CcgDualModelWithSelfHealing.ps1` 行程同時針對同一個 `ccg-fallback-policy-verification-review.md` 與同一個 `-OutputDirectory .\.ccg\dual-model-runs` 執行，各自又各自啟動一組 gemini/claude `codeagent-wrapper` 子行程。這會重複消耗兩個 provider 的 quota/session，與本次要驗證「節省額度」的目的直接衝突，也可能造成 run 目錄競爭寫入。

### Warning
* 讀 `Invoke-CcgDualModelWithSelfHealing.ps1` 原始碼確認：若 quota blocked 是在 **health check 階段**就被判定（`ExitCode -eq 3`）且未帶 `-AllowSingleModelWhenQuotaBlocked`，runner 會在呼叫任何 backend 之前直接 `break`——此情境下根本不存在「另一個已成功 backend 的輸出」可保留，因為兩個 backend 都還沒被呼叫。Fallback 保留機制實際上只在「backend 執行後才偵測到 quota blocked」時才有意義，這點文件與程式行為不完全對齊，建議在手冊中補充說明。
* `20260704-191410` run 目錄殘留不完整（只有 `health-attempt-1.json` + gemini prompt，無任何 stdout/stderr），推測與上述重複呼叫有關；建議加上同一 taskFile 的執行鎖（lock file），避免多重呼叫浪費額度與造成殘留 artifacts。

### Info
* 程式碼面確認：`Invoke-ProcessCapture` 的結果會先寫入 `stdout.md`/`stderr.md`，之後才做 quota 判斷，所以「物理保留輸出」本身沒問題；真正的分歧點在於最終 `summary.ok` / `degradedFallback` 旗標是否正確通知上游「可用單模型結果繼續」。
* 本次觀察到的 3 個並行行程都確實有帶 `-AllowSingleModelWhenQuotaBlocked`，符合 `docs/ccg-dual-model-health-permanent-fix.md` 第 148 行的建議預設值。
* 建議：待某次 run 的 `summary.json` 真的出現 `quotaBlocked=true` 且 `degradedFallback=true` 時，再以該 run 的實際 artifacts 重新確認「輸出保留＋任務繼續」端到端成立；目前結論只能停留在「程式碼邏輯支持此設計」，尚無本次 run 的真實失敗案例佐證。

---
SESSION_ID: 7409afbb-3960-4626-b3c5-2ad25d757505

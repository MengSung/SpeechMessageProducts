# 審查結果:CCG Dual-Model Runner(warning fixes 之後)

## Critical 🔴
無。分類邏輯本身(`Test-QuotaBlockedText`、`Test-BackendQuotaBlocked` 對 `ExitCode -eq 0` 的 guard)沒有發現會造成誤判為 quota/billing 的明顯正確性錯誤;`payment required.*(quota|balance|billing)` 一類的規則刻意排除單獨的 403,符合「避免 generic 403 誤判」的要求。

## Warning 🟡

- **`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:527` 與 `:539`**
  `$diagnostic = $directProbe.Output` 是**無條件**覆寫,不論 `$directProbe.QuotaBlocked` 是 `true` 還是 `false`。
  - Why:當 direct probe 沒有偵測到 quota/billing(例如真正原因是本機 toolchain 壞掉、wrapper 設定錯誤等非配額問題),summary.json 裡的 `diagnostic` 欄位會變成**這次額外探測呼叫**的輸出(可能是 `"GEMINI_DIRECT_QUOTA_PROBE_OK"` 或另一個不相干的錯誤),而不是原始 `$result.StdOut/$result.StdErr` 的真正失敗原因。`failureReason` 會正確標成 `no-usable-output`/`backend-exit-N`,但 `diagnostic` 卻可能顯示看似正常的探測回應,造成除錯時的誤導。
  - Fix:僅在 `$directProbe.QuotaBlocked` 為 `true` 時才用探測輸出覆寫 `$diagnostic`;探測未確認為配額問題時,應保留原始 `$result` 的錯誤文字(可透過 `Get-ShortDiagnostic` 處理後寫入),讓非配額失敗仍有可用的診斷資訊。

- **`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:527` 與 `:539`(附帶問題)**
  由於上述覆寫繞過了新加入的 `Get-ShortDiagnostic`(它只在 `$diagnostic` 仍為空白時才會被呼叫,見 `:548-550`),當走到「靠 direct probe 才判定為 quota blocked」這條路徑時,`diagnostic` 會是探測呼叫(`-o stream-json`)的完整原始輸出,長度不受 500 字元上限保護,可能讓 `summary.json`/health JSON 檔案異常肥大。建議統一用 `Get-ShortDiagnostic` 處理 probe 輸出。

- **`docs/scripts/Test-CcgDualModelHealth.ps1:156-176`(新增的 Gemini direct probe 區塊)**
  觸發條件是 `$Backend -eq "gemini" -and -not $ok -and -not $quotaBlocked`,**沒有** `$result.ExitCode -ne 0` 的限制,與同一份 diff 在 `Invoke-CcgDualModelWithSelfHealing.ps1:521`(`-and $result.ExitCode -ne 0`)採用的正確寫法不一致。
  - Why:若原本呼叫 `ExitCode -eq 0` 但輸出文字沒對到 `ExpectedText`(純粹的 output mismatch),目前仍會觸發一次全新、不相關的 Gemini 直接探測呼叫;若該次探測剛好命中 billing/quota 關鍵字(或探測本身也失敗),會把一個「其實只是回覆內容對不上」的健康 backend 誤判為 `provider-quota-or-billing-blocked`,正是這次 review 特別要求要避免的情境(「Avoiding false degraded fallback for … output mismatches」)。
  - Fix:比照 `Invoke-CcgDualModelWithSelfHealing.ps1` 的寫法,將 Gemini(以及既有的 Claude)探測條件都加上 `-and $result.ExitCode -ne 0`,只在真正呼叫失敗時才觸發額外探測。

## Info 🟢

- **`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:314` 與 `docs/scripts/Test-CcgDualModelHealth.ps1:220`**
  `Get-ShortDiagnostic`/inline priority-line 正則中包含單獨的 `required` 關鍵字,範圍過廣(例如 `"apiKey is required"`、`"argument is required"` 等與配額無關的訊息也會被判定為「優先行」)。這不影響 `quotaBlocked` 的分類(分類用的是另一組更嚴謹的 `Test-QuotaBlockedText`),但會讓截斷後的診斷文字容易挑到不相關的行,削弱這次新增診斷功能的效果。建議改成更具體的片語,例如 `payment required` 或 `billing.*required`。

- **`docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1:74-76` 與 `:157-159`**
  `CLAUDE_MODEL` 預設值 `"sonnet"` 的邏輯正確且可被既有環境變數覆寫,符合預期;`Invoke-ProcessCapture` 內對子行程再次顯式設定 `CLAUDE_MODEL` 屬多餘(該值已經會透過 `ProcessStartInfo.Environment` 從目前行程環境繼承),但無害,非必要修正。

## Summary

主要邏輯(quota/billing 關鍵字擴充、`CLAUDE_MODEL=sonnet` 預設值、`failureReason`/`FailureReason` 欄位、degraded fallback 條件)整體正確且與 `.ccg/tasks/fix-dual-model-operation/findings.md` 記錄的真實案例(Gemini `余额不足` 403)吻合。建議在合併前修正兩個 Warning:(1)direct probe 未確認 quota 時不應覆寫/繼承原始診斷文字,且應套用 `Get-ShortDiagnostic` 避免欄位過大;(2)`Test-CcgDualModelHealth.ps1` 的 Gemini 探測應加上 `ExitCode -ne 0` 條件,避免對「純輸出不符」的健康 backend 誤判為配額封鎖。這兩點修正成本低,且直接對應本次 review 明確要求的重點,建議修完後再進行下一輪驗證。

---
SESSION_ID: 2b797bfd-cc48-4de9-a2b2-aef0ffcf7ab4

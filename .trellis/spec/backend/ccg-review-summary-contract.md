# CCG Review Summary Contract

## 1. Scope / Trigger

Applies when a consumer interprets the output of the CCG self-healing runner. A successful requested single-model run must never be presented as a completed dual-model review.

## 2. Signatures

- Entry: docs/scripts/Start-CcgDualModelRun.ps1; optional -ClaudeOnly selects Claude explicitly.
- Metadata: Get-CcgReviewCompletion -ClaudeOnly:<bool> -CompletedBackends <string[]>.
- Rendering: ConvertTo-CcgRunMarkdown -Summary <record>.

## 3. Contracts

- reviewMode is dual or claude-only.
- ok means every requested backend produced verified usable output. It does not identify dual-model completion.
- dualModelComplete is true only when both gemini and claude completed with usable output. Consumers must use this field to claim dual-model completion.
- summary.json and summary.md must both expose reviewMode and dualModelComplete.
- diagnostic retains the failing wrapper's own diagnostic; probeDiagnostic and probe separately identify the independent probe result and its artifact path.
- Pure metadata functions retain no prompts, identity, credentials, tasks or process handles. All state belongs to the current call.
- A user-delivered manual review is recorded as Claude 單模型人工審查（使用者交付） with the reviewed diff SHA256. It must not change a failed automatic run into a successful one.

## 4. Validation / Error Matrix

| Requested / completed | ok | dualModelComplete | Result |
|---|---|---|---|
| dual / gemini and claude | true | true | Completed dual review |
| claude-only / claude | true | false | 單模型（使用者指定 ClaudeOnly） |
| dual / claude only | false | false | Incomplete or explicit quota fallback |
| claude-only / none | false | false | Incomplete review |

Exit code 0 can also represent accepted quota fallback; it is never a substitute for dualModelComplete. A successful probe does not make the original failed review successful.

## 5. Good / Base / Bad Cases

- Good: display the selected reviewMode and inspect dualModelComplete independently.
- Base: preserve a usable Claude-only result without invoking Gemini when the user selected ClaudeOnly.
- Bad: claim both models completed because ok or the process exit code indicates success.

## 6. Required Tests

Run powershell -NoProfile -File docs/scripts/Tests/Test-CcgReviewSummary.ps1. Its six cases verify complete dual output, complete explicit single output, partial output, missing output, JSON metadata and Markdown metadata without invoking external models.

Phase 0 runs external review once after P0-11 and before P0-12. Between work packages, use local validation. If the stage-end entrypoint fails or times out, preserve the review packet and stop for the user's review result.

## 7. Wrong / Correct

Wrong: treat summary.ok or exit code 0 as proof that Gemini and Claude both completed.

Correct: claim dual completion only when summary.dualModelComplete is true; otherwise describe the actual reviewMode or fallback state explicitly.


## 2026-11-05 週額度診斷補充

`You've hit your weekly limit` 與 session limit 同屬 provider quota，不能當作 PATH／本機工具故障反覆修復。
runner 與 health 的 regex 必須一致；只在真實失敗／probe診斷分類，普通 weekly report 或任意 exit1 不算。
離線回歸 `powershell -NoProfile -File docs/scripts/Tests/Test-CcgWeeklyQuota.ps1` 必須7/7，
既有 `Test-CcgReviewSummary.ps1` 六案不變。兩者只擷取AST純函式／字串，不啟動模型或讀取憑證。
真實自癒執行若 `quotaBlocked=true`、`degradedFallback=true`，仍 `dualModelComplete=false`，
只能稱「Claude週額度阻擋，Gemini單模型降級」。不得把診斷修復說成提高額度、繞過限制或取得Claude審查。

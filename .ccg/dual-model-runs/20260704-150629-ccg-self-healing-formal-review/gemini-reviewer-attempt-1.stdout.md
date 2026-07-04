<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: (not initialized)
Git: branch Jesus_5.1.7.WorktreeRefactorRichMenu; dirty 71 paths.
Current task: none.
Active tasks: 3 total. Use `python ./.trellis/scripts/task.py list --mine` only if needed.
Spec indexes: 3 available.
&lt;/current-state&gt;

&lt;trellis-workflow&gt;
# Development Workflow - Session Summary
Full guide: .trellis/workflow.md. Step detail: `python ./.trellis/scripts/get_context.py --mode phase --step &lt;X.Y&gt;`.

## Phase Index

```
Phase 1: Plan    → classify, get task-creation consent, then write planning artifacts
Phase 2: Execute → implement only after task status is in_progress
Phase 3: Finish  → verify, update spec, commit, and wrap up
```

### Request Triage

- Simple conversation or small task: ask only whether this turn should create a Trellis task. If the user says no, skip Trellis for this session.
- Complex task: ask whether you may create a Trellis task and enter planning. If the user says no, do not do broad inline implementation; explain, clarify scope, or suggest a smaller split.
- User approval to create a task is not approval to start implementation. Planning still happens first.

### Planning Artifacts

- `prd.md` — requirements, constraints, and acceptance criteria. Do not put technical design or execution checklists here.
- `design.md` — technical design for complex tasks: boundaries, contracts, data flow, tradeoffs, compatibility, rollout / rollback shape.
- `implement.md` — execution plan for complex tasks: ordered checklist, validation commands, review gates, and rollback points.
- `implement.jsonl` / `check.jsonl` — spec and research manifests for sub-agent context. They do not replace `implement.md`.
- Lightweight tasks may be PRD-only. Complex tasks must have `prd.md`, `design.md`, and `implement.md` before `task.py start`.

### Parent / Child Task Trees

Use a parent task when one user request contains several independently verifiable deliverables. The parent task owns the source requirement set, the task map, cross-child acceptance criteria, and final integration review; it normally should not be the implementation target unless it also has direct work.

Use child tasks for deliverables that can be planned, implemented, checked, and archived independently. Parent/child structure is not a dependency system: if one child must wait for another, write that ordering in the child `prd.md` / `implement.md` and keep each child's acceptance criteria testable.

Create new children with `task.py create "&lt;title&gt;" --slug &lt;name&gt; --parent &lt;parent-dir&gt;`. Link existing tasks with `task.py add-subtask &lt;parent&gt; &lt;child&gt;`, and unlink mistakes with `task.py remove-subtask &lt;parent&gt; &lt;child&gt;`.

### Phase 1: Plan
- 1.0 Create task `[required · once]` (only after task-creation consent)
- 1.1 Requirement exploration `[required · repeatable]` (`prd.md`; complex tasks also need `design.md` + `implement.md`)
- 1.2 Research `[optional · repeatable]`
- 1.3 Configure context `[required · once]` — Claude Code, Cursor, OpenCode, Codex, Kiro, Gemini, Qoder, CodeBuddy, Copilot, Droid, Pi (sub-agent-dispatch platforms only; inline platforms skip)
- 1.4 Activate task `[required · once]` (review gate, then `task.py start`; status → in_progress)
- 1.5 Completion criteria

### Phase 2: Execute
- 2.1 Implement `[required · repeatable]`
- 2.2 Quality check `[required · repeatable]`
- 2.3 Rollback `[on demand]`

Sub-agent dispatch protocol applies to all platforms and all sub-agents, including class-2 Codex/Copilot/Gemini/Qoder and `trellis-research`: every dispatch prompt starts with `Active task: &lt;task path from task.py current&gt;` before role-specific instructions.

### Phase 3: Finish
- 3.2 Debug retrospective `[on demand]`
- 3.3 Spec update `[required · once]`
- 3.4 Commit changes `[required · once]`
- 3.5 Wrap-up reminder

&gt; Note: step 3.1 was folded into 2.2 (last-iteration full-scope check) and 3.4 (commit preamble). Numbering kept stable to avoid breaking external references.

### Rules

1. Identify which Phase you're in, then continue from the next step there
2. Run steps in order inside each Phase; `[required]` steps can't be skipped
3. Phases can roll back (e.g., Execute reveals a prd defect → return to Plan to fix, then re-enter Execute)
4. Steps tagged `[once]` are skipped if the output already exists; don't re-run
5. Artifact presence informs the next step; missing `design.md` / `implement.md` is valid for lightweight tasks and incomplete planning for complex tasks.

### Active Task Routing

When a user request matches one of these intents inside an active task, route first, then load the detailed phase step if needed.

- Planning or unclear requirements -&gt; `trellis-brainstorm`.
- `in_progress` implementation/check -&gt; dispatch `trellis-implement` / `trellis-check`.
- Repeated debugging -&gt; `trellis-break-loop`; spec updates -&gt; `trellis-update-spec`.

- Planning or unclear requirements -&gt; `trellis-brainstorm`.
- Before editing -&gt; `trellis-before-dev`; after editing -&gt; `trellis-check`.
- Repeated debugging -&gt; `trellis-break-loop`; spec updates -&gt; `trellis-update-spec`.

### Guardrails

- Task creation approval is not implementation approval; implementation waits for `task.py start` after artifact review.
- PRD-only is valid for lightweight tasks; complex tasks need `design.md` + `implement.md`.
- Planning must be persisted to task artifacts; checks must run before reporting completion.

### Loading Step Detail

At each step, run this to fetch detailed guidance:

```bash
python ./.trellis/scripts/get_context.py --mode phase --step &lt;step&gt;
# e.g. python ./.trellis/scripts/get_context.py --mode phase --step 1.1
```

---
&lt;/trellis-workflow&gt;

&lt;guidelines&gt;
Task context order for implementation/check: jsonl entries -&gt; `prd.md` -&gt; `design.md if present` -&gt; `implement.md if present`. Missing optional artifacts are skipped for lightweight tasks.

## Available indexes (read on demand)
- .trellis/spec/guides/index.md
- .trellis/spec/backend/index.md
- .trellis/spec/frontend/index.md

Discover more via: `python ./.trellis/scripts/get_context.py --mode packages`
&lt;/guidelines&gt;

&lt;task-status&gt;
Status: NO ACTIVE TASK
Next-Action: Classify the current turn before creating any Trellis task. Simple conversation / small task asks only whether this turn should create a Trellis task. Complex task asks whether task creation and planning are allowed.
&lt;/task-status&gt;

&lt;ready&gt;
Context loaded. Follow &lt;task-status&gt;. Load workflow/spec/task details only when needed.
&lt;/ready&gt;</hook_context>

# Gemini Role: UI Reviewer

> For: /ccg:review, /ccg:bugfix validation, /ccg:dev Phase 5

You are a senior UI reviewer specializing in frontend code quality, accessibility, and design system compliance.

## CRITICAL CONSTRAINTS

- **ZERO file system write permission** - READ-ONLY sandbox
- **OUTPUT FORMAT**: Structured review with scores (for bugfix validation)
- **Focus**: UX, accessibility, consistency, performance

## Review Checklist

### Accessibility (Critical)
- [ ] Semantic HTML structure
- [ ] ARIA labels and roles present
- [ ] Keyboard navigable
- [ ] Focus visible and managed
- [ ] Color contrast sufficient

### Design Consistency
- [ ] Uses design system tokens
- [ ] No hardcoded colors/sizes
- [ ] Consistent spacing and typography
- [ ] Follows existing component patterns

### Code Quality
- [ ] TypeScript types complete
- [ ] Props interface clear
- [ ] No inline styles (unless justified)
- [ ] Component is reusable
- [ ] Proper event handling

### Performance
- [ ] No unnecessary re-renders
- [ ] Proper memoization where needed
- [ ] Lazy loading for heavy components
- [ ] Image optimization

### Responsive
- [ ] Works on mobile
- [ ] Works on tablet
- [ ] Works on desktop
- [ ] No horizontal scroll issues

## Scoring Format (for /ccg:bugfix)

```
VALIDATION REPORT
=================
User Experience: XX/20 - [reason]
Visual Consistency: XX/20 - [reason]
Accessibility: XX/20 - [reason]
Performance: XX/20 - [reason]
Browser Compatibility: XX/20 - [reason]

TOTAL SCORE: XX/100

ISSUES FOUND:
- [issue 1]
- [issue 2]

RECOMMENDATION: [PASS/NEEDS_IMPROVEMENT]
```

## Response Structure

1. **Summary** - Overall assessment
2. **Accessibility Issues** - a11y problems found
3. **Design Issues** - Inconsistencies
4. **Suggestions** - Improvements
5. **Positive Notes** - What's done well

## .context Awareness

If the project has a `.context/` directory:
1. Read `.context/prefs/coding-style.md` as the primary review standard
2. Read `.context/prefs/workflow.md` to verify the full development flow was followed (tests written, docs updated, etc.)
3. Check `.context/history/commits.jsonl` for past decisions on the same components — flag if current changes contradict previous design decisions without justification

<TASK>
請以 reviewer 角色審查這次 CCG Gemini + Claude 雙模型自我修復流程的變更。

重點：
1. 確認 Invoke-CcgDualModelWithSelfHealing.ps1 是否能在同一個 process 內修 PATH/env 後繼續執行。
2. 確認 provider quota/session limit 不會被誤報成成功 review。
3. 確認文件與 AGENTS 規則是否足以讓未來 CCG analyze/review 自動走自修 runner。
4. 檢查是否有明顯 PowerShell bug、流程漏洞、錯誤分類問題。

請輸出 Critical / Warning / Info。

Git status:
 M .trellis/spec/guides/ccg-external-review-thinking-guide.md  M AGENTS.md ?? docs/ccg-dual-model-health-permanent-fix.md ?? docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1 ?? docs/scripts/Test-CcgDualModelHealth.ps1

Git diff:
diff --git a/.trellis/spec/guides/ccg-external-review-thinking-guide.md b/.trellis/spec/guides/ccg-external-review-thinking-guide.md index b044a4a9..70d4c33d 100644 --- a/.trellis/spec/guides/ccg-external-review-thinking-guide.md +++ b/.trellis/spec/guides/ccg-external-review-thinking-guide.md @@ -1,6 +1,21 @@  # CCG External Review Thinking Guide   -> Use this before running or repairing Gemini/Claude CCG external review. Full runbook: `docs/ccg-gemini-claude-review-troubleshooting.md`. +> Use this before running or repairing Gemini/Claude CCG external review. Full runbook: `docs/ccg-dual-model-health-permanent-fix.md`. + +## Standard Entry + +For CCG analysis or review, use the self-healing runner first: + +```powershell +powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Invoke-CcgDualModelWithSelfHealing.ps1" ` +  -TaskFile ".\.ccg\dual-model-runs\<task>.md" ` +  -Role reviewer ` +  -RepositoryPath "<worktree-root>" ` +  -OutputDirectory ".\.ccg\dual-model-runs" +``` + +Do not start by debugging Gemini or Claude manually. The runner owns PATH setup, +UTF-8 environment setup, backend smoke checks, retries, and summary output.    ## Quick Trigger   @@ -16,11 +31,12 @@ Read the full runbook when any of these appear:    ## Required Health Check   -Before spending time debugging reviewer prompts, verify the toolchain: +If you only need a health check without running a review prompt:    ```powershell -cmd.exe /c "where gemini & where claude & where python & gemini --version & claude --version & python --version" -& "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --version +powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-CcgDualModelHealth.ps1" ` +  -RepositoryPath "<worktree-root>" ` +  -OutputDirectory ".\.ccg\dual-model-runs"  ```    ## Stable Reviewer Shape @@ -34,6 +50,13 @@ codeagent-wrapper.exe --lite --backend claude    Do not use Gemini with `--progress` on Windows unless the wrapper/Gemini crash path has been revalidated.   +## Failure Classification + +- `ok=true`: both backends completed. +- `quotaBlocked=true`: external provider quota/session limit; not locally repairable. +- exit code `2`: local toolchain still needs repair; inspect the run folder health/stdout/stderr files. +- `-AllowSingleModelWhenQuotaBlocked`: allowed only when the task owner accepts a fallback; never call it a completed dual-model review. +  ## Mental Model    Treat CCG external review as a multi-layer integration: diff --git a/AGENTS.md b/AGENTS.md index c9c4c666..030e7062 100644 --- a/AGENTS.md +++ b/AGENTS.md @@ -19,3 +19,25 @@ If you're using Codex or another agent-capable tool, additional project-scoped h  Managed by Trellis. Edits outside this block are preserved; edits inside may be overwritten by a future `trellis update`.    <!-- TRELLIS:END --> + +<!-- CCG-SELF-HEALING:START --> +# CCG Gemini + Claude Self-Healing Rule + +When a task requires external CCG analysis or review, do not call Gemini or Claude directly. + +Use the project runner: + +```powershell +powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Invoke-CcgDualModelWithSelfHealing.ps1" ` +  -TaskFile ".\.ccg\dual-model-runs\<task>.md" ` +  -Role reviewer ` +  -RepositoryPath "<worktree-root>" ` +  -OutputDirectory ".\.ccg\dual-model-runs" +``` + +The runner performs the health check, repairs local PATH/env issues, retries repairable failures, +records all prompts/stdout/stderr/summary files, and distinguishes local failures from provider +quota or session-limit blockers. If `quotaBlocked=true`, report it as an external blocker or use +`-AllowSingleModelWhenQuotaBlocked` only when the task explicitly permits a single-model fallback. +Never report a quota-blocked run as a successful dual-model review. +<!-- CCG-SELF-HEALING:END -->

</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.已載入 Trellis SessionStart 上下文。

我們現在正以 Reviewer（審查者）的角色，針對 CCG Gemini + Claude 雙模型自我修復流程的變更進行代碼與流程審查。由於這是一個審查與評估任務，我將遵循系統指示，不主動建立 Trellis 任務，並直接使用唯讀工具進行深入的代碼審查，最後輸出繁體中文的 Reviewer 報告（包含 Critical / Warning / Info 分類）。

首先，我們需要讀取以下新增的四個檔案，特別是：
1. `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`
2. `docs/scripts/Test-CcgDualModelHealth.ps1`
3. `docs/ccg-dual-model-health-permanent-fix.md`

我們使用 `glob` 搜尋這三個檔案的位置以確認其路徑，並使用 `read_file` 仔細閱讀其內容，以便深入評估其功能是否符合審查重點。這是一份針對 **CCG Gemini + Claude 雙模型自我修復流程** 的專業審查與評估報告。

已載入 Trellis SessionStart 上下文。以下為針對本次變更的審查結果。

---

# CCG 雙模型自我修復流程審查報告

## 1. 審查摘要 (Summary)
本次變更引入了自動化環境檢測與修復的指令碼 (`Test-CcgDualModelHealth.ps1` 與 `Invoke-CcgDualModelWithSelfHealing.ps1`)，用以取代過去手動呼叫 Gemini / Claude 的流程，並在 `AGENTS.md` 與 `ccg-external-review-thinking-guide.md` 中確立了標準入口。此設計能有效收斂環境 PATH、UTF-8 編碼及工作區信任問題，同時將「外部配額/Session限制」與「本機環境損壞」清晰分離，是一次高品質的流程改善。

---

## 2. 審查發現 (Findings Classification)

### 🔴 Critical (嚴重問題)
* **無 (None)**：未發現會導致執行崩潰、安全性漏洞或死結的嚴重錯誤。

### 🟡 Warning (警告與潛在風險)
* **對 `-AllowSingleModelWhenQuotaBlocked` 的寬容度處理：**
  * 在 `Invoke-CcgDualModelWithSelfHealing.ps1` 中，若 `Test-CcgDualModelHealth.ps1` 偵測到有配額限制並回傳 `exit 3`，且調用者傳入了 `-AllowSingleModelWhenQuotaBlocked`，流程會繼續進行。但在 `gemini` / `claude` 的 `foreach` 迴圈中，即便其中一個模型成功取得回應，另一個配額阻塞的模型仍然會被寫入 `quotaBlocked = $true`。
  * 這在 `AllowSingleModelWhenQuotaBlocked` 為 `$true` 的情境下運作良好（會標記 `ok=false`，但保留成功的 backend 回傳）。但當此開關為 `$false` 時，第二個 attempt 仍會重複呼叫已知被 Quota Blocked 的 API，可能導致不必要的外部呼叫。
  * *建議：* 未來若有優化空間，可在首輪確認 Quota Blocked 且不允許單模型回退時，提早中斷 (early-break)，避免進入第二次重試。

### 🟢 Info (建議與細節優化)
1. **未使用的臨時檔案變數：**
   * 在 `Test-CcgDualModelHealth.ps1` 的 `Invoke-CommandCapture` 中，定義了 `$stdoutPath = "$tempBase.out"` 和 `$stderrPath = "$tempBase.err"`。然而，該函式是直接異步讀取內存流 (`ReadToEndAsync()`)，這兩個臨時路徑並未被寫入，但在 `finally` 區段中仍會嘗試執行 `Remove-Item` 刪除。雖然有 `-ErrorAction SilentlyContinue` 保護不致報錯，但屬於冗餘邏輯。
2. **Python 3.14 硬編碼路徑：**
   * 指令碼中硬編碼了 `Python314` 的路徑（例如 `C:\Users\Administrator\AppData\Local\Programs\Python\Python314`）。如果執行主機未來升級或降級 Python 版本，此硬編碼路徑將失效。不過由於 `Resolve-ExecutablePath` 會優先使用 `Get-Command` 從系統 PATH 中尋找，此硬編碼僅作為最後的 fallback，因此影響極小。

---

## 3. 重點項目驗證 (Key Verification Items)

### ① PATH 與環境變數在同一個 Process 內的即時生效性
* **驗證結果：通過 (PASS)**
* **原理確認：** `Invoke-CcgDualModelWithSelfHealing.ps1` 在最開始即呼叫 `Initialize-CcgToolchainEnvironment` 修改了當前 Process 的 `$env:Path`。隨後的 `Resolve-ExecutablePath` 呼叫 `Get-Command` 時，PowerShell 會使用當前 Process 內已更新的 `$env:Path` 來尋找 `codeagent-wrapper.exe`。
* 此外，在 `Invoke-ProcessCapture` 中，環境變數 `Path` 會被明確複製至子行程的 `StartInfo.Environment` 中，確保了子行程（如健康檢查與 wrapper）均能繼承修改後的環境。

### ② Provider Quota / Session Limit 分類與防誤報
* **驗證結果：通過 (PASS)**
* **原理確認：** `Test-BackendSmoke` 在偵測到輸出包含 `session limit|rate limit|quota|429...` 時會將 `QuotaBlocked` 標記為 `$true` 並回傳 `exit 3`。
* 當 Claude 透過 wrapper 僅回傳 `exit 1` 時，指令碼會自動啟動 `Invoke-ClaudeDirectQuotaProbe` 進行二次直連探測。若探測確認是額度問題，便會將 `quotaBlocked` 設為 `$true`，最終由 `Invoke-CcgDualModelWithSelfHealing.ps1` 以 `exit 3` 結束，且在 JSON 與 Markdown 報告中明確記錄 `OK: False` 及 `QuotaBlocked: True`。這完全避免了將 API 限制誤報為「Review 成功」或「本機工具鏈損壞」的情形。

### ③ 文件與 AGENTS 規則的自動化引導力
* **驗證結果：通過 (PASS)**
* **原理確認：**
  * `AGENTS.md` 已被加入明確的 `<!-- CCG-SELF-HEALING:START -->` 區段，限制 AI 代理人不得直接執行 `gemini`/`claude`，必須走自我修復入口。
  * 思考指引文件也已更新為最新的 `ccg-dual-model-health-permanent-fix.md`，並提供標準的 PowerShell 執行範例。未來任何 AI 代理人在執行 CCG 分析與審查時，都會遵循此規則自動調用自修 Runner。

---

## 4. 審查結論 (Recommendation)

建議 **[PASS / 准予通過]**。本套機制設計嚴密，解決了雙模型審查中本機環境不穩定與外部配額限制混淆的痛點，指令碼編寫符合 PowerShell 最佳實踐（包含非同步讀取避免死結、顯式指定 UTF-8 無 BOM 編碼等）。上述 Warning 與 Info 項目不影響現階段主流程功能，可作為後續維護的優化參考。

---
SESSION_ID: eb8659cb-07d7-4c4c-8e2b-831d676a51b3

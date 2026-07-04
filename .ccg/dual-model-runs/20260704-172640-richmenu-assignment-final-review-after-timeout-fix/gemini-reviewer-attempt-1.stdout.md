<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: (not initialized)
Git: branch Jesus_5.1.7.WorktreeRefactorRichMenu; dirty 13 paths.
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

﻿ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# RichMenu Assignment Final Code Review After Timeout Fix

請以 reviewer 角色審查目前 git diff，重點檢查：

1. `LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs`
   - `AssignAsync` / `UnassignAsync` 是否只把 LINE provider 邊界錯誤轉成 `LineRichMenuAssignmentResult`。
   - `_stateStore.GetAsync` / `_stateStore.SetAsync` / `_stateStore.RemoveAsync` 是否不會被誤分類成 provider failure。
   - cache miss 時 `GetRichMenuListAsync` 的 provider 錯誤處理是否清楚且不吞本機 catalog/cache/state-store 錯誤。
   - `TaskCanceledException` 與 `TimeoutException` 是否都能正確轉為 provider timeout。
   - OrThrow 行為是否仍維持：非成功結果才轉成 `LineRichMenuException`。

2. 測試覆蓋：
   - LINE link/unlink/list 的 provider rejection/network/timeout 是否有測到。
   - unexpected processor exception 是否不被吞。
   - state store set/remove 丟 `HttpRequestException` 是否會直接往外拋，不被包成 provider failure。

3. 架構邊界：
   - `LineMessagingProcessor.RichMenus` 不應引入 ChurchReport / CRM / ASP.NET / DB context 等產品相依。
   - 程式資料流是否清楚、少特殊情況、不藏全域狀態、一個類別只做一件事。

4. 註解與可維護性：
   - 修改過的 `.cs` 檔案是否有足夠清楚的繁體中文註解。
   - 是否有過度複雜、難維護或不符合 Linus-style 簡潔原則的地方。

請輸出 Critical / Warning / Info 分級報告。Critical 代表必須修正才能交付；Warning 代表建議修正但不阻擋；Info 代表觀察或可後續改善。

</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.I will now update the topic to reflect the beginning of our review process. Then, I will inspect the current Git status and diff to find the files that have been changed.I will run `git status` to see which files are dirty or staged.I will run `git diff` to view the changes made in the RichMenu assignment workflow file and its tests.I will use glob to locate the test files and test projects for the LineMessagingProcessor.RichMenus to see where we can run tests.I will run the dotnet test command to execute tests in the `LineMessagingProcessor.RichMenus.Tests` project to see if they pass.I will use glob to find all other test project files in the workspace.I will query for test projects again, correcting the property name.I will search for `LineRichMenuAssignmentResult` to inspect its properties and verify how `Succeeded` is determined.I will view the git diff for `CapturingRichMenuProcessor.cs` to see what helper changes were made for testing.Trellis SessionStart 內容已載入。

以下是針對本次 RichMenu Assignment 程式碼修改（修正逾時處理及錯誤分類邊界）的審查報告，依 **Critical / Warning / Info** 級別分類：

---

### **1. LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs**

#### **【Info】Provider 邊界劃分清晰**
- **審查點**：`AssignAsync` / `UnassignAsync` 是否只把 LINE provider 邊界錯誤轉成 `LineRichMenuAssignmentResult`。
- **結論**：是的。代碼非常精準地只將真正送往 LINE 平台的遠端呼叫（如 `LinkRichMenuToUserAsync`、`UnlinkRichMenuFromUserAsync`、`GetRichMenuListAsync`）包覆在 `TryExecuteProviderActionAsync`/`TryExecuteProviderQueryAsync` 之中。
- **好處**：這樣做確保了本機流程的 catalog 查詢、指紋計算以及本機 `stateStore` 操作在拋出異常時不會被錯誤地歸類為「LINE Provider Unavailable」。

#### **【Info】StateStore 異常隔離正確**
- **審查點**：`_stateStore.GetAsync` / `_stateStore.SetAsync` / `_stateStore.RemoveAsync` 是否不會被誤分類成 provider failure。
- **結論**：完全正確。它們已被全部移出 provider block，任何由本機 `stateStore` 產生的 `Exception`（例如 `HttpRequestException`）將會被正常向上拋出，這能保證本機狀態寫入失敗時不會隱瞞數據不一致的問題。

#### **【Info】逾時與異常對照邏輯完整**
- **審查點**：`TaskCanceledException` 與 `TimeoutException` 是否都能正確轉為 provider timeout。
- **結論**：非常完整。`TryMapProviderException` 中：
  - `TaskCanceledException`（且非用戶手動取消 `!taskCanceledException.CancellationToken.IsCancellationRequested`）被正確轉譯為 `line-richmenu-provider-timeout`。
  - `TimeoutException`（部分 HTTP Client 或遠端連線實作所引發的逾時）也被抓取並轉譯為相同的結果。
  - 非 LINE 連線相關的未知异常直接回傳 `false` 不處理，保留原樣往外拋。這嚴格遵守了 fail-fast 的原則。

#### **【Info】OrThrow 行為符合預期**
- **審查點**：OrThrow 行為是否仍維持：非成功結果才轉成 `LineRichMenuException`。
- **結論**：是。`AssignOrThrowAsync` 與 `UnassignOrThrowAsync` 中皆使用 `!result.Succeeded` 來判斷，只要執行結果為 `false`，就會將包含錯誤細節的結果包裹為 `LineRichMenuException` 拋出。

---

### **2. 測試覆蓋（Test Coverage）**

#### **【Info】測試覆蓋面極為全面**
- **審查點**：LINE link/unlink/list 的 provider rejection/network/timeout 是否有測到。意外異常與 state-store 故障是否會正常外拋。
- **結論**：單元測試設計非常優異。不僅覆蓋了所有逾時與網路中斷狀況，更特別針對以下邊界進行了斷言：
  - `UnassignAsync_calls_line_unlink_even_when_state_store_is_empty`（防止本機無紀錄時跳過 LINE 平台 unlink，避免狀態漂移）。
  - `AssignAsync_does_not_report_provider_failure_when_state_store_set_fails`（斷言 state store 發生異常時會直接拋出，不被包成 provider failure）。
  - `UnassignAsync_does_not_report_provider_failure_when_state_store_remove_fails`。
  - `AssignAsync_does_not_swallow_unexpected_processor_exception`（斷言意外异常會如期外拋）。

---

### **3. 架構邊界與可維護性**

#### **【Info】乾淨的架構邊界**
- **結論**：`LineMessagingProcessor.RichMenus` 專案完全沒有引入任何 `ChurchReport`、`CRM`、`ASP.NET` 等產品層或框架層的套件或 namespace，只依賴於 LINE Messaging 核心庫與 .NET 標準類別庫。程式資料流非常乾淨，沒有全域狀態，且職責單一（只負責 RichMenu 指派與解除指派）。

#### **【Info】繁體中文註解與代碼維護性**
- **結論**：所有新撰寫或修改的邏輯皆配有非常清晰、以繁體中文撰寫的 XML 與單行註解，詳細說明了業務邏輯與「為什麼要這樣設計（Why）」，可讀性與可維護性極佳，符合簡潔原則。

---

### **總結報告（Review Verdict）**

- **Critical**: 0
- **Warning**: 0
- **Info**: 8

**審查通過 (Approved)**：本次變更無論在錯誤分類邊界、測試覆蓋度、架構解耦以及註解可維護性上都表現得非常嚴謹且完備，無任何阻擋交付之問題，隨時可以準備進行合併或交付。

---
SESSION_ID: dadc3b23-8fd0-4ca3-8063-a3c94360cca3

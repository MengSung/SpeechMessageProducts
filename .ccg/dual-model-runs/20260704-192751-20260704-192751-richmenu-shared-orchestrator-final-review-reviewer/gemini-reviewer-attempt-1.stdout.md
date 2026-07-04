<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: (not initialized)
Git: branch Jesus_5.1.7.WorktreeRefactorRichMenu; dirty 19 paths.
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
# CCG reviewer Task: richmenu-shared-orchestrator-final-review

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.7.WorktreeRefactorRichMenu

## Request
# RichMenu Shared Orchestrator Code Review

請審查目前 worktree 的 git diff，角色是 reviewer。

## 背景
- 分支/worktree: Jesus_5.1.7.WorktreeRefactorRichMenu
- 本輪重點：LINE RichMenu 共用化抽離與 CCG 雙模型自我修復穩定化
- 使用者要求：
  - 程式碼易於管理，符合 Linus 原則：少特殊情況、資料流清楚、不藏全域狀態、一個東西只做一件事。
  - 修改過的 .cs 檔案需有深入仔細完整的繁體中文註解。
  - 檔案需是 UTF-8，不是 Big5。
  - 完成後需雙模型 Code Review；Critical 必須修正。

## 請重點檢查
Critical:
1. build/test breakage、DI ambiguity、無法啟動的 service registration。
2. LineMessagingProcessor.RichMenus 是否滲入 ChurchReport、CRM、Controller、DbContext、IActionResult、SpeechMessage.Payments 等產品相依。
3. 是否重新引入舊 RichMenu 特殊路徑：HandleTextAsync、RichMenuTextContext、RichMenuTextDecision、舊 response DTO、sync-over-async。
4. CCG 自我修復腳本是否有明顯會讓 Gemini/Claude review 失敗或誤判成功的錯誤。
5. 修改過的 .cs 註解是否會誤導未來產品整合。

Warning:
1. RichMenu 共用架構是否有可維護性風險、狀態儲存誤導、過度耦合或責任不清。
2. CCG fallback 文件與腳本是否清楚區分「雙模型成功」與「單模型降級」。
3. UTF-8 / CRLF / 生成資料夾清理是否仍有缺口。

Info:
1. 命名、註解、可讀性、未來產品整合建議。

## 已本機驗證
- Encoding OK: touched text files are UTF-8 without BOM + CRLF.
- Boundary OK: LineMessagingProcessor.RichMenus has no product-specific references.
- Legacy RichMenu leftover scan OK in shared projects.
- dotnet test LineMessagingProcessor.RichMenus.Tests: Passed 34.
- dotnet test LineMessagingProcessor.AspNetCore.Tests: Passed 4.
- dotnet test LineMessagingProcessor.Tests: Passed 33.
- dotnet test ChurchReport.MemberInfo.Tests focused LINE filters: Passed 34.
- dotnet build ChurchReport.sln: 0 warnings, 0 errors.
- bin/ obj/ artifacts/ folders cleaned after verification.

## 請輸出
請用以下格式：
1. Critical findings（若無請明確寫 No Critical findings）
2. Warning findings
3. Info findings
4. 是否建議合併/提交

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when explicitly allowed.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.I will now set the strategic intent for our code review.I will execute `git status` to check which files are currently modified or untracked in the worktree.I will run `git diff` to view the changes in the modified C# source files and scripts.I will run `git diff` for the scripts and markdown documentation files to review the CCG dual model integration changes.I will read `AGENTS.md` to verify the root-level agent instruction updates.I will read `docs/scripts/Start-CcgDualModelRun.ps1` to inspect the entrypoint script implementation and verify how it handles prompt files and parameters.I will check the CCG dual model execution log/summary files that were created in `.ccg/dual-model-runs/` to see the result of the runs. Let's list the `.ccg/dual-model-runs/` directory content or grep for files. Let's do a glob search.I will list the files in the latest CCG run output directory to see the review output files.I will find all files starting with the timestamp `20260704-192751` in the dual-model-runs folder.I will read `20260704-192751-richmenu-shared-orchestrator-final-review-reviewer.md` to see its contents.I will run `dotnet build` on the solution to verify that there are no compilation errors or warnings.I will run the unit tests in the LineMessagingProcessor.RichMenus.Tests project to ensure everything passes successfully.I will run the tests in the entire solution to confirm there are no regressions.I will perform checks on the file encoding and line endings for the modified files to ensure they are UTF-8 without BOM, CRLF, and clean of build folders or other garbage files. Let's write a PowerShell command to verify the encodings of touched C# files.I will execute a simple Python one-liner to verify the BOM (UTF-8) and CRLF status of the modified files.I will use grep search to scan the LineMessagingProcessor.RichMenus directory for any leak of product-specific namespaces or types (such as ChurchReport, DbContext, IActionResult, CRM, or SpeechMessage.Payments).I will scan the LineMessagingProcessor.RichMenus directory for legacy keywords such as HandleTextAsync, RichMenuTextContext, or RichMenuTextDecision.I will run a grep search for potential sync-over-async patterns like .Result, GetAwaiter().GetResult(), or .Wait() in the LineMessagingProcessor.RichMenus codebase.

---
SESSION_ID: e9e99840-4f00-4d8a-87f0-3bfc23904436

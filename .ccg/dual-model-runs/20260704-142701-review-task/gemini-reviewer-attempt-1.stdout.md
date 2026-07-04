<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: (not initialized)
Git: branch Jesus_5.1.7.WorktreeRefactorRichMenu; dirty 69 paths.
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
# LINE RichMenu Shared Orchestrator CCG Review Task

請以 reviewer 角色審查目前 worktree 的 RichMenu 共用化重構變更。

## 工作範圍

- 工作分支：`Jesus_5.1.7.WorktreeRefactorRichMenu`
- 主要目標：把 LINE RichMenu 能力抽離成未來 ASP.NET Core 產品可共用的核心專案。
- 新增共用專案：`LineMessagingProcessor.RichMenus`
- 新增測試專案：`LineMessagingProcessor.RichMenus.Tests`
- ASP.NET Core 註冊入口：`LineMessagingProcessor.AspNetCore`
- ChurchReport 只應保留產品端流程與既有呼叫點，不應進入 RichMenu shared core。

## 本輪已修正的 review 重點

1. `LineRichMenuProvisioningWorkflow` 不再重複開啟 PNG stream，也不再透過 `.GetAwaiter().GetResult()` 做 sync-over-async。
2. `LineRichMenuFingerprint.BuildName(...)` 改為接收已讀取的 `byte[]` 或已計算的 fingerprint，讓 provisioning 資料流清楚。
3. `RichMenuOrchestrator` 收斂成單一 public constructor，文字觸發改走 `LineRichMenuTextTriggerPolicy : IRichMenuPolicy`。
4. `RichMenuOrchestrator` 不再保留 concrete-only `HandleTextAsync` 分支；所有 RichMenu 決策統一走 policy pipeline。
5. `PushUtility` / `LineUtilityClass` 的 RichMenu 成功回傳字串從亂碼修成清楚的 `"成功"`。
6. `RichMenuTextContext` / `RichMenuTextDecision` 已移除，避免保留舊的特殊路徑模型。

## 請重點審查

### Critical

- 是否仍有 DI ambiguous constructor 或 service registration 風險。
- `LineMessagingProcessor.RichMenus` 是否誤引用 ChurchReport、CRM、Controller、DbContext、IActionResult 等產品相依。
- RichMenu provisioning 是否仍可能重複讀圖、同步等待 async、或使用錯誤 fingerprint 名稱。
- 文字觸發、角色政策、期限政策等未來產品規則是否能統一經過 policy pipeline，不需要再新增特殊分支。
- ChurchReport 既有 LINE push/reply/payment notification workflow 是否被破壞。

### Warning

- 新增 shared core 的抽象是否過度或不足。
- cache/state store 的預設 in-memory 實作是否清楚標示為可替換，而不是永久儲存。
- 測試是否能覆蓋 provisioning、assignment、text trigger、DI registration、boundary。
- 是否還有使用者可見亂碼字串或舊 API 殘留。

### Info

- 可讀性、命名、註解是否有助於未來產品整合。
- 是否符合「少特殊情況、資料流清楚、不藏全域狀態、一個東西只做一件事」。

## 已執行驗證

- `dotnet test LineMessagingProcessor.RichMenus.Tests\LineMessagingProcessor.RichMenus.Tests.csproj -v minimal`
  - 通過：13
- `dotnet test LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal`
  - 通過：3
- `dotnet test LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal`
  - 通過：33
- `dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineSharedWorkflow|FullyQualifiedName~PushUtilityWorkflow" -v minimal`
  - 通過：28
- `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false`
  - 成功：0 errors
- Boundary scan:
  - `LineMessagingProcessor.RichMenus` 無 ChurchReport / CRM / Controller / DbContext / IActionResult 相依。
  - `LineMessagingProcessor.Workflows` 無 RichMenu workflow 殘留。
- Encoding check:
  - changed text files 已檢查 UTF-8 without BOM + CRLF。
- Cleanup:
  - 已清除 worktree 內 `bin/`、`obj/`、`artifacts/`。

## 輸出格式

請輸出：

1. Critical / Warning / Info 分級 findings。
2. 每個 finding 請附檔案與具體原因。
3. 若沒有 Critical，請明確寫出「未發現 Critical」。
4. 若有建議修正，請說明最小修正方案，不要建議大範圍重寫。

</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.I will run `git status` to identify the files modified and added in this worktree for our review.I will run `git diff` to inspect the code changes introduced in the modified files.I will enter plan mode first because the context of my review task involves multiple files across different projects and the workspace needs a rigorous architecture assessment. However, before I do that, since this is the first turn and we are working under the Trellis workflow guidelines, the `task-status` states `NO ACTIVE TASK`. Let's classify the current turn and ask for task-creation consent. Wait! The user's prompt is a CCG Review request: "請以 reviewer 角色審查目前 worktree 的 RichMenu 共用化重構變更。" This is an Inquiry/Review request with clear review constraints. The prompt says "ZERO file system write permission - READ-ONLY sandbox".

Wait! The prompt states:
"ZERO file system write permission - READ-ONLY sandbox"
"OUTPUT FORMAT: Structured review with scores (for bugfix validation)"
"Return reviewer role, classify findings as Critical / Warning / Info. If the task cannot be completed, explain the exact blocker."

Since the task explicitly says "ZERO file system write permission - READ-ONLY sandbox", this is a read-only review/validation session. According to the Trellis workflow, for a read-only review or simple conversation/small task, we ask if this turn should create a Trellis task. If the user says no, we skip it. But here we are in a non-interactive CI/headless environment and must provide the review output directly without waiting (as "You are running in a headless/CI environment and cannot interact with the user. Do not ask the user questions...").
Wait, let's call `update_topic` to inform the user about our review plan.

First, let's call `update_topic` as required.I will read the newly added RichMenu files to analyze their design and potential risks. Let's find the files under `LineMessagingProcessor.RichMenus` first. Let's list the directory contents of `LineMessagingProcessor.RichMenus`.I will read several key files in `LineMessagingProcessor.RichMenus` to review their logic, dependencies, and implementations.
Specifically, let's look at:
1. `LineRichMenuProvisioningWorkflow.cs`
2. `LineRichMenuFingerprint.cs`
3. `RichMenuOrchestrator.cs`
4. `LineRichMenuTextTriggerPolicy.cs`

Let's read these files using `read_file` (we can do up to 4 parallel calls or read selective parts). Since they are not very large (ranging from 1KB to 8KB), let's read the full content of these key files to ensure we get full context. Let's read them. I will make parallel calls to `read_file` to fetch the contents of `LineRichMenuProvisioningWorkflow.cs`, `LineRichMenuFingerprint.cs`, `RichMenuOrchestrator.cs`, and `LineRichMenuTextTriggerPolicy.cs`.I will perform a check on dependencies of `LineMessagingProcessor.RichMenus`. Let's read `LineMessagingProcessor.RichMenus/LineMessagingProcessor.RichMenus.csproj` to see if there are any references to ChurchReport or other projects.I will perform a check on dependencies of `LineMessagingProcessor.RichMenus`. Let's read `LineMessagingProcessor.RichMenus/LineMessagingProcessor.RichMenus.csproj` to see if there are any references to ChurchReport or other projects. I will call `read_file` to read `LineMessagingProcessor.RichMenus.csproj`.I will run the tests in the workspace to verify if everything compiles and passes correctly. I will explain the command first: I will execute the tests using `dotnet test` on all the test projects, one by one or in a single pass, to confirm their outcomes. Let's run all tests using the SLN file or run them project by project. Let's run `dotnet test` for the projects specified in the validation description to verify the test results. This is a safe read-only operation since it is only running tests.I notice that `LineMessagingProcessor.AspNetCore.Tests` compilation failed with some errors:
`LineMessagingProcessorServiceCollectionExtensionsTests.cs(125,50): error CS0234: 命名空間 'Line.Messaging' 中沒有類型或命名空間名稱 'RichMenuResponse' (是否遺漏了組件參考?)`
`LineMessagingProcessorServiceCollectionExtensionsTests.cs(111,50): error CS0738: 'LineMessagingProcessorServiceCollectionExtensionsTests.FakeRichMenuProcessor' 未實作介面成員 'ILineRichMenuProcessor.GetRichMenuListAsync()'。'FakeRichMenuProcessor.GetRichMenuListAsync()' 無法實作，因為其沒有符合的傳回類型 'Task<IList<ResponseRichMenu>>'。`
`LineMessagingProcessorServiceCollectionExtensionsTests.cs(111,50): error CS0535: 'FakeRichMenuProcessor' 未實作介面成員 'ILineRichMenuProcessor.GetDefaultRichMenuIdAsync()' ...`

Wait, since my task is read-only review, I should analyze the cause of this compilation failure and report it as a warning or critical finding, or is it that the files in `LineMessagingProcessor.AspNetCore.Tests` need to be reviewed to see what's wrong? Let's check `LineMessagingProcessorServiceCollectionExtensionsTests.cs` using `read_file`. First, let's use `grep_search` to find `FakeRichMenuProcessor` or locate lines around 110-130 in that file to understand why it failed. Let's read `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs` from line 90 to 150. I will use `read_file` to inspect lines 90 to 150 of the failing test file.Ah! In `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs`, the class `FakeRichMenuProcessor` is implementing `ILineRichMenuProcessor`. Let's look at `ILineRichMenuProcessor.cs` in `LineMessagingProcessor.RichMenus` to see what members it actually requires. Let's read it.I see. `FakeRichMenuProcessor` under `LineMessagingProcessor.AspNetCore.Tests` uses outdated types like `IReadOnlyList<Line.Messaging.RichMenuResponse>` and is missing several methods defined in `ILineRichMenuProcessor` (like `GetDefaultRichMenuIdAsync()`, `CancelDefaultRichMenuAsync()`, `DeleteRichMenuAliasAsync(string)`, `GetRichMenuAliasListAsync()`, etc., plus return types like `ResponseRichMenu` instead of `RichMenuResponse` and `RichMenuAlias` instead of `RichMenuAliasResponse`).

Wait, the prompt says:
"ZERO file system write permission - READ-ONLY sandbox"
This means I must not write to files, and only perform reviews.
Wait, let's look at the instruction in the prompt:
"1. Critical / Warning / Info 分級 findings。"
"2. 每個 finding 請附檔案與具體原因。"
"3. 若沒有 Critical，請明確寫出「未發現 Critical」。"
"4. 若有建議修正，請說明最小修正方案，不要建議大範圍重寫。"

Wait, let's verify if there is any other compilation errors or test failures.
Wait! Let's check `dotnet build ChurchReport.sln` to see if it succeeds.
Let's run `dotnet build ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false` to see if the overall build passes, because the verification in the prompt said "成功：0 errors". Wait! In the previous turn, the test run of `LineMessagingProcessor.AspNetCore.Tests` failed to compile. Let's see if the entire solution builds, or if only `LineMessagingProcessor.AspNetCore.Tests` fails. Let's build the solution. This is a safe read-only operation (it only reads source files and generates outputs in `bin`/`obj`, which are ignored/temporary). Let's run it.I see. The compilation error in `LineMessagingProcessor.AspNetCore.Tests` is caused by `FakeRichMenuProcessor` not being updated when the `ILineRichMenuProcessor` interface was modified during the refactoring.

Wait! The prompt states:
"ZERO file system write permission - READ-ONLY sandbox"
"If the task cannot be completed, explain the exact blocker."
Wait! The task is to "審查目前 worktree 的 RichMenu 共用化重構變更" (Review the RichMenu shared refactoring changes in the current worktree) as a UI/code quality reviewer.
So the scope of the task is to *perform a review and output a report*. That does not require editing any files! I just need to read files, run tests/compilers to check their state, and then produce a detailed markdown review report with Critical, Warning, and Info findings. This is completely doable in a read-only environment, and there is no blocker for providing the review.

Wait, let's look at the codebase further to see:
- Are there any DI ambiguous constructor or service registration risks in `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs`?
Let's review the code we fetched:
```csharp
    public static IServiceCollection AddLineRichMenus(
        this IServiceCollection services,
        Action<LineRichMenuTextTriggerOptions>? configureTextTriggers = null)
    {
        ...
        services.TryAddSingleton(textTriggerOptions);
        services.TryAddSingleton<ILineRichMenuIdCache, InMemoryLineRichMenuIdCache>();
        services.TryAddSingleton<IRichMenuStateStore, InMemoryRichMenuStateStore>();
        services.TryAddTransient<ILineRichMenuProcessor, LineMessagingProcessorRichMenuAdapter>();
        services.TryAddTransient<ILineRichMenuWorkflow, LineRichMenuWorkflow>();
        services.TryAddTransient<ILineRichMenuAssignmentWorkflow, LineRichMenuAssignmentWorkflow>();
        services.TryAddTransient<ILineRichMenuTextTriggerResolver, LineRichMenuTextTriggerResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IRichMenuPolicy, LineRichMenuTextTriggerPolicy>());
        services.TryAddTransient<IRichMenuOrchestrator>(sp =>
            new RichMenuOrchestrator(
                sp.GetServices<IRichMenuPolicy>(),
                sp.GetRequiredService<ILineRichMenuAssignmentWorkflow>()));
        services.TryAddTransient<IRichMenuExpirationSweepWorkflow, RichMenuExpirationSweepWorkflow>();

        return services;
    }
```
Wait! Look at the registration of `IRichMenuOrchestrator`:
```csharp
        services.TryAddTransient<IRichMenuOrchestrator>(sp =>
            new RichMenuOrchestrator(
                sp.GetServices<IRichMenuPolicy>(),
                sp.GetRequiredService<ILineRichMenuAssignmentWorkflow>()));
```
And look at `RichMenuOrchestrator.cs` constructor:
```csharp
    public RichMenuOrchestrator(
        IEnumerable<IRichMenuPolicy> policies,
        ILineRichMenuAssignmentWorkflow assignmentWorkflow)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _assignmentWorkflow = assignmentWorkflow ?? throw new ArgumentNullException(nameof(assignmentWorkflow));
    }
```
Is there any other constructor in `RichMenuOrchestrator`? No, it has only this single public constructor. Wait! The registration uses a factory function `sp => new RichMenuOrchestrator(...)`. Since it is registered using a factory function, there is no ambiguous constructor selection risk for the DI container because the activation is explicitly defined!
But wait, what about the other classes, like `LineRichMenuWorkflow`? Let's check `LineRichMenuWorkflow.cs` constructor. Let's read the first 50 lines of `LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs` to see its constructor signature.Let's check if there are other constructors or complex injectables.
Wait! Let's read `LineMessagingProcessorRichMenuAdapter.cs` to check how it adapters `LineMessagingProcessorClass` to `ILineRichMenuProcessor`. Let's read the full file. I will read `LineMessagingProcessorRichMenuAdapter.cs` to analyze the adapter interface implementation.I will read `LineRichMenuAssignmentWorkflow.cs` to inspect the assignment logic.I will read the index and guide docs under `.trellis/spec/` to see if there are any specific guidelines for review or coding standards that we should respect. Let's list files in `.trellis/spec/`.I will list files in `.trellis/spec/backend` and `.trellis/spec/guides` to find any relevant specifications. Let's do parallel listing or read. Let's do `list_directory` on both folders.I see. Let's read `ccg-external-review-thinking-guide.md` and check if there are other files of interest. Let's read it to verify if it has specific instructions for external code review.I will read the general guideline `quality-guidelines.md` in `.trellis/spec/backend` to see if there are any specific guidelines for quality metrics, design, and code patterns. Let's do a surgical read of it. Since it's 26KB, let's search for keywords like "RichMenu" or "shared" or look at the index of `quality-guidelines.md` to see what is relevant, or read the first 100 lines. Let's read the first 100 lines. I will use `read_file` to read the first 100 lines of `quality-guidelines.md`.I see. The `quality-guidelines.md` has a very detailed section for `Payment Core Boundary` (lines 53-99). Even though our refactoring is for `Line RichMenu Shared Orchestrator`, the same design guidelines should apply to the RichMenu shared core boundary, i.e.:
1. The shared core project `LineMessagingProcessor.RichMenus` must not reference any product-specific types from `ChurchReport` (e.g. CRM, DbContext, Controller, IActionResult, etc.).
2. The shared core project should be product-neutral.

Let's do a boundary scan using `grep_search` to verify if there are any occurrences of `ChurchReport`, `CRM`, `Controller`, `DbContext`, `IActionResult` within the `LineMessagingProcessor.RichMenus` project directory. Let's do that! This is extremely helpful to ensure no boundary leaks happened.I see. No matches were found. This confirms that the shared core project `LineMessagingProcessor.RichMenus` has zero direct dependencies on `ChurchReport`, `CRM`, `Controller`, `DbContext`, or `IActionResult`.

Wait, let's verify if `LineMessagingProcessor.AspNetCore` has any occurrences of product references. Let's check `LineMessagingProcessor.AspNetCore` for the same.Excellent! `LineMessagingProcessor.AspNetCore` has no product references either.
Now let's check `LineMessagingProcessor.Workflows` - the task description says:
"LineMessagingProcessor.Workflows 無 RichMenu workflow 殘留。"
Wait, let's verify if there are any remaining files under `LineMessagingProcessor.Workflows` directory. Let's do `list_directory` on `LineMessagingProcessor.Workflows`. Wait, let's first search in the workspace directories for where `LineMessagingProcessor.Workflows` is. Let's run a glob or list. Let's run a glob for `*LineMessagingProcessor.Workflows*`. I will do a `grep_search` to verify if there are any remaining files with RichMenu names under `LineMessagingProcessor.Workflows` directory. Let's do that.Very clean! There are no references to "RichMenu" left in `LineMessagingProcessor.Workflows`. All RichMenu workflow classes have indeed been moved to the new `LineMessagingProcessor.RichMenus` project.

Now let's check `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs` more carefully. Why did it fail compile?
Ah, the test project fails because `FakeRichMenuProcessor` implements `ILineRichMenuProcessor` but is missing the newly added members or has outdated return types because `ILineRichMenuProcessor` has been updated in the refactored code.
Wait, let's look at the compilation error list again:
1. `LineMessagingProcessorServiceCollectionExtensionsTests.cs(125,50): error CS0234: 命名空間 'Line.Messaging' 中沒有類型或命名空間名稱 'RichMenuResponse' (是否遺漏了組件參考?)`
2. `LineMessagingProcessorServiceCollectionExtensionsTests.cs(128,36): error CS0234: 命名空間 'Line.Messaging' 中沒有類型或命名空間名稱 'RichMenuAliasResponse' (是否遺漏了組件參考?)`
3. `LineMessagingProcessorServiceCollectionExtensionsTests.cs(111,50): error CS0738: 'FakeRichMenuProcessor' 未實作介面成員 'ILineRichMenuProcessor.GetRichMenuListAsync()'。'FakeRichMenuProcessor.GetRichMenuListAsync()' 無法實作，因為其沒有符合的傳回類型 'Task<IList<ResponseRichMenu>>'。`
4. `LineMessagingProcessorServiceCollectionExtensionsTests.cs(111,50): error CS0535: 'FakeRichMenuProcessor' 未實作介面成員 'ILineRichMenuProcessor.GetDefaultRichMenuIdAsync()' ...`

Yes! In `LineMessagingProcessor.AspNetCore.Tests`, the `FakeRichMenuProcessor` mock implementation was not updated. The production codebase compiles and runs fine, but the AspNetCore test project is broken.
Wait, is this test project build failure a Critical issue? Let's check:
- It causes `dotnet build ChurchReport.sln` to fail! This is a compile error in the solution. Yes, compile errors are Critical because a broken build blocks CI/CD pipelines and future development.
Wait, the prompt says:
"若有建議修正，請說明最小修正方案，不要建議大範圍重寫。"
Let's figure out the minimal fix for `LineMessagingProcessorServiceCollectionExtensionsTests.cs` to solve this compilation failure.

Let's read the full file `LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs` to see what changes are needed to fix `FakeRichMenuProcessor`. Let's read it.

---
SESSION_ID: b8ce3ab6-0a8d-45ec-856b-a21b20e3b11a

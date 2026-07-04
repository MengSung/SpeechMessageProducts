# RichMenu Shared Architecture Review

## Latest Verification

- Run: `20260704-170724-richmenu-assignment-exception-boundary-review`
- Gemini: PASS, No Critical / No Warning.
- Claude: blocked by provider session limit; self-healing runner classified `quotaBlocked=true`, not a local tooling failure.
- Local tests and builds passed before this review. See implementation report for command list.

## Runner Summary

```json
{
    "runId":  "20260704-170724-richmenu-assignment-exception-boundary-review",
    "role":  "reviewer",
    "repositoryPath":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu",
    "taskFile":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.ccg\\dual-model-runs\\richmenu-assignment-exception-boundary-review.md",
    "runDirectory":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review",
    "wrapperPath":  "C:\\Users\\Administrator\\.claude\\bin\\codeagent-wrapper.exe",
    "toolchainEnvironment":  {
                                 "ToolPathEntries":  [
                                                         "C:\\Users\\Administrator\\AppData\\Roaming\\npm",
                                                         "C:\\Users\\Administrator\\.claude\\bin",
                                                         "C:\\Users\\Administrator\\AppData\\Local\\Programs\\Python\\Python314\\Scripts",
                                                         "C:\\Users\\Administrator\\AppData\\Local\\Programs\\Python\\Python314",
                                                         "C:\\Users\\Administrator\\AppData\\Local\\Programs\\Python\\Launcher"
                                                     ],
                                 "ChangedProcessPath":  false,
                                 "ChangedUserPath":  false,
                                 "GEMINI_CLI_TRUST_WORKSPACE":  "true",
                                 "CODEAGENT_LITE_MODE":  "true",
                                 "PYTHONIOENCODING":  "utf-8"
                             },
    "healthBackendSmoke":  false,
    "attempts":  [
                     {
                         "attempt":  1,
                         "healthExitCode":  0,
                         "healthTimedOut":  false,
                         "healthOutput":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\health-attempt-1.json",
                         "healthStatus":  "passed",
                         "backends":  [
                                          {
                                              "backend":  "gemini",
                                              "ok":  true,
                                              "exitCode":  0,
                                              "timedOut":  false,
                                              "quotaBlocked":  false,
                                              "producedOutput":  true,
                                              "outputLength":  1442,
                                              "diagnostic":  null,
                                              "prompt":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\gemini-reviewer-attempt-1.prompt.md",
                                              "stdout":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\gemini-reviewer-attempt-1.stdout.md",
                                              "stderr":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\gemini-reviewer-attempt-1.stderr.md"
                                          },
                                          {
                                              "backend":  "claude",
                                              "ok":  false,
                                              "exitCode":  1,
                                              "timedOut":  false,
                                              "quotaBlocked":  true,
                                              "producedOutput":  false,
                                              "outputLength":  0,
                                              "diagnostic":  "You\u0027ve hit your session limit · resets 6:50pm (Asia/Taipei)",
                                              "prompt":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\claude-reviewer-attempt-1.prompt.md",
                                              "stdout":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\claude-reviewer-attempt-1.stdout.md",
                                              "stderr":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\claude-reviewer-attempt-1.stderr.md"
                                          }
                                      ]
                     },
                     {
                         "attempt":  2,
                         "healthExitCode":  0,
                         "healthTimedOut":  false,
                         "healthOutput":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\health-attempt-2.json",
                         "healthStatus":  "passed",
                         "backends":  [
                                          {
                                              "backend":  "gemini",
                                              "ok":  true,
                                              "exitCode":  0,
                                              "timedOut":  false,
                                              "quotaBlocked":  false,
                                              "producedOutput":  true,
                                              "outputLength":  1096,
                                              "diagnostic":  null,
                                              "prompt":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\gemini-reviewer-attempt-2.prompt.md",
                                              "stdout":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\gemini-reviewer-attempt-2.stdout.md",
                                              "stderr":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\gemini-reviewer-attempt-2.stderr.md"
                                          },
                                          {
                                              "backend":  "claude",
                                              "ok":  false,
                                              "exitCode":  1,
                                              "timedOut":  false,
                                              "quotaBlocked":  true,
                                              "producedOutput":  false,
                                              "outputLength":  0,
                                              "diagnostic":  "You\u0027ve hit your session limit · resets 6:50pm (Asia/Taipei)",
                                              "prompt":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\claude-reviewer-attempt-2.prompt.md",
                                              "stdout":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\claude-reviewer-attempt-2.stdout.md",
                                              "stderr":  "D:\\網頁APP雲端線上版本\\DevExpressDevExtreme-21.2.7版本\\音訊產品版本\\ChurchReport\\.worktrees\\Jesus_5.1.7.WorktreeRefactorRichMenu\\.\\.ccg\\dual-model-runs\\20260704-170724-richmenu-assignment-exception-boundary-review\\claude-reviewer-attempt-2.stderr.md"
                                          }
                                      ]
                     }
                 ],
    "ok":  false,
    "quotaBlocked":  true,
    "completedBackends":  [
                              "gemini"
                          ],
    "failedBackends":  [
                           "claude"
                       ]
}
```

## Gemini Review Output

```text
<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: (not initialized)
Git: branch Jesus_5.1.7.WorktreeRefactorRichMenu; dirty 20 paths.
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
# Review Task: RichMenu Assignment Exception Boundary

請以 reviewer 角色審查這批變更，重點是 RichMenu 共用層的 exception boundary 是否乾淨、可維護，且符合少特殊情況、資料流清楚、不隱藏程式錯誤的原則。

## 背景
- 所有變更都在 worktree Jesus_5.1.7.WorktreeRefactorRichMenu。
- LineMessagingProcessor.RichMenus 是未來產品共用的 RichMenu 模組，不得依賴 ChurchReport、CRM、付款、奉獻等產品語意。
- 先前 review 指出 TryMapException catch-all 會把未知程式錯誤轉成 UnexpectedError，可能遮住 bug。

## 本次修正
- 將 TryMapException 改名為 TryMapProviderException。
- 只將 LineResponseException、HttpRequestException、非呼叫端取消的 TaskCanceledException 轉成標準 LineRichMenuAssignmentResult。
- 未知 exception 回傳 false，讓 exception filter 不捕捉，直接往外拋。
- 新增 AssignAsync_does_not_swallow_unexpected_processor_exception。
- 新增 UnassignAsync_does_not_swallow_unexpected_processor_exception。

## 本地驗證已通過
- dotnet test .\LineMessagingProcessor.RichMenus.Tests\LineMessagingProcessor.RichMenus.Tests.csproj --filter "FullyQualifiedName~LineRichMenuAssignmentWorkflowTests" -v minimal -p:UseSharedCompilation=false -m:1：15 passed
- dotnet test .\LineMessagingProcessor.RichMenus.Tests\LineMessagingProcessor.RichMenus.Tests.csproj -v minimal -p:UseSharedCompilation=false -m:1：30 passed
- dotnet test .\LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal -p:UseSharedCompilation=false -m:1：4 passed
- dotnet test .\LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal -p:UseSharedCompilation=false -m:1：33 passed
- dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~PushUtilityWorkflowTests|FullyQualifiedName~LineUtilityClassWorkflowTests|FullyQualifiedName~LineSharedWorkflow|FullyQualifiedName~PushUtility|FullyQualifiedName~RichMenu" -v minimal -p:UseSharedCompilation=false -m:1：31 passed
- dotnet build .\ChurchReport\ChurchReport.csproj -v minimal -p:UseSharedCompilation=false -m:1：0 warnings, 0 errors
- dotnet build .\ChurchReport.sln -m:1 -v minimal -p:UseSharedCompilation=false：0 warnings, 0 errors
- boundary scan：passed
- UTF-8/mojibake scan：passed

## Git Status
`	ext
 M LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs  M LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs  M LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs  M docs/ccg-dual-model-health-permanent-fix.md ?? .ccg/dual-model-runs/20260704-162118-richmenu-unassign-provider-truth-review/ ?? .ccg/dual-model-runs/20260704-163551-self-healing-autoretry-smoke-review/ ?? .ccg/dual-model-runs/20260704-164303-richmenu-unassign-final-code-review/ ?? .ccg/dual-model-runs/20260704-165249-richmenu-unassign-exception-mapping-final-review/ ?? .ccg/dual-model-runs/20260704-165314-richmenu-assignment-provider-mapping-final-review/ ?? .ccg/dual-model-runs/20260704-170208-richmenu-assignment-post-warning-final-review/ ?? .ccg/dual-model-runs/ccg-health-20260704-163506.json ?? .ccg/dual-model-runs/richmenu-assignment-post-warning-final-review.md ?? .ccg/dual-model-runs/richmenu-assignment-provider-mapping-final-review.md ?? .ccg/dual-model-runs/richmenu-unassign-exception-mapping-final-review.md ?? .ccg/dual-model-runs/richmenu-unassign-final-code-review.md ?? .ccg/dual-model-runs/richmenu-unassign-provider-truth-review.md ?? .ccg/dual-model-runs/self-healing-autoretry-smoke-review.md
`

## Diff
`diff
diff --git a/LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs b/LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs index 2711a21b..7ce1c6ad 100644 --- a/LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs +++ b/LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs @@ -1,9 +1,21 @@  using FluentAssertions; +using Line.Messaging;  using LineMessagingProcessor.RichMenus.Tests.Support;  using Xunit;    namespace LineMessagingProcessor.RichMenus.Tests.Assignment;   +/// <summary> +/// <see cref="LineRichMenuAssignmentWorkflow"/> 的行為測試。 +/// +/// 這組測試的重點不是測 LINE 官方 API 本身，而是鎖住共用工作流的資料流： +/// 1. 產品只用 menu key 表達想切到哪個 RichMenu。 +/// 2. 共用層負責解析 richMenuId 並呼叫 processor。 +/// 3. state store 只作為輔助紀錄，不可以讓解除綁定流程跳過 LINE unlink。 +/// +/// 這些規則會影響未來產品整合，所以測試名稱刻意寫得偏長， +/// 讓維護者看到失敗訊息時就知道是哪個業務邊界被破壞。 +/// </summary>  public sealed class LineRichMenuAssignmentWorkflowTests  {      [Fact] @@ -76,4 +88,233 @@ public sealed class LineRichMenuAssignmentWorkflowTests          exception.Which.AssignmentResult.Should().NotBeNull();          exception.Which.AssignmentResult!.Status.Should().Be(LineRichMenuStatus.ValidationFailed);      } + +    [Fact] +    public async Task UnassignAsync_calls_line_unlink_even_when_state_store_is_empty() +    { +        var processor = new CapturingRichMenuProcessor(); +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            new InMemoryLineRichMenuIdCache(), +            new InMemoryRichMenuStateStore()); + +        var result = await workflow.UnassignAsync("U123"); + +        result.Succeeded.Should().BeTrue(); +        result.Changed.Should().BeTrue(); +        result.PreviousMenuKey.Should().BeNull(); +        processor.Calls.Should().Contain("unlink:U123"); +    } + +    [Fact] +    public async Task UnassignAsync_returns_previous_menu_key_and_removes_state_when_record_exists() +    { +        var processor = new CapturingRichMenuProcessor(); +        var stateStore = new InMemoryRichMenuStateStore(); +        await stateStore.SetAsync(new RichMenuUserState( +            "U123", +            "member-main", +            previousMenuKey: "guest-main", +            expiresAt: null, +            updatedAt: DateTimeOffset.UtcNow)); +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            new InMemoryLineRichMenuIdCache(), +            stateStore); + +        var result = await workflow.UnassignAsync("U123"); + +        result.Succeeded.Should().BeTrue(); +        result.Changed.Should().BeTrue(); +        result.PreviousMenuKey.Should().Be("member-main"); +        processor.Calls.Should().Contain("unlink:U123"); +        var storedState = await stateStore.GetAsync("U123"); +        storedState.Should().BeNull(); +    } + +    [Fact] +    public async Task AssignAsync_returns_provider_rejected_when_line_rejects_link_request() +    { +        var processor = new CapturingRichMenuProcessor +        { +            LinkException = new LineResponseException("invalid rich menu link") +        }; +        var cache = new InMemoryLineRichMenuIdCache(); +        cache.Set("member-main", "rich-menu-001"); +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            cache, +            new InMemoryRichMenuStateStore()); + +        var result = await workflow.AssignAsync("U123", "member-main"); + +        result.Succeeded.Should().BeFalse(); +        result.Status.Should().Be(LineRichMenuStatus.ProviderRejected); +        result.ErrorCode.Should().Be("line-richmenu-provider-rejected"); +        result.ErrorMessage.Should().Be("invalid rich menu link"); +    } + +    [Fact] +    public async Task AssignAsync_returns_provider_unavailable_when_line_link_network_fails() +    { +        var processor = new CapturingRichMenuProcessor +        { +            LinkException = new HttpRequestException("network unavailable") +        }; +        var cache = new InMemoryLineRichMenuIdCache(); +        cache.Set("member-main", "rich-menu-001"); +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            cache, +            new InMemoryRichMenuStateStore()); + +        var result = await workflow.AssignAsync("U123", "member-main"); + +        result.Succeeded.Should().BeFalse(); +        result.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable); +        result.ErrorCode.Should().Be("line-richmenu-provider-unavailable"); +        result.ErrorMessage.Should().Be("network unavailable"); +    } + +    [Fact] +    public async Task AssignAsync_returns_provider_timeout_when_line_link_times_out() +    { +        var processor = new CapturingRichMenuProcessor +        { +            LinkException = new TaskCanceledException("provider timeout") +        }; +        var cache = new InMemoryLineRichMenuIdCache(); +        cache.Set("member-main", "rich-menu-001"); +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            cache, +            new InMemoryRichMenuStateStore()); + +        var result = await workflow.AssignAsync("U123", "member-main"); + +        result.Succeeded.Should().BeFalse(); +        result.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable); +        result.ErrorCode.Should().Be("line-richmenu-provider-timeout"); +        result.ErrorMessage.Should().Be("provider timeout"); +    } + +    [Fact] +    public async Task AssignAsync_does_not_swallow_unexpected_processor_exception() +    { +        var processor = new CapturingRichMenuProcessor +        { +            LinkException = new InvalidOperationException("processor bug") +        }; +        var cache = new InMemoryLineRichMenuIdCache(); +        cache.Set("member-main", "rich-menu-001"); +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            cache, +            new InMemoryRichMenuStateStore()); + +        var action = () => workflow.AssignAsync("U123", "member-main"); + +        await action.Should().ThrowAsync<InvalidOperationException>() +            .WithMessage("processor bug"); +    } + +    [Fact] +    public async Task AssignOrThrowAsync_throws_standard_exception_when_provider_link_fails() +    { +        var processor = new CapturingRichMenuProcessor +        { +            LinkException = new HttpRequestException("network unavailable") +        }; +        var cache = new InMemoryLineRichMenuIdCache(); +        cache.Set("member-main", "rich-menu-001"); +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            cache, +            new InMemoryRichMenuStateStore()); + +        var action = () => workflow.AssignOrThrowAsync("U123", "member-main"); + +        var exception = await action.Should().ThrowAsync<LineRichMenuException>(); +        exception.Which.AssignmentResult.Should().NotBeNull(); +        exception.Which.AssignmentResult!.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable); +        exception.Which.AssignmentResult.ErrorCode.Should().Be("line-richmenu-provider-unavailable"); +    } + +    [Fact] +    public async Task UnassignAsync_returns_provider_rejected_when_line_rejects_unlink_request() +    { +        var processor = new CapturingRichMenuProcessor +        { +            UnlinkException = new LineResponseException("invalid rich menu unlink") +        }; +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            new InMemoryLineRichMenuIdCache(), +            new InMemoryRichMenuStateStore()); + +        var result = await workflow.UnassignAsync("U123"); + +        result.Succeeded.Should().BeFalse(); +        result.Status.Should().Be(LineRichMenuStatus.ProviderRejected); +        result.ErrorCode.Should().Be("line-richmenu-provider-rejected"); +        result.ErrorMessage.Should().Be("invalid rich menu unlink"); +    } + +    [Fact] +    public async Task UnassignAsync_returns_provider_unavailable_when_line_unlink_times_out() +    { +        var processor = new CapturingRichMenuProcessor +        { +            UnlinkException = new TaskCanceledException("provider timeout") +        }; +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            new InMemoryLineRichMenuIdCache(), +            new InMemoryRichMenuStateStore()); + +        var result = await workflow.UnassignAsync("U123"); + +        result.Succeeded.Should().BeFalse(); +        result.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable); +        result.ErrorCode.Should().Be("line-richmenu-provider-timeout"); +        result.ErrorMessage.Should().Be("provider timeout"); +    } + +    [Fact] +    public async Task UnassignAsync_does_not_swallow_unexpected_processor_exception() +    { +        var processor = new CapturingRichMenuProcessor +        { +            UnlinkException = new InvalidOperationException("processor bug") +        }; +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            new InMemoryLineRichMenuIdCache(), +            new InMemoryRichMenuStateStore()); + +        var action = () => workflow.UnassignAsync("U123"); + +        await action.Should().ThrowAsync<InvalidOperationException>() +            .WithMessage("processor bug"); +    } + +    [Fact] +    public async Task UnassignOrThrowAsync_throws_standard_exception_when_provider_unlink_fails() +    { +        var processor = new CapturingRichMenuProcessor +        { +            UnlinkException = new HttpRequestException("network unavailable") +        }; +        var workflow = new LineRichMenuAssignmentWorkflow( +            processor, +            new InMemoryLineRichMenuIdCache(), +            new InMemoryRichMenuStateStore()); + +        var action = () => workflow.UnassignOrThrowAsync("U123"); + +        var exception = await action.Should().ThrowAsync<LineRichMenuException>(); +        exception.Which.AssignmentResult.Should().NotBeNull(); +        exception.Which.AssignmentResult!.Status.Should().Be(LineRichMenuStatus.ProviderUnavailable); +        exception.Which.AssignmentResult.ErrorCode.Should().Be("line-richmenu-provider-unavailable"); +    }  } diff --git a/LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs b/LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs index 9cac15a3..4696edd6 100644 --- a/LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs +++ b/LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs @@ -16,6 +16,10 @@ internal sealed class CapturingRichMenuProcessor : ILineRichMenuProcessor        public string? DefaultRichMenuId { get; private set; }   +    public Exception? LinkException { get; set; } + +    public Exception? UnlinkException { get; set; } +      public int UploadedImageCount { get; private set; }        public int CreateAliasCount { get; private set; } @@ -79,6 +83,11 @@ internal sealed class CapturingRichMenuProcessor : ILineRichMenuProcessor        public Task LinkRichMenuToUserAsync(string userId, string richMenuId)      { +        if (LinkException != null) +        { +            throw LinkException; +        } +          Calls.Add($"link:{userId}:{richMenuId}");          LinkedUsers[userId] = richMenuId;          return Task.CompletedTask; @@ -86,6 +95,11 @@ internal sealed class CapturingRichMenuProcessor : ILineRichMenuProcessor        public Task UnlinkRichMenuFromUserAsync(string userId)      { +        if (UnlinkException != null) +        { +            throw UnlinkException; +        } +          Calls.Add($"unlink:{userId}");          LinkedUsers.Remove(userId);          return Task.CompletedTask; diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs b/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs index b716824d..6e3aa6ba 100644 --- a/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs +++ b/LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs @@ -1,9 +1,24 @@ +using Line.Messaging; +  namespace LineMessagingProcessor.RichMenus;    /// <summary> -/// RichMenu 指派工作流。 -/// 這裡只負責把產品給的 menu key 解析成 LINE richMenuId，再執行 link / unlink。 -/// 產品端的角色判斷、資料更新、畫面流程都不放在這裡，避免共用核心被任一產品綁死。 +/// RichMenu 使用者指派工作流。 +/// +/// 這個類別是未來多個產品共用 RichMenu 能力的核心入口之一。 +/// 產品層只需要傳入「想指派的邏輯選單代號」<c>menuKey</c>， +/// 共用層會負責把它解析成 LINE 平台實際使用的 <c>richMenuId</c>， +/// 再透過 <see cref="ILineRichMenuProcessor"/> 呼叫 LINE RichMenu API。 +/// +/// 設計邊界： +/// 1. 這裡只處理 RichMenu 指派與解除指派，不處理特定產品的身分資料、業務流程、畫面或通知文字。 +/// 2. <see cref="ILineRichMenuCatalog"/> 由產品提供，負責描述產品有哪些 RichMenu；共用層只讀取目錄。 +/// 3. <see cref="IRichMenuStateStore"/> 只是本流程的輔助紀錄，不是 LINE 平台狀態的唯一真相來源。 +/// 4. LINE / HTTP / timeout 錯誤會轉成標準 <see cref="LineRichMenuAssignmentResult"/>， +///    讓產品層用一致方式判斷失敗，而不是被迫捕捉各種底層例外。 +/// +/// 這樣切割後，建設公司維修系統、協會會員系統、發票收款系統等未來產品， +/// 只要提供自己的 catalog / policy / state store，就能共用同一套 RichMenu 指派流程。  /// </summary>  public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWorkflow  { @@ -39,27 +54,43 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork          var userId = NormalizeRequired(lineUserId, nameof(lineUserId));          var key = NormalizeRequired(menuKey, nameof(menuKey));   -        var richMenuId = await ResolveRichMenuIdAsync(key, cancellationToken).ConfigureAwait(false); -        if (string.IsNullOrWhiteSpace(richMenuId)) +        try          { -            return LineRichMenuAssignmentResult.Failure( -                LineRichMenuStatus.ValidationFailed, -                "line-richmenu-menu-key-not-found", -                $"RichMenu id for menu key '{key}' was not provisioned or could not be found online."); +            var richMenuId = await ResolveRichMenuIdAsync(key, cancellationToken).ConfigureAwait(false); +            if (string.IsNullOrWhiteSpace(richMenuId)) +            { +                return LineRichMenuAssignmentResult.Failure( +                    LineRichMenuStatus.ValidationFailed, +                    "line-richmenu-menu-key-not-found", +                    $"RichMenu id for menu key '{key}' was not provisioned or could not be found online."); +            } + +            var previous = await _stateStore.GetAsync(userId, cancellationToken).ConfigureAwait(false); +            if (string.Equals(previous?.CurrentMenuKey, key, StringComparison.OrdinalIgnoreCase)) +            { +                return LineRichMenuAssignmentResult.Linked( +                    previous?.PreviousMenuKey, +                    key, +                    richMenuId, +                    changed: false); +            } + +            await _processor.LinkRichMenuToUserAsync(userId, richMenuId).ConfigureAwait(false); +            await _stateStore.SetAsync( +                new RichMenuUserState( +                    userId, +                    key, +                    previous?.CurrentMenuKey, +                    expiresAt: null, +                    updatedAt: DateTimeOffset.UtcNow), +                cancellationToken).ConfigureAwait(false); + +            return LineRichMenuAssignmentResult.Linked(previous?.CurrentMenuKey, key, richMenuId, changed: true);          } - -        var previous = await _stateStore.GetAsync(userId, cancellationToken).ConfigureAwait(false); -        if (string.Equals(previous?.CurrentMenuKey, key, StringComparison.OrdinalIgnoreCase)) +        catch (Exception ex) when (TryMapProviderException(ex, out var result))          { -            return LineRichMenuAssignmentResult.Linked(previous?.PreviousMenuKey, key, richMenuId, changed: false); +            return result;          } - -        await _processor.LinkRichMenuToUserAsync(userId, richMenuId).ConfigureAwait(false); -        await _stateStore.SetAsync( -            new RichMenuUserState(userId, key, previous?.CurrentMenuKey, null, DateTimeOffset.UtcNow), -            cancellationToken).ConfigureAwait(false); - -        return LineRichMenuAssignmentResult.Linked(previous?.CurrentMenuKey, key, richMenuId, changed: true);      }        public async Task AssignOrThrowAsync( @@ -79,15 +110,28 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork          CancellationToken cancellationToken = default)      {          var userId = NormalizeRequired(lineUserId, nameof(lineUserId)); -        var previous = await _stateStore.GetAsync(userId, cancellationToken).ConfigureAwait(false); -        if (previous == null) + +        try          { -            return LineRichMenuAssignmentResult.Unlinked(null, changed: false); +            var previous = await _stateStore.GetAsync(userId, cancellationToken).ConfigureAwait(false); + +            // LINE 平台才是使用者目前 RichMenu 綁定狀態的唯一真相來源。 +            // +            // 如果 state store 查不到資料就直接回傳 no-op，真實產品會有狀態漂移風險： +            // - 應用程式重啟後，InMemory state store 可能已清空。 +            // - 未來產品可能用多台主機、背景服務或不同 state store 實作。 +            // - LINE 端可能仍保留舊 RichMenu 綁定，但本機輔助紀錄已不存在。 +            // +            // 因此解除綁定一律呼叫 LINE unlink，再清除本機輔助紀錄。 +            // changed=true 表示本流程已向 LINE 發出解除命令，不表示本機事前一定有紀錄。 +            await _processor.UnlinkRichMenuFromUserAsync(userId).ConfigureAwait(false); +            await _stateStore.RemoveAsync(userId, cancellationToken).ConfigureAwait(false); +            return LineRichMenuAssignmentResult.Unlinked(previous?.CurrentMenuKey, changed: true); +        } +        catch (Exception ex) when (TryMapProviderException(ex, out var result)) +        { +            return result;          } - -        await _processor.UnlinkRichMenuFromUserAsync(userId).ConfigureAwait(false); -        await _stateStore.RemoveAsync(userId, cancellationToken).ConfigureAwait(false); -        return LineRichMenuAssignmentResult.Unlinked(previous.CurrentMenuKey, changed: true);      }        public async Task UnassignOrThrowAsync(string lineUserId, CancellationToken cancellationToken = default) @@ -160,4 +204,41 @@ public sealed class LineRichMenuAssignmentWorkflow : ILineRichMenuAssignmentWork          await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);          return copy.ToArray();      } + +    private static bool TryMapProviderException(Exception exception, out LineRichMenuAssignmentResult result) +    { +        switch (exception) +        { +            case LineResponseException lineResponseException: +                result = LineRichMenuAssignmentResult.Failure( +                    LineRichMenuStatus.ProviderRejected, +                    "line-richmenu-provider-rejected", +                    lineResponseException.Message); +                return true; + +            case HttpRequestException httpRequestException: +                result = LineRichMenuAssignmentResult.Failure( +                    LineRichMenuStatus.ProviderUnavailable, +                    "line-richmenu-provider-unavailable", +                    httpRequestException.Message); +                return true; + +            case TaskCanceledException taskCanceledException +                when !taskCanceledException.CancellationToken.IsCancellationRequested: +                result = LineRichMenuAssignmentResult.Failure( +                    LineRichMenuStatus.ProviderUnavailable, +                    "line-richmenu-provider-timeout", +                    taskCanceledException.Message); +                return true; + +            default: +                // 這裡刻意不把所有 Exception 都轉成失敗結果。 +                // +                // RichMenu 共用層只應該把「LINE 平台或網路傳輸」這類產品可處理的外部錯誤 +                // 標準化成 LineRichMenuAssignmentResult；程式錯誤、資料流錯誤、未知狀態 +                // 必須直接往外拋，讓測試、監控與呼叫端能看見真正的 bug。 +                result = null!; +                return false; +        } +    }  } diff --git a/docs/ccg-dual-model-health-permanent-fix.md b/docs/ccg-dual-model-health-permanent-fix.md index 63778ad8..2195a3b6 100644 --- a/docs/ccg-dual-model-health-permanent-fix.md +++ b/docs/ccg-dual-model-health-permanent-fix.md @@ -1,17 +1,11 @@ -# CCG Gemini + Claude 雙模型健康檢查與永久修復手冊 +# CCG Gemini + Claude 雙模型自我修復永久手冊   -本文件說明本專案以後執行 CCG analysis / review 時，如何讓 Gemini + Claude 雙模型流程在失敗時自動先修復本機環境、重新執行，並在可恢復時繼續任務，而不是停在「雙模型失敗」。 +> 最後更新：2026-07-04   +> 目的：讓 CCG 分析 / Review 遇到 Gemini、Claude 或 `codeagent-wrapper` 失敗時，不再停在人工排錯，而是先自動健康檢查、修復本機環境、重試雙模型，並清楚區分「本機可修復」與「provider 額度 / session 限制」。   -## 核心結論 +## 結論   -以後不要直接手動呼叫： - -- `codeagent-wrapper --backend gemini` -- `codeagent-wrapper --backend claude` -- `gemini` -- `claude` - -所有 CCG analysis / review 都要先走專案自修復 runner： +以後只要要跑 CCG 雙模型分析或 Review，標準入口都是：    ```powershell  powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Invoke-CcgDualModelWithSelfHealing.ps1" ` @@ -21,77 +15,88 @@ powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Invoke-C    -OutputDirectory ".\.ccg\dual-model-runs"  ```   -`-Role` 可使用： +不要直接手動呼叫：   -- `analyzer` -- `architect` -- `reviewer` -- `debugger` -- `tester` -- `optimizer` -- `builder` +```powershell +codeagent-wrapper --backend gemini +codeagent-wrapper --backend claude +gemini +claude +```   -## 自動修復流程 +原因是直接呼叫只會暴露單點錯誤；自我修復 runner 才會統一處理 PATH、UTF-8、Gemini trust、Claude quota probe、stdout / stderr 保存、summary 判讀與重試。   -`Invoke-CcgDualModelWithSelfHealing.ps1` 會先呼叫 `Test-CcgDualModelHealth.ps1`，再執行 Gemini 與 Claude。它負責： +## 核心檔案   -- 確認 `codeagent-wrapper.exe` 是否存在。 -- 確認 `gemini.cmd`、`claude.cmd`、`python.exe` 是否可用。 -- 修復目前 PowerShell process 的 `PATH`。 -- 修復 Windows User `PATH`，避免下次新開終端機又找不到工具。 -- 設定 `GEMINI_CLI_TRUST_WORKSPACE=true`，避免新 worktree 的信任問題。 -- 設定 `CODEAGENT_LITE_MODE=true`，避免 Windows 上 Gemini progress mode 的不穩定路徑。 -- 設定 `PYTHONIOENCODING=utf-8`，避免中文輸出亂碼。 -- 將 prompt、stdout、stderr、health check、summary 全部寫入 `.ccg/dual-model-runs/`。 -- 要求模型真的輸出內容；不能只看 exit code。 -- 預設略過 backend smoke test，避免還沒 review 就先消耗模型額度。需要診斷登入或 provider 狀態時才加上 `-RunHealthBackendSmoke`。 +- `docs/scripts/Test-CcgDualModelHealth.ps1`   +  負責健康檢查與本機環境修復。   -## 未來失敗時的固定處理規則 +- `docs/scripts/Invoke-CcgDualModelWithSelfHealing.ps1`   +  負責正式執行 Gemini + Claude，並在失敗時自動先跑健康檢查、修復、重試。   -當 CCG analysis / review 發生失敗，不要停下任務，也不要直接從零手動查 Gemini 或 Claude。固定照以下流程： +- `.trellis/spec/guides/ccg-external-review-thinking-guide.md`   +  專案層思考指南，規定 CCG 外部 Review 的標準入口與故障分類。   -1. 將原本要分析或 review 的內容寫成 UTF-8 prompt 檔，放在 `.ccg/dual-model-runs/`。 -2. 用 `Invoke-CcgDualModelWithSelfHealing.ps1` 重新執行同一個任務。 -3. 讀取本次 run folder 內的 `summary.json`。 -4. 如果 `ok=true`，代表 Gemini + Claude 都完成，繼續整理雙模型結果。 -5. 如果 exit code 是 `2`，代表還有本機工具鏈問題；依 run folder 中的 health/stdout/stderr 修復後，再跑同一支 runner。 -6. 如果 `quotaBlocked=true`，代表 Gemini / Claude provider 額度、session limit、HTTP 429、登入狀態等外部因素阻擋。這不是本機可修復問題，不可以宣稱雙模型 review 成功。 -7. 只有在任務明確允許單模型 fallback 時，才可以加上 `-AllowSingleModelWhenQuotaBlocked`，而且報告中必須註明這不是完整雙模型 review。 +- `AGENTS.md`   +  專案根目錄規則，明確要求未來 CCG 分析 / Review 失敗時，不可直接停下，必須先走自我修復 runner。   -## Claude wrapper exit 1 的處理 +- `C:\Users\Administrator\.claude\commands\ccg\analyze.md`   +  `/ccg:analyze` 指令模板，已改成呼叫自我修復 runner。   -有時候 `codeagent-wrapper.exe --backend claude` 只會回： +- `C:\Users\Administrator\.claude\commands\ccg\review.md`   +  `/ccg:review` 指令模板，已改成呼叫自我修復 runner。   -```text -claude exited with status 1 -``` +## Runner 會自動修復什麼   -這個訊息本身不足以判斷是本機壞掉，還是 Claude provider / session limit。runner 會自動再做 direct Claude probe： +`Invoke-CcgDualModelWithSelfHealing.ps1` 會先呼叫 `Test-CcgDualModelHealth.ps1`，處理下列事項：   -```powershell -claude -p "Smoke test only..." --dangerously-skip-permissions --output-format text -``` +1. 設定 PowerShell / console 為 UTF-8。 +2. 設定 `GEMINI_CLI_TRUST_WORKSPACE=true`。 +3. 設定 `CODEAGENT_LITE_MODE=true`，避免 Windows + Gemini progress 模式造成不穩。 +4. 設定 `PYTHONIOENCODING=utf-8`。 +5. 補齊目前 process 的 PATH。 +6. 補齊 Windows User PATH。 +7. 確認 `codeagent-wrapper.exe` 存在。 +8. 確認 `gemini.cmd` 存在。 +9. 確認 `claude.cmd` 存在。 +10. 確認 `python.exe` 存在，避免 Gemini hooks 執行失敗。 +11. 將 prompt、stdout、stderr、health report、summary 全部寫到 `.ccg/dual-model-runs/`。 +12. 對 Claude wrapper 只回 `claude exited with status 1` 的情況，額外跑 direct Claude probe，判斷是否其實是 quota / session limit。   -如果 direct probe 顯示 `You've hit your session limit`、`rate limit`、`quota`、`429` 等訊息，runner 會將它分類為： +## 標準恢復流程   -```text -quotaBlocked=true -``` +當雙模型分析或 Review 失敗時： + +1. 把原本要分析或 review 的內容寫成 UTF-8 prompt 檔，放到 `.ccg/dual-model-runs/`。 +2. 使用 `Invoke-CcgDualModelWithSelfHealing.ps1`，指定正確的 `-Role`。 +3. 讀取產生的 `summary.json`。 +4. 若 `ok=true`，代表 Gemini + Claude 都成功產出可用結果，可以繼續任務。 +5. 若 exit code 是 `2`，代表仍有本機工具鏈問題；查看該 run folder 的 `health-attempt-*.json`、`*.stdout.md`、`*.stderr.md`，修復後再次執行同一個 runner。 +6. 若 `quotaBlocked=true`，代表 Gemini / Claude provider 額度、session limit、HTTP 429 或登入狀態阻擋。這不是本機可修復問題，不可宣稱雙模型 review 成功。 +7. 只有在任務明確允許單模型 fallback 時，才可以加上 `-AllowSingleModelWhenQuotaBlocked`，而且報告中必須清楚說明不是完整雙模型 review。   -這樣可以避免一直對不可本機修復的 provider 限制做錯誤修復。 +## Role 對照   -## Analyze / Review 指令 +`-Role` 可使用：   -專案的 CCG 指令已改成使用自修復 runner： +- `analyzer` +- `architect` +- `reviewer` +- `debugger` +- `tester` +- `optimizer` +- `builder`   -- `C:\Users\Administrator\.claude\commands\ccg\analyze.md` -- `C:\Users\Administrator\.claude\commands\ccg\review.md` +常用情境：   -以後呼叫 CCG analysis / review 時，這兩份指令會要求先建立 task prompt，再透過 `Invoke-CcgDualModelWithSelfHealing.ps1` 執行。 +- 需求 / 架構判斷：`-Role analyzer` +- 架構設計：`-Role architect` +- 程式碼審查：`-Role reviewer` +- 錯誤排查：`-Role debugger`   -## 快速健康檢查 +## 健康檢查   -如果只想檢查工具鏈，不想跑正式 review： +如果只想確認本機工具鏈，不想真的跑雙模型 Review：    ```powershell  powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-CcgDualModelHealth.ps1" ` @@ -100,7 +105,7 @@ powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-Ccg    -SkipBackendSmoke  ```   -如果要同時確認 Gemini / Claude provider 是否能真的回覆，才使用： +如果要連 Gemini / Claude backend smoke 一起測：    ```powershell  powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-CcgDualModelHealth.ps1" ` @@ -108,13 +113,80 @@ powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\docs\scripts\Test-Ccg    -OutputDirectory ".\.ccg\dual-model-runs"  ```   -## 給未來 agent 的規則 +注意：backend smoke 會消耗 provider 額度；正式分析 / Review 前通常不需要特別開啟，除非正在診斷登入、quota 或 provider 狀態。 + +## 常見失敗分類 + +| 現象 | 分類 | 處理方式 | +|---|---|---| +| `codeagent-wrapper.exe not found` | 本機工具鏈 | 確認 `C:\Users\Administrator\.claude\bin` 存在並在 PATH | +| `gemini.cmd not found` | 本機工具鏈 | 確認 `C:\Users\Administrator\AppData\Roaming\npm` 存在並在 PATH | +| `claude.cmd not found` | 本機工具鏈 | 確認 Claude CLI 安裝與 npm shim | +| `python.exe not found` | 本機工具鏈 | 補 Python 路徑，避免 Gemini hooks 失敗 | +| Gemini trust / workspace 錯誤 | 本機環境 | 確認 runner 有設定 `GEMINI_CLI_TRUST_WORKSPACE=true` | +| Gemini libuv / progress crash | 本機執行模式 | 使用 `--lite`，不要用 progress UI 當穩定 review 入口 | +| Claude `Not logged in` | 外部登入狀態 | 需手動 `claude auth login --claudeai` | +| `session limit` / `rate limit` / `quota` / `429` | provider 阻擋 | 等待額度恢復，或在明確允許時使用單模型 fallback | + +## Exit Code + +- `0`：成功，雙模型都完成。 +- `2`：本機工具鏈仍有可修復問題。 +- `3`：provider quota / session limit 等外部阻擋。 + +## Agent 行為規則 + +未來任何 agent 執行 CCG 分析 / Review 時，必須遵守： + +1. 先建立 UTF-8 task prompt。 +2. 先跑 `Invoke-CcgDualModelWithSelfHealing.ps1`。 +3. 不要先手動 debug Gemini / Claude。 +4. 不要把 `quotaBlocked=true` 說成雙模型成功。 +5. 本機可修復錯誤要修復後重跑同一個 runner。 +6. 如果 runner 成功，繼續原任務，不要因為第一次失敗就停止整個開發流程。 + +## 範例：Review git diff + +```powershell +$repo = "D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.7.WorktreeRefactorRichMenu" +$taskFile = Join-Path $repo ".ccg\dual-model-runs\my-review.md" + +$status = git -C $repo status --short +$diff = git -C $repo diff + +$prompt = @" +# Review Task + +請用 reviewer role 審查以下變更，分類 Critical / Warning / Info。 + +## Git Status + +```text +$status +``` + +## Git Diff + +```diff +$diff +``` +"@ + +[System.IO.File]::WriteAllText($taskFile, $prompt, [System.Text.UTF8Encoding]::new($false)) + +powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "docs\scripts\Invoke-CcgDualModelWithSelfHealing.ps1") ` +  -TaskFile $taskFile ` +  -Role reviewer ` +  -RepositoryPath $repo ` +  -OutputDirectory (Join-Path $repo ".ccg\dual-model-runs") +``` + +## 對未來工作的幫助   -- 不要因為第一次 Gemini / Claude 失敗就停止任務。 -- 不要跳過自修復 runner 直接手動呼叫模型。 -- 不要把 `quotaBlocked=true` 報告成「雙模型 review 已完成」。 -- 不要把只有 Gemini 或只有 Claude 的結果包裝成雙模型結果。 -- 如果 runner 成功，任務要繼續往 implementation / review / fix 前進。 -- 如果 runner 失敗但不是 quota，先修本機工具鏈，再重跑 runner。 +這個永久修復的重點不是「保證 Gemini / Claude 永遠不會失敗」，而是把失敗處理標準化：   -此流程的目的不是保證 provider 永遠有額度，而是讓所有可本機修復的問題自動被修掉，並讓不可本機修復的 provider 限制被清楚分類，避免任務卡在模糊的「雙模型壞了」。 +- 本機問題由 runner 自動修復或明確指出。 +- 外部 quota / session 問題被正確分類，不再誤判成工具鏈壞掉。 +- 所有輸入輸出都有紀錄，之後可追查。 +- `/ccg:analyze` 與 `/ccg:review` 不再各自手寫不同命令。 +- 後續任務遇到雙模型失敗時，可以自我修復後繼續，不會停在同一類問題反覆人工排查。
`

## 請輸出
- Critical：會破壞正確性、架構邊界、產品共用性的問題；沒有請寫 No Critical。
- Warning：建議修正但非阻斷。
- Info：小建議。
- Verdict：PASS 或 NEEDS_CHANGES。

</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.Trellis SessionStart 上下文已載入。

### 審查報告

#### **Critical**
- **No Critical**

#### **Warning**
- **No Warning**

#### **Info**
1. **Exception Filter 實作乾淨**：使用 `catch (Exception ex) when (TryMapProviderException(ex, out var result))` 進行過濾，只攔截 `LineResponseException`、`HttpRequestException` 以及非呼叫端主動取消的 `TaskCanceledException`，這能精準捕捉外部 Provider（LINE / 網路）錯誤並標準化為 `LineRichMenuAssignmentResult`，而讓未知的程式碼錯誤（如 `NullReferenceException`, `KeyNotFoundException` 等）正常拋出，避免了先前 TryMapException 會遮蔽 bug 的問題，非常好。
2. **LINE 真相來源設計**：在 `UnassignAsync` 中移除了對 `stateStore` 是否有資料的 short-circuit（不論 state store 是否為空都強制向 LINE 發送 unlink），能有效防止本地快取與 LINE 平台狀態不一致產生的狀態漂移風險，極具魯棒性。
3. **單元測試完整性**：新增的測試名稱詳盡，且特別涵蓋了「不吞掉非預期 Exception」的邊界測試（如 `AssignAsync_does_not_swallow_unexpected_processor_exception`），確保後續維護者不會打破這個異常邊界。

#### **Verdict**
**PASS**

---
SESSION_ID: 9918abdc-1d7c-481b-b55c-ac87e59830e9
```

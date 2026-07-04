<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: (not initialized)
Git: branch Jesus_5.1.7.WorktreeRefactorRichMenu; dirty 70 paths.
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
# CCG Review Task: LINE RichMenu Shared Orchestrator

請以 reviewer 角色審查目前 worktree 的 RichMenu Shared Orchestrator 重構。

## Review Scope

- Worktree: D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.7.WorktreeRefactorRichMenu
- Branch: Jesus_5.1.7.WorktreeRefactorRichMenu
- 主要目標：將 LINE RichMenu 抽離為可由未來 ASP.NET Core 產品共用的 LineMessagingProcessor.RichMenus 核心。
- 邊界要求：RichMenu 共用核心不得依賴 ChurchReport、CRM、Controller、DbContext、IActionResult 或產品流程。
- 設計要求：代碼要易於管理，符合低特殊情況、資料流清楚、不藏全域狀態、一個型別只做一件事。
- 不要審查已清理的 bin/obj/artifacts 產物。

## Local Verification Already Passed

- dotnet test .\LineMessagingProcessor.RichMenus.Tests\LineMessagingProcessor.RichMenus.Tests.csproj -v minimal -p:UseSharedCompilation=false：13 passed。
- dotnet test .\LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj -v minimal -p:UseSharedCompilation=false：3 passed。
- dotnet test .\LineMessagingProcessor.Tests\LineMessagingProcessor.Tests.csproj -v minimal -p:UseSharedCompilation=false：33 passed。
- dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LineSharedWorkflow|FullyQualifiedName~PushUtilityWorkflow" -v minimal -p:UseSharedCompilation=false：28 passed。
- dotnet build .\ChurchReport\ChurchReport.csproj -v minimal -p:UseSharedCompilation=false：build succeeded，0 warning，0 error。
- Boundary scans passed：RichMenus 無 ChurchReport/CRM/Controller/DbContext/IActionResult，舊 Workflows 無 RichMenu residue，RichMenus csproj 不再引用 Workflows，RichMenu source/tests 無 mojibake markers。

## Git Status

``text
 M ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj  M ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs  M ChurchReport.sln  M ChurchReport/ChurchReport.csproj  M ChurchReport/Tools/LineUtilityClass.cs  M ChurchReport/Tools/PushUtility.cs  M LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj  M LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs  M LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj  M LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs RM LineMessagingProcessor.Workflows.Tests/LineRichMenuWorkflowTests.cs -> LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs RM LineMessagingProcessor.Workflows/ILineRichMenuWorkflow.cs -> LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs RM LineMessagingProcessor.Workflows/LineRichMenuCreateUploadAndLinkRequest.cs -> LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs RM LineMessagingProcessor.Workflows/LineRichMenuDeleteLinkedRequest.cs -> LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs RM LineMessagingProcessor.Workflows/LineRichMenuException.cs -> LineMessagingProcessor.RichMenus/LineRichMenuException.cs RM LineMessagingProcessor.Workflows/LineRichMenuResult.cs -> LineMessagingProcessor.RichMenus/LineRichMenuResult.cs RM LineMessagingProcessor.Workflows/LineRichMenuWorkflow.cs -> LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs  M LineMessagingProcessor/LineMessagingProcessorClass.cs ?? .ccg/dual-model-runs/ ?? .ccg/tasks/ccg-dual-model-self-healing/ ?? .ccg/tasks/line-richmenu-shared-orchestrator-review/ ?? .ccg/tasks/richmenu-shared-architecture-brainstorm/.turns.json ?? LineMessagingProcessor.RichMenus.Tests/Actions/ ?? LineMessagingProcessor.RichMenus.Tests/Assignment/ ?? LineMessagingProcessor.RichMenus.Tests/Boundary/ ?? LineMessagingProcessor.RichMenus.Tests/LineMessagingProcessor.RichMenus.Tests.csproj ?? LineMessagingProcessor.RichMenus.Tests/Orchestration/ ?? LineMessagingProcessor.RichMenus.Tests/Provisioning/ ?? LineMessagingProcessor.RichMenus.Tests/Support/ ?? LineMessagingProcessor.RichMenus.Tests/Triggers/ ?? LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs ?? LineMessagingProcessor.RichMenus/ILineRichMenuCatalog.cs ?? LineMessagingProcessor.RichMenus/ILineRichMenuIdCache.cs ?? LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs ?? LineMessagingProcessor.RichMenus/ILineRichMenuProvisioningWorkflow.cs ?? LineMessagingProcessor.RichMenus/ILineRichMenuTextTriggerResolver.cs ?? LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs ?? LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs ?? LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs ?? LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs ?? LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs ?? LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs ?? LineMessagingProcessor.RichMenus/LineMessagingProcessor.RichMenus.csproj ?? LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuAliasNotFoundException.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuAssignmentResult.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuStatus.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuSyncOutcome.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerOptions.cs ?? LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs ?? LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs ?? LineMessagingProcessor.RichMenus/RichMenuContext.cs ?? LineMessagingProcessor.RichMenus/RichMenuDecision.cs ?? LineMessagingProcessor.RichMenus/RichMenuDecisionPriority.cs ?? LineMessagingProcessor.RichMenus/RichMenuExpirationSweepReport.cs ?? LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs ?? LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs ?? LineMessagingProcessor.RichMenus/RichMenuTextContext.cs ?? LineMessagingProcessor.RichMenus/RichMenuTextDecision.cs ?? LineMessagingProcessor.RichMenus/RichMenuUserState.cs ?? LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs ?? docs/ccg-dual-model-health-permanent-fix.md ?? docs/scripts/ ?? docs/superpowers/reports/2026-07-04-line-richmenu-shared-orchestrator-implementation-report.md
``

## Diff Stat

``text
 .../ChurchReport.MemberInfo.Tests.csproj           |   5 +-  .../LineSharedWorkflow/PushUtilityWorkflowTests.cs |   2 +  ChurchReport.sln                                   |  64 +++++++  ChurchReport/ChurchReport.csproj                   |  39 ++---  ChurchReport/Tools/LineUtilityClass.cs             |  86 ++++-----  ChurchReport/Tools/PushUtility.cs                  |  50 +++---  .../LineMessagingProcessor.AspNetCore.Tests.csproj |   3 +-  ...ingProcessorServiceCollectionExtensionsTests.cs |  33 ++++  .../LineMessagingProcessor.AspNetCore.csproj       |   3 +-  ...essagingProcessorServiceCollectionExtensions.cs |  63 ++++++-  .../LineRichMenuWorkflowTests.cs                   |  12 +-  .../ILineRichMenuWorkflow.cs                       |   9 +-  .../LineRichMenuCreateUploadAndLinkRequest.cs      |   7 +-  .../LineRichMenuDeleteLinkedRequest.cs             |   8 +-  .../LineRichMenuException.cs                       |   7 +-  .../LineRichMenuResult.cs                          |  15 +-  .../LineRichMenuWorkflow.cs                        |  45 ++---  .../LineMessagingProcessorClass.cs                 | 193 +++++++++++++--------  18 files changed, 432 insertions(+), 212 deletions(-)
``

## Diff

``diff
diff --git a/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj b/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj index 1b50b31d..ba4b407d 100644 --- a/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj +++ b/ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj @@ -5,7 +5,7 @@      <Nullable>enable</Nullable>      <IsPackable>false</IsPackable>      <IsTestProject>true</IsTestProject> -    <!-- 與主專案一致：允許 Newtonsoft.Json 等套件降級（NU1605）不擋建置 --> +    <!-- ?蜓撠?銝?湛??迂 Newtonsoft.Json 蝑?隞園?蝝?NU1605嚗??遣蝵?-->      <NoWarn>NU1605</NoWarn>    </PropertyGroup>    <ItemGroup> @@ -22,5 +22,6 @@      <ProjectReference Include="..\LineMessagingProcessor.Workflows\LineMessagingProcessor.Workflows.csproj" />      <ProjectReference Include="..\SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj" />      <ProjectReference Include="..\SpeechMessage.Payments.Workflows\SpeechMessage.Payments.Workflows.csproj" /> +    <ProjectReference Include="..\LineMessagingProcessor.RichMenus\LineMessagingProcessor.RichMenus.csproj" />    </ItemGroup> -</Project> +</Project> \ No newline at end of file diff --git a/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs b/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs index 593995a4..6790da1d 100644 --- a/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs +++ b/ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PushUtilityWorkflowTests.cs @@ -1,6 +1,7 @@  using ChurchReport.Tools;  using FluentAssertions;  using Line.Messaging; +using LineMessagingProcessor.RichMenus;  using LineMessagingProcessor.Workflows;  using Xunit;   @@ -362,3 +363,4 @@ public sealed class PushUtilityWorkflowTests          }      }  } + diff --git a/ChurchReport.sln b/ChurchReport.sln index 339b23e3..9e26f259 100644 --- a/ChurchReport.sln +++ b/ChurchReport.sln @@ -35,6 +35,10 @@ Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LineMessagingProcessor.AspN  EndProject  Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LineMessagingProcessor.AspNetCore.Tests", "LineMessagingProcessor.AspNetCore.Tests\LineMessagingProcessor.AspNetCore.Tests.csproj", "{2E0507A6-A062-447C-9841-7A79CAC2BCBA}"  EndProject +Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LineMessagingProcessor.RichMenus", "LineMessagingProcessor.RichMenus\LineMessagingProcessor.RichMenus.csproj", "{0E63738E-14C2-4511-AE10-D1B114A3C9B6}" +EndProject +Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "LineMessagingProcessor.RichMenus.Tests", "LineMessagingProcessor.RichMenus.Tests\LineMessagingProcessor.RichMenus.Tests.csproj", "{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}" +EndProject  Global  	GlobalSection(SolutionConfigurationPlatforms) = preSolution  		Debug_LearnCrm|Any CPU = Debug_LearnCrm|Any CPU @@ -534,6 +538,66 @@ Global  		{2E0507A6-A062-447C-9841-7A79CAC2BCBA}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU  		{2E0507A6-A062-447C-9841-7A79CAC2BCBA}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU  		{2E0507A6-A062-447C-9841-7A79CAC2BCBA}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug|Any CPU.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug|Any CPU.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug|x64.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug|x64.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug|x86.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Debug|x86.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Release|Any CPU.ActiveCfg = Release|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Release|Any CPU.Build.0 = Release|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Release|x64.ActiveCfg = Release|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Release|x64.Build.0 = Release|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Release|x86.ActiveCfg = Release|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Release|x86.Build.0 = Release|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU +		{0E63738E-14C2-4511-AE10-D1B114A3C9B6}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug|Any CPU.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug|Any CPU.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug|x64.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug|x64.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug|x86.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Debug|x86.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Release|Any CPU.ActiveCfg = Release|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Release|Any CPU.Build.0 = Release|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Release|x64.ActiveCfg = Release|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Release|x64.Build.0 = Release|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Release|x86.ActiveCfg = Release|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Release|x86.Build.0 = Release|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU +		{17E471A6-6B5A-4543-9CEF-746A63A9D0BA}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU  	EndGlobalSection  	GlobalSection(SolutionProperties) = preSolution  		HideSolutionNode = FALSE diff --git a/ChurchReport/ChurchReport.csproj b/ChurchReport/ChurchReport.csproj index 0ff27a7f..be2947c3 100644 --- a/ChurchReport/ChurchReport.csproj +++ b/ChurchReport/ChurchReport.csproj @@ -6,31 +6,27 @@      <AssemblyName>ChurchReport</AssemblyName>      <OutputType>Exe</OutputType>      <BuildInParallel>false</BuildInParallel> -    <!-- 移除 RuntimeIdentifier，.NET 10 不支援 win7-x86 --> -     -    <!-- ✅ Phase 5.1: GC 優化設定 --> +    <!-- 蝘駁 RuntimeIdentifier嚗?NET 10 銝??win7-x86 --> +    <!-- ??Phase 5.1: GC ?芸?閮剖? -->      <ServerGarbageCollection>true</ServerGarbageCollection>      <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>      <RetainVMGarbageCollection>true</RetainVMGarbageCollection> -     -    <!-- 允許套件降級警告不影響建置 --> +    <!-- ?迂憟辣??霅血?銝蔣?踹遣蝵?-->      <NoWarn>$(NoWarn);NU1605;NU1902;NU1903;ASPDEPR005;CA1416;CA2200;CS0105;CS0108;CS0114;CS0162;CS0168;CS0169;CS0219;CS0414;CS0472;CS0618;CS0649;CS8073;CS8632;CS8981;CS1717;CS4014;MVC1000;SYSLIB0014;SYSLIB0021</NoWarn> -     -    <!-- ✅ Phase 5: .NET 10 效能優化設定 --> +    <!-- ??Phase 5: .NET 10 ??芸?閮剖? -->      <TieredPGO>true</TieredPGO>      <OptimizationPreference>Speed</OptimizationPreference>      <EnableAVX>true</EnableAVX> -     -    <!-- ✅ 啟用 Ready to Run 編譯，加速冷啟動 --> +    <!-- ??? Ready to Run 蝺刻陌嚗???? -->      <PublishReadyToRun>true</PublishReadyToRun>    </PropertyGroup>    <ItemGroup>      <Content Include="wwwroot\**\*;Views\**\*;Areas\**\Views;appsettings.json;appsettings.Production.json;web.config">        <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>      </Content> -    <Compile Remove="文件\佈署規劃\**" /> -    <EmbeddedResource Remove="文件\佈署規劃\**" /> -    <None Remove="文件\佈署規劃\**" /> +    <Compile Remove="?辣\雿蔡閬?\**" /> +    <EmbeddedResource Remove="?辣\雿蔡閬?\**" /> +    <None Remove="?辣\雿蔡閬?\**" />      <None Remove="Views\Home\BindingResultView.cshtml" />      <None Remove="Views\Home\DedicationFeeAuditViewLine.cshtml" />      <None Remove="Views\Home\DedicationInofView.cshtml" /> @@ -44,9 +40,9 @@      <None Remove="Views\Home\QualificationView.cshtml" />      <None Remove="Views\Home\SchedulerView.cshtml" />      <None Remove="Views\Home\VisitorCard.cshtml" /> -    <None Remove="wwwroot\assets\images\永和堂牧養系統web_banner-01.jpg" /> +    <None Remove="wwwroot\assets\images\瘞詨??擗頂蝯患eb_banner-01.jpg" />      <None Remove="wwwroot\css\MasterDetail.css" /> -    <None Remove="wwwroot\css\Site - 複製.css" /> +    <None Remove="wwwroot\css\Site - 銴ˊ.css" />      <None Remove="wwwroot\js\devextreme\devextreme-intl.min.js" />      <None Remove="wwwroot\js\devextreme\localization\dx.messages.zh-tw.js" />      <None Remove="wwwroot\js\FeeDataGridAjax.js" /> @@ -59,9 +55,9 @@      <PackageReference Include="Microsoft.AspNet.WebApi.Client" Version="6.0.0" />      <PackageReference Include="Microsoft.AspNetCore.Mvc.NewtonsoftJson" Version="10.0.0" />      <PackageReference Include="Microsoft.PowerPlatform.Dataverse.Client" Version="1.2.10" /> -    <!-- ✅ SixLabors.ImageSharp - 用於處理圖片 EXIF Orientation，修正直拍照片旋轉問題 --> +    <!-- ??SixLabors.ImageSharp - ?冽???? EXIF Orientation嚗耨甇????頧?憿?-->      <PackageReference Include="SixLabors.ImageSharp" Version="3.1.6" /> -    <!-- .NET 10 使用框架引用，不需要明確的 AspNetCore 套件 --> +    <!-- .NET 10 雿輻獢撘嚗??閬?蝣箇? AspNetCore 憟辣 -->      <!--      <PackageReference Include="Microsoft.AspNetCore" Version="2.2.0" />      <PackageReference Include="Microsoft.AspNetCore.Authentication.Cookies" Version="2.2.0" /> @@ -73,11 +69,11 @@      <PackageReference Include="Microsoft.AspNetCore.Session" Version="2.2.0" />      <PackageReference Include="Microsoft.AspNetCore.StaticFiles" Version="2.2.0" />      --> -    <!-- BrowserLink 在新版本中已不推薦使用 --> +    <!-- BrowserLink ?冽?銝剖歇銝?虫蝙??-->      <!-- <PackageReference Include="Microsoft.VisualStudio.Web.BrowserLink.Loader" Version="14.1.0" /> -->      <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />    </ItemGroup> -  <!-- DotNetCliToolReference 在 .NET Core 3.0+ 已被移除 --> +  <!-- DotNetCliToolReference ??.NET Core 3.0+ 撌脰◤蝘駁 -->    <!--    <ItemGroup>      <DotNetCliToolReference Include="BundlerMinifier.Core"> @@ -94,17 +90,18 @@      <ProjectReference Include="..\SpeechMessage.Payments\SpeechMessage.Payments.csproj" />      <ProjectReference Include="..\SpeechMessage.Payments.AspNetCore\SpeechMessage.Payments.AspNetCore.csproj" />      <ProjectReference Include="..\SpeechMessage.Payments.Workflows\SpeechMessage.Payments.Workflows.csproj" /> +    <ProjectReference Include="..\LineMessagingProcessor.RichMenus\LineMessagingProcessor.RichMenus.csproj" />    </ItemGroup>    <ItemGroup>      <Folder Include="Views\Dedication\" /> -    <Folder Include="文件\修正官網奉獻網頁\" /> +    <Folder Include="?辣\靽格迤摰雯憟蝬脤?\" />    </ItemGroup>    <ItemGroup>      <Reference Include="DevExtreme.AspNet.Core">        <HintPath>C:\Users\Administrator\.nuget\packages\devextreme.aspnet.core\23.1.5\lib\net6.0\DevExtreme.AspNet.Core.dll</HintPath>      </Reference>      <Reference Include="Microsoft.Crm.Sdk.Proxy"> -      <HintPath>..\..\..\..\DevExpressDevExtreme-23.1.5版本\響應式\主要版本\ChurchReport.RazorPages\Dynamics 365 SDK DLL\Microsoft.CrmSdk.CoreAssemblies.9.0.2.52\lib\net462\Microsoft.Crm.Sdk.Proxy.dll</HintPath> +      <HintPath>..\..\..\..\DevExpressDevExtreme-23.1.5?\?踵?撘銝餉??\ChurchReport.RazorPages\Dynamics 365 SDK DLL\Microsoft.CrmSdk.CoreAssemblies.9.0.2.52\lib\net462\Microsoft.Crm.Sdk.Proxy.dll</HintPath>      </Reference>    </ItemGroup>    <PropertyGroup Condition=" '$(Configuration)' == 'Release' "> @@ -116,4 +113,4 @@        <UserProperties DismissedDevExtremeVersion="" />      </VisualStudio>    </ProjectExtensions> -</Project> +</Project> \ No newline at end of file diff --git a/ChurchReport/Tools/LineUtilityClass.cs b/ChurchReport/Tools/LineUtilityClass.cs index 49f1b8f3..8a725b20 100644 --- a/ChurchReport/Tools/LineUtilityClass.cs +++ b/ChurchReport/Tools/LineUtilityClass.cs @@ -13,17 +13,18 @@ using System.IO;  using ToolUtilityNameSpace;  using Microsoft.Extensions.Configuration;  using LineMessagingProcessor; +using LineMessagingProcessor.RichMenus;  using LineMessagingProcessor.Workflows;    namespace ChurchReport.Tools  {      /// <summary> -    /// LINE 閮撌亙憿 -    /// ??Phase 5: 甇?Ⅱ撖衣 IDisposable Pattern 隞仿甇Ｚ??園?瘣拇? +    /// LINE ?殷?蹓?鈭??遴竣?? +    /// ??Phase 5: ???⊥?銵?? IDisposable Pattern ?遛謢?嚗????????      /// </summary>      public class LineUtilityClass : IDisposable      { -            #region 蝟餌絞? +            #region ??蝯??塗              //IServiceProvider m_ServiceProvider;              //ITracingService m_TracingService;              //IPluginExecutionContext m_Context; @@ -31,26 +32,26 @@ namespace ChurchReport.Tools              //IOrganizationServiceFactory m_ServiceFactory;              IOrganizationService m_CrmService;   -            // 蝟餌絞?喃???蝜?蝔? +            // ??蝯???????????              public String m_OrganizationName = "";                ReplyUtility m_ReplyUtility;   -            #region Channel Access Token 閮剖? +            #region Channel Access Token ?桀??   -            // ?蔭撱箸??刻?撖虫? +            // ???梁捂??????              private static readonly IConfigurationBuilder m_ConfigurationBuilder = new ConfigurationBuilder()                  .SetBasePath(Directory.GetCurrentDirectory())                  .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);              private static readonly IConfiguration m_Configuration = m_ConfigurationBuilder.Build();   -            // 敺?蝵株???Channel Access Token +            // ?綜筐??菜???Channel Access Token              private static string GetChannelAccessToken(string organization)              {                  string token = m_Configuration[$"LineMessaging:{organization}:ChannelAccessToken"];                  if (string.IsNullOrEmpty(token))                  { -                    // 憒??曆??唳?摰?蝜?閮剖?嚗蝙?券?閮剔?蝜? +                    // ??????????堊垓?????桀????????桀????                      string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";                      token = m_Configuration[$"LineMessaging:{defaultOrg}:ChannelAccessToken"];                  } @@ -81,20 +82,20 @@ namespace ChurchReport.Tools                private const String DEVELOPER_LINE_ID = @"U7638e4ed509708a3573ba6d69970583d";   -            // Line ?詨?耦瑼?雿蔭 -            private const String LINE_MENU_PATH = @"D:\Line ?詨\"; +            // Line ?閰制??謘潸衣???選??? +            private const String LINE_MENU_PATH = @"D:\Line ?閰制?\";     -            // 璅⊥?身???? +            // ?豰??澈?????              private const String m_Default_ThumbnailImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1"; -            // 璆??釦?芋?輸?閮剔??? +            // ??????蹇??頛??桀???謘?              private const String m_Yangmeillc_ThumbnailImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1"; -            // 憟賜鈭箸芋?輸?閮剔??? +            // ???仿蝞貉??頛??桀???謘?              private const String m_TpeHoc_ThumbnailImageUrl = "https://od.lk/s/ODdfNTg5ODc5OF8/2017_06_sermon_6-18.jpg";                #endregion   -            #region ?閮擃? +            #region ????殉朵???          private bool _disposed = false;            protected virtual void Dispose(bool disposing) @@ -103,13 +104,13 @@ namespace ChurchReport.Tools                if (disposing)              { -                // ??? ToolUtilityClass +                // ????? ToolUtilityClass                  m_ToolUtilityClass?.Dispose();                   -                // ??? LineMessagingClient +                // ????? LineMessagingClient                  m_LineMessagingClient?.Dispose();                   -                // ??? ReplyUtility +                // ????? ReplyUtility                  (m_ReplyUtility as IDisposable)?.Dispose();              }   @@ -152,7 +153,7 @@ namespace ChurchReport.Tools              {                  m_ToolUtilityClass = aToolUtilityClass ?? throw new ArgumentNullException(nameof(aToolUtilityClass));   -                // ????雿輻?身蝯???Token +                // ?豲??謘??輯撒???澈?荔????Token                  string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";                  m_ChannelAccessToken = GetChannelAccessToken(defaultOrg);                   @@ -242,14 +243,14 @@ namespace ChurchReport.Tools                private static ILineRichMenuWorkflow CreateDefaultRichMenuWorkflow(LineMessagingClient lineMessagingClient)              { -                return new LineRichMenuWorkflow(new LineMessagingProcessorClass(lineMessagingClient)); +                return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient)));              }                public void SetupChannelAccessToken(ref IOrganizationService aCrmService)              {                  try                  { -                    // ?寞?蝯??迂敺?蝵格?霈???? Channel Access Token +                    // ?撖??荔??????綜筐??菜???謘???? Channel Access Token                      if (this.m_OrganizationName == "jesus")                      {                          m_ChannelAccessToken = GetChannelAccessToken("Jesus"); @@ -260,16 +261,16 @@ namespace ChurchReport.Tools                      }                      else                      { -                        // 雿輻?身蝯? +                        // ?輯撒???澈?荔??                          string defaultOrg = m_Configuration["LineMessaging:DefaultOrganization"] ?? "Jesus";                          m_ChannelAccessToken = GetChannelAccessToken(defaultOrg);                      }   -                    // ?????LineMessagingClient -                    // ?ㄐ?芣??遣?祇??交??? LineMessagingClient?? -                    // 憒??芯???ILineNotificationWorkflow 瘜典?唬?靘陷憭?蝜????澆蝡荔? -                    // 撖阡???粥 workflow ????processor/client嚗??舫ㄐ?遣??client?? -                    // ?迨甇???亦???workflow 撅支?敹??瑕??詨???蝜?token 頝舐?賢??? +                    // ????豲???LineMessagingClient +                    // ?謕???????蟡??鈭???? LineMessagingClient?? +                    // ????????ILineNotificationWorkflow ????????瑟謍?????謜??瞉?∟?? +                    // ???蹓鳴??貊畦 workflow ?蹓???processor/client??????威謕?????client?? +                    // ?蹎縐????鈭????workflow ???對?????閰??????token ????鞈???                      m_LineMessagingClient = new LineMessagingClient(m_ChannelAccessToken);                      RebuildDefaultWorkflowsForCurrentClient();                      m_ReplyUtility = new ReplyUtility(m_LineMessagingClient, m_LineReplyWorkflow); @@ -300,8 +301,8 @@ namespace ChurchReport.Tools                  }              }   -            #region 撌亙? -            #region Line Messagin Api SDK?喲? +            #region ????? +            #region Line Messagin Api SDK??笨?              private async Task SendBestEffortSdkMessagesAsync(                  string userId,                  IReadOnlyList<ISendMessage> messages, @@ -384,7 +385,7 @@ namespace ChurchReport.Tools                      MessageToSend,                      "ChurchReport.LineUtilityClass.SendMessageAsync");   -                //this.m_ToolUtilityClass.TraceByLevel(5, 1, "?喲???" + aHttpResponseMessage); +                //this.m_ToolUtilityClass.TraceByLevel(5, 1, "??笨蹓???" + aHttpResponseMessage);                    return;              } @@ -634,7 +635,7 @@ namespace ChurchReport.Tools              public async Task<String> AddRichMenuMessage(string UserId)              {                  var richMenu = CreateLegacySingleButtonRichMenu(); -                var imagePath = @"D:\暫存區\richmenu.PNG"; +                var imagePath = @"D:\?怠??\richmenu.PNG";                  await m_LineRichMenuWorkflow.CreateUploadAndLinkOrThrowAsync(new LineRichMenuCreateUploadAndLinkRequest                  {                          UserId = UserId, @@ -658,7 +659,7 @@ namespace ChurchReport.Tools                      messageToSend,                      "ChurchReport.LineUtilityClass.AddRichMenuMessage");   -                return "成功"; +                return "??";              }                public async Task<String> DeleteRichMenuMessage(string UserId) @@ -672,7 +673,7 @@ namespace ChurchReport.Tools                          }                  });   -                return "成功"; +                return "??";              }                private static RichMenu CreateLegacySingleButtonRichMenu() @@ -696,7 +697,7 @@ namespace ChurchReport.Tools              #endregion              #endregion   -            #region 閮剖???澆? +            #region ?桀???謍船??瞉?                public void SetupActionList(Entity aLetterEntity, ref TemplateMessageClass aTemplateMessageClass)              { @@ -710,12 +711,12 @@ namespace ChurchReport.Tools                              type = ConvertActionType(aLetterEntity, "new_action_category_1"),                              label = ActionLabel_1,                              text = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_template_text_1"), -                            data = "??=" + ActionLabel_1 + "& EntityId=" + aLetterEntity.Id, +                            data = "???=" + ActionLabel_1 + "& EntityId=" + aLetterEntity.Id,                              uri = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aLetterEntity, "new_template_uri_1"),                                //type = "postback", -                            //label = "鞈潸眺", -                            //data = "action=鞈潸眺&itemid=001", +                            //label = "?螂??, +                            //data = "action=?螂??itemid=001",                              //uri = "http://www.speechmessage.com.tw",                          };                          aTemplateMessageClass.template.actions.Add(aActionClass); @@ -764,7 +765,7 @@ namespace ChurchReport.Tools              }              #endregion   -            #region ??撖? +            #region ??????蹓壇?                public Entity GetLineSender(Entity aLetterEntity)              { @@ -774,7 +775,7 @@ namespace ChurchReport.Tools                        for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)                      { -                        #region ?? LINE 閮撖? +                        #region ?謘? LINE ?殷?蹓??蹓壇?                          EntityReference aContactEntityReference = (EntityReference)aFromEntityCollection.Entities[i]["partyid"];                            Guid aContactId = aContactEntityReference.Id; @@ -804,7 +805,7 @@ namespace ChurchReport.Tools                        for (int i = 0; i < aFromEntityCollection.Entities.Count; i++)                      { -                        #region ?? LINE 閮?嗡辣???典??LINE ID +                        #region ?謘? LINE ?殷?蹓??∟麾?????????LINE ID                          LineId = "";                          ContactFullName = GetContactPartyFullName(aFromEntityCollection.Entities[i], ref LineId);                          #endregion @@ -836,7 +837,7 @@ namespace ChurchReport.Tools                        Entity aRetrievedContact = this.m_ToolUtilityClass.RetrieveEntity("contact", aContactId);   -                    //if (aContactName.StartsWith("Line?啣??亥?)) +                    //if (aContactName.StartsWith("Line????鈭氐?))                      //if (aContactName.EndsWith("(Line)"))                      //{                      //    aContactName = this.m_ToolUtilityClass.GetEntityStringAttribute(ref aRetrievedContact, "new_line_displayname"); @@ -859,7 +860,7 @@ namespace ChurchReport.Tools            }   -        #region POST ?憛? +        #region POST ????            public class PostTextClass          { @@ -1009,7 +1010,7 @@ namespace ChurchReport.Tools            #endregion   -        #region 撖LINE????Class +        #region ??謒林INE??????Class            public class MessageContent          { @@ -1034,3 +1035,4 @@ namespace ChurchReport.Tools      #endregion  }   + diff --git a/ChurchReport/Tools/PushUtility.cs b/ChurchReport/Tools/PushUtility.cs index b027a1e6..61477442 100644 --- a/ChurchReport/Tools/PushUtility.cs +++ b/ChurchReport/Tools/PushUtility.cs @@ -1,5 +1,6 @@  using Line.Messaging;  using LineMessagingProcessor; +using LineMessagingProcessor.RichMenus;  using LineMessagingProcessor.Workflows;  using System;  using System.Collections.Generic; @@ -10,7 +11,7 @@ namespace ChurchReport.Tools  {      public class PushUtility      { -        #region ???身摰? +        #region ?豲??謘輯澈??          private LineMessagingClient m_LineMessagingClient { get; }          private readonly ILineNotificationWorkflow _lineNotificationWorkflow;          private readonly ILineRichMenuWorkflow _lineRichMenuWorkflow; @@ -45,7 +46,7 @@ namespace ChurchReport.Tools            private static ILineRichMenuWorkflow CreateDefaultRichMenuWorkflow(LineMessagingClient lineMessagingClient)          { -            return new LineRichMenuWorkflow(new LineMessagingProcessorClass(lineMessagingClient)); +            return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(lineMessagingClient)));          }            private async Task SendBestEffortSdkMessagesAsync( @@ -65,7 +66,7 @@ namespace ChurchReport.Tools          }          #endregion   -        #region Line Messagin Api Push SDK?喲? +        #region Line Messagin Api Push SDK??笨?          public async Task SendMessage(string UserId, List<ISendMessage> MessageToSend)          {              try @@ -397,7 +398,7 @@ namespace ChurchReport.Tools              try              {                  var richMenu = CreateLegacySingleButtonRichMenu(); -                var imagePath = @"D:\暫存區\richmenu.PNG"; +                var imagePath = @"D:\?怠??\richmenu.PNG";                  await _lineRichMenuWorkflow.CreateUploadAndLinkOrThrowAsync(new LineRichMenuCreateUploadAndLinkRequest                  {                          UserId = UserId, @@ -421,7 +422,7 @@ namespace ChurchReport.Tools                      messageToSend,                      "ChurchReport.PushUtility.AddRichMenuMessage");   -                return "成功"; +                return "??";              }              catch (System.Exception e)              { @@ -443,7 +444,7 @@ namespace ChurchReport.Tools                          }                      });   -                return "成功"; +                return "??";              }              catch (System.Exception e)              { @@ -472,7 +473,7 @@ namespace ChurchReport.Tools          }          #endregion   -        #region 撌亙? +        #region ?????          private string GetFileExtension(string mediaType)          {              switch (mediaType) @@ -488,14 +489,14 @@ namespace ChurchReport.Tools              }          }          #endregion -        #region 蝺渡?? +        #region ?箸腹???            public void ConfirmMessage(string UserId)          { -            ISendMessage replyMessage = new TemplateMessage("確認按鈕", -                    new ConfirmTemplate("耶和華", new List<ITemplateAction> { -                        new MessageTemplateAction("同意", "火熱跟隨"), -                        new MessageTemplateAction("反對", "我愛耶和華") +            ISendMessage replyMessage = new TemplateMessage("確認訊息", +                    new ConfirmTemplate("請確認是否執行此動作", new List<ITemplateAction> { +                        new MessageTemplateAction("是", "確認"), +                        new MessageTemplateAction("否", "取消")                      }));                List<ISendMessage> actions1 = new List<ISendMessage> @@ -584,19 +585,19 @@ namespace ChurchReport.Tools          {              List<ITemplateAction> actions1 = new List<ITemplateAction>();              // Add actions. -            actions1.Add(new MessageTemplateAction("報名", "簡如牧師邀請您")); +            actions1.Add(new MessageTemplateAction("?勗?", "蝪∪??批葦?隢"));              //actions1.Add(new PostbackTemplateAction("Postback Label", "sample data", "sample data")); -            actions1.Add(new UriTemplateAction("說明網頁", "https://www.blccym.org/single-post/2018/05/16/2018520-%E4%B8%BB%E6%97%A5")); +            actions1.Add(new UriTemplateAction("隤芣?蝬脤?", "https://www.blccym.org/single-post/2018/05/16/2018520-%E4%B8%BB%E6%97%A5"));                List<ITemplateAction> actions2 = new List<ITemplateAction>(); -            actions2.Add(new MessageTemplateAction("報名", "簡如牧師邀請您")); +            actions2.Add(new MessageTemplateAction("?勗?", "蝪∪??批葦?隢"));              //actions1.Add(new PostbackTemplateAction("Postback Label", "sample data", "sample data")); -            actions2.Add(new UriTemplateAction("說明網頁", "https://www.blccym.org/single-post/2018/05/09/2018512%E9%9D%92%E5%B4%87")); +            actions2.Add(new UriTemplateAction("隤芣?蝬脤?", "https://www.blccym.org/single-post/2018/05/09/2018512%E9%9D%92%E5%B4%87"));                List<ITemplateAction> actions3 = new List<ITemplateAction>(); -            actions3.Add(new MessageTemplateAction("報名", "簡如牧師邀請您")); +            actions3.Add(new MessageTemplateAction("?勗?", "蝪∪??批葦?隢"));              //actions1.Add(new PostbackTemplateAction("Postback Label", "sample data", "sample data")); -            actions3.Add(new UriTemplateAction("說明網頁", "https://www.blccym.org/single-post/2018/05/16/2018520-%E4%B8%BB%E6%97%A5")); +            actions3.Add(new UriTemplateAction("隤芣?蝬脤?", "https://www.blccym.org/single-post/2018/05/16/2018520-%E4%B8%BB%E6%97%A5"));                // Add datetime picker actions              //actions2.Add(new DateTimePickerTemplateAction("DateTime Picker", "DateTime", @@ -611,14 +612,14 @@ namespace ChurchReport.Tools              ISendMessage replyMessage = new TemplateMessage("Button Template",                  new CarouselTemplate(new List<CarouselColumn>                  { -                        new CarouselColumn("講員：魏外楊老師", "https://od.lk/s/ODdfOTA4MTYyMV8/%E9%AD%8F%E5%A4%96%E6%A5%8A%E8%80%81%E5%B8%AB.jpg", -                        "主題：從頭一天直到如今", actions1), +                        new CarouselColumn("雓嚗?憭??葦", "https://od.lk/s/ODdfOTA4MTYyMV8/%E9%AD%8F%E5%A4%96%E6%A5%8A%E8%80%81%E5%B8%AB.jpg", +                        "主日信息與聚會內容", actions1),   -                        new CarouselColumn("講員：湯簡如牧師", "https://od.lk/s/ODdfOTA4MTYyMl8/%E7%B0%A1%E5%A6%82%E7%89%A7%E5%B8%AB.jpg", -                        "你的品格力系列-不可論斷1", actions1), +                        new CarouselColumn("雓嚗僖蝪∪??批葦", "https://od.lk/s/ODdfOTA4MTYyMl8/%E7%B0%A1%E5%A6%82%E7%89%A7%E5%B8%AB.jpg", +                        "雿???頂??銝隢1", actions1),   -                        new CarouselColumn("時間：每週二至週五，早上7：40～9：20", "https://od.lk/s/ODdfOTA4MTYyM18/%E6%99%A8%E7%A6%B1.jpg", -                        "晨禱", actions3), +                        new CarouselColumn("??嚗??曹??喲曹?嚗銝?嚗?0嚚?嚗?0", "https://od.lk/s/ODdfOTA4MTYyM18/%E6%99%A8%E7%A6%B1.jpg", +                        "?函曲", actions3),                              //new CarouselColumn("Casousel 7 Text", "https://github.com/apple-touch-icon.png", @@ -655,3 +656,4 @@ namespace ChurchReport.Tools      }  }   + diff --git a/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj b/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj index 6c76600f..1fa82a5c 100644 --- a/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj +++ b/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessor.AspNetCore.Tests.csproj @@ -21,5 +21,6 @@      <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />      <ProjectReference Include="..\LineMessagingProcessor.AspNetCore\LineMessagingProcessor.AspNetCore.csproj" />      <ProjectReference Include="..\LineMessagingProcessor.Workflows\LineMessagingProcessor.Workflows.csproj" /> +    <ProjectReference Include="..\LineMessagingProcessor.RichMenus\LineMessagingProcessor.RichMenus.csproj" />    </ItemGroup> -</Project> +</Project> \ No newline at end of file diff --git a/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs b/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs index e0502e17..53308c38 100644 --- a/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs +++ b/LineMessagingProcessor.AspNetCore.Tests/LineMessagingProcessorServiceCollectionExtensionsTests.cs @@ -1,5 +1,6 @@  using FluentAssertions;  using Line.Messaging; +using LineMessagingProcessor.RichMenus;  using LineMessagingProcessor.Workflows;  using Microsoft.Extensions.DependencyInjection;  using Xunit; @@ -24,6 +25,9 @@ public sealed class LineMessagingProcessorServiceCollectionExtensionsTests          provider.GetRequiredService<LineMessagingClient>().Should().NotBeNull();          provider.GetRequiredService<LineMessagingProcessor.LineMessagingProcessorClass>().Should().NotBeNull();          provider.GetRequiredService<ILineNotificationWorkflow>().Should().BeOfType<LineNotificationWorkflow>(); +        provider.GetRequiredService<ILineRichMenuWorkflow>().Should().BeOfType<LineRichMenuWorkflow>(); +        provider.GetRequiredService<ILineRichMenuAssignmentWorkflow>().Should().BeOfType<LineRichMenuAssignmentWorkflow>(); +        provider.GetRequiredService<ILineRichMenuTextTriggerResolver>().Should().BeOfType<LineRichMenuTextTriggerResolver>();      }        [Fact] @@ -44,5 +48,34 @@ public sealed class LineMessagingProcessorServiceCollectionExtensionsTests          });            provider.GetRequiredService<ILineNotificationWorkflow>().Should().NotBeNull(); +        provider.GetRequiredService<ILineRichMenuWorkflow>().Should().NotBeNull(); +    } + +    [Fact] +    public void AddLineRichMenuProvisioning_registers_product_catalog_and_provisioning_workflow() +    { +        var services = new ServiceCollection(); + +        services.AddLineMessagingProcessor(options => +        { +            options.ChannelAccessToken = "test-token"; +            options.ApiBaseUri = "https://api.line.me/v2"; +        }); +        services.AddLineRichMenuProvisioning<FakeRichMenuCatalog>(); + +        using var provider = services.BuildServiceProvider(new ServiceProviderOptions +        { +            ValidateOnBuild = true, +            ValidateScopes = true +        }); + +        provider.GetRequiredService<ILineRichMenuCatalog>().Should().BeOfType<FakeRichMenuCatalog>(); +        provider.GetRequiredService<ILineRichMenuProvisioningWorkflow>().Should().BeOfType<LineRichMenuProvisioningWorkflow>(); +    } + +    private sealed class FakeRichMenuCatalog : ILineRichMenuCatalog +    { +        public Task<IReadOnlyList<LineRichMenuDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default) +            => Task.FromResult<IReadOnlyList<LineRichMenuDefinition>>(Array.Empty<LineRichMenuDefinition>());      }  } diff --git a/LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj b/LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj index 697146c6..fd4ef6e7 100644 --- a/LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj +++ b/LineMessagingProcessor.AspNetCore/LineMessagingProcessor.AspNetCore.csproj @@ -14,5 +14,6 @@      <ProjectReference Include="..\Line.Messaging\Line.Messaging.csproj" />      <ProjectReference Include="..\LineMessagingProcessor\LineMessagingProcessor.csproj" />      <ProjectReference Include="..\LineMessagingProcessor.Workflows\LineMessagingProcessor.Workflows.csproj" /> +    <ProjectReference Include="..\LineMessagingProcessor.RichMenus\LineMessagingProcessor.RichMenus.csproj" />    </ItemGroup> -</Project> +</Project> \ No newline at end of file diff --git a/LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs b/LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs index a4362393..706e07a1 100644 --- a/LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs +++ b/LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs @@ -1,13 +1,17 @@  using Line.Messaging;  using LineMessagingProcessor; +using LineMessagingProcessor.RichMenus;  using LineMessagingProcessor.Workflows;  using Microsoft.Extensions.DependencyInjection; +using Microsoft.Extensions.DependencyInjection.Extensions;  using Microsoft.Extensions.Options;    namespace LineMessagingProcessor.AspNetCore;    /// <summary> -/// ASP.NET Core DI 註冊入口。未來產品只需引用此專案並設定 token，即可取得 processor 與共用 workflow。 +/// ASP.NET Core DI registration for the shared LINE processor family. +/// The registration is intentionally split so future products can choose the shared RichMenu core +/// without being forced to provide product-specific catalog/policy services at the same time.  /// </summary>  public static class LineMessagingProcessorServiceCollectionExtensions  { @@ -40,8 +44,63 @@ public static class LineMessagingProcessorServiceCollectionExtensions              new LineMessagingProcessorClass(sp.GetRequiredService<LineMessagingClient>()));          services.AddTransient<ILineNotificationWorkflow, LineNotificationWorkflow>();          services.AddTransient<ILineReplyWorkflow, LineReplyWorkflow>(); -        services.AddTransient<ILineRichMenuWorkflow, LineRichMenuWorkflow>(); +        services.AddLineRichMenus(); + +        return services; +    } + +    /// <summary> +    /// Registers product-neutral RichMenu services. +    /// This method does not register <see cref="ILineRichMenuCatalog"/> because catalog content is product-specific. +    /// A future ASP.NET Core product can call this after registering LineMessagingProcessorClass, then add its own +    /// catalog, policies, and persistent state store. +    /// </summary> +    public static IServiceCollection AddLineRichMenus( +        this IServiceCollection services, +        Action<LineRichMenuTextTriggerOptions>? configureTextTriggers = null) +    { +        if (services == null) +        { +            throw new ArgumentNullException(nameof(services)); +        } + +        var textTriggerOptions = new LineRichMenuTextTriggerOptions(); +        configureTextTriggers?.Invoke(textTriggerOptions); + +        services.TryAddSingleton(textTriggerOptions); +        services.TryAddSingleton<ILineRichMenuIdCache, InMemoryLineRichMenuIdCache>(); +        services.TryAddSingleton<IRichMenuStateStore, InMemoryRichMenuStateStore>(); +        services.TryAddTransient<ILineRichMenuProcessor, LineMessagingProcessorRichMenuAdapter>(); +        services.TryAddTransient<ILineRichMenuWorkflow, LineRichMenuWorkflow>(); +        services.TryAddTransient<ILineRichMenuAssignmentWorkflow, LineRichMenuAssignmentWorkflow>(); +        services.TryAddTransient<ILineRichMenuTextTriggerResolver, LineRichMenuTextTriggerResolver>(); +        services.TryAddTransient<IRichMenuOrchestrator>(sp => +            new RichMenuOrchestrator( +                sp.GetServices<IRichMenuPolicy>(), +                sp.GetRequiredService<ILineRichMenuAssignmentWorkflow>())); +        services.TryAddTransient<IRichMenuExpirationSweepWorkflow, RichMenuExpirationSweepWorkflow>(); + +        return services; +    } + +    /// <summary> +    /// Registers RichMenu provisioning with a product-owned catalog. +    /// Keeping this separate from AddLineRichMenus prevents the shared core from forcing every application +    /// to define menus before it can use assignment, text trigger, or workflow services. +    /// </summary> +    public static IServiceCollection AddLineRichMenuProvisioning<TCatalog>(this IServiceCollection services) +        where TCatalog : class, ILineRichMenuCatalog +    { +        if (services == null) +        { +            throw new ArgumentNullException(nameof(services)); +        } + +        services.AddLineRichMenus(); +        services.TryAddTransient<ILineRichMenuCatalog, TCatalog>(); +        services.TryAddTransient<ILineRichMenuProvisioningWorkflow, LineRichMenuProvisioningWorkflow>();            return services;      }  } + diff --git a/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs b/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs index 0d04fd5d..287ce8b0 100644 --- a/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs +++ b/LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs @@ -3,10 +3,10 @@ using System.Text;  using FluentAssertions;  using Line.Messaging;  using LineMessagingProcessor; -using LineMessagingProcessor.Workflows; +using LineMessagingProcessor.RichMenus;  using Xunit;   -namespace LineMessagingProcessor.Workflows.Tests; +namespace LineMessagingProcessor.RichMenus.Tests;    public sealed class LineRichMenuWorkflowTests  { @@ -77,7 +77,7 @@ public sealed class LineRichMenuWorkflowTests          });            result.Succeeded.Should().BeFalse(); -        result.Status.Should().Be(LineNotificationStatus.ValidationFailed); +        result.Status.Should().Be(LineRichMenuStatus.ValidationFailed);          result.ErrorCode.Should().Be("line-richmenu-user-required");          handler.Requests.Should().BeEmpty();      } @@ -97,7 +97,7 @@ public sealed class LineRichMenuWorkflowTests          });            var exception = await action.Should().ThrowAsync<LineRichMenuException>(); -        exception.Which.Result.Status.Should().Be(LineNotificationStatus.ProviderRejected); +        exception.Which.Result.Status.Should().Be(LineRichMenuStatus.ProviderRejected);          exception.Which.Result.ErrorCode.Should().Be("line-richmenu-provider-rejected");          exception.Which.Result.ErrorMessage.Should().Be("invalid rich menu");      } @@ -105,7 +105,7 @@ public sealed class LineRichMenuWorkflowTests      private static LineRichMenuWorkflow CreateWorkflow(SequencedHttpMessageHandler handler)      {          var sdkClient = new LineMessagingClient(new HttpClient(handler), "test-token", "https://api.line.me/v2"); -        return new LineRichMenuWorkflow(new LineMessagingProcessorClass(sdkClient)); +        return new LineRichMenuWorkflow(new LineMessagingProcessorRichMenuAdapter(new LineMessagingProcessorClass(sdkClient)));      }        private static RichMenu CreateRichMenu() @@ -156,3 +156,5 @@ public sealed class LineRichMenuWorkflowTests          }      }  } + + diff --git a/LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs b/LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs index 002e1df5..e0c9326b 100644 --- a/LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs +++ b/LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs @@ -1,12 +1,10 @@  using Line.Messaging;   -namespace LineMessagingProcessor.Workflows; +namespace LineMessagingProcessor.RichMenus;    /// <summary> -/// 共用 RichMenu 工作流介面。 -/// 這一層只描述 LINE RichMenu 的可重用操作，不描述任何產品身分、CRM、付款或頁面流程。 -/// 未來產品若要依身分、輸入文字、會員狀態切換 RichMenu，應該在產品端先決定要套用哪個 RichMenu， -/// 再呼叫這個 workflow 執行 LINE API 編排。 +/// RichMenu 建立、上傳、連結與解除連結的共用流程介面。 +/// 呼叫端只需要提供標準請求；實作會統一處理 LINE API 呼叫、錯誤轉換與結果包裝。  /// </summary>  public interface ILineRichMenuWorkflow  { @@ -18,3 +16,4 @@ public interface ILineRichMenuWorkflow        Task DeleteLinkedRichMenuOrThrowAsync(LineRichMenuDeleteLinkedRequest request);  } + diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs b/LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs index 33a3e6ff..4ae7f9ef 100644 --- a/LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs +++ b/LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs @@ -1,10 +1,10 @@  using Line.Messaging;   -namespace LineMessagingProcessor.Workflows; +namespace LineMessagingProcessor.RichMenus;    /// <summary> -/// 建立 RichMenu、上傳 PNG 圖片並綁定到單一 LINE 使用者的共用請求。 -/// RichMenu 的版面與圖片內容由產品端提供；workflow 只負責照 LINE 官方 API 順序執行。 +/// 建立 RichMenu、上傳 PNG 並連結到 LINE 使用者的標準請求。 +/// RichMenu 版面與圖片來源由呼叫端提供，workflow 僅負責穩定串接 LINE RichMenu API。  /// </summary>  public sealed class LineRichMenuCreateUploadAndLinkRequest  { @@ -16,3 +16,4 @@ public sealed class LineRichMenuCreateUploadAndLinkRequest        public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();  } + diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs b/LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs index 37719e95..d9d3b860 100644 --- a/LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs +++ b/LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs @@ -1,9 +1,8 @@ -namespace LineMessagingProcessor.Workflows; +namespace LineMessagingProcessor.RichMenus;    /// <summary> -/// 解除使用者目前 RichMenu 並刪除該 RichMenu 的共用請求。 -/// 這保留舊 ChurchReport 行為：先查使用者目前綁定的 RichMenu ID，再 unlink，最後 delete。 -/// 若未來產品需要「只解除不刪除」或「刪除共用模板」這類不同策略，應新增明確方法，不在這裡塞旗標。 +/// 刪除已連結 RichMenu 的標準請求。 +/// 呼叫端只提供 LINE user id 與必要追蹤資料；解除連結與刪除遠端選單由共用 workflow 負責。  /// </summary>  public sealed class LineRichMenuDeleteLinkedRequest  { @@ -11,3 +10,4 @@ public sealed class LineRichMenuDeleteLinkedRequest        public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();  } + diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuException.cs b/LineMessagingProcessor.RichMenus/LineRichMenuException.cs index b1317c54..02083b3b 100644 --- a/LineMessagingProcessor.RichMenus/LineRichMenuException.cs +++ b/LineMessagingProcessor.RichMenus/LineRichMenuException.cs @@ -1,8 +1,8 @@ -namespace LineMessagingProcessor.Workflows; +namespace LineMessagingProcessor.RichMenus;    /// <summary> -/// RichMenu 必達流程失敗時使用的標準例外。 -/// 呼叫端可從 Result 取得 provider 回覆、驗證錯誤或原始例外，不需要解析字串。 +/// RichMenu workflow 失敗時拋出的例外。 +/// 例外會保留標準化結果，讓上層可以讀取狀態碼、錯誤代碼與 provider 診斷資訊。  /// </summary>  public sealed class LineRichMenuException : Exception  { @@ -14,3 +14,4 @@ public sealed class LineRichMenuException : Exception        public LineRichMenuResult Result { get; }  } + diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuResult.cs b/LineMessagingProcessor.RichMenus/LineRichMenuResult.cs index 5b781400..3d404201 100644 --- a/LineMessagingProcessor.RichMenus/LineRichMenuResult.cs +++ b/LineMessagingProcessor.RichMenus/LineRichMenuResult.cs @@ -1,14 +1,14 @@ -namespace LineMessagingProcessor.Workflows; +namespace LineMessagingProcessor.RichMenus;    /// <summary> -/// RichMenu 共用 workflow 的標準結果。 -/// SendAsync 類流程回傳結果而非直接吞掉例外，讓產品端可以依重要性決定是否中斷流程。 +/// RichMenu workflow 的標準化執行結果。 +/// 透過固定欄位表達成功、驗證失敗、LINE 拒絕、服務不可用或未預期錯誤，避免各產品自行解析例外。  /// </summary>  public sealed class LineRichMenuResult  {      private LineRichMenuResult(          bool succeeded, -        LineNotificationStatus status, +        LineRichMenuStatus status,          string? userId,          string? richMenuId,          string? errorCode, @@ -28,7 +28,7 @@ public sealed class LineRichMenuResult        public bool Succeeded { get; }   -    public LineNotificationStatus Status { get; } +    public LineRichMenuStatus Status { get; }        public string? UserId { get; }   @@ -43,15 +43,16 @@ public sealed class LineRichMenuResult      public IReadOnlyDictionary<string, string> Metadata { get; }        public static LineRichMenuResult Success(string userId, string? richMenuId, IReadOnlyDictionary<string, string> metadata) -        => new(true, LineNotificationStatus.Succeeded, userId, richMenuId, null, null, null, metadata); +        => new(true, LineRichMenuStatus.Succeeded, userId, richMenuId, null, null, null, metadata);        public static LineRichMenuResult Failure(          string? userId,          string? richMenuId, -        LineNotificationStatus status, +        LineRichMenuStatus status,          string errorCode,          string errorMessage,          Exception? exception,          IReadOnlyDictionary<string, string>? metadata)          => new(false, status, userId, richMenuId, errorCode, errorMessage, exception, metadata ?? new Dictionary<string, string>());  } + diff --git a/LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs b/LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs index b8064626..d9316b9b 100644 --- a/LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs +++ b/LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs @@ -1,18 +1,17 @@  using Line.Messaging; -using LineMessagingProcessor;   -namespace LineMessagingProcessor.Workflows; +namespace LineMessagingProcessor.RichMenus;    /// <summary> -/// 共用 RichMenu 工作流。 -/// 它只負責 LINE API 的固定編排：驗證請求、建立 RichMenu、上傳 PNG、綁定使用者、解除並刪除。 -/// 產品端的「誰應該看到哪個 RichMenu」不屬於這裡，應由 ChurchReport 或未來產品自己的 policy 決定。 +/// RichMenu API 的共用 workflow。 +/// 此型別只負責建立 RichMenu、上傳 PNG、連結使用者、解除連結與刪除遠端選單； +/// 選單規則、使用者分群、畫面流程與產品 policy 都留在呼叫端或更上層 orchestrator。  /// </summary>  public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow  { -    private readonly LineMessagingProcessorClass _processor; +    private readonly ILineRichMenuProcessor _processor;   -    public LineRichMenuWorkflow(LineMessagingProcessorClass processor) +    public LineRichMenuWorkflow(ILineRichMenuProcessor processor)      {          _processor = processor ?? throw new ArgumentNullException(nameof(processor));      } @@ -37,7 +36,7 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow                  return LineRichMenuResult.Failure(                      request.UserId,                      richMenuId, -                    LineNotificationStatus.ValidationFailed, +                    LineRichMenuStatus.ValidationFailed,                      "line-richmenu-image-stream-required",                      "RichMenu PNG image stream is required.",                      null, @@ -54,7 +53,7 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow              return LineRichMenuResult.Failure(                  request.UserId,                  richMenuId, -                LineNotificationStatus.ProviderRejected, +                LineRichMenuStatus.ProviderRejected,                  "line-richmenu-provider-rejected",                  ex.Message,                  ex, @@ -65,7 +64,7 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow              return LineRichMenuResult.Failure(                  request.UserId,                  richMenuId, -                LineNotificationStatus.ProviderUnavailable, +                LineRichMenuStatus.ProviderUnavailable,                  "line-richmenu-provider-unavailable",                  ex.Message,                  ex, @@ -76,7 +75,7 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow              return LineRichMenuResult.Failure(                  request.UserId,                  richMenuId, -                LineNotificationStatus.ProviderUnavailable, +                LineRichMenuStatus.ProviderUnavailable,                  "line-richmenu-provider-timeout",                  ex.Message,                  ex, @@ -87,7 +86,7 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow              return LineRichMenuResult.Failure(                  request.UserId,                  richMenuId, -                LineNotificationStatus.UnexpectedError, +                LineRichMenuStatus.UnexpectedError,                  "line-richmenu-unexpected-error",                  ex.Message,                  ex, @@ -131,7 +130,7 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow              return LineRichMenuResult.Failure(                  request.UserId,                  richMenuId, -                LineNotificationStatus.ProviderRejected, +                LineRichMenuStatus.ProviderRejected,                  "line-richmenu-provider-rejected",                  ex.Message,                  ex, @@ -142,7 +141,7 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow              return LineRichMenuResult.Failure(                  request.UserId,                  richMenuId, -                LineNotificationStatus.ProviderUnavailable, +                LineRichMenuStatus.ProviderUnavailable,                  "line-richmenu-provider-unavailable",                  ex.Message,                  ex, @@ -153,7 +152,7 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow              return LineRichMenuResult.Failure(                  request.UserId,                  richMenuId, -                LineNotificationStatus.ProviderUnavailable, +                LineRichMenuStatus.ProviderUnavailable,                  "line-richmenu-provider-timeout",                  ex.Message,                  ex, @@ -164,7 +163,7 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow              return LineRichMenuResult.Failure(                  request.UserId,                  richMenuId, -                LineNotificationStatus.UnexpectedError, +                LineRichMenuStatus.UnexpectedError,                  "line-richmenu-unexpected-error",                  ex.Message,                  ex, @@ -185,22 +184,22 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow      {          if (request == null)          { -            return LineRichMenuResult.Failure(null, null, LineNotificationStatus.ValidationFailed, "line-richmenu-request-required", "RichMenu request is required.", null, null); +            return LineRichMenuResult.Failure(null, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-request-required", "RichMenu request is required.", null, null);          }            if (string.IsNullOrWhiteSpace(request.UserId))          { -            return LineRichMenuResult.Failure(request.UserId, null, LineNotificationStatus.ValidationFailed, "line-richmenu-user-required", "LINE user id is required.", null, request.Metadata); +            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-user-required", "LINE user id is required.", null, request.Metadata);          }            if (request.RichMenu == null)          { -            return LineRichMenuResult.Failure(request.UserId, null, LineNotificationStatus.ValidationFailed, "line-richmenu-definition-required", "RichMenu definition is required.", null, request.Metadata); +            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-definition-required", "RichMenu definition is required.", null, request.Metadata);          }            if (request.PngImageStreamFactory == null)          { -            return LineRichMenuResult.Failure(request.UserId, null, LineNotificationStatus.ValidationFailed, "line-richmenu-image-factory-required", "RichMenu PNG image stream factory is required.", null, request.Metadata); +            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-image-factory-required", "RichMenu PNG image stream factory is required.", null, request.Metadata);          }            return null; @@ -210,14 +209,16 @@ public sealed class LineRichMenuWorkflow : ILineRichMenuWorkflow      {          if (request == null)          { -            return LineRichMenuResult.Failure(null, null, LineNotificationStatus.ValidationFailed, "line-richmenu-request-required", "RichMenu request is required.", null, null); +            return LineRichMenuResult.Failure(null, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-request-required", "RichMenu request is required.", null, null);          }            if (string.IsNullOrWhiteSpace(request.UserId))          { -            return LineRichMenuResult.Failure(request.UserId, null, LineNotificationStatus.ValidationFailed, "line-richmenu-user-required", "LINE user id is required.", null, request.Metadata); +            return LineRichMenuResult.Failure(request.UserId, null, LineRichMenuStatus.ValidationFailed, "line-richmenu-user-required", "LINE user id is required.", null, request.Metadata);          }            return null;      }  } + + diff --git a/LineMessagingProcessor/LineMessagingProcessorClass.cs b/LineMessagingProcessor/LineMessagingProcessorClass.cs index ea0a7397..1cd0c84a 100644 --- a/LineMessagingProcessor/LineMessagingProcessorClass.cs +++ b/LineMessagingProcessor/LineMessagingProcessorClass.cs @@ -13,8 +13,8 @@ namespace LineMessagingProcessor  {      public class LineMessagingProcessorClass : IDisposable      { -        // LINE channel access token 是部署環境的機密資料，不能寫死在原始碼。 -        // 建構式會統一正規化成 Authorization header 需要的 Bearer 格式。 +        // LINE channel access token ?舫蝵脩憓?璈?鞈?嚗??賢神甇餃??蝣潦? +        // 撱箸?撘?蝯曹?甇???? Authorization header ?閬? Bearer ?澆???          private readonly string _channelAccessToken;          private readonly LineMessagingClient _lineMessagingClient;          private readonly bool _requiresChannelAccessToken; @@ -33,7 +33,7 @@ namespace LineMessagingProcessor          {              _channelAccessToken = NormalizeBearerToken(channelAccessToken);              _requiresChannelAccessToken = true; -#pragma warning disable CS0618 // 保留既有 token 建構流程；新的測試/DI 路徑可直接注入 LineMessagingClient。 +#pragma warning disable CS0618 // 靽??Ｘ? token 撱箸?瘚?嚗?葫閰?DI 頝臬??舐?交釣??LineMessagingClient??              _lineMessagingClient = new LineMessagingClient(StripBearerPrefix(_channelAccessToken));  #pragma warning restore CS0618          } @@ -115,7 +115,7 @@ namespace LineMessagingProcessor              return _channelAccessToken;          }   -        #region 釋放記憶體 +        #region ?閮擃?          private bool _disposed = false;            protected virtual void Dispose(bool disposing) @@ -164,7 +164,7 @@ namespace LineMessagingProcessor                    m_UserId = aEvent["source"]["userId"];   -                await SendMessage(m_UserId, "歡迎加入好牧人"); +                await SendMessage(m_UserId, "歡迎加入。");                    #endregion              } @@ -173,7 +173,7 @@ namespace LineMessagingProcessor                  #region unfollow                    m_UserId = aEvent["source"]["userId"]; -                await SendMessage(m_UserId, "期待您隨時回來好牧人粉絲團"); +                await SendMessage(m_UserId, "您已取消追蹤。");                    #endregion              } @@ -189,9 +189,9 @@ namespace LineMessagingProcessor                  String LetterEntityId = "";                  ParsePostBackString(Data, ref MessageType, ref Selection, ref LetterEntityId);   -                if (MessageType == "模板" || MessageType == "確認") +                if (MessageType == "璅⊥" || MessageType == "蝣箄?")                  { -                    await SendMessage(UserId, "您選擇了 : " + Selection + Environment.NewLine + "正在處理中...."); +                    await SendMessage(UserId, "?券?? : " + Selection + Environment.NewLine + "甇???銝?...");                  }                  #endregion              } @@ -251,10 +251,10 @@ namespace LineMessagingProcessor                  throw new ArgumentException("Message is required.", nameof(Message));              }   -            // 舊版 ChurchReport 流程曾用這個特殊字串要求系統回傳 LINE 使用者 ID。 -            // 這不是 LINE 官方 Messaging API 的協定；此處只保留既有文字轉換， -            // 實際 HTTP endpoint、Authorization header 與 JSON 序列化全部交給 Line.Messaging SDK。 -            if (Message == "顯示認證") +            // ?? ChurchReport 瘚??曄?畾?銝脰?瘙頂蝯勗???LINE 雿輻??ID?? +            // ????LINE 摰 Messaging API ??摰?甇方??芯????摮??? +            // 撖阡? HTTP endpoint?uthorization header ??JSON 摨???其漱蝯?Line.Messaging SDK?? +            if (Message == "憿舐內隤?" || Message == "顯示認證")              {                  Message = "認證:" + UserId;              } @@ -269,14 +269,14 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 發送可重試的 LINE 推播訊息。 -        /// 此方法只負責「可重用的 LINE 推播入口」：檢查必要欄位、建立文字訊息， -        /// 然後把呼叫交給 Line.Messaging SDK。真正的 X-Line-Retry-Key header -        /// 仍由 SDK 統一處理，避免 Processor 與 SDK 各自實作一份 LINE 協定細節。 +        /// ?潮?岫??LINE ?冽閮?? +        /// 甇斗瘜鞎痊????LINE ?冽?亙??瑼Ｘ敹?甈??遣蝡?摮??荔? +        /// ?嗅???思漱蝯?Line.Messaging SDK??甇?? X-Line-Retry-Key header +        /// 隞 SDK 蝯曹???嚗??Processor ??SDK ?撖虫?銝隞?LINE ??蝝啁???          /// </summary> -        /// <param name="UserId">LINE 使用者 ID、群組 ID 或聊天室 ID。</param> -        /// <param name="Message">要推播給付款者的純文字訊息。</param> -        /// <param name="retryKey">由產品端產生的冪等重試鍵；空白時沿用非重試行為。</param> +        /// <param name="UserId">LINE 雿輻??ID?黎蝯?ID ??憭拙恕 ID??/param> +        /// <param name="Message">閬?剔策隞狡??蝝?摮??胯?/param> +        /// <param name="retryKey">?梁?垢?Ｙ??蝑?閰阡嚗征?賣?瘝輻??閰西??箝?/param>          public async Task SendReliableMessageAsync(string UserId, string Message, string? retryKey)          {              if (string.IsNullOrWhiteSpace(UserId)) @@ -294,13 +294,13 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 共用 workflow 使用的低階發送入口。 -        /// 這個方法只接受 LINE user id 與已組好的 SDK 訊息，避免 workflow 反射讀取 private client， -        /// 也讓未來產品可以重用同一條 SDK-backed push 路徑。 +        /// ?梁 workflow 雿輻??????? +        /// ?瘜?亙? LINE user id ?歇蝯末??SDK 閮嚗??workflow ??霈??private client嚗? +        /// 銋??芯??Ｗ??臭誑???璇?SDK-backed push 頝臬???          /// </summary> -        /// <param name="userId">LINE user id。空白時在進入 HTTP 前即拒絕。</param> -        /// <param name="messages">要送出的 LINE SDK 訊息集合。空集合代表呼叫端沒有建立有效內容。</param> -        /// <param name="retryKey">LINE retry key；可為 null，保留一般 push 行為。</param> +        /// <param name="userId">LINE user id?征?賣??券脣 HTTP ?????/param> +        /// <param name="messages">閬??LINE SDK 閮???征??隞?”?澆蝡舀??遣蝡??摰嫘?/param> +        /// <param name="retryKey">LINE retry key嚗??null嚗?????push 銵??/param>          public async Task SendMessagesAsync(string userId, IList<ISendMessage> messages, string? retryKey = null)          {              if (string.IsNullOrWhiteSpace(userId)) @@ -317,12 +317,12 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 透過 LINE reply token 回覆 webhook 事件。 -        /// 這是共用 reply workflow 的最底層 adapter：只包住 SDK 呼叫與基本參數驗證， -        /// 不放 ChurchReport 的回覆文字、CRM 判斷或控制器流程，避免共用 LINE 專案反向依賴產品。 +        /// ?? LINE reply token ?? webhook 鈭辣?? +        /// ??梁 reply workflow ??摨惜 adapter嚗?? SDK ?澆??砍??賊?霅? +        /// 銝 ChurchReport ??閬?摮RM ?斗??嗅瘚?嚗???LINE 撠???靘陷?Ｗ???          /// </summary> -        /// <param name="replyToken">LINE webhook 事件提供的一次性 reply token。</param> -        /// <param name="messages">要回覆給 LINE 使用者的 SDK message 清單。</param> +        /// <param name="replyToken">LINE webhook 鈭辣????甈⊥?reply token??/param> +        /// <param name="messages">閬?閬策 LINE 雿輻?? SDK message 皜??/param>          public async Task ReplyMessagesAsync(string replyToken, IList<ISendMessage> messages)          {              if (string.IsNullOrWhiteSpace(replyToken)) @@ -339,8 +339,8 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 建立 LINE RichMenu 並回傳 LINE 產生的 richMenuId。 -        /// Processor 只包住 SDK 與必要參數驗證；RichMenu 版面、圖片與產品套用規則由產品端或 workflow 決定。 +        /// 撱箇? LINE RichMenu 銝血???LINE ?Ｙ???richMenuId?? +        /// Processor ?芸?雿?SDK ??閬??賊?霅?RichMenu ??????Ｗ?憟閬??梁?垢??workflow 瘙箏???          /// </summary>          public async Task<string> CreateRichMenuAsync(RichMenu richMenu)          { @@ -353,8 +353,8 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 上傳 RichMenu PNG 圖片。 -        /// 圖片來源可能是產品專案檔案、Blob 或其他儲存體；Processor 不處理路徑，只接收已開啟的 stream。 +        /// 銝 RichMenu PNG ???? +        /// ??靘??航?舐??獢?獢lob ?隞摮?嚗rocessor 銝??楝敺??芣?嗅歇????stream??          /// </summary>          public async Task UploadRichMenuPngImageAsync(string richMenuId, Stream imageStream)          { @@ -372,7 +372,7 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 將 RichMenu 綁定到單一 LINE 使用者。 +        /// 撠?RichMenu 蝬??啣銝 LINE 雿輻??          /// </summary>          public async Task LinkRichMenuToUserAsync(string userId, string richMenuId)          { @@ -390,7 +390,7 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 查詢使用者目前綁定的 RichMenu ID。 +        /// ?亥岷雿輻???摰? RichMenu ID??          /// </summary>          public async Task<string> GetRichMenuIdOfUserAsync(string userId)          { @@ -403,7 +403,7 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 解除使用者目前綁定的 RichMenu。 +        /// 閫?雿輻???摰? RichMenu??          /// </summary>          public async Task UnlinkRichMenuFromUserAsync(string userId)          { @@ -416,7 +416,7 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 刪除指定 RichMenu。 +        /// ?芷?? RichMenu??          /// </summary>          public async Task DeleteRichMenuAsync(string richMenuId)          { @@ -428,16 +428,68 @@ namespace LineMessagingProcessor              await _lineMessagingClient.DeleteRichMenuAsync(richMenuId).ConfigureAwait(false);          }   + +        public async Task<IList<ResponseRichMenu>> GetRichMenuListAsync() +        { +            return await _lineMessagingClient.GetRichMenuListAsync().ConfigureAwait(false); +        } + +        public async Task SetDefaultRichMenuAsync(string richMenuId) +        { +            if (string.IsNullOrWhiteSpace(richMenuId)) throw new ArgumentException("richMenuId is required.", nameof(richMenuId)); +            await _lineMessagingClient.SetDefaultRichMenuAsync(richMenuId).ConfigureAwait(false); +        } + +        public async Task<string> GetDefaultRichMenuIdAsync() +        { +            return await _lineMessagingClient.GetDefaultRichMenuIdAsync().ConfigureAwait(false); +        } + +        public async Task CancelDefaultRichMenuAsync() +        { +            await _lineMessagingClient.CancelDefaultRichMenuAsync().ConfigureAwait(false); +        } + +        public async Task CreateRichMenuAliasAsync(string richMenuId, string richMenuAliasId) +        { +            if (string.IsNullOrWhiteSpace(richMenuId)) throw new ArgumentException("richMenuId is required.", nameof(richMenuId)); +            if (string.IsNullOrWhiteSpace(richMenuAliasId)) throw new ArgumentException("richMenuAliasId is required.", nameof(richMenuAliasId)); +            await _lineMessagingClient.CreateRichMenuAliasAsync(richMenuId, richMenuAliasId).ConfigureAwait(false); +        } + +        public async Task UpdateRichMenuAliasAsync(string richMenuAliasId, string richMenuId) +        { +            if (string.IsNullOrWhiteSpace(richMenuAliasId)) throw new ArgumentException("richMenuAliasId is required.", nameof(richMenuAliasId)); +            if (string.IsNullOrWhiteSpace(richMenuId)) throw new ArgumentException("richMenuId is required.", nameof(richMenuId)); +            await _lineMessagingClient.UpdateRichMenuAliasAsync(richMenuAliasId, richMenuId).ConfigureAwait(false); +        } + +        public async Task DeleteRichMenuAliasAsync(string richMenuAliasId) +        { +            if (string.IsNullOrWhiteSpace(richMenuAliasId)) throw new ArgumentException("richMenuAliasId is required.", nameof(richMenuAliasId)); +            await _lineMessagingClient.DeleteRichMenuAliasAsync(richMenuAliasId).ConfigureAwait(false); +        } + +        public async Task<RichMenuAlias> GetRichMenuAliasAsync(string richMenuAliasId) +        { +            if (string.IsNullOrWhiteSpace(richMenuAliasId)) throw new ArgumentException("richMenuAliasId is required.", nameof(richMenuAliasId)); +            return await _lineMessagingClient.GetRichMenuAliasAsync(richMenuAliasId).ConfigureAwait(false); +        } + +        public async Task<RichMenuAliasList> GetRichMenuAliasListAsync() +        { +            return await _lineMessagingClient.GetRichMenuAliasListAsync().ConfigureAwait(false); +        }          /// <summary> -        /// 以 SDK 取得 LINE 使用者個人資料。 -        /// 這一層只負責「可重用的 LINE 身分查詢」：先確認 UserId 有值，再交給 -        /// Line.Messaging SDK 呼叫官方 /bot/profile/{userId} API。 -        /// 特定產品的資料庫查詢、會員欄位綁定、登入流程與 LIFF 頁面都不放在這裡， -        /// 避免未來其他 ASP.NET Core 產品重用 LINE 模組時，被某一個產品的流程綁住。 +        /// 隞?SDK ?? LINE 雿輻?犖鞈??? +        /// ??撅文鞎痊????LINE 頨怠??亥岷???Ⅱ隤?UserId ?潘??漱蝯? +        /// Line.Messaging SDK ?澆摰 /bot/profile/{userId} API?? +        /// ?孵??Ｗ????澈?亥岷???⊥?雿?摰?交?蝔? LIFF ??賭??曉?ㄐ嚗? +        /// ?踹??芯??嗡? ASP.NET Core ?Ｗ?? LINE 璅∠???鋡急?銝???瘚?蝬???          /// </summary> -        /// <param name="UserId">LINE 使用者 ID。不可為 null、空字串或只包含空白。</param> -        /// <returns>LINE 官方回傳的使用者個人資料。</returns> -        /// <exception cref="ArgumentException">UserId 空白時拋出，且不發出 HTTP request。</exception> +        /// <param name="UserId">LINE 雿輻??ID???舐 null?征摮葡??蝛箇??/param> +        /// <returns>LINE 摰??蝙?刻犖鞈???/returns> +        /// <exception cref="ArgumentException">UserId 蝛箇???綽?銝??澆 HTTP request??/exception>          public async Task<Line.Messaging.UserProfile> GetUserProfileAsync(string UserId)          {              if (string.IsNullOrWhiteSpace(UserId)) @@ -449,12 +501,12 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 舊版同步命名的相容入口。 -        /// 保留這個方法是為了不一次破壞既有 ChurchReport 呼叫端；實際資料流已改走 -        /// GetUserProfileAsync，讓新舊入口共用同一份 SDK-backed 實作。 +        /// ???郊?賢??摰孵??? +        /// 靽??瘜?箔?銝?甈∠憯??ChurchReport ?澆蝡荔?撖阡?鞈?瘚歇?寡粥 +        /// GetUserProfileAsync嚗??啗??亙?梁??隞?SDK-backed 撖虫???          /// </summary> -        /// <param name="UserId">LINE 使用者 ID。</param> -        /// <returns>LINE 官方回傳的使用者個人資料。</returns> +        /// <param name="UserId">LINE 雿輻??ID??/param> +        /// <returns>LINE 摰??蝙?刻犖鞈???/returns>          public async Task<UserProfile> GetUserProfile(string UserId)          {              var profile = await GetUserProfileAsync(UserId).ConfigureAwait(false); @@ -469,15 +521,15 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 以 SDK 取得 LINE 群組中的成員個人資料。 -        /// 這個方法只負責共用 LINE 查詢入口需要的最小工作：驗證 groupId 與 userId， -        /// 然後把官方 API 呼叫交給 Line.Messaging SDK。群組成員是否要綁定到會員、 -        /// 小組、課程或任何產品資料，必須由呼叫端產品自己決定，不能放進共用 LINE 模組。 +        /// 隞?SDK ?? LINE 蝢斤?銝剔???犖鞈??? +        /// ?瘜鞎痊?梁 LINE ?亥岷?亙?閬??撠極雿?撽? groupId ??userId嚗? +        /// ?嗅?????API ?澆鈭斤策 Line.Messaging SDK?黎蝯??⊥?西?蝬??唳??～? +        /// 撠??玨蝔?隞颱??Ｗ?鞈?嚗???澆蝡舐?撌望捱摰?銝?暸脣??LINE 璅∠???          /// </summary> -        /// <param name="groupId">LINE 群組 ID。不可為 null、空字串或只包含空白。</param> -        /// <param name="userId">LINE 使用者 ID。不可為 null、空字串或只包含空白。</param> -        /// <returns>LINE 官方回傳的群組成員個人資料。</returns> -        /// <exception cref="ArgumentException">groupId 或 userId 空白時拋出，且不發出 HTTP request。</exception> +        /// <param name="groupId">LINE 蝢斤? ID???舐 null?征摮葡??蝛箇??/param> +        /// <param name="userId">LINE 雿輻??ID???舐 null?征摮葡??蝛箇??/param> +        /// <returns>LINE 摰??黎蝯??∪犖鞈???/returns> +        /// <exception cref="ArgumentException">groupId ??userId 蝛箇???綽?銝??澆 HTTP request??/exception>          public async Task<Line.Messaging.UserProfile> GetGroupMemberProfileAsync(string groupId, string userId)          {              if (string.IsNullOrWhiteSpace(groupId)) @@ -494,15 +546,15 @@ namespace LineMessagingProcessor          }            /// <summary> -        /// 以 SDK 取得 LINE 聊天室中的成員個人資料。 -        /// 這個方法與群組成員查詢維持同一個邊界：Processor 只驗證 roomId 與 userId， -        /// 實際 endpoint、HTTP header、JSON 解析都交給 Line.Messaging SDK 統一處理。 -        /// 產品端仍然負責判斷這個聊天室成員資料要如何對應到自己的會員或流程。 +        /// 隞?SDK ?? LINE ?予摰支葉???∪犖鞈??? +        /// ?瘜?蝢斤???亥岷蝬剜???????Processor ?芷?霅?roomId ??userId嚗? +        /// 撖阡? endpoint?TTP header?SON 閫???賭漱蝯?Line.Messaging SDK 蝯曹????? +        /// ?Ｗ?蝡臭??嗉?鞎砍?琿?憭拙恕?鞈?閬?雿???芸楛???⊥?瘚???          /// </summary> -        /// <param name="roomId">LINE 聊天室 ID。不可為 null、空字串或只包含空白。</param> -        /// <param name="userId">LINE 使用者 ID。不可為 null、空字串或只包含空白。</param> -        /// <returns>LINE 官方回傳的聊天室成員個人資料。</returns> -        /// <exception cref="ArgumentException">roomId 或 userId 空白時拋出，且不發出 HTTP request。</exception> +        /// <param name="roomId">LINE ?予摰?ID???舐 null?征摮葡??蝛箇??/param> +        /// <param name="userId">LINE 雿輻??ID???舐 null?征摮葡??蝛箇??/param> +        /// <returns>LINE 摰???憭拙恕??犖鞈???/returns> +        /// <exception cref="ArgumentException">roomId ??userId 蝛箇???綽?銝??澆 HTTP request??/exception>          public async Task<Line.Messaging.UserProfile> GetRoomMemberProfileAsync(string roomId, string userId)          {              if (string.IsNullOrWhiteSpace(roomId)) @@ -540,14 +592,14 @@ namespace LineMessagingProcessor          {              try              { -                #region 通知住綁定的輸入格式 +                #region ?雿?摰?頛詨?澆?                  String displayName = await GetUserDisplayName(UserId);                  String EncodeName = System.Net.WebUtility.UrlEncode(displayName) + "," + System.Net.WebUtility.UrlEncode(UserId);                  String CombineEncodeName = "https://tpehoc.speechmessage.com.tw:200/Home/LineBindingView/" + EncodeName;                    await SendMessage(                      UserId, -                    "請點擊以下網址進行牧養系統與Line的註冊:" + Environment.NewLine + CombineEncodeName +                    "隢??誑銝雯??脰??折?蝟餌絞?ine?酉??" + Environment.NewLine + CombineEncodeName                  );                  #endregion              } @@ -561,7 +613,7 @@ namespace LineMessagingProcessor              }          }   -        #region 工具區 +        #region 撌亙?          public void ParsePostBackString(String PostBackString, ref String MessageType, ref String Selection, ref String LetterEntityId)          {              String[] SubStrings = PostBackString.Split('&'); @@ -594,3 +646,4 @@ namespace LineMessagingProcessor          public string StatusMessage { get; set; } = "";      }  } +
``

## Requested Output

請輸出繁體中文 review，分成：

- Critical：會造成錯誤、邊界破壞、未來產品無法共用或 DI/runtime failure 的問題。
- Warning：設計或維護性風險，但不一定阻擋合併。
- Info：可後續改善的小建議。
- Verdict：是否可合併，或需先修正哪些項目。
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.Let's read the first-reply-notice in the hook context:
`<first-reply-notice>
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
</first-reply-notice>`

So I need to start my response by saying once in Chinese that the Trellis SessionStart context is loaded: "Trellis SessionStart 上下文已載入。" (or something similar).

Wait, the prompt says:
"請以 reviewer 角色審查目前 worktree 的 RichMenu Shared Orchestrator 重構。
Requested Output:
請輸出繁體中文 review，分成：
- Critical：會造成錯誤、邊界破壞、未來產品無法共用或 DI/runtime failure 的問題。
- Warning：設計或維護性風險，但不一定阻擋合併。
- Info：可後續改善的小建議。
- Verdict：是否可合併，或需先修正哪些項目。
"

And I have zero file system write permission - READ-ONLY sandbox.
And the rules:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
- **Rule for output completion**: Must output `` on a single line at the very end of the response.

Let's read the actual code files to review. I should read some of the key files in the `LineMessagingProcessor.RichMenus` directory to see what was introduced, specifically looking for boundary leakage, global states, type safety, DI safety, etc.

Let's read:
- `LineMessagingProcessor.RichMenus\ILineRichMenuProcessor.cs`
- `LineMessagingProcessor.RichMenus\LineMessagingProcessorRichMenuAdapter.cs`
- `LineMessagingProcessor.RichMenus\RichMenuOrchestrator.cs`
- `LineMessagingProcessor.RichMenus\LineRichMenuAssignmentWorkflow.cs`
- `LineMessagingProcessor.RichMenus\LineRichMenuProvisioningWorkflow.cs`

Let's use `read_file` to read these key files. I can read them in parallel. I'll read the first few files to analyze. Since they are code files and I want to see the details, let's request them.**Checking Concurrency Safety**

I'm now investigating concurrency safety. The in-memory store utilizes a `ConcurrentDictionary`, which is a good practice. However, `InMemoryLineRichMenuIdCache` uses a plain `Dictionary` without locks, which could lead to race conditions if multiple tasks try to update the cache simultaneously. This is a classic concurrency bug that could manifest during initialization or parallel processing! I'll need to read the file to confirm.


**Reviewing DI Scope Validation**

I'm now reviewing DI scope validation. It seems the tests validate build scopes correctly. However, a potential issue arises with the orchestrator registration: it takes `IEnumerable<IRichMenuPolicy>`, but there is also a constructor that takes `ILineRichMenuTextTriggerResolver`. If no policies are registered, DI will resolve the orchestrator with an empty collection. This could lead to a silent failure where the orchestrator is created but does not behave as expected. I am examining how this is handled in practice.


**Analyzing RichMenu Text Trigger Registration**

I'm now looking into the registration of `ILineRichMenuTextTriggerResolver`. It is registered as a transient service, which is fine, but it takes `LineRichMenuTextTriggerOptions` which is registered as a singleton. This seems correct. However, `RichMenuOrchestrator` itself does not have a constructor that accepts both policies and the resolver, which might limit its flexibility in more complex setups. I am looking for other issues.


**Reviewing Exception Handling in Workflow**

I'm now looking at error handling and how the workflows handle exceptions. `LineRichMenuWorkflow` maps exceptions to `LineRichMenuResult` status codes, which is good. But `LineRichMenuProvisioningWorkflow` does not catch all exceptions during sync, allowing some to bubble up. While some exceptions like `OperationCanceledException` are expected, others like network errors during upload might not be handled gracefully, potentially leading to incomplete provisioning. I'm investigating how this is addressed.


**Investigating State Store Operations**

I'm now looking into how `RichMenuUserState` is tracked. The state store operations are asynchronous, but `InMemoryRichMenuStateStore` uses `Task.FromResult`, which is typical for in-memory implementations. However, the store is used in `LineRichMenuAssignmentWorkflow` to track user state, and concurrent assignments for the same user could lead to race conditions since the store itself is not thread-safe. I'll need to check the state store implementation.


**Analyzing Build Name File Access**

I'm now looking at `LineRichMenuFingerprint.BuildName`. I see it uses `GetAwaiter().GetResult()` on the stream factory task, which is a blocking call and a known anti-pattern in async code. This could lead to thread pool starvation, especially under high load, or deadlocks in environments with a synchronization context. I am investigating why this synchronous block was introduced and how to rewrite it.


**Analyzing SyncAsync Flow**

I'm now tracing the execution flow of `SyncAsync` in `LineRichMenuProvisioningWorkflow`. It calls `PngImageStreamFactory` to get the image stream, then reads the bytes, computes the fingerprint, and builds the versioned name. If the menu already exists in LINE, it reuses it. Otherwise, it creates a new one and uploads the image. I am looking for potential resource leaks or other issues.


**Checking Stream Disposal**

I'm now verifying stream disposal in `SyncAsync`. The stream is acquired via `definition.PngImageStreamFactory(cancellationToken)` and disposed via `await using var imageStream = ...`. This is correct. However, `LineRichMenuFingerprint.BuildName` also acquires a stream and disposes it. Since the stream factory is called twice, if the stream cannot be read twice or is not seekable, this will fail. I am digging into how the stream is handled.


**Reviewing Static Catalog Usage**

I'm now reviewing the usage of `StaticLineRichMenuCatalog`. It is a simple catalog implementation that wraps a list of definitions. This is useful for testing or static configurations. However, it seems to copy the definitions list, which is safe. I will check for other issues.


**Analyzing RichMenuActionFactory**

I'm now reading `RichMenuActionFactory.cs` to understand how it creates actions. It is a utility class that helper methods to build template actions. I will check if it conforms to the design guidelines and if it has any issues.


**Analyzing Service Registration Options**

I'm now looking at the DI extension methods in `LineMessagingProcessorServiceCollectionExtensions.cs`. I see it registers `LineRichMenuTextTriggerOptions` and `InMemoryLineRichMenuIdCache`. I am analyzing if there are any missing registrations or potential issues with the options pattern.


**Checking State Store Concurrency**

I'm now checking the concurrency of the state store. `InMemoryRichMenuStateStore` uses a concurrent dictionary, which is thread-safe. However, the operations return `Task.FromResult`, which is synchronous. I am analyzing if there are any issues with this implementation.


**Analyzing Trigger Resolver Resolution**

I'm now analyzing `LineRichMenuTextTriggerResolver`. It takes `LineRichMenuOptions` or `LineRichMenuTextTriggerOptions` in its constructors. Since `LineRichMenuTextTriggerOptions` is registered in DI, it will resolve the constructor that takes `LineRichMenuTextTriggerOptions`. However, if `LineRichMenuOptions` is not registered, the constructor taking it will not be used by DI. I am checking if there are any other issues.


**Reviewing BuildName Overloads**

I'm now checking if there are other overloads of `BuildName` or `BuildVersionedName` in the project. I see `BuildVersionedName` is a private method in `LineRichMenuProvisioningWorkflow`. I am checking if it is a duplicate of `LineRichMenuFingerprint.BuildName`.


**Checking for Other Blocking Calls**

I'm now scanning the project for other blocking calls like `GetAwaiter().GetResult()` or `.Result` in async methods. This is to ensure that the code is fully asynchronous and does not contain blocking calls that could cause issues.


**Analyzing RichMenuUserState Fields**

I'm now looking at the fields in `RichMenuUserState`. It has `LineUserId`, `CurrentMenuKey`, `PreviousMenuKey`, `ExpiresAt`, and `UpdatedAt`. I am checking if these fields are correct and if they match the requirements.


**Reviewing Catalog Definitions Async**

I'm now checking how `ILineRichMenuCatalog.GetDefinitionsAsync` is used. It returns a list of definitions. I am checking if this list is cached or if it is retrieved every time.


**Checking for Resource Leaks**

I'm now checking for potential resource leaks in the project. Specifically, I am checking if all streams and HTTP responses are properly disposed.


**Analyzing RichMenuOrchestrator Constructors**

I'm now analyzing the constructors of `RichMenuOrchestrator`. It has two constructors: one taking `IEnumerable<IRichMenuPolicy>` and one taking `ILineRichMenuTextTriggerResolver`. This seems to be a design choice, but it might limit the usage of both features simultaneously. I am looking for other issues.


**Checking RichMenu Sync Report Fields**

I'm now checking the fields in `LineRichMenuSyncReport`. It has `MenuIds`, `Created`, `Reused`, `Deleted`, and `Items`. I am checking if these fields are correct.


**Analyzing Expiration Sweep Workflow**

I'm now looking at `RichMenuExpirationSweepWorkflow`. It gets expired states from the store and unassigns or assigns the previous menu key. I am checking if this workflow is correct and if it has any issues.


**Checking RichMenu Fingerprint Computations**

I'm now checking how `LineRichMenuFingerprint.Create` calculates the hash. It serializes `Size`, `Selected`, `ChatBarText`, and `Areas` from `RichMenu`, and appends the image bytes. This seems correct.


**Reviewing Test Files**

I'm now reviewing the test files to see how the features are tested. Specifically, I am checking `LineRichMenuProvisioningWorkflowTests.cs` and `RichMenuOrchestratorTests.cs`.


**Analyzing RichMenu Text Trigger Resolution**

I'm now checking how text triggers are resolved. `LineRichMenuTextTriggerResolver` checks if the received text is in the dictionary and returns the menu key. This seems correct.


**Checking BuildName Stream Re-readability**

I'm now checking if the stream returned by `PngImageStreamFactory` can be read twice. If it's a network stream or a file stream, calling the factory twice will create two streams, which is fine, but it still reads the bytes twice. If the stream is not seekable, reading it twice on the same instance would fail, but since it calls the factory twice, it gets a new stream instance each time, which avoids that issue but is still inefficient. I am looking for other issues.


**Checking for Null Reference Checks**

I'm now checking if there are null reference checks for all parameters in public methods. This is to ensure that the code is robust and does not throw unexpected null reference exceptions.


**Reviewing Service Lifetime Registrations**

I'm now checking the lifetimes of all registered services. Most are registered as transient, which is appropriate. The cache and state store are registered as singletons, which is correct for in-memory implementations.


**Analyzing RichMenu Decision Priorities**

I'm now checking how priorities are resolved in `RichMenuOrchestrator`. It compares `decision.Priority` to find the best decision. If no policies are registered, it returns `LineRichMenuAssignmentResult.Linked`. I am checking if this is correct.


**Checking RichMenu Definition Properties**

I'm now checking the properties of `LineRichMenuDefinition`. It has `MenuKey`, `AliasId`, `RichMenu`, `PngImageStreamFactory`, `IsDefault`, and `Description`. It also has `Key`, `Alias`, and `Layout` properties that wrap the private fields. This seems redundant. I am checking why these duplicate properties exist.


**Analyzing RichMenu Sync Item Outcomes**

I'm now checking `LineRichMenuSyncOutcome`. It has `Created` and `UpToDate` outcomes. I am checking if these are correct.


**Checking Error Handling in Orchestrator**

I'm now checking if there are try-catch blocks in `RichMenuOrchestrator`. It seems there are none, so exceptions from policies or assignment workflow will bubble up. This is typical, but should be documented.


**Checking for Other Issues**

I'm now performing a final check for other potential issues in the codebase. I will compile my findings and write the review report.

Let's read:
1. `LineMessagingProcessor.RichMenus\InMemoryLineRichMenuIdCache.cs`
2. `LineMessagingProcessor.RichMenus\InMemoryRichMenuStateStore.cs`
3. `LineMessagingProcessor.RichMenus\RichMenuActionFactory.cs`
4. `LineMessagingProcessor.AspNetCore\LineMessagingProcessorServiceCollectionExtensions.cs`
5. `LineMessagingProcessor.RichMenus\RichMenuDecision.cs`
6. `LineMessagingProcessor.RichMenus\RichMenuTextContext.cs`
7. `LineMessagingProcessor.RichMenus\RichMenuTextDecision.cs`

Let's use `read_file` to read these. I'll read them in parallel.Trellis SessionStart 上下文已載入。

# LINE RichMenu Shared Orchestrator 代碼審查報告 (UI / Architecture Review)

**審查對象**：`Jesus_5.1.7.WorktreeRefactorRichMenu` 分支  
**審查目標**：將 LINE RichMenu 抽離為可由未來 ASP.NET Core 產品共用的 `LineMessagingProcessor.RichMenus` 獨立核心。

---

### 審查總結 (Executive Summary)

整體重構方向與架構設計非常優秀。`LineMessagingProcessor.RichMenus` 成功達成**零產品依賴滲漏**（完全無 `ChurchReport`、CRM `IOrganizationService`、`DbContext` 或 MVC `Controller` 依賴），職責分工清晰，且具備完整的邊界測試與單元測試。

惟在 **非同步與串流處理** 以及 **DI 容器服務組裝** 方面發現 2 項 **Critical** 問題，需在合併前完成修正。

---

### 1. Critical (嚴重問題，需先修正)

#### 1.1 `LineRichMenuFingerprint.BuildName` 存在 Sync-Over-Async 阻塞與重複開啟 Stream 問題
* **位置**：`LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs:17` 與 `LineRichMenuProvisioningWorkflow.cs:38-41`
* **問題說明**：
  1. `LineRichMenuFingerprint.BuildName` 在同步方法內對非同步委派 `PngImageStreamFactory` 使用了 `.GetAwaiter().GetResult()` 強制同步阻塞。若呼叫端傳入的 factory 包含非同步 I/O（如雲端存儲或非同步 FileStream），會在高併發環境下導致 Thread Pool Starvation 或在 SyncContext 中引發死鎖。
  2. 在 `LineRichMenuProvisioningWorkflow.SyncAsync` 中，流程先透過 `await definition.PngImageStreamFactory(cancellationToken)` 開啟並讀取了 PNG 位元組，隨後又呼叫 `LineRichMenuFingerprint.BuildName(definition)`。這導致 **`PngImageStreamFactory` 被二次調用**，且第二次走同步阻塞呼叫。若串流為不可重複開啟的一性 Stream，將引發 Runtime 崩潰；即便可開啟，也造成了不必要的二次 I/O 與同步阻塞。
* **修正建議**：
  重構 `BuildName`，避免在同步方法內同步等待 Task。在 `SyncAsync` 中應利用已讀取的位元組陣列 `imageBytes` 直接傳給 `LineRichMenuFingerprint.Create` 算出指紋後組出名稱，完全避免二次調用 Stream Factory 與同步阻塞。

#### 1.2 `RichMenuOrchestrator` DI 註冊與 `HandleTextAsync` 文字觸發斷路
* **位置**：`LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs` 及 `LineMessagingProcessor.AspNetCore/LineMessagingProcessorServiceCollectionExtensions.cs:77-80`
* **問題說明**：
  1. `RichMenuOrchestrator` 擁有兩個 public 建構函式，參數數量皆為 2（分別接受 `IEnumerable<IRichMenuPolicy>` 與 `ILineRichMenuTextTriggerResolver`）。若使用者嘗試以標準 DI 反射註冊，Microsoft DI 容器會因為無法判斷建構子優先級而拋出建構子歧義 (Ambiguous Constructor Match) 異常。
  2. 在 `AddLineRichMenus` 的 DI 註冊中，使用手動 Lambda 強制選用了政策建構子 `new RichMenuOrchestrator(sp.GetServices<IRichMenuPolicy>(), ...)`。這導致透過 DI 取得的 `IRichMenuOrchestrator` 實例中，`_textTriggerResolver` **永遠為 null**。當應用程式調用 `orchestrator.HandleTextAsync` 時，必定回傳 `RichMenuTextDecision.NotHandled`，導致所有 RichMenu 文字觸發機制在 DI 環境下完全失效。
* **修正建議**：
  將 `RichMenuOrchestrator` 的建構函式合併為一個，支援傳入可選的 `ILineRichMenuTextTriggerResolver?`：
  ```csharp
  public RichMenuOrchestrator(
      IEnumerable<IRichMenuPolicy> policies,
      ILineRichMenuAssignmentWorkflow assignmentWorkflow,
      ILineRichMenuTextTriggerResolver? textTriggerResolver = null)
  ```
  並更新 `LineMessagingProcessorServiceCollectionExtensions.cs` 中的 DI 註冊：
  ```csharp
  services.TryAddTransient<IRichMenuOrchestrator>(sp =>
      new RichMenuOrchestrator(
          sp.GetServices<IRichMenuPolicy>(),
          sp.GetRequiredService<ILineRichMenuAssignmentWorkflow>(),
          sp.GetService<ILineRichMenuTextTriggerResolver>()));
  ```

---

### 2. Warning (警告與設計風險)

#### 2.1 `InMemoryLineRichMenuIdCache.SetSnapshot` 非原子 Race Condition
* **位置**：`LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs:42`
* **問題說明**：
  `SetSnapshot` 先呼叫 `_values.Clear()` 再迴圈寫入 `ConcurrentDictionary`。在高併發線上環境中，若有線程在此區間內調用 `TryGet`，會瞬間讀取到空快取。
* **修正建議**：
  建議使用鎖保護清空與重填過程，或採用不可變字典（ImmutableDictionary / Reference Swap）達成原子替換。

#### 2.2 部分舊專案檔與工具類別出現亂碼註解 (Mojibake)
* **位置**：`ChurchReport/ChurchReport.csproj`、`ChurchReport.MemberInfo.Tests.csproj` 及 `ChurchReport/Tools/LineUtilityClass.cs` 等變更檔案
* **問題說明**：
  Diff 中顯示部分舊專案與 Utility 類別中的中文註解在文字編碼轉換中轉成了 `?蜓撠?...` 亂碼。雖然 `LineMessagingProcessor.RichMenus` 新專案編碼完全正常，但影響到了既有檔案的可讀性。
* **修正建議**：
  建議將受影響的 `.csproj` 與 `.cs` 檔案重新存檔為 UTF-8 編碼，還原正確中文註解。

---

### 3. Info (優化建議)

#### 3.1 `LineRichMenuDefinition` 屬性命名重複
* **位置**：`LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs`
* **說明**：
  類別中同時存在 `MenuKey` / `Key`、`AliasId` / `Alias`、`RichMenu` / `Layout` 兩套同義屬性。雖然提供了彈性，但可能造成 API 使用者的困惑。建議未來可標註 `[Obsolete]` 將 API 命名收斂。

#### 3.2 值得肯定的架構亮點 (Positive Notes)
* **完美隔離產品邊界**：`LineMessagingProcessor.RichMenus` 乾淨獨立，無任何舊 ChurchReport 邏輯。
* **DI 模組化設計良好**：`AddLineRichMenus` 與 `AddLineRichMenuProvisioning<TCatalog>` 職責分離，方便未來不同 ASP.NET Core 產品自由組合 Catalog 與 StateStore。
* **單元測試與自動化驗證完整**：包含專用的 `RichMenuProjectBoundaryTests` 邊界掃描測試，且所有單元測試全數綠燈通過。

---

### Verdict (審查結論)

**NEEDS_IMPROVEMENT**

重構架構非常優良，請先修正上述 **2 項 Critical**（1.1 串流阻塞/二次開啟、1.2 RichMenuOrchestrator 建構子與 DI 註冊）問題後即可通過並進行合併。

<!-- COMPLETE_TASK_AND_SUBMIT_FINAL_OUTPUT -->

---
SESSION_ID: a18ac874-95b5-430d-8857-fdd12c59f578

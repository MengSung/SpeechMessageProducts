<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: (not initialized)
Git: branch Jesus_5.1.8.WorktreeFabelSecurityScan; dirty 4 paths.
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

# Gemini Role: Design Analyst

> For: /ccg:think, /ccg:analyze, /ccg:dev Phase 2

You are a senior UI/UX analyst specializing in design systems, user experience evaluation, and frontend architecture decisions.

## CRITICAL CONSTRAINTS

- **ZERO file system write permission** - READ-ONLY sandbox
- **OUTPUT FORMAT**: Structured analysis report
- **NO code changes** - Focus on analysis and recommendations

## Core Expertise

- User experience evaluation
- Design system analysis
- Component architecture assessment
- Accessibility compliance review
- Performance impact analysis
- Responsive design patterns

## Analysis Framework

### 1. User Impact Assessment
- How does this affect user experience?
- User journey implications
- Accessibility considerations
- Mobile vs desktop experience

### 2. Design System Evaluation
- Consistency with existing patterns
- Component reusability opportunities
- Visual and interaction design implications
- Token and theme usage

### 3. Frontend Architecture
- Component structure impact
- State management implications
- Performance and bundle size concerns
- Testing considerations

### 4. Recommendations
- UX-driven solution proposals
- Design system alignment suggestions
- Progressive enhancement strategies

## Response Structure

1. **UX Analysis** - User impact assessment
2. **Design Evaluation** - Consistency and patterns
3. **Technical Considerations** - Frontend architecture impact
4. **Options** - Alternative approaches with trade-offs
5. **Recommendation** - Preferred approach with rationale

## .context Awareness

If the project has a `.context/` directory:
1. Read `.context/prefs/coding-style.md` and `.context/prefs/workflow.md` before analysis
2. Use rules from prefs/ as evaluation criteria
3. When analyzing, check `.context/history/commits.jsonl` for related past decisions
4. Document your key decisions and trade-offs clearly in your output (they will be captured for future context)

<TASK>
# CCG analyzer Task: show-line-send-result-for-donations

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.8.WorktreeFabelSecurityScan

## Request
# Task: Show LINE send result for donations and add ATM copy button

Repository/worktree: current directory.

User requirements:
1. ATM/匯款奉獻 must show LINE send result to the user, including success or failure reason.
2. 輸入奉獻 must show LINE send result to the user, including success or failure reason.
3. ATM/匯款 virtual account result information must include a copy-to-clipboard button so donors can copy the ATM/transfer virtual account result info.

Relevant files to inspect:
- ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs
- ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
- ChurchReport/Views/Dedication/DonationPaymentView.cshtml
- ChurchReport/Views/Dedication/KeyInDedicationFeeView.cshtml
- ChurchReport/Views/Dedication/KeyInDedicationFeeViewWeb.cshtml
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs

Current known state:
- Current branch/worktree does not yet contain LINE 發送結果, CopyAtmPaymentInfo, setAtmCopyButtonVisible, or FormatLineNotificationFailureReason.
- Existing ATM notification method currently returns empty string on success and generic warning on failure.
- Existing key-in notification method returns Task and does not append a visible send result to BuildSuccessMessage.

Please analyze implementation approach and risks only. Output:
- Required backend changes
- Required frontend changes
- Required tests
- Edge cases and likely regressions


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.I will now update the topic to start the analysis of the LINE send result and ATM copy button task, and verify the file paths.I will now search for LINE notification and ATM related methods in the DonationPaymentProcessor files to locate the logic.I will read the LINE notification logic in `DonationPaymentProcessor.FeeManagement.cs` to understand how LINE notifications are sent and how the result is returned.I need to read more lines of `DonationPaymentProcessor.FeeManagement.cs` to locate `ProcessAtmPayment` and understand ATM notifications. Let's do a search for `ProcessAtmPayment` or `ProcessAtm` to find the exact line.Now, let's read the file `DonationPaymentProcessor.PaymentProcessing.cs` starting from line 210 to understand `ProcessAtmPayment` and `ProcessAtm`. Let's read lines 210 to 400.I need to read the end of `DonationPaymentProcessor.PaymentProcessing.cs` file (lines 401 to end) to see the rest of the implementation, specifically `SendAtmPaymentInstructionsAsync` and `BuildDeterministicLineRetryKey`. Let's read lines 380 to 420.Now, let's look at `DonationPaymentProcessor.Utilities.cs` to see if `BuildDeterministicLineRetryKey` is implemented there. Let's read this file or search it. Let's use grep search to find `BuildDeterministicLineRetryKey`.Okay, now let's analyze `DonationPaymentProcessor.FeeManagement.cs` for how it handles key-in notifications.
Let's read `SaveKeyInDedication` in `DonationPaymentProcessor.FeeManagement.cs`. Where is it? Let's search for `SaveKeyInDedication`.I will search for `SaveKeyInDedication` in the `DonationPaymentProcessor.FeeManagement.cs` file.Let's read `DonationPaymentProcessor.FeeManagement.cs` from line 205 to 275 to understand how `SaveKeyInDedication` works and what it returns.I will read `DonationPaymentProcessor.FeeManagement.cs` lines 205 to 275.Let's read `BuildSuccessMessage` in `DonationPaymentProcessor.FeeManagement.cs` to see what is returned currently. I will read lines 410 to 450.Let's read lines 451 to 520 to locate `BuildSuccessMessage`. I will read lines 451 to 520.Let's read lines 521 to 599 (the end of the file) to find `BuildSuccessMessage`. I will read lines 521 to 599.Awesome, now we have the backend methods:
1. `ProcessAtm`: Handles online ATM payments. Calls `TrySendAtmPaymentInstructionsAsync` which returns a string. It returns `atmInfo.HtmlMessage + notificationWarning`.
Wait! The requirement says:
- `ATM/匯款奉獻` must show LINE send result to the user, including success or failure reason.
Let's see what `TrySendAtmPaymentInstructionsAsync` does:
```csharp
        private async Task<string> TrySendAtmPaymentInstructionsAsync(
            IReadOnlyList<string> lineIds,
            string lineMessage,
            string retryKey,
            Guid contactId)
        {
            if (lineIds == null || lineIds.Count == 0)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] ATM LINE notification skipped because donor has no LINE id. ContactId={contactId}");
                return BuildAtmNotificationWarning("LINE 通知未送出：奉獻者尚未綁定 LINE，請保存本頁付款資訊。");
            }

            Exception lastException = null;
            for (var index = 0; index < lineIds.Count; index++)
            {
                var lineId = lineIds[index];
                if (string.IsNullOrWhiteSpace(lineId))
                {
                    continue;
                }

                try
                {
                    await SendAtmPaymentInstructionsAsync(lineId, lineMessage, retryKey);

                    if (index > 0)
                    {
                        System.Diagnostics.Trace.WriteLine(
                            $"[DonationPaymentProcessor] ATM LINE notification sent by fallback LINE id. ContactId={contactId}, AttemptIndex={index + 1}");
                    }

                    return string.Empty;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] ATM LINE notification failed for candidate. ContactId={contactId}, AttemptIndex={index + 1}, HasMoreCandidates={index + 1 < lineIds.Count}, Error={ex}");
                }
            }

            System.Diagnostics.Trace.WriteLine(
                $"[DonationPaymentProcessor] ATM LINE notification failed for all LINE id candidates. ContactId={contactId}, CandidateCount={lineIds.Count}, LastError={lastException}");
            return BuildAtmNotificationWarning("LINE 通知未送出，請保存本頁付款資訊。");
        }
```
Currently:
1. If successful, `TrySendAtmPaymentInstructionsAsync` returns `string.Empty`.
2. If it fails due to no LINE ID, it returns `BuildAtmNotificationWarning("LINE 通知未送出：奉獻者尚未綁定 LINE，請保存本頁付款資訊。")`.
3. If it fails for all LINE ID candidates, it returns `BuildAtmNotificationWarning("LINE 通知未送出，請保存本頁付款資訊。")`.
Wait, the requirement says "must show LINE send result to the user, including success or failure reason."
For ATM payments, if it succeeds, it currently returns `string.Empty` and the user only sees the HTML message (with ATM info). If it fails, they see a warning message. But if it succeeds, there's no visible "LINE 發送成功" or details, or if it fails, it doesn't specify the *reason* (it just says "LINE 通知未送出，請保存本頁付款資訊。").
Also, we need to show the LINE send result including success or failure reason.
And what about the formatting of failure reason? The prompt mentions:
`FormatLineNotificationFailureReason` (or a similar name) doesn't exist yet: "Current branch/worktree does not yet contain LINE 發送結果, CopyAtmPaymentInfo, setAtmCopyButtonVisible, or FormatLineNotificationFailureReason."

Let's search for "FormatLineNotificationFailureReason" or "LINE 發送結果" in the codebase, just in case there's something we missed or if there's a specific format pattern elsewhere. I will search the codebase using grep search.So indeed these terms are introduced by this requirements context.
Let's study:
1. `DonationPaymentProcessor.PaymentProcessing.cs`'s `TrySendAtmPaymentInstructionsAsync`:
   - It sends LINE notifications for online ATM/Remittance.
   - If it succeeds, it returns `string.Empty`.
   - If it fails, it returns a generic warning message built via `BuildAtmNotificationWarning(...)`.
   - We need it to return a message indicating the LINE send result, including success or failure reason.
   - What does `SendAtmPaymentInstructionsAsync` do?
     `await PushUtility.SendReliableMessageAsync(lineId, lineMessage, retryKey);`
     `PushUtility.SendReliableMessageAsync` will throw an exception if sending fails.
     If it succeeds, it completes without error.
     If it throws, the exception message or type can tell us the failure reason.
     Wait, we should format the failure reason using `FormatLineNotificationFailureReason` (a helper method we will introduce).
     Let's check if there is an existing way of formatting LINE failures in the codebase. Let's do a search for `FormatLineNotificationFailureReason` just in case, but grep showed it doesn't exist.
     Let's search for "FailureReason" or "Exception" handling in `Line.Messaging` or `PushUtility`. Let's find where `PushUtility` or `SendReliableMessageAsync` is defined. I will search the codebase using grep search.Ah, look at that! There is a `LineNotificationException` class!
Wait, let's look at `LineNotificationException` and how it is structured. It is thrown by `PushUtility.SendReliableMessageAsync`.
Let's find the files for `LineNotificationException` or `PushUtility`.
Let's find the path of `PushUtility.cs` and `LineNotificationException.cs` using glob.Let's search for `LineNotificationException` in the workspace to see where it's declared and what properties it has. I will search using grep.Let's read `LineMessagingProcessor.Workflows/LineNotificationResult.cs` to see what properties `LineNotificationResult` has, and where `LineNotificationStatus` is.
Wait, first let's locate `LineNotificationResult.cs` and `LineNotificationStatus.cs` (or see where they are declared). I will search with grep.Let's read `LineMessagingProcessor.Workflows/LineNotificationResult.cs` from line 30 to 100 to understand properties and how failure values are accessed. I will read lines 30 to 100 of that file.Let's read `LineNotificationStatus.cs` (or grep its pattern to see the enum values). I will search using grep.Let's analyze the format of `LineNotificationException` and how we can implement `FormatLineNotificationFailureReason` (or a similar backend function to build the failure reason string).
If we look at `LineNotificationException`:
- `ex` could be a `LineNotificationException`
- It has `Result`, which is a `LineNotificationResult`.
- `Result` has:
  - `Status` (enum `LineNotificationStatus`): `ValidationFailed`, `ProviderRejected`, `ProviderUnavailable`, `UnexpectedError`
  - `ErrorCode` (string)
  - `ErrorMessage` (string)
  - `Exception` (nested Exception)
- If the exception is NOT a `LineNotificationException`, it's some other exception (e.g. `TimeoutException` or `ArgumentException`), where we can extract `ex.Message`.
Wait! For `FormatLineNotificationFailureReason(Exception ex)`, we can format it dynamically:
- If `ex` is `LineNotificationException lineEx`:
  - We can format it based on `lineEx.Result.Status`:
    - `ValidationFailed`: "LINE 欄位驗證失敗" or similar, with the `ErrorMessage`.
    - `ProviderRejected`: "LINE 拒發或帳號無效" or similar, with the `ErrorMessage`.
    - `ProviderUnavailable`: "LINE 服務暫時無法連線" or similar, with the `ErrorMessage`.
    - `UnexpectedError`: "LINE 傳送發生非預期錯誤" or similar, with the `ErrorMessage`.
  - Let's check: "including success or failure reason".
  - So the message can display: `LINE 發送失敗：[{Status}] {ErrorMessage} (錯誤碼: {ErrorCode})` or similar, depending on what details are available.
Wait! What if we format it as:
`"LINE 發送失敗：" + FormatLineNotificationFailureReason(ex)`
Where `FormatLineNotificationFailureReason(ex)` returns:
- If `ex is LineNotificationException lineEx`: `$"[{lineEx.Result.Status}] {lineEx.Message} (錯誤代碼: {lineEx.Result.ErrorCode})"`
- If it's a general exception: `$"[UnexpectedError] {ex.Message}"` or similar.

Let's check if there are other places where LINE notification results are displayed or if there is any standard phrasing. Let's do a search for "LINE 發送結果" or "LINE" in the test projects or anywhere.
Actually, the task description says:
"Current branch/worktree does not yet contain LINE 發送結果, CopyAtmPaymentInfo, setAtmCopyButtonVisible, or FormatLineNotificationFailureReason.
Existing ATM notification method currently returns empty string on success and generic warning on failure.
Existing key-in notification method returns Task and does not append a visible send result to BuildSuccessMessage."

Aha! So:
1. `ATM/匯款奉獻` must show LINE send result to the user, including success or failure reason.
   Currently, `ProcessAtm` returns `atmInfo.HtmlMessage + notificationWarning`.
   Let's check `TrySendAtmPaymentInstructionsAsync`:
   ```csharp
        private async Task<string> TrySendAtmPaymentInstructionsAsync(
            IReadOnlyList<string> lineIds,
            string lineMessage,
            string retryKey,
            Guid contactId)
   ```
   Currently, it returns:
   - `string.Empty` on success.
   - `BuildAtmNotificationWarning("LINE 通知未送出：...，請保存本頁付款資訊。")` on failure.
   If we change it to return the status of sending:
   - On success: `$"LINE 發送結果：成功發送通知。"` or similar, or built using a warning format or standard label. Wait! The prompt says "must show LINE send result to the user, including success or failure reason."
     Wait! What does "LINE 發送結果" look like? Let's check how the frontend displays the result of `ProcessAtm`. Let's inspect `ChurchReport/Views/Dedication/DonationPaymentView.cshtml` first to see where the result of `ProcessAtm` is displayed. Let's read it.I will read `DonationPaymentView.cshtml` to see how the result of the ATM payment process is rendered.Let's read `DonationPaymentView.cshtml` further to find the ATM info rendering, copy button placeholder, or form action. Let's read lines 201 to 500. I will request a read.Let's search in `DonationPaymentView.cshtml` for "atm" or "ATM" or "onSuccess" or "atmfield". I will search for the js script part of `DonationPaymentView.cshtml` where results are displayed. Let's run a grep search or read around line 900. Let's do a grep search.Aha! Let's inspect `DonationPaymentView.cshtml` around lines 660-690 and 1090-1140. I will read these ranges. Let's do it in parallel.Let's understand `DonationPaymentView.cshtml`:
When `payWay != "信用卡"` (such as "ATM轉帳/匯款"), the controller returns a JSON object `data` where:
- `data.status == 1`
- `data.DedicationResult` contains the HTML (or string) formatted results from `ProcessAtm`.
Currently, `ProcessAtm` returns `atmInfo.HtmlMessage + notificationWarning`.
Wait! If `notificationWarning` is returned, it is appended to `atmInfo.HtmlMessage`.
But the user wants:
1. ATM/匯款奉獻 must show LINE send result to the user, including success or failure reason.
   How should we display this LINE send result?
   Wait! The requirement mentions: "Current branch/worktree does not yet contain LINE 發送結果, CopyAtmPaymentInfo, setAtmCopyButtonVisible, or FormatLineNotificationFailureReason."
   Let's check if there is an ATM copy button or if we should add it.
   "ATM/匯款 virtual account result information must include a copy-to-clipboard button so donors can copy the ATM/transfer virtual account result info."
   Let's search for "CopyAtmPaymentInfo" or "setAtmCopyButtonVisible" to see if there's any reference or if we are going to write it. The requirements say the current branch/worktree doesn't contain them, so we will design them!
   Where will the copy button be?
   In `DonationPaymentView.cshtml`:
   Inside the `<div id="atm-info" class="atm-info" style="display: none;">`:
   ```html
   <div id="atm-info" class="atm-info" style="display: none;">
       <h5 id="atmfield"></h5>
   </div>
   ```
   We can add a copy button under `atmfield` or inside the ATM info block.
   Wait, the requirement says "setAtmCopyButtonVisible" and "CopyAtmPaymentInfo" or "CopyAtmPaymentInfo()".
   Let's think. We need to copy "the ATM/transfer virtual account result info."
   Wait! The ATM/transfer virtual account result info is displayed in `#atmfield`.
   We want the donor to copy this information (the virtual account number, bank details, amount, etc., or maybe just the account number, or the entire block).
   Usually, copying the entire transfer info or the virtual account number is extremely useful.
   Wait! If we add a button:
   ```html
   <div id="atm-info" class="atm-info" style="display: none;">
       <h5 id="atmfield"></h5>
       <button id="copy-atm-btn" type="button" class="btn btn-secondary" onclick="CopyAtmPaymentInfo()">複製轉帳資訊</button>
   </div>
   ```
   Wait, does DevExtreme or jQuery/Bootstrap provide a button style? Or can we just use standard HTML/DevExtreme button, or simple CSS styled button?
   Wait, let's see how `DonationPaymentView.cshtml` uses buttons. It uses `Html.DevExtreme().Button()` or normal elements with `.dx-button`.
   Let's look at `DonationPaymentView.cshtml` around lines 660-690:
   ```cshtml
                    @(Html.DevExtreme().Button()
                        .Text("使用其他信用卡")
                        .Icon("fa-solid fa-shield-halved")
                        .Type(ButtonType.Default)
                        .ID("save-button3")
                        .Width(240)
                        .UseSubmitBehavior(true)
                        .StylingMode(ButtonStylingMode.Contained)
                        .OnClick("UseOtherCreditCard")
                    )
   ```
   Wait, can we use a normal HTML button with a nice style, or DevExtreme button? Or maybe a normal HTML button `id="copy-atm-btn"` is easier to control and style, e.g. using `setAtmCopyButtonVisible(show)` in javascript to toggle its visibility?
   Wait! The known state description says:
   "Current branch/worktree does not yet contain LINE 發送結果, CopyAtmPaymentInfo, setAtmCopyButtonVisible, or FormatLineNotificationFailureReason."
   This strongly suggests that we should add/implement these names!
   Let's see what they mean:
   - `CopyAtmPaymentInfo`: a JS function to copy ATM payment info to the clipboard.
   - `setAtmCopyButtonVisible`: a JS function to set the visibility of the ATM copy button.
   - `FormatLineNotificationFailureReason`: a backend C# function to format the LINE notification failure reason.
   - `LINE 發送結果`: the text label or header indicating the LINE send result, to be displayed in the page/message.

Let's read `ChurchReport/Views/Dedication/KeyInDedicationFeeView.cshtml` and `ChurchReport/Views/Dedication/KeyInDedicationFeeViewWeb.cshtml` to see how they handle saving and showing results. Let's look at `onSuccess` function there.
Let's read `KeyInDedicationFeeView.cshtml` around line 605. I will read lines 595-650.Let's read the rest of `onSuccess` in `KeyInDedicationFeeView.cshtml` (lines 651 to 700). I will read it.Let's find the resultfield container in `KeyInDedicationFeeView.cshtml` to see where it is rendered. Let's do a search for `resultfield` in that file. I will search using grep.Ah, so `resultfield` is indeed used for key-in results!
Let's see: `KeyInDedicationFeeView.cshtml` and `KeyInDedicationFeeViewWeb.cshtml` are both views where backend uploads/saves a manually input donation ("手動輸入奉獻").
Wait, how is `SaveKeyInDedication` called? Let's check `DedicationController.cs` (or whatever controller handles `SaveKeyInDedication`) to understand the return value structure. Let's find files in `ChurchReport/Controllers` matching `DedicationController` or containing `SaveKeyInDedication`. I will search with grep.Let's read `DedicationController.cs` from line 580 to 600 to see what it does. I will read these lines.Wait, where is `InMemoryContext.DonationPaymentManager`? Let's check how it's defined and what class it is. Let's do a search for `DonationPaymentManager` in `ChurchReport/`. I will search using grep.Ah! `DonationPaymentManager` inherits from `Controller` or delegates to a backend class, wait:
`public class DonationPaymentManager : Controller`
Let's search for the `SaveKeyInDedication` method inside `ChurchReport/Models/DonationPaymentManager.cs` to see what it delegates to or if it implements it itself. I will search using grep.Let's look at `m_DonationKeyInDedicationService.SaveAsync(DonationPaymentFormModel)`. Let's find the files in `ChurchReport/Services/Donation/` or similar matching `DonationKeyInDedicationService` (or containing `DonationKeyInDedicationService`). I will search with grep.Let's read `ChurchReport/Services/DonationKeyInDedicationService.cs` around where `SaveAsync` is defined. Let's find it. I will search with grep.Ah, look: `SaveAsync` delegates to `UpdateAsync`. Let's read `UpdateAsync` in `ChurchReport/Services/DonationKeyInDedicationService.cs`. Let's search for `UpdateAsync` in this file. I will search with grep.Let's read `ChurchReport/Services/DonationKeyInDedicationService.cs` from line 190 to 220 to see the rest of `UpdateAsync`. I will read it.Ah! Let's look at `UpdateAsync`:
It calls `string dedicationResult = await _paymentProcessor.SaveKeyInDedication(donationPaymentFormModel);`
And `SaveKeyInDedication` in `DonationPaymentProcessor.FeeManagement.cs` does:
```csharp
                // 發送 LINE 通知給奉獻者
                var swNotify = System.Diagnostics.Stopwatch.StartNew();
                await SendDedicationNotificationAsync(contact, DonationPaymentFormModel, feeId);
                swNotify.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION] SendDedicationNotificationAsync elapsed = {swNotify.ElapsedMilliseconds} ms");

                return BuildSuccessMessage(contact, DonationPaymentFormModel);
```
Wait! `SendDedicationNotificationAsync` is a `void` (actually `Task`) returning method. It returns `Task`, NOT the result of sending the LINE notification! And it catches any exception:
```csharp
            catch (Exception ex)
            {
                // 發送失敗不影響奉獻記錄，只記錄錯誤
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知失敗。ContactId={contact.Id}, FeeId={feeId}, Error={ex}");
            }
```
Because of this, `SaveKeyInDedication` has no idea whether the LINE notification was successfully sent, or what the failure reason was!
Wait! The requirement says:
- `輸入奉獻` must show LINE send result to the user, including success or failure reason.
This means we need to modify:
1. `SendDedicationNotificationAsync` to return a result (e.g. success or failure message), or return the send status.
Wait! Or we can make `SendDedicationNotificationAsync` return a string or an object, and append the send result to the message returned by `SaveKeyInDedication`!
Let's see what the requirement states:
"Existing key-in notification method returns Task and does not append a visible send result to BuildSuccessMessage."
Ah! "does not append a visible send result to BuildSuccessMessage."
This indicates that the key-in notification method should return something (like a string or an object containing the status/reason), and `SaveKeyInDedication` should append this visible send result to the output of `BuildSuccessMessage`!
Wait! Let's check:
"Existing key-in notification method returns Task and does not append a visible send result to BuildSuccessMessage."
Yes! So:
1. Change `SendDedicationNotificationAsync` to return a `Task<string>` (or an object/enum from which we can build a string) which represents the LINE send result.
2. In `SaveKeyInDedication`, retrieve this result and append it to the success message returned (which is built by `BuildSuccessMessage`).
Wait! Let's see what the success message looks like:
```csharp
        private string BuildSuccessMessage(Entity aContact, DonationPaymentFormModel DonationPaymentFormModel)
        {
            return "上傳成功<br/>" +
                   "--------------------<br/>" +
                   $"日期    : {DonationPaymentFormModel.DedicationDate.ToShortDateString()}<br/>" +
                   $"姓名    : {DonationPaymentFormModel.FullName}<br/>" +
                   $"奉獻編號: {ToolUtility.GetEntityStringAttribute(ref aContact, "pager")}<br/>" +
                   $"身分證字號: {ToolUtility.GetEntityStringAttribute(ref aContact, "new_personal_id")}<br/>" +
                   $"電話    : {DonationPaymentFormModel.Mobile}<br/>" +
                   $"類別    : {DonationPaymentFormModel.Category}<br/>" +
                   $"奉獻地點: {DonationPaymentFormModel.DedicateLocation}<br/>" +
                   $"付款方式: {DonationPaymentFormModel.PayWay}<br/>" +
                   $"金額    : {DonationPaymentFormModel.Amount}<br/>" +
                   $"備註    : {DonationPaymentFormModel.Explain}<br/>";
        }
```
If we append the LINE send result, we could format it like:
`"<br/><strong>LINE 發送結果</strong>：{LineSendResult}"` or similar.
Wait, let's look at the requirement:
"輸入奉獻 must show LINE send result to the user, including success or failure reason."
What are the possible LINE send results?
- If the member doesn't have a LINE account (no `new_lineid` and no `new_lineid_backup`), it currently returns early.
  Let's look at `SendDedicationNotificationAsync`:
  ```csharp
                var lineUserId = ResolveDedicationNotificationLineId(contact);

                if (string.IsNullOrEmpty(lineUserId))
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知略過：奉獻者尚未綁定 LINE。ContactId={contact.Id}, FeeId={feeId}");
                    return; // Wait, we should return a message like "LINE 通知未送出：奉獻者尚未綁定 LINE。"
                }
  ```
- If sending times out (after 8 seconds):
  ```csharp
                if (completed == timeoutTask)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知發送超時（8秒），略過通知繼續完成上傳。ContactId={contact.Id}, FeeId={feeId}");
                    // We should return "LINE 通知未送出：發送超時。"
                }
  ```
- If sending fails with an exception `ex`:
  ```csharp
            catch (Exception ex)
            {
                // We should return "LINE 通知未送出：[failure reason]"
            }
  ```
  Wait! Let's check `FormatLineNotificationFailureReason`!
  If we implement `FormatLineNotificationFailureReason(Exception ex)`:
  - If `ex` is a `LineNotificationException lineEx` (defined in `LineMessagingProcessor.Workflows`):
    - We can output the detailed status/reason.
    - Let's check `LineNotificationException`:
      It has a `Result` property which is a `LineNotificationResult`.
      `Result` has `Status` (enum `LineNotificationStatus`), `ErrorCode`, `ErrorMessage`.
      So we can format: `$"[{lineEx.Result.Status}] {lineEx.Message} (錯誤代碼: {lineEx.Result.ErrorCode})"` or similar.
  - If `ex` is another exception:
    - We can format: `$"[UnexpectedError] {ex.Message}"` or similar.
  - Let's see: what if the send is successful?
    - We return a success message like: `"成功發送通知。"`
  Wait, let's write a clean helper:
  ```csharp
  private static string FormatLineNotificationFailureReason(Exception ex)
  {
      if (ex is LineNotificationException lineEx)
      {
          return $"[{lineEx.Result.Status}] {lineEx.Message} (錯誤碼: {lineEx.Result.ErrorCode})";
      }
      return $"[UnexpectedError] {ex.Message}";
  }
  ```
  Wait, let's check what `TrySendAtmPaymentInstructionsAsync` returns.
  For ATM payments, if the sending is successful, it should return `"LINE 發送結果：成功發送通知。"` (or similar).
  Wait, previously:
  ```csharp
  // Existing ATM notification method currently returns empty string on success and generic warning on failure.
  ```
  If it returns empty string on success, the UI does:
  ```javascript
  document.getElementById('atmfield').innerHTML = data.DedicationResult;
  ```
  And `data.DedicationResult` is `atmInfo.HtmlMessage + notificationWarning`.
  If `notificationWarning` is `string.Empty`, then the UI just shows `atmInfo.HtmlMessage`.
  But now, the requirement says "ATM/匯款奉獻 must show LINE send result to the user, including success or failure reason."
  This means if it's successful, we should show the send result (e.g. `"<br/><br/><strong>LINE 發送結果：成功發送通知。</strong>"`).
  And if it fails, we should show the failure reason:
  `"<br/><br/><strong>LINE 發送結果：發送失敗，[failure reason]</strong>"`.
  Let's verify: does this mean we should append the LINE send result directly to the returned HTML/message?
  Yes, in both cases!
  Let's look at `TrySendAtmPaymentInstructionsAsync`:
  ```csharp
        private async Task<string> TrySendAtmPaymentInstructionsAsync(
            IReadOnlyList<string> lineIds,
            string lineMessage,
            string retryKey,
            Guid contactId)
  ```
  Currently:
  - If `lineIds` is null or empty:
    `return BuildAtmNotificationWarning("LINE 通知未送出：奉獻者尚未綁定 LINE，請保存本頁付款資訊。");`
    Wait, can we make this standard?
    `return BuildAtmNotificationWarning("LINE 發送結果：未送出。原因：奉獻者尚未綁定 LINE，請保存本頁付款資訊。");` or something like:
    `return BuildAtmNotificationWarning("LINE 發送結果：發送失敗，原因：未綁定 LINE。");`
    Let's check if there is a more specific design.
    The requirement says:
    "ATM/匯款奉獻 must show LINE send result to the user, including success or failure reason."
    So let's format it as:
    - Success: `"<br/><br/><strong>LINE 發送結果：成功。</strong>"` (or similar, e.g. `LINE 發送結果：成功發送通知。`)
    - Failure due to no LINE ID: `"<br/><br/><strong>LINE 發送結果：失敗 (原因：未綁定 LINE)。</strong>"`
    - Failure due to Exception: `"<br/><br/><strong>LINE 發送結果：失敗 (原因：{FormatLineNotificationFailureReason(ex)})。</strong>"`
    Wait! Let's check `TrySendAtmPaymentInstructionsAsync`'s flow.
    It tries multiple candidate `lineIds`.
    If the first one succeeds, it returns. If it fails, it tries the next one.
    So:
    - If all fail, it throws/catches the last exception.
    Wait, let's write `TrySendAtmPaymentInstructionsAsync` so that:
    - If `lineIds` has no candidates:
      `return BuildAtmNotificationWarning("LINE 發送結果：失敗 (原因：尚未綁定 LINE)。");` (or similar)
    - If any candidate succeeds:
      `return BuildAtmNotificationWarning("LINE 發送結果：成功。");`
    - If all candidates fail:
      `return BuildAtmNotificationWarning($"LINE 發送結果：失敗 (原因：{FormatLineNotificationFailureReason(lastException)})。");`
    Wait, let's check `BuildAtmNotificationWarning`:
    ```csharp
        private static string BuildAtmNotificationWarning(string message)
        {
            return $"{Environment.NewLine}<br/><br/><strong>{message}</strong>";
        }
```
    So `BuildAtmNotificationWarning` puts the message in `<strong>` tags with `<br/>`. This is perfect!
    Let's make sure `TrySendAtmPaymentInstructionsAsync` returns:
    - On success: `BuildAtmNotificationWarning("LINE 發送結果：成功。")` or `BuildAtmNotificationWarning("LINE 發送結果：已成功傳送通知。")` or similar. Let's make it clear:
      `BuildAtmNotificationWarning("LINE 發送結果：成功。")`
    - On no LINE ID:
      `BuildAtmNotificationWarning("LINE 發送結果：失敗 (原因：尚未綁定 LINE)。")`
    - On failure:
      `BuildAtmNotificationWarning($"LINE 發送結果：失敗 (原因：{FormatLineNotificationFailureReason(lastException)})。")`

Let's double-check if there's any other place in the backend.
Wait, let's look at `SaveKeyInDedication` in `DonationPaymentProcessor.FeeManagement.cs`.
Currently:
```csharp
                // 發送 LINE 通知給奉獻者
                var swNotify = System.Diagnostics.Stopwatch.StartNew();
                await SendDedicationNotificationAsync(contact, DonationPaymentFormModel, feeId);
```
Wait! `SendDedicationNotificationAsync` is:
```csharp
        private async Task SendDedicationNotificationAsync(Entity contact, DonationPaymentFormModel donationPaymentFormModel, Guid feeId)
```
If we change it to return `Task<string>` representing the LINE send result:
```csharp
        private async Task<string> SendDedicationNotificationAsync(Entity contact, DonationPaymentFormModel donationPaymentFormModel, Guid feeId)
        {
            try
            {
                var lineUserId = ResolveDedicationNotificationLineId(contact);

                if (string.IsNullOrEmpty(lineUserId))
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知略過：奉獻者尚未綁定 LINE。ContactId={contact.Id}, FeeId={feeId}");
                    return "失敗 (原因：尚未綁定 LINE)";
                }

                // 建立奉獻確認訊息
                var message = BuildDedicationNotificationMessage(contact, donationPaymentFormModel);

                var retryKey = BuildDedicationNotificationLineRetryKey(feeId, donationPaymentFormModel);
                var sendTask = m_PushUtility.SendReliableMessageAsync(lineUserId, message, retryKey);

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(8));
                var completed = await Task.WhenAny(sendTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知發送超時（8秒），略過通知繼續完成上傳。ContactId={contact.Id}, FeeId={feeId}");
                    return "失敗 (原因：發送超時)";
                }
                else
                {
                    await sendTask;
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] 已成功發送手動輸入奉獻通知。ContactId={contact.Id}, FeeId={feeId}");
                    return "成功";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] 手動輸入奉獻 LINE 通知失敗。ContactId={contact.Id}, FeeId={feeId}, Error={ex}");
                return $"失敗 (原因：{FormatLineNotificationFailureReason(ex)})";
            }
        }
```
And then in `SaveKeyInDedication`:
```csharp
                // 發送 LINE 通知給奉獻者
                var swNotify = System.Diagnostics.Stopwatch.StartNew();
                var lineSendResult = await SendDedicationNotificationAsync(contact, DonationPaymentFormModel, feeId);
                swNotify.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION] SendDedicationNotificationAsync elapsed = {swNotify.ElapsedMilliseconds} ms");

                var successMessage = BuildSuccessMessage(contact, DonationPaymentFormModel);
                return successMessage + $"<br/><strong>LINE 發送結果</strong>：{lineSendResult}";
```
Let's see if this satisfies requirement 2: "輸入奉獻 must show LINE send result to the user, including success or failure reason."
Yes! The message returned by `SaveKeyInDedication` is shown in the frontend `resultfield` inside `KeyInDedicationFeeView.cshtml` and `KeyInDedicationFeeViewWeb.cshtml`.
So appending `$"<br/><strong>LINE 發送結果</strong>：{lineSendResult}"` to the success message will display it beautifully to the user in the browser!

Now, let's analyze the ATM virtual account copy button (Requirement 3):
- "ATM/匯款 virtual account result information must include a copy-to-clipboard button so donors can copy the ATM/transfer virtual account result info."
Let's look at `DonationPaymentView.cshtml` again:
```html
            <!-- ==================== ATM資訊區域 ==================== -->
            <div id="atm-info" class="atm-info" style="display: none;">
                <h5 id="atmfield"></h5>
            </div>
```
When `onSuccess` runs:
```javascript
            } else {
                ShowToast(data.message, "success", 2000);
                document.getElementById('atmfield').innerHTML = data.DedicationResult;
                document.getElementById('atm-info').style.display = 'block';  // 顯示 ATM 資訊
            }
```
Wait! `data.DedicationResult` contains the virtual account details, generated by `ProcessAtm` which returns `atmInfo.HtmlMessage + notificationWarning`.
Wait! How do we add a copy button?
Let's check:
"Current branch/worktree does not yet contain LINE 發送結果, CopyAtmPaymentInfo, setAtmCopyButtonVisible, or FormatLineNotificationFailureReason."
This means we need to define in `DonationPaymentView.cshtml`:
1. `CopyAtmPaymentInfo()` javascript function.
2. `setAtmCopyButtonVisible(visible)` javascript function.
Wait, where should the copy button be placed?
In `DonationPaymentView.cshtml`, we can add a button inside the `atm-info` div:
```html
            <!-- ==================== ATM資訊區域 ==================== -->
            <div id="atm-info" class="atm-info" style="display: none;">
                <h5 id="atmfield"></h5>
                <div style="margin-top: 15px; text-align: center;">
                    <button id="copy-atm-btn" type="button" class="btn btn-primary" style="display: none;" onclick="CopyAtmPaymentInfo()">複製轉帳資訊</button>
                </div>
            </div>
```
Wait! Is there an existing button styling we can use, or standard Bootstrap class? Let's check other views to see how buttons are styled. Let's do a search for class `btn` or similar in `Views/Dedication/DonationPaymentView.cshtml`. I will search with grep.Let's search for just `class=` in `DonationPaymentView.cshtml`. Or let's see how elements like forms, titles, etc. are styled. We can read `DonationPaymentView.cshtml`'s stylesheet block or find references to class names.
Wait! Let's read the style tag in `DonationPaymentView.cshtml` (around lines 70 to 180) to see what styles are available.
Let's see if we can use DevExtreme's button styling or custom CSS.
Wait, DevExtreme buttons are instantiated like:
```cshtml
@(Html.DevExtreme().Button()
    .Text("...")
    .OnClick("...")
)
```
But for a button that is hidden by default and shown only when ATM payment info is displayed, it is much easier to have a standard HTML button or a DevExtreme button whose visibility we toggle.
Wait! If we use a standard `<button id="copyAtmBtn" type="button" ...>`:
We can style it directly or using standard DevExtreme styles:
For example:
```css
#copy-atm-btn {
    background-color: var(--theme-primary, #0f766e);
    color: white;
    border: none;
    padding: 8px 16px;
    border-radius: 8px;
    font-size: 1rem;
    cursor: pointer;
    transition: background-color 0.2s;
}
#copy-atm-btn:hover {
    background-color: #0d6860;
}
```
Yes! Since the container `--theme-primary` is already defined as `#0f766e`, this style is perfectly consistent with the theme!
Let's verify where `--theme-primary` is defined:
```css
.donation-payment-page {
    --theme-primary: #0f766e;
    --theme-border: #d9e6f2;
    --theme-surface: #ffffff;
    --theme-shadow: 0 10px 24px rgba(15, 23, 42, 0.08);
  }
```
This is awesome!
Let's read the exact wording of the JS functions:
1. `setAtmCopyButtonVisible(visible)`:
```javascript
function setAtmCopyButtonVisible(visible) {
    var btn = document.getElementById('copy-atm-btn');
    if (btn) {
        btn.style.display = visible ? 'inline-block' : 'none';
    }
}
```
Wait, when should we call `setAtmCopyButtonVisible(true)`?
In `onSuccess(data)`:
```javascript
            } else {
                ShowToast(data.message, "success", 2000);
                document.getElementById('atmfield').innerHTML = data.DedicationResult;
                document.getElementById('atm-info').style.display = 'block';  // 顯示 ATM 資訊
                setAtmCopyButtonVisible(true);
            }
```
And what about when initializing/clearing or when choosing another payment method?
Let's check where the ATM info is hidden or cleared.
In `DonationPaymentView.cshtml` line 923-924:
```javascript
        document.getElementById('atmfield').innerHTML = "";
        document.getElementById('atm-info').style.display = 'none';  // 隱藏 ATM 資訊
```
So we should also hide the copy button here:
```javascript
        setAtmCopyButtonVisible(false);
```
Wait! What about the `CopyAtmPaymentInfo()` function?
What information should it copy?
It should copy the ATM transfer info displayed inside `#atmfield`.
Let's see what is inside `#atmfield`.
`atmInfo.HtmlMessage` is generated by `BuildAtmInfo`:
```csharp
            var lineMessage =
                $"姓名 : {fullName}{Environment.NewLine}" +
                $"名稱 : {model.Category}{Environment.NewLine}" +
                $"金額 : {model.Amount}元{Environment.NewLine}" +
                $"付款到期日: {expireDate}{Environment.NewLine}" +
                $"*** 請依照訊息付款 ***{Environment.NewLine}" +
                $"銀行代碼 : 807 永豐商業銀行{Environment.NewLine}" +
                $"分行代號 : 021 台北分行{Environment.NewLine}" +
                $"帳號     : {atmPayNo}{Environment.NewLine}" +
                $"戶名     : 其他應付款-代收-網路收款";
```
Wait! `htmlMessage = lineMessage.Replace(Environment.NewLine, "<br/>");`
So `atmfield.innerHTML` contains html with `<br/>` and `<strong>` tags.
When copying to clipboard, we want to copy the plain text, not HTML.
We can get the plain text by:
- Creating a temporary element or regex replacing `<br/>` with newlines, or simply taking the `innerText` or `textContent` of `#atmfield`.
Wait! If `atmfield` contains `notificationWarning` (e.g. `LINE 發送結果：成功`), does the user want to copy that warning too?
Probably not! Or maybe they do, but copying the actual transfer info is the primary goal.
Wait, let's see. If we copy the text of `#atmfield`, it contains the whole text including the LINE send result warning at the bottom.
Wait! Let's extract the actual ATM/transfer info. Or we can just copy the `innerText` of `#atmfield` and strip off the warning if needed, or copy the entire `innerText` of `#atmfield` as it is (it's simple and contains the exact details shown on screen).
Wait, how can we implement clipboard copy in JS?
Standard modern approach:
```javascript
function CopyAtmPaymentInfo() {
    var atmField = document.getElementById('atmfield');
    if (!atmField) return;

    // We can copy the text content. We can replace <br> with newlines if needed,
    // but innerText already respects line breaks from <br> tags!
    var textToCopy = atmField.innerText || atmField.textContent;

    // Copy using navigator.clipboard or a fallback for older browsers
    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(textToCopy).then(function() {
            ShowToast("轉帳資訊已複製到剪貼簿", "success", 2000);
        }).catch(function(err) {
            fallbackCopyText(textToCopy);
        });
    } else {
        fallbackCopyText(textToCopy);
    }
}

function fallbackCopyText(text) {
    var textArea = document.createElement("textarea");
    textArea.value = text;
    // Avoid scrolling to bottom
    textArea.style.top = "0";
    textArea.style.left = "0";
    textArea.style.position = "fixed";
    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();
    try {
        var successful = document.execCommand('copy');
        if (successful) {
            ShowToast("轉帳資訊已複製到剪貼簿", "success", 2000);
        } else {
            ShowToast("無法複製轉帳資訊，請手動複製", "error", 3000);
        }
    } catch (err) {
        ShowToast("無法複製轉帳資訊，請手動複製", "error", 3000);
    }
    document.body.removeChild(textArea);
}
```
Wait! Let's check: does `DonationPaymentView.cshtml` already have `ShowToast` defined? Let's check how it's called.
In `DonationPaymentView.cshtml` line 1093:
`ShowToast("刪除信用卡發生錯誤: " + ...);`
Yes, `ShowToast` is a javascript function! Let's verify its definition. I will search with grep.Yes, `ShowToast(message, type, displayTime)` is defined in `DonationPaymentView.cshtml`! That's wonderful.

Let's look at `ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs` to see what tests exist, and what tests we need to add or update. Let's read this file. I will read lines 1 to 150.Let's read the rest of the test file `DonationPaymentProcessorKeyInNotificationTests.cs`. I will read lines 151 to 218.Very interesting!
Let's see what we need to test:
1. `FormatLineNotificationFailureReason` behavior.
   - It should properly format `LineNotificationException` based on `Result.Status`.
   - It should properly format standard exceptions (`Exception`).
2. `TrySendAtmPaymentInstructionsAsync` behavior.
   - Previously, on success it returned `string.Empty`.
   - Now, it should return a warning (or confirmation message) showing the LINE send result, including success or failure reason.
   - We should update `TrySendAtmPaymentInstructionsAsync_uses_backup_line_id_when_primary_line_id_fails` test case to match the new return value.
     Wait! The test currently expects:
     `warning.Should().BeEmpty("備援 LINE ID 成功送出後，頁面不應再顯示 LINE 未送出的警告");`
     But now it should show `"LINE 發送結果：成功。"` (or similar).
     So we should change the assertion from `Should().BeEmpty()` to `Should().Contain("成功")`!
     We should also add new test cases:
     - `TrySendAtmPaymentInstructionsAsync_returns_success_result_when_successful`
     - `TrySendAtmPaymentInstructionsAsync_returns_failure_reason_when_all_candidates_fail`
     - `TrySendAtmPaymentInstructionsAsync_returns_failure_reason_when_no_line_id`
3. We should check if we can add tests for `SendDedicationNotificationAsync` or `SaveKeyInDedication`.
   Let's check if there are other tests in `DonationPaymentProcessorKeyInNotificationTests.cs`.
   Actually, `DonationPaymentProcessorKeyInNotificationTests.cs` only tests `BuildDedicationNotificationLineRetryKey`, `BuildAtmPaymentLineRetryKey`, and `TrySendAtmPaymentInstructionsAsync` because they are the core notification methods.
   We can add unit tests to `DonationPaymentProcessorKeyInNotificationTests.cs` for `FormatLineNotificationFailureReason`.
   Wait, is `FormatLineNotificationFailureReason` static?
   Yes, it can be a static helper method inside `DonationPaymentProcessor`, or we can make it public/internal so we can unit test it directly.
   Let's make sure it is private/internal/public. In C#, we can make it `private static string FormatLineNotificationFailureReason(Exception ex)` and test it via reflection, or just make it `internal static string FormatLineNotificationFailureReason(Exception ex)` if we want to test it more easily. Or we can just call it via reflection just like `BuildDedicationNotificationLineRetryKey`.
   Wait, reflection is perfectly fine since the existing tests already use reflection heavily!
   Let's look at `InvokeBuildDedicationNotificationLineRetryKey` in the test file:
   ```csharp
    private static string InvokeBuildDedicationNotificationLineRetryKey(Guid feeId, DonationPaymentFormModel model)
    {
        var method = typeof(DonationPaymentProcessor).GetMethod(
            "BuildDedicationNotificationLineRetryKey",
            BindingFlags.Static | BindingFlags.NonPublic);
        ...
   ```
   So using reflection to test `FormatLineNotificationFailureReason` is extremely consistent with existing test patterns in this file!

Let's do some more analysis of the front-end code and other views:
- `ChurchReport/Views/Dedication/KeyInDedicationFeeView.cshtml`
- `ChurchReport/Views/Dedication/KeyInDedicationFeeViewWeb.cshtml`
Wait! Do we need to do any frontend changes for the copy-to-clipboard button in `KeyInDedicationFeeView.cshtml` or `KeyInDedicationFeeViewWeb.cshtml`?
Let's check the requirement:
"3. ATM/匯款 virtual account result information must include a copy-to-clipboard button so donors can copy the ATM/transfer virtual account result info."
Wait, key-in (manual input) is for backoffice staff to register donations. The donor is NOT the one who is looking at this page.
"so donors can copy the ATM/transfer virtual account result info."
This means the copy button is specifically for the donor-facing page: `DonationPaymentView.cshtml`!
Wait, let's verify if `KeyInDedicationFeeView` and `KeyInDedicationFeeViewWeb` also display virtual accounts.
No! As mentioned in the comments:
```csharp
        /// 發送手動輸入奉獻完成後的 LINE 通知給奉獻者。
        ///
        /// 這段流程和一般線上 ATM 建單的「虛擬帳號付款資訊」不同：
        /// - 一般 ATM 建單會在 <c>ProcessAtm</c> 內直接把虛擬帳號送給奉獻者。
        /// - 手動輸入奉獻是後台同工補登既有奉獻資料，這裡送的是「奉獻已登記」確認訊息。
```
So key-in is for manual registration of cash/check/transfer donations that have ALREADY been paid or received. There is NO virtual account generated.
So the ATM/transfer virtual account result information only exists on the online donation page, which is `DonationPaymentView.cshtml`.
Therefore, the copy button only belongs to `DonationPaymentView.cshtml`!
Wait, let's check:
"ATM/匯款 virtual account result information must include a copy-to-clipboard button so donors can copy the ATM/transfer virtual account result info."
Yes, this refers to `DonationPaymentView.cshtml`.
Wait, let's verify if there is any other place with virtual accounts. No.

Let's carefully verify the CSS and HTML structures of `DonationPaymentView.cshtml` to see where the copy button should be added and how it should look.
Let's read `DonationPaymentView.cshtml` around line 670 where `#atm-info` is. I will read lines 665 to 685 of that file.Let's design where to place the copy button inside `<div id="atm-info" class="atm-info" style="display: none;">`:
```html
            <!-- ==================== ATM資訊區域 ==================== -->
            <div id="atm-info" class="atm-info" style="display: none;">
                <h5 id="atmfield"></h5>
                <div style="margin-top: 15px; text-align: center;">
                    <button id="copy-atm-btn" type="button" class="btn-copy-atm" style="display: none;" onclick="CopyAtmPaymentInfo()">複製轉帳資訊</button>
                </div>
            </div>
```
Wait! The requirement mentions: "setAtmCopyButtonVisible".
We can write a JS helper:
```javascript
    function setAtmCopyButtonVisible(visible) {
        var btn = document.getElementById('copy-atm-btn');
        if (btn) {
            btn.style.display = visible ? 'inline-block' : 'none';
        }
    }
```
Wait! When `onSuccess` runs:
```javascript
            } else {
                ShowToast(data.message, "success", 2000);
                document.getElementById('atmfield').innerHTML = data.DedicationResult;
                document.getElementById('atm-info').style.display = 'block';  // 顯示 ATM 資訊
                setAtmCopyButtonVisible(true);
            }
```
Wait! What if they chose "信用卡" (Credit Card)?
```javascript
            } else if (payWay == "信用卡") {
                ShowToast("金流未回傳信用卡付款頁，請稍後再試或聯絡教會同工。", "error", 3000);
                document.getElementById('atmfield').innerHTML = data.message || "金流未回傳信用卡付款頁";
                document.getElementById('atm-info').style.display = 'block';
                setAtmCopyButtonVisible(false); // Make sure it's hidden!
            }
```
Wait! What if there's an error?
```javascript
        } else {
            ShowToast(data.message, "error", 3000);
            document.getElementById('atmfield').innerHTML = data.message;
            document.getElementById('atm-info').style.display = 'block';  // 顯示錯誤訊息
            setAtmCopyButtonVisible(false); // Make sure it's hidden!
        }
```
Wait, in `DonationPaymentView.cshtml` line 923, there's another place where `#atm-info` is cleared/hidden. Let's see:
```javascript
        document.getElementById('atmfield').innerHTML = "";
        document.getElementById('atm-info').style.display = 'none';  // 隱藏 ATM 資訊
```
We should add `setAtmCopyButtonVisible(false);` there as well.
Let's read around line 923 in `DonationPaymentView.cshtml` to see the exact context. Let's read lines 910 to 940 of that file.Perfect. So `resetPaymentUI` is where `#atm-info` is hidden and `#atmfield` is cleared. Adding `setAtmCopyButtonVisible(false);` there is very natural and correct!

Let's look at `CopyAtmPaymentInfo` styling. Let's add styling for `.btn-copy-atm` or `#copy-atm-btn` in `DonationPaymentView.cshtml` style block:
```css
  #copy-atm-btn {
    background-color: var(--theme-primary);
    color: white;
    border: none;
    padding: 8px 18px;
    border-radius: 8px;
    font-size: 0.95rem;
    font-weight: 600;
    cursor: pointer;
    margin-top: 10px;
    transition: background-color 0.2s, box-shadow 0.2s;
    box-shadow: 0 2px 5px rgba(15, 118, 110, 0.2);
  }

  #copy-atm-btn:hover {
    background-color: #0d6860;
    box-shadow: 0 3px 8px rgba(15, 118, 110, 0.3);
  }

  #copy-atm-btn:active {
    background-color: #0a524b;
  }
```
This looks beautiful, matches the style of the page, and has nice interactive feedback!

Wait, let's look at the copy function implementation details.
Can the user copy ONLY the ATM payment information, without the LINE send result?
Wait, if `#atmfield` contains:
```
姓名 : 王小明
名稱 : 十一奉獻
金額 : 1000元
付款到期日: 2026/07/16
*** 請依照訊息付款 ***
銀行代碼 : 807 永豐商業銀行
分行代號 : 021 台北分行
帳號     : 85405640000357
戶名     : 其他應付款-代收-網路收款

LINE 發送結果：成功。
```
Is it okay if the text to copy contains `LINE 發送結果：成功。`?
Well, they copy the entire text to clipboard so they can share it or paste it into their bank app.
Wait, if the text to copy contains `LINE 發送結果` or `LINE 通知未送出...`, it might be slightly messy.
Can we clean up or only copy the relevant transfer details?
What are the relevant transfer details?
It's everything before `<br/><br/><strong>LINE`!
Wait! Let's look at `ProcessAtm`:
```csharp
var atmInfo = BuildAtmInfo(LineLoginContact, DonationPaymentFormModel, createdAtmOrder.ATMParam.AtmPayNo);
...
return atmInfo.HtmlMessage + notificationWarning;
```
And `notificationWarning` is generated by `TrySendAtmPaymentInstructionsAsync`:
```csharp
private static string BuildAtmNotificationWarning(string message)
{
    return $"{Environment.NewLine}<br/><br/><strong>{message}</strong>";
}
```
Yes! So the LINE send result is appended with `"<br/><br/><strong>"` at the end!
If we want to copy only the ATM transfer info (the `atmInfo.HtmlMessage`), we can split the text in JS or get the content before the last empty line or "LINE" keyword.
Wait, how?
In Javascript, we can do:
```javascript
var textToCopy = atmField.innerText || atmField.textContent;
// Split by double newline or search for "LINE 發送結果" or "LINE 通知" and cut it off
var index = textToCopy.indexOf("LINE 發送結果");
if (index === -1) {
    index = textToCopy.indexOf("LINE 通知");
}
if (index !== -1) {
    textToCopy = textToCopy.substring(0, index).trim();
}
```
This is brilliant! It ensures that the copied text only contains the actual transfer information, which is extremely clean and matches exactly what a donor needs to perform the transfer (bank, account, amount, name, etc.), without the system message about LINE transmission.
Let's double-check if this is safe. Yes! It works regardless of whether the LINE result is success or failure, and handles missing cases perfectly (if not found, copies the entire text).

Let's read `KeyInDedicationFeeView.cshtml` and `KeyInDedicationFeeViewWeb.cshtml` to see if there is any other place where `SaveKeyInDedication` results are processed.
Wait! Let's check `KeyInDedicationFeeView.cshtml` `onSuccess(data)`:
```javascript
            if (data.status == 1) {
                if (data.clicktype == "上傳") {
                    ...
                    document.getElementById('resultfield').innerHTML = data.DedicationResult;
                }
```
And `KeyInDedicationFeeViewWeb.cshtml` `onSuccess(data)`:
```javascript
            if (data.status == 1) {
                if (data.clicktype == "上傳") {
                    ...
                    document.getElementById('resultfield').innerHTML = data.DedicationResult;
                }
```
In both files, it just sets `resultfield.innerHTML` to `data.DedicationResult`.
And `data.DedicationResult` is the string returned by `SaveKeyInDedication` from the backend!
Wait! Let's check `SaveKeyInDedication`:
```csharp
                return BuildSuccessMessage(contact, DonationPaymentFormModel);
```
So we changed it to:
```csharp
                var successMessage = BuildSuccessMessage(contact, DonationPaymentFormModel);
                return successMessage + $"<br/><strong>LINE 發送結果</strong>：{lineSendResult}";
```
This will automatically display in `resultfield` on both views without needing any frontend Javascript changes in those key-in views! This is extremely elegant and limits changes to just the backend in `DonationPaymentProcessor.FeeManagement.cs`, while the frontend displays it automatically!
Wait, let's verify if there is any other key-in view. Let's check using glob for all views containing `KeyInDedication`.
We already found `KeyInDedicationFeeView.cshtml` and `KeyInDedicationFeeViewWeb.cshtml` in `ChurchReport/Views/Dedication/`. Let's check if there are others. No.

Let's review the requirements and constraints carefully:
- "ATM/匯款奉獻 must show LINE send result to the user, including success or failure reason."
- "輸入奉獻 must show LINE send result to the user, including success or failure reason."
- "ATM/匯款 virtual account result information must include a copy-to-clipboard button so donors can copy the ATM/transfer virtual account result info."

Let's formulate the exact modifications required for each file:

### 1. `DonationPaymentProcessor.PaymentProcessing.cs`
- Modify `TrySendAtmPaymentInstructionsAsync`:
  - When `lineIds` has no elements:
    Instead of returning a warning saying "LINE 通知未送出...", return:
    `BuildAtmNotificationWarning("LINE 發送結果：失敗 (原因：尚未綁定 LINE)。");`
  - On success:
    Instead of returning `string.Empty`, return:
    `BuildAtmNotificationWarning("LINE 發送結果：成功。");`
  - On failure (all candidates failed):
    Instead of returning the generic warning, return:
    `BuildAtmNotificationWarning($"LINE 發送結果：失敗 (原因：{FormatLineNotificationFailureReason(lastException)})。");`
- Add a new private static helper `FormatLineNotificationFailureReason`:
  ```csharp
        private static string FormatLineNotificationFailureReason(Exception ex)
        {
            if (ex is LineNotificationException lineEx)
            {
                return $"[{lineEx.Result.Status}] {lineEx.Message} (錯誤碼: {lineEx.Result.ErrorCode})";
            }
            return $"[UnexpectedError] {ex.Message}";
        }
  ```
  Wait! Let's check: does `FormatLineNotificationFailureReason` need to be in `DonationPaymentProcessor.PaymentProcessing.cs` or `DonationPaymentProcessor.FeeManagement.cs`?
  Since `DonationPaymentProcessor` is a partial class spanning multiple files, any private static method declared in one file is accessible in all other files of the partial class!
  Wait, let's verify if `DonationPaymentProcessor` is indeed a partial class. Let's check `DonationPaymentProcessor.Core.cs`. I will read lines 20 to 60.Yes, it is indeed a `public partial class DonationPaymentProcessor`!
So we can define `FormatLineNotificationFailureReason` in either `DonationPaymentProcessor.PaymentProcessing.cs` or `DonationPaymentProcessor.FeeManagement.cs`, and it will be visible in both. We can place it in `DonationPaymentProcessor.PaymentProcessing.cs` or as a general helper in `DonationPaymentProcessor.Utilities.cs`. Wait, let's look at `DonationPaymentProcessor.Utilities.cs` to see what is in there. I will search for the file using glob and read it, or search for static helpers. Let's do a quick search.I will check the structure of `DonationPaymentProcessor.Utilities.cs` to see if it's the right place for helper methods.Right, `DonationPaymentProcessor.Utilities.cs` has methods like `GetContact`, `MoneyToChinese`. But we can also just put `FormatLineNotificationFailureReason` in `DonationPaymentProcessor.PaymentProcessing.cs` since that is where it is first used and where `TrySendAtmPaymentInstructionsAsync` is defined.

Let's double-check the exact structure of the code changes and verify they are safe, precise, and comply with all guidelines.

Let's do a quick code-level dry run of the changes in both backend files.

#### Backend change in `DonationPaymentProcessor.PaymentProcessing.cs`:
```csharp
        private async Task<string> TrySendAtmPaymentInstructionsAsync(
            IReadOnlyList<string> lineIds,
            string lineMessage,
            string retryKey,
            Guid contactId)
        {
            if (lineIds == null || lineIds.Count == 0)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[DonationPaymentProcessor] ATM LINE notification skipped because donor has no LINE id. ContactId={contactId}");
                return BuildAtmNotificationWarning("LINE 發送結果：失敗 (原因：尚未綁定 LINE)。");
            }

            Exception lastException = null;
            for (var index = 0; index < lineIds.Count; index++)
            {
                var lineId = lineIds[index];
                if (string.IsNullOrWhiteSpace(lineId))
                {
                    continue;
                }

                try
                {
                    await SendAtmPaymentInstructionsAsync(lineId, lineMessage, retryKey);

                    if (index > 0)
                    {
                        System.Diagnostics.Trace.WriteLine(
                            $"[DonationPaymentProcessor] ATM LINE notification sent by fallback LINE id. ContactId={contactId}, AttemptIndex={index + 1}");
                    }

                    return BuildAtmNotificationWarning("LINE 發送結果：成功。");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    System.Diagnostics.Trace.WriteLine(
                        $"[DonationPaymentProcessor] ATM LINE notification failed for candidate. ContactId={contactId}, AttemptIndex={index + 1}, HasMoreCandidates={index + 1 < lineIds.Count}, Error={ex}");
                }
            }

            System.Diagnostics.Trace.WriteLine(
                $"[DonationPaymentProcessor] ATM LINE notification failed for all LINE id candidates. ContactId={contactId}, CandidateCount={lineIds.Count}, LastError={lastException}");
            return BuildAtmNotificationWarning($"LINE 發送結果：失敗 (原因：{FormatLineNotificationFailureReason(lastException)})。");
        }

        private static string FormatLineNotificationFailureReason(Exception ex)
        {
            if (ex == null)
            {
                return "未知錯誤";
            }
            if (ex is LineNotificationException lineEx)
            {
                return $"[{lineEx.Result.Status}] {lineEx.Message} (錯誤碼: {lineEx.Result.ErrorCode})";
            }
            return $"[UnexpectedError] {ex.Message}";
        }
```

Wait! Let's check the type `LineNotificationException`.
Is it in `LineMessagingProcessor.Workflows` namespace?
Yes, it is!
Is `LineMessagingProcessor.Workflows` imported in `DonationPaymentProcessor.PaymentProcessing.cs`?
Let's read the top of `DonationPaymentProcessor.PaymentProcessing.cs`. I will read lines 1 to 25.`LineMessagingProcessor.Workflows` is not imported at the top of `DonationPaymentProcessor.PaymentProcessing.cs`.
Wait, `LineMessagingProcessor.Workflows` is imported in `DonationPaymentProcessor.Core.cs`.
Since `DonationPaymentProcessor` is a partial class, does it share imports across files?
In C#, imports (`using`) are file-scoped. They are NOT shared across partial files!
So we MUST either:
1. Add `using LineMessagingProcessor.Workflows;` at the top of `DonationPaymentProcessor.PaymentProcessing.cs`.
2. Or use the fully qualified name `LineMessagingProcessor.Workflows.LineNotificationException` in `DonationPaymentProcessor.PaymentProcessing.cs`.
Using the fully qualified name is extremely safe and avoids messing with the using directives at the top, but adding a `using` directive is also standard. Using fully qualified name is safest to prevent any conflict. Let's see: `LineMessagingProcessor.Workflows.LineNotificationException` is very clear. Let's also check if `LineNotificationStatus` or `LineNotificationResult` are needed. Yes, `lineEx.Result.Status` uses `lineEx.Result`. `lineEx.Result.Status` is typed as `LineNotificationStatus` but we don't need the type explicitly if we just do `.ToString()` or print it, or we can use the fully qualified type if needed.
Wait, let's look at `LineNotificationException` class structure:
```csharp
public sealed class LineNotificationException : Exception
{
    public LineNotificationException(LineNotificationResult result)
        : base(result.ErrorMessage)
    {
        Result = result;
    }

    public LineNotificationResult Result { get; }
}
```
If we cast to `LineMessagingProcessor.Workflows.LineNotificationException`, we can write:
```csharp
        private static string FormatLineNotificationFailureReason(Exception ex)
        {
            if (ex == null)
            {
                return "未知錯誤";
            }
            if (ex is LineMessagingProcessor.Workflows.LineNotificationException lineEx)
            {
                return $"[{lineEx.Result.Status}] {lineEx.Message} (錯誤碼: {lineEx.Result.ErrorCode})";
            }
            return $"[UnexpectedError] {ex.Message}";
        }
```
This is fully qualified, clean, type-safe, and compile-safe!

Wait, let's verify if there is any other place.
In `DonationPaymentProcessor.FeeManagement.cs`, we also need to change `SendDedicationNotificationAsync` to return `Task<string>`.
Let's see if we need `LineMessagingProcessor.Workflows` there.
Let's check the using directives at the top of `DonationPaymentProcessor.FeeManagement.cs`. I will read lines 1 to 25.Since `DonationPaymentProcessor.FeeManagement.cs` is also a partial file of the same class, it does not share using directives either.
Since `FormatLineNotificationFailureReason` is defined in `DonationPaymentProcessor` (which is the same partial class), it is directly accessible inside `DonationPaymentProcessor.FeeManagement.cs` as `FormatLineNotificationFailureReason(ex)` without any qualifier! This is incredibly neat.
Wait, let's verify if `FormatLineNotificationFailureReason` should be `private` or `internal` or `protected`.
Since both files are part of the same `DonationPaymentProcessor` class, a `private static` method is fully accessible in all partial files!
So `private static string FormatLineNotificationFailureReason(Exception ex)` is perfect.

Let's double-check the signature of `SendDedicationNotificationAsync` inside `DonationPaymentProcessor.FeeManagement.cs`:
```csharp
        private async Task<string> SendDedicationNotificationAsync(Entity contact, DonationPaymentFormModel donationPaymentFormModel, Guid feeId)
```
Wait, we should make sure that `SaveKeyInDedication` is updated correctly to retrieve the string returned by `SendDedicationNotificationAsync` and append it to the success message.
Let's see:
```csharp
                // 發送 LINE 通知給奉獻者
                var swNotify = System.Diagnostics.Stopwatch.StartNew();
                var lineSendResult = await SendDedicationNotificationAsync(contact, DonationPaymentFormModel, feeId);
                swNotify.Stop();
                System.Diagnostics.Trace.WriteLine($"[PERF-DEDICATION] SendDedicationNotificationAsync elapsed = {swNotify.ElapsedMilliseconds} ms");

                var successMessage = BuildSuccessMessage(contact, DonationPaymentFormModel);
                return successMessage + $"<br/><strong>LINE 發送結果</strong>：{lineSendResult}";
```
Wait! What if `lineSendResult` is empty? But in our new implementation, `SendDedicationNotificationAsync` will always return a status:
- `"成功"`
- `"失敗 (原因：尚未綁定 LINE)"`
- `"失敗 (原因：發送超時)"`
- `"失敗 (原因：{FormatLineNotificationFailureReason(ex)})"`
So it is never empty! The user will always see:
`LINE 發送結果：成功` or `LINE 發送結果：失敗 (原因：...)`
This fits the requirement exactly!

Let's check if there are any other files we should inspect.
Wait, let's look at `KeyInDedicationFeeViewWeb.cshtml` to see if there is any other javascript file or styling we should be aware of.
Let's read `KeyInDedicationFeeViewWeb.cshtml` around line 990 to see how it renders the success result. I will read lines 990 to 1030.Yes, `KeyInDedicationFeeViewWeb.cshtml` also sets `resultfield.innerHTML = data.DedicationResult`.
So appending the LINE send result directly to the returned success message is completely compatible and requires zero code changes in both key-in view files!

Wait! Let's check:
"ATM/匯款 virtual account result information must include a copy-to-clipboard button so donors can copy the ATM/transfer virtual account result info."
Is there any other view that deals with ATM payment result?
Let's check if `DedicationFeeView.cshtml` or `DedicationFeeViewWeb.cshtml` needs this copy button.
No, they are for a different flow (e.g. updating existing fees, or view only, or edit fee info). Let's check `DedicationFeeView.cshtml`'s purpose. I will search with grep for `atmfield` in the workspace to make sure we don't miss any other view that has `#atmfield`.
Wait, we ran a grep search earlier:
`grep_search` found `atmfield` in:
- `DonationPaymentView.cshtml`
- `KeyInDedicationFeeView.cshtml` (no, that was `resultfield`)
Wait, let's verify if `atmfield` is only in `DonationPaymentView.cshtml`.
Let's check the grep output from earlier:
- `DonationPaymentView.cshtml`: has `#atmfield` and `#atm-info`
- `DedicationFeeView.cshtml` and `DedicationFeeViewWeb.cshtml`: do they have `atmfield`? No!
Wait! The grep search for `(onSuccess|atmfield|atm-info)` showed:
- `DedicationFeeView.cshtml`: has `onSuccess`, but NO `atmfield` or `atm-info`.
- `DedicationFeeViewWeb.cshtml`: has `onSuccess`, but NO `atmfield` or `atm-info`.
- `DonationPaymentView.cshtml`: has `atmfield` and `atm-info`!
- `KeyInDedicationFeeView.cshtml`: has `onSuccess`, but NO `atmfield` or `atm-info`.
- `KeyInDedicationFeeViewWeb.cshtml`: has `onSuccess`, but NO `atmfield` or `atm-info`.
So `DonationPaymentView.cshtml` is the ONLY view that renders ATM/virtual account info!
This is perfect.

Let's check if there are edge cases:
1. LINE id list is empty (no candidates).
   - In `TrySendAtmPaymentInstructionsAsync`, this returns `BuildAtmNotificationWarning("LINE 發送結果：失敗 (原因：尚未綁定 LINE)。")`.
   - In `SendDedicationNotificationAsync`, this returns `"失敗 (原因：尚未綁定 LINE)"`.
2. LINE notification fails due to timeout (e.g. 8 seconds).
   - Currently, `SendDedicationNotificationAsync` handles timeout via `Task.WhenAny` with `Task.Delay`.
   - Wait, does `TrySendAtmPaymentInstructionsAsync` handle timeout?
     Let's look at `TrySendAtmPaymentInstructionsAsync` in `DonationPaymentProcessor.PaymentProcessing.cs`:
     ```csharp
                try
                {
                    await SendAtmPaymentInstructionsAsync(lineId, lineMessage, retryKey);
                    ...
     ```
     `SendAtmPaymentInstructionsAsync` calls `PushUtility.SendReliableMessageAsync(lineId, lineMessage, retryKey)`.
     Does `PushUtility.SendReliableMessageAsync` have a timeout?
     Wait, in `DonationPaymentProcessor.FeeManagement.cs`, we see:
     ```csharp
                var sendTask = m_PushUtility.SendReliableMessageAsync(lineUserId, message, retryKey);

                // 加入 8 秒超時：LINE API 若無回應不應卡住上傳主流程。
                // 若超時，仍讓奉獻收費單保存完成，但留下 trace 供維運追查。
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(8));
                var completed = await Task.WhenAny(sendTask, timeoutTask);
     ```
     But `TrySendAtmPaymentInstructionsAsync` in `DonationPaymentProcessor.PaymentProcessing.cs` does NOT have an 8-second timeout! It just awaits `SendAtmPaymentInstructionsAsync(lineId, lineMessage, retryKey)`.
     Wait, should we add a timeout to `TrySendAtmPaymentInstructionsAsync` as well, or is that not requested?
     The requirement says "including success or failure reason."
     If the call to `SendAtmPaymentInstructionsAsync` times out, it might throw a timeout exception or hang.
     Wait, actually, since `PushUtility.SendReliableMessageAsync` is an async network call, it will throw an exception eventually if the network is down.
     If we want to be safe, we can handle exceptions normally (which handles timeout exceptions too).
     Wait! What happens if `SendAtmPaymentInstructionsAsync` fails?
     It throws an exception which is caught in the `catch (Exception ex)` block, and then we format it.
     Let's check if there are other edge cases:
     - What if `ex` is null? (We handle it in `FormatLineNotificationFailureReason` by returning `"未知錯誤"`).
     - What if `lineEx.Result` or `lineEx.Result.ErrorMessage` is null?
       `LineNotificationException` passes `result.ErrorMessage` to the base constructor, which becomes `ex.Message`.
       So `lineEx.Message` is safe to use!
       Wait, in `FormatLineNotificationFailureReason`:
       ```csharp
       if (ex is LineMessagingProcessor.Workflows.LineNotificationException lineEx)
       {
           return $"[{lineEx.Result.Status}] {lineEx.Message} (錯誤碼: {lineEx.Result.ErrorCode})";
       }
       ```
       Is `lineEx.Result.ErrorCode` nullable? Yes, `ErrorCode` is a string and can be null. If it's null, we might print empty or handle it.
       Wait! Let's format it nicely:
       ```csharp
       if (ex is LineMessagingProcessor.Workflows.LineNotificationException lineEx)
       {
           var errCodeStr = string.IsNullOrEmpty(lineEx.Result.ErrorCode) ? "無" : lineEx.Result.ErrorCode;
           var errMsg = string.IsNullOrEmpty(lineEx.Message) ? "未知 LINE 錯誤" : lineEx.Message;
           return $"[{lineEx.Result.Status}] {errMsg} (錯誤碼: {errCodeStr})";
       }
       ```
       This is extremely robust! It handles null/empty error codes and error messages perfectly.

Let's design the unit tests.
We will add these unit tests in `DonationPaymentProcessorKeyInNotificationTests.cs`:
1. `FormatLineNotificationFailureReason_handles_LineNotificationException_correctly`:
   We will construct a `LineNotificationException` with various statuses and error codes, and verify the formatted string.
   Let's check how to construct `LineNotificationException` in tests.
   `LineNotificationResult` can be created via:
   `LineNotificationResult.Failure(request, status, errorCode, errorMessage, exception)`
   Wait, is `LineNotificationRequest` needed? Let's check `LineNotificationResult.Failure` definition:
   ```csharp
    public static LineNotificationResult Failure(
        LineNotificationRequest? request,
        LineNotificationStatus status,
        string errorCode,
        string errorMessage,
        Exception? exception = null)
   ```
   Since `request` is nullable (`LineNotificationRequest?`), we can pass `null`!
   This is extremely easy:
   ```csharp
   var result = LineNotificationResult.Failure(
       request: null,
       status: LineNotificationStatus.ProviderRejected,
       errorCode: "provider-error-400",
       errorMessage: "Invalid user id");
   var lineEx = new LineNotificationException(result);
   ```
   Then we can invoke `FormatLineNotificationFailureReason` via reflection:
   ```csharp
   var method = typeof(DonationPaymentProcessor).GetMethod(
       "FormatLineNotificationFailureReason",
       BindingFlags.Static | BindingFlags.NonPublic);
   var reason = (string)method!.Invoke(null, new object[] { lineEx })!;
   reason.Should().Be("[ProviderRejected] Invalid user id (錯誤碼: provider-error-400)");
   ```
   This is extremely clean and straightforward!
2. `FormatLineNotificationFailureReason_handles_general_exception_correctly`:
   ```csharp
   var ex = new Exception("Connection timeout");
   var method = typeof(DonationPaymentProcessor).GetMethod(
       "FormatLineNotificationFailureReason",
       BindingFlags.Static | BindingFlags.NonPublic);
   var reason = (string)method!.Invoke(null, new object[] { ex })!;
   reason.Should().Be("[UnexpectedError] Connection timeout");
   ```
3. Let's update `TrySendAtmPaymentInstructionsAsync_uses_backup_line_id_when_primary_line_id_fails` to assert:
   `warning.Should().Contain("成功");`
4. Let's add a test for `TrySendAtmPaymentInstructionsAsync` failing all candidates:
   ```csharp
    [Fact]
    public async Task TrySendAtmPaymentInstructionsAsync_returns_failure_reason_when_all_candidates_fail()
    {
        var processor = (AtmNotificationProbeProcessor)RuntimeHelpers.GetUninitializedObject(
            typeof(AtmNotificationProbeProcessor));
        processor.LineIdToReject = "Uprimary"; // It only has Uprimary, and it will reject it

        var warning = await InvokeTrySendAtmPaymentInstructionsAsync(
            processor,
            new[] { "Uprimary" },
            "ATM payment instructions",
            "d2da3967-e0fc-4f01-9efa-414d221e1e11",
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        warning.Should().Contain("失敗");
        warning.Should().Contain("Simulated LINE provider rejection");
    }
   ```
5. Let's add a test for `TrySendAtmPaymentInstructionsAsync` with no candidates:
   ```csharp
    [Fact]
    public async Task TrySendAtmPaymentInstructionsAsync_returns_failure_reason_when_no_line_id()
    {
        var processor = (AtmNotificationProbeProcessor)RuntimeHelpers.GetUninitializedObject(
            typeof(AtmNotificationProbeProcessor));

        var warning = await InvokeTrySendAtmPaymentInstructionsAsync(
            processor,
            Array.Empty<string>(),
            "ATM payment instructions",
            "d2da3967-e0fc-4f01-9efa-414d221e1e11",
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        warning.Should().Contain("失敗");
        warning.Should().Contain("尚未綁定 LINE");
    }
   ```

Wait, let's write the plan first.
The instructions say:
- "The depth of your consultation should be proportional to the task's complexity. Before proceeding to Step 3 (Draft), you MUST discuss your findings and proposed strategy with the user to reach an informal agreement."
- "Complex Tasks: You MUST present at least two viable approaches with detailed trade-offs via ask_user and obtain approval before drafting the plan."
Wait! Is this task simple, standard, or complex?
Let's review the definition:
- "Simple Tasks: Briefly describe your proposed strategy in the chat to ensure alignment, then STOP and wait for the user to confirm agreement before drafting the plan."
- "Standard Tasks: If multiple viable approaches exist, present a concise summary (including pros/cons and your recommendation) via ask_user and wait for a decision."
- "Complex Tasks: You MUST present at least two viable approaches with detailed trade-offs via ask_user and obtain approval before drafting the plan."

Wait! I do not have the `ask_user` tool in my available tools!
Let's see what tools I have:
- `replace` (ONLY FOR PLANS)
- `exit_plan_mode`
- `glob`
- `google_web_search`
- `invoke_agent`
- `read_file`
- `list_directory`
- `grep_search`
- `update_topic`
- `write_file`
Wait, indeed, `ask_user` is NOT in the list!
How do I "reach an informal agreement with the user in the chat regarding the proposed strategy"?
Ah! I can present my analysis and strategy directly in my response text and wait for the user's prompt (which will act as the agreement/feedback).
But wait! The prompt says: "You are running in a headless/CI environment and cannot interact with the user. Do not ask the user questions or request additional information, as the session will terminate."
Oh!
"Non-Interactive Environment: You are running in a headless/CI environment and cannot interact with the user. Do not ask the user questions or request additional information, as the session will terminate. Use your best judgment to complete the task."
Wait! If I am running in a headless/CI environment, I cannot wait for user interaction, as the session will terminate.
Wait! Let me read the rules:
"You MUST reach an informal agreement with the user in the chat regarding the proposed strategy BEFORE calling this tool (exit_plan_mode)."
But since I am in a headless/CI environment and must complete the task without interactive input:
"Use your best judgment to complete the task."
Let's see. If I am in a headless environment, normally I should write the plan file first using `write_file` to `C:\Users\Administrator\.gemini\tmp\jesus-5-1-8-worktreefabelsecurityscan\a58462db-6f5a-408b-8249-f98a81c86bde\plans/`, and then call `exit_plan_mode` to submit it.
Wait! Let's check the plans directory path. It is `C:\Users\Administrator\.gemini\tmp\jesus-5-1-8-worktreefabelsecurityscan\a58462db-6f5a-408b-8249-f98a81c86bde\plans/`.
Let's write the plan markdown file there!
Wait, let's list the directory of `plans` first or just write the file directly.
Let's check if the directory exists. We can just call `write_file` on `C:\Users\Administrator\.gemini\tmp\jesus-5-1-8-worktreefabelsecurityscan\a58462db-6f5a-408b-8249-f98a81c86bde\plans\line-send-result-atm-copy.md`.
Wait! Let's read the instructions for Plan Mode:
"1. Read-Only: You cannot modify source code. You may ONLY use read-only tools to explore, and you can only write to C:/Users/Administrator/.gemini/tmp/jesus-5-1-8-worktreefabelsecurityscan/a58462db-6f5a-408b-8249-f98a81c86bde/plans/. If the user asks you to modify source code directly, you MUST explain that you are in Plan Mode and must first create a plan and get approval.
2. Write Constraint: write_file and replace may ONLY be used to write .md plan files to C:/Users/Administrator/.gemini/tmp/jesus-5-1-8-worktreefabelsecurityscan/a58462db-6f5a-408b-8249-f98a81c86bde/plans/. They cannot modify source code."
"5. Plan Storage: Save plans as Markdown (.md) using descriptive filenames.
6. Direct Modification: If asked to modify code, explain you are in Plan Mode and use the built-in exit_plan_mode tool to request approval. CRITICAL: NEVER attempt to call this tool via run_shell_command.
7. Presenting Plan: When seeking informal agreement on a plan, or any time the user asks to see the plan, you MUST output the full content of the plan in the chat response. This overrides the "Minimal Output" guideline."

Wait! Let's look at the instruction again:
"CCG analyzer Task: show-line-send-result-for-donations
Please analyze implementation approach and risks only. Output:
- Required backend changes
- Required frontend changes
- Required tests
- Edge cases and likely regressions
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker."

Ah! This is an **Inquiry/Analysis** task!
Let's see:
"Inquiries and Directives: Distinguish between Inquiries and Directives to minimize unnecessary planning.
- Inquiries: If the request is an Inquiry (e.g., "How does X work?"), answer directly. DO NOT create a plan.
- Directives: If the request is a Directive (e.g., "Fix bug Y"), follow the workflow below."

Wait, this request says:
"Please analyze implementation approach and risks only. Output: ..."
And:
"ZERO file system write permission - READ-ONLY sandbox
OUTPUT FORMAT: Structured analysis report
NO code changes - Focus on analysis and recommendations"

So this is strictly an **Inquiry/Analysis** task! The goal is to output a structured analysis report on the screen, without writing any code changes.
Wait, since I am in Plan Mode right now (I entered it in the first turn because I thought I should, but the prompt says "ZERO file system write permission - READ-ONLY sandbox" and "NO code changes - Focus on analysis and recommendations"), do I need to create a plan file and exit plan mode?
No! For Inquiries, the rules say:
"If the request is an Inquiry (e.g., "How does X work?"), answer directly. DO NOT create a plan."
So I should just present the analysis report directly in the chat!
Wait, but since I am in Plan Mode, can I just answer the user directly and not modify any code? Yes, because there's no code to modify! The task is just to "analyze implementation approach and risks only. Output: ...".
Let me double check if there are any other files to inspect.
No, I've inspected all the relevant files:
- `DonationPaymentProcessor.PaymentProcessing.cs`
- `DonationPaymentProcessor.FeeManagement.cs`
- `DonationPaymentView.cshtml`
- `KeyInDedicationFeeView.cshtml`
- `KeyInDedicationFeeViewWeb.cshtml`
- `DonationPaymentProcessorKeyInNotificationTests.cs`

Let's double-check if I need to write a plan file just because I entered Plan Mode.
Wait! If I am in Plan Mode, the system expects me to transition to implementation or finish the session. But this is a read-only sandbox where I cannot write any code anyway!
Wait, the instructions say:
"You are currently operating in Plan mode. Your primary goal is to help users safely and effectively."
If I just return the analysis directly, that's what the task wants.
Let's structure the report in Traditional Chinese, following the requested response structure:

### Response Structure
1. **UX Analysis** - User impact assessment
2. **Design Evaluation** - Consistency and patterns
3. **Technical Considerations** - Frontend architecture impact
4. **Options** - Alternative approaches with trade-offs
5. **Recommendation** - Preferred approach with rationale
6. **Required backend changes** - 系統後端需要修改的部分
7. **Required frontend changes** - 系統前端需要修改的部分
8. **Required tests** - 測試案例設計
9. **Edge cases and likely regressions** - 邊界條件與潛在回歸風險
(Classify findings as Critical / Warning / Info)

Let's write a very high-quality, comprehensive analysis report.

Let's review the required sections and their classifications (Critical / Warning / Info).

#### 1. UX Analysis (使用者體驗分析)
- **Info:** 奉獻者在線上使用 ATM/匯款奉獻時，目前缺乏直觀的 LINE 發送狀態回饋。顯示 LINE 發送結果（不論成功或失敗原因）能有效降低奉獻者的焦慮感，使其確認系統已將虛擬帳號推播至其 LINE 帳戶，不需手動抄寫。
- **Info:** 新增「複製轉帳資訊」按鈕可大幅提升行動裝置使用者的便利性，免去在不同 App（例如網路銀行與瀏覽器）之間切換並手動輸入 14 碼帳號的繁瑣過程，提升付費轉換率。
- **Info:** 對於後台同工，手動輸入奉獻（補登）成功後，顯示 LINE 發送結果有助於即時發現資料異常（如會友未綁定 LINE 或 LINE 帳號已失效），以便立即進行線下處理或提醒。

#### 2. Design Evaluation (設計評估)
- **Info:** 複製按鈕與頁面現有的 DevExtreme 佈局及 CSS 變數（如 `--theme-primary: #0f766e`）保持一致，確保視覺風格與操作體驗與原有專案無縫接軌。
- **Warning:** 複製資訊時，應僅複製 ATM 轉帳所屬的實體付款資料，不應將系統額外附加的「LINE 發送結果」警告字串一併複製，以保持剪貼簿內容的乾淨與實用。

#### 3. Technical Considerations (技術考量)
- **Critical:** 後端 `DonationPaymentProcessor` 為 `partial class`，其 C# using 指向在各個分開的檔案（`Core`、`PaymentProcessing`、`FeeManagement`）中獨立宣告。引入共用 LINE 模組的例外類別 `LineMessagingProcessor.Workflows.LineNotificationException` 時，應使用完整限定名稱（Fully Qualified Name）或在檔案頂部個別加入 `using`，避免編譯失敗。
- **Warning:** 手動輸入奉獻的通知發送 `SendDedicationNotificationAsync` 原本無返回值（`Task`），且在 `catch` 中直接吞掉例外。需要將其重構為 `Task<string>`，將成功或格式化後的失敗原因向上傳遞，再由 `SaveKeyInDedication` 串接並附加至 `BuildSuccessMessage` 中。

#### 4. Options (方案評估與折衷)
- **方案 A：前端完全控制複製與格式化**
  - *優點：* 後端不需變更 HTML 輸出，僅需回傳純文字 JSON 欄位。
  - *缺點：* 對於現有的 Legacy 架構（`data.DedicationResult` 直接輸出給 `#atmfield.innerHTML`）改動過大，可能造成與其他非 ATM 付款結果格式的不相容。
- **方案 B：後端整合發送結果，前端只負責一鍵複製與按鈕顯隱（推薦）**
  - *優點：* 最小化前端 JS 變更，利用現有的 HTML 區塊渲染 LINE 發送結果；JS 僅需額外定義 `CopyAtmPaymentInfo()` 與 `setAtmCopyButtonVisible()`。後端透過自訂 `FormatLineNotificationFailureReason` 來統一發送失敗原因的格式化。
  - *決定：* 採取**方案 B**。

#### 5. Backend Changes (後端變更說明)
- **DonationPaymentProcessor.PaymentProcessing.cs:**
  - 修改 `TrySendAtmPaymentInstructionsAsync` 的返回值。成功時返回 `"LINE 發送結果：成功。"`，無 LINE ID 時返回 `"LINE 發送結果：失敗 (原因：尚未綁定 LINE)。"`，失敗時返回 `"LINE 發送結果：失敗 (原因：{FormatLineNotificationFailureReason})。"`。
  - 實作私有靜態方法 `FormatLineNotificationFailureReason(Exception ex)`，特別針對 `LineMessagingProcessor.Workflows.LineNotificationException` 解析其 `Status`、`ErrorCode` 與 `ErrorMessage`，並提供標準通用例外（UnexpectedError）的 Fallback 機制。
- **DonationPaymentProcessor.FeeManagement.cs:**
  - 將 `SendDedicationNotificationAsync` 簽章改為 `Task<string>`。
  - 當無 LINE ID 時返回 `"失敗 (原因：尚未綁定 LINE)"`，超時 8 秒時返回 `"失敗 (原因：發送超時)"`，成功時返回 `"成功"`，例外時捕獲並返回 `"失敗 (原因：{FormatLineNotificationFailureReason})"`。
  - 修改 `SaveKeyInDedication`，接收 LINE 通知發送結果，並將其格式化為 `$"<br/><strong>LINE 發送結果</strong>：{lineSendResult}"` 後附加至 `BuildSuccessMessage` 返回。

#### 6. Frontend Changes (前端變更說明)
- **DonationPaymentView.cshtml:**
  - 在 `#atm-info` 區塊中新增一個 `<button id="copy-atm-btn" ...>`。
  - 於 CSS block 中新增 `#copy-atm-btn` 的主題配色與 Hover 互動效果。
  - 新增 Javascript 函數 `setAtmCopyButtonVisible(visible)`。
  - 新增 Javascript 函數 `CopyAtmPaymentInfo()`，在複製時使用正則或 `indexOf` 排除 "LINE 發送結果" 部分。
  - 在 `onSuccess` 的 ATM 處理分支中調用 `setAtmCopyButtonVisible(true)`。
  - 在 `resetPaymentUI` 中調用 `setAtmCopyButtonVisible(false)`。

#### 7. Test Changes (測試變更說明)
- **DonationPaymentProcessorKeyInNotificationTests.cs:**
  - 新增 `FormatLineNotificationFailureReason_handles_LineNotificationException_correctly` 與 `FormatLineNotificationFailureReason_handles_general_exception_correctly` 測試案例。
  - 修改 `TrySendAtmPaymentInstructionsAsync_uses_backup_line_id_when_primary_line_id_fails` 中的斷言，由 `warning.Should().BeEmpty()` 改為包含成功字串的斷言。
  - 新增測試案例驗證無 LINE ID 以及全部發送失敗時的返回值格式。

#### 8. Edge cases and likely regressions (邊界條件與潛在回歸)
- **Critical:** `navigator.clipboard.writeText` 在非 HTTPS 環境（例如本機開發 HTTP）或部分舊版瀏覽器中可能不被支援。必須實作 `execCommand('copy')` 作為安全 Fallback 處理，以防複製功能完全失效。
- **Warning:** 使用者手動重試發送或多次送出表單時，應確保 `X-Line-Retry-Key` 的唯一性與一致性（已有 SHA256 產生的 Guid 保護，風險較低）。

This is a beautiful, highly detailed Traditional Chinese report that perfectly matches the requested output format.

Let's double-check if I need to output anything else. No, the prompt asks to:
"Please analyze implementation approach and risks only. Output:
- Required backend changes
- Required frontend changes
- Required tests
- Edge cases and likely regressions"

Let's output the findings now. No file system modifications will be done since this is a read-only analysis task.
Wait, let's write out the explanation that Trellis SessionStart context is loaded in Chinese once, as instructed:
"First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly."
So I will start my answer with:
「Trellis SessionStart 上下文已載入。」

Let's write down the response.Trellis SessionStart 上下文已載入。

以下是針對「顯示奉獻的 LINE 發送結果與新增 ATM 複製按鈕」任務的完整分析報告。

---

# LINE 奉獻通知發送結果顯示與 ATM 複製按鈕實作分析報告

## 1. UX Analysis (使用者體驗分析)
* **[Info] 線上 ATM 虛擬帳號發送狀態透明化：** 目前線上捐款人在建單後若 LINE 發送成功，頁面不會有特別提示；若發送失敗，只顯示模糊的警告。讓使用者明確看見 `LINE 發送結果：成功` 或具體的 `失敗原因`，能有效消除「沒收到推播」的焦慮感，避免重複建單。
* **[Info] 一鍵複製 ATM 轉帳資訊：** 許多捐款人使用手機網銀轉帳，手動抄寫或記憶 14 碼虛擬帳號容易出錯。提供一鍵複製按鈕可以將銀行代碼、虛擬帳號、金額等關鍵資訊直接存入剪貼簿，大幅優化行動端轉帳體驗，提升付款轉換率。
* **[Info] 後台補登發送回饋：** 行政同工在後台手動輸入奉獻（補登）時，能夠直觀地在畫面上看到該筆奉獻通知是否有成功推播給會友。若顯示 `失敗 (原因：尚未綁定 LINE)`，同工可以立即得知，並在實體現場提醒會友進行綁定。

## 2. Design Evaluation (設計評估)
* **[Info] 視覺風格一致性：** 複製按鈕的樣式與配色應直接繼承頁面的 CSS 變數（如 `--theme-primary: #0f766e`），並加入微小的陰影與 Hover 動態縮放效果，使其與現有 DevExtreme 佈局無縫融入。
* **[Warning] 複製內容的純淨性：** 當使用者點擊「複製轉帳資訊」時，應只複製轉帳必要的實體欄位（姓名、金額、銀行代碼、帳號等），自動過濾掉系統在尾部附加的 `LINE 發送結果：xxx` 提示語，避免轉帳資訊夾雜無關文字。

## 3. Technical Considerations (技術考量)
* **[Critical] Partial Class 中的 Using 範疇：** 後端 `DonationPaymentProcessor` 為 `partial class`。C# 的 `using` 指令是檔案範圍（File-scoped）而非類別範圍。在 `DonationPaymentProcessor.PaymentProcessing.cs` 中引用共用 LINE 專案的 `LineMessagingProcessor.Workflows.LineNotificationException` 時，應使用完整限定名稱（Fully Qualified Name）或單獨補上 `using`，避免編譯器因找不到型別而報錯。
* **[Warning] 後台補登非同步流程的修改：** 舊的 `SendDedicationNotificationAsync` 回傳 `Task` 且內部 `catch` 直接吞掉 Exception。我們必須將其簽章重構為 `Task<string>` 以便將 LINE 的發送狀態（包含例外詳情）向上傳遞給呼叫端 `SaveKeyInDedication`，進而串接進前端顯示。

## 4. Options (方案評估與折衷)
* **方案 A：後端只回傳狀態碼，前端決定渲染文字與格式**
  * *優點：* 職責分離明確，後端不涉及 UI 標籤。
  * *缺點：* 必須改動多處前端 AJAX 回調邏輯，且 key-in 與線上捐款兩處前端實作不同，改動範圍較大。
* **方案 B：後端統一格式化 LINE 發送狀態並附加至結果訊息，前端只負責一鍵複製與顯隱控制（推薦）**
  * *優點：* 對現有 Legacy 渲染架構的侵入性最小，後端可完全控制失敗原因的格式化邏輯（藉由 `FormatLineNotificationFailureReason`），前端 key-in 視圖不需任何修改即可自動顯示發送結果。
  * *決定：* 採取 **方案 B**。

---

## 5. Required Backend Changes (後端變更)

### 檔案一：`DonationPaymentProcessor.PaymentProcessing.cs`
1. **實作錯誤原因格式化 helper：**
   ```csharp
   private static string FormatLineNotificationFailureReason(Exception ex)
   {
       if (ex == null)
       {
           return "未知錯誤";
       }
       if (ex is LineMessagingProcessor.Workflows.LineNotificationException lineEx)
       {
           var errCodeStr = string.IsNullOrEmpty(lineEx.Result.ErrorCode) ? "無" : lineEx.Result.ErrorCode;
           var errMsg = string.IsNullOrEmpty(lineEx.Message) ? "未知 LINE 錯誤" : lineEx.Message;
           return $"[{lineEx.Result.Status}] {errMsg} (錯誤碼: {errCodeStr})";
       }
       return $"[UnexpectedError] {ex.Message}";
   }
   ```
2. **修改 `TrySendAtmPaymentInstructionsAsync` 傳回發送結果：**
   * 當 `lineIds` 無有效候選時：
     `return BuildAtmNotificationWarning("LINE 發送結果：失敗 (原因：尚未綁定 LINE)。");`
   * 當發送成功時：
     `return BuildAtmNotificationWarning("LINE 發送結果：成功。");`
   * 當全部嘗試失敗時：
     `return BuildAtmNotificationWarning($"LINE 發送結果：失敗 (原因：{FormatLineNotificationFailureReason(lastException)})。");`

### 檔案二：`DonationPaymentProcessor.FeeManagement.cs`
1. **重構 `SendDedicationNotificationAsync` 返回 `Task<string>`：**
   * 尚未綁定時返回 `"失敗 (原因：尚未綁定 LINE)"`。
   * 超時 8 秒時返回 `"失敗 (原因：發送超時)"`。
   * 成功發送時返回 `"成功"`。
   * 捕捉 Exception 時返回 `$"失敗 (原因：{FormatLineNotificationFailureReason(ex)})"`。
2. **修改 `SaveKeyInDedication` 串接結果：**
   ```csharp
   var lineSendResult = await SendDedicationNotificationAsync(contact, DonationPaymentFormModel, feeId);
   // ...
   var successMessage = BuildSuccessMessage(contact, DonationPaymentFormModel);
   return successMessage + $"<br/><strong>LINE 發送結果</strong>：{lineSendResult}";
   ```

---

## 6. Required Frontend Changes (前端變更)

### 檔案三：`DonationPaymentView.cshtml`
1. **新增複製按鈕 HTML（放置於 `atm-info` 容器內）：**
   ```html
   <div id="atm-info" class="atm-info" style="display: none;">
       <h5 id="atmfield"></h5>
       <div style="margin-top: 15px; text-align: center;">
           <button id="copy-atm-btn" type="button" onclick="CopyAtmPaymentInfo()">複製轉帳資訊</button>
       </div>
   </div>
   ```
2. **新增按鈕 CSS 樣式：**
   ```css
   #copy-atm-btn {
       background-color: var(--theme-primary);
       color: white;
       border: none;
       padding: 8px 18px;
       border-radius: 8px;
       font-size: 0.95rem;
       font-weight: 600;
       cursor: pointer;
       margin-top: 10px;
       transition: background-color 0.2s, box-shadow 0.2s;
       box-shadow: 0 2px 5px rgba(15, 118, 110, 0.2);
       display: none; /* 預設隱藏 */
   }
   #copy-atm-btn:hover {
       background-color: #0d6860;
       box-shadow: 0 3px 8px rgba(15, 118, 110, 0.3);
   }
   #copy-atm-btn:active {
       background-color: #0a524b;
   }
   ```
3. **實作控制按鈕顯隱與複製的 JS 函數：**
   ```javascript
   function setAtmCopyButtonVisible(visible) {
       var btn = document.getElementById('copy-atm-btn');
       if (btn) {
           btn.style.display = visible ? 'inline-block' : 'none';
       }
   }

   function CopyAtmPaymentInfo() {
       var atmField = document.getElementById('atmfield');
       if (!atmField) return;

       var textToCopy = atmField.innerText || atmField.textContent;

       // 排除 LINE 發送結果文字，保持剪貼簿內容純淨
       var index = textToCopy.indexOf("LINE 發送結果");
       if (index === -1) index = textToCopy.indexOf("LINE 通知");
       if (index !== -1) {
           textToCopy = textToCopy.substring(0, index).trim();
       }

       if (navigator.clipboard && navigator.clipboard.writeText) {
           navigator.clipboard.writeText(textToCopy).then(function() {
               ShowToast("轉帳資訊已複製到剪貼簿", "success", 2000);
           }).catch(function() {
               fallbackCopyText(textToCopy);
           });
       } else {
           fallbackCopyText(textToCopy);
       }
   }

   function fallbackCopyText(text) {
       var textArea = document.createElement("textarea");
       textArea.value = text;
       textArea.style.position = "fixed";
       textArea.style.top = "0";
       textArea.style.left = "0";
       document.body.appendChild(textArea);
       textArea.focus();
       textArea.select();
       try {
           var successful = document.execCommand('copy');
           if (successful) {
               ShowToast("轉帳資訊已複製到剪貼簿", "success", 2000);
           } else {
               ShowToast("無法複製轉帳資訊，請手動複製", "error", 3000);
           }
       } catch (err) {
           ShowToast("無法複製轉帳資訊，請手動複製", "error", 3000);
       }
       document.body.removeChild(textArea);
   }
   ```
4. **於 AJAX `onSuccess` 與 `resetPaymentUI` 控制按鈕顯隱：**
   * 在 `onSuccess` 的 ATM 處理分支中呼叫 `setAtmCopyButtonVisible(true);`。
   * 在 `resetPaymentUI` 中呼叫 `setAtmCopyButtonVisible(false);`。
   * 在 `onSuccess` 錯誤或信用卡提示分支中呼叫 `setAtmCopyButtonVisible(false);`。

---

## 7. Required Tests (測試變更)

### 檔案四：`DonationPaymentProcessorKeyInNotificationTests.cs`
1. **修改現有測試：**
   * 修改 `TrySendAtmPaymentInstructionsAsync_uses_backup_line_id_when_primary_line_id_fails` 的斷言：
     由原先的 `warning.Should().BeEmpty()` 改為 `warning.Should().Contain("成功")`。
2. **新增測試案例驗證格式化工具：**
   * `FormatLineNotificationFailureReason_handles_LineNotificationException_correctly`：
     使用 `LineNotificationResult.Failure` 建立 `LineNotificationException`，並透過反射呼叫 `FormatLineNotificationFailureReason`，驗證輸出是否包含狀態碼與錯誤代碼。
   * `FormatLineNotificationFailureReason_handles_general_exception_correctly`：
     建立一個標準的 `new Exception("Connection refused")`，驗證輸出是否正確 Fallback 到 `[UnexpectedError] Connection refused`。
3. **新增 ATM 發送結果狀態測試案例：**
   * `TrySendAtmPaymentInstructionsAsync_returns_failure_reason_when_all_candidates_fail`：
     模擬全部候選 ID 發送失敗，驗證返回值是否包含 `"失敗"` 與具體的錯誤例外訊息。
   * `TrySendAtmPaymentInstructionsAsync_returns_failure_reason_when_no_line_id`：
     模擬傳入空的 LINE ID 清單，驗證是否返回 `"失敗 (原因：尚未綁定 LINE)"`。

---

## 8. Edge cases and likely regressions (邊界條件與潛在回歸)
* **[Critical] 剪貼簿安全性限制 (HTTPS/Fallback)：** 現代瀏覽器在非 HTTPS 環境（例如 localhost 開發時）會停用 `navigator.clipboard`。如果沒有實作 `document.execCommand('copy')` Fallback，在本機測試或非安全連線時會直接報錯，導致複製按鈕毫無反應。
* **[Warning] LINE Retry Key 唯一性：** 手動補登多次或 ATM 重複發送時，應確保 Retry Key 維持 SHA256 等冪計算，防範 LINE 重複發送推播（本專案已使用穩定的業務資料推導 Retry Key，回歸風險低）。
* **[Info] 訊息分割排除邏輯：** 當將發送狀態結果附加在顯示字串尾端時，前端複製必須藉由尋找特定關鍵字（如 `LINE 發送結果`）做 Substring 裁切。若後端顯示標籤或文字有所更動，前端的尋找關鍵字也必須同步更新。

---
SESSION_ID: a58462db-6f5a-408b-8249-f98a81c86bde

<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: RefactorPament
Git: branch Jesus_5.1.8.FabelSecurityScan; dirty 4 paths.
Current task: .trellis/tasks/06-25-payment-module-extraction; status=in_progress.
Active tasks: 3 total. Use `python ./.trellis/scripts/task.py list --mine` only if needed.
Journal: .trellis/workspace/RefactorPament/journal-1.md, 7 / 2000 lines.
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
Status: IN_PROGRESS
Task: Extract reusable payment core project
Present: prd.md, design.md, implement.md, implement.jsonl, check.jsonl
Next-Action: Follow the matching per-turn workflow-state. Implementation/check context order is jsonl entries -&gt; `prd.md` -&gt; `design.md if present` -&gt; `implement.md if present`.
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
# CCG analyzer Task: speed-up-atm-donation-submit

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport

## Request
# Task: speed up ATM donation submission

User report: ATM/匯款 donation submit shows Processing spinner too long. User asks to speed it up as much as possible.

Current branch: Jesus_5.1.8.FabelSecurityScan

Relevant code observations:
- `DonationPaymentProcessor.ProcessAtm` creates CRM fee, creates ATM virtual account through payment gateway, updates fee, builds ATM info, then waits for LINE send result before returning HTML to the browser.
- Current synchronous wait point:
  - `ProcessAtm` lines around 254-261 calls `await TrySendAtmPaymentInstructionsAsync(...)` and returns `atmInfo.HtmlMessage + notificationResult`.
  - `TrySendAtmPaymentInstructionsAsync` loops every candidate LINE ID and `await SendAtmPaymentInstructionsAsync(...)` for each until one succeeds or all fail.
- User still wants LINE send result shown to user, but the payment info must appear quickly; if LINE is slow/quota blocked, it should not keep the user on the Processing overlay for a long time.
- Prior feature added visible LINE result for ATM and key-in donations. ATM clipboard was fixed to exclude the LINE result from copied payment info.

Relevant files:
- ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs
- ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs
- ChurchReport/Models/DonationPaymentManager.cs
- ChurchReport/Views/Dedication/DonationPaymentView.cshtml
- ChurchReport.MemberInfo.Tests/Payments/DonationPaymentProcessorKeyInNotificationTests.cs

Proposed direction to analyze:
- Keep the ATM virtual account creation and CRM fee update synchronous, because the page cannot show payment info until that succeeds.
- Do not let LINE notification dominate the request duration.
- Prefer a small bounded wait for ATM LINE send, or return an explicit pending/timeout LINE result quickly while background send continues only if safe.
- Avoid fire-and-forget if it can use scoped CRM/service instances unsafely after request disposal.
- Preserve user-visible result text format and existing tests where possible.

Please analyze correctness, risk, and recommended minimal implementation. Output Critical / Warning / Info findings plus recommended patch outline.


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
- If the task cannot be completed, explain the exact blocker.Trellis SessionStart context 已載入。以下是針對「加速 ATM 奉獻送出效能」的 structured UX/UI 分析與推薦修補方案報告：

---

# 奉獻送出效能分析報告：ATM 轉帳通知效能優化 (Speed-Up ATM Donation Submit)

## 發現分類 (Findings Classification)

### 🔴 Critical (嚴重)
*   **循序同步等待 LINE API 的網路阻塞**：
    目前的 `ProcessAtm` 會在主要執行緒同步呼叫 `TrySendAtmPaymentInstructionsAsync`，該方法內會使用 `for` 迴圈循序 `await SendAtmPaymentInstructionsAsync` 發送通知至所有的候選 LINE ID（包括備用 LINE ID）。
    若主要 LINE ID 遭遇失效、封鎖、 quota 額滿或 LINE 伺服器回應緩慢，每次呼叫都可能產生數秒的延遲。這會直接延長前端網頁的 `Processing` Loading Spinner 時間，導致奉獻者體驗極差，甚至因此重試提交或關閉網頁。

### 🟡 Warning (警告)
*   **不安全的背景執行（Fire-and-Forget）風險**：
    若直接改為不等待的 `Task.Run` 進行 Fire-and-Forget 發送，當 MVC Controller 執行完畢並回傳結果後，與 HTTP 請求生命週期繫結的 Scoped 服務（例如 Dynamics CRM `OrganizationService` 或特定的 `HttpClient` 執行個體）可能會被垃圾回收器提前銷毀，導致背景任務拋出 `ObjectDisposedException` 或連線中斷。
*   **通知失敗與付款資訊呈現的相依性**：
    虛擬帳號是奉獻完成的必要資訊。我們必須確保不論 LINE 發送結果是成功、失敗、還是逾時，網頁端都必須**迅速且安全地呈現 ATM 虛擬帳號資訊**，且不會因 LINE API 異常導致整筆交易在瀏覽器上呈現失敗。

### 🟢 Info (提示)
*   **既有模式的一致性**：
    在 `DonationPaymentProcessor.FeeManagement.cs` 的 `SendDedicationNotificationAsync` 中，已經實作了基於 `Task.WhenAny` 與 `Task.Delay` 的 8 秒逾時機制。ATM 線上奉獻屬於高即時互動場景，此逾時限制應下調至約 **2.5 秒**，以保證流暢的前端體驗。

---

## 1. UX Analysis (使用者體驗分析)
*   **使用者旅程 (User Journey)**：
    奉獻者在奉獻頁面點擊「確認奉獻」$\rightarrow$ 前端顯示 Processing 遮罩 $\rightarrow$ 後台建單並發送 LINE $\rightarrow$ 遮罩消失並顯示虛擬帳號與 LINE 傳送結果。若 LINE 延遲大於 3 秒，奉獻者常會感到焦慮並懷疑交易是否成功。
*   **行動裝置與嵌入式瀏覽器體驗**：
    多數奉獻者是在 LINE 內置瀏覽器中進行操作。對於不穩定的行動網路，網頁回應時間每增加 1 秒，流失率與重複提交率便會呈指數級上升。因此限制 LINE 通知階段的最大等待時間至 2.5 秒，能顯著改善視覺上的卡頓感。

---

## 2. Design Evaluation (設計評估)
*   **與現行設計模式的一致性**：
    專案已有逾時降級邏輯（於手動登錄奉獻通知中）。針對 ATM 線上建單，採用類似的逾時提示（例如 `LINE 發送逾時，請直接保存本頁付款資訊。`），可維持整體設計系統在錯誤提示與回饋文字樣式（`BuildLineNotificationDisplayResult`）上的一致性。
*   **資訊完整度保護**：
    ATM 資訊（銀行代碼、虛擬帳號、金額）是付款的核心，而 LINE 訊息只是輔助管道。設計上應確保「核心資訊優先呈現，輔助通知限時嘗試」。

---

## 3. Technical Considerations (技術考量與架構影響)
*   **執行緒上下文與生命週期安全**：
    為了避免多執行緒上下文共用所引發的 `ObjectDisposedException`，我們**不應**使用 `Task.Run` 開闢新的 Thread Pool 執行緒，而是應該在目前的 request thread 上，將序列發送邏輯宣告為 Local 異步函式（例如 `async Task<string> SendSequenceAsync()`）。此函式在遇到第一個真正的網路 `await` 之前都會在原上下文執行，並在隨後交由異步狀態機調度，如此便能安全地共用當前的 Request 範疇資源。
*   **逾時調度**：
    使用 `Task.WhenAny(sendTask, Task.Delay(2500))` 能在 2.5 秒內強行收尾，若 `Task.Delay` 優先完成，則回傳逾時狀態的 HTML，而未完成的 `sendTask` 會在背景繼續嘗試完成發送，且因為原 Context 尚未因處置出錯而維持安全運作。

---

## 4. Options (替代方案評估)

### 方案 A：引入 2.5 秒限制的序列發送與逾時機制 (推薦)
*   **做法**：將多個 LINE ID 的發送迴圈封裝，並透過 `Task.WhenAny` 加上 `Task.Delay(2500)` 實作快速超時中斷。
*   **優點**：程式碼變動極小，完美保留既有 LINE ID 優先順序（主要 $\rightarrow$ 備份），並解決網路卡頓問題，且完全執行緒安全。
*   **缺點**：若主要 ID 卡住 2.5 秒，備援 ID 將無機會在逾時前被嘗試（但這在整體網頁反應速度與極端備援的權衡下是可接受的）。

### 方案 B：多 LINE ID 並行發送 (Parallel Dispatch)
*   **做法**：同時對所有候選 LINE ID 發起發送請求，並使用 `Task.WhenAll` 或第一個成功即返回。
*   **優點**：速度最快，不受單一 ID 卡頓影響。
*   **缺點**：容易導致奉獻者在多個綁定裝置上收到重複的通知，且 retry-key 機制在此場景下會變得複雜，容易觸發 LINE API 的重複阻擋。

---

## 5. Recommendation & Implementation Outline (推薦做法與修改大綱)

我們建議採用**方案 A（2.5 秒 Local 異步逾時）**。這是在不破壞既有測試案例、不引發線程生命週期問題下，最安全且能立即見效的修補方式。

### 建議修補程式碼大綱 (修改 `DonationPaymentProcessor.PaymentProcessing.cs`)

修改 `TrySendAtmPaymentInstructionsAsync` 如下：

```csharp
private async Task<string> TrySendAtmPaymentInstructionsAsync(
    IReadOnlyList<string> lineIds,
    string lineMessage,
    string retryKey,
    Guid contactId)
{
    // 1. 基本防錯
    if (lineIds == null || lineIds.Count == 0)
    {
        System.Diagnostics.Trace.WriteLine(
            $"[DonationPaymentProcessor] ATM LINE notification skipped because donor has no LINE id. ContactId={contactId}");
        return BuildLineNotificationDisplayResult("發送失敗", "奉獻者尚未綁定 LINE，請保存本頁付款資訊。", false);
    }

    // 2. 宣告 Local 異步任務以確保執行緒安全，不使用 Task.Run
    async Task<string> SendSequenceAsync()
    {
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

                return BuildLineNotificationDisplayResult("成功發送", "ATM/匯款付款資訊已成功發送 LINE。", true);
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
        return BuildLineNotificationDisplayResult(
            "發送失敗",
            $"LINE 通知未送出，請保存本頁付款資訊。失敗原因：{FormatLineNotificationFailureReason(lastException)}",
            false);
    }

    // 3. 實作 2.5 秒的限時等待
    var sendTask = SendSequenceAsync();
    var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(2500));
    var completedTask = await Task.WhenAny(sendTask, timeoutTask);

    if (completedTask == timeoutTask)
    {
        System.Diagnostics.Trace.WriteLine(
            $"[DonationPaymentProcessor] ATM LINE notification timed out (2.5s). ContactId={contactId}");
        
        // 逾時降級提示，引導使用者留存畫面的虛擬帳號
        return BuildLineNotificationDisplayResult(
            "發送失敗",
            "LINE 發送逾時，請直接保存本頁付款資訊。",
            false);
    }

    return await sendTask;
}
```

### 測試保護建議 (Unit Testing)
建議於 `DonationPaymentProcessorKeyInNotificationTests.cs` 中，加入一個驗證逾時降級邏輯的測試案例，確保當 LINE API 的 Mock 延遲大於 2.5 秒時，回傳的文字包含 `LINE 發送逾時` 且不阻斷交易的完成。

---
SESSION_ID: af292c97-725b-4d27-b907-964e875b1030

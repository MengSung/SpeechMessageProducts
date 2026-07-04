<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: (not initialized)
Git: branch Jesus_5.1.7.WorktreeRichMenuAddComment; dirty 4 paths.
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
# CCG analyzer Task: annotate-richmenu-cs-files

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.7.WorktreeRichMenuAddComment

## Request
We need to add detailed, complete, maintainability-focused comments to all RichMenu-related C# files in this repository.

This is a documentation-only change. Please analyze the scope and provide guidance before implementation.

Repository branch: Jesus_5.1.7.WorktreeRichMenuAddComment
Files in scope:
- LineMessagingProcessor.RichMenus/ILineRichMenuCatalog.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuAssignmentWorkflow.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuIdCache.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuProcessor.cs
- LineMessagingProcessor.RichMenus/StaticLineRichMenuCatalog.cs
- LineMessagingProcessor.RichMenus/RichMenuUserState.cs
- LineMessagingProcessor.RichMenus/RichMenuOrchestrator.cs
- LineMessagingProcessor.RichMenus/RichMenuExpirationSweepWorkflow.cs
- LineMessagingProcessor.RichMenus/RichMenuExpirationSweepReport.cs
- LineMessagingProcessor.RichMenus/RichMenuDecisionPriority.cs
- LineMessagingProcessor.RichMenus/RichMenuDecision.cs
- LineMessagingProcessor.RichMenus/RichMenuContext.cs
- LineMessagingProcessor.RichMenus/RichMenuActionFactory.cs
- LineMessagingProcessor.RichMenus/LineRichMenuWorkflow.cs
- LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerResolver.cs
- LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerPolicy.cs
- LineMessagingProcessor.RichMenus/LineRichMenuTextTriggerOptions.cs
- LineMessagingProcessor.RichMenus/LineRichMenuSyncReport.cs
- LineMessagingProcessor.RichMenus/LineRichMenuSyncOutcome.cs
- LineMessagingProcessor.RichMenus/LineRichMenuSyncItem.cs
- LineMessagingProcessor.RichMenus/LineRichMenuStatus.cs
- LineMessagingProcessor.RichMenus/LineRichMenuResult.cs
- LineMessagingProcessor.RichMenus/LineRichMenuProvisioningWorkflow.cs
- LineMessagingProcessor.RichMenus/LineRichMenuFingerprint.cs
- LineMessagingProcessor.RichMenus/LineRichMenuException.cs
- LineMessagingProcessor.RichMenus/LineRichMenuDeleteLinkedRequest.cs
- LineMessagingProcessor.RichMenus/LineRichMenuDefinition.cs
- LineMessagingProcessor.RichMenus/LineRichMenuCreateUploadAndLinkRequest.cs
- LineMessagingProcessor.RichMenus/LineRichMenuAssignmentWorkflow.cs
- LineMessagingProcessor.RichMenus/LineRichMenuAssignmentResult.cs
- LineMessagingProcessor.RichMenus/LineRichMenuAliasNotFoundException.cs
- LineMessagingProcessor.RichMenus/LineMessagingProcessorRichMenuAdapter.cs
- LineMessagingProcessor.RichMenus/IRichMenuStateStore.cs
- LineMessagingProcessor.RichMenus/IRichMenuPolicy.cs
- LineMessagingProcessor.RichMenus/IRichMenuOrchestrator.cs
- LineMessagingProcessor.RichMenus/IRichMenuExpirationSweepWorkflow.cs
- LineMessagingProcessor.RichMenus/InMemoryRichMenuStateStore.cs
- LineMessagingProcessor.RichMenus/InMemoryLineRichMenuIdCache.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuWorkflow.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuTextTriggerResolver.cs
- LineMessagingProcessor.RichMenus/ILineRichMenuProvisioningWorkflow.cs
- Line.Messaging/Messages/RichMenu/RichMenuBulkRequest.cs
- Line.Messaging/Messages/RichMenu/RichMenuBatchOperation.cs
- Line.Messaging/Messages/RichMenu/RichMenuAlias.cs
- Line.Messaging/Messages/RichMenu/RichMenu.cs
- Line.Messaging/Messages/RichMenu/ResponseRichMenu.cs
- Line.Messaging/Messages/RichMenu/ActionArea.cs
- ChurchReport/Tools/ChurchReportLegacyRichMenuCatalog.cs
- Line.Messaging/Messages/Action/RichMenuSwitchTemplateAction.cs
- LineMessagingProcessor.RichMenus.Tests/Triggers/LineRichMenuTextTriggerResolverTests.cs
- LineMessagingProcessor.RichMenus.Tests/Support/RichMenuTestFactory.cs
- LineMessagingProcessor.RichMenus.Tests/Support/CapturingRichMenuProcessor.cs
- LineMessagingProcessor.RichMenus.Tests/Provisioning/LineRichMenuProvisioningWorkflowTests.cs
- LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuOrchestratorTests.cs
- LineMessagingProcessor.RichMenus.Tests/Orchestration/RichMenuExpirationSweepWorkflowTests.cs
- LineMessagingProcessor.RichMenus.Tests/LineRichMenuWorkflowTests.cs
- LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs
- LineMessagingProcessor.RichMenus.Tests/Assignment/LineRichMenuAssignmentWorkflowTests.cs
- LineMessagingProcessor.RichMenus.Tests/Actions/RichMenuActionFactoryTests.cs


Please output:
1. Commenting strategy for production RichMenu workflow files.
2. Commenting strategy for LINE Messaging DTO/action files.
3. Commenting strategy for test/support files.
4. Any risks where comments could accidentally mislead maintainers.
5. Suggested verification after edits.


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
- If the task cannot be completed, explain the exact blocker.Trellis SessionStart 上下文已載入。

# RichMenu C# 程式碼註解增強方案分析報告

本報告針對 `Jesus_5.1.7.WorktreeRichMenuAddComment` 分支中所有 RichMenu 相關的 C# 檔案進行分析，規劃一套兼顧維護性、平台限制與開發者體驗的註解增強策略。

---

## 1. UX 分析與使用者影響 (UX Analysis)
*   **使用者體驗關聯性**：LINE RichMenu 是直接面向使用者的主要入口介面。雖然本任務是「純文件/註解變更」，但清晰的註解能確保開發人員在異動邏輯（如選單自動切換、過期清理）時，不會破壞現有的使用者引導流程。
*   **平台特性提醒**：註解中應特別標註 LINE 官方對 RichMenu 的限制（如：圖片尺寸僅支援 `2500x1686` 或 `2500x843`、區塊上限為 20 個等），避免未來前端或圖素設計變更時造成 API 錯誤，進而影響終端使用者體驗。

## 2. 設計評估與一致性 (Design Evaluation)
*   **保母級說明 (Educational Remarks) 繼承**：專案目前在 `LineRichMenuWorkflow.cs` 與 `RichMenuOrchestrator.cs` 等檔案中，已採用了 Traditional Chinese 的「保母級說明」註解模式，說明架構設計的來龍去脈與角色分工。此優良傳統應延續至其他核心工作流檔案。
*   **術語一致性**：嚴格統一 LINE 平台的專有名詞，例如：`Alias` (別名)、`Provisioning` (配置/部署)、`Assignment` (指派/連結)、`Expiration Sweep` (過期清除) 等，確保文件與 LINE 官方 API 文件無縫對接。

## 3. 技術考量與分類註解策略 (Technical Considerations)

### 3.1 生產端 RichMenu 工作流檔案的註解策略 (Production Workflows)
*   **適用範圍**：`RichMenuOrchestrator.cs`, `LineRichMenuProvisioningWorkflow.cs`, `InMemoryRichMenuIdCache.cs` 等。
*   **註解重點**：
    1.  **線程安全與並行處理 (Concurrency & Thread-Safety)**：標明哪些快取或 StateStore 實作是非線程安全的（如 `InMemoryRichMenuStateStore`），並在核心 Orchestrator 註解中說明並行指派的行為。
    2.  **生命週期與快取失效 (Lifecycle & Cache Invalidation)**：清楚描述 RichMenu ID 快取的失效機制與重新整理（Sync）的觸發時機。
    3.  **例外處理與重試機制 (Exceptions & Retries)**：說明 `LineRichMenuException`、`LineResponseException` 與網路中斷（`HttpRequestException`）的差異，以及哪些狀態（如 `ProviderUnavailable`）適合重試，哪些（如 `ValidationFailed`、`ProviderRejected`）應直接中斷。

### 3.2 LINE Messaging DTO 及 Action 檔案的註解策略 (DTO & Action Models)
*   **適用範圍**：`RichMenu.cs`, `ResponseRichMenu.cs`, `ActionArea.cs`, `RichMenuSwitchTemplateAction.cs` 等。
*   **註解重點**：
    1.  **屬性長度與格式限制**：明確在 XML Doc 中載明 LINE API 的物理限制。例如 `ChatBarText` 上限 14 個字元、`Name` 上限 300 個字元，並說明 C# `set` 存取器中的自動截斷邏輯（避免開發者疑惑為何傳入長字串卻未報錯）。
    2.  **序列化與反序列化說明**：註記這些 DTO 對應的 JSON 欄位名稱，特別是與 LINE API 規格對應的關係。

### 3.3 測試及輔助檔案的註解策略 (Test & Support Files)
*   **適用範圍**：`LineRichMenuWorkflowTests.cs`, `CapturingRichMenuProcessor.cs` 等。
*   **註解重點**：
    1.  **Mock 機制說明**：在測試輔助類別（如 `SequencedHttpMessageHandler` 的設置）上註解其如何模擬 LINE API 的連續 HTTP 響應。
    2.  **測試邊界與場景說明**：說明每個測試案例（如驗證指派順序、失敗回滾、過期掃描）所代表的業務場景，幫助維護者快速理解測試意圖。

---

## 4. 註解可能導致的維護風險 (Risks & Pitfalls)
*   **API 規格過期風險**：若 LINE 官方放寬限制（例如增加 Tap Area 上限或字數限制），寫死在程式碼註解中的限制數值可能會誤導維護者。應在註解中附上 LINE 官方開發者文件的 Link，並註記「此數值依據 LINE Messaging API 2026 年底規格，若有變動請以官方文件為準」。
*   **同步與行為漂移**：註解描述了「預期的業務邏輯」，但若日後程式碼被修改而註解未同步更新，將造成嚴重的誤導。因此，註解應聚焦於「**Why (為什麼這樣設計、要為誰服務)**」而非贅述「How (程式碼一行行在做什麼)」。

---

## 5. 建議的驗證方式 (Suggested Verification)
1.  **編譯器 XML 文件檢查**：
    *   在 `.csproj` 中啟用 `<GenerateDocumentationFile>true</GenerateDocumentationFile>`。
    *   執行 `dotnet build` 並檢查是否有 `CS1591`（漏寫 XML 註解）或 `CS1573` 等相關編譯警告，確保新加入的 XML Doc 語法正確且無遺漏。
2.  **單元測試迴歸驗證**：
    *   由於本任務為「純註解變更」，執行所有單元測試確保沒有意外修改到任何執行邏輯或屬性。
3.  **Git 差異審查 (Git Diff Check)**：
    *   執行 `git diff` 仔細確認是否**僅有**註解（`///` 或 `//`）被修改，而沒有引入任何實質程式碼邏輯的變更。

---

## 6. Trellis 任務建立確認
本項目屬於**文件與註解維護**之變更。
請問您是否同意我為此任務建立 Trellis 工作流任務並進入 Planning 階段以準備後續的註解實作？（若您同意，我將為您建立任務並生成 `prd.md` 等規劃文件；若否，我們將維持在此 Inquiry 階段。）

***
*本分析報告已完成，等待您的下一步指示。*

---
SESSION_ID: c6add6f1-7808-4f88-ba2b-d605a0f8556e

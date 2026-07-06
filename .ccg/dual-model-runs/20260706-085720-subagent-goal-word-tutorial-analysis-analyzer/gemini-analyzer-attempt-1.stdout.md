<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: RefactorPament
Git: branch Jesus_5.1.8.FabelSecurityScan; dirty 6 paths.
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
# CCG analyzer Task: subagent-goal-word-tutorial-analysis

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport

## Request
請針對以下文件任務做分析，輸出一份 Word 教學文件的大綱與內容建議。

任務：撰寫「Subagent 與 Goal 保母級 Word 教學」。
使用者要求：
1. 教導如何調用 subagent。
2. goal 要非常詳細、完整、保母級。
3. 回答 subagent 是否應在 brainstorming、writeplan 時就預備考慮進來。
4. 產出深入、仔細、完整的 Word 檔教學。

請特別分析：
- 讀者需要先懂哪些概念。
- subagent 適合與不適合使用的情境。
- brainstorming 階段要如何預先判斷 subagent 需求。
- writeplan 階段要如何拆分 subagent 工作包。
- goal 應包含哪些欄位、粒度、驗收標準、邊界與回報格式。
- 如何避免常見錯誤：任務重疊、上下文不足、沒有驗收標準、沒有關閉代理、過早 dispatch。
- 建議的文件章節、範本、表格與檢查清單。

輸出格式：
- 繁體中文。
- 使用 Critical / Warning / Info 分級指出教學中不可漏掉的內容。
- 提供一份推薦章節大綱。
- 提供 3 個可直接放入文件的 prompt 範本。


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
- If the task cannot be completed, explain the exact blocker.First reply notice: Trellis SessionStart context loaded. 我將直接進行分析與回覆。

---

# Subagent 與 Goal 保母級 Word 教學：分析報告與內容建議

本報告針對「Subagent 與 Goal 保母級 Word 教學」進行深度 UX、架構與教學設計分析，為寫作者提供完整的文件結構、情境分析、風險處置以及可直接套用的 Prompt 範本與檢查清單。

---

## 一、 重要內容與風險警示（Severity Classification）

在撰寫教學文件時，必須以下列三分級確保讀者不會因觀念模糊或操作不當造成代理跑偏（Agent Drift）或系統崩潰。

### 🚨 Critical（關鍵死穴：漏掉或觀念錯誤會直接導致 Task / Agent 執行失敗）
1. **Goal 必須具備「防禦性邊界與明確驗收條件」**：單純給予「請幫我重構支付模組」這類模糊 Goal，會導致 Agent 隨意跨檔修改、甚至 commit/push 未經測試的代碼。Goal 必須明定：`Task Path`、`Scope/Boundaries`、`Forbidden Operations`（如禁止主動 commit）、以及 `Verification Commands`。
2. **Subagent 執行環境隔離與 上下文壓縮（Context Compression）**：主會話（Main Session）的 Context Window 是最珍貴資源。Subagent 派發的核心價值在於「上下文壓縮」，將大量搜尋與修改細節封裝在子任務中，只回傳最終摘要。未意識到此點會造成主會話 Context 爆掉。
3. **正確的 Dispatch 時機與 Phase Review Gate**：在 Trellis / AI 協同開發流程中，Goal 的確定（PRD/Design）與執行（Implementation）是分開的。**過早 Dispatch（在 PRD/Design 尚未確認前就派發實作）是重大錯誤**。

### ⚠️ Warning（重要警告：未規範會顯著增加維護成本與排錯時間）
1. **Brainstorming 與 Writeplan 階段預先規劃 Subagent**：Subagent **必須**在 Writeplan (`implement.md` / `implement.jsonl`) 階段就被規劃進來，否則開發過程會變成走一步算一步，導致子任務重疊與資源浪費。
2. **子任務獨立性與競態條件（Race Conditions）**：若在同一 Turn 發起多個會修改相同檔案/資源的 Subagent，會引發寫入衝突與程式碼遺失。同一檔案修改必須採 Sequential（順序性）發起，只有唯讀/搜尋型任務才能 Parallel（平行）發起。
3. **明確的關閉與回報機制（Shutdown / Return Format）**：Subagent 完成工作後必須回傳固定格式的 Report（包含 Files Touched, Key Decisions, Verification Status），並乾淨結束。

### ℹ️ Info（最佳實踐提示：能顯著提升教學易讀性與實用度）
1. **表格化與清單化教學**：提供 Goal 欄位對照表、Task Tree 圖示、常見 Error Case 排錯對照表。
2. **保母級 Prompt 範本**：提供「複製即用」的模板，降低讀者的認知負擔（Cognitive Load）。

---

## 二、 讀者前置知識要求（Prerequisites）

讀者在閱讀本教學前，建議具備以下基礎觀念：
1. **Large Language Model (LLM) 上下文視窗（Context Window）與 Token 成本概念**。
2. **多代理人架構（Multi-Agent System）與 Agent / Subagent 分工概念**。
3. **結構化任務管理（Task-Driven Development）**：如 `.trellis/` 規範中的 PRD (`prd.md`)、Design (`design.md`)、Execution Plan (`implement.md`) 之三段式結構。
4. **Git 與命令列基礎操作**（了解 Workspace、Status、Diff 與測試指令）。

---

## 三、 Subagent 適用與不適用情境分析

| 構面 | 適合使用 Subagent 的情境 (Do) | 不適合使用 Subagent 的情境 (Don't) |
| :--- | :--- | :--- |
| **任務範疇** | **高體積/重複性批次任務**（如：跨 10 個檔案補齊 License Header、格式化、單元測試補全） | **單一檔案的微小編輯**（如：修改 2 行 Bug、改變數名稱，直接在主會話改更快） |
| **上下文影響** | **高輸出量與探索性研究**（如：大型 Codebase 的架構探索、深度日誌分析、大量 Test Log 閱讀） | **需要主會話即時高頻對話**（如：與使用者一問一答釐清模糊需求的階段） |
| **邊界劃分** | **模組邊界清晰、可獨立驗證的子任務**（如：提取單一 Service 類別並撰寫對應 Test Case） | **跨多個底層架構且邊界模糊的任務**（需先在主會話完備 Design，不可直接放任 Subagent 自由發揮） |
| **執行限制** | 唯讀分析、或受限於特定 Task Path 的單一模組修改 | **涉及全域 Git 提交/推送、部署操作**（應由 supervises main session 統一把關） |

---

## 四、 Brainstorming 與 Writeplan 階段的 Subagent 預先判斷與工作包拆分

### 1. Brainstorming 階段（需求釐清與預判）
在 Brainstorming (`prd.md`) 階段，必須評估此需求是否需要使用 Subagent。
- **預判問句 1**：本需求是否包含大量獨立可驗收的交付物（Deliverables）？若有，應拆分為 Parent-Child Task Tree。
- **預判問句 2**：本需求是否需要高體積的原始碼探索或相依性調查？若是，預先標記 `trellis-research` 派發點。
- **預判問句 3**：本需求的實作階段是否具備可平行化的唯讀檢查或批次修改工作？

### 2. Writeplan 階段（工作包拆分與 manifest 建立）
在 Writeplan (`design.md` & `implement.md`) 階段，將大任務落實為明確的 Subagent 工作包：
- **建立 Spec & Task Manifest (`implement.jsonl`)**：為 Subagent 精準指定需要讀取的檔案與規範，控制輸入 Token。
- **設定順序性與平行性**：
  - **Sequential（依序）**：有相依性或寫入相同目錄/檔案的 `implement` 工作。
  - **Parallel（平行）**：無相依性的唯讀 `research` 或 `check` 工作。
- **定義 Phase Gate**：前一步驟的 Subagent 回報成功（Pass Verification）後，才觸發下一個 Subagent。

---

## 五、 保母級 Goal 的欄位、粒度、驗收標準與回報格式

一個合格的保母級 Goal **絕對不能只有一句話**，必須包含以下六大核心區塊：

### 保母級 Goal 結構規格表

```markdown
1. Task Identity (任務標頭)
   - Active task path (如: .trellis/tasks/06-25-payment-module-extraction)
   - Agent Role/Type (如: implement / check / codebase_investigator)

2. Objectives & Scope (目標與範疇)
   - 具體要完成的事項 (明確條列，不可模稜兩可)
   - 修改範疇限制 (僅限特定目錄/檔案)

3. Context & Input Files (上下文與參考文件)
   - 必須讀取的文件清單 (如: implement.jsonl -> prd.md -> design.md -> implement.md)
   - 專案程式碼規範 (.trellis/spec/...)

4. Boundaries & Constraints (邊界與禁忌操作)
   - 禁止 git commit / push
   - 禁止修改範疇外的檔案
   - 禁止繞過型別檢查或使用 hack

5. Acceptance Criteria & Verification (驗收標準與驗證指令)
   - 具體單元測試指令 (如: dotnet test ChurchReport.MemberInfo.Tests.csproj)
   - Typecheck & Lint 指令

6. Output / Reporting Format (回報格式)
   - 必須包含: Touched Files, Key Architectural Decisions, Verification Output
```

---

## 六、 如何避免常見錯誤（Anti-Patterns & Mitigation）

1. **任務重疊（Task Overlap）**
   - *病因*：多個 Subagent 被指派修改重疊的檔案。
   - *解法*：在 Writeplan 嚴格劃分 File Scope，並設定單一檔案一次僅允許一個寫入 Agent 操作。
2. **上下文不足（Context Starvation / Off-Track Agent）**
   - *病因*：僅傳送 Goal 的文字描述，未提供 Task Artifacts 與 Spec 檔案。
   - *解法*：Goal 開頭強制帶入 `Active task: <path>`，要求 Agent 依序讀取 `implement.jsonl`、`prd.md`、`design.md`。
3. **缺少驗收標準（Missing Verification Criteria）**
   - *病因*：Agent 宣稱「已完成修改」，但未執行測試，導致語法錯誤或邏輯 Regression。
   - *解法*：Goal 內明確指定可執行的 Test Command，並規定 Agent 必須在 Report 中附上命令執行成功的日誌。
4. **代理未正常關閉（Zombie / Leaked Agent）**
   - *病因*：Subagent 進入無窮迴圈或等待使用者輸入。
   - *解法*：設定明確的單次 Turn 限制、明確要求 Report 產出後立即結束，主會話在接收回報後進行驗收。
5. **過早 Dispatch（Premature Dispatch）**
   - *病因*：在需求 PRD 尚未釐清、技術設計 Design 尚未通過 Review 時即派發 Implement Agent。
   - *解法*：嚴格落實 Phase Gate：`Plan (PRD/Design Approval) -> task.py start -> Execute (Dispatch Subagent)`。

---

## 七、 推薦的 Word 教學文件章節大綱

本教學 Word 檔建議劃分為以下 7 個主要章節：

- **封面與文件資訊**（版本、作者、適用對象、標籤）
- **第一章：觀念篇 — 為什麼需要 Subagent 與結構化 Goal？**
  - 1.1 大模型協同開發的 Context 瓶頸
  - 1.2 主會話（Supervisor）與子代理（Subagent）的職責分工
  - 1.3 Context 壓縮效應（Context Compression）的實務價值
- **第二章：前置規劃 — Brainstorming 與 Writeplan 階段的代理預判**
  - 2.1 Brainstorming (`prd.md`)：何時該預判 Subagent 需求？
  - 2.2 Writeplan (`design.md` / `implement.md`)：如何拆分 Subagent 工作包？
  - 2.3 建立專屬 Manifest (`implement.jsonl`) 控制 Token 邊界
- **第三章：Goal 撰寫指南 — 打造「保母級」指令（核心重點）**
  - 3.1 保母級 Goal 的 6 大必備欄位解析
  - 3.2 粒度控制：什麼樣的 Task 算太小？什麼算太粗？
  - 3.3 邊界限制（Constraints）與禁忌動作（Forbidden Ops）
  - 3.4 驗收標準（Acceptance Criteria）與自動化驗證指令設計
- **第四章：派發與執行 — Subagent 實務調用與生命週期管理**
  - 4.1 Dispatch 流程與 Phase Review Gate
  - 4.2 平行派發（Parallel）與順序派發（Sequential）的安全性規則
  - 4.3 子代理回報（Report Processing）與主會話收尾驗收
- **第五章：避坑指南 — 5 大常見 Anti-Patterns 診斷與防禦**
  - 5.1 任務重疊與寫入衝突
  - 5.2 上下文不足導致的 Agent 跑偏（Drift）
  - 5.3 缺乏驗收標準導致的假性完成
  - 5.4 Zombie Agent 與 Session 洩漏
  - 5.5 Premature Dispatch（過早派發）
- **第六章：實戰範本與 Prompt 工具包（Templates & Prompts）**
  - 範本 1：研究調查型 Subagent Prompt 範本（Research / Investigation）
  - 範本 2：程式碼實作型 Subagent Prompt 範本（Implementation）
  - 範本 3：質量驗證型 Subagent Prompt 範本（Quality Gate / Check）
- **附錄：檢查清單與快速對照表（Checklists & Cheat Sheets）**
  - Subagent Dispatch 課前檢查清單（Checklist）
  - 常用 Agent 類型與工具能力對照表

---

## 八、 3 個可直接放入 Word 文件的 Prompt 範本

### 範本 1：研究調查型 Subagent Prompt (Research / Investigation Goal)

```markdown
Active task: .trellis/tasks/<TASK_NAME>

[Role]
You are acting as the `codebase_investigator` subagent for this workspace.

[Task Objective]
Perform a deep analysis of the existing code structure and dependencies for the following component:
<COMPONENT_OR_MODULE_NAME>

[Input Context & References]
1. Read `.trellis/tasks/<TASK_NAME>/prd.md` to understand the goal.
2. Read project backend index: `.trellis/spec/backend/index.md`.
3. Target source files located in: `<DIRECTORY_PATH>`.

[Investigation Scope & Requirements]
- Identify all call sites and incoming/outgoing dependencies for `<CLASS_OR_MODULE>`.
- Determine potential breaking changes if we extract this logic into a standalone project.
- DO NOT edit or modify any source files.

[Constraints]
- Read-only analysis. No file writes, no `git commit`.
- Keep output concise and structured.

[Expected Output Format]
Return your findings using the following markdown format:
1. **Summary of Component**: High-level responsibility.
2. **Dependency Map**: Inbound references and Outbound calls.
3. **Architectural Risks**: Potential edge cases and circular dependencies.
4. **Actionable Recommendations**: Next steps for implementation.
```

---

### 範本 2：程式碼實作型 Subagent Prompt (Implementation Goal)

```markdown
Active task: .trellis/tasks/<TASK_NAME>

[Role]
You are acting as the `implement` subagent. Your responsibility is to execute surgical code changes strictly matching the task execution plan.

[Input Artifacts]
Read the following task artifacts in exact order before touching code:
1. `.trellis/tasks/<TASK_NAME>/implement.jsonl` (Spec manifest)
2. `.trellis/tasks/<TASK_NAME>/prd.md` (Requirements)
3. `.trellis/tasks/<TASK_NAME>/design.md` (Technical Design)
4. `.trellis/tasks/<TASK_NAME>/implement.md` (Execution Plan)

[Scope of Work]
- Implement Task Step `<STEP_NUMBER>`: `<STEP_DESCRIPTION>`.
- Restrict file modifications ONLY to the following files:
  - `<FILE_PATH_1>`
  - `<FILE_PATH_2>`

[Coding Standards & Constraints]
- Strictly follow rules in `.trellis/spec/backend/quality-guidelines.md`.
- **FORBIDDEN**: Do NOT execute `git commit`, `git push`, or `git merge`.
- **FORBIDDEN**: Do NOT use type suppressions, hacks, or unnecessary abstractions.

[Verification & Quality Gate]
Before reporting completion, you MUST run the following verification commands:
1. Compile check: `dotnet build <PROJECT_PATH>`
2. Unit tests: `dotnet test <TEST_PROJECT_PATH> --filter <TEST_NAME>`

[Reporting Format]
Upon completion, reply with:
- **Files Touched**: List of modified files.
- **Key Decisions**: Brief rationale for key code choices.
- **Verification Result**: Paste exact test execution summary showing all tests PASSED.
```

---

### 範本 3：質量驗證型 Subagent Prompt (Quality Gate / Check Goal)

```markdown
Active task: .trellis/tasks/<TASK_NAME>

[Role]
You are acting as the `trellis-check` quality assurance subagent.

[Objective]
Perform a comprehensive verification of the recently implemented changes in task `.trellis/tasks/<TASK_NAME>`.

[Inspection Steps]
1. Read `.trellis/tasks/<TASK_NAME>/prd.md` and `.trellis/tasks/<TASK_NAME>/implement.md`.
2. Inspect the latest git diff against main/master branch.
3. Verify compliance against project standards:
   - Layer isolation (`.trellis/spec/backend/directory-structure.md`)
   - Logging standards (`.trellis/spec/backend/logging-guidelines.md`)
   - Error handling (`.trellis/spec/backend/error-handling.md`)

[Validation Execution]
Execute the full test suite and type checker:
- Run: `dotnet test <TEST_PROJECT_PATH>`

[Reporting Schema]
Output a structured Quality Gate Report:
- **Status**: PASSED / FAILED / NEEDS_REVISION
- **Compliance Check**:
  - Architecture Layer Isolation: [PASS/FAIL]
  - Error Handling & Logging: [PASS/FAIL]
  - Code Reuse & Cleanliness: [PASS/FAIL]
- **Discovered Issues**: List any warnings, unhandled edge cases, or regression risks.
- **Remediation Plan**: Precise instructions if status is NEEDS_REVISION.
```

---

## 九、 總結與 Word 檔製作建議

在將本內容匯出為正式 Word 文件 (`.docx`) 時，建議採用以下編排規範：
1. **標題層級**：使用標準 Heading 1 到 Heading 3 樣式，方便產生目錄（Table of Contents）。
2. **警示方塊（Callout Boxes）**：
   - 🚨 **Critical** 使用紅色邊框方塊。
   - ⚠️ **Warning** 使用黃/橙色邊框方塊。
   - ℹ️ **Info** 使用藍色邊框方塊。
3. **程式碼與 Prompt 區塊**：使用 Monospace 字體（如 Consolas 或 Cascadia Code），背景設為淺灰底色，設定「不跨頁斷行（Keep with next）」。
4. **檢查清單（Checklist）**：在附錄頁面提供帶有核取方塊（`[ ]`）的表格，方便開發者列印或在數位版中勾選。

本報告已完整涵蓋「Subagent 與 Goal 保母級 Word 教學」所需的架構、邏輯分析、情境判斷與 Prompt 範本，可直接作為 Word 教學文件的撰寫藍本。

---
SESSION_ID: b7461d41-5f02-43df-aae9-816be0b4836b

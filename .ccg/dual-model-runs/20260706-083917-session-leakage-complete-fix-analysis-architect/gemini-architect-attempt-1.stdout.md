<hook_context>&lt;session-context&gt;
Trellis compact SessionStart context. Use it to orient the session; load details on demand.
&lt;/session-context&gt;

&lt;first-reply-notice&gt;
First visible reply: say once in Chinese that Trellis SessionStart context is loaded, then answer directly.
This notice is one-shot: do not repeat it after the first assistant reply in the same session.
&lt;/first-reply-notice&gt;

&lt;current-state&gt;
Developer: (not initialized)
Git: branch Jesus_5.1.8.WorktreeFabelSecurityScan; dirty 6 paths.
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

# Gemini Role: Frontend Architect

> For: /ccg:plan, /ccg:execute, /ccg:workflow Phase 2-3

You are a senior frontend architect specializing in UI/UX design systems, component architecture, and modern web application structure.

## CRITICAL CONSTRAINTS

- **ZERO file system write permission** - READ-ONLY sandbox
- **OUTPUT FORMAT**: Unified Diff Patch ONLY
- **NEVER** execute actual modifications

## Core Expertise

- React/Vue/Svelte component architecture and design patterns
- Design system creation (tokens, themes, variants)
- State management architecture (Redux, Zustand, Pinia)
- Micro-frontend and module federation strategies
- Performance optimization (code splitting, lazy loading)
- Accessibility architecture (WCAG 2.1 AA compliance)

## Approach

1. **Analyze First** - Understand existing patterns before proposing changes
2. **Component-Driven** - Design reusable, composable UI building blocks
3. **Scalable Structure** - Plan for growth and team collaboration
4. **Performance Budget** - Consider bundle size and runtime impact
5. **Concrete Plans** - Provide actionable implementation steps

## Output Format

```diff
--- a/src/components/Button/Button.tsx
+++ b/src/components/Button/Button.tsx
@@ -5,6 +5,10 @@ interface ButtonProps {
   children: React.ReactNode;
+  variant?: 'primary' | 'secondary' | 'danger';
+  size?: 'sm' | 'md' | 'lg';
 }
```

## Response Structure

1. **Analysis** - Current architecture assessment
2. **Architecture Decision** - Key design choices with rationale
3. **Implementation Plan** - Step-by-step with pseudo-code
4. **Considerations** - Performance, accessibility, maintainability notes

## .context Awareness

If the project has a `.context/` directory:
1. Read `.context/prefs/coding-style.md` and `.context/prefs/workflow.md` before designing
2. Follow all coding conventions defined in prefs/
3. Check `.context/history/commits.jsonl` for past architectural decisions on related components
4. In your Architecture Decision section, clearly state: rationale, rejected alternatives, assumptions, and potential side effects (these will be captured as ContextEntry for future reference)

<TASK>
# CCG architect Task: session-leakage-complete-fix-analysis

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.8.WorktreeFabelSecurityScan

## Request
Role: senior security architect and ASP.NET Core reviewer.

Task: Analyze the implementation plan at docs/superpowers/plans/2026-07-06-session-leakage-complete-fix.md before coding.

Context:
- Worktree branch: Jesus_5.1.8.WorktreeFabelSecurityScan
- Stack: ASP.NET Core MVC, legacy UseMvc / EnableEndpointRouting=false, cookie auth registered, session leakage remediation.
- Goal: implement the plan with maintainable, simple, auditable code. Prefer small shared helpers, no client-controlled identity, default-deny authorization, and explicit safe rollout controls.

Please review:
1. Any implementation risks or plan gaps.
2. Files or call paths that need special care.
3. A pragmatic order of implementation.
4. Test and verification priorities.
5. Any suggestions to keep code simple and maintainable.

Do not produce exploit payloads. Keep the output defensive and implementation-focused.


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
- If the task cannot be completed, explain the exact blocker.I will start by setting the narrative flow and reading the session leakage complete fix plan file to perform the required analysis.Trellis SessionStart 上下文已載入。

針對本專案在 `net10.0` (Legacy UseMvc, `EnableEndpointRouting = false`) 下進行的會話洩漏與身分修復實施計畫（`docs/superpowers/plans/2026-07-06-session-leakage-complete-fix.md`），我從安全架構與 ASP.NET Core 審查的角度進行了深度分析，以下為審查報告與實作建議：

---

### 一、 實作風險與計畫缺漏分析 (Implementation Risks & Plan Gaps)

#### 1. 全域拒絕授權的匿名端點遺漏風險 (Whitelisting Gaps)
*   **等級：Critical**
*   **分析：** 由於系統關閉了端點路由（Endpoint Routing），全域授權必須透過註冊 MVC `IAsyncAuthorizationFilter` 實現。啟用預設拒絕（Default-Deny）後，**任何漏掉標註 `[AllowAnonymous]` 的公開端點將全面失效**。
*   **計畫缺漏：** 計畫中雖然列出了部分 Anonymous 控制器（如 `QrCodeController`、支付回呼等），但專案中存在多個 API 控制器（如 `ShepherdMethodLookupController`, `SpiritLeaderLookupController`, `SchedulerDataController` 等），必須在實作 Task 8 時進行地毯式排查。漏掉任何一個 AJAX API 都會導致前端 DevExtreme 元件載入失敗或報 401。

#### 2. 中間件順序與驗證主體填充 (Middleware Order Mismatch)
*   **等級：Critical**
*   **分析：** 要讓 `GlobalAuthorizationFilter` 正確判斷 `User.Identity.IsAuthenticated`，以及在 `BaseChurchController` 還原身分，必須確保 `CookieAuthenticationMiddleware` 在 MVC 之前執行。
*   **計畫缺漏：** 計畫中未提及調整 `Startup.Configure` 中的 `app.UseAuthentication()` 順序。若 `app.UseAuthentication()` 被放置在 `app.UseMvc(...)` 之後，`HttpContext.User` 將永遠為空，導致全域授權過濾器攔截所有請求（包含已登入用戶）。

#### 3. 票證與會話的生命週期不一致 (Expiration Inconsistency)
*   **等級：Warning**
*   **分析：** Cookie 認證票證預設的 `ExpireTimeSpan` 為 30 分鐘，而 Session Cookie 亦有其過期機制（通常為 20 分鐘或隨設定檔變動）。
*   **計畫缺漏：** 當兩者過期時間不一致時（例如 Session 先過期，而 Auth Ticket 仍在），`BaseChurchController.EnsureCorrectUserData` 會嘗試利用 Ticket 重新建構 Session。然而，這在頻繁請求下可能引發非預期的 Session 狀態重置。應在 `Startup.cs` 中將二者的過期時間（`IdleTimeout` 與 `ExpireTimeSpan`）明確同步。

#### 4. LINE User ID 於 URL 重導向中的曝露 (Sensitive Info in URL)
*   **等級：Info**
*   **分析：** 在 `ProcessLineUserLogin` 回呼中，系統仍會重導向至 `returnUrl/{lineUserId}`。
*   **說明：** 雖然 Task 3 的 `Referrer-Policy: no-referrer` 可以防止該敏感身分識別碼透過 HTTP Referer 標頭外流給外部站點，但 LINE User ID 依然會出現在瀏覽器歷史紀錄與伺服器日誌中。這屬於殘留風險，應作為後續的優化項目（改以 Session/TempData 傳遞身分）。

---

### 二、 需特別注意的檔案與呼叫路徑 (Files & Call Paths for Special Care)

#### 1. `BaseChurchController.EnsureCorrectUserData`
*   **核心焦點：** 該方法是身分防禦的最後一道防線。重構後將不再依賴 `Referer`。
*   **安全防範：** 讀取 `HttpContext.User` 時，需加上 Null 檢查與安全轉型。此外，需注意 `InMemoryContext.ListManager.SetupListManager` 在多執行緒環境下的靜態/狀態共享問題。若有並行請求，需防範會話相互覆蓋。

#### 2. `AuthenticationController.LineLoginOAuth.cs` 中的 `ProcessLineUserLogin`
*   **核心焦點：** `returnUrl` 驗證與重導向。
*   **安全防範：** `returnUrl` 來自用戶端 Session 的暫存，容易受到操縱。必須確保 `LocalReturnUrl.IsLocal` 能百分之百防範類似 `//evil.com`、`/\evil.com`、`http:evil.com` 等精心構造的繞過字串。

---

### 三、 務實的實作順序 (Pragmatic Order of Implementation)

為保證每次提交後系統皆能正常建置且測試通過，建議採取**漸進式部署**的實作順序：

```
[階段一：基礎數據與無害修改]
 ├─ Task 1: 移除登入 JSON 的密碼/帳號回傳 (無相依性，高優先)
 ├─ Task 2: 實作 LocalReturnUrl 並保護 OAuth 流程
 └─ Task 3: 註冊全域標頭 (Referrer-Policy / X-Frame-Options)
         ▼
[階段二：簽發認證票證]
 ├─ Task 4: 實作 Claims 工廠，並於所有登入點簽發 .ChurchReport.Auth 票證
 └─ Task 5: 明確指定 Session Cookie 命名 (.ChurchReport.Session)
         ▼
[階段三：身分還原重構與登出失效]
 ├─ Task 6: 重構 BaseChurchController 的身分恢復邏輯，徹底刪除 TryGetLineUserIdFromRequest
 └─ Task 7: 加固登出功能，確認 SignOutAsync 與 Cookie 清除生效
         ▼
[階段四：全域預設拒絕授權]
 ├─ Task 8 (Canary): 註冊 GlobalAuthorizationFilter，但將配置設為 Enforce = false
 ├─ Task 8 (Whitelist): 為所有確認的公開控制器標註 [AllowAnonymous]
 └─ Task 8 (Activate): 在測試確認無誤後，將配置切換為 Enforce = true 並提交
```

---

### 四、 測試與驗證優先級 (Test & Verification Priorities)

1.  **安全性邊界測試 (Unit Tests - P0)：**
    *   **`LocalReturnUrlTests`**：必須覆蓋各種特殊字元格式，避免 Open Redirect 漏洞。
    *   **`RefererIdentityRemovedTests`**：透過反射確認 `TryGetLineUserIdFromRequest` 已被刪除，防止未來程式碼退化 (Regression)。
    *   **`LoginResponseFactoryTests`**：驗證序列化後的 JSON 字串中「絕對不含」敏感帳密。
2.  **整合授權測試 (Integration Tests - P1)：**
    *   驗證 `GlobalAuthorizationFilter` 在 `EnforceGlobalAuthorization=true` 下的行為。
    *   模擬 AJAX 請求與一般瀏覽器請求，確認未登入時分別回傳 `401 Unauthorized` 與 `302 Redirect to /Login`。
3.  **會話與 Cookie 失效手動驗證 (Manual Verification - P2)：**
    *   確認呼叫 `/Logout` 後，瀏覽器的 `.ChurchReport.Auth` 和 `.ChurchReport.Session` 被確實清除。
    *   持已登出的舊 Auth Cookie 重發請求，確認無法通過驗證。

---

### 五、 程式簡化與可維護性建議 (Suggestions for Maintainability)

1.  **統一 Claim 鍵值常數定義：**
    不要在 `BaseChurchController` 和 `LoginClaimsFactory` 中手寫常數字串（如 `"church:loginType"`）。所有的 Claims Key 必須定義在 `LoginClaimsFactory` 作為 `public const string`，由其他模組引用。
2.  **簡化登出與 Cookie 清除：**
    由於 ASP.NET Core 的 `Response.Cookies.Delete` 在部分環境下可能因為路徑或網域不同而無法成功清除 Cookie，建議在 `Logout` 中明確指定 `CookieOptions`（如與註冊時相同的 `Path`, `Domain`），以確保 Cookie 被乾淨清除。
3.  **保留緊急關閉開關 (Kill-switch)：**
    計畫中在 `appsettings.json` 引進 `EnforceGlobalAuthorization` 開關的做法非常安全。建議在生產環境上線初期將此開關保持在預備狀態，並於離峰時間啟用，以降低漏標 `[AllowAnonymous]` 造成的營運風險。

---
SESSION_ID: 0a364de7-032b-457d-a10a-17cf920e316d

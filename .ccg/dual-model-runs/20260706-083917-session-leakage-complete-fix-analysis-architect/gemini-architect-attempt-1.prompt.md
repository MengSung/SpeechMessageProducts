ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
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
- If the task cannot be completed, explain the exact blocker.

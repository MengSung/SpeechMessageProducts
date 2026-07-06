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

ROLE_FILE: <USER_PROFILE>\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-adfs-zero-leak-security

## Repository
<WORKTREE>

## Request
# Dynamics ADFS / OAuth zero-leak security review

Review commit range `6301b4f29..2eee597cb` in this repository. This is a high-risk authentication, token-lifecycle, diagnostic, Session, HTTP resource, and memory-retention change.

## Required review scope

- Read the actual diff and all directly affected production/test/config/script files.
- Verify that plaintext local ADFS access/refresh-token persistence and tracked diagnostic artifacts are completely removed without a hidden fallback.
- Verify `AdfsOAuthTokenProvider` isolation and lifecycle: one immutable profile-generation owner, cancellation-safe single-flight, no cross-profile/static token cache, bounded response reading, deterministic `HttpClient`/handler/stream/buffer/task/CTS disposal, and no retained faulted tasks or token references after drain.
- Verify ChurchReport ADFS diagnostic authorization is truly operator-only or otherwise fail-closed according to `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`, not merely authenticated-user accessible.
- Verify ADFS and LINE OAuth state is unpredictable, fresh, fixed-time compared, consumed exactly once on every terminal path, not replayable, and not retained across Session/process boundaries.
- Verify no credential, token, authorization code, refresh token, Session ID, LINE user ID, client ID, callback URI, authority/resource URI, CRM endpoint, upstream body, exception detail, or private VM/network data reaches logs, debug output, redirects, JSON artifacts, source-controlled files, or cross-request caches.
- Verify all `HttpClient`, request, response, content, streams, `ArrayPool<byte>` buffers, cancellation registrations/tokens, background tasks, timers, sockets, and process handles have explicit bounded owners and deterministic cleanup.
- Look for cross-Session or cross-user mutable state retained through `InMemoryContext`, static/singleton state, auth cookies/claims, Session, TempData, caches, or controller fields that this change creates or worsens.
- Check boundedness and performance: no per-request handler/socket-pool churn on hot production paths, no unbounded response buffering or queue/cache growth, and no weakened fail-closed behavior.
- Check tests for real behavior rather than source-string tautologies; identify missing concurrency, replay, disposal, authorization, or leak tests.
- Verify `Package01FeeReadsEnabled=false` remains unchanged as a rollout gate and Embedded, Data8, and `PowerPlatform.Dataverse.Client` remain retained as required by the current SPEC.

## Output

Return a concise report grouped as `Critical`, `Warning`, and `Info`. For every finding, cite exact file and line, explain the concrete failure mode, and propose the smallest safe fix plus the regression test that must fail before the fix. Explicitly state whether Session Leakage, Profile Leakage, Memory/Resource Leakage, credential leakage, or operator-authorization bypass remains.


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

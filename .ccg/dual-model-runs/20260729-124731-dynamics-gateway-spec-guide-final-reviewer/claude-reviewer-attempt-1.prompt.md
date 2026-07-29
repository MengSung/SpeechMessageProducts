ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-gateway-spec-guide-final

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Final documentation review: Dynamics Central/Local Gateway and CE 8.2/9.1

Perform a read-only review. Do not modify files.

## Files under review

- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/spec/backend/index.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`
- `docs/dynamics-gateway-central-local-82-91-architecture.html`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md` amendment
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md` amendment
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md` amendment

## Required review

1. Verify that Central and Local are correctly documented as deployment topologies of the existing `ExecutionMode=Gateway`, selected by endpoint, not as current enum values.
2. Verify that Embedded is retained but deferred rather than accidentally removed.
3. Verify that products share one ProductClient/REST operation contract while CE 8.2 and CE 9.1 keep separate profile generations, authentication state, clients, and transport implementations.
4. Verify that physical pools remain process/profile-generation local while aggregate admission is coordinated by physical Dynamics organization identity.
5. Verify that CE 9.1 Web API/official ServiceClient claims are conditional on actual authentication proof.
6. Verify that CE 8.2 is not incorrectly described as inherently requiring Data8, while accurately preserving the current working WS-Trust dependency.
7. Verify that the Data8 `OnPremiseClient` lifecycle issue is accurately stated: it does not implement `IDisposable`, so the existing pool's `as IDisposable` cleanup does not prove WCF channel/factory cleanup.
8. Verify that the recommended temporary Data8 worker boundary, official net48 `CrmServiceClient` worker, Web API v8.2 alternative, and removal gates are internally consistent.
9. Verify that the SPEC includes the seven required Trellis code-spec sections: Scope/Trigger, Signatures, Contracts, Validation/Error Matrix, Good/Base/Bad, Tests, Wrong vs Correct.
10. Verify that the Traditional Chinese guide captures the full decision evolution: strict no-SDK, old-vs-new concerns, third-party download/support concern, official SDK acceptance, Central target, JSON selection, Embedded-vs-Local decision, Local first, CE 8.2/9.1 compatibility, Data8 retention/removal, and Central+Local final topology.
11. Check security/isolation/resource-lifecycle correctness and contradictions with the actual repository contracts.
12. Treat the `ExecutionMode=LocalGateway` block in the SPEC's explicitly labeled **Wrong** example as intentional, not as a contract error.

Report Critical / Warning / Info findings with file and section references. If no Critical or Warning findings remain, say so explicitly.


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
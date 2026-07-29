ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-local-gateway-decision

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Review request: D365 Local Gateway vs Embedded decision visualization

Review the following design-only deliverable for technical correctness,
clarity, consistency, and misleading claims. This task does not authorize any
product-code changes.

## Deliverable

`C:/Users/Administrator/.codex/visualizations/2026/07/29/019fab98-842e-78a1-9b65-ee684c875612/dynamics-local-gateway-decision.html`

## Architecture context

- The user's primary development goal is to start ChurchReport and a localhost
  Gateway together in Visual Studio 2026 and observe/debug both.
- Production should use a centralized Gateway for five to ten products.
- Products should keep one `Gateway` execution contract; local versus central
  is selected by `Gateway.Endpoint`, not a separate `LocalGateway` enum.
- Local Gateway is a separate process and owns its own process-local SDK client,
  connection pool, credentials, health, and disposal lifecycle.
- Embedded runs inside the product process and therefore couples SDK/runtime,
  credentials, pool, health, and cleanup to every product host.
- Embedded should be deferred from the initial supported release but not
  deleted before the user approves the revised design.
- No ChurchReport or product implementation is changed in this task.

## Review criteria

Report Critical, Warning, and Info findings. Verify especially:

1. The distinction among Central Gateway, Local Gateway, and Embedded is correct.
2. The Visual Studio multiple-startup workflow is realistic.
3. The JSON examples make clear that Local and Central both use
   `ExecutionMode: Gateway` and differ only by endpoint.
4. The recommendation does not overstate that Embedded must be removed.
5. The diagram does not imply one physical connection pool can be shared across
   processes; each Gateway process owns its process-local pool while central
   coordination governs aggregate capacity.
6. The HTML fragment is accessible, theme-aware, responsive, and its scenario
   buttons work without undefined identifiers.

Return a concise review suitable for saving in the CCG task's `review.md`.


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
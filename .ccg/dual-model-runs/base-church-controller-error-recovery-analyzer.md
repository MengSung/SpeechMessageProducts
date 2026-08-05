# CCG analyzer Task: base-church-controller-error-recovery

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# BaseChurchController error-recovery analysis

Review the current `SpeechMessageProducts.ChurchReport/Controllers/BaseChurchController.cs` implementation and its MVC usage.

User-observed evidence:

- A real F5 request reached `BaseChurchController.HandleError` line 365.
- The original exception was masked by `System.NullReferenceException` on `TempData["ErrorMessage"] = exception.Message`.
- The file contains already-persisted mojibake comments (`?` replacement characters); the file bytes are valid UTF-8 without BOM and CRLF, so this is corrupted text content, not an editor encoding preference.

Constraints:

- Do not change Dynamics/CE, SQL, IIS, DNS, ADFS, IFD, CRMWeb, or Web API behavior.
- Error handling must never leak exception details, Session state, user identity, credential, token, connection, or other mutable data.
- Normal MVC non-AJAX requests should still redirect safely to `Home/DisplayErrorView` without embedding an unbounded error in route values.
- If TempData is unavailable, fail safely without masking the original exception. No process-wide/static fallback cache is acceptable.
- Newly/substantively modified C# must use deep Traditional Chinese documentation and UTF-8 without BOM, CRLF, final CRLF.
- Prefer TDD and minimal behavior change. Identify required tests and any risk to controller lifecycle/resource cleanup.

OUTPUT: Critical / Warning / Info findings, root-cause assessment, smallest safe fix design, and test plan.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
# Review - Neutral Payment DTO And QPay Name Containment Plan

Review date: 2026-07-01
Task: `qpay-model-boundary-brainstorm`
Plan: `docs/superpowers/plans/2026-07-01-neutral-payment-dto-and-qpay-name-containment.md`
Branch/worktree: `Jesus_5.1.5_Worktree_TuneRefactorPament`

## External CCG Review

The required CCG dual-model review was attempted with both configured backends through `codeagent-wrapper.exe`.

- Gemini reviewer: blocked. `codeagent-wrapper.exe --backend gemini` started, but failed with `gemini command not found in PATH`.
- Claude reviewer: blocked. `codeagent-wrapper.exe --backend claude` started, but failed with `claude command not found in PATH`.

This means the CCG external reviewer gate is not fully satisfied yet. The plan can be locally reviewed, but before treating the plan as CCG-approved, install or expose the `gemini` and `claude` commands in `PATH`, then re-run both reviewers.

## Warning Resolution Update

Updated on 2026-07-01 after the plan review warning pass.

- Resolved: the guard-test documentation now uses the deterministic historical documentation path `ChurchReport/文件` instead of a mojibake path fragment.
- Resolved: the legacy `QPay` allowance is narrowed to explicit route-template attribute lines containing `/QPayLogin` or `/QPayCard`; entire neutral controller files are no longer allowed as QPay content white-list entries.
- Resolved: the plan now states that C# class names, action method names, parameters, variables, DTOs, services, file names, and test names must use neutral payment names even when the URL route template remains legacy-compatible.
- Resolved: Task 7 now includes an explicit payment-flow regression checklist for Sinopac credit card, Sinopac ATM/virtual account, MyPay/high-grand create payload, Taishin TSPG create/return path, LINE Pay redirect, CRM payment-record update, and LINE payer notification.
- Resolved: final CCG review now uses `.ccg/tasks/qpay-model-boundary-brainstorm/base-sha.txt` as a fixed diff base instead of a brittle moving HEAD-relative range.
- Still open: the required CCG dual-model review remains blocked until `gemini` and `claude` are available in `PATH`.
- Rechecked: after the warning fixes, a second CCG reviewer attempt was made. Gemini still failed with `gemini command not found in PATH`; Claude still failed with `claude command not found in PATH`.

## 2026-07-01 Fresh Dual-Model Review Rerun

Fresh backend status:

- Claude reviewer: reachable through `codeagent-wrapper.exe --backend claude`; smoke check completed with Session-ID `193a1f8e-3d69-4a3e-9c26-60880dd8fd34`.
- Gemini reviewer: first smoke check failed because Gemini CLI rejected the untrusted headless workspace. Re-running with `GEMINI_CLI_TRUST_WORKSPACE=true` completed with Session-ID `290c4fa7-2556-4119-979f-30d01c2d65f9`.
- Conclusion: the dual-model review plumbing is operational when the Gemini trust setting is supplied or the workspace is trusted. It is not zero-config in a fresh shell unless that trust setting is preserved.

Formal plan review rerun:

- Diff base: `.ccg/tasks/qpay-model-boundary-brainstorm/base-sha.txt` = `66b8feebe87308b89978cc7ff321bff1f72b802e`.
- Review target: `docs/superpowers/plans/2026-07-01-neutral-payment-dto-and-qpay-name-containment.md` plus this task metadata.
- Gemini reviewer: completed with Session-ID `9558c978-093b-4e6c-bc92-b50e3a4e2f3b` after one transient API 500 retry. Its report repeated earlier process-blocker/warning content and is superseded where it contradicts the fresh backend evidence above.
- Claude reviewer: completed with Session-ID `4bb4537b-3815-4d10-8b11-9de59b830727` and produced actionable repo-state findings.

Consolidated disposition:

- The dual-model tooling is now callable end-to-end, but this plan is not approved as an executable implementation plan.
- Accepted Critical from Claude: Tasks 1-6 describe DTOs and neutral payment/QPay renames that already exist in the current repo under their target neutral names. Running the plan literally would duplicate or regress work already completed by earlier tasks.
- Accepted Critical from Claude: the sample `DonationPaymentFormModelMapper` shape in the plan is stale relative to the real mapper, which has provider-code mapping, `ExternalItemId`, `Explain`, recurring defaults, and structured metadata. Executing the sample literally could reintroduce payment-flow regressions.
- Accepted Warning from Claude: recording `base-sha.txt` in the last task is too late for a fixed-base review workflow; the base must be captured before implementation begins.
- Next action: retire or re-scope the plan against current HEAD before any implementation work continues.

## Critical

- Process blocker: the required CCG dual-model review could not complete because neither `gemini` nor `claude` is available in `PATH`. This is not a plan architecture defect, but it is a review-phase blocker under the repository CCG rules.

## Warning

- Superseded by the warning resolution update: the plan previously contained a mojibake path fragment in a guard-test example. It has been replaced with the deterministic historical documentation path `ChurchReport/文件`.

- The plan allows legacy `QPay` wording in old route templates, which is reasonable, but the guard tests currently allow entire neutral controller files such as `DonationPaymentLoginController.cs` and `PaymentReturnController.cs`. That is too broad. The test should allow only route attribute strings containing old URLs such as `/QPayLogin/...` or `/QPayCard/...`; it should still fail if those files contain QPay-named classes, variables, services, DTOs, or aliases.

- The plan's wording says legacy `QPay` route names are allowed as route templates or action names. For the cleanest boundary, prefer route templates only. If a C# action method name can be neutral while preserving the URL via `[Route]` / `[ActionName]`, the action method should be neutral too.

- The validation plan covers focused unit tests, build, dependency scans, and QPay containment scans, but it should explicitly include regression coverage for each payment path affected by the rename: Sinopac credit card, Sinopac ATM/virtual account, MyPay/high-grand create request, Taishin TSPG create/return path, LINE Pay selection/redirect, CRM payment-record update, and LINE payer notification. These can be unit/integration tests with mocked gateways and mocked CRM/LINE services; they do not need to call real banks.

- The plan used a moving HEAD-relative sample review diff range. That is brittle because the number of commits can change during task execution. Record a concrete base SHA before Task 1 starts, then use `git diff --no-textconv <base-sha>..HEAD` for final CCG review.

- `PaymentOrderDraft` and related DTOs are a reasonable reusable layer, but the implementation should avoid turning `Metadata` into an untyped dumping ground. Keep provider-neutral required fields as first-class properties; use metadata only for host-product data that the common payment core must carry but must not interpret.

## Info

- The plan's main boundary is sound: `SpeechMessage.Payments` remains provider protocol execution, `SpeechMessage.Payments.Workflows` receives provider-neutral draft DTOs/mappers, and ChurchReport keeps donation, MVC, CRM, LINE, and presentation behavior.

- Removing product-layer `QPay` / `Qpay` aliases is consistent with the user's goal and with Linus-style maintainability. The plan correctly prefers updating same-solution callers to neutral names instead of carrying compatibility wrapper classes.

- The decision to preserve old external URLs through route attributes, not QPay-named controllers/files/classes, is the right compromise between clean code and operational callback compatibility.

- The planned guard tests are valuable. After the warning-level tightening above, they will help prevent future regressions where MyPay, Taishin, Line Pay, or generic ChurchReport code accidentally depends on Sinopac/QPay-shaped names.

- The plan correctly keeps ASP.NET controllers, CRM updates, LINE notifications, MVC views, DevExtreme concerns, and ChurchReport donation classifications out of the reusable payment projects.

- Inline execution is appropriate for this worktree because the active mode says not to dispatch implement/check sub-agents. The plan's task-by-task tests and separate commits are also appropriate for a high-risk rename/refactor.

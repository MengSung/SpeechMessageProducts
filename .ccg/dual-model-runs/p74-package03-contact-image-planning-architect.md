# CCG architect Task: p74-package03-contact-image-planning

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
Review the proposed P7.4 Package03 contact-image read boundary planning only. It adds a separate disabled-by-default GET route and a request-local DTO-only service; it must not modify GetContactImage, enable traffic or CE, use cache/legacy fallback, or claim P7.5/P8. Assess the requirement/design/plan for security, authorization-before-dispatch, cancellation, image-byte isolation, feature-gate ordering, and missing test gates. Return Critical/Warning/Info with precise corrective advice. Do not request external secrets or suggest CE actions.

## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
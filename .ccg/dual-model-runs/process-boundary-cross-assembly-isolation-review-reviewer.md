# CCG reviewer Task: process-boundary-cross-assembly-isolation-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# CCG Reviewer Task: process-boundary-cross-assembly-isolation

## Scope

Review only the current uncommitted implementation for the active Trellis child
`.trellis/tasks/08-12-process-boundary-cross-assembly-isolation/`.

The change introduces a test-only, source-linked xUnit collection fixture that
uses a bounded `FileShare.None` lease to serialize `WorkerTestHost`
process-boundary tests across the Dynamics and ChurchReport test assemblies.
It preserves the disabled ChurchReport process/listener/cleanup assertions; it
does not perform CE I/O, alter a feature flag, alter a deployment, use a
credential, or change P7.2 Slice C evidence.

## Requirements to verify

1. Same worktree testhost processes acquire one interprocess lease, but unrelated
   worktrees do not contend. The path partition must not expose the worktree path.
2. The wait is bounded; only Windows sharing/lock violations may be retried.
   Other I/O failures must fail closed without being recategorized as a timeout.
3. The `FileStream` has a single deterministic owner, no static retained handle,
   and OS cleanup covers aborted testhosts.
4. The shared source is linked only into test assemblies and every relevant
   `WorkerTestHost` producer / zero-worker observer uses the same collection.
5. No assertion is weakened and no cross-user, cross-profile, credential,
   process, or resource-leak path is introduced.
6. Focus on correctness/security/lifecycle. Report only Critical, Warning, Info.
   Do not recommend CE writes, gateway deployment, feature-flag rollout, P7.2
   historical-cycle retry, P7.3+, commit, push, or unrelated refactors.

## Review targets

- `TestInfrastructure/WorkerTestHostProcessBoundaryCollection.cs`
- `SpeechMessage.Dynamics.Tests/WorkerTestHostProcessBoundaryLeaseTests.cs`
- `SpeechMessage.Dynamics.Tests/SpeechMessage.Dynamics.Tests.csproj`
- `ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj`
- `SpeechMessage.Dynamics.Tests/*ProcessBoundaryTests.cs`
- `SpeechMessage.Dynamics.Tests/OfficialWorker*Tests.cs`
- `ChurchReport.MemberInfo.Tests/FeatureDisabledDynamicsProcessBoundaryTests.cs`
- `.trellis/tasks/08-12-process-boundary-cross-assembly-isolation/`

## Available local evidence

- Targeted Dynamics command: 7 passed.
- Targeted ChurchReport command: 1 passed.
- Prior full solution test: ChurchReport 528 passed / 14 skipped; Dynamics
  664 passed / 7 skipped. A fresh full gate will run after this review.

## Output

Give a concise Critical / Warning / Info report with file and line references.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.

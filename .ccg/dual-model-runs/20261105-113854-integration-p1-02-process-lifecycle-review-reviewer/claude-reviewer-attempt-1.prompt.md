ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: integration-p1-02-process-lifecycle-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# P1-02 Process Lifecycle Review

Review the current working-tree changes for the P1-02 architecture-test package. This is a read-only code review; do not edit files or run commands that contact external services.

## Files in Scope

- `tests-shared/ArchitectureTests/ArchitectureProjectReader.cs`
- `tests-shared/ArchitectureTests/BoundedProcessRunner.cs` (new, untracked at packet creation)
- `tests-shared/ArchitectureTests/ArchitectureProcessRunnerTests.cs` (new, untracked at packet creation)
- `ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs`
- `tools/RepoQuality/Tests/Test-ProgressPage.cjs`
- `文件/規劃設計書/實作計畫-3.3/實作進度.html`

Inspect the actual current files, including the two new untracked C# files. Do not rely only on `git diff`, which may omit untracked content.

## Review Questions

1. Does the runner enforce one total execution-and-drain deadline, continuously drain stdout and stderr without unbounded retained memory, and reject truncated MSBuild JSON?
2. Are timeout, process-tree termination, root-process reaping, stream disposal, and reader-task cleanup bounded and accurately described? In particular, distinguish a successful request to terminate descendants from proof that descendants exited; `Process.WaitForExit` observes the root process. The production call is `dotnet msbuild` evaluation only, with no targets executed, `-maxcpucount:1`, and `-nodeReuse:false`.
3. Can cleanup failures mask the primary failure, leave readers or process handles retained, or accidentally return success after incomplete cleanup?
4. Is the process invocation shell-free and free of sensitive output disclosure? Are arguments, paths, and imported project evaluation handled safely?
5. Does `QueryFixture.LastQuery` use safe publication, and is the concurrent test valid without asserting a nondeterministic winner?
6. Are the new C# comments complete and accurate under the repository's Traditional Chinese XML documentation and lifecycle rules? Do not request unrelated refactors.

## Local Evidence Already Collected

- Architecture and evaluated-project tests: 67 passed, 0 failed on `net10.0` after adding the single-node and no-node-reuse MSBuild options.
- Bounded subprocess fault-injection tests: 2 passed, 0 failed; one used a 500ms deadline, and one produced about 2MB on each redirected pipe while retaining at most 4,096 characters per pipe.
- Query performance suite: 16 passed, 0 failed on `net10.0`, including the 64-request concurrent fixture case.
- Progress page regression: 4 passed, 0 failed.
- Changed-scope text encoding check: 3 tracked files checked, 0 issues; byte-level checks also passed for all modified/new source, progress, and ledger files.

## Output

Return findings only, ordered as Critical, Warning, Info. Each finding must name the file and line, describe a concrete failure mode, and state the smallest correction. If there are no findings, say so and list any residual verification gap. Do not treat the whole P1 stage as complete; only this P1-02 code slice is under review.


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
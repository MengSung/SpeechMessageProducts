ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: p013-net462-migration-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
# P0-13 net462 migration analysis

Review the existing uncommitted net462 migration in this repository before further edits. This is read-only analysis; do not modify files and do not connect to D365 or LINE.

The user has formally approved changing sandbox-bound assemblies to .NET Framework 4.6.2 after a D365 9.1 pluginassembly Create failure reported from `TargetFrameworkVersionValidator.ValidateInternal()` with `System.FormatException`. Treat the framework mismatch as a high-confidence hypothesis, not as proven until the user retries registration. The server's observed `mscorlib` file version remains 4.8.x and is a runtime observation, not the plugin target.

Inspect the current diff and relevant project/test/build files. Verify:

- Plugins is net462; Core and Localization are net462;net10.0; existing test-runner targets may remain net48.
- Framework-only conditional compilation uses a symbol that is defined for net462, and tests prove the actual referenced product assemblies and Framework branch.
- `Test-PluginAssembly.ps1` accepts only net462; a real net48 fixture is rejected; assembly version, strong name, allowed references, noDrawing, diagnostic type, and size checks remain intact.
- All four ILRepack inputs are checked before merge and cannot declare a target above v4.6.2.
- The nested PluginPackaging helper can restore and build when its own assets are absent; identify a focused regression proof.
- For each net462 product target, identify resolved NuGet compile/runtime assets and any netstandard/facade implications. No facade may be added to the merge input list.
- Identify stale active documentation/spec statements that require synchronization, while retaining historical reports and user commits as history.
- Report concrete risks, missing tests, and recommended changes, separating confirmed facts from the D365 hypothesis.

Do not inspect secret values, key files, license contents, or live environment credentials. Do not claim that offline tests establish D365 registration success. Output concise findings ordered by severity with file paths and line references.


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
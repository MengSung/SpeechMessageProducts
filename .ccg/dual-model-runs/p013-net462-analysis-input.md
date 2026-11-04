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

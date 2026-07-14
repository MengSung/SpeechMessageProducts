# F01D Runtime Validation Plan

Status: COMPLETE
Runtime-pending confirmed issues: 0
Mode: DIAGNOSIS_ONLY

## Approval Effect

No retained issue depends exclusively on runtime evidence. Target declarations,
project references, warning suppression, coverage packages/commands, solution
enrollment, and the test-container graph are statically established.

The commands below are future acceptance tests only. They must run in a
disposable clean clone or approved CI branch after optimization is authorized.
This diagnostic and its CCG reviewer must not execute them because they create
or update restore, build, test, coverage, and cache artifacts.

## F01D-EXT-001 Acceptance

Executor: future F01D implementation task with F01A integration.

Environment:

- approved .NET 10 SDK;
- clean clone with empty project `bin/**` and `obj/**`;
- F01A-approved solution enrollment/workflow.

Commands:

```powershell
dotnet restore ToolUtility.Tests/ToolUtility.Tests.csproj
dotnet build ToolUtility.Tests/ToolUtility.Tests.csproj --no-restore --configuration Release
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj --no-build --configuration Release
```

Pass threshold:

- all commands exit `0`;
- no `NU1201`;
- project enrollment matches the documented F01A decision;
- F03A/F03B owner tests execute from a clean runner.

Failure effect: keep F01D-EXT-001 and the ToolUtility gate blocked.

## F01D-SEC-001 Acceptance

Executor: future F01D task with the package-edge owner.

Method:

1. Remove blanket `NU1605` suppression in a clean branch.
2. Restore/build the shared ChurchReport test project.
3. Record every downgrade edge without exposing credentials or private feeds.
4. Reconcile package versions or approve a narrowly scoped temporary exception.
5. Add a negative fixture proving a new downgrade fails the gate.

Pass threshold:

- no project-wide `NU1605` suppression;
- no unexplained package downgrade;
- any temporary exception names an owner, package edge, expiry, and test.

Failure effect: keep F01D-SEC-001.

## F01D-PERF-001 Acceptance

Executor: future F01D/F01A integration task with subject owners.

Method:

1. Capture three clean-run samples for the current shared project:
   restore, build, discovery, and selected-test execution.
2. Create one focused owner gate or equivalent independently buildable module.
3. Repeat the same measurements with warm and cold package caches.
4. Verify unrelated subject compile failures do not block the focused gate.

Pass threshold:

- focused gate has a smaller declared project/test graph;
- median build-plus-discovery time improves materially, target at least 20%;
- owner-specific and integration tests remain separately executable;
- F01A provider/consumer checks remain complete.

Failure effect:

- retain the structural issue;
- do not split projects if the gate loses contract coverage or produces no
  material isolation benefit.

## F01D-PERF-002 Acceptance

Executor: future F01D task.

Method:

1. Remove `coverlet.msbuild`.
2. Run the existing collector command in a clean clone.
3. Compare restore graph, build log, and coverage output with the baseline.

Pass threshold:

- coverage output remains equivalent;
- no tracked consumer requires MSBuild coverage;
- one documented coverage mechanism remains.

Failure effect: restore the package and document its explicit consumer.

## Rejected Candidate Validation

- Test SDK compatibility: run a clean discovery-only baseline under the
  approved SDK; promote a new issue only on a reproducible SDK/adapter failure.
- Configuration output leakage: inspect a controlled test publish/output
  manifest with fake configuration; promote only if sensitive host content is
  copied into a distributable test artifact.
- Parallel isolation: add a deliberately mutating fake fixture and verify the
  future harness serializes or scopes it correctly.

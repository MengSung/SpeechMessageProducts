# F01A Runtime Validation Plan

Status: COMPLETE
Runtime-pending confirmed issues: 0
Mode: DIAGNOSIS_ONLY

## Approval Effect

No retained issue depends exclusively on runtime evidence. The current findings
are established by tracked workflow syntax, Git metadata, solution enrollment,
target-framework declarations, and canonical project definitions. Therefore
this plan does not set `RUNTIME_VALIDATION_PENDING`.

The commands below are acceptance tests for a later optimization phase. They
must be run by the owning implementation task in a disposable clean clone or
approved CI branch because restore/build/test commands write outside this
diagnostic agent's documentation-only scope.

## F01A-SEC-001 Validation

Environment:

- GitHub.com test branch with read-only `contents` permission.
- Supported, SHA-pinned action revisions.
- Exact ReportGenerator version restored from an approved source.

Method:

1. Run the workflow on a no-op ToolUtility change.
2. Record resolved action SHAs and tool version.
3. Prove `actions/upload-artifact` completes on GitHub.com.
4. Verify no action uses a mutable tag and no executable tool floats.

Pass threshold:

- All executable dependencies resolve to reviewed immutable identifiers.
- Workflow completes without unsupported-action errors.

Failure effect:

- Keep F01A-SEC-001 open.

## F01A-SEC-002 Validation

Environment:

- Security owner with access to release/package/deployment inventories.
- Do not print or upload private key contents.

Method:

1. Derive public key tokens locally without exposing private blobs.
2. Search packages, release assets, deployment manifests, and consumers for
   those tokens.
3. Classify each key as public test material, retired identity, or retained
   signing identity.
4. After owner remediation, verify no private `.snk` remains tracked and a
   prevention test rejects a new private-key blob.

Pass threshold:

- Every key has a documented lifecycle decision.
- Retained identities are rotated and private material is outside Git.
- Prevention detects recurrence.

Failure effect:

- Keep F01A-SEC-002 and escalate affected owner handoffs.

## F01A-EXT-001 Validation

Environment:

- Clean clone with the repository's supported .NET 10 SDK.
- F01D-approved ToolUtility test-container target.

Commands:

```powershell
dotnet restore ToolUtility.Tests/ToolUtility.Tests.csproj
dotnet build ToolUtility.Tests/ToolUtility.Tests.csproj --no-restore --configuration Release
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj --no-build --configuration Release
dotnet sln SpeechMessageProducts.sln list
```

Pass threshold:

- Restore, build, and test exit `0`.
- Enrollment matches the documented F01A decision.
- The CI job is green from a clean runner.

Failure effect:

- Keep F01A-EXT-001; do not approve ToolUtility optimization.

## F01A-EXT-002 Validation

Environment:

- Test pull requests changing only:
  `SpeechMessageProducts.sln`, `Line.Messaging/**`,
  `PowerPlatform.Dataverse.Client/**`, and one unrelated enrolled module.

Method:

1. Verify solution changes schedule a solution validation gate.
2. Verify each provider-only change schedules its required consumer gate.
3. Verify unrelated paths do not trigger every expensive test suite.
4. Confirm required checks report a stable name suitable for branch protection.

Pass threshold:

- Every provider/consumer edge in the approved matrix has an executable gate.
- No enrolled project is invisible to tracked CI.

Failure effect:

- Keep F01A-EXT-002 and the affected module gate blocked.

## F01A-EXT-003 Validation

Environment:

- F01A canonical-project registry plus owner decisions from F04/F05A/F08/F02/X02Q.

Method:

1. Enumerate all tracked `.csproj` files and compare them with the solution and
   registry.
2. Fail on an unregistered project definition.
3. For retained alternates, build the declared target and compare the intended
   output/contract.
4. Confirm removed projects have no script, package, or external consumer.

Pass threshold:

- Every `.csproj` is enrolled, intentionally non-enrolled, or retired with one
  lifecycle owner and one canonical decision.

Failure effect:

- Keep F01A-EXT-003; do not delete an alternate project without consumer proof.

## Solution Matrix Performance Candidate

The rejected performance candidate can become an issue only if timing and
consumer evidence are collected:

- Measure Visual Studio/CLI solution evaluation for the current 15
  configuration/platform combinations.
- Identify consumers of `Debug_LearnCrm`, `DebugOracleConnector`, x86/x64, and
  `Test_Exchange_Service`.
- Promote only if removal has a measurable benefit and no required consumer.

Executor: future F01A optimization task with owner-approved clean-clone writes.

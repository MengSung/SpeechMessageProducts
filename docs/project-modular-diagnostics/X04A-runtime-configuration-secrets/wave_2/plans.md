# Wave 2 Revision 2 Implementation Contract: X04A Residual Secrets

- Wave: Wave 2
- Revision: 2
- Workspace: `X04A-runtime-configuration-secrets`
- Canonical issue: `X04A-SEC-001`
- Regression-only issues: `X04A-SEC-002`, `X04A-PERF-001`
- Contract status: `CONTRACT_STATUS: CONTRACT_REVISION_APPROVED_DEGRADED`
- Owner approval: 2026-07-18
- Design authority: `../revision-2-design.md`

Review evidence: Claude-only run
`20260718-102103-x04a-revision2-contract-reviewer` completed two healthy
attempts with no usable output and is not external approval. The owner approved
the Revision 2 design, and the active inline platform audit found no unresolved
Critical or Warning: baselines were reproduced as original tests 2/2 passing,
comments=3, aliases=6, and durable-artifact literal leaks=0.

Revision 2 closes a completion-evidence gap found after Revision 1 commit
`ab9993e8`. It does not reopen the host bridge, Production validator, or 13
consumer migration.

## Exact Write Allowlist

Product and test writes are limited to:

- `SpeechMessageProducts.ChurchReport/appsettings.json`
- `ChurchReport.MemberInfo.Tests/Configuration/RuntimeConfigurationSecretScanTests.cs`

Planning and redacted evidence may update only:

- this workspace's `wave_2/plans.md`;
- this workspace's `wave_2/measurements.md`;
- this workspace's `wave_2/goals.md`;
- `docs/project-modular-diagnostics/optimization-blueprint.md`;
- `docs/project-modular-diagnostics/optimization-blueprint-implementation-plan.md`;
- `.ccg/tasks/x04a-revision2-secret-scan-closure/**`;
- `.ccg/tasks/optimization-blueprint-workflow/**`.

## Explicit Exclusions

- No change to `Program.cs`, `appsettings.Production.json`, runtime bridge,
  safety validator, payment/LINE/QR consumers, project files, deployment files,
  environment configuration, or any other module.
- No new canonical issue or Wave 2 member.
- No credential rotation or Production deployment claim.
- No secret value in source outside managed runtime injection, and no value in
  tests, task records, measurements, logs, prompts, or review output.

## Required Sequence

### 1. Capture Redacted Baselines

Run the revised scanner tests before changing configuration. Required baseline:

```text
OriginalManifestLiteralCount=0/21
LegacyAliasLiteralCount=6/6
CommentedSensitiveLiteralCount=3
```

Only counts, paths, key names, line numbers, and categories may be recorded.

### 2. Add Test-First Scanner Coverage

Keep the original ordered 21-key manifest unchanged. Add the six exact legacy
aliases defined in `measurements.md`. Add raw-source scanning for commented
sensitive-key assignments with non-empty quoted values.

Synthetic fixtures must prove the scanner detects each class while returning
no matched value. The pre-repair repository test must fail only for the six
legacy aliases and three commented assignments.

### 3. Remove Residual Literals

- Clear the six legacy Sandbox values without deleting their keys or section.
- Remove the three commented sensitive assignments completely.
- Preserve Sandbox endpoint configuration and every non-secret setting.
- Do not replace removed values with placeholders, encodings, examples, or
  alternate credentials.

### 4. Verify Regression Boundaries

The original manifest remains `0/21`. The existing bridge, consumer, and
Production validator tests must remain green. No Revision 1 product path may
change.

## Required Commands

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~RuntimeConfigurationSecretScanTests" --no-restore
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~RuntimeConfigurationBridgeTests|FullyQualifiedName~RuntimeConfigurationConsumerSourceContractTests|FullyQualifiedName~RuntimeConfigurationSecretScanTests|FullyQualifiedName~RuntimeConfigurationSafetyValidatorTests" --no-restore
dotnet build .\SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj --no-restore --nologo
git diff --check
git diff --name-only
```

Success requires focused tests passing, build exit 0, no whitespace errors, and
exactly the two allowed product/test paths in the repair diff.

## Review And Commit Gate

Run Claude-only review through the project self-healing entrypoint. Claude
findings must be resolved and verification repeated. If Claude produces no
usable output, record that truthfully and use only the review mechanism allowed
by the active execution platform; never invoke Gemini and never call a no-output
run an external approval.

The repair is one Traditional Chinese commit whose body records:

```text
波次: Wave 2 / X04A Revision 2
議題: X04A-SEC-001
量測: 0/21 maintained; 6/6 -> 0/6; comments 3 -> 0
驗證: focused tests, build, diff and allowlist
審核: Claude result or truthful degraded local gate
回退: Revision 2 two-path repair commit without restoring literals
```

## Rollback

The Revision 2 repair commit is the rollback unit. Rollback must never restore
removed credential literals. Restore only non-secret metadata if required and
continue using managed external configuration.

## Revision 1 Archive

Revision 1 commit `ab9993e8` remains the authority for `X04A-SEC-002` and
`X04A-PERF-001`: original manifest `0/21`, Production controls `8/8` safe,
legacy builders `0/13`, bridge consumers `13/13`, lifecycle `4/4`, focused
tests `33/33`, and build 0 errors. Revision 2 neither reuses those paths as a
write scope nor weakens those targets.

# Wave 2 修訂實施合同：X04A Runtime Configuration And Secrets

- Wave: Wave 2
- Revision: 1
- Workspace: `X04A-runtime-configuration-secrets`
- Worktree: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`
- Canonical issues: `X04A-SEC-001`, `X04A-SEC-002`, `X04A-PERF-001`
- Contract status: `CONTRACT_STATUS: CONTRACT_REVISION_APPROVED_DEGRADED`
- Design authority: `../revision-1-design.md`
- Consumer inventory: `.ccg/tasks/x04a-safe-configuration-compatibility/consumer-configuration-inventory.md`

This Revision 1 contract supersedes the prior X04A Wave 2 repair boundary only
after it receives the required review approval. Until the status becomes
`CONTRACT_REVISION_APPROVED`, no product source, test, or runtime configuration
may be modified under this contract.

## Scope And Ownership

`X04A-SEC-001` and `X04A-SEC-002` remain the P0 outcomes. `X04A-PERF-001` is
now an explicit prerequisite because the 13 listed runtime consumers bypass the
host configuration lifecycle. The repair must establish one host-owned effective
configuration path before clearing committed secret literals.

The repair does not claim that deployment secrets exist or that exposed
credentials have been rotated. Those remain external deployment-owner actions.

## Product Allowlist

The repair may create or modify only these product, configuration, and test
paths:

- `SpeechMessageProducts.ChurchReport/Program.cs`
- `SpeechMessageProducts.ChurchReport/appsettings.json`
- `SpeechMessageProducts.ChurchReport/appsettings.Production.json`
- `SpeechMessageProducts.ChurchReport/Configuration/RuntimeConfigurationBridge.cs` (new)
- `SpeechMessageProducts.ChurchReport/Configuration/RuntimeConfigurationSafetyValidator.cs` (new)
- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
- `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs`
- `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs`
- `SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs`
- `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs`
- `SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs`
- `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs`
- `SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs`
- `ChurchReport.MemberInfo.Tests/Configuration/RuntimeConfigurationBridgeTests.cs` (new)
- `ChurchReport.MemberInfo.Tests/Configuration/RuntimeConfigurationConsumerSourceContractTests.cs` (new)
- `ChurchReport.MemberInfo.Tests/Configuration/RuntimeConfigurationSecretScanTests.cs` (new)
- `ChurchReport.MemberInfo.Tests/Configuration/RuntimeConfigurationSafetyValidatorTests.cs` (new)

The repair may append redacted runtime evidence only to this workspace's
`wave_2/{plans,measurements,goals}.md`. It must not alter the contract targets,
allowlist, exclusions, or rollback rules.

## Explicit Exclusions

- `X04A-SEC-003` and `X04A-EXT-001` remain out of scope.
- Full constructor injection, typed options migration, controller/call-site
  refactoring, and changes to `ToolUtilityFactory` are out of scope. The bridge
  is a compatibility boundary, not a DI redesign.
- `appsettings.Development.json`, `web.config`, all `.csproj` files, solution
  files, deployment scripts, CI, and other configuration consumers are out of
  scope.
- Credential rotation, secret-store provisioning, IIS/cloud environment setup,
  and live LINE/CRM/payment verification are deployment-owner work.
- Any consumer not in the frozen 13-path inventory is out of scope. Discovery
  of another production `ConfigurationBuilder` requires a new contract review.

## Required Repair Sequence

### 1. Capture the three frozen baselines

Before modifying product files, run the tests described in `measurements.md`
and record only redacted counts:

- `SecretLiteralCount=21/21` for the exact sensitive-key manifest.
- `UnsafeOrInheritedConditionCount=8/8`, `SafeEffectiveConditionCount=0/8`,
  and `ProductionOverlayPresenceCount=0/8`.
- `AdHocConfigurationBuilderConsumerCount=13/13` and
  `BridgeConsumerCount=0/13` for the exact consumer inventory.

If a baseline differs, stop and return the contract to planning; do not weaken
the target to fit the observed repository state.

### 2. Establish the host-owned compatibility bridge

Create `RuntimeConfigurationBridge` as the sole compatibility source for the
listed legacy consumers.

- It accepts the `IConfiguration` created by `WebApplication.CreateBuilder`.
- It has no JSON file provider, environment probing, reload subscription, or
  fallback builder.
- It initializes once. A different second initialization fails with a
  value-free error instead of replacing the effective configuration.
- Access before initialization fails closed with a stable, value-free error.
- It exposes only the effective configuration needed by legacy callers.

In `Program.Main`, build the host configuration, perform the Production safety
validation when appropriate, initialize the bridge with the validated
`builder.Configuration`, and only then construct `Startup` or register services.

### 3. Migrate every frozen legacy consumer

For each of the 13 allowlisted consumer files, remove its local
`ConfigurationBuilder`, `AddJsonFile("appsettings.json")`, and local static/lazy
configuration cache. Replace the configuration access with the bridge's
effective host configuration while preserving the current key paths, default
organization behavior, public constructors, direct legacy `new` call sites,
and business-flow behavior.

The source contract test must prove all 13 paths use the bridge and none owns
an ad-hoc builder. It is not sufficient to add environment variables to each
old builder.

### 4. Remove committed literals and enforce Production safety

For the frozen 21-key manifest:

1. Clear non-empty committed secret literals in `appsettings.json` without
   deleting sections, key paths, non-secret metadata, or endpoint configuration.
   Deployment values use normal .NET hierarchical environment-variable names
   (`__` in place of `:`); no real value may appear in source, tests, evidence,
   or review prompts.
2. Add explicit safe Production values for the eight frozen controls in
   `appsettings.Production.json`; do not put secrets in this file.
3. Implement `RuntimeConfigurationSafetyValidator` over the host effective
   configuration. In Production it must reject missing/placeholder sensitive
   values and every unsafe control; outside Production it must not enforce the
   Production gate. `Cash_Environment` is accepted only as an explicit positive
   Production classification (`Production` or the existing `正式環境` label),
   never merely because it lacks a test/sandbox substring. The known placeholder
   detector must reject case-insensitive values containing `placeholder`,
   `replace`, `runtime_secret`, `your_`, `_here`, `todo`, `dummy`, `example`,
   `sample`, or `changeme`; errors still omit values.
4. Validator errors may contain a key name and failure category only. They may
   never contain an effective configuration value.

### 5. Test and validate

Use synthetic in-memory values only. The test suite must cover:

- bridge uninitialized failure, successful initialization, and rejection of a
  different second initialization;
- an effective higher-priority synthetic overlay value is visible through the
  bridge, proving the bridge reads the host result instead of reconstructing a
  base-only source;
- all 13 source paths with zero local builders and 13 bridge consumers;
- the exact 21-key secret scanner baseline and final `0/21` result;
- eight Production overlay/control cases, safe Production, missing secret,
  placeholder secret, and Development bypass;
- no secret value in scanner, validator, or bridge failure output.

Run the focused test command, the ChurchReport build, `git diff --check`, and
the allowlist path check. Do not contact external services or claim deployment
secret availability.

## Required Local Verification

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~RuntimeConfigurationBridgeTests|FullyQualifiedName~RuntimeConfigurationConsumerSourceContractTests|FullyQualifiedName~RuntimeConfigurationSecretScanTests|FullyQualifiedName~RuntimeConfigurationSafetyValidatorTests" --no-restore
```

Expected result: every focused test passes. Output contains test names and
redacted counts only.

```powershell
dotnet build .\SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj --no-restore
git diff --check
git diff --name-only
```

Expected result: build succeeds with no new compiler errors; no whitespace
errors; and every changed product/test/configuration path is in this allowlist.

## Review And Commit Gate

The repair may commit only after all frozen measurements and goals pass and the
Wave diff/evidence review is approved. Claude is the only external reviewer.
If its self-healing runner produces no usable output, the controller performs
one read-only Codex fallback review. Gemini is not permitted.

The repair commit subject and body must be Traditional Chinese and include:

```text
波次: Wave 2 / X04A revision 1
Issue: X04A-SEC-001、X04A-SEC-002、X04A-PERF-001
量測: 21/21 -> 0/21；8/8 unsafe -> 0/8；13/13 builders -> 0/13
驗證: focused tests、build、diff check
審核: Claude 或 Codex fallback evidence
回退: 本合同 allowlist 的單一修復 commit
```

## Rollback Boundary

One X04A repair commit is the rollback unit. Reverting it restores the prior
legacy configuration access behavior but never reintroduces secret literals to
committed configuration. A deployment owner must keep required values in a
managed external configuration source during rollback.

## Execution Evidence

- UTC: `2026-07-18T00:22:14Z`
- Baseline: focused X04A suite reported `29 passed, 4 failed` exactly for the
  unresolved `21/21` committed literals, `0/8` Production controls, and
  `13/13` legacy consumer builder/bridge contracts.
- Repair result: the same focused suite reported `33 passed, 0 failed`.
- Build: `dotnet build` for ChurchReport completed with `0 warnings, 0 errors`.
- Scope: `git diff --check` was clean; `22` changed product/test/configuration
  paths were all in this Revision 1 allowlist and `0` paths were outside it.
- Secret handling: evidence records counts and key categories only; neither a
  command output nor a review artifact contains an old or effective secret value.
- Review: Claude-only self-healing run
  `20260718-082348-x04a-wave2-revision1-final-reviewer` produced no usable
  output after two healthy attempts; the documented inline Codex fallback found
  no Critical or Warning issue.
- Commit: `ab9993e8` (`fix: 修復 X04A 執行期設定相容性與安全閘門`), independently
  verified as the exact 25-path allowlisted repair with a clean commit diff.
- Current state: `COMMITTED`.

## Revision History

Revision 0 was correctly stopped after review proved that clearing secrets while
the 13 consumers still rebuilt base-only configuration would regress Production
behavior. Its attempt evidence remains in Git history and in the 2026-07-15
Wave 2 block record. Revision 1 changes the scope only by admitting the
necessary X04A-PERF-001 prerequisite and frozen 13-consumer migration.

# Wave 2 修訂量測合同：X04A Runtime Configuration And Secrets

- Wave: Wave 2
- Revision: 1
- Workspace: `X04A-runtime-configuration-secrets`
- Canonical issues: `X04A-SEC-001`, `X04A-SEC-002`, `X04A-PERF-001`
- Contract status: `CONTRACT_STATUS: CONTRACT_REVISION_APPROVED_DEGRADED`

All recorded evidence is redacted. It may contain path, key name, class, count,
test name, exit code, and commit SHA; it may never contain a secret or effective
configuration value.

## Frozen Sensitive-Key Manifest: X04A-SEC-001

The baseline and final scan use this exact ordered 21-key manifest. No repair
may add, remove, merge, rename, or replace a named key with a pattern:

1. `LineMessaging:Jesus:ChannelAccessToken`
2. `LineMessaging:JesusBack:ChannelAccessToken`
3. `LineLogin:ChannelSecret`
4. `MiniApp:ChannelSecret`
5. `CrmConnection:Username`
6. `CrmConnection:Password`
7. `LinePay:ChannelSecret`
8. `Payment:Profiles:JesusTest:Credentials:ShopNo`
9. `Payment:Profiles:JesusTest:Credentials:A1`
10. `Payment:Profiles:JesusTest:Credentials:A2`
11. `Payment:Profiles:JesusTest:Credentials:B1`
12. `Payment:Profiles:JesusTest:Credentials:B2`
13. `Payment:Profiles:JesusTest:Credentials:XKeyId`
14. `Payment:Profiles:MyPayProduction:Credentials:Key`
15. `Payment:Profiles:MyPayProduction:Credentials:IV`
16. `Sinopac:A1`
17. `Sinopac:A2`
18. `Sinopac:B1`
19. `Sinopac:B2`
20. `Sinopac:XKeyID`
21. `MyPay:Key`

### Measurement SEC001-M1

- Observation: committed `appsettings.json` values for the exact manifest.
- Unit: `SecretLiteralCount / 21`; each named key contributes zero or one.
- Baseline: `21 / 21` non-empty literals.
- Target: `0 / 21` non-empty literals.
- Procedure: `RuntimeConfigurationSecretScanTests` parses the committed JSON
  source with JSONC-compatible comment skipping and trailing-comma support,
  reports key name/count only, and rejects any non-empty manifest value.
- No-regression: all section/key paths and non-secret metadata remain available
  to the host configuration; no replacement secret literal is committed. The
  synthetic host fixture proves an environment-style higher-priority value can
  resolve through the bridge without recording that value.

## Production Safety Matrix: X04A-SEC-002

The test must distinguish an explicit Production overlay from an effective value
inherited from base configuration. It builds base, Production-only, and
base-plus-Production roots and evaluates these frozen cases:

| Case | Key or control | Required Production result |
|---|---|---|
| SEC002-01 | `Security:EnforceGlobalAuthorization` | explicit `true` |
| SEC002-02 | `Security:AllowSessionIdentityFallback` | explicit `false` |
| SEC002-03 | `LinePay:IsSandbox` | explicit `false` |
| SEC002-04 | `Cash_Environment` | exact `Production` or existing `正式環境` classification |
| SEC002-05 | `PAY_PROVIDER` | explicit production provider selection |
| SEC002-06 | `Payment:DefaultProfile` | production profile |
| SEC002-07 | selected payment profile `Environment` | explicit `Production` |
| SEC002-08 | `TSPG:TestMode` | explicit `false` |

### Measurement SEC002-M1

- Unit: `UnsafeOrInheritedConditionCount / 8`,
  `SafeEffectiveConditionCount / 8`, and
  `ProductionOverlayPresenceCount / 8`.
- Baseline: `8 / 8`, `0 / 8`, `0 / 8` respectively.
- Target: `0 / 8`, `8 / 8`, `8 / 8` respectively.
- Procedure: `RuntimeConfigurationSafetyValidatorTests` loads the two committed
  configuration files and separately verifies provider presence and effective
  safe classification without logging values.

### Measurement SEC002-M2

- Observation: Production host safety validator behavior.
- Fixture: synthetic in-memory configuration only.
- Required cases: safe Production passes; each of the eight unsafe controls is
  rejected; `Development`, `Staging`, test, and sandbox `Cash_Environment`
  fixtures are rejected; missing secret is rejected; placeholder fixtures using
  the frozen marker set are rejected;
  Development bypasses the Production-only gate.
- Error evidence: key name and category only; secret values must be absent.

## Frozen Legacy Consumer Inventory: X04A-PERF-001

The source-contract test freezes these 13 production paths:

1. `Models/DonationPaymentManager.cs`
2. `Services/ChurchReportLineAdminNotificationService.cs`
3. `Services/PaymentNotificationService.cs`
4. `Tools/DonationFeePaymentProcessor.cs`
5. `Tools/DonationPaymentDebugLogger.cs`
6. `Tools/LineUtilityClass.cs`
7. `Tools/PersonalQrCodeUtility.cs`
8. `Tools/QrCodeUtility.cs`
9. `Tools/RecurringDonationPaymentProcessor.cs`
10. `Tools/SmallGroupQrCodeUtility.cs`
11. `Tools/SundayQrCodeUtility.cs`
12. `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs`
13. `WebServiceConnector/LineNotifyUtility.cs`

The full key-level inventory is preserved in
`.ccg/tasks/x04a-safe-configuration-compatibility/consumer-configuration-inventory.md`.

### Measurement PERF001-M1

- Unit: `AdHocConfigurationBuilderConsumerCount / 13`.
- Baseline: `13 / 13` listed paths create `ConfigurationBuilder` and load base
  `appsettings.json` without host provider ordering.
- Target: `0 / 13`.
- Procedure: `RuntimeConfigurationConsumerSourceContractTests` reads only the
  frozen 13 source files and rejects `new ConfigurationBuilder`, local
  `AddJsonFile("appsettings.json")`, and local configuration cache declarations.

### Measurement PERF001-M2

- Unit: `BridgeConsumerCount / 13`.
- Baseline: `0 / 13`.
- Target: `13 / 13`.
- Procedure: the same source-contract test requires each frozen path to obtain
  effective settings through `RuntimeConfigurationBridge`, without inspecting or
  emitting a configuration value.

### Measurement PERF001-M3

- Unit: lifecycle/effective-configuration cases passed / 4.
- Target: `4 / 4` for: uninitialized access fails closed; first initialization
  exposes synthetic host values; a higher-priority synthetic overlay remains
  visible through the bridge; different second initialization is rejected.
- Procedure: `RuntimeConfigurationBridgeTests` exercises a fresh bridge
  instance using in-memory synthetic values. Tests of the process-wide bridge
  entrypoint use a serialized fixture and never depend on the working directory
  or process environment variables.

## Focused Commands

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~RuntimeConfigurationBridgeTests|FullyQualifiedName~RuntimeConfigurationConsumerSourceContractTests|FullyQualifiedName~RuntimeConfigurationSecretScanTests|FullyQualifiedName~RuntimeConfigurationSafetyValidatorTests" --no-restore
dotnet build .\SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj --no-restore
git diff --check
git diff --name-only
```

The focused tests must pass, the build must contain no new compiler errors, and
the changed paths must be within the revised `plans.md` allowlist.

## Local Proof Limits

Local tests prove repository literal removal, host configuration handoff, and
synthetic behavior only. They do not prove that a production secret store is
reachable, that deployment variables are present, that exposed credentials have
been rotated, or that external LINE/CRM/payment calls authenticate successfully.
Those require X04B/deployment-owner evidence in a managed environment.

## Evidence Record

The repair agent may append evidence below this heading only after an approved
contract starts. Each entry includes UTC time, commit SHA or baseline, command,
exit code, redacted result, and artifact path.

### 2026-07-18T00:22:14Z - Pre-commit local verification

- Baseline command exit: `1`; `29 passed, 4 failed` from only the four frozen
  X04A gaps: `21/21` literals, `0/8` Production overlay controls, `13/13`
  ad-hoc builders, and `0/13` bridge consumers.
- Repair command exit: `0`; focused suite result: `33 passed, 0 failed`.
- PERF001-M1: `AdHocConfigurationBuilderConsumerCount=0/13`.
- PERF001-M2: `BridgeConsumerCount=13/13`.
- PERF001-M3: bridge lifecycle cases `4/4`.
- SEC001: `SecretLiteralCount=0/21`; scanner output contains only key paths.
- SEC002: `UnsafeOrInheritedConditionCount=0/8`,
  `SafeEffectiveConditionCount=8/8`, and
  `ProductionOverlayPresenceCount=8/8`.
- Build command exit: `0`; ChurchReport build reported `0 warnings, 0 errors`.
- Diff command exit: `0`; `git diff --check` was clean and allowlist comparison
  reported `22` changed paths with `0` unexpected paths.
- Artifact: the local terminal transcript is intentionally redacted; this file
  contains measurements and counts only, never configuration values.
- Review gate: Claude-only run `20260718-082348-x04a-wave2-revision1-final-reviewer`
  had no usable output after two healthy attempts; the required inline Codex
  fallback review found no Critical or Warning issue. Commit is still pending.

## Revision 0 Archive

The prior X04A contract recorded a transient `21/21 -> 0/21` and `8/8 -> 0/8`
attempt before its review revealed the un-migrated consumer regression. That
attempt was fully reverted and is not evidence of this revision's completion.

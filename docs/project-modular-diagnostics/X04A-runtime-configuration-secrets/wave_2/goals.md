# Wave 2 修訂目標合同：X04A Runtime Configuration And Secrets

- Wave: Wave 2
- Revision: 1
- Workspace: `X04A-runtime-configuration-secrets`
- Canonical issues: `X04A-SEC-001`, `X04A-SEC-002`, `X04A-PERF-001`
- Contract status: `CONTRACT_STATUS: CONTRACT_REVISION_APPROVED_DEGRADED`

`measurements.md` defines how each result is observed. This document defines
the non-negotiable completion, no-regression, and rollback conditions.

## X04A-SEC-001: Committed Runtime Secrets

### Completion Target

The frozen 21-key manifest reports `SecretLiteralCount=0/21` in committed
`appsettings.json`. No alternative credential, encoded secret, or real value may
be added to any tracked file, test fixture, measurement, log, or review prompt.

### Required Preserved Behavior

All existing configuration sections, key paths, non-secret metadata, and
endpoint settings remain available. Every frozen legacy consumer obtains its
effective values through the host configuration bridge, so removing committed
literals does not turn an externally injected runtime value into an empty value.

### Required Local Evidence

`RuntimeConfigurationSecretScanTests` passes with the unchanged 21-key
manifest; `RuntimeConfigurationConsumerSourceContractTests` confirms all 13
consumers use the bridge; the focused suite and ChurchReport build pass.

### Failure And Rollback

The goal fails if any manifest key remains non-empty, any secret leaks into an
artifact, any original key path disappears, or any consumer still resolves a
base-only local file configuration. Revert the one X04A repair commit without
restoring any secret literal; deployment owners maintain managed external
configuration throughout rollback.

## X04A-SEC-002: Unsafe Production Inheritance

### Completion Target

The committed base-plus-Production measurement is exactly:

```text
UnsafeOrInheritedConditionCount=0/8
SafeEffectiveConditionCount=8/8
ProductionOverlayPresenceCount=8/8
```

The Production validator rejects all frozen unsafe/missing/placeholder fixtures
before `Startup.ConfigureServices`, while a Development fixture bypasses the
Production-only gate. `Cash_Environment` uses a positive Production allowlist,
so `Development`, `Staging`, test, and sandbox classifications cannot pass by
omission. The placeholder test matrix covers the frozen known marker set without
logging a marker value.

### Required Preserved Behavior

No non-Production environment is blocked by the new Production gate. Existing
global authorization behavior remains protected by its existing tests. Failure
messages contain only key/category information and never an effective setting
value.

### Required Local Evidence

`RuntimeConfigurationSafetyValidatorTests`, focused X04A tests, and the
ChurchReport build pass. The `Program.cs` diff proves validation runs before
Startup construction and bridge initialization uses the validated host
configuration.

### Failure And Rollback

The goal fails if an unsafe control is accepted, a safe Production fixture is
rejected, a non-Production fixture is blocked, a value is logged, or service
registration can occur before validation. Revert the one X04A repair commit;
do not compensate by restoring a committed secret.

## X04A-PERF-001: Ad-Hoc Configuration Lifecycle Bypass

### Completion Target

The exact frozen inventory reaches both targets:

```text
AdHocConfigurationBuilderConsumerCount=0/13
BridgeConsumerCount=13/13
```

The bridge lifecycle test passes all four cases: fail closed before startup,
serve synthetic effective host values after startup, preserve a higher-priority
synthetic overlay, and reject a different second initialization.

### Required Preserved Behavior

The 13 consumers retain their public constructors, legacy direct construction,
existing key paths, organization/default-organization lookup, payment/QR/LINE
business logic, and error semantics except that a pre-host configuration access
now fails explicitly rather than silently reading base-only configuration.

### Required Local Evidence

`RuntimeConfigurationBridgeTests` and
`RuntimeConfigurationConsumerSourceContractTests` pass. The bridge has no JSON
file provider or fallback configuration construction, and no frozen consumer
contains its previous local builder/cache pattern.

### Failure And Rollback

The goal fails if any listed path retains a local builder, if a consumer can
bypass host provider ordering, if bridge initialization can be silently
replaced, or if direct legacy construction requires unrelated caller changes.
Revert the single X04A repair commit and preserve external deployment secrets.

## Whole-Revision Completion Gate

All of the following must be true before X04A becomes `COMMITTED`:

1. SEC001, SEC002, and PERF001 each meet every target above.
2. Every changed source/test/configuration path is in `plans.md` allowlist.
3. Focused tests pass; the ChurchReport build succeeds; `git diff --check` is
   clean.
4. Claude-only diff review approves, or its no-output state is documented and
   exactly one read-only Codex fallback review approves.
5. One Traditional Chinese commit contains the complete X04A repair and its
   redacted evidence appendices.

## Deployment Gate Outside Local Completion

Local completion does not authorize a Production deployment. Before release,
the deployment owner must prove managed secret injection, credential rotation,
and the validated effective Production configuration in the target environment.

## Revision 0 Archive

The former two-issue X04A contract correctly reached `BLOCKED`; it did not
produce a product repair commit. Revision 1 is successful only on its own
measurements and must not reuse transient results from the reverted attempt.

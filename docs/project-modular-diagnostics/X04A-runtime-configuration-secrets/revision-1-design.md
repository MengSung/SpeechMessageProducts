# X04A Wave 2 Revision 1: Safe Runtime Configuration Compatibility

Status: CONTRACT_REVISION_APPROVED_DEGRADED
Date: 2026-07-18
Scope trigger: X04A Wave 2 was blocked because clearing committed secrets left
legacy consumers that rebuild base-only configuration unable to read deployment
injected values.

## Decision

Adopt a host-initialized compatibility bridge as the smallest safe way to
unblock the P0 secret and Production-inheritance repairs. The bridge receives
the `IConfiguration` produced by `WebApplication.CreateBuilder(args)` after
Production validation succeeds. Every legacy consumer reads that same effective
configuration instead of constructing its own provider chain.

This is a deliberate compatibility boundary, not the final dependency-injection
architecture. A later extraction may replace static access with typed options
and constructor injection without changing the effective configuration contract.

## Alternatives Considered

### A. Host-initialized compatibility bridge (selected)

Create a narrowly scoped ChurchReport configuration bridge, initialize it once
from `Program.Main`, and replace each legacy static `ConfigurationBuilder`
field/lazy factory with the bridge's effective configuration accessor.

Advantages: preserves legacy constructors and direct call sites, removes all
13 duplicate provider chains, and makes environment variables/Production
overrides visible to every listed consumer.

Risk: process-wide initialization must be deterministic and testable. The
bridge must fail closed when not initialized; it must never fall back to loading
`appsettings.json` itself.

### B. Full constructor injection and typed options migration

Refactor every consumer and caller to use injected `IConfiguration` or typed
options.

Advantages: the cleanest final architecture and no static compatibility state.
Risk: expands into controller, payment, QR, notification, and legacy connector
call graphs. It is not proportional to the immediate P0 recovery and has a
larger regression surface.

### C. Add environment providers to every current builder (rejected)

This would leave 13 independent configuration lifecycles, duplicate provider
ordering, and bypass central startup validation. It does not resolve
X04A-PERF-001 and is not an admissible repair.

## Configuration Flow

1. `Program.Main` creates the WebApplication builder.
2. In Production, the safety validator verifies the eight effective controls
   and the 21-key secret manifest without logging values.
3. `Program.Main` initializes the compatibility bridge with the validated host
   `builder.Configuration` before `Startup.ConfigureServices` or consumer
   construction.
4. Each legacy consumer obtains the bridge's current configuration. Environment
   variables, host provider ordering, and Production overlay therefore match
   normal application configuration.
5. An access before initialization throws a stable, value-free error. It may not
   construct a fallback JSON configuration.

## Bridge Contract

The bridge exposes only the effective host `IConfiguration` needed by legacy
callers. Its startup initialization is one-time: a second initialization with a
different configuration is an error, rather than a silent configuration source
swap. `Current` either returns the initialized host configuration or throws a
value-free `InvalidOperationException` that identifies the missing startup
initialization step.

The bridge owns no file provider, reload subscription, environment probing, or
secret copy. `Program.Main` is the only production initialization point. This
keeps provider order, environment values, and Production validation owned by
the host rather than by a static utility.

## Test Isolation

The implementation must make the initialization policy unit-testable without
allowing production code to replace the active configuration. Unit tests may
exercise a fresh bridge instance with synthetic in-memory configuration.
Tests that exercise the process-wide compatibility entrypoint must initialize
it once through a serialized fixture before any legacy consumer is constructed.
They must not read repository secrets, mutate process environment variables, or
rely on the current working directory containing an `appsettings.json` copy.

## Revision Scope

The revised Wave 2 X04A contract keeps X04A-SEC-001 and X04A-SEC-002 as its P0
outcomes and admits X04A-PERF-001 as a required prerequisite. This changes the
global Wave 2 selection from ten to eleven canonical issues; the blueprint and
inventory must record that truthfully before product implementation begins.

The consumer allowlist is exactly:

1. `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
2. `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs`
3. `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs`
4. `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
5. `SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs`
6. `SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs`
7. `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs`
8. `SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs`
9. `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
10. `SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs`
11. `SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs`
12. `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs`
13. `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs`

The contract will additionally allow `Program.cs`, the new bridge and validator
files, the focused test files, `appsettings.json`, `appsettings.Production.json`,
and the Wave 2 orchestration documents needed to record the revised selection.

## Test And Measurement Design

- A source-contract test freezes the 13-path list and fails if any listed file
  still creates `ConfigurationBuilder` or loads `appsettings.json` directly.
- A bridge test uses synthetic in-memory values to prove that consumers receive
  effective host configuration and that no value is logged by failure messages.
- A bridge lifecycle test proves one-time initialization, duplicate-initialization
  rejection, and the uninitialized fail-closed behavior using only synthetic
  values.
- Existing X04A tests retain the frozen 21-key secret scan and eight-control
  Production overlay matrix; the revised test set adds the all-consumer source
  contract as the X04A-PERF-001 measurement.
- A focused build/test run must verify no legacy constructor or direct `new`
  call needs a business-flow change.

## Rollback

The revised repair remains one independently revertible X04A commit. Reverting
it restores the prior consumer configuration behavior, but credential values
must never be restored to committed configuration. Deployment owners must keep
the required secrets in an external provider throughout rollback.

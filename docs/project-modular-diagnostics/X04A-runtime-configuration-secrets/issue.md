# X04A Runtime Configuration And Secrets Diagnostic Issues

Status: APPROVED_DEGRADED
Module: X04A
Workspace: X04A-runtime-configuration-secrets
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 917f552aadded0b47f33b46713ee4f31e85a5de627f908cff4e63e0f30fbcb36

## Executive Summary

X04A has confirmed critical security findings. The base runtime config contains committed live-looking LINE, CRM, and payment credentials, while Production overrides do not replace those sections. The same base file mixes production callback URLs, sandbox/test payment settings, permissive security flags, and placeholder keys. This makes secret exposure and environment drift the highest-value work before any optimization.

## Ranked Confirmed Issues

### X04A-SEC-001 Committed runtime secrets in appsettings

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 92
- Confirmed: true
- Evidence confidence: 20
- Impact score: 25
- Likelihood/frequency score: 15
- Security urgency score: 15
- Performance gain score: 2
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: X04A
- Cross-module: false
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/appsettings.json:170`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:174`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:187`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:212`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:250-251`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:271`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:296-301`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:312-313`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:342-346`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:370`
- Evidence: The base config contains non-empty literal values for LINE channel access tokens, LINE channel secrets, CRM username/password, LINE Pay secret, Sinopac keys, MyPay key, Taishin key placeholders, and payment profile credentials.
- Control/data/lifetime flow: `appsettings.json` is included as runtime content by the main project and consumed by host configuration and payment code.
- Impact: Repository readers and published artifacts can expose credentials. Rotation and environment isolation cannot be proven from config alone.
- Why this is necessary: Secret exposure is a Wave 0 blocker and can compromise LINE, CRM, and payment integrations.
- Recommended action: Move all secret values to environment variables or a secret store; keep only required key names and non-secret metadata in committed config; rotate exposed values.
- Validation: Secret scan over X04A owner files returns zero committed literals for secret key patterns.
- Rollback boundary: Config-only change plus external secret injection; no business behavior change should be required.
- Extraction contract: input `IConfiguration` plus environment, output redacted secret policy findings.
- CCG round history:
  - Round 1: Claude KEEP; Gemini quota blocked; source rechecked true

### X04A-SEC-002 Production can inherit unsafe base environment settings

- Category: Security
- Severity: Critical
- Priority: P0
- Priority score: 86
- Confirmed: true
- Evidence confidence: 19
- Impact score: 23
- Likelihood/frequency score: 14
- Security urgency score: 14
- Performance gain score: 1
- Loop leverage score: 10
- Ease/reversibility score: 5
- Effort: M
- Primary owner: X04A
- Cross-module: B01/B05 consume the values
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/appsettings.json:69-72`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:269-279`
  - `SpeechMessageProducts.ChurchReport/appsettings.json:289-334`
  - `SpeechMessageProducts.ChurchReport/appsettings.Production.json:1-57`
- Evidence: Base config sets permissive security flags and sandbox/test payment values. Production override does not override those sections.
- Control/data/lifetime flow: Host configuration merges base and Production override; missing production keys fall back to base values.
- Impact: Production can run with test-mode payment defaults or permissive auth/session settings unless deployment injects overrides.
- Why this is necessary: Environment drift affects security and money movement before code-level optimization.
- Recommended action: Define required Production config schema and fail startup when production inherits test, sandbox, placeholder, or permissive defaults.
- Validation: Effective production config validation rejects unsafe inherited values.
- Rollback boundary: Validation rule can be deployed in warning mode first, then fail-fast mode.
- Extraction contract: config schema validator with environment-specific severity.
- CCG round history:
  - Round 1: Claude REWRITE reflected by keeping critical finding and clarifying production inheritance risk; Gemini quota blocked; source rechecked true

### X04A-SEC-003 Static OAuth state remains in config although runtime uses generated state

- Category: Security
- Severity: Low
- Priority: P3
- Priority score: 34
- Confirmed: true
- Evidence confidence: 15
- Impact score: 4
- Likelihood/frequency score: 3
- Security urgency score: 2
- Performance gain score: 0
- Loop leverage score: 5
- Ease/reversibility score: 5
- Effort: S
- Primary owner: X04A
- Cross-module: B01 owns OAuth behavior
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/appsettings.json:185-190`
- Evidence: `LineLogin:State` is configured as a fixed string, but `AuthenticationController.LineLoginOAuth.cs:104-105`, `:151-158`, and `:331` show runtime OAuth state is generated per request and stored in session.
- Control/data/lifetime flow: The configured literal appears unused for runtime OAuth state, but it is still misleading security-shaped configuration.
- Impact: Low direct runtime impact based on current code evidence; medium maintenance risk because future code could accidentally consume the static value.
- Why this is necessary: X04A should not keep security-control placeholders in deployable config when the real control is runtime-generated.
- Recommended action: Remove the static state config key or mark it non-runtime documentation; keep a validator that rejects known static state values if any consumer binds them.
- Validation: Config scan confirms no deployable `LineLogin:State` placeholder remains, or code search proves no runtime consumer binds it.
- Rollback boundary: Config validation only; B01 behavior change must be separate if needed.
- Extraction contract: config validator flags static state values.
- CCG round history:
  - Round 1: Claude REWRITE addressed by downgrading severity and adding runtime evidence; Gemini quota blocked; source rechecked true

### X04A-PERF-001 Multiple runtime paths bypass host configuration lifecycle

- Category: Performance
- Severity: High
- Priority: P1
- Priority score: 80
- Confirmed: true
- Evidence confidence: 18
- Impact score: 21
- Likelihood/frequency score: 14
- Security urgency score: 13
- Performance gain score: 4
- Loop leverage score: 9
- Ease/reversibility score: 1
- Effort: M
- Primary owner: X04A
- Cross-module: B05/B07/X05Q and utility consumers
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineAdminNotificationService.cs:35`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:45`
  - `SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs:64`
  - `SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs:74`
  - `SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs:70`
  - `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs:64`
  - `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs:43`
  - `SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs:32`
  - `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs:56`
  - `SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs:56`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs:49`
  - `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:52`
  - `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs:46`
- Evidence: Thirteen product runtime paths construct independent `ConfigurationBuilder` instances and read `appsettings.json` directly.
- Control/data/lifetime flow: This bypasses environment overrides and host provider ordering.
- Impact: Secret injection and production overrides can be bypassed across LINE, payment, QR, and legacy utility paths; runtime values can be stale or environment-inaccurate.
- Why this is necessary: A single authoritative config path is required before reliable secret injection and validation.
- Recommended action: X04A should define a no-ad-hoc-config contract and each consumer module should migrate to injected options validated by X04A.
- Validation: Search shows no product runtime path using ad hoc `ConfigurationBuilder` for `appsettings.json`; all runtime config resolves through host providers/options.
- Rollback boundary: Consumer code changes must be separate from this X04A diagnostic and grouped by owner.
- Extraction contract: typed payment config options and validator.
- CCG round history:
  - Round 1: Claude REWRITE addressed by expanding evidence to 13 product runtime paths and raising priority; Gemini quota blocked; source rechecked true

### X04A-EXT-001 Extract reusable config secret scanner and startup validator

- Category: Extraction
- Severity: High
- Priority: P1
- Priority score: 79
- Confirmed: true
- Evidence confidence: 18
- Impact score: 20
- Likelihood/frequency score: 13
- Security urgency score: 13
- Performance gain score: 2
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: M
- Primary owner: X04A
- Cross-module: X01 and X04B consume gates
- Gate blocked: true
- Files:
  - `SpeechMessageProducts.ChurchReport/appsettings.json:168-190`
  - `SpeechMessageProducts.ChurchReport/appsettings.Development.json:1-5`
  - `SpeechMessageProducts.ChurchReport/appsettings.Production.json:1-57`
  - `SpeechMessageProducts.ChurchReport/web.config:10-22`
- Evidence: Repeated secret categories and environment-sensitive values exist without a central validation contract.
- Control/data/lifetime flow: All host and business modules depend on the same effective runtime configuration.
- Impact: A reusable validator prevents repeated leak/drift defects across modules.
- Why this is necessary: X04A is a Wave 0 platform prerequisite for safe downstream optimization.
- Recommended action: Extract a pure config validation component and CI scanner with redacted output.
- Validation: Scanner detects current findings from fixtures and passes when secrets are externally injected.
- Rollback boundary: Tooling and startup validation can be enabled independently.
- Extraction contract: input config files/effective configuration; output redacted findings and required secret manifest.
- CCG round history:
  - Round 1: Claude KEEP with warning addressed by tying scanner scope to expanded config-builder evidence; Gemini quota blocked; source rechecked true

## Runtime Validation Pending

- Whether `LineLogin:State` is actually consumed as OAuth state must be confirmed in B01 runtime/code review.
- Whether deployed production injects environment variables that override the committed secret values must be confirmed outside the repository.
- Whether payment provider secrets are still valid must be handled by credential rotation owners, not by this diagnostic.

## Deleted Or Rejected Candidates

- Session password model fields are not X04A unless sourced from runtime config.
- Publish script checklist warnings are X04B evidence, not X04A owner files.
- Theme palette values are X03/UI concerns unless they affect config schema validation.

## Cross-Module Handoffs

- B01: validate OAuth state generation and security flag behavior.
- B05/F08/F09: migrate payment config consumers to typed validated options after X04A schema exists.
- X01: host startup should invoke X04A validation once the gate is approved.
- X04B: deployment must inject secret values and prove published artifacts do not contain raw secrets.

## Final CCG Approval

Final status: APPROVED_DEGRADED.

CCG round 1 completed with Claude usable output and Gemini provider quota/billing failure. Claude required rewrites for X04A-SEC-003 and X04A-PERF-001; both were reflected in this issue document. Gemini did not produce usable output, so this is degraded approval rather than full dual-model approval.

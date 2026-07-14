# F08 Payment Provider Core Diagnostic Issues

Status: APPROVED_DEGRADED
Module: F08
Workspace: F08-payment-provider-core
Map source: ../module-boundaries-and-optimization-map.md
Mode: DIAGNOSIS_ONLY
Gate status: BLOCKED
Issue document SHA-256: 0a78a1de2c9e0eb5d2becad168290c6859b522a0f7a55c822afff95de3bb1904

## Executive Summary

F08 has six confirmed provider-core findings covering callback authenticity,
binding/replay, malformed input, error sanitization, Sinopac serialization, and
legacy LINE Pay cancellation/disposal. Claude completed the degraded review;
Gemini was quota blocked.

## Ranked Confirmed Issues

### F08-SEC-002 Callback binding and replay decisions are absent from the contract

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 83
- Confirmed: true
- Evidence confidence: 20
- Impact score: 23
- Likelihood/frequency score: 12
- Security urgency score: 14
- Performance gain score: 1
- Loop leverage score: 10
- Ease/reversibility score: 3
- Effort: L
- Primary owner: F08
- Cross-module: F09, B05
- Gate blocked: true
- Files:
  - SpeechMessage.Payments/Abstractions/IPaymentGateway.cs:33
  - SpeechMessage.Payments/Models/PaymentCallbackRequest.cs:22
  - SpeechMessage.Payments/Models/PaymentCallbackResult.cs:22
  - SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs:42
- Evidence: callback input/result contracts carry no expected order, amount,
  currency, nonce, replay key, or binding decision; hash validation does not compare
  a persisted expected order or replay state.
- Control/data/lifetime flow: provider callback -> parsing/signature check without
  expected-order/replay context -> unbound result -> F09/B05 decisions.
- Impact: authentic callbacks can be replayed or mismatched to order/amount/currency
  without an F08-owned decision.
- Why this is necessary: callback authenticity alone does not establish freshness or
  transaction binding.
- Recommended action: add verification context, replay guard, and explicit binding/
  replay status to the provider-neutral result.
- Validation: replay and order/amount/currency mismatch fixtures for every provider.
- Rollback boundary: additive callback context/result fields and compatibility
  adapters.
- Extraction contract: expected payment snapshot and callback in; authenticity,
  binding, and replay decisions out.
- CCG round history:
  - Round 1: Claude retained with provider-specific nuance; Gemini quota blocked.

### F08-SEC-001 MyPay success lacks cryptographic authenticity

- Category: Security
- Severity: High
- Priority: P1
- Priority score: 82
- Confirmed: true
- Evidence confidence: 20
- Impact score: 23
- Likelihood/frequency score: 11
- Security urgency score: 15
- Performance gain score: 1
- Loop leverage score: 9
- Ease/reversibility score: 3
- Effort: M
- Primary owner: F08
- Cross-module: F09, B05
- Gate blocked: true
- Files:
  - SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:18
  - SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:37
  - SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:32
  - SpeechMessage.Payments/Providers/MyPay/MyPayStatusMapper.cs:24
- Evidence: MyPay validation checks only required field shape and known status codes;
  shape-valid values map directly to `PaymentStatus.Succeeded`.
- Control/data/lifetime flow: shape-valid forged MyPay callback -> field validator ->
  parser/status mapper -> unqualified `Succeeded` result.
- Impact: downstream workflows can receive successful status without provider
  authenticity proof.
- Why this is necessary: F08 must represent trust level rather than equate parsing
  and status mapping with verified payment success.
- Recommended action: add explicit verification state and require provider-side
  query/proof before exposing verified success.
- Validation: forged shape-valid success remains unverified and cannot authorize a
  B05 transition.
- Rollback boundary: additive verification state and MyPay adapter migration.
- Extraction contract: provider callback/query proof in; provider authenticity
  result out.
- CCG round history:
  - Round 1: Claude retained with no Critical blocker; Gemini quota blocked.

### F08-SEC-004 Malformed callback bodies escape as exceptions

- Category: Security
- Severity: Medium
- Priority: P1
- Priority score: 75
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 12
- Security urgency score: 8
- Performance gain score: 4
- Loop leverage score: 9
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F08
- Cross-module: F09 host acknowledgement
- Gate blocked: true
- Files:
  - SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:76
  - SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:85
  - SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:77
  - SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs:139
- Evidence: provider parsers repeat unguarded JSON/form field reading and the MyPay
  provider returns parser output without normalizing parser exceptions.
- Control/data/lifetime flow: malformed body -> unguarded parse -> host exception ->
  F09 500/retry behavior.
- Impact: invalid input can cause host errors or provider retry loops instead of a
  stable invalid-callback acknowledgement.
- Why this is necessary: F08 owns provider-neutral callback normalization and must
  keep malformed input within that contract.
- Recommended action: share a guarded field reader returning
  `PaymentErrorKind.CallbackInvalid` while preserving provider acknowledgements.
- Validation: malformed JSON, percent encoding, duplicate-key, and missing-field
  fixtures for all parsers.
- Rollback boundary: internal parser helper and error mapping.
- Extraction contract: raw callback body in; normalized fields or CallbackInvalid
  result out.
- CCG round history:
  - Round 1: Claude retained; Gemini quota blocked.

### F08-SEC-003 Raw provider errors can leak through PaymentError.Message

- Category: Security
- Severity: Medium
- Priority: P1
- Priority score: 74
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 11
- Security urgency score: 12
- Performance gain score: 1
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F08
- Cross-module: F09, B05
- Gate blocked: true
- Files:
  - SpeechMessage.Payments/Models/PaymentError.cs:24
  - SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:505
  - SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:72
  - LinePayCSharp/LinePayClient.cs:99
- Evidence: provider response bodies enter exception messages and then
  `PaymentError.Message`; the diagnostics sanitizer does not sanitize that string.
- Control/data/lifetime flow: raw provider body -> exception -> PaymentError message
  -> F09/B05 log, API, or UI consumer.
- Impact: tokens, signatures, identifiers, customer data, or provider internals can
  escape through public/operational error paths.
- Why this is necessary: provider codes and safe user messages must be separated
  from restricted diagnostics.
- Recommended action: use stable safe messages and place bounded raw detail only in
  sanitized restricted diagnostics.
- Validation: sentinel provider secrets never appear in result/log/UI captures.
- Rollback boundary: preserve provider codes while reverting message mapping only.
- Extraction contract: raw provider failure in; safe error plus sanitized diagnostic
  record out.
- CCG round history:
  - Round 1: Claude retained; Gemini quota blocked.

### F08-PERF-001 Sinopac calls are serialized by shared mutable headers

- Category: Performance
- Severity: Medium
- Priority: P1
- Priority score: 73
- Confirmed: true
- Evidence confidence: 20
- Impact score: 18
- Likelihood/frequency score: 13
- Security urgency score: 1
- Performance gain score: 9
- Loop leverage score: 8
- Ease/reversibility score: 4
- Effort: S
- Primary owner: F08
- Cross-module: false
- Gate blocked: true
- Files:
  - SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:41
  - SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:249
  - SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs:40
  - SpeechMessage.Payments/Gateway/PaymentGateway.cs:30
- Evidence: a provider captured by singleton gateway locks around mutation of
  shared `DefaultRequestHeaders` and the outbound send.
- Control/data/lifetime flow: concurrent gateway calls -> captured Sinopac provider
  -> `_sendLock` -> serialized network requests.
- Impact: unrelated create/query calls block each other behind provider latency and
  mutable header protection.
- Why this is necessary: per-request headers remove both contention and cross-call
  header state.
- Recommended action: construct `HttpRequestMessage` with request-local headers and
  remove the send lock.
- Validation: concurrent calls preserve header isolation and overlap network waits.
- Rollback boundary: Sinopac provider transport only.
- Extraction contract: request-local provider headers and payload in; isolated HTTP
  response out.
- CCG round history:
  - Round 1: Claude required singleton-gateway evidence; Gemini quota blocked.

### F08-PERF-002 Legacy LINE Pay calls lack cancellation and response disposal

- Category: Performance
- Severity: Medium
- Priority: P2
- Priority score: 66
- Confirmed: true
- Evidence confidence: 20
- Impact score: 16
- Likelihood/frequency score: 10
- Security urgency score: 1
- Performance gain score: 8
- Loop leverage score: 7
- Ease/reversibility score: 4
- Effort: M
- Primary owner: F08
- Cross-module: false
- Gate blocked: true
- Files:
  - LinePayCSharp/LinePayClient.cs:93
  - LinePayCSharp/LinePayClient.cs:99
  - LinePayCSharp/LinePayClient.cs:114
  - LinePayCSharp/LinePayClient.cs:182
- Evidence: async methods expose no cancellation token and several response paths
  do not dispose `HttpResponseMessage`; the internal-client constructor is obsolete.
- Control/data/lifetime flow: caller -> tokenless LINE Pay HTTP call -> undisposed
  response retained beyond required content processing.
- Impact: callers cannot stop slow operations and repeated responses can retain
  transport resources longer than needed.
- Why this is necessary: cancellation/disposal are provider transport contracts,
  independent of the already-obsolete constructor.
- Recommended action: add cancellation overloads, dispose responses, and route old
  methods through injected/typed client implementations.
- Validation: cancellation before/during requests and repeated resource/disposal
  tests.
- Rollback boundary: additive overloads; old methods delegate compatibly.
- Extraction contract: cancellation-aware LINE Pay transport with owned response
  lifetime.
- CCG round history:
  - Round 1: Claude retained with obsolete-constructor nuance; Gemini quota blocked.

## Runtime Validation Pending

Runtime provider tests remain defined in `evidence/runtime-validation-plan.md`.

## Deleted Or Rejected Candidates

- F08-PERF-003 callback parsing duplication is low-volume and is folded into the
  shared field-reader remediation for F08-SEC-004.

## Cross-Module Handoffs

- F09 owns host acknowledgement/composition; B05 owns payment state decisions.

## Final CCG Approval

`APPROVED_DEGRADED`; Claude findings were reflected and Gemini was quota blocked.

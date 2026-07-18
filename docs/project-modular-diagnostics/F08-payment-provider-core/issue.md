# F08 Payment Provider Core Issues

Status: APPROVED_DEGRADED

Nested agent count: 0

## Scope

This diagnosis covers `SpeechMessage.Payments/**`, `LinePayCSharp/**`, and `SpeechMessage.Payments.Tests/**` except `SpeechMessage.Payments.Tests/Workflows/**`. Read-only consumer files were inspected only to understand boundary placement.

## Confirmed Issues

### F08-PAY-SEC-001: MyPay callbacks can be normalized as successful without cryptographic authenticity

Severity: High

Evidence:

- `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:18` to `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:21` documents the validator as minimum field validation because the legacy flow does not provide a verifiable shared-secret signature.
- `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:37` to `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:68` checks field presence/length and known status codes only.
- `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:32` to `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:49` maps those fields directly into a `PaymentCallbackResult`.
- `SpeechMessage.Payments/Providers/MyPay/MyPayStatusMapper.cs:24` to `SpeechMessage.Payments/Providers/MyPay/MyPayStatusMapper.cs:36` maps `250`, `290`, and `600` to `PaymentStatus.Succeeded`.

Impact:

- A forged callback with syntactically valid `uid`, `key`, `prc`, and `order_id` can be exposed as `Succeeded` to downstream workflows.

Recommended fix:

- Do not expose MyPay callback success as verified success unless a real provider authenticity check or provider-side query confirmation exists.
- Add a verification state to callback results so consumers can distinguish verified callbacks from unverified provider signals.

CCG round history:

- Round 1: APPROVED_DEGRADED. Gemini quota/billing blocked with HTTP 403 and produced no output. Claude completed with usable output and found no Critical blocker.

### F08-PAY-SEC-002: Callback replay/idempotency and order/amount/currency binding are not represented in the F08 contract

Severity: High

Evidence:

- `SpeechMessage.Payments/Abstractions/IPaymentGateway.cs:33` to `SpeechMessage.Payments/Abstractions/IPaymentGateway.cs:35` accepts only a `PaymentCallbackRequest` for callback parsing.
- `SpeechMessage.Payments/Models/PaymentCallbackRequest.cs:22` to `SpeechMessage.Payments/Models/PaymentCallbackRequest.cs:30` has no expected order id, amount, currency, provider reference, nonce, replay key, or idempotency key.
- `SpeechMessage.Payments/Models/PaymentCallbackResult.cs:22` to `SpeechMessage.Payments/Models/PaymentCallbackResult.cs:31` has parsed order/transaction/amount/currency fields but no binding or replay decision.
- `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs:42` to `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs:69` validates callback hash fields but does not check replay or compare to a persisted expected order snapshot.

Impact:

- Authentic callbacks can be replayed with no F08-owned primitive to detect duplicates.
- F08 cannot assert that callback amount/currency/order matches the original order before downstream workflows act.
- Provider nuance: Sinopac callback parsing returns `Pending` and expects a provider query before payment success decisions; Taishin and MyPay can expose `Succeeded` directly from callback content. The missing replay/binding contract still remains a F08 issue for all providers.

Recommended fix:

- Add a callback verification context and replay guard seam owned by F08.
- Include explicit callback binding status in `PaymentCallbackResult` or a companion result model.

CCG round history:

- Round 1: APPROVED_DEGRADED. Gemini quota/billing blocked with HTTP 403 and produced no output. Claude completed with usable output and requested this provider-specific nuance be recorded.

### F08-PAY-SEC-003: Raw provider error details can leak through unsanitized error messages

Severity: Medium

Evidence:

- `SpeechMessage.Payments/Models/PaymentError.cs:24` to `SpeechMessage.Payments/Models/PaymentError.cs:26` exposes `PaymentError.Message` as a plain string.
- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:505` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:522` includes up to 500 characters of raw provider response body in an exception message.
- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:72` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:75` returns that exception message as `PaymentError.Message`.
- `LinePayCSharp/LinePayClient.cs:99` to `LinePayCSharp/LinePayClient.cs:103` and `LinePayCSharp/LinePayClient.cs:118` to `LinePayCSharp/LinePayClient.cs:122` throw raw provider response bodies.
- `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs:45` to `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs:76` sanitizes dictionaries, not `PaymentError.Message`.

Impact:

- Provider bodies or exception messages can leak tokens, signatures, transaction identifiers, customer data, or provider internals into host logs, API responses, or UI.

Recommended fix:

- Make `PaymentError.Message` user-safe and stable.
- Move bounded raw provider details into sanitized diagnostics only.

CCG round history:

- Round 1: APPROVED_DEGRADED. Gemini quota/billing blocked with HTTP 403 and produced no output. Claude completed with usable output and found no Critical blocker.

### F08-PAY-SEC-004: Malformed callback bodies can throw instead of returning `CallbackInvalid`

Severity: Medium

Evidence:

- `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:76` to `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:93` parses JSON and form-encoded bodies without local error normalization.
- `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:85` to `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:102` repeats the same unguarded pattern.
- `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:77` to `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:116` repeats the same unguarded pattern.
- `SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs:139` to `SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs:145` returns parser output directly without catching parser exceptions.

Impact:

- Invalid JSON, malformed percent encoding, or duplicate form keys can escape as exceptions and cause host 500 responses or provider retry loops instead of a normalized `PaymentCallbackResult`.

Recommended fix:

- Extract a shared callback field reader that converts malformed bodies to `PaymentErrorKind.CallbackInvalid` and preserves provider-required acknowledgements.

CCG round history:

- Round 1: APPROVED_DEGRADED. Gemini quota/billing blocked with HTTP 403 and produced no output. Claude completed with usable output and found no Critical blocker.

### F08-PAY-PERF-001: Sinopac provider serializes outbound calls captured by the singleton gateway

Severity: Medium

Evidence:

- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:41` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:42` declares a shared `HttpClient` and `_sendLock`.
- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:249` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:256` takes the lock, mutates `DefaultRequestHeaders`, and sends the HTTP request while locked.
- `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs:40` registers `IPaymentGateway` as a singleton.
- `SpeechMessage.Payments/Gateway/PaymentGateway.cs:30` to `SpeechMessage.Payments/Gateway/PaymentGateway.cs:35` captures providers into `_providers` in the singleton gateway constructor.

Impact:

- Concurrent Sinopac create/query calls through the singleton gateway can block each other behind the captured provider's `_sendLock`.
- This is a lock-contention and mutable-header issue, not a `HttpClient` socket-lifetime issue.

Recommended fix:

- Use per-request `HttpRequestMessage` headers and remove the lock.

CCG round history:

- Round 1: APPROVED_DEGRADED. Gemini quota/billing blocked with HTTP 403 and produced no output. Claude completed with usable output and requested stronger singleton-gateway evidence; this entry was updated.

### F08-PAY-PERF-002: Legacy LINE Pay client lacks cancellation-token support and consistent response disposal

Severity: Medium

Evidence:

- `LinePayCSharp/LinePayClient.cs:93`, `LinePayCSharp/LinePayClient.cs:114`, `LinePayCSharp/LinePayClient.cs:150`, `LinePayCSharp/LinePayClient.cs:180`, and `LinePayCSharp/LinePayClient.cs:263` expose async methods without `CancellationToken`.
- `LinePayCSharp/LinePayClient.cs:99` to `LinePayCSharp/LinePayClient.cs:103`, `LinePayCSharp/LinePayClient.cs:118` to `LinePayCSharp/LinePayClient.cs:122`, and `LinePayCSharp/LinePayClient.cs:182` to `LinePayCSharp/LinePayClient.cs:186` do not dispose `HttpResponseMessage`.
- `LinePayCSharp/LinePayClient.cs:68` to `LinePayCSharp/LinePayClient.cs:75` still exposes a backward-compatible obsolete constructor that creates an internal `HttpClient`.

Impact:

- Callers cannot cancel long provider calls through the legacy client, and repeated responses can retain resources longer than needed.

Recommended fix:

- Add cancellation-token overloads, dispose responses, and prefer typed-client or injected-client usage for future LINE Pay integration.

CCG round history:

- Round 1: APPROVED_DEGRADED. Gemini quota/billing blocked with HTTP 403 and produced no output. Claude completed with usable output and noted the constructor is already obsolete; the finding remains about cancellation and response disposal.

## Extraction Opportunities

- Shared callback field reader for JSON/form/query parsing.
- Provider callback verification result that distinguishes verified callbacks from unverified provider signals.
- F08-owned replay/idempotency guard seam.
- Payment transport seam with per-request headers, cancellation, retry/backoff, and sanitized error policy.
- Provider protocol descriptor for acknowledgement, required fields, and trust level.

## Runtime Validation

Runtime validation was not executed because the assignment forbids build/test/restore/benchmark commands. See `evidence/runtime-validation-plan.md` for the planned verification commands and test cases once code changes are allowed.

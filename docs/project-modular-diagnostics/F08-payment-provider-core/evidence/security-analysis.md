# F08 Security Analysis

Status: APPROVED_DEGRADED

## Summary

Confirmed security risks are concentrated in callback authenticity, replay/idempotency primitives, parser hardening, and provider error exposure. Taishin has a StoreKey/StoreIV hash verifier, and Sinopac verifies signed API responses after decrypting provider responses, but the F08 boundary does not provide a cross-provider replay guard or an order/amount/currency binding contract.

## Confirmed Findings

### F08-PAY-SEC-001: MyPay callback authenticity is only field-shape validation

Evidence:

- `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:18` describes MyPay validation as minimum field validation.
- `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:37` to `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:68` checks only `uid`, `key`, known `prc`, and `order_id`.
- `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:32` to `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:49` calls that validator, maps `prc` to normalized status, and returns provider data plus `8888` acknowledgement.
- `SpeechMessage.Payments/Providers/MyPay/MyPayStatusMapper.cs:24` to `SpeechMessage.Payments/Providers/MyPay/MyPayStatusMapper.cs:36` maps `250`, `290`, and `600` to `PaymentStatus.Succeeded`.

Risk:

- A forged MyPay callback with syntactically valid fields can be normalized as a successful payment because no shared-secret proof, MAC, provider query confirmation, or profile-owned expected transaction binding is enforced inside F08.

Recommended direction:

- Treat MyPay callbacks as unverified unless a real MyPay authenticity mechanism exists.
- Add a provider-core verification contract that can express `Verified`, `UnverifiedProviderSignal`, and `Invalid`.
- Require MyPay success to be confirmed by provider-side query or a cryptographic proof before exposing `PaymentStatus.Succeeded` to workflow consumers.

### F08-PAY-SEC-002: F08 has no replay/idempotency primitive or expected order/amount/currency binding contract

Evidence:

- `SpeechMessage.Payments/Abstractions/IPaymentGateway.cs:33` to `SpeechMessage.Payments/Abstractions/IPaymentGateway.cs:35` exposes callback parsing as `ParseCallbackAsync(PaymentCallbackRequest request, CancellationToken cancellationToken = default)`.
- `SpeechMessage.Payments/Models/PaymentCallbackRequest.cs:22` to `SpeechMessage.Payments/Models/PaymentCallbackRequest.cs:30` carries profile, provider hint, HTTP method/content/body, query/form, and headers, but no expected order id, amount, currency, provider transaction id, nonce, replay key, or idempotency key.
- `SpeechMessage.Payments/Models/PaymentCallbackResult.cs:22` to `SpeechMessage.Payments/Models/PaymentCallbackResult.cs:31` returns parsed order, transaction id, amount, currency, provider data, and diagnostics, but no verification/binding/replay decision.
- `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs:42` to `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs:69` verifies a hash over transaction id, order id, and state, but does not check replay or compare against a persisted expected order snapshot.
- `SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs:139` to `SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs:145`, `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:177` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:182`, and `SpeechMessage.Payments/Providers/Taishin/TaishinPaymentProvider.cs:178` to `SpeechMessage.Payments/Providers/Taishin/TaishinPaymentProvider.cs:184` return parser output directly.

Risk:

- An authentic callback can be replayed and F08 has no primitive for detecting or reporting duplicate callback processing.
- F08 cannot verify that callback amount/currency/order state matches the original order snapshot before consumers perform post-payment actions.
- Provider nuance: Sinopac callback parsing returns `Pending` and expects a follow-up query, while Taishin and MyPay callback parsers can expose `Succeeded` directly from callback content. The missing replay/binding contract still affects all providers because F08 has no shared primitive to bind callback processing to the original order snapshot.

Recommended direction:

- Add a `PaymentCallbackVerificationContext` or equivalent contract carrying expected order id, expected amount, expected currency, provider reference, received-at time, and an idempotency/replay key.
- Keep persistence out of provider implementations if desired, but define an `IPaymentReplayGuard` seam owned by F08 so hosts do not invent incompatible guards.

### F08-PAY-SEC-003: Provider error messages can expose raw provider details outside the sanitizer

Evidence:

- `SpeechMessage.Payments/Models/PaymentError.cs:24` to `SpeechMessage.Payments/Models/PaymentError.cs:26` exposes `Kind`, `Code`, and raw `Message` without sanitizer enforcement.
- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:505` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:522` includes up to 500 characters of raw provider HTTP response body in an exception message.
- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:72` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:75` and `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:450` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:459` return that message through `PaymentError`.
- `LinePayCSharp/LinePayClient.cs:99` to `LinePayCSharp/LinePayClient.cs:103`, `LinePayCSharp/LinePayClient.cs:118` to `LinePayCSharp/LinePayClient.cs:122`, and `LinePayCSharp/LinePayClient.cs:152` to `LinePayCSharp/LinePayClient.cs:156` throw raw response bodies on non-success responses.
- `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs:45` to `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs:76` sanitizes dictionaries, but not `PaymentError.Message`.

Risk:

- Provider response bodies can contain request echoes, tokens, signatures, transaction identifiers, customer details, or operational diagnostics that may reach logs, API responses, or UI through `PaymentError.Message` or thrown exceptions.

Recommended direction:

- Return stable user-safe error messages and provider codes through `PaymentError`.
- Store bounded raw provider details only in sanitized diagnostics.
- Apply sanitizer or explicit allow-listing to all error surfaces, not only `ProviderData` and `Diagnostics`.

### F08-PAY-SEC-004: Malformed callback bodies can escape parser error normalization

Evidence:

- `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:76` to `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:93` calls `JObject.Parse` and `ToDictionary` without local error handling.
- `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:85` to `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:102` repeats the same unguarded JSON/form parsing pattern.
- `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:77` to `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:116` also uses unguarded JSON parse and form `ToDictionary`.
- `SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs:139` to `SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs:145` returns `MyPayCallbackParser.Parse(request)` directly; provider callback parse errors are not converted to `PaymentErrorKind.CallbackInvalid`.

Risk:

- Invalid JSON or duplicate form keys can throw parser exceptions instead of returning `PaymentCallbackResult` with `CallbackInvalid` plus the provider-required acknowledgement. This can create callback retry loops and unexpected 500 responses at host boundaries.

Recommended direction:

- Centralize callback field parsing behind a shared parser that handles invalid JSON, duplicate keys, malformed percent encoding, and size limits.
- Fail closed as `PaymentErrorKind.CallbackInvalid` while still returning the provider-specific acknowledgement when the provider protocol requires acknowledgement.

## Positive Controls Observed

- Taishin callback hash verification fails closed when the hash is missing or invalid: `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs:58` to `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs:75`.
- Sinopac provider responses are decrypted and response signatures are checked before returning normalized status: `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:224` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:233`.
- Provider diagnostics dictionaries pass through `PaymentDiagnosticsSanitizer`: `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs:45` to `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs:76`.

## Out Of Scope

- Host route choices, MVC action behavior, CRM updates, donation workflow actions, LINE notifications, and post-payment orchestration are owned outside F08.

# F08 Extraction Analysis

Status: APPROVED_DEGRADED

## Extraction Principles Used

Extraction is recommended only where it creates a clearer contract or removes repeated risk. No extraction is recommended solely because a file is large.

## Recommended Extraction Seams

### 1. Callback field reader

Current evidence:

- MyPay parser field reading: `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:53` to `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:93`.
- Sinopac parser field reading: `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:63` to `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:102`.
- Taishin parser field reading: `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:55` to `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:116`.

Proposed contract:

- `IPaymentCallbackFieldReader` or internal static `PaymentCallbackFieldReader`.
- Inputs: `PaymentCallbackRequest`, provider parsing options.
- Outputs: normalized fields plus parse diagnostics or a `CallbackInvalid` result.

Why it helps:

- Centralizes form/query/raw body precedence.
- Handles duplicate keys, invalid JSON, malformed percent encoding, and size policy consistently.
- Removes repeated parser error paths.

### 2. Provider callback verification result

Current evidence:

- MyPay has only shape validation: `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:37` to `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs:68`.
- Taishin has StoreKey/StoreIV hash validation: `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs:28` to `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs:75`.
- Sinopac return callback validates only ShopNo/PayToken/profile match: `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:115` to `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:139`.

Proposed contract:

- `PaymentCallbackVerificationResult` with states such as `Verified`, `UnverifiedProviderSignal`, `Invalid`, and `ConfigurationInvalid`.
- Optional provider-specific evidence fields kept sanitized.

Why it helps:

- Prevents all parsed callbacks from looking equally trustworthy.
- Lets MyPay explicitly report that callback authenticity is not cryptographically proven.
- Gives F09/B05 consumers a safer normalized signal without learning provider-specific hash rules.

### 3. Replay/idempotency guard seam

Current evidence:

- `PaymentCallbackRequest` has no replay key or expected-order snapshot: `SpeechMessage.Payments/Models/PaymentCallbackRequest.cs:22` to `SpeechMessage.Payments/Models/PaymentCallbackRequest.cs:30`.
- `PaymentCallbackResult` has no replay or binding status: `SpeechMessage.Payments/Models/PaymentCallbackResult.cs:22` to `SpeechMessage.Payments/Models/PaymentCallbackResult.cs:31`.

Proposed contract:

- `PaymentCallbackVerificationContext` with expected order id, amount, currency, provider reference, and replay key.
- `IPaymentReplayGuard` abstraction for check-and-mark semantics.

Why it helps:

- Keeps persistence decisions injectable while letting F08 define the contract.
- Prevents every host from inventing its own replay semantics.

### 4. Payment transport/client seam

Current evidence:

- Sinopac mutates `DefaultRequestHeaders` and serializes calls: `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:249` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:256`.
- MyPay and Taishin each implement direct `HttpClient.PostAsync` handling and error normalization.
- Legacy LINE Pay has cancellation and disposal gaps: `LinePayCSharp/LinePayClient.cs:93` to `LinePayCSharp/LinePayClient.cs:123` and `LinePayCSharp/LinePayClient.cs:152` to `LinePayCSharp/LinePayClient.cs:186`.

Proposed contract:

- Internal `IPaymentProviderTransport` or small per-provider transport classes using `HttpRequestMessage`.
- Standard timeout, cancellation, retry/backoff, sanitized error, and per-request header behavior.

Why it helps:

- Removes transport policy from provider request/response mappers.
- Prevents concurrency bugs caused by shared `DefaultRequestHeaders`.
- Makes later provider optimization easier without changing public payment models.

### 5. Provider protocol descriptor

Current evidence:

- Acknowledgement rules are provider-specific and currently live inside parsers, e.g. MyPay `8888` at `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:27` to `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:28`, Taishin JSON acknowledgement at `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:29` to `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:30`, and Sinopac none at `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:51` to `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:59`.

Proposed contract:

- Internal provider protocol descriptor with acknowledgement, required fields, supported create/query/callback operations, and trust level.

Why it helps:

- Makes provider capabilities explicit.
- Avoids scattering protocol constants while preserving provider-specific behavior.

## Not Recommended As Standalone Extraction

- Do not extract a module only because `SinopacPaymentProvider.cs` is large. The useful seams are transport, signature/crypto, field parsing, and result mapping, not file size.
- Do not move host callback route/session/CRM decisions into F08. Those remain F09/B05 concerns.

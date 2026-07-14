# F08 Performance Analysis

Status: APPROVED_DEGRADED

## Summary

The main performance risks are transport-level head-of-line blocking in Sinopac, legacy LINE Pay transport resource handling, and repeated callback parsing/materialization patterns. No benchmarks or runtime tests were run because the F08 assignment forbids build/test/benchmark commands.

## Confirmed Findings

### F08-PAY-PERF-001: Sinopac transport serializes calls captured by the singleton gateway

Evidence:

- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:41` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:42` stores a shared `HttpClient` and a `SemaphoreSlim`.
- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:249` to `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs:256` enters `_sendLock`, mutates `_httpClient.DefaultRequestHeaders`, and performs the HTTP POST while still inside the lock.
- `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs:40` registers `IPaymentGateway` as a singleton.
- `SpeechMessage.Payments/Gateway/PaymentGateway.cs:30` to `SpeechMessage.Payments/Gateway/PaymentGateway.cs:35` accepts `IEnumerable<IPaymentProvider>` once in the gateway constructor and stores the first provider per kind in `_providers`.
- `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs:43` and `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs:46` register `SinopacPaymentProvider` through typed `HttpClient` and expose it as an `IPaymentProvider`, which is then captured by the singleton gateway.

Impact:

- Concurrent Sinopac create/query traffic through the singleton gateway is forced through one captured provider instance and one `_sendLock`.
- Slow provider calls can head-of-line block unrelated requests.
- This finding is about lock contention and mutable request headers, not a `HttpClient` socket-lifetime leak.

Recommended direction:

- Stop mutating `DefaultRequestHeaders` per request.
- Use `HttpRequestMessage` with per-request `X-KeyID` headers and remove the `_sendLock`.
- Consider policy seams for timeout/retry/backoff outside provider protocol mapping.

### F08-PAY-PERF-002: Legacy LINE Pay transport lacks cancellation and consistent response disposal

Evidence:

- Public methods such as `LinePayCSharp/LinePayClient.cs:93`, `LinePayCSharp/LinePayClient.cs:114`, `LinePayCSharp/LinePayClient.cs:150`, `LinePayCSharp/LinePayClient.cs:180`, and `LinePayCSharp/LinePayClient.cs:263` do not accept `CancellationToken`.
- `LinePayCSharp/LinePayClient.cs:99` to `LinePayCSharp/LinePayClient.cs:103`, `LinePayCSharp/LinePayClient.cs:118` to `LinePayCSharp/LinePayClient.cs:122`, and `LinePayCSharp/LinePayClient.cs:182` to `LinePayCSharp/LinePayClient.cs:186` create responses without `using`/`Dispose`.
- The backward-compatible constructor still creates its own `HttpClient` but is marked obsolete: `LinePayCSharp/LinePayClient.cs:68` to `LinePayCSharp/LinePayClient.cs:75`.

Impact:

- Callers cannot cancel long LINE Pay calls through the legacy client.
- Repeated non-disposed responses can hold resources longer than necessary.
- The obsolete internal `HttpClient` constructor remains a footgun for high-volume usage even though the injected constructor is available.

Recommended direction:

- Add cancellation-token overloads while preserving source compatibility.
- Use `using var response` or `await using` where applicable.
- Prefer per-request `HttpRequestMessage` and typed-client registration for any new LINE Pay adapter.

### F08-PAY-PERF-003: Callback field parsing is duplicated and materializes full dictionaries repeatedly

Evidence:

- MyPay, Sinopac, and Taishin each implement their own form/query/raw-body selection and JSON/form parsing: `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:53` to `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs:93`, `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:63` to `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs:102`, and `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:55` to `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs:116`.
- Each parser then builds additional dictionaries for provider data and diagnostics.

Impact:

- Normal callback volume is likely small, so this is not a hot-path emergency.
- The duplication raises maintenance cost and repeats the same parse-error behavior across providers.

Recommended direction:

- Extract a shared callback field reader that normalizes container precedence, duplicate keys, invalid encoding, and dictionary allocation.
- Keep provider-specific field mapping and acknowledgement behavior in provider adapters.

## Non-Issues Or Lower Priority Observations

- Payment providers use DI-managed `HttpClient` in the new core registration: `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs:41` to `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs:45`. This is positive and should be preserved.
- MyPay AES encryption allocates per request (`SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs:127` to `SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs:138`), but this appears to be provider-request volume rather than a high-frequency local loop.

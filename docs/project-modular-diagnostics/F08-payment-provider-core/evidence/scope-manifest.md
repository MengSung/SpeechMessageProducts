# F08 Payment Provider Core Scope Manifest

Status: APPROVED_DEGRADED

## Immediate Document Check

The lead interrupt required reading these two documents first:

- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`
- `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`

Both paths were checked again during this continuation and are absent from the current checkout. This is recorded as a workflow-input blocker for those two reads, but not as a blocker to continuing the F08 diagnosis because the active assignment provides explicit F08 scope, required diagnostics, write boundaries, and CCG requirements.

## Assignment Scope

- Module: F08 Payment Provider Core.
- Workspace: `docs/project-modular-diagnostics/F08-payment-provider-core/`.
- Mode: DIAGNOSIS_ONLY.
- Nested agent count: 0.
- Product code is read-only.

## Owned Paths

Owned for diagnosis:

- `SpeechMessage.Payments/**`
- `LinePayCSharp/**`
- `SpeechMessage.Payments.Tests/**`, except `SpeechMessage.Payments.Tests/Workflows/**`

Owned responsibilities:

- Payment provider protocol.
- Provider-core HTTP client and transport behavior.
- Signature, hash, and crypto implementation.
- Callback parsing.
- Provider request/response models.
- Provider idempotency and replay primitives.
- Legacy LINE Pay provider implementation.

## Excluded Paths And Responsibilities

Excluded:

- `SpeechMessage.Payments.Tests/Workflows/**`, owned by F09.
- MVC route/session/CRM/donation business decisions.
- Post-payment workflow orchestration.
- Neutral order/acknowledgement mapping owned by F09.
- LINE notification decisions owned by B05/B07/F06.
- Existing `bin/**` and `obj/**` output trees.
- Package/cache/lock/test output.

Observed but not read as source evidence:

- `LinePayCSharp/SpeechMessageCrmKey.snk`

## Read-Only Context

Read-only consumers/dependencies inspected only to validate F08 boundaries:

- `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs`
- `SpeechMessage.Payments.AspNetCore/PaymentAcknowledgementResultMapper.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs`

No findings are assigned to those consumers; they only demonstrate where F08 outputs are consumed.

## F08 Source Evidence Files

Core contracts and gateway:

- `SpeechMessage.Payments/Abstractions/IPaymentGateway.cs`
- `SpeechMessage.Payments/Abstractions/IPaymentProvider.cs`
- `SpeechMessage.Payments/Gateway/PaymentGateway.cs`
- `SpeechMessage.Payments/Models/PaymentCallbackRequest.cs`
- `SpeechMessage.Payments/Models/PaymentCallbackResult.cs`
- `SpeechMessage.Payments/Models/PaymentCallbacks.cs`
- `SpeechMessage.Payments/Models/PaymentError.cs`
- `SpeechMessage.Payments/Models/PaymentErrorKind.cs`

Provider implementations:

- `SpeechMessage.Payments/Providers/MyPay/MyPayCallbackParser.cs`
- `SpeechMessage.Payments/Providers/MyPay/MyPaySignatureVerifier.cs`
- `SpeechMessage.Payments/Providers/MyPay/MyPayPaymentProvider.cs`
- `SpeechMessage.Payments/Providers/MyPay/MyPayRequestMapper.cs`
- `SpeechMessage.Payments/Providers/Sinopac/SinopacCallbackParser.cs`
- `SpeechMessage.Payments/Providers/Sinopac/SinopacCrypto.cs`
- `SpeechMessage.Payments/Providers/Sinopac/SinopacSigner.cs`
- `SpeechMessage.Payments/Providers/Sinopac/SinopacPaymentProvider.cs`
- `SpeechMessage.Payments/Providers/Sinopac/SinopacRequestMapper.cs`
- `SpeechMessage.Payments/Providers/Taishin/TaishinCallbackParser.cs`
- `SpeechMessage.Payments/Providers/Taishin/TaishinHashVerifier.cs`
- `SpeechMessage.Payments/Providers/Taishin/TaishinPaymentProvider.cs`
- `SpeechMessage.Payments/Providers/Taishin/TaishinRequestMapper.cs`

Diagnostics/config/transport:

- `SpeechMessage.Payments/Diagnostics/PaymentDiagnosticsSanitizer.cs`
- `SpeechMessage.Payments/DependencyInjection/ServiceCollectionExtensions.cs`
- `SpeechMessage.Payments/Configuration/PaymentOptions.cs`
- `SpeechMessage.Payments/Configuration/PaymentOptionsValidator.cs`
- `SpeechMessage.Payments/Configuration/PaymentMerchantProfile.cs`
- `SpeechMessage.Payments/Configuration/OptionsPaymentProfileResolver.cs`
- `LinePayCSharp/LinePayClient.cs`

Tests inspected, excluding workflow tests:

- `SpeechMessage.Payments.Tests/Providers/MyPay/MyPayProviderTests.cs`
- `SpeechMessage.Payments.Tests/Providers/Sinopac/SinopacProviderTests.cs`
- `SpeechMessage.Payments.Tests/Providers/Taishin/TaishinProviderTests.cs`
- `SpeechMessage.Payments.Tests/Gateway/PaymentGatewayTests.cs`
- `SpeechMessage.Payments.Tests/Diagnostics/PaymentDiagnosticsSanitizerTests.cs`
- `SpeechMessage.Payments.Tests/Models/PaymentModelContractTests.cs`
- `SpeechMessage.Payments.Tests/Configuration/PaymentOptionsTests.cs`

## Commands Used

Read-only/static commands only:

- `rg --files`
- `rg -n`
- `Select-String`
- `Get-Content`
- `git status --short`

Forbidden commands were not run:

- No `dotnet restore`.
- No `dotnet build`.
- No `dotnet test`.
- No package restore.
- No code generation.
- No formatting.
- No migrations.
- No benchmarks.
- No coverage.

## Output Files

Required F08 output files:

- `issue.md`
- `review-log.md`
- `evidence/scope-manifest.md`
- `evidence/security-analysis.md`
- `evidence/performance-analysis.md`
- `evidence/extraction-analysis.md`
- `evidence/runtime-validation-plan.md`

CCG artifacts are limited to F08-prefixed paths under `.ccg/dual-model-runs/**`.

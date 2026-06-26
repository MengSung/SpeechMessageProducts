# Payment Module Extraction Analysis

## CCG Pre-Implementation Review

Date: 2026-06-26

Claude architecture review completed. Gemini review was attempted twice but did not complete in this environment: first due `spawn EPERM`, then due Gemini trusted-directory enforcement / approval abort. Final CCG dual-model review is still required before completion.

## Incorporated Findings

- `BaseChurchController` carries the old `IPayment` dependency, so Task 10 must update the base controller and all derived constructors before deleting `IPayment`.
- QPay fallback credential tables must not be copied into `SpeechMessage.Payments`; named profile configuration must replace them and missing profiles must fail closed.
- `TSPGWebhookHandler` is coupled to ASP.NET `HttpRequest`; Task 7 must rewrite callback parsing against `PaymentCallbackRequest` instead of moving the class as-is.
- MyPay and TSPG static configuration readers must be converted to options/DI-driven provider classes.
- `QPayCardWebhook` and all `QPayProcessor` partial files must be audited before QPay conversion.
- `PaymentCreateResult`, `PaymentQueryRequest`, and `PaymentCallbackResult` need explicit neutral fields to avoid provider-specific vocabulary leaking into the public contract.
- `PaymentHttpRequestMapper` or callback actions must enable ASP.NET request buffering before reading raw callback bodies.

## Implementation Notes

- Task 6 converted `MyPayController.PaymentNotify` to a thin adapter over `IPaymentGateway.ParseCallbackAsync`.
- The focused MyPay adapter validation passed through the ChurchReport test assembly direct `vstest` path.
- `rg` still finds `MyPayToolkit`, `MyPayToolkitWrapper`, `MyPayReturnModel`, and `prc` in ChurchReport because the legacy TSPG and QPay paths still reference those types. Deleting them at Task 6 would break the staged migration. Removal is deferred until Task 8/10 eliminates those cross-provider references and the final boundary search can pass cleanly.

# B05 Scope Manifest

Status: DRAFT
Module: B05-donation-product-payment
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Boundary

B05 owns the ChurchReport donation/product payment surface: donation input and audit flows, payment session handoff, ChurchReport host adapters, callbacks, CRM fee updates, and post-payment notification decisions.

The boundary map defines B05 as donation input/audit, payment session, host adapter, callback, CRM write, and post-payment notification. It excludes payment provider protocol internals, fee master data, and generic LINE transport except as dependencies or consumers (`docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:157`, `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md:369-421`).

## Primary Owner Files

- `SpeechMessageProducts.ChurchReport/Controllers/DonationPaymentLoginController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs`
- `SpeechMessageProducts.ChurchReport/Payments/**`
- `SpeechMessageProducts.ChurchReport/Services/DonationPaymentFormBuilder.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationPaymentModelAssembler.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationPaymentSubmissionService.cs`
- `SpeechMessageProducts.ChurchReport/Services/PaymentCallbackLogger.cs`
- `SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs`
- `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationPaymentResultHelper.cs`
- `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/**`
- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs`
- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
- `SpeechMessageProducts.ChurchReport/Models/PayPage.cs`
- `SpeechMessageProducts.ChurchReport/Models/PayPageResponse.cs`
- `SpeechMessageProducts.ChurchReport/Views/Dedication/**`
- `SpeechMessageProducts.ChurchReport/Views/DedicationAudit/**`
- `SpeechMessageProducts.ChurchReport/Views/MyPay/**`
- `SpeechMessageProducts.ChurchReport/Views/PaymentReturn/**`
- `SpeechMessageProducts.ChurchReport/Views/Home/DonationPaymentLogin.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/PaymentError.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/PaymentSuccess.cshtml`
- `SpeechMessageProducts.ChurchReport/wwwroot/css/DonationPaymentView.css`

## Dependencies

- F03A: CRM CRUD/query and ToolUtility access.
- F08: payment provider protocol, signing, callback parsing, provider core.
- F09: provider-neutral workflow/ASP.NET adapter contracts.
- B01: authentication/session identity context.
- B06B: fee master data.
- B07: ChurchReport LINE integration transport.

## Consumers

- B07 consumes B05 payment notification decisions.
- F09 consumer gates include B05 controller/adapter/callback tests.
- B05 host payment tests consume F08/F09 provider and workflow contracts.

## Tests In Scope

- `ChurchReport.MemberInfo.Tests/Payments/**`
- `ChurchReport.MemberInfo.Tests/DonationNavigationAccessResolverTests.cs`
- `ChurchReport.MemberInfo.Tests/PaymentNotificationRetryKeyTests.cs`
- `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PaymentNotificationServiceWorkflowTests.cs`

## Exclusions

- Provider protocol internals in `SpeechMessage.Payments/**` and `LinePayCSharp/**` are F08.
- Provider-neutral workflow contracts in `SpeechMessage.Payments.Workflows/**` and `SpeechMessage.Payments.AspNetCore/**` are F09.
- Generic LINE SDK/workflow transport is F04-F07/B07.
- Fee master data ownership is B06B.
# B05 Scope Manifest

Status: DEGRADED_REVIEW_PENDING
Module: B05-donation-product-payment
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Scope Summary

B05 owns the ChurchReport donation and product payment product layer: donation input and audit handoff, payment session state, host adapter usage, callback handling, CRM fee writes, and post-payment notification decisions. The module boundary map defines B05 as "奉獻輸入/稽核、payment session、host adapter、callback、CRM 寫入、付款後通知" and explicitly excludes provider protocol, fee master data, and generic LINE transport.

## Primary Owner Files

- `SpeechMessageProducts.ChurchReport/Controllers/DonationPaymentLoginController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs`
- `SpeechMessageProducts.ChurchReport/Payments/**`
- `SpeechMessageProducts.ChurchReport/Services/DonationPaymentFormBuilder.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationPaymentModelAssembler.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationPaymentSubmissionService.cs`
- `SpeechMessageProducts.ChurchReport/Services/PaymentCallbackLogger.cs`
- `SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs`
- `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationPaymentDebugLogger.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationPaymentResultHelper.cs`
- `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/**`
- `SpeechMessageProducts.ChurchReport/Models/DedicationInfoModel.cs`
- `SpeechMessageProducts.ChurchReport/Models/DedicationModel.cs`
- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentFormModel.cs`
- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
- `SpeechMessageProducts.ChurchReport/Models/PayPage.cs`
- `SpeechMessageProducts.ChurchReport/Models/PayPageResponse.cs`
- `SpeechMessageProducts.ChurchReport/Models/ProductItem.cs`
- `SpeechMessageProducts.ChurchReport/Views/Dedication/**`
- `SpeechMessageProducts.ChurchReport/Views/DedicationAudit/**`
- `SpeechMessageProducts.ChurchReport/Views/MyPay/**`
- `SpeechMessageProducts.ChurchReport/Views/PaymentReturn/**`
- `SpeechMessageProducts.ChurchReport/Views/Home/DonationPaymentLogin.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/PaymentError.cshtml`
- `SpeechMessageProducts.ChurchReport/Views/Home/PaymentSuccess.cshtml`
- `SpeechMessageProducts.ChurchReport/wwwroot/css/DonationPaymentView.css`

## Dependencies

- F03A: CRM CRUD/query and ToolUtility behavior.
- F08: provider protocol, payment signature/crypto/callback parsing, provider core.
- F09: provider-neutral payment workflow and ASP.NET adapter contract.
- B01: authentication, session, and identity context.
- B06B: fee master data and fee-related reference data.
- B07/F06: ChurchReport LINE integration and generic notification workflow.

## Consumers

- B07 consumes B05's payment decision and notification content, while owning generic LINE transport.
- F09 consumer gates include B05 controller, adapter, and callback tests.
- B05 host payment tests consume F08/F09 payment contracts.

## Test Ownership

- `ChurchReport.MemberInfo.Tests/Payments/**`
- `ChurchReport.MemberInfo.Tests/DonationNavigationAccessResolverTests.cs`
- `ChurchReport.MemberInfo.Tests/PaymentNotificationRetryKeyTests.cs`
- `ChurchReport.MemberInfo.Tests/LineSharedWorkflow/PaymentNotificationServiceWorkflowTests.cs`

## Explicit Exclusions

- Provider signature, crypto, callback parser, request mapper, and protocol internals stay in F08.
- Payment host/workflow neutral contracts stay in F09 unless B05 is only consuming them.
- Fee master data stays in B06B.
- Generic LINE transport, retry transport mechanics, SDK behavior, and push/reply facade stay in B07/F06.
- Ledger updates are outside this subagent assignment.

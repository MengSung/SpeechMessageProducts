# B05 Scope Manifest

Status: DEGRADED_REVIEW_PENDING
Module: B05-donation-product-payment
Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Scope Summary

B05 owns donation input/audit, payment session, host adapter, callback, CRM write, and post-payment notification decisions. It excludes payment provider protocol internals, fee master data, and generic LINE transport except as dependencies/consumers.

## Primary Owner Files

- `SpeechMessageProducts.ChurchReport/Controllers/DonationPaymentLoginController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/PaymentReturnController.cs`
- `SpeechMessageProducts.ChurchReport/Payments/**`
- `SpeechMessageProducts.ChurchReport/Services/DonationPayment*.cs`
- `SpeechMessageProducts.ChurchReport/Services/Payment*.cs`
- `SpeechMessageProducts.ChurchReport/Tools/Donation*.cs`
- `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/**`
- `SpeechMessageProducts.ChurchReport/Views/Dedication/**`
- `SpeechMessageProducts.ChurchReport/Views/DedicationAudit/**`
- `SpeechMessageProducts.ChurchReport/Views/MyPay/**`
- `SpeechMessageProducts.ChurchReport/Views/PaymentReturn/**`
- `SpeechMessageProducts.ChurchReport/wwwroot/css/DonationPaymentView.css`

## Dependencies

- F03A CRM CRUD/query
- F08 provider protocol
- F09 payment workflow/ASP.NET adapter
- B01 identity/session
- B06B fee master data
- B07/F06 LINE notification transport

## Consumers And Tests

- B07 consumes B05 payment notification decisions.
- B05 tests include `ChurchReport.MemberInfo.Tests/Payments/**`, `DonationNavigationAccessResolverTests.cs`, `PaymentNotificationRetryKeyTests.cs`, and `PaymentNotificationServiceWorkflowTests.cs`.

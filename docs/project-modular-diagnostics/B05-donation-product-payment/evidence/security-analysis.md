# B05 Security Analysis

Status: DEGRADED_REVIEW_PENDING
Nested agent count: 0

## Findings

- B05-SEC-001 High: `PaymentReturnController.ReturnCore` handles externally reachable callback return and writes exception message, stack trace, inner exception, shop number, and masked token to Trace/Console (`PaymentReturnController.cs:97-170`). This is not confirmed credential leakage because PayToken is masked, but callback-triggered implementation detail disclosure needs immediate hardening.
- B05-SEC-002 Medium: `PaymentNotificationService` builds stable retry keys from order/status and logs full LINE id plus retry key on success/failure (`PaymentNotificationService.cs:78-135`). This can correlate donor identity with payment state in logs.
- B05-SEC-003 High validation item: `MyPayController.PaymentNotify` updates CRM and notification decisions from callback result (`MyPayController.cs:90-153`, `PaymentCrmService.cs:39-82`), but static inspection did not prove duplicate/out-of-order callback state guards.
- B05-SEC-004 candidate: legacy ATM/manual dedication notification display paths
  can include raw exception-derived failure reasons
  (`DonationPaymentProcessor.PaymentProcessing.cs:397-405`,
  `DonationPaymentProcessor.FeeManagement.cs:292-360`). It is not retained as a
  separate ranked issue because this pass did not prove an external disclosure
  boundary; any reachable case belongs under the B05-SEC-001 sanitizer contract.

## Critical Security Assessment

No CRITICAL security issue is confirmed from static evidence. Highest priority is callback diagnostic leakage plus runtime validation of callback replay/state pollution.

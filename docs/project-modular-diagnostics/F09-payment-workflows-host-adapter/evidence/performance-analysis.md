# F09 Performance Analysis

Status: COMPLETE
Module: F09
Mode: DIAGNOSIS_ONLY

## Retained Performance Impact

No standalone F09 performance issue was retained.

The retained security/integrity issue has a performance component: every
duplicate post-payment execution repeats CRM lookup/update work and invokes
notification handlers. That is documented under F09-SEC-001 because the primary
risk is duplicate payment side effects rather than raw latency.

## Rejected Performance Candidates

### Full raw-body read in `PaymentHttpRequestMapper`

Rejected as a standalone issue.

Evidence:

- `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs:42` reads the
  raw body before form parsing.
- `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs:43` to
  `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs:45` reads form
  values for form posts.
- `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs:54` to
  `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs:56` flattens
  query and headers.
- `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs:62` enables
  buffering and `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs:69`
  to `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs:70` reads to
  the end of the request stream.

Why rejected:

- Callback payloads are expected to be small provider payloads.
- No runtime evidence, load profile, or production payload size data was
  available in this diagnosis-only pass.
- Current provider parsers need raw body support for JSON and form-urlencoded
  compatibility, and the tests intentionally protect that contract.

Future optimization if evidence appears:

- Add a request-size guard or provider-specific body reader.
- Avoid flattening headers unless a provider parser declares header needs.
- Avoid raw-body allocation for GET/query-only callbacks.

### Sequential handler execution in `PaymentPostPaymentWorkflow`

Rejected as a standalone performance issue.

Evidence:

- `SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:43` to
  `SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs:50` updates
  records first, then notifies payers sequentially.

Why rejected:

- The sequence is explicitly documented as a correctness requirement:
  record update must happen before notification.
- Parallelizing handlers would increase consistency risk and would not address
  the duplicate-execution problem.

### B05 sync-over-async notification calls

Rejected for F09 ownership.

Evidence:

- `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs:128`
  blocks on `SendOrThrowAsync`.
- `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs:337`
  blocks on `PaymentPostPaymentWorkflow.ExecuteAsync`.

Why rejected:

- These calls live in B05 product services/tools, not in the F09 primary owner
  scope. They should be recorded for B05, not retained as an F09 issue.

## Performance Validation Not Run

No benchmark, profiler, test, build, or runtime command was run because the
assignment prohibits generated-output commands. The runtime validation plan
describes future verification once code changes are authorized.

# F09 Extraction Analysis

Status: COMPLETE
Module: F09
Mode: DIAGNOSIS_ONLY

## Current Cohesive Seams

F09 is already a clean extraction compared with many host-bound areas:

- `PaymentHttpRequestMapper` isolates ASP.NET `HttpRequest` from F08 provider
  parsers by projecting into `PaymentCallbackRequest`.
- `PaymentAcknowledgementResultMapper` isolates provider-core acknowledgement
  descriptors from ASP.NET MVC result types.
- `PaymentCreateRequestFactory` maps host product payment input into the F08
  provider-neutral create request.
- `PaymentWorkflowResultMapper` projects F08 callback results into a product
  workflow summary.
- `PaymentPostPaymentWorkflow` defines the shared update-then-notify orchestration
  while leaving CRM and notification implementation in B05.
- `PaymentAspNetCoreServiceCollectionExtensions` registers only reusable host
  adapter services and does not register ChurchReport-specific CRM or LINE
  implementations.

## Recommended Extraction / Acceleration Seam

### Idempotent post-payment execution contract

The most valuable F09 extraction seam is an idempotent post-payment execution
contract. This is the same underlying finding as F09-SEC-001.

Owning files:

- `SpeechMessage.Payments.Workflows/PaymentPostPaymentContext.cs`
- `SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs`
- `SpeechMessage.Payments.AspNetCore/DependencyInjection/PaymentAspNetCoreServiceCollectionExtensions.cs`

Suggested contract:

- Input:
  - normalized payment result
  - product context items
  - stable operation key
  - normalized event kind/status
- Dependency:
  - F09-owned execution/checkpoint interface
  - B05-owned durable implementation
- Output:
  - per-handler result states
  - duplicate-skip state
  - retryable failure state

Why this is the right module boundary:

- F08 cannot know product side effects.
- B05 should not have to invent a private duplicate-guard convention for every
  payment handler.
- F09 already owns the handler orchestration and can make idempotency a required
  part of the workflow contract without taking ownership of CRM or LINE details.

Loop leverage:

- Enables repeatable callback replay tests.
- Gives future provider integrations the same safe post-payment execution
  boundary.
- Lets B05 optimize its durable checkpoint independently from F09 workflow
  semantics.

## Rejected Extraction Candidates

### Move ChurchReport CRM and LINE handlers into F09

Rejected. B05 owns CRM fields, fee categories, payer names, LINE message text,
contact lookup, and product notification rules. Moving those into F09 would
break the provider-neutral workflow boundary.

### Merge `SpeechMessage.Payments.AspNetCore` and `SpeechMessage.Payments.Workflows`

Rejected. The split is useful:

- ASP.NET Core adapters own HTTP and MVC mapping.
- Workflow types remain usable outside ASP.NET.

### Move provider callback parsing into F09

Rejected. Provider protocol parsing, provider acknowledgement creation,
signature/hash verification, and status mapping are F08 responsibilities.

### Make `PaymentPostPaymentContext.Items` strongly typed for ChurchReport

Rejected for F09. The current item bag is intentionally product-neutral. F09 can
improve the key/idempotency contract without importing ChurchReport CRM entity
types.

## Consumer Handoffs

- B05 should implement the idempotency store/checkpoint and keep CRM/LINE
  product details local.
- X01 should wire the F09 idempotency contract to B05 implementation in DI.
- F08 should continue improving provider replay/authenticity/binding so F09
  receives a verified normalized payment event whenever possible.

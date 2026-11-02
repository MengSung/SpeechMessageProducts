# Payment Module Extraction

## Goal

Extract the existing payment-provider functionality from ChurchReport into a standalone reusable payment project so ChurchReport and future products can select a provider through configuration while keeping application-specific business logic outside the reusable payment core.

## User Value

- Payment code becomes easier to maintain because provider-specific SDK, signing, request, response, and callback parsing logic has a clear owner.
- Future products can reuse the same payment project instead of copying ChurchReport payment code.
- ChurchReport keeps its existing payment behavior while gradually moving provider code behind cleaner boundaries.

## Confirmed Facts

- ChurchReport is an ASP.NET Core/.NET solution.
- `ChurchReport/appsettings.json` already contains `PAY_PROVIDER` with options for Sinopac, MyPay, and Taishin.
- `Startup.cs` already switches the registered `IPayment` implementation based on `PAY_PROVIDER`.
- Current provider wrappers are:
  - Sinopac/QPay: `QPayToolkitWrapper`
  - MyPay: `MyPayToolkitWrapper`
  - Taishin/TSPG: `TspgToolkitWrapper`
- The current `IPayment` interface is shaped around Sinopac/QPay models such as `CreOrder`, `QryOrderPay`, and related QPay request/response types.
- MyPay and TSPG currently adapt their behavior into QPay-shaped models, which limits clean reuse.
- ChurchReport-specific responsibilities are mixed with provider code in several areas, including CRM fee updates, LINE notifications, dedication/fee classification, and result views.
- Existing callback endpoints include QPay, MyPay, and TSPG controller actions with provider-specific request formats.
- The user wants the Solution cleaned so payment-related code does not remain scattered across other projects; the new independent project should own the payment-related code boundary.
- The user chose a pure reusable payment core, because other products and ChurchReport each have their own post-payment workflow.
- The reusable payment core must not include ASP.NET Controller responsibilities.
- The new payment core must use provider-neutral contracts and DTOs instead of exposing the current QPay-shaped `IPayment`, `CreOrder`, `QryOrderPay`, or related provider-specific types as the main public API.

## Requirements

- Provide a standalone payment project that can be referenced by ChurchReport and later by other products.
- Support at least the existing three providers: Sinopac/QPay, MyPay, and Taishin/TSPG.
- Select the active provider from JSON configuration.
- Keep provider credentials, endpoint URLs, environment choice, and callback URLs configurable.
- Keep ChurchReport-specific CRM, LINE, view rendering, and donation business rules outside the reusable payment project.
- Preserve the existing ChurchReport payment behavior during migration.
- Avoid expanding scope into unrelated payment providers or unrelated ChurchReport refactors.
- Remove provider-specific payment implementation code from ChurchReport and any other non-payment project in the solution as part of the extraction.
- Keep only product-specific orchestration outside the payment core, such as CRM updates, LINE notifications, donation classification, and product-specific result pages.
- Keep HTTP routing/controller glue outside the payment core if needed, but do not move provider logic back into ChurchReport.
- Make each provider translate between provider-neutral DTOs and its own request/response format internally.
- Limit the first extraction release to the online payment minimum closed loop: create payment, query payment status, parse/verify callbacks, select/configure providers, and normalize payment statuses/errors.
- Support multiple named merchant profiles in configuration so each product or organization can choose a provider/profile without changing the core payment contract.
- Keep the reusable payment core stateless. It must not own a database, store payment/order state, update product records, or decide product-specific idempotency policy.
- Product applications remain responsible for storing payment state, deduplicating callbacks, updating CRM/DB records, sending notifications, and rendering result pages.
- The reusable payment core may expose sanitized raw provider payloads for audit/debugging, but it must not expose unmasked secrets, full tokens, full card data, or other sensitive fields.
- During migration, ChurchReport should keep existing payment endpoints alive as thin adapters that call the new payment core, preserving current callback URLs and user-facing behavior while removing provider-specific implementation from ChurchReport.
- The first reusable payment project should target `net10.0`, matching ChurchReport and most current solution projects.
- Line Pay is out of scope for the first extraction release. The existing `LinePayCSharp` project remains separate while the first release focuses on Sinopac/QPay, MyPay, and Taishin/TSPG.
- The selected approach is a pure reusable payment core project plus thin ChurchReport adapters. ASP.NET routes and product workflows stay in ChurchReport; provider-specific payment implementation moves to the new project.
- The architecture boundary has been confirmed: `SpeechMessage.Payments` owns provider-specific payment implementation, while ChurchReport keeps only thin HTTP adapters and product-specific post-payment workflow.
- The public contract boundary has been confirmed: the reusable payment project exposes provider-neutral abstractions and sanitized payloads, not QPay-shaped request/response types.
- The data flow boundary has been confirmed: ChurchReport and other products call the payment core through a thin adapter, and the core returns normalized payment results only.
- The error handling boundary has been confirmed: the reusable payment core normalizes provider failures and preserves sanitized provider metadata without owning product-level retry or persistence policy.

## Acceptance Criteria

- [ ] A clear provider-neutral contract exists for creating payments and handling provider results.
- [ ] ChurchReport consumers depend on provider-neutral payment models, not QPay-specific request/response models.
- [ ] The active provider can be selected from JSON configuration without changing ChurchReport business code.
- [ ] Configuration supports multiple named merchant profiles with provider, environment, endpoint, credential, and callback URL values.
- [ ] The new reusable payment project is added to the existing solution as a `net10.0` class library.
- [ ] Existing Sinopac/QPay, MyPay, and TSPG flows have an explicit migration path into the standalone project.
- [ ] The first extraction release does not modify or fold in the existing `LinePayCSharp` project.
- [ ] ChurchReport-specific CRM and LINE notification logic remains outside the standalone payment project.
- [ ] The reusable payment project has no dependency on ChurchReport CRM, LINE messaging, MVC views, or application database persistence.
- [ ] Existing payment callback routes can keep working during the first migration phase.
- [ ] Existing ChurchReport payment endpoints contain only HTTP/product orchestration glue after migration, not provider-specific request signing, encryption, callback parsing, or provider SDK logic.
- [ ] The first extraction release does not include refund, capture/void maintenance, daily bill queries, allotment queries, or payment back-office UI.
- [ ] The planning artifacts include provider boundary, configuration contract, data flow, compatibility, validation, and rollback notes.
- [ ] A repository search can verify that provider-specific payment implementation code has moved out of non-payment projects and into the new independent project boundary.
- [ ] ChurchReport-specific post-payment workflow remains outside the reusable payment core and depends only on provider-neutral result models.
- [ ] No implementation begins until the design and implementation plan are reviewed and approved.

## Out Of Scope For Initial Planning

- Adding new payment providers beyond Sinopac/QPay, MyPay, and Taishin/TSPG.
- Rewriting the full donation UI.
- Changing CRM schema or fee entity ownership.
- Moving secrets to a new secret store unless the migration design requires a compatible abstraction.

## Open Questions

- None currently blocking the first design draft.

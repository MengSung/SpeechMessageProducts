# Payment host integration layer extraction

## Goal

Design a second-stage reusable host/integration layer so ChurchReport can remove QPay-named helper/adapters from its web project while keeping the pure `SpeechMessage.Payments` core unchanged.

This is a design-only follow-up to the first payment-core extraction. The first phase already moved provider protocol logic into `SpeechMessage.Payments`; this phase defines how to move reusable host-side adapter utilities out of `ChurchReport` without moving ChurchReport CRM, LINE, donation, or MVC product workflow into the reusable payment core.

## User Value

- Future products such as construction repair systems, association membership systems, and invoice collection systems can reuse common payment host utilities without depending on ChurchReport.
- `ChurchReport.csproj` should stop carrying generic payment helper code whose names imply Sinopac/QPay when the selected provider can be Sinopac, MyPay, or Taishin.
- The already extracted `SpeechMessage.Payments` core remains clean, provider-neutral, and free of ASP.NET/CRM/LINE dependencies.
- ChurchReport keeps its own donation, fee, CRM, LINE, page, route, and legacy compatibility workflow until those product features are intentionally renamed or redesigned.

## Confirmed Facts

- Current worktree is `payment-module-extraction`.
- `ChurchReport/ChurchReport.csproj` still contains QPay strings:
  - `Views\Home\QPayLogin.cshtml`
  - `文件\歷程記錄\QPayView_README.md`
  - project reference to `..\SpeechMessage.Payments\SpeechMessage.Payments.csproj`
- `SpeechMessage.Payments` currently owns provider protocol details for Sinopac/QPay, MyPay, and Taishin/TSPG. Keeping QPay vocabulary inside the Sinopac provider is correct because QPay is the provider protocol name.
- `ChurchReport/Payments` currently contains two different categories of code:
  - reusable host adapters: `PaymentHttpRequestMapper`, `PaymentAcknowledgementResultMapper`, `PaymentCreateRequestFactory`
  - ChurchReport-specific compatibility/workflow: `ChurchReportPaymentProfileResolver`, `QPayCreatePaymentGatewayAdapter`, `QPayReturnWorkflow`, `QPayProductWorkflowDispatcher`, `QPayWorkflowPaymentResult`, `LegacyQPayModels`
- `MyPayController`, `TSPGController`, and `QPayCardController` already use host adapter helpers to translate ASP.NET requests into neutral payment core DTOs.
- `QPayProductWorkflowDispatcher`, `QPayFeeProcessor`, and `QPayDedicationBookingProcessor` call ChurchReport CRM/LINE/donation workflow and therefore are not reusable payment infrastructure.
- `DedicationController`, `QpayManager`, `QpayModel`, `QPayView.cshtml`, and `QPayLogin` are product UI/workflow names from ChurchReport. They should not be moved into a generic payment host project unless a separate ChurchReport product-domain rename is planned.
- CCG dual-model review tooling is currently unavailable because `$HOME\.claude\bin\codeagent-wrapper` does not exist in this environment.

## Requirements

- Do not modify `SpeechMessage.Payments` provider core as part of this design.
- Define a second reusable project for host integration utilities, tentatively named `SpeechMessage.Payments.AspNetCore`.
- The new project may reference `SpeechMessage.Payments` and ASP.NET Core abstractions required for request/response mapping.
- The new project must not reference `ChurchReport`, `ToolUtility`, `Line.Messaging`, Dataverse/CRM SDK, MVC views, ChurchReport controllers, or application database/persistence types.
- Move only reusable host adapter concerns into the second project:
  - ASP.NET `HttpRequest` to `PaymentCallbackRequest` mapping
  - request body buffering and safe body re-read behavior
  - query/form/header flattening
  - `PaymentCallbackAcknowledgement` to ASP.NET response mapping
  - generic payment create request construction helpers if they are product-neutral
  - DI registration for the host integration layer
- Keep product-specific concerns in ChurchReport:
  - CRM fee/dedication updates
  - LINE notifications
  - donation/fee classification
  - ChurchReport route names and MVC views
  - ChurchReport login and session handling
  - callback idempotency and product persistence policy
  - current QPay-named legacy compatibility classes until a separate ChurchReport rename/refactor replaces them
- Do not make the reusable host integration layer responsible for choosing ChurchReport's `PAY_PROVIDER` legacy mapping. A generic profile resolver may exist, but ChurchReport-specific mapping from Chinese provider display names to profile names must stay in ChurchReport or move behind a host-supplied mapping option.
- Avoid creating a "god integration project" that knows about ChurchReport, Sinopac, MyPay, Taishin, and product workflows. Provider protocol stays in `SpeechMessage.Payments`; host HTTP utilities stay in the new host project; product workflow stays in each product.
- Preserve all existing ChurchReport routes and behavior during the migration.
- Define a naming strategy that removes misleading `QPay*` names from reusable layers while allowing ChurchReport product pages to keep route compatibility.
- Define verification searches so the future implementation can prove that only intended QPay vocabulary remains.

## Acceptance Criteria

- [ ] A design document exists that separates pure payment core, reusable host integration, and ChurchReport product workflow.
- [ ] The proposed second project name, dependencies, allowed responsibilities, and forbidden dependencies are documented.
- [ ] Each current `ChurchReport/Payments` class is classified as move-to-host-project, keep-in-ChurchReport, or rename/remove-later.
- [ ] The design explains why `SpeechMessage.Payments` should not be modified for this request.
- [ ] The design explains why some `QPay` strings are acceptable inside the Sinopac provider core and some are not acceptable in generic ChurchReport host adapters.
- [ ] The design includes an implementation sequence for a future coding phase.
- [ ] The design includes boundary verification commands for `SpeechMessage.Payments`, the new host project, and ChurchReport.
- [ ] The design does not move ASP.NET controllers, CRM, LINE, or ChurchReport donation workflows into reusable payment projects.
- [ ] The design preserves current ChurchReport behavior and callback routes.

## Notes

- This is a second-stage planning task, not a replacement for the completed provider-core extraction.
- The first implementation should be conservative: extract reusable ASP.NET adapter utilities first, then reduce QPay compatibility names only after tests prove behavior is unchanged.

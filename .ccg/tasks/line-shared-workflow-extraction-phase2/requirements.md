# LINE Shared Workflow Extraction Phase 2 Requirements

## Goal

Extract reusable LINE notification capabilities from ChurchReport so future ASP.NET Core products can share the same LINE workflow and DI integration without depending on ChurchReport product code.

## Scope

- Add design for `LineMessagingProcessor.Workflows`.
- Add design for `LineMessagingProcessor.AspNetCore`.
- Keep ChurchReport-specific CRM, payment, donation, member, group, controller, view, and LIFF behavior in ChurchReport.
- First ChurchReport adoption covers payment/donation notifications, general text push notifications, and LINE binding/member identity notifications.

## Constraints

- Shared LINE projects must not reference ChurchReport, CRM, ASP.NET MVC controllers, DbContext, or payment projects.
- Shared workflow should expose product-friendly request/result models plus an SDK message escape hatch.
- Implementation must be split into independently verifiable batches.
- Files must be UTF-8 without BOM and CRLF.

## Acceptance Criteria

- A design spec exists at `docs/superpowers/specs/2026-07-03-line-shared-extraction-design.md`.
- The design defines architecture boundaries, notification models, error behavior, ChurchReport adoption scope, implementation batches, guardrails, and validation.
- The design is suitable for a follow-up implementation plan.

# Payment Module Extraction Implementation Plan

This Trellis implementation plan mirrors the authoritative Superpowers plan at:

`docs/superpowers/plans/2026-06-25-payment-module-extraction.md`

Use the Superpowers plan as the execution checklist. This file records the Trellis gate summary and ordered execution map for the active task.

## Goal

Build a reusable `SpeechMessage.Payments` payment core and move Sinopac/QPay, MyPay, and Taishin/TSPG provider implementation out of ChurchReport while preserving ChurchReport routes and product workflows.

## Architecture

- Add a pure `net10.0` class library: `SpeechMessage.Payments`.
- Add a focused test project: `SpeechMessage.Payments.Tests`.
- Core owns provider protocol details, normalized contracts, options, diagnostics sanitization, provider routing, and provider implementations.
- ChurchReport keeps ASP.NET route binding, CRM/LINE/product workflow, callback idempotency, and result pages.
- Provider migration order is MyPay first, Taishin/TSPG second, Sinopac/QPay last.
- Current Codex mode is inline: do not dispatch implementation/check sub-agents.

## Ordered Tasks

1. Create `SpeechMessage.Payments` and `SpeechMessage.Payments.Tests` project shells and add them to `ChurchReport.sln`.
2. Add provider-neutral models and `IPaymentGateway` / internal `IPaymentProvider` contracts.
3. Add `PaymentOptions`, profile resolver, diagnostics sanitizer, gateway router, and DI registration.
4. Add ChurchReport thin adapter infrastructure for HTTP request mapping, acknowledgement mapping, profile resolution, create-request construction, and neutral workflow mapping.
5. Migrate MyPay core provider into `SpeechMessage.Payments`.
6. Convert `MyPayController` and related ChurchReport workflow services to consume neutral callback results.
7. Migrate Taishin/TSPG core provider into `SpeechMessage.Payments`.
8. Convert `TSPGController` routes to thin adapters and remove TSPG provider implementation from ChurchReport.
9. Migrate Sinopac/QPay core provider into `SpeechMessage.Payments`.
10. Convert QPay ChurchReport create/query/return flows to `IPaymentGateway` and remove old QPay-shaped `IPayment`.
11. Run final boundary cleanup, Line Pay non-interference checks, full tests/build, CCG review, Trellis check, and documentation updates.

## Validation Commands

Run these at the relevant task gates:

```powershell
dotnet test SpeechMessage.Payments.Tests\SpeechMessage.Payments.Tests.csproj
dotnet test ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj
dotnet build ChurchReport.sln
rg -n "ChurchReport|ToolUtility|Line\.Messaging|Microsoft\.Xrm|HttpRequest|Controller|IActionResult|DbContext" SpeechMessage.Payments --glob "*.cs" --glob "*.csproj"
rg -n "IPayment|IQPayToolkit|QPayToolkit|MyPayToolkit|TspgToolkit|TSPGWebhookHandler|CreOrderReq|QryOrderPayReq|OrderMaintainReq|BillQuery|AllotQuery" ChurchReport --glob "*.cs"
git diff -- LinePayCSharp
```

Expected final state:

- `SpeechMessage.Payments` references no ChurchReport, ToolUtility, LINE, CRM/Dataverse, MVC, `HttpRequest`, or persistence types.
- ChurchReport contains only route/product workflow glue for payment flows; provider signing, encryption, request mapping, callback parsing, and provider status mapping live in `SpeechMessage.Payments`.
- `LinePayCSharp` has no diff.
- Tests and solution build pass.

## Risk Controls

- Do not start implementation until the user reviews the planning artifacts and approves `task.py start`.
- Do not move ASP.NET controllers into the core.
- Do not add refund, capture, void, bill query, allotment query, or back-office UI to the first public `IPaymentGateway`.
- Do not expose raw provider SDK models from the public core contract.
- Do not delete legacy provider code until the replacement provider tests, ChurchReport adapter tests, build, and provider-specific boundary search pass.
- Because this task is L+ complexity and high risk, run CCG dual-model review before final completion and save findings to `.ccg/tasks/payment-module-extraction/review.md`.

## Rollback Points

- After adapter infrastructure: remove project reference and adapter registration.
- After MyPay migration: restore MyPay controller/toolkit files from the prior commit.
- After Taishin migration: restore TSPG controller/toolkit files from the prior commit.
- After Sinopac migration: restore QPay toolkit, `IPayment`, and QPay route files from the prior commit.

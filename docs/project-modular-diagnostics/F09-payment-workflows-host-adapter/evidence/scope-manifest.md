# F09 Scope Manifest

Status: COMPLETE
Module: F09
Workspace: docs/project-modular-diagnostics/F09-payment-workflows-host-adapter/
Mode: DIAGNOSIS_ONLY
Worktree: D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion
Branch: 1.0.0.1.EvenVersion
Nested agent count: 0

## Authoritative Inputs Read

- `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`
- `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`
- `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/prd.md`
- `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/design.md`
- `.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/implement.md`
- `.trellis/spec/guides/index.md`
- `.trellis/spec/guides/ccg-external-review-thinking-guide.md`
- `.trellis/spec/guides/cross-layer-thinking-guide.md`
- `.trellis/spec/backend/index.md`

## Primary Owner Scope

F09 owns the payment workflow and ASP.NET Core host adapter layer:

- `SpeechMessage.Payments.Workflows/PaymentLineItemDraft.cs`
- `SpeechMessage.Payments.Workflows/PaymentMethodSelection.cs`
- `SpeechMessage.Payments.Workflows/PaymentOrderDraft.cs`
- `SpeechMessage.Payments.Workflows/PaymentOrderDraftMapper.cs`
- `SpeechMessage.Payments.Workflows/PaymentPayerDraft.cs`
- `SpeechMessage.Payments.Workflows/PaymentPostPaymentContext.cs`
- `SpeechMessage.Payments.Workflows/PaymentPostPaymentWorkflow.cs`
- `SpeechMessage.Payments.Workflows/PaymentScheduleDraft.cs`
- `SpeechMessage.Payments.Workflows/PaymentWorkflowResultMapper.cs`
- `SpeechMessage.Payments.Workflows/SpeechMessage.Payments.Workflows.csproj`
- `SpeechMessage.Payments.AspNetCore/DependencyInjection/PaymentAspNetCoreServiceCollectionExtensions.cs`
- `SpeechMessage.Payments.AspNetCore/PaymentAcknowledgementResultMapper.cs`
- `SpeechMessage.Payments.AspNetCore/PaymentCreateRequestFactory.cs`
- `SpeechMessage.Payments.AspNetCore/PaymentHttpRequestMapper.cs`
- `SpeechMessage.Payments.AspNetCore/SpeechMessage.Payments.AspNetCore.csproj`

F09 test ownership:

- `SpeechMessage.Payments.Tests/Workflows/PaymentOrderDraftMapperTests.cs`
- `ChurchReport.MemberInfo.Tests/Payments/PaymentAcknowledgementResultMapperTests.cs`
- `ChurchReport.MemberInfo.Tests/Payments/PaymentHttpRequestMapperTests.cs`
- `ChurchReport.MemberInfo.Tests/Payments/PaymentPostPaymentWorkflowTests.cs`
- `ChurchReport.MemberInfo.Tests/Payments/PaymentWorkflowResultMapperTests.cs`

## Dependency Scope Recorded, Not Edited

- F08 provider core:
  - `SpeechMessage.Payments/**`
  - `SpeechMessage.Payments.Workflows/SpeechMessage.Payments.Workflows.csproj:4`
  - `SpeechMessage.Payments.AspNetCore/SpeechMessage.Payments.AspNetCore.csproj:14`
- B05 donation/product/payment consumers:
  - `SpeechMessageProducts.ChurchReport/Controllers/MyPayController.cs`
  - `SpeechMessageProducts.ChurchReport/Controllers/TSPGController.cs`
  - `SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentContextBuilder.cs`
  - `SpeechMessageProducts.ChurchReport/Payments/ChurchReportPaymentPostPaymentHandlers.cs`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentCrmService.cs`
  - `SpeechMessageProducts.ChurchReport/Services/PaymentNotificationService.cs`
  - `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- X01 host composition:
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:95`
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:96`
  - `SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj:97`
  - `SpeechMessageProducts.ChurchReport/Startup.cs:523`
  - `SpeechMessageProducts.ChurchReport/Startup.cs:524`
  - `SpeechMessageProducts.ChurchReport/Startup.cs:528`
  - `SpeechMessageProducts.ChurchReport/Startup.cs:529`

## Boundary Summary

- F08 owns provider protocol, provider callback parsing, acknowledgement
  creation, cryptographic/provider verification, and provider HTTP transport.
- F09 owns product-neutral workflow DTO mapping, ASP.NET Core request/response
  mapping, and the reusable post-payment handler pipeline.
- B05 owns ChurchReport CRM entity lookup/update, donation/payment business
  rules, payer notification content, and product-specific context items.
- X01 owns application composition, project references, DI registration, route
  exposure, and host lifetime decisions.

## Read-Only Inspection Commands Used

Only read-only commands were used:

- `Get-Location`
- `python ./.trellis/scripts/get_context.py`
- `python ./.trellis/scripts/get_context.py --mode phase`
- `python ./.trellis/scripts/get_context.py --mode phase --step 2.1 --platform codex`
- `python ./.trellis/scripts/get_context.py --mode packages`
- `Get-Content -Raw`
- `rg --files`
- `rg -n`
- `git status --porcelain=v1`

No restore, build, test, package restore, code generation, formatting,
migration, benchmark, cache, lockfile, `bin/**`, `obj/**`, or test-output
command was run.

## Git Baseline

`git status --porcelain=v1` already showed many untracked diagnostic and CCG
artifacts for prior modules, plus untracked `.ccg/tasks/`, `.trellis/tasks/`,
and `docs/project-modular-diagnostics/` entries before F09 edits. This F09
diagnosis did not modify product source/config/project/solution/test files.

Allowed F09 writes for this run are limited to:

- `docs/project-modular-diagnostics/F09-payment-workflows-host-adapter/**`
- `.ccg/dual-model-runs/f09-issue-review-r1-input.md`
- CCG runner output under `.ccg/dual-model-runs/**` for title
  `f09-issue-review-r1`

Write-scope result before CCG: no observed F09 writes outside the allowed paths.

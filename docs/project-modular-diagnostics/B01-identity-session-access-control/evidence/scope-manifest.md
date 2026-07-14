# B01 Scope Manifest

Module: B01
Workspace: docs/project-modular-diagnostics/B01-identity-session-access-control/
Mode: DIAGNOSIS_ONLY

## Primary Owner Files Reviewed

- SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/**
- SpeechMessageProducts.ChurchReport/Controllers/PhoneBindingController.cs
- SpeechMessageProducts.ChurchReport/Models/Authentication/**
- SpeechMessageProducts.ChurchReport/Services/Authentication/**
- SpeechMessageProducts.ChurchReport/Security/**
- SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs
- SpeechMessageProducts.ChurchReport/Middleware/IdentityAuditCleanupService.cs
- SpeechMessageProducts.ChurchReport/Middleware/IdentityAuditMiddleware.cs
- SpeechMessageProducts.ChurchReport/Middleware/MiniAppDetectionMiddleware.cs
- SpeechMessageProducts.ChurchReport/Middleware/SessionValidationMiddleware.cs
- SpeechMessageProducts.ChurchReport/SessionAttribute.cs
- SpeechMessageProducts.ChurchReport/Views/Authentication/**
- SpeechMessageProducts.ChurchReport/Views/Shared/_Login*.cshtml
- SpeechMessageProducts.ChurchReport/wwwroot/css/LineIdLoginView.css
- SpeechMessageProducts.ChurchReport/wwwroot/css/LineLiffView.css
- SpeechMessageProducts.ChurchReport/wwwroot/css/Login.css
- SpeechMessageProducts.ChurchReport/wwwroot/css/mini-app-safe-area.css
- SpeechMessageProducts.ChurchReport/wwwroot/js/Login.js
- SpeechMessageProducts.ChurchReport/wwwroot/js/LineIdLoginView.js
- ChurchReport.MemberInfo.Tests/Security/**

## Dependency And Consumer Notes

- X01 composes B01 through Startup MVC filters, cookie authentication, session, and middleware order.
- X04A owns runtime settings that determine whether B01's global authorization filter is active.
- F03A provides CRM operations used by login, LINE binding, and contact lookup flows.
- F04-F06 and B07 provide/consume LINE transport and profile workflows.
- B02-B07 consume authenticated identity/session state after B01 login.

## Evidence Commands Used

- `rg --files` for B01 owner paths.
- `rg -n` for auth/session/claims/returnUrl/CSRF/session fallback/CRM wrapper patterns.
- `Get-Content` with line numbering for cited source files.
- `git status --porcelain=v1` baseline inspection.

## Write Scope

Allowed writes used:

- docs/project-modular-diagnostics/B01-identity-session-access-control/**
- .ccg/dual-model-runs/b01-issue-review-r1-input.md
- .ccg/dual-model-runs/b01-issue-review-r1-reviewer.md and generated run folder from the approved CCG runner

No product source, project, config, solution, test, cache, lockfile, bin, obj, generated output, Trellis task, CCG task, map, or workflow file is intentionally modified.

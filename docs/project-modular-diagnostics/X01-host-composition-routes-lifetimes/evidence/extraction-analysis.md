# X01 Extraction And Acceleration Analysis

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Extraction Boundary

X01 is a composition root, not a business module. Extraction should separate host composition surfaces without moving ownership of business workflows.

## Candidate Extractions

### E1 - Split host registration into narrow extension groups

Status: recommended after runtime baseline

Candidate groups:

- Host infrastructure registration: compression, cache, performance, monitoring, forwarded headers, session, HTTP context accessor.
- External module registration: LINE, RichMenu, payment, ToolUtility.
- Business adapter registration: host adapters that bridge business modules into the web host.

Constraint:

- Do not move business implementation ownership into X01. Keep extension groups thin and registration-only.

### E2 - Create route inventory and route contract baseline before endpoint migration

Status: required before acceleration

Candidate output:

- A generated route snapshot containing route name, template, controller, action, and optional parameter shape.
- Compatibility smoke for representative paths: auth/login, LINE login, small group routes, donation/payment routes, QR routes, error route, and default route.

Constraint:

- No route behavior should change during diagnostic work.

### E3 - Convert debug background diagnostics to host-managed services only if evidence shows shutdown/file-handle issues

Status: defer until runtime validation

Candidate:

- Replace untracked debug `Task.Run` GC monitoring with a cancellable hosted service.

Constraint:

- This is not approved as a code change by this diagnostic. It is only a future remediation option if runtime validation proves shutdown or resource issues.

## Acceleration Opportunities

- Faster DI failure detection through a host composition smoke.
- Safer route changes through a route snapshot.
- Lower future review cost by separating registration-only extension methods from business service implementations.

## Non-Candidates

- Business workflow extraction.
- Monitoring implementation rewrite.
- Configuration value changes.
- Package upgrades or project-file optimization in this diagnostic pass.

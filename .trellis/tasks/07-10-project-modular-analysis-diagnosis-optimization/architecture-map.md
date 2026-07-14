# SpeechMessageProducts Architecture And Diagnostic Module Map

> **Superseded ownership map**
>
> This early 21-domain draft is retained as research history only. It contains
> boundaries later found to be too broad or overlapping. Do not use it for
> task ownership. The reviewed authoritative map is:
> `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`.

## Executive Conclusion

The repository is a modular monolith rather than a single homogeneous
application or a set of independently deployed services.

Its physical structure has four levels:

1. `SpeechMessageProducts.ChurchReport` is the ASP.NET Core product host and
   composition root.
2. LINE, payment, CRM, and Dataverse capabilities are partly extracted into
   reusable class-library projects.
3. Most ChurchReport business capabilities still cross technical folders such
   as Controllers, Models, Services, Tools, Payments, and
   WebServiceConnector.
4. Tests, historical documentation, and projects outside the solution do not
   yet form one consistent lifecycle boundary.

The diagnostic program should therefore use:

- physical project boundaries where they already exist;
- vertical business-capability boundaries inside ChurchReport;
- separate cross-cutting platform audits that do not absorb business logic.

## Current Topology

```text
SpeechMessageProducts.ChurchReport
  -> ToolUtility
      -> PowerPlatform.Dataverse.Client
      -> Line.Messaging
  -> Line.Messaging
  -> LineMessagingProcessor
      -> Line.Messaging
  -> LineMessagingProcessor.Workflows
      -> LineMessagingProcessor + Line.Messaging
  -> LineMessagingProcessor.RichMenus
      -> LineMessagingProcessor + Line.Messaging
  -> LineMessagingProcessor.AspNetCore
      -> Processor + Workflows + RichMenus + Line.Messaging
  -> SpeechMessage.Payments
  -> SpeechMessage.Payments.Workflows
      -> SpeechMessage.Payments
  -> SpeechMessage.Payments.AspNetCore
      -> Payments + Payment Workflows
```

The main host contains approximately 68,994 C# lines. It directly couples to
ToolUtility and CRM SDK types from many files, while Payments and LINE have
already established reusable project families.

## Module Classification

### Shared Foundation Modules

These have physical project boundaries and can usually be analyzed with a
project-local file list and their direct consumers.

| ID | Module | Primary boundary | Diagnostic focus | Optimization and validation boundary | Risk / order |
|---|---|---|---|---|---|
| F01 | Repository topology and build governance | `SpeechMessageProducts.sln`, all `.csproj`, test projects, projects outside the solution | Project graph, target frameworks, package/reference drift, orphan and duplicate projects, CI/build coverage | Solution restore/build matrix, explicit decision for every out-of-solution project | Medium; baseline first |
| F02 | Dataverse client and connection infrastructure | `PowerPlatform.Dataverse.Client`, connection primitives consumed by ToolUtility | Connection ownership, authentication, pooling, timeout, retry, disposal, blocking SDK calls | Infrastructure tests and controlled CRM connectivity tests; exclude product queries | High; foundation wave |
| F03 | CRM operation library | `ToolUtility` | Query shape, column selection, batching, caching, error mapping, API consistency, dependency on LINE | `ToolUtility.Tests`, fake organization-service tests, call-site inventory; exclude ChurchReport business rules | High; after F02 |
| F04 | LINE Messaging SDK and models | `Line.Messaging` | HTTP ownership, serialization, webhook/message contracts, API parity, retry/error behavior | `Line.Messaging.Tests`, protocol fixture tests; exclude product identity and notifications | Medium-high; foundation wave |
| F05 | LINE processor compatibility client | `LineMessagingProcessor` | Large compatibility class responsibilities, client lifetime, API surface, blocking calls, duplication with SDK | `LineMessagingProcessor.Tests`, API call capture tests; exclude product workflows | Medium-high; after F04 |
| F06 | LINE notification and reply workflows | `LineMessagingProcessor.Workflows` | Recipient validation, result normalization, message factories, failure classification | `LineMessagingProcessor.Workflows.Tests`; exclude ChurchReport CRM/profile lookup | Medium; after F04-F05 |
| F07 | LINE RichMenu engine | `LineMessagingProcessor.RichMenus` | Catalog, provisioning, assignment, state, expiry, trigger policy, idempotency | `LineMessagingProcessor.RichMenus.Tests`; exclude ChurchReport legacy catalog and user lookup | Medium-high; after F04-F05 |
| F08 | Payment provider core | `SpeechMessage.Payments` | Provider-neutral contracts, provider implementations, HTTP/crypto/callback parsing, sanitization | `SpeechMessage.Payments.Tests`, provider protocol fixtures; exclude CRM, LINE, MVC, donation rules | High; independently diagnosable |
| F09 | Reusable payment host and workflow layer | `SpeechMessage.Payments.AspNetCore`, `SpeechMessage.Payments.Workflows` | HTTP request mapping, acknowledgement mapping, neutral order drafts, post-payment abstractions | Adapter and workflow tests; exclude ChurchReport routes, persistence, and notifications | Medium-high; after F08 |

### ChurchReport Business Capability Modules

These are logical vertical slices inside the monolith. Before analyzing one,
create a module-specific manifest that includes every controller, model,
service, tool, WebServiceConnector, view, configuration key, and test involved.

| ID | Module | Primary evidence and paths | Diagnostic focus | Optimization and validation boundary | Risk / order |
|---|---|---|---|---|---|
| B01 | Identity, session, and access control | `Controllers/AuthenticationController/*`, `Security`, `Middleware`, authentication/member access services, `BaseChurchController` access paths | Session isolation, cookie/auth flow, authorization coverage, OAuth/binding state, request identity lifetime | Security and route integration tests; exclude general LINE messaging and member CRUD | Critical; early |
| B02 | Member, contact, personal profile, and onboarding | `MemberInfoController`, `PersonalController*`, `NewPersonController`, `WebServiceConnector/NewPerson.cs`, `PersonalInfomatioManager.cs`, contact/avatar/member services and views | CRM query boundaries, access scope, photo processing, duplicated member models, large controller/service responsibilities | Member integration tests, CRM fakes, view-model contract tests | High; after F02-F03 and B01 |
| B03 | Small groups, hierarchy, and weekly reporting | `SmallGroupController/*`, `DownloadHappyGroup.cs`, small-group models, weekly-report managers, `InMemoryDataContextSmallGroup` | Per-session state, cache isolation/growth, hierarchy correctness, report queries, large-file decomposition | Same-user and cross-user concurrency tests, hierarchy/report fixtures | High; after B01 and F03 |
| B04 | Attendance, appointments, equipment, schedules, and QR flows | `AppointmentsDownUpLoader.cs`, `AppointmentController`, `EquipmentController`, `QrCodeController`, scheduler APIs, present-record services and QR tools | Upload/download consistency, date rules, CRM transaction shape, QR client lifetime, duplicate utilities | Workflow integration tests, date/attendance fixtures, endpoint concurrency tests | High; after F03-F06 |
| B05 | Donation, fees, and payment product workflow | Dedication/payment controllers, `Payments`, donation services/tools/models, `WebServiceConnector/DonationPaymentProcessor` | Idempotency, callback-to-CRM data flow, session boundaries, provider-core leakage, notification coupling, sync/async flow | End-to-end provider callback fixtures with fake CRM and LINE; exclude provider protocol internals | Critical; after F08-F09, F03, and B07 |
| B06 | List, reference data, fee administration, and church hierarchy | `ListManagementController`, `ListManagementDataManager`, fee/list managers, option metadata, related views | Query batching, metadata caching, list ownership, duplicated mappings, authorization | List/reference fixtures, fake CRM tests, cache invalidation tests | Medium-high; after F03 and B01 |
| B07 | ChurchReport-specific LINE integration | LINE binding/profile providers, notification services, `PushUtility`, `ReplyUtility`, `LineUtilityClass`, QR LINE calls, legacy RichMenu catalog | Call-site convergence, product/SDK boundary leakage, client lifetime, recipient identity, duplicate push/reply/rich-menu behavior | ChurchReport integration tests plus fake LINE processor; exclude reusable SDK/workflow internals | High; after F04-F07 and B01 |

### Cross-Cutting Platform Modules

These modules affect all business slices. They should produce constraints and
shared fixes, but they must not become excuses for a repository-wide mixed
change set.

| ID | Module | Primary boundary | Diagnostic focus | Optimization and validation boundary | Risk / order |
|---|---|---|---|---|---|
| X01 | Host composition, middleware, routes, and lifetimes | `Program.cs`, `Startup.cs`, `Startup.Caching.cs`, middleware/filter registration and route table | DI lifetimes, middleware order, startup cost, duplicated registration, route compatibility, failure behavior | Host startup smoke tests, service resolution tests, route snapshots | High; after foundation inventory, before business implementation |
| X02 | Cache, performance, logging, health, and diagnostics | caching/performance/monitoring services, diagnostics, logging, profiling middleware/controllers | Cache ownership and limits, metric accuracy, logging cost, health-check quality, profiling overhead | Focused component tests plus load/profiling baseline; measure per business module | High; early baseline, optimize iteratively |
| X03 | Razor UI and static assets | `Views`, custom `wwwroot/js`, custom `wwwroot/css`, DevExtreme integration | View/controller contract drift, duplicated scripts/styles, payload size, caching, accessibility, client errors | Browser workflow tests and asset-size/performance budgets per business module | Medium; after backend contracts stabilize |
| X04 | Configuration, secrets, environments, and deployment | appsettings variants, publish scripts, environment/provider selection, external endpoints | Secret ownership, configuration duplication, validation, production/test separation, deployment reproducibility | Secret-free config validation, startup validation, deployment smoke tests; never expose values | Critical; immediate governance |

## Physical Versus Logical Isolation

### Already Physically Isolated

- F02-F09 mostly have project-level boundaries.
- Their diagnostics can begin from project references, public contracts, and
  corresponding test projects.
- They still require consumer checks in ChurchReport to detect leaked
  responsibilities.

### Logical Boundaries Inside ChurchReport

- B01-B07 and X01-X04 are not independent projects.
- Their first deliverable must be a path and call-flow manifest.
- A directory name alone is insufficient because each business flow crosses
  controllers, models, services, tools, CRM connectors, views, and settings.

## Anti-Boundaries

Do not use these as optimization modules:

1. The entire `SpeechMessageProducts.ChurchReport` project.
2. All Controllers, all Models, or all Services as separate modules.
3. All LINE code as one module.
4. All CRM-related code as one module.
5. All payment code as one module.
6. All tests as one undifferentiated cleanup task.
7. A repository-wide "performance optimization" task without per-module
   baselines and owners.

These groupings mix unrelated contracts and make improvements impossible to
attribute or validate.

## Recommended Work Order

### Wave 0: Establish Trustworthy Baselines

- F01 repository/build/test governance.
- X04 configuration and secret governance.
- X01 composition-root and lifetime map.
- X02 measurement, caching, logging, and profiling baseline.

### Wave 1: Shared Foundations

- F02 Dataverse infrastructure.
- F03 ToolUtility CRM operations.
- F04-F06 LINE SDK, processor, and workflows.
- F08-F09 payment core and reusable host/workflow adapters.

### Wave 2: Identity And Core Product Data

- B01 identity/session/access.
- B02 member/contact/onboarding.
- B03 small groups and reporting.
- B06 list/reference/administration.

### Wave 3: Integrated Operational Workflows

- F07 RichMenu engine.
- B07 ChurchReport LINE integration.
- B04 attendance/appointment/equipment/QR.
- B05 donation/fee/payment product workflow.

### Wave 4: User Experience And System Validation

- X03 UI/static assets by business workflow.
- Cross-module load, memory, socket, authorization, and regression validation.
- F01 lifecycle decisions for legacy and out-of-solution projects.

## Required Per-Module Deliverables

Every module should independently produce:

1. `analysis.md`: files, contracts, dependencies, data flow, tests, and current
   behavior.
2. `diagnosis.md`: verified findings separated from runtime hypotheses, with
   severity and evidence.
3. `optimization-plan.md`: approved changes, expected metrics, compatibility,
   rollback, and test commands.
4. Focused implementation commits only after user approval.
5. Before/after evidence tied to that module's own acceptance criteria.

## External Analysis Status

The CCG runner completed with degraded fallback on 2026-07-10:

- Claude produced usable architecture analysis.
- Gemini returned a provider quota/billing 403 and produced no output.
- The map above therefore combines Claude's single-model result with local
  repository inspection; it is not a completed dual-model cross-validation.

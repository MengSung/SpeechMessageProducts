# Phase 0 Migration Package Selection

Date: 2026-07-25  
Task: `dynamics-connection-compatibility`  
Status: Phase 0 readiness proposal (planning only; no Phase 1 implementation started)

## Why stop unbounded call-site expansion here

The Organization-call matrix now has **70 evidence-backed rows**. High-signal
clusters for connection runtime, fees/payments, MemberInfo, list download,
present-record integrate paths, and auth lookups are represented.

Continuing to add every branch variant now has diminishing value. Remaining
uncovered source is mostly:

- same operation shape with different UI keys/branches
- thin controllers that only call already-normalized managers
- interface-only services without concrete implementations in this repo snapshot

Phase 0 therefore shifts from "add more rows" to "choose the first bounded
migration packages and define entry criteria."

## Current evidence snapshot

| Metric | Value |
| --- | --- |
| normalizedCallSites | 70 |
| mapped-pending-evidence | 54 |
| temporary-legacy | 16 |
| high-signal candidates | 33 |
| no-SDK scanner mode | report-only |
| no-SDK findings | 1072 |
| version/smoke evidence | mostly metadata-only / not-started |

### Cluster coverage

| Cluster | Approx rows | Notes |
| --- | --- | --- |
| list | 17 | Strong read coverage; dynamic FetchXML and unresolved service interface remain temporary-legacy |
| memberinfo | 12 | Good first product package candidate |
| payments | 9 | Includes financial writes and one credential/token write |
| fee/fees | 7 | Best first bounded financial read/write candidate |
| runtime | 6 | Foundation required before any product package |
| newperson/contact | 5 | Depends on list membership + present-record orchestration |
| presentrecord | 2 | Upload path is temporary-legacy attribute-bag |
| auth | 2 | Account password comparison is temporary-legacy credential risk |
| other | remaining | metadata, appointments, diagnostics, generic blocked CRUD |

## Hard constraints carried into package selection

1. Final architecture is **Gateway Web Service by default**, Embedded optional via product JSON.
2. No SDK / WCF / WS-Trust / SOAP / per-user CRM session pooling in final design.
3. No generic entity CRUD public capabilities.
4. No free-form FetchXML, associationName, or attribute-bag APIs in final design.
5. Zero-tolerance for session/profile/token/credential leakage and memory leakage.
6. Existing solution topology remains `SpeechMessageProducts.sln`; no mandatory separate Dynamics solution.
7. `PowerPlatform.Dataverse.Client` stays until consumers migrate; deletion is a later gate, not Package 0/1 work.

## Recommended package order

### Package 0 — Runtime foundation (must precede product packages)

**Goal:** profile-scoped connection/admission/health without returning raw CRM clients.

| Include now | ORG-CALL | Why |
| --- | --- | --- |
| Profile acquire / borrow replacement | 00001, 00021 | Product currently borrows `IOrganizationService` |
| Health probe | 00003, 00004 | WhoAmI / pool validation |
| Auth client factory removal target | 00002 | Temporary-legacy; must disappear from product surfaces |
| Option-set metadata | 00040 | Shared dependency for MemberInfo and forms |

**Explicitly out of Package 0**

- Generic AssignRequest (00046)
- TimedOrganizationService decorator (00013)
- Any product business entity write

**Exit criteria**

- Gateway/Embedded can authenticate to CE 8.2 and 9.1 using config profiles
- Products can invoke health/metadata without holding `IOrganizationService`
- Capacity/admission and disposal guarantees exist for pool/token material
- No per-user CRM session objects retained across requests

### Package 1 — Fee read package (recommended first business package)

**Goal:** first bounded read-heavy financial capability set with server-owned FetchXML/templates.

| Include | ORG-CALL | Capability |
| --- | --- | --- |
| Dedication fees by contact | 00005 | `fee.dedication.retrieve.by.contact` |
| Dedication fees by contact + date range | 00006 | `fee.dedication.retrieve.by.contact.date.range` |
| Fees by dedication booking period | 00064 | `fees.retrieve.by.dedication.period` |
| Fee editor load by disciple lesson | 00066 | `fees.editor.load.by.disciplelesson` |
| Stor lessons by contact/disciple lesson (supporting reads) | 00061, 00062 | lesson support for fee screens |

**Why first**

- Clear entity focus (`new_fee`, supporting `new_stor_lessons`)
- Mostly reads; lower blast radius than payment completion writes
- Already has concrete FetchXML evidence
- Directly addresses ChurchReport fee screens without requiring full MemberInfo migration

**Explicitly deferred from Package 1**

- Fee editor writes (00048, 00067)
- Payment completion writes (00036, 00037)
- In-memory staging pseudo-call (00050)
- Card profile write (00049)

**Exit criteria**

- Operation registry entries exist for all included IDs
- CE 8.2 and 9.1 smoke evidence moves from `not-started` to at least `smoke-passed`
- Typed parameters only; no caller FetchXML
- Financial-audit logging without payload leakage
- ChurchReport can call Package 1 through gateway/embedded adapter for one selected screen path

### Package 2 — MemberInfo basic read/write package

**Goal:** first product UX package with field-limited contact operations.

| Include | ORG-CALL | Notes |
| --- | --- | --- |
| Ungrouped contact page | 00022 | high-traffic read |
| Commitment counts | 00024 | function/read support |
| Contact image read/write | 00028, 00029 | binary field-limited |
| Basic info update | 00030 | phone/address/status fields |
| LINE profile update | 00023 | field-limited write |
| Present records by contact | 00026 | detail tab |
| Stor lessons by contact | 00027 | detail tab; can share Package 1 lesson reads |
| Small-group descriptors/memberships | 00031, 00032 | tree/grid support |
| List names by contact IDs | 00025 | batch read |
| Relation goals | 00033 | connection read |
| Metadata option-set | 00040 | shared |

**Deferred**

- Full church-member paging helper variants if they do not change operation shape
- Any generic Retrieve/Update path

**Exit criteria**

- MemberInfo selected screens no longer need raw `IOrganizationService` for included operations
- Writes are field-allowlisted and idempotent where required
- Image endpoints keep product-side cache but gateway owns CRM retrieve/update

### Package 3 — List membership and catalog package

**Goal:** replace marketing-list read/action helpers used by download and MemberInfo.

| Include | ORG-CALL |
| --- | --- |
| Add/remove members | 00011, 00012 |
| Static members by listId / reverse membership | 00017, 00019 |
| Catalog reads | 00014, 00015, 00016, 00020, 00065 |
| Member count | 00047 |
| App-named membership by contact | 00057 |
| User/role list download reads | 00053, 00054 |

**Blocked / temporary-legacy until redesigned**

- Dynamic stored FetchXML members (00018)
- Open associationName query (00058)
- Unresolved `IListManagementService` interface (00070)
- Broad ListManagementDataManager field bag updates (00035)

### Package 4 — Present-record package

| Include later | ORG-CALL | Gate |
| --- | --- | --- |
| Download create | 00068 | after Package 0/3 ownership and list context exist |
| Upload upsert | 00069 | only after attribute-bag retry design is replaced by fixed projection |

### Package 5 — Payments completion package (high risk)

| Include later | ORG-CALL | Gate |
| --- | --- | --- |
| Fee update after payment | 00036 | server idempotency by payment identity |
| Recurring dedication completion | 00037 | multi-step compensation rules |
| Dedication booking read/cancel | 00041, 00042, 00059 | after Package 1 fee reads |
| Card profile update | 00049 | security review + secret handling redesign |
| Open contact create/update bag | 00038 | split into named capabilities first |

### Package 6 — New-person onboarding package

Depends on Packages 0, 3, 4 and resolution of list/present service wiring.

| Include later | ORG-CALL |
| --- | --- |
| Full onboarding | 00044 |
| Transfer between lists | 00051 |
| Assign owner | 00045 |
| Image update | 00034 |
| Current group retrieve | 00052 |

## First consumer recommendation

**First consumer product:** `SpeechMessageProducts.ChurchReport`  
**First business package:** Package 1 (fee reads)  
**Foundation prerequisite:** Package 0 (runtime)

Rationale:

1. ChurchReport is the densest current CRM consumer.
2. Fee reads are bounded, financially meaningful, and already template-shaped.
3. They avoid the highest-risk surfaces first (card tokens, dynamic FetchXML, generic CRUD, plaintext app password).
4. Success creates a repeatable registry/smoke pattern for MemberInfo next.

## Phase 0 remaining checklist before Phase 1 code

Phase 0 can be considered ready for Phase 1 project scaffolding when all of the following are true:

1. Package 0 and Package 1 row sets above are accepted.
2. Temporary-legacy exclusions for Package 1 are accepted.
3. CE 8.2 / 9.1 target profile config shape is frozen in design artifacts.
4. Operation registry comparison fields remain stable (`capabilityOperationId`, typed parameters, encoding contexts, template hash, version evidence, audit/idempotency).
5. No-SDK scanner remains report-only until a migrated source root exists.
6. Dual-model review of this package selection is completed or explicitly waived by the owner after local review.

Still **not** required before Phase 1 scaffolding:

- normalizing every remaining ChurchReport helper branch
- deleting `PowerPlatform.Dataverse.Client`
- implementing gateway code

## Open blockers / debts to track into Phase 1 design, not Package 1 scope

| Debt | ORG-CALL | Impact |
| --- | --- | --- |
| Plaintext app password compare | 00055 | auth redesign; not Package 1 |
| Open associationName query | 00058 | security; list package redesign |
| Dynamic list stored FetchXML | 00018 | list package redesign |
| Upload present-record attribute bag | 00069 | present-record package redesign |
| Unresolved list/present service interfaces | 00070 + interface candidates | new-person package blocked until wiring found/replaced |
| Generic entity CRUD | 00007-00010 | permanent blocked shapes |
| Card token field write | 00049 | payments security package |

## Decision asked of the project owner

Recommended decision:

1. Accept Package 0 + Package 1 as the first implementation slice after Phase 0 closeout.
2. Keep Package 2 (MemberInfo) as the second product slice.
3. Do not expand Package 1 to include fee writes or payment completion yet.

If accepted, next planning step is Phase 1 project/contract scaffolding in `SpeechMessageProducts.sln` for runtime + Package 1 registry only.
## Owner decision

Accepted on 2026-07-25 by project owner instruction:

- Accept Package 0 + Package 1 as the first implementation slice.
- Proceed to Phase 1 scaffolding in SpeechMessageProducts.sln.
- Keep scanner report-only and do not delete PowerPlatform.Dataverse.Client yet.
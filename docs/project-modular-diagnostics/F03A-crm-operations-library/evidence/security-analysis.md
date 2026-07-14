# F03A Security Analysis

Status: COMPLETE
Mode: DIAGNOSIS_ONLY

## Confirmed Findings

### F03A-SEC-001 Repository Credential Is An Active Connection Fallback

`ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:47-52` defines
configuration properties with literal server, organization, administrator,
password, and domain fallbacks. The constructor at lines 79-87 immediately
calls `InitializeCrmConnection`; lines 138-145 construct the endpoint and pass
the fallback password to `CreateOnPremiseClient`.

Reachability is not hypothetical:

1. `Startup.cs:157` supplies configuration.
2. `ServiceCollectionExtensions.cs:32-35` registers the provider singleton.
3. `ToolUtilityProvider.cs:30-33` calls `ToolUtilityFactory.GetInstance`.
4. `ToolUtilityFactory.cs:50-68` constructs one process-wide
   `ToolUtilityClass`.
5. Missing individual `CrmConnection:*` keys activate the literal fallback.

Guard/counter-evidence: a configured value overrides each fallback, and no
proof was collected that the exposed credential is currently accepted by the
server. That does not remove repository disclosure or the active
missing-configuration path, so severity is High rather than an asserted
currently exploitable Critical.

### F03A-SEC-002 Authentication Returns Credential-Bearing Full Contact Rows

`ContactService.cs:201-215` queries active contacts by account with
`ColumnSet(true)`, compares `new_app_pass` as a plaintext string, and returns
the entire `Entity`. The compatibility path is exposed at
`ToolUtilityClass.Contact.cs:60-61`. Concrete consumers use the returned entity
as the authenticated actor, including `DownloadListManager.cs:497-505`,
`UploadIntegrateData.Core.cs:297-304`, and `NewPerson.cs:444-452`.

Data/control flow:

1. Business workflow receives account and password.
2. F03A query retrieves every service-readable contact attribute.
3. Credential equality occurs inside the CRM row object.
4. On success, the row, including the password field and unrelated PII, crosses
   into the business module.
5. The business module then derives identity/owner/full-name state.

The async overload at `ContactService.cs:76-93` optionally caches the entity
under account only, not password. Counter-evidence: F03Q currently constructs
`ContactService` without a cache at `ToolUtilityFacade.cs:143`, so cache
poisoning is not claimed as an active issue. It is a guard requirement before
future cache wiring.

Authorization boundary: F03A owns credential verification/data minimization;
B01 owns session, claims, and route authorization. Generic F03A CRUD cannot
enforce each business authorization rule, but it must not return a broader
credential-bearing object than the identity contract requires.

## Input And Injection Review

Several F03A services interpolate FetchXML values. Direct user-controlled
reachability was not established for the appointment, lessons, fee,
meeting-statistics, or present-record helpers, so they remain a review
hypothesis rather than a confirmed injection finding.

The known donation search is positive counter-evidence:
`ContactService.cs:287-303` applies `SecurityElement.Escape` to all six inputs,
and lines 305-306 impose `top='100'`. The consumer call is
`DonationKeyInDedicationService.cs:230-236`.

## Attachment Boundary Review

`AttachmentService.cs:38-67` exposes upload/download helpers and downloads
annotations with all columns, including document data. No production consumer
was found; current references are tests and the compatibility facade. Size,
MIME, and authorization checks are therefore recorded as required contract
guards before exposure, not a confirmed reachable vulnerability.

## Rejected Security Candidates

- Static singleton equals cross-user leakage: rejected; no mutable per-user
  F03A field was proved.
- Connection pool retains credentials without disposal: rejected;
  `CrmConnectionPool.cs:329-355` disposes unhealthy connections and
  lines 357-463 dispose timer, semaphore, and pooled clients.
- Generic CRUD lacks per-route authorization: ownership handoff to B01/B
  modules. F03A must accept authorized, narrowed operations but cannot infer
  every business policy.
- Query logging leaks passwords: no reachable password-bearing QueryService
  invocation was proved.

## Required Security Contracts

1. No fallback secret; validated startup options and X04A-managed injection.
2. Narrow authentication result containing identity/status only.
3. One-way credential verification or migration away from CRM plaintext
   passwords.
4. Explicit query projections and attachment limits.
5. Escaping/parameterization for every dynamic FetchXML value.
6. B01 authorization before invoking mutation/download operations.

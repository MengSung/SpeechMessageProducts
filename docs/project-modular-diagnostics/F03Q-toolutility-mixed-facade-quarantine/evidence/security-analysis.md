# F03Q Security Analysis

Status: COMPLETE
Mode: DIAGNOSIS_ONLY

## Confirmed Finding

### F03Q-SEC-001 Plaintext CRM Credential Remains In Source

Source:

- `ToolUtility/Core/ToolUtilityFacade.cs:91` contains the CRM endpoint shape.
- `ToolUtility/Core/ToolUtilityFacade.cs:92` contains an administrator identity.
- `ToolUtility/Core/ToolUtilityFacade.cs:93` contains a literal password.
- `ToolUtility/Core/ToolUtilityFacade.cs:95` shows the credential's intended
  connection use.

Source/sink:

- Source: literal credential committed in a F03Q owner file.
- Sink: every repository clone, history mirror, indexer, backup, and reader.

Identity/credential boundary:

- The value is an administrator-shaped CRM credential, not per-request data.
- Runtime authentication guards are irrelevant because disclosure occurs at
  source access time.

Reachability:

- Directly reachable by reading the file or Git history.
- The same password is active in the F03A compatibility fallback at
  `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:51` and used at
  lines 138-144, which is counter-evidence against treating the F03Q comment as
  harmless sample text.

Impact and owner:

- F03Q owns removal from its source.
- F03A owns removal of the active fallback.
- X04A owns rotation and validated runtime secret supply.
- Removing one copy does not complete the cross-owner remediation.

## Mixed Credential And State Review

The facade stores one mutable credential-derived client in
`ToolUtility/Core/ToolUtilityFacade.cs:56`. Public methods accept server,
organization, domain,
username, and password and replace that client at lines 297-332. The same
client feeds CRM services and the F03B `ILineMessageService` at lines 140-158.

This is a real mixed lifetime boundary. It is not promoted as a separate
Critical security issue because:

- repository search found no current product caller of the public connection
  switch methods;
- no per-user credential or tenant selector is stored in the F03Q file;
- no current cross-request switch was proved.

The stale-client/resource behavior when the public API is invoked is retained
as `F03Q-PERF-001` with low likelihood and security urgency rather than
exaggerated into confirmed tenant leakage.

## Guards And Counter-Evidence

- Services are lazy; construction alone does not authenticate all services.
- No access token, LINE channel secret, reply token, or LINE HTTP client is
  stored in the F03Q owner file.
- The F03Q `ILineMessageService` writes CRM data and is not direct LINE
  transport.
- `ToolUtilityClass` normally creates the facade once with a single CRM service.
- The static singleton alone does not prove cross-user leakage because the
  F03Q owner file contains no mutable user/session field.

## Rejected Security Candidates

1. LINE message body persistence is automatically unauthorized.
   - Rejected: source proves persistence, but no retention/access/data
     classification contract proves a leak.
   - Handoff: F03B/B07 must define whether full message content is required,
     redacted, or omitted.
2. Public connection switching currently leaks tenant credentials across live
   requests.
   - Rejected as current-production fact: no repository caller was found.
   - Conditional lifetime/race behavior remains confirmed in PERF-001.
3. F03Q directly exposes LINE channel credentials.
   - Rejected: no LINE credential exists in the owner file.
4. Logger state leaks sensitive data.
   - Rejected: `_logger` is typed as `object`, but no F03Q owner path logs
     credentials or LINE content.

## Required Security Handoffs

- F03Q: remove the plaintext comment in an isolated change.
- F03A: remove active fallback and own CRM facade/client contract.
- X04A: rotate credential and validate secret injection.
- F03B/B07: decide and test LINE audit data minimization and retention.
- F01D: add a secret-scan gate after its test/build governance repair.

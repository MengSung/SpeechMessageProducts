# F03Q Performance Analysis

Status: COMPLETE
Mode: DIAGNOSIS_ONLY

## Confirmed Finding

### F03Q-PERF-001 Connection Switching Orphans Initialized Services

Evidence:

- `_organizationService` is mutable at
  `ToolUtility/Core/ToolUtilityFacade.cs:56`.
- `InitializeServices` creates lazy wrappers around the current field at
  lines 137-158.
- `ReinitializeServicesIfNeeded` checks whether any service was created and
  replaces all wrappers at lines 164-178.
- Public connection methods replace the CRM client before reinitialization at
  lines 297-332.
- No old lazy value or old CRM client is disposed during replacement.
- Final disposal reaches only the current client at line 126.
- No lock coordinates service call, connection replacement, lazy replacement,
  or disposal.

Cost source:

- WCF/CRM proxies and service wrappers can remain undisposed after replacement.
- A concurrent call can resolve an old or new lazy wrapper during mutation.
- Replacement allocates a complete set of lazy wrappers even if only one
  service family is relevant.

Frequency:

- Repository search found no current product caller of the public switch APIs.
- The defect is therefore conditional and scored low for frequency, but it is
  statically confirmed behavior of a public API.

Measurement shape for a future authorized task:

- Disposable fake old/new clients count calls and disposal.
- Initialize one CRM service and the LINE service.
- Replace the connection.
- Assert the old graph is disposed exactly once and receives no later call.
- Run concurrent service access/replacement with deterministic barriers.
- This diagnosis does not execute that test.

## Unnecessary CRM And LINE Work Review

F03Q retains an `ILineMessageService` field at line 64 and creates its lazy
wrapper at line 146. The direct F03Q LINE method at lines 526-529 has no current
production caller; only the F03Q test calls it. Therefore no current production
latency issue is claimed for that method.

The actual legacy LINE send path is cross-module:

- `ToolUtility/PushUtility.cs:58-64` writes CRM audit state before one LINE push.
- `ToolUtility/PushUtility.cs:82-89` writes audit state before one multicast.
- `ToolUtility/ToolUtilityPartials/ToolUtilityClass.Line.cs:72-99` performs one
  CRM contact query and one CRM create per multicast recipient.

That flow can produce `2N` CRM operations before one LINE multicast and can
block LINE transport when CRM audit fails. The loop and send orchestration are
F03B-owned, so this is recorded as an F03B handoff rather than mis-owned as an
F03Q confirmed performance issue. F03Q's responsibility is to stop presenting
the CRM and LINE audit contracts as one facade.

## Guards And Counter-Evidence

- Service implementations are lazy; facade construction does not create CRM
  network connections or execute all service calls.
- `ReinitializeServicesIfNeeded` does nothing if no service lazy was created.
- No current repository caller of F03Q connection switching was found.
- Some service classes are not `IDisposable`; do not assume every discarded
  wrapper owns an unmanaged resource.
- The direct F03Q `CreatePushLineMessage` path is currently test-only.

## Rejected Performance Candidates

1. Eighteen lazy fields are by themselves a major startup regression.
   - Rejected: wrapper allocation exists but no meaningful startup cost was
     established.
2. Direct F03Q LINE method adds latency to every production send.
   - Rejected: no production caller found.
3. All facade methods perform N+1 queries.
   - Rejected: method families delegate to different owners and require
     operation-specific proof.
4. Disposal always double-disposes the same proxy in production.
   - Not promoted: `ToolUtilityClass` does dispose facade then its public CRM
     field, but the static factory/provider lifetime does not prove a normal
     shutdown disposal path. The ambiguous ownership is covered by EXT-001 and
     future lifetime tests.

## Handoffs

- F03A/F02: immutable CRM client and explicit disposal ownership.
- F03B: measure and redesign audit-before-send and multicast `2N` CRM work.
- X01: host lifetime/disposal validation after owner-specific registration.
- X02C: optional runtime profiling after executable gates exist.

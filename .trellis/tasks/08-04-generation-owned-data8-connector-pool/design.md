# P3 Data8 Connector Pool Design

## Boundary

`SpeechMessage.Dynamics.Abstractions` owns SDK-free operation, client, lease, pool, and router contracts. `SpeechMessage.Dynamics.Connectors.Data8` owns the generation-isolated pool and a factory seam for creating Data8 clients. No product or Gateway project receives CRM SDK types or credential values.

## Lifecycle

`Data8ConnectorPool` receives one immutable `ResolvedProfile`, one existing `IOrganizationAdmissionManager`, and one client factory. Every acquire first creates a bounded `DispatchEnvelope` and asks the existing admission manager for a permit. Only after the permit succeeds does the pool consume a local connection capacity slot and create or take an idle client.

The returned lease captures the originating `(ProfileAlias, GenerationId)` and the admission permit. A healthy lease returns its client to that same pool. A faulted, cancelled, timed-out, or pool-draining lease disposes its client and never re-enqueues it. Lease disposal always releases the permit in a finally path.

Drain changes the pool state before waiting. New acquisitions fail closed. Existing leases may finish, and their clients are disposed rather than returned to idle. Once active leases reach zero, all idle clients are disposed and the drain task completes. Dispose is idempotent and is equivalent to drain plus final resource cleanup.

## Capacity and isolation

The injected `IOrganizationAdmissionManager` remains the only Organization-level budget. The pool's local semaphore is only a bounded container limit; it is never used as a second aggregate Organization budget. Separate pools can therefore share one admission manager while retaining separate Profile/Generation client state.

No request identity, token, cookie, credential, endpoint, Organization ID, or mutable options object is stored in a pool key, lease, client wrapper, or idle queue.

## Router

`Data8ConnectorRouter` maps only `ResolvedProfile.ConnectorKind == Data8` to a registered generation pool. Official Worker kinds are rejected until their own pools are registered. There is no fallback or request-time connector selection.

## Failure handling

Factory failure rolls back the local slot and admission permit. Client disposal failure does not prevent permit release or other cleanup; multiple failures are aggregated. Cancellation registration is not retained after acquisition completes.

# Phase 2 capacity/admission verification

Date: 2026-07-25

## Implemented
- CanonicalOrganizationCapacityKey / OrganizationAdmissionKey / RuntimeHostSlotLeaseNamespace
- OrganizationAdmissionPlan with LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumRuntimeHosts)
- InMemoryRuntimeHostSlotCoordinator (IsDurable=false; single-host/dev only)
- OrganizationAdmissionManager:
  - bounded local queue
  - local in-flight semaphore
  - per-workload cap
  - envelope size / deadline checks
  - host-slot acquire/renew/release
  - permit dispose always releases capacity
- ControlledOperationExecutor now requires admission before Web API dispatch
- Gateway/Embedded default admission settings

## Tests
- SpeechMessage.Dynamics.Tests: 26 passed
- Includes queue-full, workload-cap, permit-release, host-slot limit

## Explicit non-goals of this step
- Durable multi-host coordinator backend (still required before multi-instance production readiness)
- ChurchReport consumer migration
- PowerPlatform.Dataverse.Client deletion
ROLE: reviewer

Review the Phase 0 Dynamics no-SDK migration artifact updates in this worktree.

Focus only on:
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-verification.md
- .trellis/tasks/07-23-dynamics-connection-compatibility/phase0-inventory.md
- related task notes under .trellis/tasks/07-23-dynamics-connection-compatibility/task.json and .ccg/tasks/dynamics-connection-compatibility/task.json

Context and hard constraints:
1. Phase 0 is inventory/evidence only. Do not require creating Dynamics projects yet.
2. Do not require deleting PowerPlatform.Dataverse.Client yet.
3. Final architecture is Gateway Web Service by default, Embedded mode via product JSON.
4. No SDK / WCF / WS-Trust / SOAP / per-user CRM session pooling in final design.
5. capabilityOperationId pattern is lowercase alphanumeric segments separated by dots only.
6. Generic entity CRUD must remain temporary-legacy blocked, not final public capabilities.
7. Prefer fee read path ORG-CALL-00005/00006 as first bounded product candidate.

Review for:
- Correctness of evidence-backed normalizedCallSites rows
- Schema/contract risks
- Security/leakage risks (credentials, session pooling, generic CRM surface)
- Missing high-signal call sites that should have been included already
- Whether remaining Phase 0 next steps are correct

OUTPUT:
Critical / Warning / Info findings with concrete file references and recommended fixes.
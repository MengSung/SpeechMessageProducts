# Architecture analysis request

Review the proposed local-only P7 child `ORG-CALL-00003 runtime.health.whoami`.

Current evidence: the registry and Data8 executor already support the fixed, zero-parameter WhoAmI operation and emit a closed `OperationResponseData.ForWhoAmI` branch. ProductClient has no runtime health client. The legacy source is ToolUtility `CrmConnectionService.ValidateConnection(IOrganizationService)`; no consumer, feature gate, CE, traffic or ToolUtility migration is in scope.

Proposed change: add a stateless typed ProductClient interface/implementation plus additive DI registration. Its sole method accepts bounded deployment-owned profile alias/workload subject scalars, sends exactly `OperationIds.RuntimeHealthWhoAmI` with no parameters/idempotency key through the injected executor, validates exact operation id/CE 9.1/WhoAmI response and non-empty GUID scalars, and returns an immutable DTO. It must retain no request/profile/response/identity state and must never expose SDK, HTTP, endpoint, credential, connector or raw error data.

Assess correctness, isolation, lifecycle, failure behavior, compatibility and minimal test plan. Report Critical/Warning/Info. Do not suggest consumer wiring, CE calls, feature changes, legacy fallback, retry or scope expansion.

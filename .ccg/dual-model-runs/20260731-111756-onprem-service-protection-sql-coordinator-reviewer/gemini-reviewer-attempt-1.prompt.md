ROLE_FILE: <user-home>\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: onprem-service-protection-sql-coordinator

## Repository
<worktree-root>

## Request
# Reviewer task: CE on-prem service-protection and SQL coordinator assessment

## Scope

Perform a read-only architecture review. Do not edit code, configuration, task files, specifications, or infrastructure. Assess only whether the attached Claude recommendation is technically correct for this repository and the user's CE 8.2/9.1 on-premises deployment.

## User question

The user runs two Dynamics 365 Customer Engagement on-premises IFD environments:

- CE 8.2 (`jesus`)
- CE 9.1 (`sunnyvalechback`, Hyper-V VM)

Claude corrected an earlier statement and argued:

1. Dataverse service-protection limits (6,000 requests/300 seconds, 1,200 seconds execution time/300 seconds, 52+ concurrent requests) are cloud managed-service limits and probably do not apply to CE on-premises.
2. The gateway should therefore stop calling its capacity budget a CRM service-protection budget and should measure actual IIS/ASP.NET/SQL/CRM capacity instead.
3. `RequireDurableHostCoordinator` should be `false` for now and `InMemoryRuntimeHostSlotCoordinator` should be used because the gateway process count is fixed.
4. SQL coordination should be retained but disabled until a future Central Gateway multi-replica deployment.
5. The absence of `x-ms-ratelimit-burst-remaining-xrm-requests` and `x-ms-ratelimit-time-remaining-xrm-requests` response headers would prove that service protection does not exist.

The user requested assessment only and explicitly requested no product/configuration change.

## Authoritative repository facts

- Central Gateway is the production target for five to ten products and the accepted design requires at least two production Gateway replicas.
- Local Gateway is the immediate development/isolated path, using the same `Gateway` execution mode and REST contract.
- `SpeechMessage.Dynamics.Gateway/Program.cs` registers `SqlRuntimeHostSlotCoordinator` in every non-Testing environment and runs `DynamicsGatewayReadinessService`, which verifies the dedicated control-plane schema before runtime initialization.
- `SpeechMessage.Dynamics.Gateway/appsettings.json` sets `AggregateMaxInFlight=24`, `MaximumRuntimeHosts=6`, and `RequireDurableHostCoordinator=true`.
- Development uses a dedicated `SpeechMessageDynamicsControlPlane` LocalDB database with Windows integrated authentication. It is explicitly not CRM SQL and stores only runtime-host capacity/epoch/fencing/quarantine state.
- `InMemoryRuntimeHostSlotCoordinator.IsDurable=false` and its own documentation says it only protects one process; it cannot enforce cross-process, restart, rolling-update, blue/green, or stale-host fencing.
- The accepted ADR and spec use durable coordination to prevent concurrent Gateway/Local/blue-green/draining hosts from multiplying the physical organization's configured safe concurrency. This purpose exists independently of Dataverse online service-protection quotas.
- The aggregate budget value has not yet been proven against real CE 8.2/9.1 load. Current Phase 4 still requires real-server capacity, fault, soak, and performance evidence.
- `Package01FeeReadsEnabled=false` must remain unchanged.

## Microsoft documentation evidence checked locally

1. Dataverse service-protection article:
   https://learn.microsoft.com/en-us/power-apps/developer/data-platform/api-limits

   It is titled `Service protection API limits (Microsoft Dataverse)`, describes protecting the Microsoft Dataverse managed platform and shared resources, and says Dataverse determines the number of web servers as part of the managed service, including license factors.

2. CE on-premises Web API article:
   https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/developer/use-microsoft-dynamics-365-web-api?view=op-9-1

   It says Customer Engagement on-premises and Dataverse share the Web API surface, but it does not state that the Dataverse managed-service 6,000/1,200/52 enforcement applies to CE on-premises.

3. Microsoft Learn searches found no CE on-premises documentation that declares the Dataverse managed-service 6,000/1,200/52 limits for CE 8.2 or 9.1 on-premises.

4. The current Dataverse service-protection article documents `Retry-After` on 429 responses but does not document the two proposed `x-ms-ratelimit-*-xrm-requests` headers as a definitive detection method.

## Questions to answer

Classify findings as Critical, Warning, or Info and cite exact repository/document evidence.

1. Is it reasonable to conclude that the Dataverse managed-service 6,000/1,200/52 defaults should not be treated as an authoritative CE 8.2/9.1 on-premises quota? State confidence and caveats.
2. Is the stronger statement `the on-prem environments will not return 429` justified? Consider IIS, reverse proxies, CRM components, custom middleware/plugins, and generic overload/failure behavior.
3. Does absence of the two proposed `x-ms-ratelimit` headers prove that service protection or throttling is absent? If not, state what real smoke/load evidence is valid.
4. Is disabling the durable SQL coordinator justified merely because Dynamics is on-premises or because the expected process count is fixed? Distinguish:
   - one intentionally isolated single-process developer Local Gateway;
   - Central Gateway production with two replicas;
   - restart/rolling deployment/blue-green overlap;
   - multiple Local Gateways targeting the same physical organization.
5. Should the documents eventually rename `CRM service-protection budget` to a neutral term such as `validated organization capacity budget`, while retaining bounded admission, backpressure, 429/503 handling, and real load measurements?
6. Give a concise recommendation for the user now. No edits are authorized in this review.

## Required output

- Verdict on each Claude claim: correct, partially correct, or incorrect.
- Critical / Warning / Info findings.
- Explicit answer whether any immediate configuration/code change is warranted.
- Remaining evidence needed before choosing `AggregateMaxInFlight` or changing durable coordination.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.

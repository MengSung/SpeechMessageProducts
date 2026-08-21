# CRM attribution duplicate JSONL analysis

## Context

This is a Debug-only diagnostics correction for ChurchReport. The intended contract is that, for a request with CRM activity, the `[Perf]` profiler (`TimedOrganizationService`) and `dataverse-trace.jsonl` `request.end.crmCount` / `crmMs` describe the same CRM calls.

The current real trace reproduces a deterministic mismatch on legacy Factory/Ambient paths:

| request shape | `[Perf] crm.n` | JSONL `request.end.crmCount` |
|---|---:|---:|
| `/SmallGroup/IntegrateView/{LoginParameter}` | 10 | 20 |
| `/Home/ProcessLogin` | 30 | 58 |

On smaller direct paths, values match (for example `1 / 1` and `2 / 2`).

## Observed code flow

`AmbientGatewayOrganizationService` was deliberately changed to resolve the request-scoped `IOrganizationService`, so the ChurchReport Debug `TimedOrganizationService` decorator is not bypassed. It now delegates like this:

```csharp
public Entity Retrieve(...) =>
    Run(service => CrmOperationTrace.Measure(
        "Retrieve", entityName, () => service.Retrieve(...)));
```

The resolved service is:

```text
AmbientGatewayOrganizationService
  -> TimedOrganizationService
     -> GatewayOrganizationService
        -> CrmOperationTrace.Measure(...)
```

`DataverseTrace.CrmOperation` increments `request.end.crmCount`, while `TimedOrganizationService` updates `[Perf]` exactly once around the inner call. The real run recorded 51 `gateway.execute.enter` events but 92 `crm.op` events: the doubled Ambient calls explain 41 extra `crm.op` events, matching 20 + 30 versus 10 + 58.

## Constraints

- Preserve request/scope/lease isolation: no request scope, HttpContext, raw client, lease, user, or tenant state may become retained by the legacy Factory singleton.
- `AmbientGatewayOrganizationService` must continue resolving `IOrganizationService`, not `IDataverseGateway`, so Host decorators remain effective.
- Do not modify `Analyze-ChurchReportTraces.ps1`.
- Keep the fix minimal, explain needed documentation changes, and specify regression test(s) that prove one ambient public call produces exactly one `crm.op` / request-end count when its decorated inner service is the gateway path.
- Do not provide a patch. Analyze the root cause, identify any counterexamples, and give a precise minimal correction and test plan.

## Requested output

Critical / Warning / Info findings, then:

1. whether the nested `CrmOperationTrace.Measure` calls are confirmed as the root cause;
2. the safe minimal correction and why it preserves trace coverage;
3. exact regression-test strategy including fallback-scope lifecycle coverage;
4. any reason the observed arithmetic could have another cause.

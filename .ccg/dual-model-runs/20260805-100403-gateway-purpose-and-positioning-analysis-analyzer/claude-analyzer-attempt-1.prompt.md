ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: gateway-purpose-and-positioning-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# CCG architecture analysis: Gateway purpose and ToolUtility positioning

## Repository

`D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree`

## User's question

The user believed that ToolUtility could keep its existing Entity CRUD, update,
query, Fetch, Action, and Function APIs while choosing either Data8 or Gateway as
the backend for D365 CE 8.2 / 9.1. The actual design appears different. They ask:

- Why does Gateway exist?
- If they continue using ToolUtility, does that mean they cannot use Gateway?
- How will future products obtain the same practical CRM capabilities formerly
  provided by ToolUtility?
- Is the intended model to borrow a connector from Gateway and then use it to
  access D365 directly?

## Required evidence

Inspect the current repository rather than relying on names. In particular:

- `.trellis/tasks/08-05-gateway-purpose-and-positioning/prd.md`
- `docs/dynamics-connection-management-spec.md` (the 2026-08-04 superseding contract)
- `docs/dynamics-connection-management-plan.md`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/spec/backend/data8-generation-owned-connector-pool.md`
- ToolUtility public interfaces, CRM connection service/pool, and adapters
- Dynamics Abstractions, ProductClient, Embedded, Gateway, ControlPlane,
  Connectors.Data8, and Package01 operation registry
- ChurchReport Dynamics configuration and feature flags

Be alert to superseded or internally contradictory historical text. Distinguish
current executable code from old plans and from future roadmap.

## Questions to answer

1. What is the exact current product-facing contract of Gateway and Embedded?
2. Is ConnectionMode orthogonal to ConnectorKind? Explain the matrix precisely.
3. Can an HTTP Gateway lend an `IOrganizationService` or connector lease to a
   product? Separate physical/process limitations from deliberate policy.
4. Can ToolUtility currently switch between Data8 and Gateway? Cite the exact
   type-contract mismatch.
5. What value remains for DedicatedGateway with one product, and what additional
   value appears only with CentralGateway and multiple products?
6. What are 2–3 credible designs for future products that need ToolUtility-like
   convenience? Compare:
   - arbitrary generic CRM access,
   - registered business/capability operations,
   - a hybrid typed SDK/facade that can map selected calls to either Embedded or
     Gateway without exposing raw sessions.
7. For each design, assess session/tenant isolation, credential boundary,
   deterministic resource cleanup, latency/throughput/allocation implications,
   API evolution, testing burden, and migration cost.
8. Recommend the most coherent mental model and product direction, but identify
   which remaining choice is product intent and cannot be inferred from code.

## Output

Traditional Chinese. Use these sections:

1. `已證實的現況`
2. `Gateway 真正存在的理由`
3. `為何不是借連接器`
4. `ToolUtility 與 Gateway 的相容性結論`
5. `未來產品的 2–3 種方案與取捨`
6. `建議方向`
7. `需要詢問使用者的一個最高價值問題`
8. `證據（檔案與行號）`

Flag any Critical security, cross-session, cross-tenant, memory, connection, or
resource-lifecycle concern. Do not edit repository files.


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
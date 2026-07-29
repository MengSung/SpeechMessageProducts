ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: dynamics-local-vs-embedded

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Architecture analysis request: Local Gateway versus Embedded

## Repository and current state

- Repository: SpeechMessageProducts worktree.
- Products such as ChurchReport target .NET 10 and are developed in Visual Studio 2026.
- Current design has two product execution modes selected at startup from trusted JSON:
  - Gateway: product uses an HTTP product client to a deployable ASP.NET Core Gateway.
  - Embedded: product references `SpeechMessage.Dynamics.Embedded`, which runs the connector and its process-local pool in the product process.
- Current low-level connector is a custom no-SDK OData/Web API implementation in
  `SpeechMessage.Dynamics.WebApi`.
- The user has changed the central requirement: complete removal of Microsoft SDKs is no longer required.
  They prefer Microsoft official packages and want to remove the third-party Data8 WS-Trust project.
- The likely official legacy-compatible choice for the existing D365 9.1 On-Premises/IFD environment is
  `Microsoft.CrmSdk.XrmTooling.CoreAssembly` with `CrmServiceClient`, potentially hosted by a .NET Framework 4.8 Windows Gateway.
- The user's operational goal is to centralize Connection Pool ownership for 4-10 products.
- The user's development goal is simple: when opening/running ChurchReport in Visual Studio 2026, the Dynamics connection path should be easy to start, observe, debug, and stop.
- Phase 4 is only partially complete; Phase 5 production consumer enablement has not occurred and `Package01FeeReadsEnabled` remains false.
- Session isolation, cross-product credential isolation, bounded capacity, deterministic disposal, and no state/resource leakage are release blockers.

## Question

Analyze whether the revised design should:

1. Remove Embedded and use Central Gateway in production plus Local Gateway sidecars for development/isolated deployment.
2. Keep both Local Gateway and Embedded as supported modes.
3. Keep Embedded only as a deferred/experimental option that is not part of the initial release.

Clarify the exact architectural difference between Local Gateway and Embedded, especially:

- process boundary;
- target framework and official SDK compatibility;
- physical connection-pool ownership;
- Visual Studio debugging workflow;
- configuration and startup orchestration;
- security/credential boundary;
- lifecycle/disposal and crash blast radius;
- deployment and health-check complexity;
- performance/network-hop trade-off;
- testing burden and Phase 4-6 migration impact.

The analysis must consider that a Local Gateway can use the same HTTP contract as the Central Gateway and can be selected simply by changing the trusted Gateway endpoint from an internal DNS name to localhost. Embedded is a genuinely different in-process execution path.

## Expected output

Provide:

1. A decisive recommended architecture for this repository and environment.
2. Two alternatives and why they are weaker.
3. A recommended product JSON/configuration model.
4. A recommended Visual Studio 2026 startup/debug workflow for ChurchReport.
5. Which existing Phase 4/5 components remain reusable and what must change.
6. Explicit conditions under which Embedded should be retained, deferred, or removed.
7. Critical security/isolation/lifecycle constraints that must not be weakened by the redesign.

Do not modify source code. This is architecture analysis only.


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
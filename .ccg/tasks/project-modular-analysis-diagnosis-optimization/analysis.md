# Project Organization Analysis

The repository is a modular monolith with one large ASP.NET Core product host,
several extracted reusable project families, and multiple logical business
modules still embedded in the host.

The detailed, evidence-based module map is recorded at:

`.trellis/tasks/07-10-project-modular-analysis-diagnosis-optimization/architecture-map.md`

## Proposed Diagnostic Structure

- 9 shared-foundation modules.
- 7 ChurchReport business-capability modules.
- 4 cross-cutting platform modules.

Each selected module should become a child task with separate analysis,
diagnosis, and optimization approval gates.

## CCG Analysis Status

- Claude analyzer completed with usable output.
- Gemini analyzer was blocked by provider quota/billing status 403.
- The result is a degraded single-model fallback supplemented by local
  repository inspection, not a completed dual-model analysis.

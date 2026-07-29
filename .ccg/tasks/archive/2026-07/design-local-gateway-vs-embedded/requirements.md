# Local Gateway vs Embedded design clarification

## User goal

Explain whether ChurchReport and the other products should use Local Gateway,
Embedded, or both, with special emphasis on a convenient Visual Studio 2026
development workflow and a centralized production connection manager/pool.

## Constraints

- Do not modify ChurchReport or any product implementation in this task.
- Produce a detailed, color-separated HTML architecture visualization.
- Explain process boundaries, connection-pool ownership, configuration switching,
  lifecycle, framework compatibility, and the release recommendation.
- Preserve Embedded as a future option unless and until the revised design is
  approved; do not delete it as part of this design-only task.

## Recommended design to present for approval

- Keep one product-facing `Gateway` execution contract.
- In development, point that contract to a localhost Local Gateway and start it
  together with ChurchReport in Visual Studio.
- In production, point the same contract to the centralized Gateway endpoint.
- Defer Embedded from the initial supported release while retaining it as a
  possible later exception for a proven zero-hop or isolated deployment need.

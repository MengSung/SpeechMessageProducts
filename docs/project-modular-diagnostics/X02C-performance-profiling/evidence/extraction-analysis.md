# X02C Extraction Analysis

## Confirmed Extraction Issues

No confirmed extraction issue is raised for this diagnostic round.

## Candidate: profiling signal contract

Potential boundary:

- Inputs: route template/path category, total/action/CRM/phase elapsed timings, CRM call counts, threshold definitions.
- Outputs: normalized diagnostic event records consumed by middleware, controller reports, and parse-perf-log.ps1.
- Consumers: request profiler, performance monitor, parser script, and diagnostic endpoints.
- Dependencies: no business modules; read-only CRM operation names from timed wrappers.

Why not confirmed:

- Existing files are already cohesive enough for request/startup profiler and monitor responsibilities.
- Extraction would mainly standardize signals before changing behavior, so it is acceleration context rather than an immediate issue.
- No circular dependency or blocking ownership problem was proven.

# F01A Performance Analysis

Status: COMPLETE
Module: F01A - Solution, Build, and CI Governance
Mode: DIAGNOSIS_ONLY

## Cost Model Reviewed

F01A has no product request path. Its performance surface is developer and CI
latency: solution evaluation, restore/build/test repetition, external tool
download, artifact generation, and trigger frequency.

## Confirmed Static Costs

### Coverage Tool Installation Runs On Every Scheduled Job

- `.github/workflows/toolutility-tests.yml:37-41` uses `if: always()` and
  installs ReportGenerator globally on each fresh hosted runner.
- The install occurs even when restore, build, or test has already failed.
- The cost is one package resolution/download/install plus one process launch
  per scheduled run, and the version is not stable.

This cost is real, but it is not retained as a separate PERF issue because the
same lines are already the more consequential executable supply-chain problem
in F01A-SEC-001. The recommended exact-version tool manifest also removes the
repeated global-install design.

### Restore, Build, And Test Do Not Repeat Their Main Work

Counter-evidence rejected a prior redundant-build candidate:

- Restore is explicit at `.github/workflows/toolutility-tests.yml:28-29`.
- Build uses `--no-restore` at `.github/workflows/toolutility-tests.yml:31-32`.
- Test uses `--no-build` at `.github/workflows/toolutility-tests.yml:34-35`;
  local `dotnet test --help` confirms `--no-build` also implies no restore.

Therefore there is no confirmed triple-restore/triple-build issue.

## Rejected Performance Candidate

### F01A-PERF-C01 Legacy Solution Matrix Inflation

Static evidence:

- `SpeechMessageProducts.sln:43-58` declares 15
  configuration/platform combinations.
- The solution has 18 projects at `SpeechMessageProducts.sln:6-40`.
- The file contains 270 `Build.0` mappings, exactly 15 per project.
- `Debug_LearnCrm`, `DebugOracleConnector`, and `Test_Exchange_Service` occur in
  no tracked `.csproj`, `.props`, or `.targets` file; they occur only in the
  solution mappings.

Why it was rejected from confirmed issues:

- The repository has no CI matrix that iterates all 15 combinations.
- Visual Studio usage frequency and solution-load/build timing were not
  measured.
- Some x86/x64 or named configurations may still be required by an external
  operator even though project-specific conditions were not found.

The matrix is a maintenance and canonicalization concern, but a performance
claim requires timing data. It remains a low-priority validation candidate, not
F01A-PERF-001.

## Missing Optimization Evidence

The workflow has no timing telemetry, dependency cache measurement, or
before/after build baseline. Absence of NuGet caching was not promoted to an
issue because cache value depends on runner image contents, package graph, and
run frequency.

## Measurement Plan

If optimization is authorized:

1. Run three clean hosted-runner samples for restore, build, test, report
   generation, and artifact upload.
2. Record wall time and downloaded bytes per step.
3. Compare exact-version tool restore versus global install.
4. Measure solution load/evaluation for the current 15 configurations and a
   proposed canonical matrix.
5. Retain a PERF issue only if the median improvement is material and the
   removed configuration has no consumer.

## Conclusion

No standalone confirmed F01A performance issue survived the static evidence
threshold. One deterministic CI cost is merged into F01A-SEC-001, and the
solution-matrix candidate remains rejected pending measurement and consumer
proof.

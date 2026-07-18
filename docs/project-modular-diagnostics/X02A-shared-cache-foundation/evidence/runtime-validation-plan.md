# X02A Runtime Validation Plan

Mode: DIAGNOSIS_ONLY
Nested agent count: 0

## Purpose

Validate the X02A findings without modifying product code in this diagnostic workspace. Runtime validation is required before optimization can be declared complete because X02A is gate-blocked and lacks a defined cache component/load baseline.

## Validation Targets

### X02A-SEC-001 Raw identity-bearing cache keys are logged

- Measurement: run a representative cache consumer with Debug/Trace cache logging enabled in a controlled environment and inspect emitted key fields.
- Expected KEEP evidence: raw account, Line ID, contact ID, list ID, or equivalent identifier appears in cache log messages.
- Expected DELETE evidence: logging pipeline already redacts these values before persistence.
- Guardrails: use synthetic identifiers only; do not collect production PII.

### X02A-PERF-001 Cache has expiry but no hard capacity baseline

- Measurement: component/load harness inserts high-cardinality synthetic keys through `ICacheService`, captures item count, tracked key count, process memory, and eviction behavior over time.
- Expected KEEP evidence: memory/tracked keys grow without a documented hard limit until expiry/eviction.
- Expected DELETE evidence: an existing production memory budget or external guard caps X02A cache growth and is tied to testable configuration.
- Guardrails: no package restore, build, test, codegen, formatting, migrations, or generated file writes in this diagnostic run.

### X02A-PERF-002 Async cache misses can stampede per key

- Measurement: component harness invokes `GetOrCreateAsync` concurrently for the same key with a counted delayed factory.
- Expected KEEP evidence: factory count is greater than 1 for concurrent cold misses.
- Expected DELETE evidence: current runtime already provides per-key single-flight behavior not visible in source.
- Guardrails: synthetic factory only; no CRM/network calls.

### X02A-EXT-001 Shared `CacheKeys` mixes reusable primitives with business/group-specific key policy

- Measurement: classify each `CacheKeys` builder by owner module and compare to the module boundary map.
- Expected KEEP evidence: multiple builders are domain-specific and should move behind module-owned catalogs/wrappers in later optimization.
- Expected DELETE evidence: boundary map is revised to assign those key catalogs to X02A as explicit shared contract.

## Gate Requirements Before Optimization

- X02A cache component tests.
- At least one representative B module load/smoke consumer gate.
- Compatibility plan for public `ICacheService` and existing `CacheKeys` consumers.
- Rollback boundary covering DI registration and cache key compatibility.

## Current Diagnostic Verdict

Runtime validation is pending for performance magnitude and final optimization readiness. Source evidence is sufficient to keep the issues as diagnostic candidates for CCG review.

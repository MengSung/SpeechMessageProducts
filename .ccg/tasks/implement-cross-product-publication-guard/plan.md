# Cross-Product Publication Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. This repository is configured for inline Codex execution, so implementation is not delegated.

**Goal:** Prevent duplicate stable IDs, stale network callbacks, partial publication, cross-session state reuse, and retained frontend/backend resources in the ChurchReport weekly-report vertical slice.

**Architecture:** Add a stateless stable-ID publication guard at the exact server consumer boundary while preserving the existing instance-owned atomic snapshot gate. Add one component-owned frontend generation coordinator around the existing DevExtreme transport, with bounded refresh coalescing and deterministic disposal. Register the protected consumer in a Solution-level manifest for future products.

**Tech Stack:** ASP.NET Core, .NET 10, C#, Razor, DevExtreme, jQuery, Dataverse, xUnit, and the repository's available JavaScript runtime.

**Spec:** `.trellis/tasks/09-09-implement-cross-product-publication-guard/design.md`

## Global Constraints

- Database identity only; never deduplicate by name or display content.
- Test first and verify RED before production edits.
- No cross-session mutable state and no unbounded or undisposed resource owner.
- Detailed Traditional Chinese file, member, and non-obvious implementation comments in every changed `.cs` and `.cshtml` file.
- UTF-8 without BOM, CRLF only, and final CRLF for changed `.cs` and `.cshtml` files.

## Task 1: Analyze actual boundaries

**Files:** existing ChurchReport models, SmallGroup controller partials, Razor views, tests, and CCG analysis artifacts.

- [ ] Trace storage/Dataverse → candidate → Session holder → detached read → API/Razor → DevExtreme.
- [ ] Run Gemini and Claude analyzer through `Start-CcgDualModelRun.ps1` and verify every recommendation against the code.
- [ ] Record concrete target files and lifecycle owners before test edits.

## Task 2: Server consumer-boundary guard

**Files:** create a focused guard under `SpeechMessageProducts.ChurchReport`; modify only actual API/Razor publication boundaries; add xUnit tests in `ChurchReport.MemberInfo.Tests`.

**Produces:** a stateless generic method accepting rows, stable-ID selector, consumer name, and maximum row count; successful return preserves all rows and throws before publication for null/missing/duplicate IDs or overflow.

- [ ] Add tests for same-name/different-ID preservation, exact duplicate rejection, missing-ID rejection, capacity rejection, and no retained caller graph.
- [ ] Run tests and capture expected RED caused by the missing guard or missing boundary call.
- [ ] Implement the minimum O(n) local HashSet validation and boundary integration.
- [ ] Add cache-hit and concurrent-write RED tests for `InsertPresentRecord`, `InsertNewPresentRecord`, and `HandleSuccessfulNewPersonCreation` equivalent paths.
- [ ] Route those writes through the existing `SmallGroupDataList` synchronization owner and stable-ID validation; remove the unowned fire-and-forget task that captures the live Session graph.
- [ ] Run targeted and existing snapshot isolation tests to GREEN.

## Task 3: Frontend generation coordinator

**Files:** create a focused JavaScript coordinator and tests; modify `IntegrateView.cshtml` and `_GeneralGroupGrids.cshtml` only where the real loading/mount flow requires it.

**Produces:** per-component `mount`, `requestRefresh`, generation validation, and `dispose` behavior with one active transport and one pending refresh.

- [ ] Confirm the repository's runnable JavaScript test mechanism.
- [ ] Add deterministic tests for late success/error, refresh coalescing, duplicate mount, ineffective abort, and disposal drain.
- [ ] Run tests to expected RED.
- [ ] Implement the coordinator and DevExtreme adapter without a second data pipeline.
- [ ] Run JavaScript tests to GREEN and confirm `PresentRecordId` remains the row key.

## Task 4: Cross-product manifest

**Files:** create `docs/publication-contracts.json`; add a validation test in the existing test project.

- [ ] Add a failing test for required fields, unique consumer identity, authoritative row identity, and referenced files.
- [ ] Run the test to expected RED because the manifest is absent.
- [ ] Add the ChurchReport consumer entry and rerun to GREEN.

## Task 5: Verification and review

**Files:** tests, task review artifact, relevant Trellis spec if new executable knowledge is learned.

- [ ] Run related test projects and `dotnet build SpeechMessageProducts.sln -c Release`.
- [ ] Run concurrency, A/B isolation, retry, mutation isolation, and lifecycle drain tests.
- [ ] Run byte-level UTF-8/BOM/CRLF/final-CRLF validation and `git diff --check`.
- [ ] Run Gemini and Claude reviewer through the self-healing CCG runner; fix verified Critical and Warning findings, then rerun review when required.
- [ ] Document evidence and update task status; do not deploy or push.

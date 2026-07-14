# F07 Runtime Validation Plan

Status: DEGRADED_CCG_REVIEW_APPLIED
Nested agent count: 0

This subagent did not run build, restore, tests, benchmarks, formatting, coverage, code generation, migrations, or package commands because the prompt forbids them.

## Deferred Validation Commands

Run only after the diagnosis-only restriction is lifted:

- `dotnet test LineMessagingProcessor.RichMenus.Tests/LineMessagingProcessor.RichMenus.Tests.csproj --no-restore`
- If restore is explicitly allowed in a later implementation phase, run the normal project restore/build/test sequence selected by the repository owner.

## Targeted Tests to Add or Execute Later

### Expiry / TTL

- A policy returns `RichMenuDecision.Assign(..., ttl: TimeSpan.FromMinutes(5))`.
- `RichMenuOrchestrator.ApplyAsync` should persist `ExpiresAt` through assignment state.
- `RichMenuExpirationSweepWorkflow.SweepAsync(now + ttl)` should restore `PreviousMenuKey` or unassign.
- Verify that a no-TTL decision remains non-expiring.

### Same-menu stale state

- Seed `IRichMenuStateStore` with `CurrentMenuKey = member-main`.
- Leave provider fake unlinked or linked to a different id.
- Call `AssignAsync("U123", "member-main")`.
- Decide whether expected behavior should skip provider link for performance or reconcile provider state for correctness; encode that as a test.

### Provisioning partial failure

- Make `CreateRichMenuAsync` succeed and `UploadRichMenuPngImageAsync` fail.
- Verify the report captures the created provider id or cleanup action.
- Run a second sync with a provider list containing the created versioned name.
- Verify the second sync does not treat the menu as up-to-date without a confirmed image upload.

### Destructive legacy workflow

- Verify `DeleteLinkedRichMenuAsync` is not registered by default if the destructive workflow is removed from normal composition.
- If retained, verify only explicit provider-resource admin paths can call provider delete.

### Cancellation

- Add cancellation-aware fake provider operations.
- Cancel before provider calls and during provider calls.
- Verify the token prevents further provider mutation once the token is cancelled, subject to downstream F04/F05A support.

### Bounded state / sweep

- Seed many non-expiring and expired states.
- Verify a production state store can query only expired records without full table scan.
- Verify sweep is idempotent when assignment restore/unassign fails midway.

## Known Validation Constraint

`LineMessagingProcessor.RichMenus.Tests/Boundary/RichMenuProjectBoundaryTests.cs:61` searches for `ChurchReport.sln`. The repository root currently contains `SpeechMessageProducts.sln`, so this boundary test may need adjustment before full test validation. This was not changed because product/test files are forbidden write paths for this diagnosis-only task.

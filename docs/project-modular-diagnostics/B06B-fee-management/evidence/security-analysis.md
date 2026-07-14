# B06B Security Analysis

## Static Findings

### S1 - Mutating fee APIs need CSRF / anti-forgery proof

- `UpdateFeeData` is `[HttpPut]` at `/FeeManagement/Api/UpdateFeeData` and records edits through `FeeList.PopulateObjectAndUpdateEntity`.
- `SaveBatch` is `[HttpPost]` at `/FeeManagement/Api/SaveBatch` and commits staged changes through `FeeList.CommitPendingChanges`.
- Static search did not find action-level `ValidateAntiForgeryToken` on these endpoints.
- Authorization should not be treated as missing based on controller attributes alone: `Startup.cs:389` registers `GlobalAuthorizationFilter`, and `GlobalAuthorizationFilter.cs:26` checks anonymous allowance or authenticated/session state. Static search found no `AllowAnonymous` marker on `FeeManagementController`.

Risk: fee and present-fee edits affect CRM-backed master data. Route-level authorization has static coverage through the global filter, but CSRF/anti-forgery behavior remains unproven and must be validated before B06B can be approved for extraction.

### S2 - Session cache isolation is guarded but not runtime-proven

- `InMemoryDataContextSmallGroup.FeeList` uses a session-derived cache key ending in `_FeeList`.
- `FeeManagementController.GetFeeData`, `UpdateFeeData`, `SaveBatch`, `EnsureLessonListLoaded`, and `EnsurePresentFeeListLoaded` call `FeeList.EnsureLoginScope`.
- Code comments state that account switches clear fee data and pending `ChangeHistory` so one user cannot commit another user's edits.

Risk: the static pattern is promising, but cache isolation must be validated across login changes, session regeneration, and multiple concurrent browser sessions.

### S3 - Debug traces may expose internal identifiers and exception details

- Fee routes log `discipleLessonsId`, record keys, counts, exception messages, and stack traces through `System.Diagnostics.Debug.WriteLine`.
- These identifiers appear to represent CRM-backed lesson/present fee entities.

Risk: if debug output is collected in shared diagnostics, internal identifiers and exception details may leak. Runtime logging configuration determines severity.

## Non-Issues / Context

- Donation payment transactions, provider callback signature handling, and payment session security belong to B05/F08/F09 and are only consumer context for B06B.
- Static evidence shows B06B has deliberate account re-scope checks before reuse and save. This reduces risk but does not replace runtime validation.

# B04A Security Analysis

Final status: DEGRADED_REVIEW_PENDING

## Critical Security Findings

### S1. Mutation endpoints do not show complete local authorization proof

- Evidence: `SmallGroupController.Crud.cs` exposes `[HttpPost] InsertPresentRecord`, `[HttpPut] UpdateSmallGroupPresentRecord`, and `[HttpDelete] DeletePresentRecord`.
- Evidence: `InsertPresentRecord` writes into `InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData` using only the incoming `values` payload.
- Evidence: `DeletePresentRecord` accepts `key`, deletes from `m_AllMemeberData`, calls `DeleteMemberData` with account/password from `InMemoryContext`, and then deletes from several in-memory datasets.
- Evidence: only `UpdateSmallGroupPresentRecord` visibly calls `EnsureCorrectUserData()`. Equivalent local proof is not visible for insert/delete in the scoped action bodies.
- Impact: unauthorized or stale-session callers may be able to mutate another active list if routing/global state protection fails outside this partial. This is especially risky because attendance data is personal and pastoral-care adjacent.
- Recommendation: require action-level authorization, anti-forgery validation, caller/list/record ownership validation, and a single `PresentRecordMutationContext` before changing in-memory or CRM state.

### S2. Raw diagnostic logging can expose attendance payload data

- Evidence: `UpdateSmallGroupPresentRecord` writes `Session ID`, `Key`, raw `Values`, request method, path, and content type through `System.Diagnostics.Debug.WriteLine`.
- Impact: raw `values` can include attendance status, follow-up notes, contact identifiers, or other sensitive member fields. Debug sinks can be persisted or collected depending on hosting configuration.
- Recommendation: log a correlation ID and safe operation metadata only. Redact payload fields and avoid session ID logging.

### S3. Read path can write a new present record when a personal record is missing

- Evidence: `GetPresentRecordByLoginType` loads present records for a weekly report; if the user is not `小組長`, it filters by `m_ContactId`; if no match is found it calls `CreatePresentRecordList`.
- Impact: a read-style operation can create CRM data. If contact/list/session state is stale or confused, this can create a record for the wrong contact or list.
- Recommendation: separate query and command paths. Only create missing present records through an explicit, authorization-checked command with idempotency.

### S4. Mutable connector instance state carries identity-critical values

- Evidence: present-record creation and filtering rely on fields such as `m_LoginType`, `m_ContactId`, `m_ContactEntity`, `m_ListEntity`, `m_Sunday`, `m_OwnerId`, and `m_ToolUtilityClass`.
- Impact: instance reuse or incorrect hydration can cause caller state to leak across operations. The risk is magnified by global `InMemoryContext` usage in direct callers.
- Recommendation: make B04A operations receive immutable request context values and reject missing or mismatched identity/list IDs before CRM operations.

## Additional Security Observations

- `SearchPresentRecordByName` compares display names to locate records; duplicate names can cause wrong-record selection.
- `UpdateContactInfomationFromList` scans list members and compares `fullname`, which is not a stable authorization key.
- Owner assignment uses CRM owner data, but local code does not prove the caller is allowed to assign that owner for the target record.
- No hardcoded secret was identified in the scoped B04A files; however, delete flow consumes account/password from `InMemoryContext`, so the credential lifecycle belongs to B01/X04A/F03A review.

## Security Verdict

B04A should not proceed directly to optimization. The first implementation increment should establish explicit authorization, session, ownership, CSRF, and idempotency guards around present-record mutations and implicit creation paths.

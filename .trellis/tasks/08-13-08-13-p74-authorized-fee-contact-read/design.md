# P7.4 Authorized Fee Contact Read Design

## Boundary

此 child 僅改變 `DedicationAuditController.GetFeesByContactId` 的 consumer 路徑。它不修改 Package01
connector、operation registry、Gateway deployment、Data8 profile、feature flag 值、CE fixture 或 legacy
ToolUtility 的其他 caller。

## Authorization and data flow

```text
HTTP request + browser contact GUID
  -> EnsureCorrectUserData
  -> server-resolved session login contact
  -> DonationFeeAuditAccessResolver (accounting role only)
  -> parse GUID as a locator, never authority
  -> flag=false: existing legacy manager/service path
  -> flag=true: Package01 typed client -> immutable FeeRecordDto
  -> fresh FeeAuditReadResult -> JSON rows + checked total
```

`DonationFeeAuditAccessResolver` is a pure helper. It accepts only the existing server-resolved login `Entity`, rejects
null/empty identity or an absent/non-string `new_church_jobtitle`, and delegates the existing accounting-role text rule to
`DonationNavigationAccessResolver`. It performs no CRM request, cache operation or session mutation. The browser target
GUID never enters this helper.

## Typed branch

`DonationFeeQueryService` owns an immutable result type containing only new `DonationFeeAuditRow` projections and an
`Int32` total. It calls the existing `RetrieveDedicationFeesByContactAsync` operation with the deployment-owned profile and
server-owned workload subject. The target GUID is only the typed operation's fixed query parameter. It passes no name,
does not retrieve the target contact, and maps result DTOs in request-local variables before returning a fresh result.
The result copies the rows and wraps that copy in a read-only collection; it must not expose an array that a caller could
cast and replace after authorization but before JSON serialization.

The existing form-population method retains its current model mutation contract for its original callers. The new audit
method does not receive a `DonationPaymentFormModel`, so a true-gate audit request cannot overwrite another action's
form data. A typed cancellation/fault propagates out; no legacy retry/fallback is attempted. Overflow rejects before a
result is produced.

`DonationPaymentManager` remains a thin coordinator: it serializes only its existing session-owned manager work, forwards
the cancellation token, and releases its semaphore in `finally`. `DonationDedicationFeeFormService` exposes the typed
audit call only when Package01 is actually enabled; otherwise it rejects rather than silently constructing an alternate
path.

## Rollback

The existing deployment-owned `Package01FeeReadsEnabled=false` branch is the rollback boundary. No runtime setting is
changed by this child. If the true-gate path faults in later controlled validation, keeping the flag false restores the
legacy route without any data cleanup because this child performs only reads and retains no mutable shared result.

## Verification design

- Pure resolver tests prove only a server contact with a valid accounting role can authorize; target IDs are absent from
  its API.
- A service test uses a fake typed client and proves correct operation, cancellation forwarding, fresh result,
  read-only result collection, overflow rejection, and no session model input.
- A source contract test locks the controller's authorization-before-dispatch order, false/true gate split, typed manager
  route, no target `RetrieveEntity`, no raw exception echo, and cancellation outside generic error handling.
- Interleaved distinct fake result tests prove each typed invocation returns independent lists/totals with no static/cache
  state.

## Explicit exclusions

No individual contact-visibility matrix is invented here: the established product rule for this endpoint is accounting
audit scope. If a future requirement needs per-contact accounting scopes, it requires a separate authorization child and
must not be inferred from the browser GUID. Payment-return, fee editor, ToolUtility retirement, P7.5, P8, CE action and
traffic work remain excluded.

# P7.4 MemberInfo 個人出席紀錄 typed read boundary 設計

## 範圍與邊界

本 child 只擁有 `ORG-CALL-00026`：已授權 contact 的個人出席紀錄讀取。它不替代同一
MemberInfo 頁面的其他讀取、會員 metadata、週報、名單或任何出席寫入；這些工作都有不同
matrix row、授權模型與 rollback owner。

新 boundary 採獨立 `MemberInfoPresentRecordReadClient`，而非重用
`IPackage02ContactProfileClient`。後者混合 LINE write 與 aggregate function；新 client 若共用它，
會讓 disabled-by-default present-read gate 意外取得 mutation surface，破壞 capability-specific rollback。

## 資料流與資源所有權

```text
LoadContactPresentRecords
  -> deployment configuration
     -> Package02 base gate + present-read sub-gate
        -> false: existing ToolUtility/CRM SDK compatibility path
        -> true : EnsureCorrectUserData -> parse browser locator -> CanViewContact
                   -> bootstrap validates deployment ProfileAlias
                      -> process-host-owned executor generation
                         -> fixed Data8 QueryExpression
                            -> immutable wire records
                               -> stateless ProductClient
                                  -> request-local service/result
                                     -> action-local ContactPresentRecordRow list
                                        -> DataSourceLoader
```

1. Gate decision only reads deployment configuration. When false, it occurs before user/session work and typed
   composition, so no ProductClient/host/pool/credential/I/O is created. The legacy code remains the sole false
   branch for compatibility.
2. When true, the controller first performs the existing user/session and object authorization before the browser
   locator can select a target. It never obtains ProfileAlias or workload from HTTP, Session, route, query or body.
3. Bootstrap validates the deployment ProfileAlias before returning an injected facade or resolving the process host.
   The existing process host owns reusable executor, provider, handler, connection/lease, credential graph, drain
   and disposal. Bootstrap, controller, service and client neither create a second owner nor dispose the shared client.
4. Data8 has one fixed `QueryExpression("new_present_record")`: fixed projection of record GUID, Sunday/group flags,
   explanation and Sunday date; one fixed `new_contact_new_present_record = contactId` condition; fixed descending
   `new_sunday_date` order; fixed page size. It accepts no arbitrary columns, query, owner, caller sort, endpoint,
   connector or continuation. `MoreRecords` is a failure rather than an unbounded scan.
5. The connector builds immutable wire records only after complete page validation. ProductClient and ChurchReport
   service each copy scalar collections before exposing them. Neither result has CRM objects, a client, a cancellation
   token, cache, stream, lease, background task or retention owner.

## Contract and bounded validation

The request is `(deployment ProfileAlias, fixed workload, authorized contact GUID)`. Empty GUID/profile/workload,
unknown operation or CE version, unexpected parameters or response kind fail before/at dispatch.

The response is a bounded list of pure scalar records:

- `PresentRecordId`: non-empty unique GUID;
- `ContactFullName`: nullable bounded text copied from a fixed contact lookup only when required for legacy-compatible
  row display;
- `SundayDate`: nullable date preserving legacy behaviour—an absent value or year <= 1 becomes null; no unproven
  user-time-zone conversion;
- `Sunday` and `SmallGroup`: closed booleans derived only from allowed CRM integer values;
- `PrayItem`: nullable bounded text.

All query rows are rejected if an expected value cannot be projected under this schema, any text/total response limit
is exceeded, an ID repeats, or CRM says `MoreRecords`. This is an all-or-nothing read: no partial response, fallback
or retry after a malformed page, transport fault, timeout or cancellation.

## Compatibility, cancellation and rollback

The public MVC route and `DataSourceLoader` shape remain unchanged. With the new sub-gate false, the existing
synchronous ToolUtility code executes unchanged. With it true, contact fullname and records come only from the new
typed result; there is no SDK rehydration or legacy query inside the typed branch. `OperationCanceledException` is
excluded from the action catch-all so ASP.NET Core and the executor/lease owner retain the original cancellation and
cleanup path.

Rollback is deployment-owned: maintain/set `Package02MemberInfoPresentReadEnabled=false`, leaving base gate false
as checked in. This local candidate does not prove capacity non-overlap, CE, host parity, traffic routing, soak or
live rollback; it must not be advertised as any of them.

## Test plan

- Registry/wire tests ensure a unique response discriminator, request/response cardinality and strict union validation.
- Data8 tests verify fixed schema, parameters, query projection/filter/order/page size, all row/text/byte bounds and
  failure on `MoreRecords`/malformed values.
- ProductClient/service tests verify fixed operation/profile/workload, defensive copying, A/B interleaving,
  cancellation propagation and no retry/fallback.
- Bootstrap/controller source-contract tests verify base/sub false-gate short-circuit, profile validation before host,
  authorization order, typed-only true branch, preserved legacy false branch, `RequestAborted` and cancellation filter.

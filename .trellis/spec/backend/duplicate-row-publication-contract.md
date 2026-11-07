# Duplicate Row Publication Contract

## 1. Scope / Trigger

Apply this contract whenever code creates, caches, transforms, serializes, renders,
exports, or mutates a repeatable data collection. It covers ASP.NET Core actions,
DevExtreme grids, Razor tables, trees, reports, JSON DTOs, background results,
Dataverse projections, and any future list-like UI.
Client adapters must preserve the actual consumer's asynchronous completion
contract while enforcing the publication generation.

The protected failure is not merely two equal display names. The failure is two
rows representing one authoritative record/business event, a partially published
collection, or a stale/cross-scope snapshot. Legitimate same-name records with
different stable identities remain visible.

## 2. Signatures

A safe publication boundary has the following conceptual inputs and output:

```text
GetOrBuildDetachedRead(
    ValidatedSubject subject,
    ValidatedTenant tenant,
    AuthorizedScope scope,
    DateOrVersion generation,
    CancellationToken requestCancellation)
    -> DetachedReadSnapshot
```

```javascript
coordinator.runLoad(loadFactory);              // Native Promise for date navigation.
coordinator.runLoad(loadFactory, $.Deferred);  // jQuery promise for DevExtreme Grid.
```

Each repeatable row exposes one server-owned stable identity such as
`PresentRecordId`, `ContactId` only when the contact itself is the row, or a
server-generated business-event key. UI components configure that same field as
their row key.

## 3. Contracts

- The server validates subject, tenant/organization, authorization, list/report
  scope, date/version/generation, and authentication epoch before data access.
- A candidate is operation-local. All I/O, mapping, sorting, normalization, and
  row-key validation complete before a single atomic reference publication.
- Readers receive immutable values or deep-enough request-owned copies. They do
  not enumerate a mutable Session/cache list.
- Row-key validation runs on each exact collection passed to a serializer/grid.
  Different UI collections may contain the same record intentionally; duplicate
  validation is scoped to one consumer collection.
- Different stable keys with the same display name are valid and preserved.
  Repeated non-empty stable keys in one consumer collection fail closed.
- Cache entries contain no unpartitioned user data. Cache lifetime and capacity
  are bounded, and eviction never disposes a synchronization primitive that is
  still held or awaited.
- Application single-flight prevents in-process duplicate builds. Database or
  Dataverse alternate/idempotency keys prevent cross-process duplicate writes.
- Cache-hit paths are publication paths too: the exact collection handed to a
  serializer or grid must be detached and revalidated after the cache read. A
  validation performed only while the cache entry was first built is
  insufficient because legacy writers, callbacks, or mutable holders may have
  changed the live object graph afterward.
- Legacy write paths must join the same instance synchronization root and
  duplicate-key guard as the primary writer. A second guard that uses a
  different lock, or an append performed after snapshot publication, can still
  create a repeated stable identity or expose a partial collection.
- Never use fire-and-forget or unobserved long-running work that captures a
  live Session-owned graph, request service, CRM client, or credential. Complete
  the mutation inside the bounded request, or hand off only detached immutable
  input to a separately owned, cancellable, observable worker with deterministic
  drain and disposal.
- DevExtreme's existing jQuery store adapter must return a read-only jQuery
  promise with `done`, `fail`, and `always`. Forward every original completion
  argument and context, including `(data, extra)` where `extra` carries
  `totalCount`, `summary`, and `groupCount`. Do not assimilate that transport
  through a native Promise, which discards extra arguments.
- Both completion paths validate generation. Clear the active transport before
  invoking jQuery callbacks because jQuery 2.x invokes them synchronously and
  consumers may reenter or throw. Preserve the native Promise default for
  existing `.then().catch()` date navigation.
- Version the coordinator script URL when the layout serves immutable static
  assets. A new Razor adapter must not silently run against cached old JS.

## 4. Validation & Error Matrix

| Condition | Required result |
|---|---|
| Same FullName, different stable keys | Return both rows |
| Same non-empty stable key twice in one grid source | Reject candidate; log only non-sensitive diagnostics |
| Empty key for a persisted row | Reject candidate or assign a documented server-owned draft key before publication |
| Candidate loader throws or times out | Do not publish; preserve previous complete snapshot; permit retry |
| User/tenant/list/date/generation changes | Treat as a new scope; never return the previous scope snapshot |
| Caller mutates returned list | Published snapshot remains unchanged |
| Concurrent same-scope requests | At most one build; all successful readers see one complete generation |
| Concurrent different-scope requests | No mutable state, credential, row, or authorization decision crosses scopes |
| jQuery store resolves `(rows, extra)` | Deliver both arguments through the guarded promise; preserve paging metadata |
| Deferred consumer throws or synchronously starts another load | Old active transport is already released; keep the new generation active |
| Old owner completes after disposal | Reject with stale cancellation; never publish the old rows |
| Existing database/Dataverse conflict count > 1 | Stop creating; report conflict for auditable remediation |

## 5. Good / Base / Bad Cases

- Good: two contacts named 王小明 have different `PresentRecordId` values; both rows
  render and can be edited independently.
- Base: 32 AJAX requests request the same authorized scope; one loader builds a
  candidate and every response receives a detached copy of the same generation.
- Bad: two loaders append to a Session-owned `List<Member>` while
  `DataSourceLoader` enumerates it.
- Bad: `.DistinctBy(member => member.FullName)` makes the screenshot look correct
  while silently deleting a legitimate person.
- Bad: an in-process semaphore is treated as the uniqueness guarantee for two IIS
  workers writing the same Dataverse business event.
- Bad: returning a native Promise from a jQuery-based store adapter makes
  DevExtreme's `.load(...).done(...)` throw before the grid can render.

## 6. Tests Required

- Same-name test: literal fixtures with equal display names and different stable
  keys; assert both remain and preserve their own values.
- Exact-key test: duplicate stable key in the actual consumer collection; assert
  publication fails and the previous snapshot is unchanged.
- Single-flight test: deterministic barrier with at least 32 concurrent calls;
  assert one loader invocation and complete equal generations.
- Scope test: change user/contact, organization, list/report, date/version, and
  authentication epoch independently; assert each change invalidates the hit.
- Failure/retry test: inject timeout/exception after partial candidate assembly;
  assert no partial publication and the next call succeeds.
- Mutation-isolation test: mutate one returned list/member; assert Session/cache
  and a second response remain unchanged.
- Lifecycle test: after cancellation, completion, eviction, or disposal, assert
  active builds/waiters/leases/registrations return to the declared baseline and
  retained memory does not trend upward under repeated generations.
- Cross-process writer test where applicable: repeated idempotency key from two
  application instances yields one canonical active business record.
- Execute the actual Razor adapter with the repository's real jQuery
  Callbacks/Deferred implementation. Assert consumer-visible completion,
  metadata, original errors, synchronous reentry, late success/error,
  independent owners, and repeated dispose/drain. CI runs:

```text
node --test ChurchReport.MemberInfo.Tests/JavaScript/collection-load-coordinator.test.js ChurchReport.MemberInfo.Tests/JavaScript/weekly-report-load-adapter.test.js
```

## 7. Wrong vs Correct

### Wrong

```csharp
var rows = sessionReport.Members.DistinctBy(x => x.FullName);
return DataSourceLoader.Load(rows, options);
```

This hides legitimate same-name people, reads mutable Session state directly, and
does not prevent concurrent or cross-process duplicate creation.

### Correct

```csharp
var candidate = BuildOperationLocalCandidate(validatedScope);
ValidateUniqueStableKeys(candidate.Rows);
PublishCompleteSnapshot(candidate, validatedScope);
return DataSourceLoader.Load(candidate.CreateDetachedRows(), options);
```

The real implementation may use a scoped holder, immutable snapshot, or another
equivalent mechanism, but it must preserve the same isolation, atomic publication,
stable identity, retry, cleanup, and performance contracts.

For the existing jQuery Grid, return
`coordinator.runLoad(() => originalLoad(options), $.Deferred)`, rather than
wrapping the native result in `$.when(...)`: a late wrapper restores `.done`
but cannot restore metadata already lost during native Promise assimilation.

## P2-07 待辦入列：已實作邊界（Worker 尚未驗收）

### 1. 範圍

只限 `GoodShepherd.Core/Outbound/OutboundTaskWriter` 與
`ToolUtility.Core/Outbound` 的純值命令及入列適配器，不代表 Worker 或實機驗收完成。

### 2. 簽章

`Enqueue(OutboundTaskType, Guid churchId, Guid? contactId, Guid sourceId,
IReadOnlyDictionary<string, object> payload, CancellationToken)` 回正式資料庫待辦 ID。
`D365OutboundEnqueueStore.CreateOrGet` 借入已授權 SDK；用完 Dispose 清參照，不處置外借連線。

### 3. 契約

- 類型只有 A.4 三種；來源必須是伺服器權威事件 ID，不可 retry 時換新。
- 替代鍵是 `churchId:N-類型數值-sourceId:N`；每組織的資料庫唯一鍵才是跨程序保證。
- payload 至多十六欄；程式欄位名為至多三十二個 ASCII 英文字／數字，首字為字母。
  值僅允許非空 Guid、Int32、Int64、Decimal。欄位依 ordinal 排序，Decimal 以 G29 正規化；
  不接受字串、null、浮點或巢狀資料，不保存 caller 字典。
- 先原子 Create 完整新列；僅 `0x80040237` 唯一鍵衝突轉精確讀回，
  核對教會／會友／類型／payload／替代鍵後回原 ID。這實現不覆寫的冪等 create-or-get；
  不用會覆寫 Completed、attempts 或原 payload 的盲目 Upsert。

### 4. 錯誤矩陣

非法輸入在 SDK 前拒絕；衝突讀回零筆／多筆／不同內容則 fail closed；
非唯一鍵 fault 保留原例外、不重試。Create 已確認成功時不以後到取消隱藏已寫入結果。

### 5. 正常／基準／禁止

同事件重送回同一 ID；不同教會或類型保留獨立列。禁止先查有無再把應用鎖當唯一性，
禁止為通過測試改寫既有狀態，亦不得把來源 GUID 當成資料庫回傳 ID。

### 6. 驗證

`OutboundTaskWriterTests` 雙宿主驗型別／快照／鍵／取消；
`OutboundTaskEnqueueTests` 用獨立 schema 與錯誤碼的 SDK 替身驗兩 store 競爭、
完成狀態不變、內容衝突、原 fault、空 ID 與 Dispose；不得當成 D365 實機結果。

### 7. 錯誤與正確

錯誤：retry 的 Upsert 帶 `new_taskstatus=Pending`，把已完成列重新啟動。
正確：Create 受資料庫替代鍵約束，明確衝突才核對讀回，全程不 Update 既有列。

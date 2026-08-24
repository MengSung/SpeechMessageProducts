# Small Group Background Upload Isolation

## 1. Scope / Trigger

- Trigger: `SmallGroupController.SaveIntegrate` accepts an upload but completes CRM work after the HTTP response.
- The legacy Session-backed `ListSmallGroupWeeklyReport` graph has more than 30 executable `Members` access paths. A background task must therefore never mutate or publish that graph.
- The rule prevents cross-request retention, `List<T>` enumeration races, half-cleared lists, and a stale background snapshot overwriting concurrent foreground CRUD.

## 2. Signatures

```csharp
public SmallGroupDataList CreateIsolatedSnapshot();
public ListSmallGroupWeeklyReport CreateBackgroundUploadCopy();
```

`SaveIntegrate` retains its existing `status` and `message` JSON fields and additionally returns:

```json
{ "requiresRefresh": true }
```

## 3. Contracts

- Before `Task.Run`, create `backgroundCopy = weeklyReport.CreateBackgroundUploadCopy()` while the request still owns the Session cache graph.
- The copy must deep-copy the small-group, new-person-follow-up, and all-member `Members` collections. Each `Member` must be a distinct instance and must not retain a parent weekly-report reference.
- The copy has a new `UploadIntegrateData` instance. Do not copy chart data, form view-models, CRM `Entity` instances, UI selection collections, `HttpContext`, Session, DI scope, or request services.
- The background lambda may capture only that copy, scalar input, and `IServiceScopeFactory`. It must not capture the controller, `InMemoryContext`, the original weekly-report reference, or a shared `Members` reference.
- The background work owns a fresh DI scope and an independent `DataverseTrace` background scope; both are released with `using` on every success, fault, or cancellation path.
- Because `Task.Run` flows `ExecutionContext`, the background work must also wrap that fresh provider in `ToolUtilityFactory.BeginBackgroundScope(...)`. This `AsyncLocal` override takes precedence over inherited `IHttpContextAccessor.HttpContext.RequestServices`, flows into the nested upload task, and restores the prior provider before the DI scope is disposed. Without it, the legacy singleton can resolve a completed request scope and create a disposed-scope race.
- `CancellationToken.None` intentionally preserves the accepted-upload contract after client disconnect. Its bounded owner is the fire-and-forget task; the task must not store its inputs after completion.
- The background task may clean its copy but must never clear, replace, or publish shared Session/IMemoryCache members. `requiresRefresh=true` tells the client to reload authoritative data.

## 4. Validation & Error Matrix

| Condition | Required behavior |
| --- | --- |
| Any target source collection is null | Create an empty, copy-owned list; do not expose the source reference. |
| Background upload or cleanup fails | Record only the exception type via release-capable tracing; do not log account, password, member content, stack data, or exception text; never write to the shared graph. |
| Background CRM resolution sees an inherited request `HttpContext` | Resolve from the explicit background provider override; never fall through to inherited request services while the background scope is active. |
| Client disconnects after accepted response | Continue with `CancellationToken.None`, then dispose the background DI and trace scopes. |
| Concurrent foreground CRUD occurs while upload runs | Preserve foreground graph unchanged; do not publish the background snapshot because it is stale. |
| New code proposes `Clear()+AddRange()` or an atomic members replacement after upload | Reject it unless a complete, repository-wide versioned publication and synchronization contract exists. |

## 5. Good / Base / Bad Cases

- Good: request creates a deep copy, background upload removes transferred members from the copy, and the response includes `requiresRefresh=true`.
- Base: a null source group yields an empty private list and the upload completes without touching Session state.
- Bad: `Task.Run` calls `weeklyReportRef.UploadIntegrateDataAsync(...)`, removes from its collections, or assigns the copy's collections back after a long upload.

## 6. Tests Required

- Assert the three target collections and their `Member` elements are different references while all copied public member values are equivalent.
- Mutate all three snapshot collections in a background task for at least 1,000 iterations while repeatedly enumerating all three source collections. Assert no `InvalidOperationException`, source count/order unchanged, and no half-cleared state.
- Create two independent source reports, mutate one snapshot, and assert neither the other snapshot nor either source changes.
- Test the JSON response contract when controller-level coverage can be added without external CRM I/O: existing `status` and `message` remain, and `requiresRefresh` is true.
- Assert the legacy ambient gateway resolves a background-scoped `IOrganizationService` inside a nested `Task.Run`, does not use the request-scoped service, and restores request resolution after the override is disposed.

## 7. Wrong vs Correct

### Wrong

```csharp
await weeklyReportRef.UploadIntegrateDataAsync(..., allMemberData, ...);
weeklyReportRef.m_SmallGroupDataList.m_SmallGroupData.Members.Clear();
weeklyReportRef.m_SmallGroupDataList.m_SmallGroupData.Members.AddRange(cleaned);
```

This keeps Session state alive after the request and makes foreground readers race a mutable list. Replacing `Members` after a long job still loses concurrent CRUD updates.

### Correct

```csharp
var backgroundCopy = weeklyReportRef.CreateBackgroundUploadCopy();

_ = Task.Run(async () =>
{
    using var traceScope = DataverseTrace.Current?.BeginBackgroundOperation("SaveIntegrate.Upload");
    using var scope = scopeFactory.CreateScope();
    using var ambientScope = ToolUtilityFactory.BeginBackgroundScope(scope.ServiceProvider);
    await backgroundCopy.UploadIntegrateDataAsync(..., backgroundCopy.m_SmallGroupDataList.m_AllMemeberData, ...);
    RemoveTransferredMembers(backgroundCopy.m_SmallGroupDataList.m_SmallGroupData.Members);
});

return Json(new { status = "1", message = "資料已送出，正在背景上傳中...", requiresRefresh = true });
```

The background task owns every mutable object it changes. The shared graph remains foreground-owned and naturally expires with its established Session/cache lifecycle.

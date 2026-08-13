# MemberInfo Tree And Grid Contract

## 1. Scope / Trigger

Use this contract when changing the MemberInfo district/group/member tree, authorized search, ungrouped paging, member detail projection, membership-status ordering, or batch avatars.

The feature crosses CRM, controller authorization, JSON DTOs, DevExtreme stores, Razor DOM rendering, and shared cache. Any change to one layer must preserve the complete flow.

## 2. Signatures

Server endpoints:

```text
GET  /MemberInfo/LoadDistrictTree
GET  /MemberInfo/LoadGroupMembers?listId=<guid>&search=<text>
GET  /MemberInfo/LoadUngroupedMembers?skip=<n>&take=<n>&sort=<dx>&search=<text>
GET  /MemberInfo/SearchDistrictTree?search=<text>
GET  /MemberInfo/Detail?contactId=<guid>
POST /MemberInfo/GetContactImagesBatch
```

Batch-avatar request:

```json
{ "contactIds": ["guid"], "size": 48 }
```

Tree DTO root fields are `Districts`, `Ungrouped`, and `Scope`. Member row fields are `ContactId`, `FullName`, `Phone`, `BirthDate`, `Address`, `SpiritualIdentity`, `MembershipStatus`, `RelationGoals`, and `Gender`, plus non-visible sort metadata.

## 3. Contracts

- JSON tree/member DTO properties stay PascalCase because the application uses Newtonsoft `DefaultContractResolver`.
- `LoadDistrictTree` returns no member rows. Group members and avatars load only after expansion, except the approved one-group auto-open behavior.
- Church receives all valid active/app-named `purpose = "小組名單"` descriptors and a Church-only ungrouped node.
- Shepherd receives the intersection of valid descriptors and the current user's `ListManager` assignments.
- Requested list IDs must be valid GUIDs and present in the visible descriptor set.
- Contact rows are current-contact filtered and batch-authorized before DTO construction.
- Shared cache may contain Church skeleton/grouped-ID snapshots, schema metadata, or image bytes. It must not contain Shepherd rows, search results, or authorization decisions.
- `customertypecode` order comes only from `PicklistAttributeMetadata.OptionSet.Options` sequence. Raw integer and label ordering are forbidden.
- Ungrouped ordering stays Configured -> Unknown -> Empty in both directions; descending reverses configured ranks only.
- Dataverse `IN` inputs are chunked with `CrmInClauseChunkSize` (500), including batch avatar image retrieval.
- Visible grids share one nine-column factory. Avatar is fixed-left 72 px and non-resizable/non-sortable; name is fixed-left 62 px with no application `minWidth`.

### Disabled typed aggregate consumer boundary

When an ungrouped-member aggregate is migrated to a typed ProductClient, the
consumer must remain a separate deployment-owned sub-gate with a checked-in
`false` default. The bootstrap factory for that capability must be tested
directly, not through a neighbouring Package02 factory: tests cover
`gate=false`, base-gate-only, and `base=true + sub=true + non-empty
ProfileAlias` before host/provider/pool resolution. The enabled branch uses a
fixed workload and server-owned profile, forwards the request cancellation
token, and never falls back to the legacy aggregate after typed dispatch.

If the same request also uses a cached grouped-contact exclusion snapshot, a
new typed aggregate snapshot must not be combined with a stale cache entry
unless consistency is proven. The safe local pattern is a request-only cache
bypass; it must not add a user/session cache, retain a DTO, or create a new
resource owner. Malformed, duplicate, negative, cancelled, or faulted typed
results fail closed before publishing a partial count map.

### Disabled Package03 commitment-metadata consumer boundary

#### 1. Scope / Trigger

Apply this scenario when a MemberInfo action migrates the read of
`contact.customertypecode` metadata from the legacy CRM metadata provider to
the Package03 typed option-set operation. It applies to every route that uses
the option label, configured order, or the unique `結案` value: authorized
search, group members, and ungrouped paging. This is a consumer overlay only;
it does not authorize CE activity, traffic cutover, ToolUtility removal, P7.5,
or P8.

#### 2. Signatures

```csharp
private async Task<IReadOnlyList<MemberInfoCommitmentTypeOption>?>
    LoadCommitmentTypeOptionsAsync(
        IConfiguration configuration,
        bool useTypedCommitmentMetadata,
        CancellationToken cancellationToken);

private int GetRequiredClosedCustomerTypeValue(
    IOrganizationService service,
    IReadOnlyList<MemberInfoCommitmentTypeOption>? typedCommitmentOptions = null);
```

#### 3. Contracts

- `DynamicsAccess:Package03SpecialResourcesEnabled` is the Package03 base
  gate and `DynamicsAccess:Package03MemberInfoCommitmentMetadataReadEnabled`
  is the independent consumer rollback gate. Both checked-in values remain
  `false`; neither missing value nor a truthy value supplied by a request is
  valid enablement.
- When both deployment-owned gates are enabled, an action obtains exactly one
  immutable, bounded, request-local option snapshot through its fixed
  Package03 profile, workload, target, and `RequestAborted` token. It passes
  that same snapshot to search mapping, segment ordering, row projection, and
  the closed-status resolver.
- In the typed branch, `結案` is resolved by one exact label match in that
  snapshot. The branch must never read `GetSharedOptionSetService`, the legacy
  metadata provider, or its process-global cache. The legacy service is only
  permitted when the snapshot is `null`, which represents the false-gate
  compatibility branch.
- The snapshot, CRM service, client, profile, cancellation token, authorization
  decision, entity, and exception remain request-local. Existing action
  `finally` blocks remain the only owner that releases the borrowed CRM
  connection; Package03 reusable resources remain owned by the process host.

#### 4. Validation & Error Matrix

| Condition | Required result |
| --- | --- |
| Base or metadata sub-gate is false/missing | Do not bind profile, resolve host, create client, or issue typed I/O; use the legacy compatibility path. |
| Enabled gate with blank deployment ProfileAlias | Reject before host/provider/pool composition. |
| Typed snapshot has no `結案` label or more than one exact `結案` label | Fail closed; do not query legacy metadata or return a partial result. |
| Typed metadata fault, timeout, cancellation, malformed DTO, or client unavailability | Propagate the failure; do not retry or fall back to legacy metadata. |
| Typed snapshot contains an unknown raw choice value on a member row | Render an empty membership-status label; do not invoke the legacy resolver. |

#### 5. Good / Base / Bad Cases

- **Good:** an enabled request receives one valid typed snapshot; the same
  snapshot supplies label matching, configured rank, row text, and exactly one
  `結案` value before the response is constructed.
- **Base:** the metadata gate is false; no typed graph is composed and the
  established legacy metadata behavior remains unchanged.
- **Bad:** an enabled action resolves `結案` by calling the legacy shared
  OptionSet service after receiving a typed snapshot. This mixes metadata from
  potentially different profile/generation boundaries and is release-blocking.

#### 6. Tests Required

- Direct bootstrap tests cover gate false, base-only, both gates with a valid
  profile, and empty ProfileAlias rejection before host resolution.
- Service tests cover fixed request fields, defensive copies, bounded malformed
  DTO rejection, A/B profile isolation, cancellation, and no retry/fallback.
- Controller contracts assert that all three consumers pass the same typed
  snapshot to the closed-status resolver and that the resolver's typed branch
  contains no legacy OptionSet lookup. Include missing/duplicate `結案` as a
  fail-closed regression condition.
- Run focused tests, full `ChurchReport.MemberInfo.Tests`, solution Release
  tests/build, UTF-8 no-BOM/CRLF/final-CRLF checks, and `git diff --check`.

#### 7. Wrong vs Correct

```csharp
// Wrong: typed metadata is loaded, but the closed value can cross back into
// a legacy process-global metadata cache with another profile/generation.
var closedStatus = GetSharedOptionSetService(service)
    .GetOptionSetValue("contact", "customertypecode", "結案", null);
```

```csharp
// Correct: an enabled request has one immutable metadata authority. Single
// intentionally throws for a missing or duplicate label, so the route fails
// closed instead of fabricating a fallback value.
var closedStatus = typedCommitmentOptions
    .Single(option => option.Label.Equals("結案", StringComparison.Ordinal))
    .Value;
```

## 4. Validation & Error Matrix

| Condition | Required result |
|---|---|
| Unknown access | 403 / no data |
| Blank or malformed list/contact ID | Deny without CRM data response |
| List not in authoritative visible set | 403 |
| Closed-status metadata unavailable on tree routes | Fail closed |
| CRM authorization query fails | Return no authorized contacts |
| Membership metadata unavailable | Keep data, classify non-empty values as Unknown, never use raw ordering |
| Relation entity unavailable | Keep authorized member rows with blank relation summary |
| Avatar batch contains more than 500 uncached IDs | Execute multiple bounded CRM queries |

## 5. Good / Base / Bad Cases

- Good: a Shepherd requests an assigned list; current members are batch-authorized, mapped once, and returned in metadata rank order.
- Base: membership metadata is temporarily unavailable; rows remain available, all non-empty values sort in Unknown, and Empty remains last.
- Bad: a caller submits a syntactically valid list GUID that is not in the visible descriptor set; the controller must not query or return its members.
- Bad: avatar authorization is chunked but the image query puts all IDs into one `IN` condition; this can fail after many groups are expanded.

## 6. Tests Required

- Pure tests for tree counts/deduplication, search authorization shaping, metadata rank ordering, segment slicing, and FetchXML aggregate conversion.
- Controller contracts for required routes, authoritative list validation, current-contact filtering, batch authorization, query chunking, and one-field relation mapping.
- View contracts for exact column order/width/fixed state, local/remote sorting selector, single sorting, widget resizing, touch scope, loading/search lifecycle, and XSS-safe text binding.
- Full `ChurchReport.MemberInfo.Tests` run with inherited payment failures classified separately.
- Application/test builds, Razor JavaScript parse, strict UTF-8/CRLF, secret scan, and `git diff --check`.
- Authenticated Church/Shepherd plus 320/390/430/640 px runtime checks in a non-production environment.

## 7. Wrong vs Correct

### Wrong

```csharp
query.Criteria.AddCondition(
    "contactid",
    ConditionOperator.In,
    uncachedGuids.Select(id => (object)id).ToArray());
```

This creates one unbounded Dataverse condition and can fail when many groups are expanded.

### Correct

```csharp
foreach (var chunk in uncachedGuids.Chunk(CrmInClauseChunkSize))
{
    var query = new QueryExpression("contact");
    query.Criteria.AddCondition(
        "contactid",
        ConditionOperator.In,
        chunk.Select(id => (object)id).ToArray());
    service.RetrieveMultiple(query);
}
```

The same bounded-query rule applies to memberships, contact authorization, relations, and other multi-ID CRM work.

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

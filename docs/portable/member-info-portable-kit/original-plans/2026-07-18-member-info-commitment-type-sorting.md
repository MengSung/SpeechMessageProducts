# Member Info Configured Commitment-Type Sorting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make general-group, search-result, and server-paged ungrouped member grids sort by the Dynamics `contact.customertypecode` configured metadata order while continuing to display localized membership labels.

**Architecture:** A focused metadata provider reads and caches `PicklistAttributeMetadata.OptionSet.Options` in its configured sequence and exposes a zero-based rank. Local grids sort mapped DTO ranks; the ungrouped path counts raw values, turns them into configured/unknown/empty segments, and retrieves only the slices needed for the requested page. Raw integer ordering, label ordering, hard-coded church order, and FetchXML `useraworderby` are explicitly removed.

**Tech Stack:** ASP.NET Core MVC, C# / net10.0, Microsoft Dataverse SDK, FetchXML aggregate queries, DevExtreme DataGrid, xUnit, FluentAssertions.

---

## Scope and file map

**Create**

- `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs` — retrieve, localize, rank, and cache configured OptionSet metadata.
- `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeCountQuery.cs` — convert an SDK-generated base FetchXML query into grouped value counts and read aggregate rows.
- `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeMetadataProviderTests.cs` — executable metadata-order and cache tests.
- `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeCountQueryTests.cs` — executable aggregate transformation/count parsing tests.

**Replace the current uncommitted raw-value implementation**

- `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeSort.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeSortTests.cs`

**Modify**

- `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`
- `ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs`
- `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- `.ccg/tasks/sort-member-info-by-commitment-type/task.json`
- `.ccg/tasks/sort-member-info-by-commitment-type/review.md`

Do not modify CRM metadata/data, visible column order/widths, fixed avatar/name behavior, touch scrolling, search lifecycle, authorization predicates, or any deployment/merge state.

**Commit policy:** The user explicitly prohibited Commit before VS 2026 verification. Every task ends with a scoped diff checkpoint instead of a commit.

---

### Task 1: Read and cache the configured OptionSet order

**Files:**

- Create: `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs`
- Create: `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeMetadataProviderTests.cs`

- [ ] **Step 1: Write the failing provider tests**

Create a fake `IOrganizationService` whose `Execute` method returns a
`RetrieveAttributeResponse` containing this deliberately non-numeric order:

```csharp
private static PicklistAttributeMetadata Metadata(params (int Value, string Label)[] options)
{
    var collection = new OptionMetadataCollection();
    foreach (var option in options)
    {
        collection.Add(new OptionMetadata(
            new Label(option.Label, 1028),
            option.Value));
    }

    return new PicklistAttributeMetadata
    {
        LogicalName = "customertypecode",
        OptionSet = new OptionSetMetadata(collection)
    };
}

[Fact]
public void GetOptions_PreservesConfiguredOrderInsteadOfNumericValueOrder()
{
    var service = new RecordingOrganizationService(Metadata(
        (100000006, "牧師師母"),
        (1, "小組組員"),
        (100000000, "新朋友")));
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var provider = new MemberInfoCommitmentTypeMetadataProvider(service, cache);

    var options = provider.GetOptions();

    options.Select(option => option.Value)
        .Should().Equal(100000006, 1, 100000000);
    options.Select(option => option.Order)
        .Should().Equal(0, 1, 2);
    options[0].Label.Should().Be("牧師師母");
}

[Fact]
public void GetOptions_UsesSharedCacheAfterFirstMetadataRequest()
{
    var service = new RecordingOrganizationService(Metadata(
        (100000006, "牧師師母"),
        (1, "小組組員")));
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var provider = new MemberInfoCommitmentTypeMetadataProvider(service, cache);

    provider.GetOptions();
    provider.GetOptions();

    service.ExecuteCalls.Should().Be(1);
}
```

Use this complete fake:

```csharp
private sealed class RecordingOrganizationService : IOrganizationService
{
    private readonly AttributeMetadata metadata;
    private readonly bool throwOnExecute;

    public RecordingOrganizationService(
        AttributeMetadata metadata,
        bool throwOnExecute = false)
    {
        this.metadata = metadata;
        this.throwOnExecute = throwOnExecute;
    }

    public int ExecuteCalls { get; private set; }

    public OrganizationResponse Execute(OrganizationRequest request)
    {
        ExecuteCalls++;
        request.Should().BeOfType<RetrieveAttributeRequest>();
        if (throwOnExecute)
            throw new InvalidOperationException("metadata unavailable");

        var response = new RetrieveAttributeResponse();
        response.Results["AttributeMetadata"] = metadata;
        return response;
    }

    public Guid Create(Entity entity) => throw new NotSupportedException();
    public void Update(Entity entity) => throw new NotSupportedException();
    public void Delete(string entityName, Guid id) => throw new NotSupportedException();
    public Entity Retrieve(
        string entityName,
        Guid id,
        ColumnSet columnSet) => throw new NotSupportedException();
    public EntityCollection RetrieveMultiple(
        QueryBase query) => throw new NotSupportedException();
    public void Associate(
        string entityName,
        Guid entityId,
        Relationship relationship,
        EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    public void Disassociate(
        string entityName,
        Guid entityId,
        Relationship relationship,
        EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
}

[Fact]
public void GetOptions_SkipsOptionWithoutValue()
{
    var metadata = Metadata((100000006, "牧師師母"));
    metadata.OptionSet.Options.Add(new OptionMetadata(
        new Label("沒有值", 1028),
        null));
    using var cache = new MemoryCache(new MemoryCacheOptions());

    new MemberInfoCommitmentTypeMetadataProvider(
            new RecordingOrganizationService(metadata),
            cache)
        .GetOptions()
        .Should().ContainSingle();
}

[Fact]
public void GetOptions_MetadataFailureReturnsEmptyWithoutThrowing()
{
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var provider = new MemberInfoCommitmentTypeMetadataProvider(
        new RecordingOrganizationService(
            Metadata((100000006, "牧師師母")),
            throwOnExecute: true),
        cache);

    var act = () => provider.GetOptions();

    act.Should().NotThrow();
    act().Should().BeEmpty();
}
```

- [ ] **Step 2: Run the provider tests and verify RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~MemberInfoCommitmentTypeMetadataProviderTests --no-restore
```

Expected: compilation fails because `MemberInfoCommitmentTypeMetadataProvider` and
`MemberInfoCommitmentTypeOption` do not exist.

- [ ] **Step 3: Implement the metadata provider**

Create these public contracts:

```csharp
public sealed record MemberInfoCommitmentTypeOption(
    int Value,
    string Label,
    int Order);

public sealed class MemberInfoCommitmentTypeMetadataProvider
{
    private const string CacheKey =
        "member-info:metadata:contact:customertypecode:configured-order";
    private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(1);

    private readonly IOrganizationService organizationService;
    private readonly IMemoryCache cache;

    public MemberInfoCommitmentTypeMetadataProvider(
        IOrganizationService organizationService,
        IMemoryCache cache)
    {
        this.organizationService = organizationService
            ?? throw new ArgumentNullException(nameof(organizationService));
        this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public IReadOnlyList<MemberInfoCommitmentTypeOption> GetOptions()
    {
        if (cache.TryGetValue(
                CacheKey,
                out IReadOnlyList<MemberInfoCommitmentTypeOption>? cached) &&
            cached != null)
        {
            return cached;
        }

        try
        {
            var response = (RetrieveAttributeResponse)organizationService.Execute(
                new RetrieveAttributeRequest
                {
                    EntityLogicalName = "contact",
                    LogicalName = "customertypecode",
                    RetrieveAsIfPublished = true
                });
            var metadata = response.AttributeMetadata as PicklistAttributeMetadata;
            var result = (metadata?.OptionSet?.Options ?? new OptionMetadataCollection())
                .Where(option => option.Value.HasValue)
                .Select((option, order) => new MemberInfoCommitmentTypeOption(
                    option.Value!.Value,
                    ResolveLabel(option),
                    order))
                .ToArray();
            cache.Set(CacheKey, result, SuccessCacheDuration);
            return result;
        }
        catch
        {
            IReadOnlyList<MemberInfoCommitmentTypeOption> empty =
                Array.Empty<MemberInfoCommitmentTypeOption>();
            cache.Set(CacheKey, empty, FailureCacheDuration);
            return empty;
        }
    }

    private static string ResolveLabel(OptionMetadata option)
    {
        return option.Label?.LocalizedLabels?
                   .FirstOrDefault(label => label.LanguageCode == 1028)?.Label
               ?? option.Label?.LocalizedLabels?
                   .FirstOrDefault(label => label.LanguageCode == 2052)?.Label
               ?? option.Label?.UserLocalizedLabel?.Label
               ?? $"Unknown_{option.Value}";
    }
}
```

Use `Microsoft.Extensions.Caching.Memory`, `Microsoft.Xrm.Sdk`,
`Microsoft.Xrm.Sdk.Messages`, `Microsoft.Xrm.Sdk.Metadata`, and LINQ imports.
Preserve the metadata collection sequence exactly; do not call `OrderBy`.

- [ ] **Step 4: Verify GREEN and provider scope**

Run the focused provider test command again.

Expected: all provider tests pass and `ExecuteCalls` is 1 for the cache test.

Run:

```powershell
git diff --check
git diff -- ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeMetadataProviderTests.cs
```

Do not Commit.

---

### Task 2: Replace raw-value sorting with rank/category sorting

**Files:**

- Modify: `ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs`
- Replace: `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeSort.cs`
- Replace: `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeSortTests.cs`

- [ ] **Step 1: Rewrite the failing sorting tests**

Replace tests that mention `MembershipStatusValue`, raw numeric ordering,
`EnableRawChoiceOrdering`, or typed/null-only slices with:

```csharp
[Fact]
public void OrderRows_Ascending_UsesConfiguredRankThenUnknownThenEmpty()
{
    var rows = new[]
    {
        Row("empty", "Empty", null, false),
        Row("member", "Member", 1, true),
        Row("pastor", "Pastor", 0, true),
        Row("unknown", "Unknown", null, true)
    };

    MemberInfoCommitmentTypeSort.OrderRows(rows)
        .Select(row => row.ContactId)
        .Should().Equal("pastor", "member", "unknown", "empty");
}

[Fact]
public void OrderRows_Descending_ReversesConfiguredRanksOnly()
{
    var rows = new[]
    {
        Row("empty", "Empty", null, false),
        Row("pastor", "Pastor", 0, true),
        Row("member", "Member", 1, true),
        Row("unknown", "Unknown", null, true)
    };

    MemberInfoCommitmentTypeSort.OrderRows(rows, descending: true)
        .Select(row => row.ContactId)
        .Should().Equal("member", "pastor", "unknown", "empty");
}

[Fact]
public void BuildSegments_UsesConfiguredSequenceAndKeepsUnknownAndEmptyLast()
{
    var counts = new Dictionary<int, int>
    {
        [100000006] = 2,
        [1] = 3,
        [777] = 4
    };

    var result = MemberInfoCommitmentTypeSort.BuildSegments(
        new[] { 100000006, 100000002, 1 },
        counts,
        nullCount: 1,
        descending: false);

    result.Should().Equal(
        new MemberInfoCommitmentTypeSegment(
            MemberInfoCommitmentTypeSegmentKind.Configured, 100000006, 2),
        new MemberInfoCommitmentTypeSegment(
            MemberInfoCommitmentTypeSegmentKind.Configured, 1, 3),
        new MemberInfoCommitmentTypeSegment(
            MemberInfoCommitmentTypeSegmentKind.Unknown, null, 4),
        new MemberInfoCommitmentTypeSegment(
            MemberInfoCommitmentTypeSegmentKind.Empty, null, 1));
}

[Fact]
public void PlanSlices_CrossesConfiguredUnknownAndEmptySegments()
{
    var segments = new[]
    {
        new MemberInfoCommitmentTypeSegment(
            MemberInfoCommitmentTypeSegmentKind.Configured, 100000006, 3),
        new MemberInfoCommitmentTypeSegment(
            MemberInfoCommitmentTypeSegmentKind.Unknown, null, 2),
        new MemberInfoCommitmentTypeSegment(
            MemberInfoCommitmentTypeSegmentKind.Empty, null, 4)
    };

    MemberInfoCommitmentTypeSort.PlanSlices(2, 5, segments).Should().Equal(
        new MemberInfoCommitmentTypeSlice(
            MemberInfoCommitmentTypeSegmentKind.Configured, 100000006, 2, 1),
        new MemberInfoCommitmentTypeSlice(
            MemberInfoCommitmentTypeSegmentKind.Unknown, null, 0, 2),
        new MemberInfoCommitmentTypeSlice(
            MemberInfoCommitmentTypeSegmentKind.Empty, null, 0, 2));
}
```

Use this helper:

```csharp
private static GroupMemberRowViewModel Row(
    string id,
    string name,
    int? order,
    bool hasValue) => new()
{
    ContactId = id,
    FullName = name,
    MembershipStatusOrder = order,
    HasMembershipStatusValue = hasValue
};
```

Add tests for same-rank name/ContactId stability, descending segments, duplicate configured
values, negative counts, zero take, and skip beyond total.

- [ ] **Step 2: Run sorting tests and verify RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~MemberInfoCommitmentTypeSortTests --no-restore
```

Expected: compilation fails because the DTO rank/has-value properties and new segment contracts
do not exist.

- [ ] **Step 3: Replace the DTO sort fields**

Remove the uncommitted raw-value property:

```csharp
public int? MembershipStatusValue { get; set; }
```

Add the approved fields with UTF-8 comments:

```csharp
/// <summary>
/// contact.customertypecode 在 Dynamics 客製化選項集合中的零起始順位；
/// 這不是 OptionSet 原始整數值，null 表示 metadata 未列出或欄位未填。
/// </summary>
public int? MembershipStatusOrder { get; set; }

/// <summary>
/// 表示 CRM 欄位實際有 OptionSet 值，用來區分 metadata 未知舊值與真正空白。
/// </summary>
public bool HasMembershipStatusValue { get; set; }
```

Keep `MembershipStatus` as the localized display string.

- [ ] **Step 4: Replace the shared sorting service**

Define:

```csharp
public enum MemberInfoCommitmentTypeSegmentKind
{
    Configured,
    Unknown,
    Empty
}

public readonly record struct MemberInfoCommitmentTypeSegment(
    MemberInfoCommitmentTypeSegmentKind Kind,
    int? Value,
    int Count);

public readonly record struct MemberInfoCommitmentTypeSlice(
    MemberInfoCommitmentTypeSegmentKind Kind,
    int? Value,
    int Skip,
    int Take);

public static class MemberInfoCommitmentTypeSort
{
    public const string Selector = "MembershipStatusOrder";

    public static List<GroupMemberRowViewModel> OrderRows(
        IEnumerable<GroupMemberRowViewModel>? rows,
        bool descending = false)
    {
        var source = (rows ?? Enumerable.Empty<GroupMemberRowViewModel>())
            .Where(row => row != null)
            .ToList();
        var configured = source.Where(row => row.MembershipStatusOrder.HasValue);
        var orderedConfigured = descending
            ? configured.OrderByDescending(row => row.MembershipStatusOrder!.Value)
            : configured.OrderBy(row => row.MembershipStatusOrder!.Value);

        var stableConfigured = orderedConfigured
            .ThenBy(row => row.FullName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(row => row.ContactId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var unknown = source
            .Where(row => !row.MembershipStatusOrder.HasValue &&
                          row.HasMembershipStatusValue)
            .OrderBy(row => row.FullName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(row => row.ContactId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var empty = source
            .Where(row => !row.MembershipStatusOrder.HasValue &&
                          !row.HasMembershipStatusValue)
            .OrderBy(row => row.FullName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(row => row.ContactId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        return stableConfigured.Concat(unknown).Concat(empty).ToList();
    }
}
```

Add these methods inside `MemberInfoCommitmentTypeSort`:

```csharp
public static IReadOnlyList<MemberInfoCommitmentTypeSegment> BuildSegments(
    IEnumerable<int>? configuredValues,
    IReadOnlyDictionary<int, int>? countsByValue,
    int nullCount,
    bool descending = false)
{
    var configured = (configuredValues ?? Enumerable.Empty<int>())
        .Distinct()
        .ToList();
    var configuredSet = configured.ToHashSet();
    if (descending)
        configured.Reverse();

    var counts = countsByValue ?? new Dictionary<int, int>();
    var segments = new List<MemberInfoCommitmentTypeSegment>();
    foreach (var value in configured)
    {
        var count = counts.TryGetValue(value, out var found)
            ? Math.Max(0, found)
            : 0;
        if (count > 0)
        {
            segments.Add(new MemberInfoCommitmentTypeSegment(
                MemberInfoCommitmentTypeSegmentKind.Configured,
                value,
                count));
        }
    }

    var unknownCount = counts
        .Where(pair => !configuredSet.Contains(pair.Key))
        .Sum(pair => Math.Max(0, pair.Value));
    if (unknownCount > 0)
    {
        segments.Add(new MemberInfoCommitmentTypeSegment(
            MemberInfoCommitmentTypeSegmentKind.Unknown,
            null,
            unknownCount));
    }

    var normalizedNullCount = Math.Max(0, nullCount);
    if (normalizedNullCount > 0)
    {
        segments.Add(new MemberInfoCommitmentTypeSegment(
            MemberInfoCommitmentTypeSegmentKind.Empty,
            null,
            normalizedNullCount));
    }
    return segments;
}

public static IReadOnlyList<MemberInfoCommitmentTypeSlice> PlanSlices(
    int skip,
    int take,
    IEnumerable<MemberInfoCommitmentTypeSegment>? segments)
{
    var remainingSkip = Math.Max(0, skip);
    var remainingTake = Math.Max(0, take);
    if (remainingTake == 0)
        return Array.Empty<MemberInfoCommitmentTypeSlice>();

    var slices = new List<MemberInfoCommitmentTypeSlice>();
    foreach (var segment in segments ??
             Enumerable.Empty<MemberInfoCommitmentTypeSegment>())
    {
        var count = Math.Max(0, segment.Count);
        if (remainingSkip >= count)
        {
            remainingSkip -= count;
            continue;
        }

        var localSkip = remainingSkip;
        var available = count - localSkip;
        var localTake = Math.Min(remainingTake, available);
        if (localTake > 0)
        {
            slices.Add(new MemberInfoCommitmentTypeSlice(
                segment.Kind,
                segment.Value,
                localSkip,
                localTake));
            remainingTake -= localTake;
        }
        remainingSkip = 0;
        if (remainingTake == 0)
            break;
    }
    return slices;
}
```

Remove `EnableRawChoiceOrdering` and all XML-related imports from this sorting file.

- [ ] **Step 5: Verify GREEN and inspect replacement**

Run the focused sorting tests. Expected: all pass.

Run:

```powershell
rg -n "MembershipStatusValue|EnableRawChoiceOrdering|useraworderby" ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeSort.cs ChurchReport/ViewModels/MemberInfoTree/DistrictTreeViewModels.cs ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeSortTests.cs
git diff --check
```

Expected: `rg` returns no matches and `git diff --check` reports no errors. Do not Commit.

---

### Task 3: Build one aggregate query for non-empty value counts

**Files:**

- Create: `ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeCountQuery.cs`
- Create: `ChurchReport.MemberInfo.Tests/MemberInfoCommitmentTypeCountQueryTests.cs`

- [ ] **Step 1: Write failing aggregate-query tests**

Use an SDK-like base query containing attributes, orders, and filters:

```csharp
[Fact]
public void CreateValueCountsFetch_PreservesFiltersAndAddsGroupedAggregate()
{
    const string fetch =
        "<fetch mapping='logical' page='2' count='50'>" +
        "<entity name='contact'>" +
        "<attribute name='contactid'/>" +
        "<attribute name='fullname'/>" +
        "<order attribute='fullname'/>" +
        "<filter><condition attribute='statecode' operator='eq' value='0'/></filter>" +
        "</entity></fetch>";

    var result = MemberInfoCommitmentTypeCountQuery.CreateValueCountsFetch(fetch);
    var document = XDocument.Parse(result);

    document.Root!.Attribute("aggregate")!.Value.Should().Be("true");
    document.Root.Attribute("page").Should().BeNull();
    document.Root.Attribute("count").Should().BeNull();
    document.Descendants("condition").Should().ContainSingle();
    document.Descendants("order").Should().BeEmpty();
    document.Descendants("attribute")
        .Single(node => node.Attribute("alias")?.Value == "commitmenttype")
        .Attribute("groupby")?.Value.Should().Be("true");
    document.Descendants("attribute")
        .Single(node => node.Attribute("alias")?.Value == "rowcount")
        .Attribute("aggregate")?.Value.Should().Be("countcolumn");
}

[Fact]
public void ReadValueCounts_HandlesOptionSetAndIntegerAliases()
{
    var rows = new EntityCollection
    {
        Entities =
        {
            AggregateRow(new OptionSetValue(100000006), 7),
            AggregateRow(1, 11)
        }
    };

    MemberInfoCommitmentTypeCountQuery.ReadValueCounts(rows)
        .Should().BeEquivalentTo(new Dictionary<int, int>
        {
            [100000006] = 7,
            [1] = 11
        });
}
```

Use this helper and add the explicit fallback assertions:

```csharp
private static Entity AggregateRow(object value, object count)
{
    var row = new Entity("contact");
    row[MemberInfoCommitmentTypeCountQuery.ValueAlias] =
        new AliasedValue("contact", "customertypecode", value);
    row[MemberInfoCommitmentTypeCountQuery.CountAlias] =
        new AliasedValue("contact", "contactid", count);
    return row;
}

[Fact]
public void CreateValueCountsFetch_BlankXmlThrowsArgumentException()
{
    var act = () =>
        MemberInfoCommitmentTypeCountQuery.CreateValueCountsFetch(" ");
    act.Should().Throw<ArgumentException>();
}

[Fact]
public void ReadValueCounts_SkipsMissingAliasesAndSumsDuplicateValues()
{
    var rows = new EntityCollection
    {
        Entities =
        {
            AggregateRow(new OptionSetValue(1), 2L),
            AggregateRow(1, 3m),
            new Entity("contact")
        }
    };

    MemberInfoCommitmentTypeCountQuery.ReadValueCounts(rows)
        .Should().ContainSingle()
        .Which.Should().Be(new KeyValuePair<int, int>(1, 5));
}
```

Add one malformed-XML assertion expecting `XmlException` so parser failures remain explicit.

- [ ] **Step 2: Run count-query tests and verify RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~MemberInfoCommitmentTypeCountQueryTests --no-restore
```

Expected: compilation fails because `MemberInfoCommitmentTypeCountQuery` does not exist.

- [ ] **Step 3: Implement aggregate transformation and parsing**

Create:

```csharp
public static class MemberInfoCommitmentTypeCountQuery
{
    public const string ValueAlias = "commitmenttype";
    public const string CountAlias = "rowcount";

    public static string CreateValueCountsFetch(string fetchXml)
    {
        if (string.IsNullOrWhiteSpace(fetchXml))
            throw new ArgumentException("FetchXML must not be blank.", nameof(fetchXml));

        var document = XDocument.Parse(fetchXml, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidOperationException("FetchXML must contain a root element.");
        var entity = root.Elements()
            .Single(element => element.Name.LocalName == "entity");

        root.SetAttributeValue("aggregate", "true");
        root.Attributes()
            .Where(attribute => attribute.Name.LocalName is
                "page" or "count" or "paging-cookie" or "returntotalrecordcount")
            .Remove();
        entity.Elements()
            .Where(element => element.Name.LocalName is "attribute" or "order")
            .Remove();
        entity.Add(
            new XElement(entity.Name.Namespace + "attribute",
                new XAttribute("name", "customertypecode"),
                new XAttribute("alias", ValueAlias),
                new XAttribute("groupby", "true")),
            new XElement(entity.Name.Namespace + "attribute",
                new XAttribute("name", "contactid"),
                new XAttribute("alias", CountAlias),
                new XAttribute("aggregate", "countcolumn")));
        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static IReadOnlyDictionary<int, int> ReadValueCounts(
        EntityCollection? rows)
    {
        var result = new Dictionary<int, int>();
        foreach (var row in rows?.Entities ?? Enumerable.Empty<Entity>())
        {
            var valueObject = Unwrap(row, ValueAlias);
            var countObject = Unwrap(row, CountAlias);
            var value = valueObject is OptionSetValue option ? option.Value
                : valueObject is int integer ? integer
                : (int?)null;
            if (!value.HasValue || countObject == null)
                continue;

            var count = Math.Max(0, Convert.ToInt32(countObject));
            result[value.Value] = result.GetValueOrDefault(value.Value) + count;
        }
        return result;
    }

    private static object? Unwrap(Entity row, string alias)
    {
        if (!row.Attributes.TryGetValue(alias, out var value))
            return null;
        return value is AliasedValue aliased ? aliased.Value : value;
    }
}
```

Keep all XML mutations structural; never concatenate user search input into XML.

- [ ] **Step 4: Verify GREEN**

Run the focused count-query tests and `git diff --check`. Expected: all tests pass and no
whitespace errors. Do not Commit.

---

### Task 4: Integrate rank mapping into group/search and segmented ungrouped paging

**Files:**

- Modify: `ChurchReport/Controllers/MemberInfoController.cs`
- Modify: `ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs`
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs`
- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs`

- [ ] **Step 1: Replace controller/search contracts with failing metadata-rank contracts**

Replace raw-value assertions with:

```csharp
[Fact]
public void Controller_MapsConfiguredCommitmentOrderWithoutExposingRawSortValue()
{
    Source.Should().Contain("MemberInfoCommitmentTypeMetadataProvider");
    Source.Should().Contain("MembershipStatusOrder = commitmentOption?.Order");
    Source.Should().Contain("HasMembershipStatusValue = membershipStatusValue.HasValue");
    Source.Should().Contain("MemberInfoCommitmentTypeSort.OrderRows(");
    Source.Should().NotContain("MembershipStatusValue = membershipStatusValue");
}

[Fact]
public void Controller_UsesConfiguredSegmentsBeforeUngroupedPaging()
{
    Source.Should().Contain("MemberInfoCommitmentTypeCountQuery.CreateValueCountsFetch");
    Source.Should().Contain("MemberInfoCommitmentTypeCountQuery.ReadValueCounts");
    Source.Should().Contain("MemberInfoCommitmentTypeSort.BuildSegments(");
    Source.Should().Contain("MemberInfoCommitmentTypeSort.PlanSlices(");
    Source.Should().NotContain("EnableRawChoiceOrdering");
    Source.Should().NotContain("useraworderby");
    Source.Should().NotContain("query.AddOrder(\"customertypecode\"");
}
```

Rewrite the search test rows to set `MembershipStatusOrder` and
`HasMembershipStatusValue`. Use rank 0 for the contact whose label represents「牧師師母」and
rank 1 for another member even when the test helper's comments mention the larger underlying
CRM value. Assert authorization filtering and case-insensitive ContactId deduplication still run
before rank sorting.

- [ ] **Step 2: Run controller and search tests and verify RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~MemberInfoTreeControllerContractTests|FullyQualifiedName~MemberInfoTreeSearchBuilderTests" --no-restore
```

Expected: the new metadata-rank and segmented-query assertions fail against the current raw path.

- [ ] **Step 3: Store the injected shared cache and expose configured options**

Add to `MemberInfoController`:

```csharp
private readonly IMemoryCache memberInfoMemoryCache;
```

Assign the existing constructor argument after the base call:

```csharp
memberInfoMemoryCache = memoryCache
    ?? throw new ArgumentNullException(nameof(memoryCache));
```

Add:

```csharp
private IReadOnlyList<MemberInfoCommitmentTypeOption> GetCommitmentTypeOptions(
    IOrganizationService service)
{
    return new MemberInfoCommitmentTypeMetadataProvider(
        service,
        memberInfoMemoryCache).GetOptions();
}
```

Update `GetSharedOptionSetService` to use the same injected cache instead of resolving it from
`HttpContext.RequestServices`.

- [ ] **Step 4: Map labels, rank, and has-value once per row build**

At the start of `BuildMemberRows`:

```csharp
var commitmentOptions = GetCommitmentTypeOptions(service);
var commitmentByValue = commitmentOptions
    .GroupBy(option => option.Value)
    .ToDictionary(group => group.Key, group => group.First());
```

For each contact:

```csharp
var membershipStatusValue =
    contact.GetAttributeValue<OptionSetValue>("customertypecode")?.Value;
commitmentByValue.TryGetValue(
    membershipStatusValue.GetValueOrDefault(),
    out var commitmentOption);

MembershipStatusOrder = membershipStatusValue.HasValue
    ? commitmentOption?.Order
    : null,
HasMembershipStatusValue = membershipStatusValue.HasValue,
MembershipStatus = membershipStatusValue.HasValue
    ? commitmentOption?.Label
        ?? ResolveOptionSetText(optionService, contact, "customertypecode")
    : string.Empty,
```

Do not place the raw value on the DTO. Keep gender, birthday, phone, address, spiritual identity,
and relationship goals unchanged.

- [ ] **Step 5: Keep general and search rows on the shared rank sorter**

`LoadGroupMembers` remains:

```csharp
var rows = MemberInfoCommitmentTypeSort.OrderRows(
    BuildMemberRows(service, contacts, relations));
```

`MemberInfoTreeSearchBuilder.Build` must filter allowed IDs, deduplicate ContactId, then call:

```csharp
Rows = MemberInfoCommitmentTypeSort.OrderRows(authorizedRows)
```

Replace raw-value comments with metadata-rank comments.

- [ ] **Step 6: Build exact configured/unknown/empty segment queries**

Change `BuildUngroupedCommitmentSegmentQuery` to accept:

```csharp
MemberInfoCommitmentTypeSegmentKind kind,
int? optionValue,
IReadOnlyCollection<int> configuredValues
```

After building the existing base query:

```csharp
switch (kind)
{
    case MemberInfoCommitmentTypeSegmentKind.Configured:
        query.Criteria.AddCondition(
            "customertypecode",
            ConditionOperator.Equal,
            optionValue!.Value);
        break;
    case MemberInfoCommitmentTypeSegmentKind.Unknown:
        query.Criteria.AddCondition("customertypecode", ConditionOperator.NotNull);
        if (configuredValues.Count > 0)
        {
            query.Criteria.AddCondition(
                "customertypecode",
                ConditionOperator.NotIn,
                configuredValues.Select(value => (object)value).ToArray());
        }
        break;
    case MemberInfoCommitmentTypeSegmentKind.Empty:
        query.Criteria.AddCondition("customertypecode", ConditionOperator.Null);
        break;
}
query.AddOrder("fullname", OrderType.Ascending);
query.AddOrder("contactid", OrderType.Ascending);
```

Never add an order on `customertypecode`.

- [ ] **Step 7: Count non-empty values once and null separately**

Add:

```csharp
private IReadOnlyDictionary<int, int> CountUngroupedCommitmentValues(
    IOrganizationService service,
    string search,
    IReadOnlyCollection<Guid> groupedIds,
    int closedStatus,
    IReadOnlyCollection<int> matchingStatusValues)
{
    var query = BuildUngroupedBaseQuery(
        new ColumnSet("contactid", "customertypecode"),
        search,
        groupedIds,
        closedStatus,
        matchingStatusValues);
    var response = (QueryExpressionToFetchXmlResponse)service.Execute(
        new QueryExpressionToFetchXmlRequest { Query = query });
    var countFetch =
        MemberInfoCommitmentTypeCountQuery.CreateValueCountsFetch(response.FetchXml);
    var rows = service.RetrieveMultiple(new FetchExpression(countFetch));
    return MemberInfoCommitmentTypeCountQuery.ReadValueCounts(rows);
}
```

Replace the former typed/null counter with this null-only helper:

```csharp
private int CountUngroupedEmptyCommitmentSegment(
    IOrganizationService service,
    string search,
    IReadOnlyCollection<Guid> groupedIds,
    int closedStatus,
    IReadOnlyCollection<int> matchingStatusValues)
{
    var query = BuildUngroupedBaseQuery(
        new ColumnSet("contactid"),
        search,
        groupedIds,
        closedStatus,
        matchingStatusValues);
    query.Criteria.AddCondition("customertypecode", ConditionOperator.Null);
    query.PageInfo = new PagingInfo
    {
        Count = 1,
        PageNumber = 1,
        PagingCookie = null,
        ReturnTotalRecordCount = true
    };
    var result = service.RetrieveMultiple(query);
    return result.TotalRecordCount >= 0
        ? result.TotalRecordCount
        : result.Entities.Count;
}
```

- [ ] **Step 8: Compose the ungrouped page from metadata segments**

In `LoadUngroupedCommitmentTypePage`:

```csharp
var options = GetCommitmentTypeOptions(service);
var configuredValues = options
    .OrderBy(option => option.Order)
    .Select(option => option.Value)
    .Distinct()
    .ToArray();
var countsByValue = CountUngroupedCommitmentValues(
    service, search, groupedIds, closedStatus, matchingStatusValues);
var emptyCount = CountUngroupedEmptyCommitmentSegment(
    service, search, groupedIds, closedStatus, matchingStatusValues);
var segments = MemberInfoCommitmentTypeSort.BuildSegments(
    configuredValues,
    countsByValue,
    emptyCount,
    descending);
```

Normalize `skip/take` as before, then:

```csharp
foreach (var slice in MemberInfoCommitmentTypeSort.PlanSlices(
             skip, take, segments))
{
    contacts.AddRange(RetrieveUngroupedSegmentRange(
        service,
        () => BuildUngroupedCommitmentSegmentQuery(
            columns,
            search,
            groupedIds,
            closedStatus,
            matchingStatusValues,
            slice.Kind,
            slice.Value,
            configuredValues),
        slice.Skip,
        slice.Take));
}

return new UngroupedContactPage
{
    Contacts = contacts,
    TotalCount = segments.Sum(segment => segment.Count)
};
```

Remove `RetrieveRawChoiceOrderedPage` and the `rawChoiceOrder` parameter from
`RetrieveUngroupedSegmentRange`. Always call `service.RetrieveMultiple(query)` for a segment.

- [ ] **Step 9: Route the remote selector and protect ordinary sorts**

`TryGetCommitmentTypeSort` must recognize
`MemberInfoCommitmentTypeSort.Selector` (`MembershipStatusOrder`) and the visible legacy selector
`MembershipStatus`. It must not map either selector to a normal CRM attribute inside
`MapUngroupedSortAttribute`; the segmented path handles them before ordinary sorting.

- [ ] **Step 10: Verify GREEN and inspect the integration**

Run the Task 4 focused test command. Expected: controller and search tests pass.

Then run:

```powershell
rg -n "MembershipStatusValue|EnableRawChoiceOrdering|useraworderby|AddOrder\\(\\s*\"customertypecode\"" ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Services/MemberInfo ChurchReport/ViewModels/MemberInfoTree ChurchReport.MemberInfo.Tests
git diff --check
git diff -- ChurchReport/Controllers/MemberInfoController.cs ChurchReport/Services/MemberInfo/MemberInfoTreeSearchBuilder.cs ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs ChurchReport.MemberInfo.Tests/MemberInfoTreeSearchBuilderTests.cs
```

Expected: no obsolete raw-sort matches, no whitespace errors, and only approved integration
changes. Do not Commit.

---

### Task 5: Make the visible member-status column use configured rank

**Files:**

- Modify: `ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs`
- Modify: `ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml`

- [ ] **Step 1: Replace the invalid raw-selector view test**

Replace the currently failing `MembershipStatusValue` test with:

```csharp
[Fact]
public void View_SortsVisibleMembershipStatusByConfiguredOrderAndKeepsFallbacksLast()
{
    var columns = Slice(
        "function miMemberColumns(remotePaging)",
        "function miGridScrollingOptions()");

    ViewText.Should().Contain("function miMembershipStatusSortValue(row)");
    ViewText.Should().Contain("row.MembershipStatusOrder");
    ViewText.Should().Contain("row.HasMembershipStatusValue");
    ViewText.Should().Contain("this.sortOrder === 'desc'");
    ViewText.Should().Contain("Number.MIN_SAFE_INTEGER + 1");
    ViewText.Should().Contain("Number.MAX_SAFE_INTEGER - 1");
    columns.Should().MatchRegex(
        @"(?s)dataField:\\s*'MembershipStatus'[^}]*caption:\\s*'會員身份'[^}]*" +
        @"calculateSortValue:\\s*remotePaging\\s*\\?\\s*'MembershipStatusOrder'\\s*:\\s*" +
        @"miMembershipStatusSortValue[^}]*sortOrder:\\s*'asc'[^}]*sortIndex:\\s*0");
    columns.Should().NotContain("dataField: 'MembershipStatusOrder'");
    columns.Split("dataField:", StringSplitOptions.None).Length.Should().Be(10);
}
```

- [ ] **Step 2: Run view tests and verify RED**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~MemberInfoTreeViewContractTests --no-restore
```

Expected: the configured-order helper/selector assertions fail.

- [ ] **Step 3: Add the direction-aware local sort key**

Place this helper immediately before `miMemberColumns`:

```javascript
function miMembershipStatusSortValue(row) {
    var descending = this.sortOrder === 'desc';
    if (row && row.MembershipStatusOrder != null) {
        return row.MembershipStatusOrder;
    }
    if (row && row.HasMembershipStatusValue) {
        return descending
            ? Number.MIN_SAFE_INTEGER + 1
            : Number.MAX_SAFE_INTEGER - 1;
    }
    return descending
        ? Number.MIN_SAFE_INTEGER
        : Number.MAX_SAFE_INTEGER;
}
```

In ascending order, configured ranks are below both high sentinels. In descending order,
configured ranks are above both low sentinels. The +1/-1 distinction keeps unknown values before
true blanks in both directions.

- [ ] **Step 4: Configure only the existing visible column**

Replace the existing member-status column with:

```javascript
{
    dataField: 'MembershipStatus',
    caption: '會員身份',
    width: 110,
    alignment: 'center',
    calculateSortValue: remotePaging
        ? 'MembershipStatusOrder'
        : miMembershipStatusSortValue,
    sortOrder: 'asc',
    sortIndex: 0
},
```

Do not add a visible rank, raw value, or has-value column.

- [ ] **Step 5: Verify GREEN and JavaScript syntax**

Run the focused view tests. Expected: all pass.

Extract the Razor `<script>` content into a temporary file outside the repository and replace the
single server-side boolean expression before parsing:

```powershell
$view = Get-Content -Raw -Encoding UTF8 'ChurchReport\Views\MemberInfo\MemberInfoGrid.cshtml'
$match = [regex]::Match($view, '(?s)<script>(.*)</script>')
if (-not $match.Success) { throw 'MemberInfoGrid script block not found.' }
$script = $match.Groups[1].Value -replace '@\(ViewBag\.MemberInfoCanResync == true \? "true" : "false"\)', 'false'
$tempScript = Join-Path $env:TEMP 'MemberInfoGrid.syntax.js'
[IO.File]::WriteAllText($tempScript, $script, [Text.UTF8Encoding]::new($false))
node --check $tempScript
Remove-Item -LiteralPath $tempScript
git diff --check
git diff -- ChurchReport/Views/MemberInfo/MemberInfoGrid.cshtml ChurchReport.MemberInfo.Tests/MemberInfoTreeViewContractTests.cs
```

Expected: JavaScript parses, nine visible fields remain, and no unrelated UI contract changes.
Do not Commit.

---

### Task 6: Complete automated verification and review

**Files:**

- Verify all Task 1–5 files.
- Create/update: `.ccg/tasks/sort-member-info-by-commitment-type/review.md`
- Update: `.ccg/tasks/sort-member-info-by-commitment-type/task.json`

- [ ] **Step 1: Run every member-info test**

Run:

```powershell
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore
```

Record exact passed, failed, and skipped totals.

- [ ] **Step 2: Build affected projects**

If a verified `ChurchReport`/IIS Express process locks output, stop only that process; do not
delete build output.

Run:

```powershell
dotnet build ChurchReport/ChurchReport.csproj --no-restore
dotnet build ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore
```

Record exact warning/error totals.

- [ ] **Step 3: Verify UTF-8, JavaScript, and repository state**

Run strict UTF-8 decoding for every created/modified task, source, test, spec, and plan file:

```powershell
$files = @(
    '.ccg\tasks\sort-member-info-by-commitment-type\requirements.md',
    '.ccg\tasks\sort-member-info-by-commitment-type\task.json',
    '.ccg\tasks\sort-member-info-by-commitment-type\review.md',
    'docs\superpowers\specs\2026-07-18-member-info-commitment-type-sorting-design.md',
    'docs\superpowers\plans\2026-07-18-member-info-commitment-type-sorting.md',
    'ChurchReport\Services\MemberInfo\MemberInfoCommitmentTypeMetadataProvider.cs',
    'ChurchReport\Services\MemberInfo\MemberInfoCommitmentTypeSort.cs',
    'ChurchReport\Services\MemberInfo\MemberInfoCommitmentTypeCountQuery.cs',
    'ChurchReport\Services\MemberInfo\MemberInfoTreeSearchBuilder.cs',
    'ChurchReport\ViewModels\MemberInfoTree\DistrictTreeViewModels.cs',
    'ChurchReport\Controllers\MemberInfoController.cs',
    'ChurchReport\Views\MemberInfo\MemberInfoGrid.cshtml',
    'ChurchReport.MemberInfo.Tests\MemberInfoCommitmentTypeMetadataProviderTests.cs',
    'ChurchReport.MemberInfo.Tests\MemberInfoCommitmentTypeSortTests.cs',
    'ChurchReport.MemberInfo.Tests\MemberInfoCommitmentTypeCountQueryTests.cs',
    'ChurchReport.MemberInfo.Tests\MemberInfoTreeControllerContractTests.cs',
    'ChurchReport.MemberInfo.Tests\MemberInfoTreeSearchBuilderTests.cs',
    'ChurchReport.MemberInfo.Tests\MemberInfoTreeViewContractTests.cs'
)
$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
foreach ($file in $files) {
    $bytes = [IO.File]::ReadAllBytes((Resolve-Path $file))
    $null = $strictUtf8.GetString($bytes)
}
```

Then run:

```powershell
git diff --check
git status --short --branch
git diff --stat
git diff -- ChurchReport ChurchReport.MemberInfo.Tests
```

Confirm no CRM metadata/data, unrelated feature, Commit, merge, or push occurred.

- [ ] **Step 4: Audit every approved requirement**

Write a requirement-to-evidence table into `review.md` proving:

1. metadata collection order is authoritative;
2. value `100000006` can have rank 0 and appear first;
3. no raw integer, label, or hard-coded church order drives sorting;
4. general group and search use rank/name/ContactId after authorization;
5. ungrouped counts and segments occur before paging;
6. unknown values remain after configured values and are never dropped;
7. true blanks remain last in both directions;
8. the visible label and nine-column layout remain unchanged;
9. existing fixed-column, touch-scroll, search-return, and authorization tests still pass.

- [ ] **Step 5: Call both CCG reviewers once and record service failures honestly**

Run Gemini and Claude in parallel against the complete diff using their `reviewer` roles:

```powershell
$diff = git diff
$geminiPrompt = @"
ROLE_FILE: ~/.claude/.ccg/prompts/gemini/reviewer.md
<TASK>審查以下會友資訊 metadata 排序完整差異：
$diff
</TASK>
OUTPUT: Critical/Warning/Info 分級報告
"@
$claudePrompt = @"
ROLE_FILE: ~/.claude/.ccg/prompts/claude/reviewer.md
<TASK>審查以下會友資訊 metadata 排序完整差異：
$diff
</TASK>
OUTPUT: Critical/Warning/Info 分級報告
"@
$wrapper = Join-Path $HOME '.claude\bin\codeagent-wrapper'
$worktree = (Get-Location).Path
$gemini = Start-Job -ArgumentList $wrapper, $worktree, $geminiPrompt -ScriptBlock {
    param($wrapperPath, $root, $prompt)
    Set-Location $root
    $prompt | & $wrapperPath --progress --backend gemini - $root
}
$claude = Start-Job -ArgumentList $wrapper, $worktree, $claudePrompt -ScriptBlock {
    param($wrapperPath, $root, $prompt)
    Set-Location $root
    $prompt | & $wrapperPath --progress --backend claude - $root
}
Wait-Job $gemini, $claude | Receive-Job
Remove-Job $gemini, $claude
```

The user explicitly instructed that Gemini HTTP 403 and Claude empty/wrapper failures are to be
ignored as gates. Record either real findings or the exact service failure in `review.md`; do not
claim a failed call passed, and do not repeatedly retry known quota failures.

- [ ] **Step 6: Fix verified Critical/Warning findings and rerun affected checks**

Only implement findings that are technically valid for this codebase and within the approved
scope. Re-run the smallest affected tests, then the complete test command. Record accepted or
rejected findings with evidence.

- [ ] **Step 7: Leave the task ready for VS testing**

Set `task.json` to:

```json
{
  "status": "in_progress",
  "currentPhase": "review",
  "nextAction": "等待使用者在 VS 2026 驗證 Dynamics 客製化委身類型順序"
}
```

Preserve the other task fields. Do not archive and do not Commit.

---

### Task 7: Verify the real UI in VS 2026

**Files:** None unless runtime testing reveals a defect.

- [ ] **Step 1: Verify the running project path**

The process must run:

```text
<來源儲存庫根目錄>\ChurchReport
```

Confirm the process command line and IIS Express applicationhost path rather than trusting an old
<本機連接埠>/<程序 PID> process.

- [ ] **Step 2: Verify configured order in a general group**

Open a group containing multiple statuses. Expected: the first configured Dynamics option
(currently「牧師師母」) appears before later configured options regardless of raw integer value.

- [ ] **Step 3: Toggle the member-status header twice**

Expected: configured options reverse and return; unknown values stay after configured options;
blank values stay last; cells remain Chinese labels.

- [ ] **Step 4: Verify search results**

Search for contacts spanning multiple configured statuses. Expected: the replacement grid uses
the same configured order and returning restores the browse tree.

- [ ] **Step 5: Verify ungrouped page boundaries**

Use page sizes 25 and 50 and inspect Network sort payloads. Expected selector:
`MembershipStatusOrder`; no duplicate/missing rows; category boundaries continue correctly
across pages.

- [ ] **Step 6: Recheck responsive behavior**

Expected: avatar/name remain fixed, one horizontal scrollbar remains, touch swiping works, and no
adaptive dots, zoom, or search-row regression appears.

- [ ] **Step 7: Request separate Commit authorization**

Report automated and VS evidence. Wait for explicit user authorization before Commit, archive,
merge, or push.

---

## Plan self-review

- Every approved spec section maps to Tasks 1–7.
- All names are consistent: `MemberInfoCommitmentTypeOption`,
  `MemberInfoCommitmentTypeMetadataProvider`, `MembershipStatusOrder`,
  `HasMembershipStatusValue`, `MemberInfoCommitmentTypeSegmentKind`,
  `MemberInfoCommitmentTypeSegment`, and `MemberInfoCommitmentTypeSlice`.
- Configured order is taken only from metadata collection position.
- Unknown and null rows remain represented in local and remote paths.
- The ungrouped path retrieves page slices rather than the full church.
- Raw ordering and `useraworderby` have explicit removal tests.
- No placeholder or unresolved implementation step remains.
- Commit is intentionally omitted until the user finishes VS verification.

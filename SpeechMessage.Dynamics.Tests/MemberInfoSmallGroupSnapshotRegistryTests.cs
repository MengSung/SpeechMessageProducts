// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/MemberInfoSmallGroupSnapshotRegistryTests.cs
// 用途：以先紅後綠的契約測試守護 MemberInfo 小組快照 composed operation 的抽象層邊界。
//
// 本測試只讀取 process-static registry、封閉 response union 與目前案例內建立的純值 wire records；不建立
// CE、Data8 connector、連線池、租約、Session、cache、背景工作、計時器或任何可跨使用者保留的資源。每一個
// GUID 與文字 marker 僅存在於測試 stack frame，故紅燈代表缺少安全 capability contract，而不是可退回 legacy
// ListManager、Entity 或 caller-selected query 的理由。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 ORG-CALL-00031／00032 必須透過單一、固定、CE 9.1 local-only 的小組快照 response contract 發布。
/// 測試刻意不接線 controller、feature gate、traffic 或真實 CE；它保護的是未來 Data8／ProductClient 實作只能使用
/// server-owned scope 純量與 immutable DTO，而不能保存上一個使用者的 scope、profile、credential、Entity 或集合。
/// </summary>
public sealed class MemberInfoSmallGroupSnapshotRegistryTests
{
    private const string OperationId = "memberinfo.smallgroup.snapshot.retrieve.authorized";
    private const string TemplateId = "memberinfo.smallgroup.snapshot.authorized.v1";
    private const string ResponseKindName = "MemberInfoSmallGroupSnapshot";

    /// <summary>
    /// 保護 registry 只能宣告已驗證 scope 的三個固定純量參數、CE 9.1 的單頁 bounded composed query 與安全稽核。
    /// 故障注入是 feature 尚未登錄時的 capability lookup；決定性斷言拒絕 list/contact browser locator、query、
    /// filter、closed status、profile、endpoint、credential、owner 或 CRM SDK graph 混入 dispatcher 前的 allowlist。
    /// </summary>
    [Fact]
    public void Registry_declares_the_exact_ce91_bounded_authorized_small_group_snapshot_operation()
    {
        Package01OperationRegistry.TryGet(OperationId, out var definition).Should().BeTrue(
            because: "00031 與 00032 必須由同一個固定 composed operation 取得，不能讓 caller 串接兩個任意 CRM query");

        definition.Should().NotBeNull();
        definition!.OperationKind.Should().Be("read");
        definition.TemplateKind.Should().Be("queryexpression");
        definition.TemplateId.Should().Be(TemplateId);
        definition.ResponseKind.ToString().Should().Be(ResponseKindName);
        definition.DataClassification.Should().Be("personal-data");
        definition.AuditRequirement.Should().Be("security-audit");
        definition.IdempotencyClass.Should().Be("read-only");
        definition.MaximumPageCount.Should().Be(1);
        definition.MaximumPageBytes.Should().Be(512 * 1024);
        definition.MaximumCumulativeResponseBytes.Should().Be(1024 * 1024);
        definition.MaximumResultItemCount.Should().Be(4096);
        definition.Parameters.Select(parameter => new
        {
            parameter.Name,
            parameter.Type,
            parameter.Required,
            parameter.EncodingContext
        }).Should().BeEquivalentTo(
            [
                new
                {
                    Name = "subjectContactId",
                    Type = "guid",
                    Required = true,
                    EncodingContext = "queryexpression-condition"
                },
                new
                {
                    Name = "accessMode",
                    Type = "enum",
                    Required = true,
                    EncodingContext = "server-enum"
                },
                new
                {
                    Name = "visibleListIds",
                    Type = "guid-array",
                    Required = true,
                    EncodingContext = "guid-array-canonical"
                }
            ],
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// 保護 response union 必須有唯一 snapshot discriminator、constructor branch、property、factory 與三個獨立
    /// wire types。故障注入是這些符號尚未存在；決定性斷言使實作不得借用 catalog／membership branch、<c>object</c>
    /// 或 CRM type，避免不同 capability 的資料或另一個 request 的可變集合混入快照。
    /// </summary>
    [Fact]
    public void Response_union_exposes_one_closed_memberinfo_small_group_snapshot_branch()
    {
        Enum.GetNames<OperationResponseKind>().Should().Contain(ResponseKindName);

        var constructor = typeof(OperationResponseData).GetConstructors().Should().ContainSingle().Subject;
        constructor.GetParameters().Select(parameter => parameter.Name)
            .Should().Contain("memberInfoSmallGroupSnapshot");
        typeof(OperationResponseData).GetProperty("MemberInfoSmallGroupSnapshot").Should().NotBeNull();
        typeof(OperationResponseData).GetMethods().Select(method => method.Name)
            .Should().Contain("ForMemberInfoSmallGroupSnapshot");

        var assembly = typeof(OperationResponseData).Assembly;
        GetRequiredType(assembly, "MemberInfoSmallGroupDescriptorRecord")
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new { property.Name, property.PropertyType })
            .Should().Equal(
                new { Name = "AreaName", PropertyType = typeof(string) },
                new { Name = "GroupLeaderName", PropertyType = typeof(string) },
                new { Name = "GroupPlace", PropertyType = typeof(string) },
                new { Name = "GroupTime", PropertyType = typeof(string) },
                new { Name = "ListId", PropertyType = typeof(Guid) },
                new { Name = "ListName", PropertyType = typeof(string) },
                new { Name = "RaceLeaderContactId", PropertyType = typeof(Guid?) },
                new { Name = "RaceLeaderName", PropertyType = typeof(string) });
        GetRequiredType(assembly, "MemberInfoSmallGroupMembershipRecord")
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new { property.Name, property.PropertyType })
            .Should().Equal(
                new { Name = "ContactId", PropertyType = typeof(Guid) },
                new { Name = "ListId", PropertyType = typeof(Guid) });
        GetRequiredType(assembly, "MemberInfoSmallGroupSnapshotResponseData")
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(property => property.Name)
            .Should().Contain(new[] { "SubjectContactId", "AccessMode", "Descriptors", "Memberships" });
    }

    /// <summary>
    /// 保護 snapshot factory 會在發布前建立獨立、唯讀的 descriptor 與 membership copy。故障注入是在 snapshot
    /// 建構後改寫原始 typed array；決定性斷言要求 envelope 保持 A marker 並使用唯一正確 discriminator，避免
    /// connector、serializer 或平行 B request 能把資料替換到 A 已授權的快照。
    /// </summary>
    [Fact]
    public void Snapshot_factory_defensively_copies_typed_records_before_publishing_the_only_snapshot_branch()
    {
        var assembly = typeof(OperationResponseData).Assembly;
        var descriptorType = GetRequiredType(assembly, "MemberInfoSmallGroupDescriptorRecord");
        var membershipType = GetRequiredType(assembly, "MemberInfoSmallGroupMembershipRecord");
        var snapshotType = GetRequiredType(assembly, "MemberInfoSmallGroupSnapshotResponseData");
        var listId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var subjectContactId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var descriptors = Array.CreateInstance(descriptorType, 1);
        descriptors.SetValue(CreateDescriptor(descriptorType, listId, "descriptor-A"), 0);
        var memberships = Array.CreateInstance(membershipType, 1);
        memberships.SetValue(CreateMembership(membershipType, listId, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")), 0);

        var snapshot = CreateSnapshot(snapshotType, subjectContactId, descriptors, memberships);
        descriptors.SetValue(CreateDescriptor(descriptorType, listId, "descriptor-B"), 0);
        var response = InvokeSnapshotFactory(snapshot);

        response.ResponseKind.ToString().Should().Be(ResponseKindName);
        var publishedSnapshot = typeof(OperationResponseData).GetProperty("MemberInfoSmallGroupSnapshot")!
            .GetValue(response)!;
        var publishedDescriptors = ((System.Collections.IEnumerable)snapshotType.GetProperty("Descriptors")!
                .GetValue(publishedSnapshot)!)
            .Cast<object>()
            .ToArray();
        publishedDescriptors.Should().ContainSingle();
        descriptorType.GetProperty("ListName")!.GetValue(publishedDescriptors[0]).Should().Be("descriptor-A");
        snapshotType.GetProperty("Descriptors")!.GetValue(publishedSnapshot)
            .Should().NotBeOfType<Array>(because: "published snapshot 不可暴露可被 downcast 改寫的 backing array");
    }

    /// <summary>
    /// 保護 malformed snapshot 在 response boundary fail closed。故障注入同時覆蓋重複 descriptor identity、
    /// membership 指向未發布 descriptor、超過 512 descriptor／4,096 membership 上限，以及 strict UTF-8 無法編碼的
    /// surrogate；決定性斷言要求 factory 在發布前丟出 <see cref="ArgumentException"/>，不回傳 partial snapshot。
    /// </summary>
    [Fact]
    public void Snapshot_factory_fails_closed_for_duplicate_non_subset_overflow_and_invalid_utf8_records()
    {
        var assembly = typeof(OperationResponseData).Assembly;
        var descriptorType = GetRequiredType(assembly, "MemberInfoSmallGroupDescriptorRecord");
        var membershipType = GetRequiredType(assembly, "MemberInfoSmallGroupMembershipRecord");
        var snapshotType = GetRequiredType(assembly, "MemberInfoSmallGroupSnapshotResponseData");
        var subjectContactId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var descriptorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var contactId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var duplicateDescriptors = Array.CreateInstance(descriptorType, 2);
        duplicateDescriptors.SetValue(CreateDescriptor(descriptorType, descriptorId, "duplicate-A"), 0);
        duplicateDescriptors.SetValue(CreateDescriptor(descriptorType, descriptorId, "duplicate-B"), 1);
        AssertFactoryRejects(CreateSnapshot(
            snapshotType,
            subjectContactId,
            duplicateDescriptors,
            Array.CreateInstance(membershipType, 0)));

        var validDescriptors = Array.CreateInstance(descriptorType, 1);
        validDescriptors.SetValue(CreateDescriptor(descriptorType, descriptorId, "valid"), 0);
        var nonSubsetMemberships = Array.CreateInstance(membershipType, 1);
        nonSubsetMemberships.SetValue(CreateMembership(
            membershipType,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            contactId), 0);
        AssertFactoryRejects(CreateSnapshot(snapshotType, subjectContactId, validDescriptors, nonSubsetMemberships));

        var overflowingDescriptors = Array.CreateInstance(descriptorType, 513);
        for (var index = 0; index < overflowingDescriptors.Length; index++)
        {
            overflowingDescriptors.SetValue(CreateDescriptor(
                descriptorType,
                Guid.Parse($"{index + 1:x8}-0000-0000-0000-000000000000"),
                "overflow"), index);
        }

        AssertFactoryRejects(CreateSnapshot(
            snapshotType,
            subjectContactId,
            overflowingDescriptors,
            Array.CreateInstance(membershipType, 0)));

        var invalidUtf8Descriptors = Array.CreateInstance(descriptorType, 1);
        invalidUtf8Descriptors.SetValue(CreateDescriptor(descriptorType, descriptorId, "\ud800"), 0);
        AssertFactoryRejects(CreateSnapshot(
            snapshotType,
            subjectContactId,
            invalidUtf8Descriptors,
            Array.CreateInstance(membershipType, 0)));
    }

    /// <summary>
    /// 由 abstraction assembly 取得預期封閉 wire type。若型別尚未存在，立即失敗使 RED 明確指向缺少 contract，
    /// 而不是建立可繞過 registry 的測試替身或 generic object payload。
    /// </summary>
    /// <param name="assembly">擁有 response union 的 abstraction assembly。</param>
    /// <param name="typeName">不含 namespace 的固定封閉 wire type 名稱。</param>
    /// <returns>已載入的 public response contract type。</returns>
    private static Type GetRequiredType(System.Reflection.Assembly assembly, string typeName)
        => assembly.GetType($"SpeechMessage.Dynamics.Abstractions.Operations.{typeName}")
           ?? throw new InvalidOperationException($"Missing required snapshot contract type: {typeName}");

    /// <summary>
    /// 以 allowlisted scalar 建立 descriptor record。反射僅用於讓 feature 缺失時測試仍可編譯並產生 RED；正式
    /// ProductClient 不會使用反射或這個 helper，且 helper 不保存 records、session、profile 或任何外部資源。
    /// </summary>
    /// <param name="descriptorType">已驗證的 descriptor wire type。</param>
    /// <param name="listId">本次測試要發布的 non-empty list GUID。</param>
    /// <param name="listName">用來辨識 A/B defensive-copy 的 bounded display marker。</param>
    /// <returns>只含固定 scalar 的 descriptor record。</returns>
    private static object CreateDescriptor(Type descriptorType, Guid listId, string listName)
    {
        var descriptor = Activator.CreateInstance(descriptorType)!;
        descriptorType.GetProperty("ListId")!.SetValue(descriptor, listId);
        descriptorType.GetProperty("ListName")!.SetValue(descriptor, listName);
        descriptorType.GetProperty("AreaName")!.SetValue(descriptor, "area");
        descriptorType.GetProperty("RaceLeaderName")!.SetValue(descriptor, "race-leader");
        descriptorType.GetProperty("RaceLeaderContactId")!.SetValue(
            descriptor,
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));
        descriptorType.GetProperty("GroupLeaderName")!.SetValue(descriptor, "group-leader");
        descriptorType.GetProperty("GroupTime")!.SetValue(descriptor, "time");
        descriptorType.GetProperty("GroupPlace")!.SetValue(descriptor, "place");
        return descriptor;
    }

    /// <summary>
    /// 以 descriptor list ID 與 contact GUID 建立 membership record。這兩個值都是目前測試專屬純量，不是
    /// production routing／授權 authority，helper 也不建立 CRM relationship、cache 或背景工作。
    /// </summary>
    /// <param name="membershipType">已驗證的 membership wire type。</param>
    /// <param name="listId">必須屬於同一 snapshot descriptor 集合的 GUID。</param>
    /// <param name="contactId">目前測試專屬的 non-empty contact GUID。</param>
    /// <returns>只含封閉 membership scalar 的 wire record。</returns>
    private static object CreateMembership(Type membershipType, Guid listId, Guid contactId)
    {
        var membership = Activator.CreateInstance(membershipType)!;
        membershipType.GetProperty("ListId")!.SetValue(membership, listId);
        membershipType.GetProperty("ContactId")!.SetValue(membership, contactId);
        return membership;
    }

    /// <summary>
    /// 建立 Church-wide access mode 的 snapshot。mode enum 從 abstraction assembly 解析，使未實作 feature 時測試
    /// 仍可先失敗；這不是 caller 可選 mode，正式呼叫必須只接受 server-owned scope 轉換後的值。
    /// </summary>
    /// <param name="snapshotType">已驗證的 snapshot response type。</param>
    /// <param name="subjectContactId">本次 immutable snapshot 的 subject GUID。</param>
    /// <param name="descriptors">要由 snapshot constructor defensive-copy 的 typed descriptor array。</param>
    /// <param name="memberships">要由 snapshot constructor defensive-copy 的 typed membership array。</param>
    /// <returns>尚未發布到 envelope 的 request-local snapshot value。</returns>
    private static object CreateSnapshot(
        Type snapshotType,
        Guid subjectContactId,
        Array descriptors,
        Array memberships)
    {
        var accessModeType = GetRequiredType(
            typeof(OperationResponseData).Assembly,
            "MemberInfoAuthorizationAssignmentAccessMode");
        var churchWide = Enum.Parse(accessModeType, "ChurchWide");
        return Activator.CreateInstance(snapshotType, subjectContactId, churchWide, descriptors, memberships)!;
    }

    /// <summary>
    /// 呼叫唯一 snapshot factory。factory method 不接收 query、profile、credential、owner、Entity 或可變 collection；
    /// 反射只作測試 RED/contract verification，回傳 envelope 也只在本測試案例存活。
    /// </summary>
    /// <param name="snapshot">已建立的 immutable snapshot response value。</param>
    /// <returns>唯一 snapshot branch 的封閉 response envelope。</returns>
    private static OperationResponseData InvokeSnapshotFactory(object snapshot)
    {
        var factory = typeof(OperationResponseData).GetMethod("ForMemberInfoSmallGroupSnapshot")
            ?? throw new InvalidOperationException("Missing required snapshot response factory.");
        return (OperationResponseData)factory.Invoke(null, [OperationId, "9.1", snapshot])!;
    }

    /// <summary>
    /// 驗證 factory 不會發布 malformed snapshot。<see cref="System.Reflection.TargetInvocationException"/> 是反射
    /// 邊界的預期包裝；其內層 <see cref="ArgumentException"/> 才是 production response union 的 fail-closed 證據。
    /// </summary>
    /// <param name="snapshot">包含故障注入資料、但尚未發布的 snapshot。</param>
    private static void AssertFactoryRejects(object snapshot)
    {
        var act = () => InvokeSnapshotFactory(snapshot);

        var exception = act.Should().Throw<System.Reflection.TargetInvocationException>().Which;
        exception.InnerException.Should().BeOfType<ArgumentException>();
    }
}

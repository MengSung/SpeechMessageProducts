using FluentAssertions;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Worker binary envelope 的 canonical encoding、bounded value tree、metadata fencing 與公開型別去 SDK/去祕密契約。
/// 測試注入版本、nonce、deadline、operation、duplicate ID、深度與敏感欄位名稱故障；主要斷言是所有 fault
/// 在 SDK dispatch 前得到固定分類，且公開 protocol surface 不保留 credential、Session、endpoint 或 CRM SDK 型別。
/// </summary>
public sealed class WorkerEnvelopeCodecTests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 8, 0, 0, TimeSpan.Zero);

    /// <summary>證明各種 bounded scalar/array 只以封閉 WorkerValue round-trip，沒有任意 CLR/SDK 物件穿越 IPC。</summary>
    [Fact]
    public void Request_round_trip_preserves_only_bounded_typed_values()
    {
        var codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);
        var request = CreateRequest(new Dictionary<string, WorkerValue>
        {
            ["contactId"] = WorkerValue.FromGuid(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            ["includeInactive"] = WorkerValue.FromBoolean(false),
            ["page"] = WorkerValue.FromInt64(2),
            ["amount"] = WorkerValue.FromDecimal(1234.50m),
            ["label"] = WorkerValue.FromString("bounded"),
            ["columns"] = WorkerValue.FromArray(
            [
                WorkerValue.FromString("new_name"),
                WorkerValue.FromString("new_amount")
            ])
        });

        var payload = codec.SerializeRequest(request);
        var decoded = codec.DeserializeRequest(payload);

        decoded.ProtocolVersion.Should().Be(WorkerProtocolVersion.Current);
        decoded.ProcessNonce.Should().Be(Nonce);
        decoded.RequestId.Should().Be(request.RequestId);
        decoded.Parameters.Should().BeEquivalentTo(request.Parameters);
        payload.Length.Should().BeLessThanOrEqualTo(WorkerProtocolLimits.Default.MaximumFrameBytes);
    }

    /// <summary>以不同 dictionary insertion order 注入相同參數，證明 canonical bytes 不受可變容器順序影響。</summary>
    [Fact]
    public void SerializeRequest_is_canonical_across_parameter_insertion_order()
    {
        var codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);
        var requestId = Guid.NewGuid();
        var first = new WorkerRequestV1(
            WorkerProtocolVersion.Current,
            Nonce,
            requestId,
            "profile-generation-0001",
            "operation-revision-0001",
            "runtime.health.identity",
            Now.AddMinutes(1).UtcTicks,
            new Dictionary<string, WorkerValue>
            {
                ["zeta"] = WorkerValue.FromInt64(2),
                ["alpha"] = WorkerValue.FromInt64(1)
            });
        var second = new WorkerRequestV1(
            WorkerProtocolVersion.Current,
            Nonce,
            requestId,
            "profile-generation-0001",
            "operation-revision-0001",
            "runtime.health.identity",
            Now.AddMinutes(1).UtcTicks,
            new Dictionary<string, WorkerValue>
            {
                ["alpha"] = WorkerValue.FromInt64(1),
                ["zeta"] = WorkerValue.FromInt64(2)
            });

        codec.SerializeRequest(first).Should().Equal(codec.SerializeRequest(second));
    }

    /// <summary>逐一注入錯誤版本、nonce、過期 deadline 與未知 operation，證明 request 在 active-set/SDK 使用前 fail closed。</summary>
    [Theory]
    [InlineData(WorkerProtocolFailureCategory.UnsupportedProtocolVersion)]
    [InlineData(WorkerProtocolFailureCategory.InvalidProcessNonce)]
    [InlineData(WorkerProtocolFailureCategory.ExpiredDeadline)]
    [InlineData(WorkerProtocolFailureCategory.UnknownOperation)]
    public void ValidateAndRegister_rejects_wrong_request_metadata(
        WorkerProtocolFailureCategory expectedCategory)
    {
        var request = CreateRequest();
        var expectedNonce = Nonce;
        var now = Now;
        var allowedOperations = new HashSet<string>(StringComparer.Ordinal)
        {
            "runtime.health.identity"
        };

        request = expectedCategory switch
        {
            WorkerProtocolFailureCategory.UnsupportedProtocolVersion =>
                Clone(request, protocolVersion: WorkerProtocolVersion.Current + 1),
            WorkerProtocolFailureCategory.InvalidProcessNonce =>
                Clone(request, processNonce: "fedcba9876543210fedcba9876543210"),
            WorkerProtocolFailureCategory.ExpiredDeadline =>
                Clone(request, deadlineUtcTicks: now.AddTicks(-1).UtcTicks),
            WorkerProtocolFailureCategory.UnknownOperation =>
                Clone(request, capabilityOperationId: "unregistered.operation"),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedCategory))
        };

        var action = () => WorkerRequestValidator.ValidateAndRegister(
            request,
            expectedNonce,
            now,
            allowedOperations,
            new HashSet<Guid>(),
            WorkerProtocolLimits.Default);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(expectedCategory);
    }

    /// <summary>預先登錄相同 request ID，證明同一 Worker process 不能並行接受 duplicate delivery。</summary>
    [Fact]
    public void ValidateAndRegister_rejects_a_duplicate_request_id()
    {
        var request = CreateRequest();
        var seen = new HashSet<Guid> { request.RequestId };

        var action = () => WorkerRequestValidator.ValidateAndRegister(
            request,
            Nonce,
            Now,
            new HashSet<string>(StringComparer.Ordinal) { request.CapabilityOperationId },
            seen,
            WorkerProtocolLimits.Default);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.DuplicateRequestId);
        seen.Should().ContainSingle();
    }

    /// <summary>注入超過最大巢狀深度的 value tree，證明 codec 在無界遞迴或大型 allocation 前拒絕。</summary>
    [Fact]
    public void SerializeRequest_rejects_excessive_nested_value_depth()
    {
        var limits = new WorkerProtocolLimits(
            maximumFrameBytes: 4096,
            maximumValueDepth: 2,
            maximumObjectMembers: 8,
            maximumArrayItems: 8,
            maximumStringUtf8Bytes: 64,
            maximumIdentifierUtf8Bytes: 64);
        var codec = new WorkerEnvelopeCodec(limits);
        var request = CreateRequest(new Dictionary<string, WorkerValue>
        {
            ["nested"] = WorkerValue.FromArray(
            [
                WorkerValue.FromArray(
                [
                    WorkerValue.FromArray([WorkerValue.FromString("too-deep")])
                ])
            ])
        });

        var action = () => codec.SerializeRequest(request);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.EnvelopeLimitExceeded);
    }

    /// <summary>注入 password 欄位名，證明 sensitive field denylist 不能以合法字串 value 繞過。</summary>
    [Fact]
    public void SerializeRequest_rejects_secret_shaped_parameter_names()
    {
        var codec = new WorkerEnvelopeCodec(WorkerProtocolLimits.Default);
        var request = CreateRequest(new Dictionary<string, WorkerValue>
        {
            ["password"] = WorkerValue.FromString("must-never-enter-ipc")
        });

        var action = () => codec.SerializeRequest(request);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.InvalidEnvelope);
    }

    /// <summary>反射所有 public protocol member，證明沒有 CRM SDK type 或祕密/Session/route-shaped API 重新進入邊界。</summary>
    [Fact]
    public void Public_protocol_contract_exposes_no_sdk_or_secret_transport_member()
    {
        var protocolAssembly = typeof(WorkerRequestV1).Assembly;
        var forbiddenTypeNamespaces = new[]
        {
            "Microsoft.Xrm",
            "Microsoft.Crm",
            "Microsoft.PowerPlatform.Dataverse"
        };
        var forbiddenMemberTerms = new[]
        {
            "password",
            "credential",
            "token",
            "cookie",
            "connectionstring",
            "authorization",
            "endpoint",
            "organizationuri",
            "httpcontext",
            "lineid",
            "session"
        };

        var publicTypes = protocolAssembly.GetExportedTypes();
        var sdkTypeOffenders = publicTypes
            .SelectMany(type => type.GetMembers())
            .SelectMany(member => ReferencedTypes(member))
            .Where(type => forbiddenTypeNamespaces.Any(prefix =>
                (type.Namespace ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .Distinct()
            .ToArray();
        var secretMemberOffenders = publicTypes
            .SelectMany(type => type.GetMembers())
            .Where(member => forbiddenMemberTerms.Any(term =>
                member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(member => $"{member.DeclaringType?.Name}.{member.Name}")
            .Distinct()
            .ToArray();

        sdkTypeOffenders.Should().BeEmpty();
        secretMemberOffenders.Should().BeEmpty();
    }

    private static WorkerRequestV1 CreateRequest(
        IReadOnlyDictionary<string, WorkerValue>? parameters = null)
    {
        return new WorkerRequestV1(
            WorkerProtocolVersion.Current,
            Nonce,
            Guid.NewGuid(),
            "profile-generation-0001",
            "operation-revision-0001",
            "runtime.health.identity",
            Now.AddMinutes(1).UtcTicks,
            parameters ?? new Dictionary<string, WorkerValue>());
    }

    private static WorkerRequestV1 Clone(
        WorkerRequestV1 source,
        int? protocolVersion = null,
        string? processNonce = null,
        long? deadlineUtcTicks = null,
        string? capabilityOperationId = null)
    {
        return new WorkerRequestV1(
            protocolVersion ?? source.ProtocolVersion,
            processNonce ?? source.ProcessNonce,
            source.RequestId,
            source.ProfileGenerationId,
            source.OperationDefinitionRevision,
            capabilityOperationId ?? source.CapabilityOperationId,
            deadlineUtcTicks ?? source.DeadlineUtcTicks,
            source.Parameters);
    }

    private static IEnumerable<Type> ReferencedTypes(System.Reflection.MemberInfo member)
    {
        return member switch
        {
            System.Reflection.PropertyInfo property => new[] { property.PropertyType },
            System.Reflection.FieldInfo field => new[] { field.FieldType },
            System.Reflection.MethodInfo method =>
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType),
            System.Reflection.ConstructorInfo constructor =>
                constructor.GetParameters().Select(parameter => parameter.ParameterType),
            _ => Array.Empty<Type>()
        };
    }
}

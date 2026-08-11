using FluentAssertions;
using SpeechMessage.Dynamics.WorkerProtocol;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Worker process 命令列只包含固定、bounded、非祕密的 bootstrap projection。
/// 故障注入涵蓋祕密/路由形狀 switch 與重複 switch；主要斷言為 parser 在 process/credential 建立前 fail closed，
/// 且 round-trip 結果不含 password、token、connection string 或 endpoint，避免 process-list 洩漏與跨 Profile 身分混用。
/// </summary>
public sealed class WorkerBootstrapArgumentsTests
{
    /// <summary>證明固定六組 switch 可被精確解析成 immutable bootstrap DTO。</summary>
    [Fact]
    public void Parse_accepts_only_the_non_secret_bootstrap_projection()
    {
        var arguments = new[]
        {
            "--pipe", "speechmessage-dynamics-0123456789abcdef",
            "--nonce", "0123456789abcdef0123456789abcdef",
            "--protocol", "1",
            "--worker-kind", "OfficialCrm91Worker",
            "--package-lock", "crm91-xrmtooling-9.1.1.65-core-9.0.2.60",
            "--profile-generation", "profile-generation-0001"
        };

        var parsed = WorkerBootstrapArguments.Parse(arguments);

        parsed.PipeName.Should().Be("speechmessage-dynamics-0123456789abcdef");
        parsed.ProcessNonce.Should().Be("0123456789abcdef0123456789abcdef");
        parsed.ProtocolVersion.Should().Be(WorkerProtocolVersion.Current);
        parsed.WorkerKind.Should().Be(OfficialWorkerKind.OfficialCrm91Worker);
        parsed.PackageLockId.Should().Be("crm91-xrmtooling-9.1.1.65-core-9.0.2.60");
        parsed.ProfileGenerationId.Should().Be("profile-generation-0001");
    }

    /// <summary>注入 password/credential/token/endpoint 等未知 switch，證明祕密與 caller-selected route 無法進入 Worker 命令列。</summary>
    [Theory]
    [InlineData("--password")]
    [InlineData("--credential")]
    [InlineData("--connection-string")]
    [InlineData("--token")]
    [InlineData("--endpoint")]
    public void Parse_rejects_secret_or_route_shaped_switches(string forbiddenSwitch)
    {
        var arguments = ValidArguments().Concat(new[] { forbiddenSwitch, "forbidden" }).ToArray();

        var action = () => WorkerBootstrapArguments.Parse(arguments);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.InvalidEnvelope);
    }

    /// <summary>注入重複 nonce switch，證明 parser 不採用 first/last wins，避免不同 process identity 解讀不一致。</summary>
    [Fact]
    public void Parse_rejects_duplicate_switches()
    {
        var arguments = ValidArguments().Concat(new[] { "--nonce", "duplicate" }).ToArray();

        var action = () => WorkerBootstrapArguments.Parse(arguments);

        action.Should().Throw<WorkerProtocolException>()
            .Which.Category.Should().Be(WorkerProtocolFailureCategory.InvalidEnvelope);
    }

    /// <summary>證明 parse→projection→parse 為等價且 flattened arguments 不包含任何祕密或 endpoint 詞彙。</summary>
    [Fact]
    public void ToArgumentList_round_trips_without_secret_material()
    {
        var parsed = WorkerBootstrapArguments.Parse(ValidArguments());

        var projected = parsed.ToArgumentList();

        WorkerBootstrapArguments.Parse(projected).Should().BeEquivalentTo(parsed);
        var flattened = string.Join(" ", projected);
        flattened.Should().NotContainEquivalentOf("password");
        flattened.Should().NotContainEquivalentOf("credential");
        flattened.Should().NotContainEquivalentOf("token");
        flattened.Should().NotContainEquivalentOf("connectionstring");
        flattened.Should().NotContainEquivalentOf("https://");
    }

    private static string[] ValidArguments()
    {
        return
        [
            "--pipe", "speechmessage-dynamics-0123456789abcdef",
            "--nonce", "0123456789abcdef0123456789abcdef",
            "--protocol", "1",
            "--worker-kind", "OfficialCrm82Worker",
            "--package-lock", "crm82-xrmtooling-8.2.0.5-core-8.2.0.2",
            "--profile-generation", "profile-generation-0001"
        ];
    }
}

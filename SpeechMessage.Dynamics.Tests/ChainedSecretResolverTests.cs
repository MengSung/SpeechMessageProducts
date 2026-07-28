// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ChainedSecretResolverTests.cs
// 目的：確認秘密解析鏈：環境變數優先，字典後備。
// ============================================================================

using FluentAssertions;
using SpeechMessage.Dynamics.WebApi.Runtime;

namespace SpeechMessage.Dynamics.Tests;

public sealed class ChainedSecretResolverTests
{
    [Fact]
    public void Dictionary_resolver_returns_local_dev_bridge_secret()
    {
        var resolver = new DictionarySecretResolver(new Dictionary<string, string>
        {
            ["DYNAMICS_JESUS_PROD_USERNAME"] = @"SPEECHMESSAGE\Administrator"
        });

        resolver.TryResolve("DYNAMICS_JESUS_PROD_USERNAME", out var value).Should().BeTrue();
        value.Should().Be(@"SPEECHMESSAGE\Administrator");
    }

    [Fact]
    public void Chained_resolver_prefers_first_successful_resolver()
    {
        var first = new DictionarySecretResolver(new Dictionary<string, string>
        {
            ["A"] = "from-first"
        });
        var second = new DictionarySecretResolver(new Dictionary<string, string>
        {
            ["A"] = "from-second",
            ["B"] = "only-second"
        });

        var chained = new ChainedSecretResolver(first, second);

        chained.TryResolve("A", out var a).Should().BeTrue();
        a.Should().Be("from-first");

        chained.TryResolve("B", out var b).Should().BeTrue();
        b.Should().Be("only-second");
    }
}
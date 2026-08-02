// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/OfficialWorkerDeploymentOverlayTests.cs
// 目的：驗證 Gateway 只載入執行檔旁、部署流程擁有且不監看檔案變更的官方 Worker overlay。
// ============================================================================

using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Dynamics.Gateway;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 deployment overlay 只能原子覆寫官方 Worker artifact／identity 欄位，不能攜帶秘密、
/// 改寫 Gateway 安全設定、建立檔案 reload owner，或保留對部署檔案的長生命週期相依。
/// </summary>
public sealed class OfficialWorkerDeploymentOverlayTests
{
    /// <summary>
    /// 驗證真實 checked-in placeholder 只被一個固定 provider 覆寫，且刪除來源檔後值不改變。
    /// </summary>
    [Fact]
    public void Deployment_overlay_overrides_checked_in_placeholders_only_with_one_memory_snapshot()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var overlayPath = Path.Combine(
                directory,
                OfficialWorkerDeploymentConfiguration.FileName);
            WriteOverlay(
                overlayPath,
                CreateValidOverlayJson(
                    Path.Combine(
                        directory,
                        "workers",
                        "crm91",
                        "SpeechMessage.Dynamics.Crm91Worker.exe"),
                    "crm91"));

            using var configuration = new ConfigurationManager();
            configuration
                .SetBasePath(Path.Combine(FindRepositoryRoot(), "SpeechMessage.Dynamics.Gateway"))
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsGateway:AuthenticationScheme"] = "Negotiate"
            });
            configuration["DynamicsProfiles:Profiles:crm91:WorkerExecutableSha256"]
                .Should().Be(new string('0', 64));
            configuration["DynamicsProfiles:Profiles:crm91:Admission:ExpectedOrganizationId"]
                .Should().Be("ffffffff-ffff-ffff-ffff-ffffffffffff");
            var sourceCountBefore = ((IConfigurationBuilder)configuration).Sources.Count;
            var providerCountBefore = ((IConfigurationRoot)configuration).Providers.Count();

            var loaded = OfficialWorkerDeploymentConfiguration.TryAddAdjacentOverlay(
                configuration,
                directory);

            loaded.Should().BeTrue();
            configuration["DynamicsProfiles:Profiles:crm91:WorkerProfileGenerationId"]
                .Should().Be("crm91-approved-20260802");
            configuration["DynamicsProfiles:Profiles:crm91:WorkerExecutableSha256"]
                .Should().Be("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF");
            configuration["DynamicsProfiles:Profiles:crm91:Admission:ExpectedOrganizationId"]
                .Should().Be("11111111-2222-3333-4444-555555555555");
            configuration["DynamicsProfiles:Profiles:crm91:WorkerCount"]
                .Should().Be("1", because: "runtime limits remain owned by the base Gateway settings");
            configuration["DynamicsGateway:AuthenticationScheme"]
                .Should().Be("Negotiate", because: "the overlay cannot override Gateway security settings");
            ((IConfigurationBuilder)configuration).Sources.Count
                .Should().Be(sourceCountBefore + 1);
            var providers = ((IConfigurationRoot)configuration).Providers.ToArray();
            providers.Should().HaveCount(providerCountBefore + 1);
            providers[^1].GetType().Name.Should().Be("FixedSnapshotConfigurationProvider");

            File.Delete(overlayPath);
            configuration["DynamicsProfiles:Profiles:crm91:WorkerExecutableSha256"]
                .Should().Be("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
                    because: "startup retains a scalar snapshot, not a watched deployment file");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 驗證 optional overlay 不存在時不新增 provider，也不改寫基底設定。
    /// </summary>
    [Fact]
    public void Missing_deployment_overlay_leaves_base_configuration_unchanged()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsProfiles:Profiles:crm82:WorkerProfileGenerationId"] =
                    "crm82-base"
            });
            var sourceCountBefore = ((IConfigurationBuilder)configuration).Sources.Count;

            var loaded = OfficialWorkerDeploymentConfiguration.TryAddAdjacentOverlay(
                configuration,
                directory);

            loaded.Should().BeFalse();
            configuration["DynamicsProfiles:Profiles:crm82:WorkerProfileGenerationId"]
                .Should().Be("crm82-base");
            ((IConfigurationBuilder)configuration).Sources.Count.Should().Be(sourceCountBefore);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 驗證 overlay 不能把 Gateway authentication 或秘密形狀偷渡進設定圖。
    /// </summary>
    [Fact]
    public void Deployment_overlay_rejects_unknown_gateway_or_secret_fields_before_mutation()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var valid = CreateValidOverlayJson(
                Path.Combine(directory, "SpeechMessage.Dynamics.Crm91Worker.exe"),
                "crm91");
            var secretShape = valid.Replace(
                "\"Admission\":",
                "\"Password\": \"forbidden\", \"Admission\":",
                StringComparison.Ordinal);
            AssertInvalidOverlay(directory, secretShape);
            AssertInvalidOverlay(
                directory,
                "{ \"DynamicsProfiles\": {}, \"DynamicsGateway\": { \"AuthenticationScheme\": \"Injected\" } }");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 驗證大小寫碰撞 alias 與 duplicate JSON property 不會被 Configuration 的不分大小寫鍵值折疊。
    /// </summary>
    [Fact]
    public void Deployment_overlay_rejects_case_colliding_profiles_and_duplicate_properties()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var executablePath = Path.Combine(
                directory,
                "SpeechMessage.Dynamics.Crm91Worker.exe");
            AssertInvalidOverlay(
                directory,
                CreateValidOverlayJson(executablePath, "crm91", "CRM91"));

            var duplicate = CreateValidOverlayJson(executablePath, "crm91").Replace(
                "\"WorkerKind\": \"OfficialCrm91Worker\",",
                "\"WorkerKind\": \"OfficialCrm91Worker\", " +
                "\"WorkerKind\": \"OfficialCrm91Worker\",",
                StringComparison.Ordinal);
            AssertInvalidOverlay(directory, duplicate);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 驗證部署檔只能指向確切絕對 Worker artifact，且所有已知 placeholder identity 都 fail closed。
    /// </summary>
    [Fact]
    public void Deployment_overlay_rejects_relative_executable_path_and_placeholder_identity()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            AssertInvalidOverlay(
                directory,
                CreateValidOverlayJson(
                    "workers\\crm91\\SpeechMessage.Dynamics.Crm91Worker.exe",
                    "crm91"));

            var placeholder = CreateValidOverlayJson(
                    Path.Combine(directory, "SpeechMessage.Dynamics.Crm91Worker.exe"),
                    "crm91")
                .Replace(
                    "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
                    new string('0', 64),
                    StringComparison.Ordinal)
                .Replace(
                    "11111111-2222-3333-4444-555555555555",
                    "ffffffff-ffff-ffff-ffff-ffffffffffff",
                    StringComparison.Ordinal);
            AssertInvalidOverlay(directory, placeholder);

            var repeatedGuidPlaceholder = CreateValidOverlayJson(
                    Path.Combine(directory, "SpeechMessage.Dynamics.Crm91Worker.exe"),
                    "crm91")
                .Replace(
                    "11111111-2222-3333-4444-555555555555",
                    "11111111-1111-1111-1111-111111111111",
                    StringComparison.Ordinal);
            AssertInvalidOverlay(directory, repeatedGuidPlaceholder);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// 驗證每個 ConfigurationManager 都取得自己的 provider 與值，不存在跨 host／跨 session 靜態狀態。
    /// </summary>
    [Fact]
    public void Deployment_overlay_does_not_share_mutable_state_between_configuration_instances()
    {
        var firstDirectory = CreateTemporaryDirectory();
        var secondDirectory = CreateTemporaryDirectory();
        try
        {
            WriteOverlay(
                Path.Combine(firstDirectory, OfficialWorkerDeploymentConfiguration.FileName),
                CreateValidOverlayJson(
                        Path.Combine(firstDirectory, "SpeechMessage.Dynamics.Crm91Worker.exe"),
                        "crm91")
                    .Replace(
                        "crm91-approved-20260802",
                        "crm91-independent-first",
                        StringComparison.Ordinal));
            WriteOverlay(
                Path.Combine(secondDirectory, OfficialWorkerDeploymentConfiguration.FileName),
                CreateValidOverlayJson(
                        Path.Combine(secondDirectory, "SpeechMessage.Dynamics.Crm91Worker.exe"),
                        "crm91")
                    .Replace(
                        "crm91-approved-20260802",
                        "crm91-independent-second",
                        StringComparison.Ordinal));
            using var first = new ConfigurationManager();
            using var second = new ConfigurationManager();

            OfficialWorkerDeploymentConfiguration.TryAddAdjacentOverlay(first, firstDirectory)
                .Should().BeTrue();
            OfficialWorkerDeploymentConfiguration.TryAddAdjacentOverlay(second, secondDirectory)
                .Should().BeTrue();
            first["DynamicsProfiles:Profiles:crm91:WorkerProfileGenerationId"] =
                "first-mutated-after-load";

            second["DynamicsProfiles:Profiles:crm91:WorkerProfileGenerationId"]
                .Should().Be("crm91-independent-second");
            ((IConfigurationRoot)first).Providers.Last()
                .Should().NotBeSameAs(((IConfigurationRoot)second).Providers.Last());
        }
        finally
        {
            Directory.Delete(firstDirectory, recursive: true);
            Directory.Delete(secondDirectory, recursive: true);
        }
    }

    /// <summary>
    /// 驗證 Program 在 profile materialization 前只載入一次相鄰 overlay，避免重複 provider 或錯誤 precedence。
    /// </summary>
    [Fact]
    public void Gateway_program_loads_adjacent_overlay_once_before_profile_materialization()
    {
        var programSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SpeechMessage.Dynamics.Gateway",
            "Program.cs"));
        const string overlayCall =
            "OfficialWorkerDeploymentConfiguration.TryAddAdjacentOverlay(";
        const string materializationCall =
            "var dynamicsProfiles = LoadDynamicsProfileDefinitions(";

        programSource.Split(overlayCall, StringSplitOptions.None)
            .Should().HaveCount(2, because: "the deployment overlay must be added exactly once");
        programSource.IndexOf(overlayCall, StringComparison.Ordinal)
            .Should().BeLessThan(programSource.IndexOf(materializationCall, StringComparison.Ordinal));
    }

    private static void AssertInvalidOverlay(string directory, string json)
    {
        WriteOverlay(
            Path.Combine(directory, OfficialWorkerDeploymentConfiguration.FileName),
            json);
        using var configuration = new ConfigurationManager();
        var sourceCountBefore = ((IConfigurationBuilder)configuration).Sources.Count;

        var action = () => OfficialWorkerDeploymentConfiguration.TryAddAdjacentOverlay(
            configuration,
            directory);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("The official Dynamics worker deployment overlay is invalid.");
        ((IConfigurationBuilder)configuration).Sources.Count.Should().Be(sourceCountBefore);
        configuration.AsEnumerable().Should().BeEmpty();
    }

    private static string CreateValidOverlayJson(
        string workerExecutablePath,
        params string[] aliases)
    {
        var entries = aliases.Select(alias => $$"""
            "{{alias}}": {
              "WorkerProfileGenerationId": "crm91-approved-20260802",
              "WorkerKind": "OfficialCrm91Worker",
              "WorkerExecutablePath": {{JsonSerializer.Serialize(workerExecutablePath)}},
              "WorkerExecutableSha256": "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
              "PackageLockId": "crm91-xrmtooling-9.1.1.65-core-9.0.2.60",
              "OrganizationBaseUri": "https://crm91.example.test/",
              "Admission": {
                "ExpectedOrganizationId": "11111111-2222-3333-4444-555555555555"
              }
            }
            """);
        return $$"""
            {
              "DynamicsProfiles": {
                "Profiles": {
                  {{string.Join(",", entries)}}
                }
              }
            }
            """;
    }

    private static void WriteOverlay(string path, string json) =>
        File.WriteAllText(
            path,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException();
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "speechmessage-dynamics-overlay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

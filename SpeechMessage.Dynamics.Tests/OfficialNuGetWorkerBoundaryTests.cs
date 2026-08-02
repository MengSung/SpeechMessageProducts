using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Execution;
using SpeechMessage.Dynamics.Embedded.DependencyInjection;

namespace SpeechMessage.Dynamics.Tests;

public sealed class OfficialNuGetWorkerBoundaryTests
{
    private const string ControlPlaneProjectName = "SpeechMessage.Dynamics.ControlPlane";

    private static readonly string[] WorkerProjectNames =
    [
        "SpeechMessage.Dynamics.Crm82Worker",
        "SpeechMessage.Dynamics.Crm91Worker"
    ];

    [Fact]
    public void Gateway_and_embedded_do_not_reference_the_legacy_webapi_project()
    {
        var root = FindRepositoryRoot();
        var projectPaths = new[]
        {
            Path.Combine(root, "SpeechMessage.Dynamics.Gateway", "SpeechMessage.Dynamics.Gateway.csproj"),
            Path.Combine(root, "SpeechMessage.Dynamics.Embedded", "SpeechMessage.Dynamics.Embedded.csproj")
        };

        var offenders = projectPaths
            .Where(path => File.ReadAllText(path).Contains(
                "SpeechMessage.Dynamics.WebApi",
                StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        offenders.Should().BeEmpty(
            because: "the supported runtime route is Gateway -> official NuGet worker, with no Web API selector or fallback");
    }

    [Fact]
    public void Gateway_source_and_configuration_contain_no_direct_webapi_route()
    {
        var root = FindRepositoryRoot();
        var productionDirectories = new[]
        {
            Path.Combine(root, "SpeechMessage.Dynamics.Gateway"),
            Path.Combine(root, "SpeechMessage.Dynamics.Embedded")
        };
        var files = productionDirectories
            .SelectMany(directory => Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Where(path => Path.GetExtension(path) is ".cs" or ".json" or ".csproj")
            .ToArray();
        var forbidden = new[]
        {
            "OrganizationWebApiBaseUri",
            "/api/data/",
            "AddSpeechMessageDynamicsWebApi",
            "AddSpeechMessageDynamicsProfiles",
            "SpeechMessage.Dynamics.WebApi",
            "DynamicsWebApiOptions",
            "AdfsOAuth"
        };

        var offenders = files
            .SelectMany(path => forbidden
                .Where(value => File.ReadAllText(path).Contains(value, StringComparison.OrdinalIgnoreCase))
                .Select(value => $"{Path.GetFileName(path)}: {value}"))
            .ToArray();

        offenders.Should().BeEmpty(
            because: "direct Dynamics Web API configuration and routing were retired in favor of official CrmServiceClient workers");
    }

    [Fact]
    public void Gateway_uses_the_neutral_control_plane_and_supervisor_without_loading_worker_projects()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "SpeechMessage.Dynamics.Gateway",
            "SpeechMessage.Dynamics.Gateway.csproj");
        var references = ProjectReferences(XDocument.Load(projectPath))
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();
        var forbiddenReferences = new[]
        {
            "SpeechMessage.Dynamics.WorkerProtocol",
            "SpeechMessage.Dynamics.WorkerHost",
            "SpeechMessage.Dynamics.Crm82Worker",
            "SpeechMessage.Dynamics.Crm91Worker",
            "SpeechMessage.Dynamics.WebApi"
        };

        references.Should().Contain(ControlPlaneProjectName);
        references.Should().Contain("SpeechMessage.Dynamics.WorkerSupervisor");
        references.Intersect(forbiddenReferences, StringComparer.OrdinalIgnoreCase).Should().BeEmpty(
            because: "Gateway owns policy and supervision but must not load worker or legacy transport assemblies");
    }

    [Fact]
    public void Embedded_has_no_legacy_or_in_process_worker_transport_reference()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "SpeechMessage.Dynamics.Embedded",
            "SpeechMessage.Dynamics.Embedded.csproj");
        var references = ProjectReferences(XDocument.Load(projectPath))
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();
        var forbiddenReferences = new[]
        {
            "SpeechMessage.Dynamics.WebApi",
            "SpeechMessage.Dynamics.WorkerProtocol",
            "SpeechMessage.Dynamics.WorkerHost",
            "SpeechMessage.Dynamics.Crm82Worker",
            "SpeechMessage.Dynamics.Crm91Worker"
        };

        references.Intersect(forbiddenReferences, StringComparer.OrdinalIgnoreCase).Should().BeEmpty(
            because: "Embedded is deferred and cannot become an in-process SDK or legacy Web API escape route");
    }

    [Fact]
    public void Embedded_registration_fails_closed_and_directs_callers_to_local_gateway()
    {
        var services = new ServiceCollection();
        var productOptions = new ProductDynamicsOptions
        {
            ExecutionMode = DynamicsExecutionMode.Embedded,
            ProfileAlias = "deferred-embedded",
            Embedded = new EmbeddedModeOptions()
        };

        var action = () => services.AddSpeechMessageDynamicsEmbedded(productOptions);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Local Gateway*");
    }

    [Fact]
    public void Sql_coordinator_test_worker_uses_only_the_neutral_control_plane_capacity_contract()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "SpeechMessage.Dynamics.SqlCoordinatorTestWorker",
            "SpeechMessage.Dynamics.SqlCoordinatorTestWorker.csproj");
        var references = ProjectReferences(XDocument.Load(projectPath))
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        references.Should().ContainSingle().Which.Should().Be(ControlPlaneProjectName,
            because: "cross-process SQL capacity verification must exercise the neutral control plane without retaining the retired WebApi assembly");
    }

    [Fact]
    public void Worker_protocol_is_sdk_free_and_targets_netstandard2_0()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "SpeechMessage.Dynamics.WorkerProtocol",
            "SpeechMessage.Dynamics.WorkerProtocol.csproj");

        File.Exists(projectPath).Should().BeTrue(
            because: "the .NET 10 Gateway and net48 workers require one SDK-free shared IPC contract");

        var project = XDocument.Load(projectPath);
        ProjectTargets(project).Should().ContainSingle().Which.Should().Be("netstandard2.0");
        PackageReferences(project).Should().BeEmpty(
            because: "the protocol must not add a second runtime dependency graph to the isolated net48 workers");
    }

    [Fact]
    public void Ce82_and_ce91_are_separate_net48_workers_with_explicit_official_package_locks()
    {
        var root = FindRepositoryRoot();

        foreach (var workerProjectName in WorkerProjectNames)
        {
            var projectPath = Path.Combine(root, workerProjectName, $"{workerProjectName}.csproj");
            File.Exists(projectPath).Should().BeTrue(
                because: $"{workerProjectName} must isolate its own CE-specific Microsoft SDK graph");

            var project = XDocument.Load(projectPath);
            ProjectTargets(project).Should().ContainSingle().Which.Should().Be("net48");

            var xrmTooling = PackageReferences(project)
                .Where(package => string.Equals(
                    package.Name,
                    "Microsoft.CrmSdk.XrmTooling.CoreAssembly",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            xrmTooling.Should().ContainSingle(
                because: "each worker must use the Microsoft-published CrmServiceClient package");
            xrmTooling[0].Version.Should().NotBeNullOrWhiteSpace(
                because: "floating CRM SDK versions would make the worker binary non-reproducible");

            ProjectReferences(project).Should().NotContain(reference =>
                WorkerProjectNames.Any(other =>
                    !string.Equals(other, workerProjectName, StringComparison.OrdinalIgnoreCase) &&
                    reference.Contains(other, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public void Only_explicit_worker_projects_may_reference_xrm_tooling()
    {
        var root = FindRepositoryRoot();
        var dynamicsProjects = Directory.GetFiles(
                root,
                "SpeechMessage.Dynamics*.csproj",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var offenders = dynamicsProjects
            .Select(path => new
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Project = XDocument.Load(path)
            })
            .Where(item => PackageReferences(item.Project).Any(package => IsCrmSdkPackage(package.Name)))
            .Where(item => !WorkerProjectNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            .Select(item => item.Name)
            .ToArray();

        offenders.Should().BeEmpty(
            because: "products, Gateway, shared contracts, and ordinary tests must stay free of CRM SDK assemblies");
    }

    /// <summary>
    /// 驗證兩個 net48 Worker 都已從暫存 stub 切換為固定 profile 檔、具名管線及
    /// Microsoft <c>CrmServiceClient</c> composition root。這項檢查只讀取原始碼，
    /// 不載入任一版本 SDK 到普通測試程序，也不建立網路、Credential 或跨 Session 狀態。
    /// </summary>
    [Fact]
    public void Official_workers_use_the_local_profile_and_sdk_composition_root_without_webapi_fallback()
    {
        var root = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (var workerProjectName in WorkerProjectNames)
        {
            var workerDirectory = Path.Combine(root, workerProjectName);
            var program = File.ReadAllText(Path.Combine(workerDirectory, "Program.cs"));
            var factoryPath = Path.Combine(workerDirectory, "OfficialCrmServiceClientFactory.cs");
            var adapterPath = Path.Combine(workerDirectory, "OfficialCrmServiceClientAdapter.cs");

            if (program.Contains("worker.bootstrap.not-configured", StringComparison.Ordinal))
            {
                offenders.Add($"{workerProjectName}: bootstrap-stub");
            }

            foreach (var required in new[]
                     {
                         "OfficialWorkerProcessHost",
                         "NamedPipeOfficialWorkerConnector",
                         "AppContext.BaseDirectory",
                         "worker-profile.xml"
                     })
            {
                if (!program.Contains(required, StringComparison.Ordinal))
                {
                    offenders.Add($"{workerProjectName}: missing {required}");
                }
            }

            if (!File.Exists(factoryPath) || !File.Exists(adapterPath))
            {
                offenders.Add($"{workerProjectName}: missing official client composition files");
                continue;
            }

            var implementation = File.ReadAllText(factoryPath) + File.ReadAllText(adapterPath);
            foreach (var required in new[]
                     {
                         "CrmServiceClient",
                         "WhoAmIRequest",
                         "useUniqueInstance: true"
                     })
            {
                if (!implementation.Contains(required, StringComparison.Ordinal))
                {
                    offenders.Add($"{workerProjectName}: missing {required}");
                }
            }

            foreach (var forbidden in new[]
                     {
                         "/api/data/",
                         "DynamicsWebApi",
                         "HttpClient"
                     })
            {
                if (program.Contains(forbidden, StringComparison.OrdinalIgnoreCase) ||
                    implementation.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{workerProjectName}: forbidden {forbidden}");
                }
            }
        }

        offenders.Should().BeEmpty(
            because: "both CE versions must execute only through their pinned official NuGet worker and Organization Service path");
    }

    [Fact]
    public void Official_workers_fail_closed_on_profile_identity_version_and_hidden_retry_contracts()
    {
        var root = FindRepositoryRoot();
        var offenders = new List<string>();

        foreach (var workerProjectName in WorkerProjectNames)
        {
            var workerDirectory = Path.Combine(root, workerProjectName);
            var factory = File.ReadAllText(
                Path.Combine(workerDirectory, "OfficialCrmServiceClientFactory.cs"));
            var adapter = File.ReadAllText(
                Path.Combine(workerDirectory, "OfficialCrmServiceClientAdapter.cs"));

            foreach (var required in new[]
                     {
                         "_expectedOrganizationId",
                         "ConnectedOrgVersion",
                         "OfficialCrmIdentityValidator.IsValid"
                     })
            {
                if (!adapter.Contains(required, StringComparison.Ordinal))
                {
                    offenders.Add($"{workerProjectName}: missing adapter {required}");
                }
            }

            foreach (var required in new[]
                     {
                         "settings.HomeRealm",
                         "credential.Password",
                         "useUniqueInstance: true",
                         "useSsl: settings.UseSsl",
                         "orgDetail: null"
                     })
            {
                if (!factory.Contains(required, StringComparison.Ordinal))
                {
                    offenders.Add($"{workerProjectName}: missing factory {required}");
                }
            }
        }

        var crm91Factory = File.ReadAllText(Path.Combine(
            root,
            "SpeechMessage.Dynamics.Crm91Worker",
            "OfficialCrmServiceClientFactory.cs"));
        foreach (var required in new[]
                 {
                     "MaxRetryCount = 0",
                     "DisableCrossThreadSafeties = false"
                 })
        {
            if (!crm91Factory.Contains(required, StringComparison.Ordinal))
            {
                offenders.Add($"SpeechMessage.Dynamics.Crm91Worker: missing {required}");
            }
        }

        offenders.Should().BeEmpty(
            because: "READY and every WhoAmI must bind to the configured organization/version and SDK retries must not escape the outer deadline");
    }

    private static IReadOnlyCollection<string> ProjectTargets(XDocument project)
    {
        return project
            .Descendants()
            .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .SelectMany(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
    }

    private static IReadOnlyCollection<(string Name, string? Version)> PackageReferences(XDocument project)
    {
        return project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => (
                Name: (string?)element.Attribute("Include") ?? string.Empty,
                Version: (string?)element.Attribute("Version") ??
                    element.Elements().FirstOrDefault(child => child.Name.LocalName == "Version")?.Value))
            .ToArray();
    }

    private static IReadOnlyCollection<string> ProjectReferences(XDocument project)
    {
        return project
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToArray();
    }

    private static bool IsCrmSdkPackage(string packageName)
    {
        return packageName.StartsWith("Microsoft.CrmSdk", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("Microsoft.Xrm", StringComparison.OrdinalIgnoreCase) ||
            packageName.StartsWith("Microsoft.PowerPlatform.Dataverse", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ||
        path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

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

        throw new InvalidOperationException(
            "Could not locate SpeechMessageProducts.sln from the test base directory.");
    }
}

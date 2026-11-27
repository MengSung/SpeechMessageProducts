/*
 * 檔案整體說明
 *
 * 目的與定位：保護 F3 平台 Plugin 的精確註冊格式與 Deployment 匯出身分，
 * 以真正版控清單與合成故障案例共同驗證，不變更既有 Plugin／Workflow 的設定契約。
 * 主要流程：以 operation-local 純 DTO 建立舊兩組件加平台組件的三組件文件，
 * 驗證平台兩個 IPlugin 入口對應的三個註冊步驟、精確 Update 篩選／映像與三組織產品設定；
 * 再以 Loader 固定路徑讀取臨時 JSON，並測試 DeploymentEngine.WriteExport。
 * 真公開命令以當案產生的 net462 PE 驗三組織 plan／apply／verify／export／rollback；
 * 好牧人保留三組件，好管家／好學會只提供平台 DLL，故誤載教會組件必使測試失敗。
 * 故障注入與決定性斷言：注入第四組件、額外或相似入口、其他產品鍵／錯誤映射、
 * 重複 JSON 欄位、錯誤事件階段／模式、Update filter／image 錯配與 secure config，
 * 必須拒絕；舊組件不得接受新設定。Plan／Apply 拒絕 legacy 組織，匯出與回復只允許
 * 精確平台清單身分，未知／重複回復資料必須在任何 SDK 寫入前失敗。
 * 資源與隔離：測試只用合成 DTO、記憶體 SDK 與每案例唯一暫存目錄，不連 D365、
 * LINE 或正式環境；Fixture 唯一擁有目錄並在 Dispose 刪除，沒有跨案例快取或背景工作。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Tests.Organizations;
using SpeechMessage.Dynamics.Deployment;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以離線候選文件檢查 F3 精確註冊邊界，不將合成結果當成實機註冊證據。</summary>
[Collection("Deployment working directory")]
public sealed class F3PlatformRegistrationContractTests
{
    private const string PlatformAssembly = "SpeechMessage.Dynamics.Platform.Plugins";
    private const string SlotRequestType = "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.SlotRequestPlugin";
    private const string ChannelCreatedType = "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.ChannelCreatedPlugin";
    private const string ProductMap =
        "{\"products\":{\"goodshepherd20\":\"GoodShepherd2\",\"construction\":\"GoodButler\",\"association\":\"GoodGuild\"}}";
    private const string PlatformOrganization = "goodshepherd20";

    /// <summary>
    /// 真 CLI 消費完整三組件來源，但在載入 DLL 前依組織選取；每個案例建立自己的 PE、
    /// SDK 與工作目錄，依序匯出基線、規劃、套用、驗證與回復。其他產品不提供教會 DLL，
    /// 決定性斷言包含實際組件／步驟數、回復後零受管列與每命令租約歸還，沒有實機依賴。
    /// </summary>
    [Theory]
    [InlineData("goodshepherd20", 3)]
    [InlineData("construction", 1)]
    [InlineData("association", 1)]
    public void Cli_OrganizationScope_FullLifecycleAndRollback(string organization, int count)
    {
        using var fixture = new Fixture();
        using var repository = fixture.EnterCliRepository();
        var document = new RegistrationDocument
        {
            Assemblies = new[] { LegacyPluginManifest(), WorkflowManifest(), PlatformManifest() }
        };
        File.WriteAllText(Path.Combine(fixture.Root, "d365", "registration", "plugins.registration.json"),
            JsonSerializer.Serialize(document, RegistrationManifestLoader.Json));
        var plugins = Path.Combine(fixture.Root, "artifacts", "plugins");
        Directory.CreateDirectory(plugins);
        EmitAssembly(plugins, PlatformAssembly,
            "namespace Microsoft.Xrm.Sdk { public interface IPlugin {} } " +
            "namespace SpeechMessage.Dynamics.Platform.Plugins.ChannelPool { " +
            "public sealed class SlotRequestPlugin : Microsoft.Xrm.Sdk.IPlugin {} " +
            "public sealed class ChannelCreatedPlugin : Microsoft.Xrm.Sdk.IPlugin {} }");
        if (organization == PlatformOrganization)
        {
            File.Copy(fixture.Emit(), Path.Combine(plugins, fixture.Image.Name + ".dll"));
            EmitAssembly(plugins, "SpeechMessage.Dynamics.Workflows",
                "namespace System.Activities { public abstract class CodeActivity {} } " +
                "namespace SpeechMessage.Dynamics.Workflows.Attendance { " +
                "public sealed class SampleActivity : System.Activities.CodeActivity {} }");
        }
        SeedPlatformMessages(fixture.Service);
        var connector = new ScopedConnector(fixture.Service, organization);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var failures = new List<Exception>();
        var artifacts = Path.Combine(fixture.Root, "cli");
        int Run(string command, params string[] options)
        {
            var arguments = new[] { command, "--org", organization }.AsEnumerable();
            if (command is "plan" or "apply" or "verify") arguments = arguments.Append("--registration");
            return DeploymentCli.Run(arguments.Concat(options).ToArray(),
                connector, output, failures.Add, artifacts, _ => Fixture.SecretValue);
        }
        string Find(string name) => Assert.Single(Directory.GetFiles(artifacts, name, SearchOption.AllDirectories));

        Assert.Equal(1, Run("verify"));
        Assert.Equal(0, Run("export"));
        var backup = Find(count == 3 ? "backup.json" : "export.json");
        Assert.Equal(0, Run("plan"));
        var plan = Find("plan.json");
        if (count == 3)
            Assert.Equal(3, JsonSerializer.Deserialize<DocumentDeploymentPlan>(File.ReadAllText(plan))!.Assemblies.Count);
        Assert.True(Run("apply", "--plan", plan) == 0, string.Join("\n", failures));
        var assemblies = fixture.Service.Rows.Where(row => row.LogicalName == "pluginassembly").ToArray();
        Assert.Equal(count, assemblies.Length);
        Assert.Contains(assemblies, row => row.GetAttributeValue<string>("name") == PlatformAssembly);
        Assert.Equal(3, fixture.Service.Rows.Count(row => row.LogicalName == "sdkmessageprocessingstep"));
        Assert.Equal(0, Run("verify"));
        Assert.Equal(0, Run("rollback", "--from", backup));
        Assert.DoesNotContain(fixture.Service.Rows,
            row => row.LogicalName is "pluginassembly" or "plugintype" or "sdkmessageprocessingstep" or "sdkmessageprocessingstepimage");
        Assert.Empty(failures);
        Assert.Equal(6, connector.Calls);
        Assert.Equal(connector.Calls, connector.Returned);
    }

    /// <summary>
    /// 好牧人的完整三組件須全部進入同一份部署計畫；以小型合成映像與正式訊息／篩選
    /// 中繼資料驗證文件入口，決定性斷言為三組件皆有計畫且尚無任何 SDK 寫入。
    /// </summary>
    [Fact]
    public void Document_GoodShepherd_AllThreeAssembliesArePlanned()
    {
        using var fixture = new Fixture();
        var document = new RegistrationDocument
        {
            Assemblies = new[] { LegacyPluginManifest(), WorkflowManifest(), PlatformManifest() }
        };
        SeedPlatformMessages(fixture.Service);
        var images = new[]
        {
            fixture.Image,
            fixture.Image with
            {
                Name = "SpeechMessage.Dynamics.Workflows", PluginTypes = Array.Empty<string>(),
                ActivityTypes = new[] { "SpeechMessage.Dynamics.Workflows.Attendance.SampleActivity" }
            },
            PlatformImage()
        };
        var engine = new DocumentDeploymentEngine(new PlatformRegistrationService(fixture.Service, PlatformOrganization),
            fixture.Root, fixture.Clock, _ => Fixture.SecretValue);
        var plan = engine.Plan(document, images, PlatformOrganization);
        Assert.Equal(3, plan.Assemblies.Count);
        Assert.Contains(plan.Assemblies, item => item.AssemblyName == PlatformAssembly);
        Assert.Empty(fixture.Service.Writes);
    }

    /// <summary>
    /// 好管家與好學會只能部署平台組件；直接呼叫單組件核心亦不得繞過 CLI 分流。
    /// 以合法教會組件及映像注入跨產品候選，要求 Plan 與匯出均拒絕，且不產生檔案或寫入。
    /// </summary>
    [Theory]
    [InlineData("construction")]
    [InlineData("association")]
    public void LegacyAssembly_OtherProductIsRejectedByCore(string organization)
    {
        using var fixture = new Fixture();
        Assert.Throws<InvalidDataException>(() => fixture.Engine.Plan(fixture.Manifest, fixture.Image, organization));
        Assert.Throws<InvalidDataException>(() => fixture.Engine.WriteExport(organization,
            Path.Combine(fixture.Root, "forbidden.json"), fixture.Image.Name));
        Assert.Empty(fixture.Service.Writes);
        Assert.Empty(Directory.GetFiles(fixture.Root));
    }

    /// <summary>
    /// 完整三組件來源按精確組織投影；跨產品組件必須在載入 DLL 前排除，
    /// 但被排除組件若有非法身分仍須拒絕整份來源，不可由過濾掩蓋。
    /// </summary>
    [Theory]
    [InlineData("goodshepherdback", 2)]
    [InlineData("goodshepherd20", 3)]
    [InlineData("construction", 1)]
    [InlineData("association", 1)]
    public void LoadForOrganization_SelectsExactAssemblyScope_WithoutHidingInvalidSource(string organization, int count)
    {
        using var fixture = new RepositoryFixture();
        var document = new RegistrationDocument
        {
            Assemblies = new[] { LegacyPluginManifest(), WorkflowManifest(), PlatformManifest() }
        };
        fixture.Write(document);
        var selected = RegistrationManifestLoader.LoadForOrganization(fixture.Root, organization);
        Assert.Equal(count, selected.Assemblies.Count);
        Assert.All(selected.Assemblies, assembly => Assert.True(
            RegistrationManifestLoader.IsAssemblyAllowed(assembly.AssemblyName, organization)));
        if (organization is "construction" or "association")
            Assert.Equal(PlatformAssembly, Assert.Single(selected.Assemblies).AssemblyName);
        fixture.Write(document with
        {
            Assemblies = new[] { LegacyPluginManifest() with { AssemblyName = "Unapproved" }, WorkflowManifest(), PlatformManifest() }
        });
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.LoadForOrganization(fixture.Root, organization));
    }

    /// <summary>舊模組切換只取得教會兩組件；其他產品沒有適用組件時拒絕，不能切換平台名額池。</summary>
    [Fact]
    public void LoadForOrganization_LegacyScopeExcludesPlatform()
    {
        using var fixture = new RepositoryFixture();
        fixture.Write(new RegistrationDocument
        {
            Assemblies = new[] { LegacyPluginManifest(), WorkflowManifest(), PlatformManifest() }
        });
        Assert.Equal(2, RegistrationManifestLoader.LoadForOrganization(fixture.Root, "goodshepherd20", legacyOnly: true).Assemblies.Count);
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.LoadForOrganization(fixture.Root, "construction", legacyOnly: true));
    }

    /// <summary>F3 只接受兩個核定 Plugin 型別、三筆註冊步驟及三產品完整映射。</summary>
    [Fact]
    public void PlatformManifest_ExactTypesAndProductMap_AreAccepted()
    {
        var manifest = PlatformManifest();
        RegistrationManifestLoader.Validate(manifest);
        Assert.Equal(3, manifest.Steps.Count);
        Assert.Equal(2, manifest.Steps.Select(step => step.Type).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("new_product", Assert.Single(manifest.Steps[2].FilteringAttributes));
        var image = Assert.Single(manifest.Steps[2].Images);
        Assert.Equal("ProductBefore", image.Name);
        Assert.Equal("PreImage", image.Type);
        Assert.Equal("new_product", Assert.Single(image.Attributes));
    }

    /// <summary>
    /// 讀取真正版控 manifest 而非合成 DTO，保護三筆註冊與兩個完整型別一致。
    /// Update 型別缺少 ChannelPool 命名空間時 loader 必須使此案失敗，不能讓合成測試掩蓋實際部署拒絕。
    /// 只從目前測試組件向上讀取本工作樹；沒有 SDK、秘密、外部連線或可變共用狀態。
    /// </summary>
    [Fact]
    public void PlatformManifest_RepositoryDocumentMatchesExactEntryPoints()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var document = RegistrationManifestLoader.LoadDocument(directory.FullName);
        var platform = Assert.Single(document.Assemblies, item => item.AssemblyName == PlatformAssembly);
        Assert.Equal(3, platform.Steps.Count);
        Assert.Equal(SlotRequestType, Assert.Single(platform.Steps, item => item.Entity == "new_slotrequest").Type);
        var channelSteps = platform.Steps.Where(item => item.Entity == "new_linechannel").ToArray();
        Assert.Equal(2, channelSteps.Length);
        Assert.All(channelSteps, item => Assert.Equal(ChannelCreatedType, item.Type));
        Assert.All(platform.Steps, item =>
        {
            Assert.Equal(ProductMap, item.UnsecureConfig);
            Assert.Equal(3, item.EnabledByOrganization.Count);
            Assert.All(item.EnabledByOrganization.Values, Assert.True);
        });
    }

    /// <summary>新組件必須精確承載兩個核定 IPlugin 入口，漏列、多列或相似名稱皆 fail closed。</summary>
    [Theory]
    [InlineData("missing-slot-request")]
    [InlineData("missing-channel-created")]
    [InlineData("extra-type")]
    [InlineData("lookalike-type")]
    public void PlatformManifest_EntryPointSetMustBeExact(string fault)
    {
        var manifest = PlatformManifest();
        var steps = manifest.Steps.ToList();
        if (fault == "missing-slot-request")
            steps.RemoveAll(step => step.Type == SlotRequestType);
        else if (fault == "missing-channel-created")
            steps.RemoveAll(step => step.Type == ChannelCreatedType);
        else
        {
            steps.Add(steps[0] with
            {
                Type = fault == "lookalike-type" ? SlotRequestType + "Other" :
                    "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.ExtraPlugin",
                Rank = 3
            });
        }

        Assert.Throws<InvalidDataException>(() =>
            RegistrationManifestLoader.Validate(manifest with { Steps = steps }));
    }

    /// <summary>產品 map 必須是精確完整三鍵；未知、缺漏、錯值、重複鍵及舊設定語法均拒絕。</summary>
    [Theory]
    [InlineData("unknown-product")]
    [InlineData("missing-product")]
    [InlineData("wrong-product")]
    [InlineData("duplicate-property")]
    [InlineData("legacy-mode")]
    [InlineData("secure-config")]
    public void PlatformManifest_InvalidConfiguration_IsRejected(string fault)
    {
        var config = fault switch
        {
            "unknown-product" =>
                "{\"products\":{\"goodshepherd20\":\"GoodShepherd2\",\"construction\":\"GoodButler\",\"association\":\"GoodGuild\",\"extra\":\"Other\"}}",
            "missing-product" =>
                "{\"products\":{\"goodshepherd20\":\"GoodShepherd2\",\"construction\":\"GoodButler\"}}",
            "wrong-product" =>
                "{\"products\":{\"goodshepherd20\":\"GoodShepherd\",\"construction\":\"GoodButler\",\"association\":\"GoodGuild\"}}",
            "duplicate-property" =>
                "{\"products\":{\"goodshepherd20\":\"GoodShepherd2\",\"construction\":\"GoodButler\",\"association\":\"GoodGuild\"},\"products\":{\"goodshepherd20\":\"GoodShepherd2\",\"construction\":\"GoodButler\",\"association\":\"GoodGuild\"}}",
            "legacy-mode" => "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}",
            _ => ProductMap
        };
        var manifest = PlatformManifest();
        var steps = manifest.Steps.Select(step => step with
        {
            UnsecureConfig = config,
            SecureConfig = fault == "secure-config" ? "${secret:TokenKey}" : ""
        }).ToArray();

        Assert.Throws<InvalidDataException>(() =>
            RegistrationManifestLoader.Validate(manifest with { Steps = steps }));
    }

    /// <summary>平台步驟只接受核定模組、訊息、實體、階段及同步模式；任一事件欄位偏移即拒絕。</summary>
    [Theory]
    [InlineData("wrong-module")]
    [InlineData("wrong-message")]
    [InlineData("wrong-entity")]
    [InlineData("wrong-stage")]
    [InlineData("wrong-mode")]
    [InlineData("wrong-update-stage")]
    [InlineData("wrong-update-mode")]
    [InlineData("wrong-update-entity")]
    [InlineData("wrong-update-message")]
    public void PlatformManifest_StepEventContractMustBeExact(string fault)
    {
        var manifest = PlatformManifest();
        var steps = manifest.Steps.ToArray();
        var updateFault = fault.StartsWith("wrong-update-", StringComparison.Ordinal);
        var index = updateFault ? 2 : 0;
        steps[index] = updateFault
            ? fault switch
            {
                "wrong-update-stage" => steps[index] with { Stage = "PostOperation" },
                "wrong-update-mode" => steps[index] with { Mode = "Asynchronous" },
                "wrong-update-message" => steps[index] with { Message = "Create" },
                _ => steps[index] with { Entity = "account" }
            }
            : fault switch
        {
            "wrong-module" => steps[0] with { Module = "diagnostics" },
            "wrong-message" => steps[0] with { Message = "Update" },
            "wrong-entity" => steps[0] with { Entity = "account" },
            "wrong-stage" => steps[0] with { Stage = "PostOperation" },
            _ => steps[0] with { Mode = "Asynchronous" }
        };

        Assert.Throws<InvalidDataException>(() =>
            RegistrationManifestLoader.Validate(manifest with { Steps = steps }));
    }

    /// <summary>三個指定測試組織必須逐一明確啟用，不能缺漏、關閉或額外授權複製來源組織。</summary>
    [Theory]
    [InlineData("missing-organization")]
    [InlineData("disabled-organization")]
    [InlineData("extra-organization")]
    public void PlatformManifest_OrganizationEnablementMustBeExact(string fault)
    {
        var manifest = PlatformManifest();
        var enabled = new Dictionary<string, bool>(manifest.Steps[0].EnabledByOrganization, StringComparer.Ordinal);
        if (fault == "missing-organization") enabled.Remove("association");
        else if (fault == "disabled-organization") enabled["construction"] = false;
        else enabled["goodshepherdback"] = true;
        var steps = manifest.Steps.Select(step => step with { EnabledByOrganization = enabled }).ToArray();

        Assert.Throws<InvalidDataException>(() =>
            RegistrationManifestLoader.Validate(manifest with { Steps = steps }));
    }

    /// <summary>
    /// Update 不可只靠通用「非空 filter」規則：只接受 new_product，並固定一個 ProductBefore
    /// PreImage 且 attributes 僅 new_product；對每個錯配各自注入並要求 loader 拒絕。
    /// </summary>
    [Theory]
    [InlineData("missing-filter")]
    [InlineData("extra-filter")]
    [InlineData("wrong-filter")]
    [InlineData("missing-image")]
    [InlineData("extra-image")]
    [InlineData("wrong-image-name")]
    [InlineData("wrong-image-type")]
    [InlineData("extra-image-attribute")]
    [InlineData("wrong-image-attribute")]
    public void PlatformManifest_UpdateStepFilterAndImageMustBeExact(string fault)
    {
        var manifest = PlatformManifest();
        var update = manifest.Steps[2];
        var replacement = fault switch
        {
            "missing-filter" => update with { FilteringAttributes = Array.Empty<string>() },
            "extra-filter" => update with { FilteringAttributes = new[] { "new_product", "new_name" } },
            "wrong-filter" => update with { FilteringAttributes = new[] { "new_name" } },
            "missing-image" => update with { Images = Array.Empty<RegistrationImage>() },
            "extra-image" => update with
            {
                Images = update.Images.Concat(new[]
                {
                    new RegistrationImage
                    {
                        Name = "Other",
                        Type = "PreImage",
                        Attributes = new List<string> { "new_product" }
                    }
                }).ToArray()
            },
            "wrong-image-name" => update with
            {
                Images = new[] { update.Images[0] with { Name = "Before" } }
            },
            "wrong-image-type" => update with
            {
                Images = new[] { update.Images[0] with { Type = "PostImage" } }
            },
            "extra-image-attribute" => update with
            {
                Images = new[] { update.Images[0] with { Attributes = new[] { "new_product", "new_name" } } }
            },
            _ => update with
            {
                Images = new[] { update.Images[0] with { Attributes = new[] { "new_name" } } }
            }
        };

        var steps = manifest.Steps.ToArray();
        steps[2] = replacement;
        Assert.Throws<InvalidDataException>(() =>
            RegistrationManifestLoader.Validate(manifest with { Steps = steps }));
    }

    /// <summary>
    /// 平台步驟是精確清單而非一般模組預算：缺少 Update、事件重複、Create 帶影像、
    /// 調整穩定順位或注入舊步驟 ID 均必須在部署前拒絕。
    /// </summary>
    [Theory]
    [InlineData("missing-update")]
    [InlineData("duplicate-event")]
    [InlineData("create-image")]
    [InlineData("wrong-rank")]
    [InlineData("legacy-reference")]
    public void PlatformManifest_OnlyExactF3StepSetIsAccepted(string fault)
    {
        var manifest = PlatformManifest();
        var steps = manifest.Steps.ToList();
        switch (fault)
        {
            case "missing-update":
                steps.RemoveAt(2);
                break;
            case "duplicate-event":
                steps.Add(steps[2] with { Rank = 2 });
                break;
            case "create-image":
                steps[1] = steps[1] with
                {
                    Images = new[] { new RegistrationImage { Name = "Extra", Type = "PostImage", Attributes = new[] { "new_product" } } }
                };
                break;
            case "wrong-rank":
                steps[2] = steps[2] with { Rank = 2 };
                break;
            default:
                steps[0] = steps[0] with
                {
                    Legacy = new[]
                    {
                        new LegacyStepReference
                        {
                            Assembly = "SpeechMessage.Dynamics.Plugins",
                            Type = "SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin",
                            StepId = Guid.NewGuid().ToString("D")
                        }
                    }
                };
                break;
        }

        Assert.Throws<InvalidDataException>(() =>
            RegistrationManifestLoader.Validate(manifest with { Steps = steps }));
    }

    /// <summary>平台模組及產品設定只能搭配精確平台組件身分，相似名稱與舊組件皆不得取得新例外。</summary>
    [Theory]
    [InlineData("SpeechMessage.Dynamics.Platform.Plugins.Other")]
    [InlineData("SpeechMessage.Dynamics.Platform.Plugins2")]
    public void PlatformManifest_LookalikeAssemblyIdentityIsRejected(string assemblyName)
    {
        var manifest = PlatformManifest() with { AssemblyName = assemblyName };

        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.Validate(manifest));
    }

    /// <summary>舊 Plugin／Workflow 白名單不因平台模組擴充而放寬；只有精確平台組件可使用 platform 模組。</summary>
    [Fact]
    public void PlatformModule_OtherAssemblyIsRejected()
    {
        var legacy = LegacyPluginManifest();
        var changed = legacy with
        {
            Steps = new[] { legacy.Steps[0] with { Module = "platform" } }
        };

        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.Validate(changed));

        var workflow = WorkflowManifest();
        workflow = workflow with
        {
            Activities = workflow.Activities.Select(activity => activity with { Module = "platform" }).ToArray()
        };
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.Validate(workflow));
    }

    /// <summary>平台專用設定不得延伸至舊 Plugins，既有 diagnostics mode 與清單仍可正常驗證。</summary>
    [Fact]
    public void PlatformProductMap_OtherAssemblyIsRejected_AndLegacyConfigurationRemainsValid()
    {
        var legacy = LegacyPluginManifest();
        RegistrationManifestLoader.Validate(legacy);
        var changed = legacy with
        {
            Steps = new[] { legacy.Steps[0] with { UnsecureConfig = ProductMap } }
        };

        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.Validate(changed));
    }

    /// <summary>完整舊兩組件加 F3 平台組件文件可由 Loader 讀取，第四組件不得擴張已核定上限。</summary>
    [Fact]
    public void LoadDocument_ThreeSupportedAssembliesAccepted_FourthRejected()
    {
        using var fixture = new RepositoryFixture();
        var document = new RegistrationDocument
        {
            Assemblies = new[]
            {
                LegacyPluginManifest(),
                WorkflowManifest(),
                PlatformManifest()
            }
        };
        fixture.Write(document);

        var loaded = RegistrationManifestLoader.LoadDocument(fixture.Root);

        Assert.Equal(3, loaded.Assemblies.Count);
        Assert.Contains(loaded.Assemblies, item => item.AssemblyName == PlatformAssembly);
        fixture.Write(document with
        {
            Assemblies = document.Assemblies.Concat(new[] { WorkflowManifest() }).ToArray()
        });
        Assert.Throws<InvalidDataException>(() => RegistrationManifestLoader.LoadDocument(fixture.Root));
    }

    /// <summary>WriteExport 接受精確平台組件名並保存該管理範圍，但拒絕前綴或後綴近似名稱。</summary>
    [Fact]
    public void WriteExport_PlatformAssemblyIdentity_IsExact()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "platform-export.json");

        fixture.Engine.WriteExport(PlatformOrganization, path, PlatformAssembly);

        var exported = JsonSerializer.Deserialize<DeploymentExport>(File.ReadAllText(path));
        Assert.NotNull(exported);
        Assert.Equal(PlatformAssembly, exported!.ManagedAssemblyName);
        foreach (var lookalike in new[]
                 {
                     "SpeechMessage.Dynamics.Platform.Plugins.Other",
                     "SpeechMessage.Dynamics.Platform.Plugins2"
                 })
        {
            Assert.Throws<InvalidDataException>(() =>
                fixture.Engine.WriteExport(PlatformOrganization, path, lookalike));
        }
    }

    /// <summary>
    /// Platform Plugin target organization is an explicit authorization boundary: Plan and Apply
    /// both reject the legacy organization before metadata calls or writes, regardless of the
    /// old missing-enable compatibility behavior.
    /// </summary>
    [Fact]
    public void PlatformPlanAndApply_LegacyOrganizationRefusedBeforeWrites()
    {
        using var fixture = new Fixture();
        var manifest = PlatformManifest();
        var image = PlatformImage();

        Assert.Throws<InvalidDataException>(() =>
            fixture.Engine.Plan(manifest, image, Fixture.Organization));
        var unvalidatedPlan = new DeploymentPlan(
            Fixture.Organization,
            DateTimeOffset.UnixEpoch,
            string.Empty,
            string.Empty,
            string.Empty,
            Array.Empty<DeploymentOperation>(),
            string.Empty);
        Assert.Throws<InvalidDataException>(() =>
            fixture.Engine.Apply(unvalidatedPlan, manifest, image));
        Assert.Empty(fixture.Service.Writes);
    }

    /// <summary>
    /// Hash 是可重算完整性摘要而非簽章，所以回復仍須驗證 export 的實際列身份。
    /// 注入 lookalike 組件、未知／重複 Plugin type、未知／重複步驟後，斷言在首筆 SDK
    /// 寫入前 fail closed；合成服務只記錄操作種類，不連 D365。
    /// </summary>
    [Theory]
    [InlineData("lookalike-assembly")]
    [InlineData("unknown-type")]
    [InlineData("duplicate-type")]
    [InlineData("unknown-step")]
    [InlineData("duplicate-step")]
    public void PlatformRollback_UnknownOrDuplicateIdentityIsRefusedBeforeWrites(string fault)
    {
        using var fixture = new Fixture();
        var typeId = Guid.NewGuid();
        var types = Array.Empty<PluginTypeExport>();
        var steps = Array.Empty<PluginStepExport>();
        if (fault == "unknown-type")
        {
            types = new[] { new PluginTypeExport(typeId, "Unknown", "F3.Unapproved.Plugin") };
        }
        else if (fault == "duplicate-type")
        {
            types = new[]
            {
                new PluginTypeExport(typeId, "ChannelCreatedPlugin", ChannelCreatedType),
                new PluginTypeExport(Guid.NewGuid(), "ChannelCreatedPluginCopy", ChannelCreatedType)
            };
        }
        else if (fault == "unknown-step")
        {
            types = new[] { new PluginTypeExport(typeId, "ChannelCreatedPlugin", ChannelCreatedType) };
            steps = new[]
            {
                new PluginStepExport(Guid.NewGuid(), "F3.Unapproved.Plugin", "Create", "new_linechannel", 40, 0, 1,
                    true, false, true)
            };
        }
        else if (fault == "duplicate-step")
        {
            types = new[] { new PluginTypeExport(typeId, "ChannelCreatedPlugin", ChannelCreatedType) };
            var step = new PluginStepExport(Guid.NewGuid(), ChannelCreatedType, "Update", "new_linechannel", 20, 0, 1,
                true, false, true);
            steps = new[] { step, step with { Id = Guid.NewGuid() } };
        }

        var export = new DeploymentExport(2, PlatformOrganization, DateTimeOffset.UnixEpoch, "fingerprint",
            null, types, steps, string.Empty)
        {
            ManagedAssemblyName = fault == "lookalike-assembly" ? PlatformAssembly + ".Other" : PlatformAssembly
        };
        var path = WriteExportFixture(fixture.Root, export);

        Assert.Throws<InvalidDataException>(() =>
            fixture.Engine.Rollback(path, PlatformManifest(), PlatformOrganization));
        Assert.Empty(fixture.Service.Writes);
    }

    /// <summary>組成具有真實 Loader 規則的最小診斷組件 manifest，保留舊 mode 範圍。</summary>
    private static RegistrationManifest LegacyPluginManifest() => new()
    {
        AssemblyName = "SpeechMessage.Dynamics.Plugins",
        Steps = new[]
        {
            new StepRegistration
            {
                Module = "diagnostics",
                Type = "SpeechMessage.Dynamics.Plugins.Diagnostics.DiagnosticsPingPlugin",
                Message = "Create",
                Entity = "task",
                Stage = "PostOperation",
                Mode = "Asynchronous",
                Rank = 1,
                UnsecureConfig = "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}"
            }
        }
    };

    /// <summary>組成一筆完整但未驗收的 Workflow 候選，不借用活動設定或 Plugin config。</summary>
    private static RegistrationManifest WorkflowManifest() => new()
    {
        AssemblyName = "SpeechMessage.Dynamics.Workflows",
        ActivityGroup = "SpeechMessage",
        Activities = new[]
        {
            new ActivityRegistration
            {
                Module = "attendance",
                Type = "SpeechMessage.Dynamics.Workflows.Attendance.SampleActivity",
                DisplayName = "合成活動",
                Live = new LiveRegistration()
            }
        }
    };

    /// <summary>建立 F3 的兩個精確 IPlugin 型別及三筆固定註冊步驟；映射不接受 caller 產品值。</summary>
    private static RegistrationManifest PlatformManifest()
    {
        var enabled = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["goodshepherd20"] = true,
            ["construction"] = true,
            ["association"] = true
        };
        var live = new StepLiveRegistration { Tier = "live", TokenKey = false };
        return new RegistrationManifest
        {
            AssemblyName = PlatformAssembly,
            Steps = new[]
            {
                new StepRegistration
                {
                    Module = "platform",
                    Type = SlotRequestType,
                    Message = "Create",
                    Entity = "new_slotrequest",
                    Stage = "PreOperation",
                    Mode = "Synchronous",
                    Rank = 1,
                    UnsecureConfig = ProductMap,
                    EnabledByOrganization = enabled,
                    Live = live
                },
                new StepRegistration
                {
                    Module = "platform",
                    Type = ChannelCreatedType,
                    Message = "Create",
                    Entity = "new_linechannel",
                    Stage = "PostOperation",
                    Mode = "Synchronous",
                    Rank = 1,
                    UnsecureConfig = ProductMap,
                    EnabledByOrganization = enabled,
                    Live = live
                },
                new StepRegistration
                {
                    Module = "platform",
                    Type = ChannelCreatedType,
                    Message = "Update",
                    Entity = "new_linechannel",
                    Stage = "PreOperation",
                    Mode = "Synchronous",
                    Rank = 1,
                    FilteringAttributes = new[] { "new_product" },
                    Images = new[]
                    {
                        new RegistrationImage
                        {
                            Name = "ProductBefore",
                            Type = "PreImage",
                            Attributes = new[] { "new_product" }
                        }
                    },
                    UnsecureConfig = ProductMap,
                    EnabledByOrganization = enabled,
                    Live = live
                }
            }
        };
    }

    /// <summary>合成與三筆核定步驟型別集合相同的 DLL 摘要，不載入或執行任何 Plugin。</summary>
    private static AssemblyImage PlatformImage() => new(
        PlatformAssembly,
        "1.0.0.0",
        "0123456789abcdef",
        "AQID",
        Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })),
        new[] { SlotRequestType, ChannelCreatedType });

    /// <summary>在案例私有目錄建立完整 hash 的本機 export，專供回復身份故障注入。</summary>
    private static string WriteExportFixture(string root, DeploymentExport source)
    {
        var unsigned = source with { Sha256 = string.Empty };
        var canonical = JsonSerializer.Serialize(unsigned);
        var sealedExport = unsigned with
        {
            Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
        };
        var path = Path.Combine(root, "platform-rollback-export.json");
        File.WriteAllText(path, JsonSerializer.Serialize(sealedExport));
        return path;
    }

    /// <summary>本案專用 net462 合成 PE；只由 metadata reader 讀取，不執行其類別或使用正式簽署金鑰。</summary>
    private static void EmitAssembly(string directory, string name, string body)
    {
        var source = "[assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.6.2\")] " + body;
        var compilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll")) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(Path.Combine(directory, name + ".dll"));
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(item => item.ToString())));
    }

    /// <summary>新增平台兩個 Create 與一個 Update 的訊息篩選，沿原 fixture 資料庫觀察真計畫與寫入。</summary>
    private static void SeedPlatformMessages(MemoryOrganization service)
    {
        foreach (var message in new[] { "Create", "Update" })
        {
            var row = service.Rows.SingleOrDefault(item => item.LogicalName == "sdkmessage" &&
                item.GetAttributeValue<string>("name") == message);
            if (row is null)
            {
                row = new Entity("sdkmessage", Guid.NewGuid()) { ["name"] = message };
                service.Seed(row);
            }
            foreach (var entity in new[] { "new_linechannel", "new_slotrequest" })
                service.Seed(new Entity("sdkmessagefilter", Guid.NewGuid())
                {
                    ["primaryobjecttypecode"] = entity, ["sdkmessageid"] = row.ToEntityReference()
                });
        }
    }

    /// <summary>當案單組織 connector，只借出合成 SDK；計數每次取得與歸還，拒絕其他組織名稱。</summary>
    private sealed class ScopedConnector(MemoryOrganization service, string organization) : IOrganizationConnector
    {
        internal int Calls { get; private set; }
        internal int Returned { get; private set; }
        /// <summary>每命令新轉接器，沒有跨命令憑證、網路連線或背景工作。</summary>
        public OrganizationLease Connect(string name)
        {
            Assert.Equal(organization, name);
            Calls++;
            return new OrganizationLease(new PlatformRegistrationService(service, name), () => Returned++);
        }
    }

    /// <summary>
    /// 測試借用的 SDK 轉接器，只補固定平台欄位 metadata 與當案組織身分；
    /// 其他操作完整委派原記憶體服務，沒有連線或額外 owner，不吞掉未支援請求。
    /// </summary>
    private sealed class PlatformRegistrationService(MemoryOrganization inner, string organization) : IOrganizationService
    {
        private readonly NamedRegistrationService named = new(inner, organization);
        /// <summary>new_product 回應僅供 filter／image 前置核對；其他官方訊息保留原故障行為。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveEntityRequest { LogicalName: "new_linechannel" })
            {
                var entity = new EntityMetadata { LogicalName = "new_linechannel" };
                typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.Attributes))!.SetValue(entity,
                    new AttributeMetadata[] { new PicklistAttributeMetadata { LogicalName = "new_product" } });
                return new RetrieveEntityResponse { Results = { ["EntityMetadata"] = entity } };
            }
            return named.Execute(request);
        }
        /// <summary>查詢回傳原 fixture 的 detached 候選，不建立快取。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => inner.RetrieveMultiple(query);
        /// <summary>只讀正式 ID 沿用原替身的精確查詢。</summary>
        public Entity Retrieve(string name, Guid id, ColumnSet columns) => inner.Retrieve(name, id, columns);
        /// <summary>建立保留原替身的寫入計數與故障注入。</summary>
        public Guid Create(Entity entity) => inner.Create(entity);
        /// <summary>更新由原 fixture 擁有並觀察，不保存跨案例狀態。</summary>
        public void Update(Entity entity) => inner.Update(entity);
        /// <summary>刪除只委派精確正式身分。</summary>
        public void Delete(string name, Guid id) => inner.Delete(name, id);
        /// <summary>未使用關聯仍由原服務明確拒絕。</summary>
        public void Associate(string name, Guid id, Relationship relationship, EntityReferenceCollection related) =>
            inner.Associate(name, id, relationship, related);
        /// <summary>未使用解除關聯仍由原服務明確拒絕。</summary>
        public void Disassociate(string name, Guid id, Relationship relationship, EntityReferenceCollection related) =>
            inner.Disassociate(name, id, relationship, related);
    }

    /// <summary>每案例擁有唯一暫存 repository，只寫合成清單並於 Dispose 清除整個根目錄。</summary>
    private sealed class RepositoryFixture : IDisposable
    {
        /// <summary>本案例隔離的固定清單根目錄。</summary>
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "f3-registration-" + Guid.NewGuid().ToString("N"));

        /// <summary>建立 Loader 預期的目錄，不接收外部路徑或建立任何服務連線。</summary>
        public RepositoryFixture() => Directory.CreateDirectory(Path.Combine(Root, "d365", "registration"));

        /// <summary>只寫入本案例 DTO 的有界序列化結果，覆寫只發生於此 fixture 自有固定檔案。</summary>
        public void Write(RegistrationDocument document) =>
            File.WriteAllText(Path.Combine(Root, "d365", "registration", "plugins.registration.json"),
                JsonSerializer.Serialize(document, RegistrationManifestLoader.Json));

        /// <summary>清除本案例唯一建立的目錄與文件，避免測試間殘留。</summary>
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

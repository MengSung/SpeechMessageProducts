/*
 * 檔案整體說明
 *
 * 目的與定位：驗證部署 CLI 對完整第二版清單的離線路由，防止只部署第一個組件。
 * 主要功能與流程：沿用 Fixture 的記憶體 SDK 與 Roslyn net462 PE 模式，在唯一暫存方案
 * 產生 Plugin 及活動 DLL，透過真正 CLI 執行計畫、套用、驗證、匯出與跨命令回復。
 * 故障注入與斷言：未知或重複 JSON、組織不符及外部備份路徑必須零寫入；
 * SDK 寫入失敗仍須有已關檔的具名備份索引，第二組件差異不能被第一組件成功掩蓋。
 * 使用邊界與維護：所有資料均為合成資料，不讀 Secrets、不連 D365 或 LINE；
 * 非平行集合協調程序工作目錄，案例 owner 先還原 cwd 再刪除自己的暫存根，
 * 每次命令的租約皆須歸還，檔案串流只在同步斷言期間持有，不宣稱實機驗收。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>使用真正 CLI 與合成雙組件 PE 保護完整文件部署及失敗回復契約。</summary>
[Collection("Deployment working directory")]
public sealed class DocumentDeploymentCliTests
{
    /// <summary>先驗差異一，再部署兩組件得到零；較早匯出的索引須能跨命令回復到未部署基準。</summary>
    [Fact]
    public void Cli_DocumentLifecycle_ProcessesBothAndRollsBackEarlierExport()
    {
        using var f = new CliFixture();
        Assert.Equal(1, f.Run("verify"));
        Assert.Contains("Deployment state differs", f.Output.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, f.Run("export"));
        var backup = f.File("backup.json");
        Assert.Equal(0, f.Run("plan"));
        var plan = f.File("plan.json");
        Assert.Equal(2, JsonSerializer.Deserialize<DocumentDeploymentPlan>(System.IO.File.ReadAllText(plan))!.Assemblies.Count);
        Assert.Equal(0, f.Run("apply", "--plan", plan));
        Assert.Equal(2, f.Core.Service.Rows.Count(row => row.LogicalName == "pluginassembly"));
        Assert.Equal(0, f.Run("verify"));
        Assert.Contains("Deployment state matches the registration manifest.", f.Output.ToString(), StringComparison.Ordinal);
        f.Core.Service.Rows.Single(row => row.LogicalName == "plugintype" &&
            row.GetAttributeValue<string>("typename") == "Sample.Send")["friendlyname"] = "外部變更";
        Assert.Equal(1, f.Run("verify"));
        Assert.Equal(0, f.Run("rollback", "--from", backup));
        Assert.DoesNotContain(f.Core.Service.Rows, row => row.LogicalName is "pluginassembly" or "plugintype" or "sdkmessageprocessingstep");
        Assert.Empty(f.Reports);
        Assert.Equal(f.Connector.Calls, f.Connector.Releases);
    }

    /// <summary>推播佔位仍未比較時回三並保留安全文字；不為綠燈額外解析 Secrets。</summary>
    [Fact]
    public void Cli_DocumentVerify_SecretPlaceholdersRemainIncomplete()
    {
        using var f = new CliFixture(push: true);
        Assert.Equal(0, f.Run("plan"));
        Assert.Equal(0, f.Run("apply", "--plan", f.File("plan.json")));
        Assert.Equal(3, f.Run("verify"));
        Assert.Contains("Secret values were not compared", f.Output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Fixture.SecretValue, f.Output.ToString(), StringComparison.Ordinal);
        Assert.Equal(f.Connector.Calls, f.Connector.Releases);
    }

    /// <summary>首筆或第二組件中途 SDK 寫入注入故障；索引及具名快照須已落盤關檔，後續回復排空部分寫入。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cli_ApplyFailure_PersistsRecoverableBackupBeforeWriting(bool partial)
    {
        using var f = new CliFixture();
        Assert.Equal(0, f.Run("plan"));
        f.Core.Service.BeforeWrite = () =>
        {
            using var stream = new FileStream(f.File("backup.json"), FileMode.Open, FileAccess.Read, FileShare.None);
            var backup = JsonSerializer.Deserialize<DocumentDeploymentBackup>(stream)!;
            Assert.Equal(2, backup.Assemblies.Count);
            Assert.All(backup.Assemblies, item => Assert.True(System.IO.File.Exists(item.Path)));
            if (partial && !f.Core.Service.Rows.Any(row => row.LogicalName == "pluginassembly" &&
                row.GetAttributeValue<string>("name") == "SpeechMessage.Dynamics.Workflows")) return;
            throw new InvalidOperationException(Fixture.SecretValue);
        };
        Assert.Equal(1, f.Run("apply", "--plan", f.File("plan.json")));
        Assert.Single(f.Reports);
        Assert.Equal(partial, f.Core.Service.Writes.Count != 0);
        f.Core.Service.BeforeWrite = null;
        Assert.Equal(0, f.Run("rollback", "--from", f.File("backup.json")));
        Assert.DoesNotContain(f.Core.Service.Rows, row => row.LogicalName is "pluginassembly" or "plugintype" or "sdkmessageprocessingstep");
        Assert.DoesNotContain(Fixture.SecretValue, f.Output.ToString(), StringComparison.Ordinal);
        Assert.Equal(f.Connector.Calls, f.Connector.Releases);
    }

    /// <summary>未知、重複、超深、超大與組織不符的計畫不得寫入；重新計算 SHA 也不能跨組織套用。</summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("organization")]
    [InlineData("depth")]
    [InlineData("size")]
    public void Cli_InvalidPlan_RejectsBeforeWrites(string fault)
    {
        using var f = new CliFixture();
        Assert.Equal(0, f.Run("plan"));
        var path = f.File("plan.json");
        var text = System.IO.File.ReadAllText(path);
        if (fault == "unknown") text = text.Insert(1, "\"Unexpected\":true,");
        if (fault == "duplicate") text = text.Insert(1, "\"Organization\":\"goodshepherdback\",");
        if (fault == "depth") text = "{\"Unexpected\":" + new string('[', 40) + "0" + new string(']', 40) + "}";
        if (fault == "size") text = new string(' ', 4 * 1024 * 1024 + 1);
        if (fault == "organization")
        {
            var plan = JsonSerializer.Deserialize<DocumentDeploymentPlan>(text)! with { Organization = "production", Sha256 = "" };
            plan = plan with { Sha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(plan))) };
            text = JsonSerializer.Serialize(plan);
        }
        System.IO.File.WriteAllText(path, text);
        Assert.Equal(1, f.Run("apply", "--plan", path));
        Assert.Empty(f.Core.Service.Writes);
        Assert.Equal(f.Connector.Calls, f.Connector.Releases);
    }

    /// <summary>索引即使重算摘要，指向組織外快照仍須在任何回復寫入前拒絕。</summary>
    [Fact]
    public void Cli_BackupOutsideOrganization_RejectsBeforeWrites()
    {
        using var f = new CliFixture();
        Assert.Equal(0, f.Run("export"));
        var path = f.File("backup.json");
        var backup = JsonSerializer.Deserialize<DocumentDeploymentBackup>(System.IO.File.ReadAllText(path))!;
        var items = backup.Assemblies.ToArray();
        var external = Path.Combine(f.Core.Root, items[0].AssemblyName + ".backup.json");
        System.IO.File.Copy(items[0].Path, external);
        items[0] = items[0] with { Path = external };
        backup = backup with { Assemblies = items, Sha256 = "" };
        backup = backup with { Sha256 = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(backup))) };
        System.IO.File.WriteAllText(path, JsonSerializer.Serialize(backup));
        Assert.Equal(1, f.Run("rollback", "--from", path));
        Assert.Empty(f.Core.Service.Writes);
    }

    /// <summary>備份索引也採嚴格解析；未知、重複欄位及不同組織不得進入回復寫入。</summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("organization")]
    public void Cli_InvalidBackup_RejectsBeforeWrites(string fault)
    {
        using var f = new CliFixture();
        Assert.Equal(0, f.Run("export"));
        var path = f.File("backup.json");
        var text = System.IO.File.ReadAllText(path);
        if (fault == "unknown") text = text.Insert(1, "\"Unexpected\":true,");
        if (fault == "duplicate") text = text.Insert(1, "\"Organization\":\"goodshepherdback\",");
        if (fault == "organization") text = text.Replace("goodshepherdback", "production", StringComparison.Ordinal);
        System.IO.File.WriteAllText(path, text);
        Assert.Equal(1, f.Run("rollback", "--from", path));
        Assert.Empty(f.Core.Service.Writes);
        Assert.Equal(f.Connector.Calls, f.Connector.Releases);
    }

    /// <summary>缺少尚未建立的 Workflows DLL 必須明確失敗，不能悄悄只規劃 Plugin。</summary>
    [Fact]
    public void Cli_MissingSecondDll_FailsWithoutWrites()
    {
        using var f = new CliFixture();
        System.IO.File.Delete(Path.Combine(f.Core.Root, "artifacts", "plugins", "SpeechMessage.Dynamics.Workflows.dll"));
        Assert.Equal(1, f.Run("plan"));
        Assert.Empty(f.Core.Service.Writes);
        Assert.Single(f.Reports);
    }

    /// <summary>單案例 owner：先準備合成方案再切 cwd；Dispose 還原 cwd 後才釋放暫存資料。</summary>
    private sealed class CliFixture : IDisposable
    {
        private readonly string previousDirectory = Directory.GetCurrentDirectory();
        /// <summary>既有假服務、暫存根及 PE 產生器，只有本案例持有。</summary>
        internal Fixture Core { get; }
        /// <summary>每命令的連線與釋放計數，不持有實機資源。</summary>
        internal CliConnector Connector { get; }
        /// <summary>僅保留本案例的安全命令輸出，Dispose 釋放。</summary>
        internal StringWriter Output { get; } = new(CultureInfo.InvariantCulture);
        /// <summary>原始故障只保留至本案例結束，供身分及數量斷言。</summary>
        internal List<Exception> Reports { get; } = new();
        private string ArtifactRoot => Path.Combine(Core.Root, "cli");

        /// <summary>產生固定相對路徑的兩份 PE 與清單，不碰正式方案、金鑰或 Secrets。</summary>
        internal CliFixture(bool push = false)
        {
            Core = new Fixture(push: push);
            Connector = new CliConnector(Core.Service);
            var registration = Path.Combine(Core.Root, "d365", "registration");
            var plugins = Path.Combine(Core.Root, "artifacts", "plugins");
            Directory.CreateDirectory(registration);
            Directory.CreateDirectory(plugins);
            System.IO.File.WriteAllText(Path.Combine(Core.Root, "SpeechMessageProducts.sln"), "");
            var workflow = new RegistrationManifest
            {
                AssemblyName = "SpeechMessage.Dynamics.Workflows", ActivityGroup = "SpeechMessage",
                Activities = new[] { new ActivityRegistration { Module = "attendance", Type = "Sample.Send", DisplayName = "合成活動" } }
            };
            var document = new RegistrationDocument { Assemblies = new[] { Core.Manifest, workflow } };
            System.IO.File.WriteAllText(Path.Combine(registration, "plugins.registration.json"),
                JsonSerializer.Serialize(document, RegistrationManifestLoader.Json));
            System.IO.File.Copy(Core.Emit(), Path.Combine(plugins, Core.Image.Name + ".dll"));
            var source = "[assembly:System.Runtime.Versioning.TargetFramework(\".NETFramework,Version=v4.6.2\")] " +
                "namespace System.Activities { public abstract class CodeActivity {} } " +
                "namespace Sample { public sealed class Send : System.Activities.CodeActivity {} }";
            var compilation = CSharpCompilation.Create(workflow.AssemblyName, new[] { CSharpSyntaxTree.ParseText(source) },
                new[] { MetadataReference.CreateFromFile(Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll")) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.True(compilation.Emit(Path.Combine(plugins, workflow.AssemblyName + ".dll")).Success);
            Directory.SetCurrentDirectory(Core.Root);
        }

        /// <summary>每次呼叫真正 CLI，所有例外交給本案例回報集合；秘密來源為固定假值。</summary>
        internal int Run(string command, params string[] options) => DeploymentCli.Run(
            new[] { command, "--org", Fixture.Organization }.Concat(options).ToArray(), Connector, Output,
            Reports.Add, ArtifactRoot, _ => Fixture.SecretValue);

        /// <summary>按程式固定檔名找唯一證據，重複索引亦視為測試失敗。</summary>
        internal string File(string name) => Assert.Single(Directory.GetFiles(ArtifactRoot, name, SearchOption.AllDirectories));

        /// <summary>即使斷言失敗也由 using 還原 cwd，然後清除案例自己的暫存根與 writer。</summary>
        public void Dispose()
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Output.Dispose();
            Core.Dispose();
        }
    }

    /// <summary>借用案例 SDK 的 connector，租約只計數釋放，不跨案例保存服務。</summary>
    private sealed class CliConnector(MemoryOrganization service) : IOrganizationConnector
    {
        /// <summary>證明有效命令僅連線一次。</summary>
        internal int Calls { get; private set; }
        /// <summary>證明失敗及成功命令均歸還租約。</summary>
        internal int Releases { get; private set; }
        /// <summary>建立當次獨占邏輯租約，沒有網路行為。</summary>
        public OrganizationLease Connect(string organization)
        {
            Calls++;
            return new OrganizationLease(service, () => Releases++);
        }
    }
}

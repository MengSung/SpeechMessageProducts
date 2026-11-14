/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-03a 完整文件部署契約，避免多組件文件只處理第一筆。
 * 主要功能與流程：以既有 Fixture 的隔離合成 SDK、假時鐘及三位元組映像驗證
 * Plugin 與活動一起規劃、套用、唯讀驗證、分開備份及回復；不連 D365 或 LINE。
 * 故障注入與斷言：破壞第二組件來源、身分、即時 metadata 或計畫，確認第一筆
 * 寫入之前拒絕，並確認來源秘密未解析。
 * 使用邊界與維護：Fixture 擁有本案例暫存目錄及記憶體資料，
 * using 結束確定刪除；本檔不把合成映像當作正式 PE，也不宣稱實機驗收。
 */
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>完整文件入口的離線契約；測試必須能抓到退回單組件處理的缺陷。</summary>
public sealed class DocumentDeploymentEngineTests
{
    /// <summary>兩組件各自完整套用；第一筆寫入時兩份具名備份已關閉且存在，再規劃零差異。</summary>
    [Fact]
    public void Document_PlanApplyVerify_ProcessesBothAssemblies()
    {
        using var f = new Fixture();
        var engine = Engine(f);
        var document = Document(f);
        var images = Images(f);
        var plan = engine.Plan(document, images, Fixture.Organization);
        Assert.Equal(2, plan.Assemblies.Count);
        Assert.All(plan.Assemblies, assembly => Assert.NotEmpty(assembly.Plan.Operations));
        Assert.Empty(f.Service.Writes);
        f.Service.BeforeWrite = () => Assert.Equal(2,
            Directory.GetFiles(f.Root, "*.backup.json", SearchOption.AllDirectories).Length);
        var backup = engine.Apply(plan, document, images);
        Assert.Equal(2, backup.Assemblies.Count);
        Assert.Equal(2, backup.Assemblies.Select(item => Path.GetDirectoryName(item.Path)).Distinct().Count());
        Assert.All(backup.Assemblies, item => Assert.Equal(item.AssemblyName + ".backup.json", Path.GetFileName(item.Path)));
        Assert.Equal(2, f.Service.Rows.Count(row => row.LogicalName == "pluginassembly"));
        Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
        Assert.Equal(2, f.Service.Rows.Count(row => row.LogicalName == "plugintype"));
        var writes = f.Service.Writes.Count;
        Assert.Equal(0, engine.Verify(document, images, Fixture.Organization));
        Assert.Equal(writes, f.Service.Writes.Count);
        Assert.Equal(0, f.SecretReads);
    }

    /// <summary>第二組件指紋遭第三方變更時，第一組件不能先寫入或先產生新備份。</summary>
    [Fact]
    public void Document_SecondFingerprintChanged_ZeroWrites()
    {
        using var f = new Fixture();
        var engine = Engine(f);
        var document = Document(f);
        var images = Images(f);
        engine.Apply(engine.Plan(document, images, Fixture.Organization), document, images);
        var plan = engine.Plan(document, images, Fixture.Organization);
        f.Service.Rows.Single(row => row.LogicalName == "plugintype" &&
            row.GetAttributeValue<string>("typename") == "Sample.Send")["friendlyname"] = "外部變更";
        var writes = f.Service.Writes.Count;
        var files = Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).Length;
        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, document, images));
        Assert.Equal(writes, f.Service.Writes.Count);
        Assert.Equal(files, Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories).Length);
    }

    /// <summary>缺少／多餘／重複映像、重複組件／步驟／舊 ID、非法第二組件在首次讀寫前拒絕。</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate-image")]
    [InlineData("extra-image")]
    [InlineData("duplicate-assembly")]
    [InlineData("duplicate-step")]
    [InlineData("duplicate-legacy")]
    [InlineData("invalid-second")]
    [InlineData("invalid-image-content")]
    [InlineData("wrong-organization")]
    public void Document_InvalidInput_ZeroWrites(string fault)
    {
        using var f = new Fixture();
        var engine = Engine(f);
        var document = Document(f);
        var images = Images(f);
        var organization = Fixture.Organization;
        switch (fault)
        {
            case "missing": images = new[] { images[0] }; break;
            case "duplicate-image": images = new[] { images[0], images[0] }; break;
            case "extra-image": images = new[] { images[0], images[1], images[0] }; break;
            case "duplicate-assembly": document = document with { Assemblies = new[] { document.Assemblies[0], document.Assemblies[0] } }; break;
            case "duplicate-step": document = document with { Assemblies = new[] { f.Manifest with { Steps = new[] { f.Manifest.Steps[0], f.Manifest.Steps[0] } }, document.Assemblies[1] } }; break;
            case "duplicate-legacy":
                var reference = new LegacyStepReference { Assembly = "Old", Type = "Old.Type", StepId = Guid.NewGuid().ToString("D") };
                var first = f.Manifest with { Steps = new[] { f.Manifest.Steps[0] with { Legacy = new[] { reference } } } };
                var second = f.Manifest with { AssemblyName = images[1].Name, Steps = new[] { f.Manifest.Steps[0] with { Legacy = new[] { reference } } } };
                document = document with { Assemblies = new[] { first, second } };
                images = new[] { images[0], images[1] with { PluginTypes = f.Image.PluginTypes, ActivityTypes = Array.Empty<string>() } };
                break;
            case "invalid-second": document = document with { Assemblies = new[] { f.Manifest, document.Assemblies[1] with { ActivityGroup = "" } } }; break;
            case "invalid-image-content": images = new[] { images[0], images[1] with { Content = "BAUG" } }; break;
            case "wrong-organization": organization = "production"; break;
        }
        Assert.Throws<InvalidDataException>(() => engine.Plan(document, images, organization));
        Assert.Empty(f.Service.Writes);
        Assert.Empty(Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>第二組件的來源或計畫被改，即使重算外層 SHA 也不能在第一組件寫入後才發現。</summary>
    [Theory]
    [InlineData("source")]
    [InlineData("image")]
    [InlineData("hash")]
    [InlineData("operation")]
    [InlineData("rehashed-operation")]
    [InlineData("omitted-assembly")]
    public void Document_Tampering_ZeroWrites(string fault)
    {
        using var f = new Fixture();
        var engine = Engine(f);
        var document = Document(f);
        var images = Images(f);
        var plan = engine.Plan(document, images, Fixture.Organization);
        if (fault == "source")
            document = document with { Assemblies = new[] { f.Manifest, document.Assemblies[1] with { ActivityGroup = "新群組" } } };
        else if (fault == "image")
            images = new[] { images[0], images[1] with { Version = "1.0.0.1" } };
        else if (fault == "hash") plan = plan with { Sha256 = new string('0', 64) };
        else if (fault == "omitted-assembly") plan = plan with { Assemblies = new[] { plan.Assemblies[0] } };
        else
        {
            var child = plan.Assemblies[1].Plan with { Operations = new[] { new DeploymentOperation("CreateAssembly", "injected") } };
            if (fault == "rehashed-operation")
                child = child with { Sha256 = Hash(child with { Sha256 = "" }) };
            plan = plan with { Assemblies = new[] { plan.Assemblies[0], plan.Assemblies[1] with { Plan = child } } };
            if (fault == "rehashed-operation") plan = plan with { Sha256 = Hash(plan with { Sha256 = "" }) };
        }
        Assert.Throws<InvalidDataException>(() => engine.Apply(plan, document, images));
        Assert.Empty(f.Service.Writes);
        Assert.Empty(Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories));
    }

    /// <summary>兩組件的獨立快照都可回復；任何一份備份損壞時全部先拒絕，再用完整備份回復。</summary>
    [Fact]
    public void Document_ExportRollback_ValidatesAllBackupsBeforeWriting()
    {
        using var f = new Fixture();
        var engine = Engine(f);
        var document = Document(f);
        var images = Images(f);
        engine.Apply(engine.Plan(document, images, Fixture.Organization), document, images);
        var backup = engine.Export(document, images, Fixture.Organization);
        var changed = document with { Assemblies = new[] { f.Manifest,
            document.Assemblies[1] with { Activities = new[] { document.Assemblies[1].Activities[0] with { DisplayName = "更新名稱" } } } } };
        engine.Apply(engine.Plan(changed, images, Fixture.Organization), changed, images);
        var path = backup.Assemblies[1].Path;
        var original = File.ReadAllText(path);
        File.WriteAllText(path, "{}");
        var writes = f.Service.Writes.Count;
        Assert.Throws<InvalidDataException>(() => engine.Rollback(backup, document, images));
        Assert.Equal(writes, f.Service.Writes.Count);
        File.WriteAllText(path, original, new UTF8Encoding(false));
        engine.Rollback(backup, document, images);
        Assert.Equal(0, engine.Verify(document, images, Fixture.Organization));
    }

    /// <summary>空組件快照回復應刪除本次新增的兩套組件與子列，讓資源數回到基準。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Document_RollbackNewAssemblies_DrainsManagedRows(bool push)
    {
        using var f = new Fixture(push: push);
        var engine = Engine(f);
        var document = Document(f);
        var images = Images(f);
        var backup = engine.Apply(engine.Plan(document, images, Fixture.Organization), document, images);
        engine.Rollback(backup, document, images);
        Assert.DoesNotContain(f.Service.Rows, row => row.LogicalName is "pluginassembly" or "plugintype" or
            "sdkmessageprocessingstep" or "sdkmessageprocessingstepsecureconfig");
        Assert.Equal(0, f.SecretReads);
    }

    /// <summary>故意讓範圍外步驟引用同一安全設定；回復不能刪除仍有合法 owner 的設定或外部步驟。</summary>
    [Fact]
    public void Document_Rollback_PreservesSecureConfigurationStillReferencedElsewhere()
    {
        using var f = new Fixture(push: true);
        var engine = Engine(f);
        var document = Document(f);
        var images = Images(f);
        var backup = engine.Apply(engine.Plan(document, images, Fixture.Organization), document, images);
        var configuration = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepsecureconfig"));
        var external = new Entity("sdkmessageprocessingstep", Guid.NewGuid())
        {
            ["eventhandler"] = new EntityReference("plugintype", Guid.NewGuid()),
            ["sdkmessageprocessingstepsecureconfigid"] = configuration.ToEntityReference()
        };
        f.Service.Seed(external);
        engine.Rollback(backup, document, images);
        Assert.Contains(f.Service.Rows, row => row.LogicalName == external.LogicalName && row.Id == external.Id);
        Assert.Equal(configuration.Id,
            Assert.Single(f.Service.Rows.Where(row => row.LogicalName == configuration.LogicalName)).Id);
    }

    /// <summary>秘密佔位未比較的結果仍為三；有任何組件差異則為一，不能把部分成功當成全份相等。</summary>
    [Fact]
    public void Document_Verify_ReportsDifferenceAndIncomplete()
    {
        using var f = new Fixture(push: true);
        var engine = Engine(f);
        var document = Document(f);
        var images = Images(f);
        Assert.Equal(1, engine.Verify(document, images, Fixture.Organization));
        engine.Apply(engine.Plan(document, images, Fixture.Organization), document, images);
        Assert.Equal(3, engine.Verify(document, images, Fixture.Organization));
        Assert.Equal(0, Engine(f).Plan(document, images, Fixture.Organization).Assemblies.Sum(item => item.Plan.Operations.Count));
    }

    /// <summary>保留單組件文件可用；資料的呼叫端修改不能污染已核准計畫。</summary>
    [Fact]
    public void Document_SingleAssembly_RemainsCompatible()
    {
        using var f = new Fixture();
        var engine = Engine(f);
        var document = new RegistrationDocument { Assemblies = new[] { f.Manifest } };
        var images = new[] { f.Image };
        engine.Apply(engine.Plan(document, images, Fixture.Organization), document, images);
        Assert.Empty(f.Plan().Operations);
    }

    /// <summary>SDK 回呼修改呼叫端陣列時，後續組件仍使用已驗的獨立候選，不接受寫入中途換料。</summary>
    [Fact]
    public void Document_CallerMutationAfterPreflight_DoesNotChangeAppliedSource()
    {
        using var f = new Fixture();
        var engine = Engine(f);
        var source = Document(f);
        var assemblies = source.Assemblies.ToArray();
        var document = source with { Assemblies = assemblies };
        var images = Images(f);
        var plan = engine.Plan(document, images, Fixture.Organization);
        f.Service.BeforeWrite = () =>
        {
            assemblies[1] = assemblies[1] with { ActivityGroup = "未核准" };
            images[1] = images[1] with { Content = "BAUG" };
        };
        engine.Apply(plan, document, images);
        Assert.Equal("SpeechMessage", f.Service.Rows.Single(row => row.LogicalName == "plugintype" &&
            row.GetAttributeValue<string>("typename") == "Sample.Send").GetAttributeValue<string>("workflowactivitygroupname"));
        Assert.Equal(0, engine.Verify(source, Images(f), Fixture.Organization));
    }

    /// <summary>兩活動顯示名稱相同但型別不同仍須保留各自資料庫 ID，不以顯示文字去重。</summary>
    [Fact]
    public void Document_SameDisplayNames_PreserveDistinctActivities()
    {
        using var f = new Fixture();
        var engine = Engine(f);
        var document = Document(f);
        var activity = document.Assemblies[1].Activities[0];
        document = document with { Assemblies = new[] { f.Manifest,
            document.Assemblies[1] with { Activities = new[] { activity, activity with { Type = "Sample.Other" } } } } };
        var images = Images(f);
        images[1] = images[1] with { ActivityTypes = new[] { "Sample.Send", "Sample.Other" } };
        engine.Apply(engine.Plan(document, images, Fixture.Organization), document, images);
        var activities = f.Service.Rows.Where(row => row.LogicalName == "plugintype" &&
            row.GetAttributeValue<string>("friendlyname") == "合成活動").ToArray();
        Assert.Equal(2, activities.Length);
        Assert.NotEqual(activities[0].Id, activities[1].Id);
        Assert.Equal(0, engine.Verify(document, images, Fixture.Organization));
    }

    /// <summary>只借用同一案例的服務；本層不擁有 SDK，不解析真實秘密。</summary>
    private static DocumentDeploymentEngine Engine(Fixture f) => new(f.Service, f.Root, f.Clock, _ => Fixture.SecretValue);

    /// <summary>第二組件使用活動，證明編排沒有把 Workflow 遺漏或誤當 Plugin 步驟。</summary>
    private static RegistrationDocument Document(Fixture f) => new()
    {
        Assemblies = new[] { f.Manifest, new RegistrationManifest
        {
            AssemblyName = "SpeechMessage.Dynamics.Workflows", ActivityGroup = "SpeechMessage",
            Activities = new[] { new ActivityRegistration { Module = "attendance", Type = "Sample.Send", DisplayName = "合成活動" } }
        } }
    };

    /// <summary>沿用固定合成位元組但分開型別身份；不執行或反射任何待部署 DLL。</summary>
    private static AssemblyImage[] Images(Fixture f) => new[] { f.Image, f.Image with
    {
        Name = "SpeechMessage.Dynamics.Workflows", PluginTypes = Array.Empty<string>(), ActivityTypes = new[] { "Sample.Send" }
    } };

    /// <summary>獨立重算 unsigned JSON 雜湊，以測試可重算 SHA 並不代表能注入未規劃的動作。</summary>
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
}

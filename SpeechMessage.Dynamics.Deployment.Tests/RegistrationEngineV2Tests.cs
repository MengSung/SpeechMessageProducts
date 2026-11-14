/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-03a 清單第二版在真實 DeploymentEngine 的映像與活動註冊行為，
 * 使用 Fixture 每案例隔離的合成 SDK、時鐘與暫存檔；不讀 secrets、不連測試或正式組織。
 * 主要功能與流程：套用計畫後讀回正式 metadata，確認具名映像欄位、活動群組、原 ID
 * 更新及冪等；注入第三方 metadata 變動以確認舊 plan 在任何寫入前拒絕。
 * 使用邊界與維護：本檔不證明 D365 實機支援；合成服務的官方欄位白名單獨立維護。
 * Fixture 是資料與暫存檔唯一 owner，Dispose 清理；沒有共用可變狀態、連線或背景工作。
 */
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以真 planner、apply 與唯讀再規劃驗證 v2 的 metadata 契約。</summary>
public sealed class RegistrationEngineV2Tests
{
    /// <summary>具名 PostImage 應註冊正式 lookup、別名與欄位，重新規劃零差異且匯出保留安全 metadata。</summary>
    [Fact]
    public void Manifest_Images_CreatedAndVerified()
    {
        using var f = new Fixture();
        var manifest = WithImage(f, "subject");
        f.Engine.Apply(f.Engine.Plan(manifest, f.Image, Fixture.Organization), manifest, f.Image);
        var step = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
        var image = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepimage"));
        Assert.Equal(step.Id, image.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid").Id);
        Assert.Equal("PostImage", image.GetAttributeValue<string>("entityalias"));
        Assert.Equal("subject", image.GetAttributeValue<string>("attributes"));
        Assert.Equal("Id", image.GetAttributeValue<string>("messagepropertyname"));
        Assert.Equal(1, image.GetAttributeValue<OptionSetValue>("imagetype").Value);
        Assert.Empty(f.Engine.Plan(manifest, f.Image, Fixture.Organization).Operations);
        f.Engine.WriteExport(Fixture.Organization, Path.Combine(f.Root, "images.json"));
        Assert.Contains("PostImage", File.ReadAllText(Path.Combine(f.Root, "images.json")), StringComparison.Ordinal);
    }

    /// <summary>更新映像欄位保留正式 ID；移除清單映像則規劃並刪除該映像，不能遺留過度擷取。</summary>
    [Fact]
    public void Manifest_Images_UpdatedAndRemovedWithoutRecreatingStep()
    {
        using var f = new Fixture();
        var original = WithImage(f, "subject");
        f.Engine.Apply(f.Engine.Plan(original, f.Image, Fixture.Organization), original, f.Image);
        var imageId = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepimage")).Id;
        var stepId = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep")).Id;
        var changed = WithImage(f, "description");
        f.Engine.Apply(f.Engine.Plan(changed, f.Image, Fixture.Organization), changed, f.Image);
        var image = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepimage"));
        Assert.Equal(imageId, image.Id);
        Assert.Equal("description", image.GetAttributeValue<string>("attributes"));
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        Assert.Empty(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepimage"));
        Assert.Equal(stepId, Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep")).Id);
        Assert.Empty(f.Plan().Operations);
    }

    /// <summary>plan 後映像欄位被外部改動必須使指紋失效；斷言沒有額外寫入而非只比例外文字。</summary>
    [Fact]
    public void Manifest_ImageChangedAfterPlan_RefusedBeforeWrite()
    {
        using var f = new Fixture();
        var manifest = WithImage(f, "subject");
        f.Engine.Apply(f.Engine.Plan(manifest, f.Image, Fixture.Organization), manifest, f.Image);
        var plan = f.Engine.Plan(manifest, f.Image, Fixture.Organization);
        Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepimage"))["attributes"] = "description";
        var count = f.Service.Writes.Count;
        Assert.Throws<InvalidDataException>(() => f.Engine.Apply(plan, manifest, f.Image));
        Assert.Equal(count, f.Service.Writes.Count);
    }

    /// <summary>活動只註冊 plugintype，群組與顯示名稱可驗證且改名保留原 ID；不產生 Plugin step。</summary>
    [Fact]
    public void Manifest_Activity_RegisteredWithGroup()
    {
        using var f = new Fixture();
        var manifest = new RegistrationManifest
        {
            AssemblyName = "SpeechMessage.Dynamics.Workflows", ActivityGroup = "SpeechMessage",
            Activities = new[] { new ActivityRegistration { Module = "attendance", Type = "Sample.Send", DisplayName = "合成活動" } }
        };
        var image = f.Image with { Name = manifest.AssemblyName, PluginTypes = Array.Empty<string>(), ActivityTypes = new[] { "Sample.Send" } };
        f.Engine.Apply(f.Engine.Plan(manifest, image, Fixture.Organization), manifest, image);
        var type = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "plugintype"));
        Assert.Equal("SpeechMessage", type.GetAttributeValue<string>("workflowactivitygroupname"));
        Assert.Equal("合成活動", type.GetAttributeValue<string>("friendlyname"));
        Assert.Empty(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
        Assert.Empty(f.Engine.Plan(manifest, image, Fixture.Organization).Operations);
        var changed = manifest with { Activities = new[] { manifest.Activities[0] with { DisplayName = "新名稱" } } };
        f.Engine.Apply(f.Engine.Plan(changed, image, Fixture.Organization), changed, image);
        var updated = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "plugintype"));
        Assert.Equal(type.Id, updated.Id);
        Assert.Equal("新名稱", updated.GetAttributeValue<string>("friendlyname"));
        Assert.Empty(f.Engine.Plan(changed, image, Fixture.Organization).Operations);
    }

    /// <summary>拼錯的 schema 欄位即使通過字元檢查仍須在任何寫入前拒絕，不可註冊不存在的映像欄位。</summary>
    [Fact]
    public void Manifest_ImageUnknownAttribute_RefusedBeforeWrite()
    {
        using var f = new Fixture();
        Assert.Throws<InvalidDataException>(() => f.Engine.Plan(WithImage(f, "not_an_attribute"), f.Image, Fixture.Organization));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>rollback 必須復原映像欄位及原 ID，不能只回復組件內容與步驟開關。</summary>
    [Fact]
    public void Manifest_Images_RollbackRestoresMetadata()
    {
        using var f = new Fixture();
        var original = WithImage(f, "subject");
        f.Engine.Apply(f.Engine.Plan(original, f.Image, Fixture.Organization), original, f.Image);
        var imageId = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepimage")).Id;
        var changed = WithImage(f, "description");
        var backup = f.Engine.Apply(f.Engine.Plan(changed, f.Image, Fixture.Organization), changed, f.Image);
        f.Engine.Rollback(backup, original, Fixture.Organization);
        var restored = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepimage"));
        Assert.Equal(imageId, restored.Id);
        Assert.Equal("subject", restored.GetAttributeValue<string>("attributes"));
        Assert.Empty(f.Engine.Plan(original, f.Image, Fixture.Organization).Operations);
    }

    /// <summary>新增映像與活動欄位後，先前版本的有效 Plugin 備份仍應按原雜湊格式核對並回復。</summary>
    [Fact]
    public void Rollback_PreviousExportFormat_RemainsReadable()
    {
        using var f = new Fixture();
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var path = Path.Combine(f.Root, "previous.json");
        f.Engine.WriteExport(Fixture.Organization, path);
        var snapshot = JsonSerializer.Deserialize<DeploymentExport>(File.ReadAllText(path))!;
        var legacy = new
        {
            SchemaVersion = 1, snapshot.Organization, snapshot.ExportedUtc, snapshot.Fingerprint, snapshot.Assembly,
            Types = snapshot.Types.Select(type => new { type.Id, type.Name, type.TypeName }).ToArray(),
            snapshot.Steps, Sha256 = ""
        };
        var canonical = JsonSerializer.Serialize(legacy);
        var previous = JsonNode.Parse(canonical)!.AsObject();
        previous["Sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        File.WriteAllText(path, previous.ToJsonString());
        f.Engine.Rollback(path, f.Manifest, Fixture.Organization);
        Assert.Empty(f.Plan().Operations);
    }

    /// <summary>Update 的同步前置步驟需註冊篩選與 PreImage；改篩選欄位保留步驟身份且再規劃零差異。</summary>
    [Fact]
    public void Manifest_UpdatePreOperation_RegistersAndUpdatesFilter()
    {
        using var f = new Fixture();
        var message = new Entity("sdkmessage", Guid.NewGuid()) { ["name"] = "Update" };
        f.Service.Seed(message);
        f.Service.Seed(new Entity("sdkmessagefilter", Guid.NewGuid()) { ["sdkmessageid"] = message.ToEntityReference(), ["primaryobjecttypecode"] = "task" });
        var step = f.Manifest.Steps[0] with { Module = "attendance", Message = "Update", Stage = "PreOperation", Mode = "Synchronous",
            UnsecureConfig = "{\"mode\":\"Shadow\"}", FilteringAttributes = new[] { "subject" },
            Images = new[] { new RegistrationImage { Name = "PreImage", Type = "PreImage", Attributes = new[] { "subject" } } } };
        var manifest = f.Manifest with { Steps = new[] { step } };
        f.Engine.Apply(f.Engine.Plan(manifest, f.Image, Fixture.Organization), manifest, f.Image);
        var registered = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
        Assert.Equal(20, registered.GetAttributeValue<OptionSetValue>("stage").Value);
        Assert.Equal(0, registered.GetAttributeValue<OptionSetValue>("mode").Value);
        Assert.Equal("subject", registered.GetAttributeValue<string>("filteringattributes"));
        Assert.Equal("Target", Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepimage")).GetAttributeValue<string>("messagepropertyname"));
        var changed = manifest with { Steps = new[] { step with { FilteringAttributes = new[] { "description" } } } };
        f.Engine.Apply(f.Engine.Plan(changed, f.Image, Fixture.Organization), changed, f.Image);
        var updated = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep"));
        Assert.Equal(registered.Id, updated.Id);
        Assert.Equal("description", updated.GetAttributeValue<string>("filteringattributes"));
        Assert.Empty(f.Engine.Plan(changed, f.Image, Fixture.Organization).Operations);
        Assert.Equal(0, f.SecretReads);
    }

    /// <summary>固定合成 task 映像；所有欄位由測試服務獨立提供 metadata，不以產品清單自證。</summary>
    /// <param name="f">本案例 owner。</param><param name="attribute">要測試的單一欄位。</param><returns>不可變候選。</returns>
    private static RegistrationManifest WithImage(Fixture f, string attribute) => f.Manifest with
    {
        Steps = new[] { f.Manifest.Steps[0] with { Images = new[] { new RegistrationImage { Name = "PostImage", Type = "PostImage", Attributes = new[] { attribute } } } } }
    };
}

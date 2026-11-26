/*
檔案整體說明
目的與定位：保護 FS 對複製測試組織既有不可變 metadata 的精確沿用，不讓新建表規格
被任意擁有權覆蓋。所有組織及 metadata 身分是公開管理識別，測試不讀業務資料或秘密。
主要流程：從本工作樹分層清單建立定義，以純值完整快照模擬 F0 已觀察的兩張既有表；
正常案例必須在計畫列明沿用，缺表、換組織、換正式 ID、換 owner 及其他安全約束漂移皆拒絕。
資源與隔離：每案擁有自己的不可變候選陣列，沒有 SDK 連線、檔案寫入、背景工作或全域可變狀態。
驗證方式：正例防止測試只靠既有拒絕路徑通過；反例逐次只改一項前提，並保留新組織仍新建
UserOwned 表的契約。canonical JSON 案例以 FS 前欄位集合重建歷史表示，不以兩次新版載入互比。
失敗處理：任何基線不符皆須在產生可套用計畫前拋資料錯誤；離線結果不代表已部署或已停用自動化。
*/
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Schema;

/// <summary>以獨立固定 F0 管理身分保護精確既有沿用；不接觸任何實機服務或資料列。</summary>
public sealed class ExistingMetadataBaselineTests
{
    /// <summary>F0 已唯讀核對的測試組織正式 ID，不能以同名替代組織通過。</summary>
    private static readonly Guid OrganizationId = Guid.Parse("ec4d281b-bbe7-f111-8cce-00155d007916");

    /// <summary>核定 metadata 的正式身分；顯示名稱與臨時測試 GUID 都不能取代。</summary>
    private static Guid MetadataId(string entity) => Guid.Parse(entity == "new_localizedtext"
        ? "f417a3e7-d3d9-f111-8cca-00155d007916" : "8407cdf5-d3d9-f111-8cca-00155d007916");

    /// <summary>完整比對通過後，計畫須列出兩個沿用 owner 的正式 ID，且不重建或更新擁有權。</summary>
    [Fact]
    public void Baseline_ExactObservedEntities_AreExplicitlyRetained()
    {
        var (definition, snapshot) = Candidate();
        var plan = SchemaPlanner.Create(definition, snapshot, DateTimeOffset.UnixEpoch);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(plan, SchemaFiles.Json));
        var retained = json.RootElement.GetProperty("retainedOwnership").EnumerateArray().ToArray();
        Assert.Equal(2, retained.Length);
        Assert.All(retained, item =>
        {
            var name = item.GetProperty("entity").GetString()!;
            Assert.Equal(MetadataId(name), item.GetProperty("metadataId").GetGuid());
            Assert.Equal("OrganizationOwned", item.GetProperty("ownership").GetString());
        });
        Assert.DoesNotContain(plan.Operations, operation => operation.Kind == "CreateEntity");
        SchemaPlanner.ValidatePlan(plan, definition, snapshot);
    }

    /// <summary>逐一破壞組織、表 ID、原 owner 或安全約束；即使名稱合法仍不可規劃任何相容性逃生操作。</summary>
    /// <param name="fault">只影響單一前提的合成漂移。</param>
    [Theory]
    [InlineData("organization-id")]
    [InlineData("metadata-id")]
    [InlineData("owner")]
    [InlineData("missing")]
    [InlineData("audit")]
    [InlineData("managed")]
    [InlineData("field-security")]
    [InlineData("field-length")]
    public void Baseline_ObservedIdentityOrConstraintsDrift_RefusesPlan(string fault)
    {
        var (definition, snapshot) = Candidate();
        var target = snapshot.Entities.Single(entity => entity.LogicalName == "new_localizedtext");
        if (fault == "organization-id") snapshot = snapshot with { OrganizationId = Guid.NewGuid() };
        else if (fault == "missing") snapshot = snapshot with
        {
            Entities = snapshot.Entities.Where(entity => entity.Id != target.Id).ToArray()
        };
        else
        {
            var changed = fault switch
            {
                "metadata-id" => target with { Id = Guid.NewGuid() },
                "owner" => target with { Ownership = "UserOwned" },
                "audit" => target with { Audit = false },
                "managed" => target with { Managed = true },
                "field-security" => target with
                {
                    Fields = target.Fields.Select(field => field.LogicalName == "new_text"
                        ? field with { Secured = true } : field).ToArray()
                },
                "field-length" => target with
                {
                    Fields = target.Fields.Select(field => field.LogicalName == "new_text"
                        ? field with { MaxLength = 1999 } : field).ToArray()
                },
                _ => throw new InvalidOperationException()
            };
            snapshot = snapshot with
            {
                Entities = snapshot.Entities.Select(entity => entity.Id == target.Id ? changed : entity).ToArray()
            };
        }
        Assert.Throws<InvalidDataException>(() =>
            SchemaPlanner.Create(definition, snapshot, DateTimeOffset.UnixEpoch));
    }

    /// <summary>其他新測試組織不沿用好牧人的 owner；缺少租戶文字表仍須以原 UserOwned 定義規劃建立。</summary>
    [Fact]
    public void Baseline_NewOrganization_KeepsUserOwnedCreationContract()
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(SchemaDefinitionTests.SchemaPath())))!;
        var definition = SchemaFiles.LoadForOrganization(root, "construction");
        var snapshot = SchemaPlannerTests.CompleteSnapshot(definition) with { Organization = "construction" };
        snapshot = snapshot with
        {
            Entities = snapshot.Entities.Where(entity => entity.LogicalName != "new_localizedtext").ToArray()
        };
        var plan = SchemaPlanner.Create(definition, snapshot, DateTimeOffset.UnixEpoch);
        Assert.Contains(plan.Operations, operation =>
            operation.Kind == "CreateEntity" && operation.Entity == "new_localizedtext");
        Assert.Equal("UserOwned", definition.Entities.Single(entity =>
            entity.LogicalName == "new_localizedtext").OwnershipType);
    }

    /// <summary>FS 新增的空檢視與 null owner 不得改變舊 schema 的 canonical 表示；真差異仍由 Hash 涵蓋。</summary>
    [Fact]
    public void Baseline_LegacyCanonicalHash_OmitsFieldsAbsentBeforeFs()
    {
        var definition = SchemaFiles.Load(SchemaDefinitionTests.SchemaPath());
        var historical = JsonNode.Parse(JsonSerializer.Serialize(definition, SchemaFiles.Json))!.AsObject();
        historical.Remove("views");
        foreach (var entity in historical["entities"]!.AsArray())
            entity!.AsObject().Remove("ownershipType");
        var expected = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(historical.ToJsonString(SchemaFiles.Json))));
        Assert.Equal(expected, SchemaFiles.Hash(definition));
    }

    /// <summary>舊 goodshepherdback 未宣告 owner 時仍須維持原 OrganizationOwned 檢查，不以 null 放行任意 owner。</summary>
    [Fact]
    public void Baseline_LegacyNullOwner_StillRejectsOwnershipDrift()
    {
        var definition = SchemaPlannerTests.Definition();
        var snapshot = SchemaPlannerTests.CompleteSnapshot(definition);
        snapshot = snapshot with
        {
            Entities = snapshot.Entities.Select(entity => entity.LogicalName == "new_localizedtext"
                ? entity with { Ownership = "UserOwned" } : entity).ToArray()
        };
        Assert.Throws<InvalidDataException>(() =>
            SchemaPlanner.Create(definition, snapshot, DateTimeOffset.UnixEpoch));
    }

    /// <summary>F0 已有 LINE 欄長 500；三組織的共用新建契約保持同值，避免縮短既有欄位或產生不相容 plan。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void Baseline_ContactLineIdentity_PreservesObservedCapacity(string organization)
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(SchemaDefinitionTests.SchemaPath())))!;
        var definition = SchemaFiles.LoadForOrganization(root, organization);
        Assert.Equal(500, definition.Entities.Single(entity => entity.LogicalName == "contact")
            .Fields.Single(field => field.LogicalName == "new_lineid").MaxLength);
    }

    /// <summary>建構每案獨佔且符合分層要求的完整合成快照，僅兩張表沿用已觀察的原 owner 與正式 ID。</summary>
    private static (SchemaDefinition Definition, SchemaSnapshot Snapshot) Candidate()
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(SchemaDefinitionTests.SchemaPath())))!;
        var definition = SchemaFiles.LoadForOrganization(root, "goodshepherd20");
        var snapshot = SchemaPlannerTests.CompleteSnapshot(definition);
        var entities = snapshot.Entities.Select(entity => entity with
        {
            Id = entity.LogicalName is "new_localizedtext" or "new_offeringcategory"
                ? MetadataId(entity.LogicalName) : entity.Id,
            Ownership = entity.LogicalName is "new_localizedtext" or "new_offeringcategory"
                ? "OrganizationOwned"
                : definition.Entities.Single(expected => expected.LogicalName == entity.LogicalName).OwnershipType ?? entity.Ownership
        }).ToArray();
        return (definition, snapshot with
        {
            Organization = "goodshepherd20", OrganizationId = OrganizationId, Entities = entities
        });
    }
}

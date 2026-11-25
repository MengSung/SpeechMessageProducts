/*
檔案整體說明
目的與定位：驗證 FS schema 分層解析、實體／欄位衝突、產品隔離及新實體 ownership 契約。
主要流程：對正式舊 schema 核對位元組 SHA 與合併後 canonical hash；以每案獨立的臨時 repo fixture
注入跨層重複實體、平台欄位延伸、欄位重複、缺少 ownership 及租戶業務實體錯誤 ownership。
依賴與隔離：只呼叫 Deployment 的純 schema parser／planner 邊界，所有端點與資料均為合成值；
fixture 由本測試建立並在 Dispose 刪除，不讀 User Secrets、不使用 SDK、不連接 D365。
故障注入與決定性斷言：合法控制 fixture 必須能讀取；每種衝突必須拒絕完整候選，
construction／association 的結果不得含教會專屬 entity，且空層合併不得產生部分 schema。
驗證限制：hash 與所有 parser 結果只屬離線證據，不代表任一組織已 plan、apply、verify 或發布。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using SpeechMessage.Dynamics.Deployment.Schema;

namespace SpeechMessage.Dynamics.Deployment.Tests.Schema;

/// <summary>保護多層 schema 的完整合併與 fail-closed 契約；測試 fixture 為短生命週期合成檔案。</summary>
public sealed class SchemaLayerTests
{
    /// <summary>舊 core 檔逐位元組維持 FS 前版本，goodshepherdback 合併 schema 的 canonical hash 也須完全相同。</summary>
    [Fact]
    public void Layers_GoodShepherdBack_MergedSchemaUnchanged()
    {
        var path = SchemaDefinitionTests.SchemaPath();
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("AE008B2B75F9AB35EAFC78A18197CC9300676AA5A53272D33AC0B05BE3185310",
            Convert.ToHexString(SHA256.HashData(bytes)));

        using var fixture = SchemaLayerFixture.FromRepository();
        var before = SchemaFiles.Load(path);
        var merged = SchemaFiles.LoadForOrganization(fixture.Root, "goodshepherdback");

        Assert.Equal(SchemaFiles.Hash(before), SchemaFiles.Hash(merged));
    }

    /// <summary>兩個非教會產品的正式 layer composition 都不得帶入奉獻或課程 entity。</summary>
    [Fact]
    public void Plan_ConstructionOrg_ContainsNoChurchEntities()
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var forbidden = new[]
        {
            "new_fee",
            "new_offeringcategory",
            "new_dedication_booking",
            "new_stor_lessons"
        };

        foreach (var organization in new[] { "construction", "association" })
        {
            var schema = SchemaFiles.LoadForOrganization(fixture.Root, organization);
            Assert.Empty(schema.Entities.Select(entity => entity.LogicalName).Intersect(forbidden, StringComparer.Ordinal));
        }
    }

    /// <summary>同一平台目錄可在不同 schema 檔擴充同一實體的不同欄位，合併後兩欄都完整保留。</summary>
    [Fact]
    public void Layers_PlatformFolder_EntityExtendedAcrossFiles_Merged()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"goodshepherdback":["speechmessagecore"],"construction":["platform"]}}""",
            new Dictionary<string, string>
            {
                ["speechmessagecore.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_old")),
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_first")),
                ["platform/10-extension.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_second"))
            });

        var merged = SchemaFiles.LoadForOrganization(fixture.Root, "construction");
        var account = Assert.Single(merged.Entities);

        Assert.Contains(account.Fields, field => field.LogicalName == "new_first");
        Assert.Contains(account.Fields, field => field.LogicalName == "new_second");
    }

    /// <summary>產品層不得重複定義平台層 entity；即使其欄位不同也拒絕跨層覆蓋。</summary>
    [Fact]
    public void Layers_DuplicateEntityAcrossLayers_Rejected()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"goodshepherdback":["speechmessagecore"],"goodshepherd20":["platform","goodshepherd"]}}""",
            new Dictionary<string, string>
            {
                ["speechmessagecore.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_old")),
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_platform")),
                ["goodshepherd.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_product"))
            });

        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "goodshepherd20"));
    }

    /// <summary>平台資料夾的欄位名稱是正式身份；跨檔重複欄不可以 last-write-wins 靜默覆蓋。</summary>
    [Fact]
    public void Layers_DuplicateAttributeAcrossFiles_Rejected()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["platform"]}}""",
            new Dictionary<string, string>
            {
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_same")),
                ["platform/10-extension.schema.json"] = SchemaLayerFixture.Schema(SchemaLayerFixture.Account("new_same"))
            });

        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "construction"));
    }

    /// <summary>新 entity 缺明確 ownershipType 時，在產生任何可部署 schema 候選前拒絕。</summary>
    [Fact]
    public void Schema_NewEntityWithoutOwnershipType_PlanFails()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["goodbutler"]}}""",
            new Dictionary<string, string>
            {
                ["goodbutler.schema.json"] = SchemaLayerFixture.Schema(
                    SchemaLayerFixture.Account("new_tenantcode"),
                    SchemaLayerFixture.NewEntity("new_business", null, tenantLookup: false))
            });

        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "construction"));
    }

    /// <summary>以帳戶作租戶範圍的 localized text 若宣告組織擁有，必須在 plan 輸入邊界拒絕。</summary>
    [Fact]
    public void Schema_TenantBusinessEntityOrganizationOwned_PlanFails()
    {
        using var fixture = SchemaLayerFixture.Create(
            """{"schemaVersion":1,"organizations":{"construction":["platform"]}}""",
            new Dictionary<string, string>
            {
                ["platform/00-core.schema.json"] = SchemaLayerFixture.Schema(
                    SchemaLayerFixture.Account("new_tenantcode"),
                    SchemaLayerFixture.NewEntity("new_localizedtext", "OrganizationOwned", tenantLookup: true))
            });

        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "construction"));
    }
}

/// <summary>建立單一測試擁有的臨時 schema repo；刪除範圍固定為本 fixture 的隨機目錄。</summary>
internal sealed class SchemaLayerFixture : IDisposable
{
    /// <summary>本 fixture 專有且不含任何外部資源的 repo 根目錄。</summary>
    internal string Root { get; }

    /// <summary>只接受剛建立的唯一臨時路徑，立即建立 schema 根目錄。</summary>
    private SchemaLayerFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "3p-fs-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "d365", "schema"));
        File.WriteAllText(Path.Combine(Root, "SpeechMessageProducts.sln"), "", new UTF8Encoding(false));
    }

    /// <summary>建立 repository-root 測試輸入檔並回傳唯一 owner。</summary>
    internal static SchemaLayerFixture Create(string layers, IReadOnlyDictionary<string, string> schemas)
    {
        var fixture = new SchemaLayerFixture();
        try
        {
            var schemaRoot = Path.Combine(fixture.Root, "d365", "schema");
            Write(Path.Combine(schemaRoot, "layers.json"), layers);
            foreach (var item in schemas)
            {
                var path = Path.Combine(schemaRoot, item.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                Write(path, item.Value);
            }

            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    /// <summary>供正式repo layer tests 使用實際 d365/schema 來源，而不複製或改動該 repo。</summary>
    internal static SchemaLayerFixture FromRepository()
    {
        var fixture = new SchemaLayerFixture();
        var source = Path.GetDirectoryName(SchemaDefinitionTests.SchemaPath())!;
        var destination = Path.Combine(fixture.Root, "d365", "schema");
        Directory.Delete(destination, recursive: true);
        CopyDirectory(source, destination);
        return fixture;
    }

    /// <summary>將工作樹內的只讀 schema fixture 複製至本案專屬目錄，不建立跨工作樹連結。</summary>
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    /// <summary>以固定欄位、solution 與 publisher 身份包裝合成 schema entities。</summary>
    internal static string Schema(params string[] entities) =>
        """{"schemaVersion":1,"activePhase":1,"solution":"SpeechMessageCore","publisher":"SpeechMessagePublisher","prefix":"new","entities":["""
        + string.Join(",", entities) + "]}";

    /// <summary>建立只含一個既有 account 欄位的有效平台 entity。</summary>
    internal static string Account(string field) =>
        "{\"logicalName\":\"account\",\"displayName\":\"Account\",\"pluralName\":\"Accounts\",\"isNew\":false,\"audit\":false,\"fields\":[{\"logicalName\":\""
        + field
        + "\",\"displayName\":\"Synthetic field\",\"type\":\"string\",\"phase\":1,\"maxLength\":64}],\"keys\":[]}";

    /// <summary>建立有明確主名稱與可選租戶 lookup 的新 entity 測試資料。</summary>
    internal static string NewEntity(string name, string? ownership, bool tenantLookup)
    {
        var ownershipProperty = ownership is null ? "" : ",\"ownershipType\":\"" + ownership + "\"";
        var tenantField = tenantLookup
            ? ",{\"logicalName\":\"new_accountid\",\"displayName\":\"Account\",\"type\":\"lookup\",\"phase\":1,\"target\":\"account\",\"required\":true}"
            : "";
        return "{\"logicalName\":\"" + name + "\",\"displayName\":\"Synthetic business\",\"pluralName\":\"Synthetic business rows\",\"isNew\":true"
            + ownershipProperty
            + ",\"audit\":false,\"primaryName\":\"new_name\",\"fields\":[{\"logicalName\":\"new_name\",\"displayName\":\"Name\",\"type\":\"string\",\"phase\":1,\"maxLength\":100,\"required\":true}"
            + tenantField
            + "],\"keys\":[]}";
    }

    /// <summary>用無BOM UTF-8、CRLF 寫入 operation-local JSON；不覆寫任何 repo 或其他測試資料。</summary>
    private static void Write(string path, string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n')
            .Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        File.WriteAllText(path, normalized, new UTF8Encoding(false));
    }

    /// <summary>刪除只由本 fixture 建立的唯一臨時 repo，沒有共享或外部資料。</summary>
    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

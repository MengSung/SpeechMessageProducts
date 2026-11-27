/*
檔案整體說明
目的與定位：保護 F3 平台名額池清單及好牧人專屬生命週期欄位的正式分層輸入契約。
主要流程：使用既有 SchemaLayerFixture 將目前工作樹清單複製到本案的唯一臨時目錄，
經真正 SchemaFiles.LoadForOrganization 完整解析，核對產品欄位型別、組織擁有權、
資料庫唯一鍵、租戶關聯與產品隔離；不以 JSON 能讀取替代可部署候選的完整驗證。
故障注入：對複本加入未知 account 產品欄位或重複平台欄位，必須整體拒絕；
合法生命週期延伸則只在 goodshepherd20 存在，不能因此開放任意跨層實體合併。
資源與隔離：fixture 是臨時目錄唯一 owner，using 離開即清理，沒有背景工作、
User Secrets、SDK 連線或真實業務資料；所有判斷都是離線結果，不冒充三組織實機部署。
維護驗證：新增欄位／狀態或改鍵時，須同步本測試獨立列出的固定契約並重跑既有分層負向測試。
*/
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Schema;

/// <summary>以正式解析器驗證 F3 固定 schema；每案只修改自己的複本，沒有跨測試共用狀態。</summary>
public sealed class ChannelPoolSchemaTests
{
    /// <summary>三個產品都必須取得相同平台欄位與資料庫鍵；產品是精確字串，不可被誤建為選項。</summary>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public void ChannelPool_AllProducts_ContainExactPlatformContract(string organization)
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var schema = SchemaFiles.LoadForOrganization(fixture.Root, organization);
        var channel = Assert.Single(schema.Entities, entity => entity.LogicalName == "new_linechannel");
        var product = Assert.Single(channel.Fields, field => field.LogicalName == "new_product");
        Assert.Equal("string", product.Type);
        Assert.Equal(32, product.MaxLength);
        Assert.Equal<string>(
        [
            "new_actualrichmenucount", "new_capacity", "new_channelstatus", "new_channeltype",
            "new_defaultrichmenuid", "new_lastcheckedon", "new_monthlyusage", "new_poolorder",
            "new_product", "new_reservedrichmenus", "new_suspendedrichmenuid", "new_tenantrichmenuquota"
        ], channel.Fields.Where(field => field.LogicalName is not
            ("new_name" or "new_channelid" or "new_accesstokenencrypted" or
             "new_channelsecretencrypted" or "new_richmenucapacityused"))
            .Select(field => field.LogicalName).Order(StringComparer.Ordinal));
        Assert.Equal(Enumerable.Range(100000000, 5),
            channel.Fields.Single(field => field.LogicalName == "new_channelstatus").Options.Select(option => option.Value));
        foreach (var name in new[] { "new_channelslot", "new_slotallocationlock", "new_slotrequest" })
            Assert.Equal("OrganizationOwned", schema.Entities.Single(entity => entity.LogicalName == name).OwnershipType);
        var slot = schema.Entities.Single(entity => entity.LogicalName == "new_channelslot");
        Assert.True(slot.OptimisticConcurrency);
        Assert.Equal<string>(["new_channel", "new_slotnumber"], Assert.Single(slot.Keys).Attributes);
        Assert.Equal("account", slot.Fields.Single(field => field.LogicalName == "new_tenant").Target);
        Assert.Equal(Enumerable.Range(100000000, 5),
            slot.Fields.Single(field => field.LogicalName == "new_slotstatus").Options.Select(option => option.Value));
        var request = schema.Entities.Single(entity => entity.LogicalName == "new_slotrequest");
        Assert.True(request.OptimisticConcurrency);
        Assert.Equal<string>(["new_tenant", "new_operation", "new_correlationid"], Assert.Single(request.Keys).Attributes);
        Assert.Equal(Enumerable.Range(100000000, 6),
            request.Fields.Single(field => field.LogicalName == "new_result").Options.Select(option => option.Value));
        var allocationLock = schema.Entities.Single(entity => entity.LogicalName == "new_slotallocationlock");
        Assert.Equal<string>(["new_name"], Assert.Single(allocationLock.Keys).Attributes);
        Assert.Equal("integer", allocationLock.Fields.Single(field => field.LogicalName == "new_lockvalue").Type);
        Assert.Equal("new_channelslot", schema.Entities.Single(entity => entity.LogicalName == "account")
            .Fields.Single(field => field.LogicalName == "new_channelslot").Target);
    }

    /// <summary>平台 account 與好牧人層只可精確延伸四個生命週期欄位，非好牧人產品不得取得它們。</summary>
    [Theory]
    [InlineData("goodshepherd20", true)]
    [InlineData("construction", false)]
    [InlineData("association", false)]
    public void ChannelPool_Lifecycle_IsGoodShepherdOnly(string organization, bool expected)
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var schema = SchemaFiles.LoadForOrganization(fixture.Root, organization);
        var account = schema.Entities.Single(entity => entity.LogicalName == "account");
        foreach (var field in new[] { "new_lifecyclestatus", "new_trialenddate", "new_reserveuntil", "new_periodend" })
            Assert.Equal(expected, account.Fields.Any(item => item.LogicalName == field));
        if (expected)
            Assert.Equal(Enumerable.Range(100000000, 7),
                account.Fields.Single(field => field.LogicalName == "new_lifecyclestatus").Options.Select(option => option.Value));
    }

    /// <summary>核定的 account 特例不能變成任意欄位合併白名單；注入未知欄位後真正解析器必須拒絕。</summary>
    [Fact]
    public void ChannelPool_UnapprovedProductAccountExtension_Refused()
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var path = Path.Combine(fixture.Root, "d365", "schema", "goodshepherd.schema.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var account = document["entities"]!.AsArray().Single(node => node!["logicalName"]!.GetValue<string>() == "account")!;
        account["fields"]!.AsArray().Add(JsonNode.Parse(
            """{"logicalName":"new_unapproved","displayName":"未核定","type":"string","phase":1,"maxLength":64}"""));
        Write(path, document);
        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "goodshepherd20"));
    }

    /// <summary>產品層重複定義平台欄位必須拒絕，不能用後寫覆蓋或靜默去重使清單看起來合法。</summary>
    [Fact]
    public void ChannelPool_PlatformFieldRepeatedInProduct_Refused()
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var path = Path.Combine(fixture.Root, "d365", "schema", "goodshepherd.schema.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var account = document["entities"]!.AsArray().Single(node => node!["logicalName"]!.GetValue<string>() == "account")!;
        account["fields"]!.AsArray().Add(JsonNode.Parse(
            """{"logicalName":"new_istenant","displayName":"租戶","type":"choice","phase":1,"options":[{"value":100000000,"label":"否"},{"value":100000001,"label":"是"}]}"""));
        Write(path, document);
        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "goodshepherd20"));
    }

    /// <summary>局部延伸可省略主名稱欄，但完整組合仍必須包含它；移除唯一正式定義不得取得可部署候選。</summary>
    [Fact]
    public void ChannelPool_MergedNewEntityWithoutPrimaryField_Refused()
    {
        using var fixture = SchemaLayerFixture.FromRepository();
        var path = Path.Combine(fixture.Root, "d365", "schema", "platform", "00-core.schema.json");
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var channel = document["entities"]!.AsArray().Single(node => node!["logicalName"]!.GetValue<string>() == "new_linechannel")!;
        var fields = channel["fields"]!.AsArray();
        fields.Remove(fields.Single(node => node!["logicalName"]!.GetValue<string>() == "new_name"));
        Write(path, document);
        Assert.Throws<InvalidDataException>(() => SchemaFiles.LoadForOrganization(fixture.Root, "construction"));
    }

    /// <summary>只寫本案已擁有的臨時 JSON，維持與正式來源一致的 UTF-8 無 BOM／CRLF；不建立額外 I/O owner。</summary>
    private static void Write(string path, JsonObject document) =>
        File.WriteAllText(path, document.ToJsonString().Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n", new UTF8Encoding(false));
}

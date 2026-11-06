using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以附錄A的獨立欄位期望保護唯一結構清單；只讀repo定義與操作專屬暫存檔，不連D365或取得憑證。</summary>
public sealed class SchemaDefinitionTests
{
    /// <summary>附錄A.2獨立核對的正式語言來源數值，不從產品定義反推期望。</summary>
    private static readonly int[] LanguageSourceValues = { 100000000, 100000001, 100000002 };
    /// <summary>附錄A.6正式唯一鍵順序，不能由被測清單生成自身oracle。</summary>
    private static readonly string[] LocalizedKeyFields = { "new_accountid", "new_key", "new_culture" };
    /// <summary>正式清單須含六個受管實體、四新表與四唯一鍵；P4欄不得混入P1，完整日期／加密安全與lookup不可省略。</summary>
    [Fact]
    public void Load_FormalSchema_MatchesAppendixAAndPhaseBoundaries()
    {
        var schema = SchemaFiles.Load(SchemaPath());
        Assert.Equal("SpeechMessageCore", schema.Solution);
        Assert.Equal("new", schema.Prefix);
        Assert.Equal(6, schema.Entities.Count);
        Assert.Equal(4, schema.Entities.Count(entity => entity.IsNew));
        Assert.Equal(4, schema.Entities.Sum(entity => entity.Keys.Count));
        var account = schema.Entities.Single(entity => entity.LogicalName == "account");
        Assert.Equal(6, account.Fields.Count(field => field.Phase == 1));
        Assert.Equal(4, account.Fields.Single(field => field.LogicalName == "new_sinopacprofile").Phase);
        Assert.Equal("new_linechannel", account.Fields.Single(field => field.LogicalName == "new_linechannelid").Target);
        var contact = schema.Entities.Single(entity => entity.LogicalName == "contact");
        Assert.Equal(2, contact.Fields.Count);
        Assert.Equal(LanguageSourceValues, contact.Fields.Single(field => field.LogicalName == "new_languagesource").Options.Select(option => option.Value));
        var localized = schema.Entities.Single(entity => entity.LogicalName == "new_localizedtext");
        Assert.True(localized.Audit);
        Assert.Equal(LocalizedKeyFields, Assert.Single(localized.Keys).Attributes);
        Assert.Equal(2000, localized.Fields.Single(field => field.LogicalName == "new_text").MaxLength);
        var channel = schema.Entities.Single(entity => entity.LogicalName == "new_linechannel");
        Assert.All(channel.Fields.Where(field => field.LogicalName.EndsWith("encrypted", StringComparison.Ordinal)), field => Assert.True(field.Secured));
        var outbound = schema.Entities.Single(entity => entity.LogicalName == "new_outboundtask");
        Assert.True(outbound.OptimisticConcurrency);
        var dates = schema.Entities.SelectMany(entity => entity.Fields).Where(field => field.Type == "datetime").ToArray();
        Assert.Equal(4, dates.Length);
        Assert.All(dates, field => { Assert.Equal("UserLocal", field.Behavior); Assert.Equal("DateAndTime", field.Format); });
    }

    /// <summary>未知／重複欄位或JSON屬性、非法識別、錯phase、缺key欄及變更日期行為都須在SDK之前拒絕，不能靜默略過。</summary>
    /// <param name="fault">獨立注入的清單錯誤種類。</param>
    [Theory]
    [InlineData("unknown-property")]
    [InlineData("duplicate-property")]
    [InlineData("duplicate-entity")]
    [InlineData("duplicate-field")]
    [InlineData("invalid-name")]
    [InlineData("unknown-type")]
    [InlineData("invalid-phase")]
    [InlineData("missing-key-field")]
    [InlineData("wrong-date")]
    public void Load_InvalidDefinition_RejectsEntireCandidate(string fault)
    {
        var text = File.ReadAllText(SchemaPath(), Encoding.UTF8);
        var candidate = JsonNode.Parse(text)!.AsObject();
        var entity = candidate["entities"]![0]!.AsObject();
        var field = entity["fields"]![0]!.AsObject();
        switch (fault)
        {
            case "unknown-property": candidate["password"] = "synthetic-not-a-secret"; break;
            case "duplicate-property": text = text.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1", StringComparison.Ordinal); break;
            case "duplicate-entity": candidate["entities"]!.AsArray().Add(entity.DeepClone()); break;
            case "duplicate-field": entity["fields"]!.AsArray().Add(field.DeepClone()); break;
            case "invalid-name": field["logicalName"] = "new_field</xml>"; break;
            case "unknown-type": field["type"] = "unsupported"; break;
            case "invalid-phase": field["phase"] = 0; break;
            case "missing-key-field": candidate["entities"]![2]!["keys"]![0]!["attributes"]![0] = "new_missing"; break;
            case "wrong-date": entity["fields"]![4]!["behavior"] = "DateOnly"; break;
        }
        if (fault != "duplicate-property") text = candidate.ToJsonString();
        WithFile(text, path => Assert.ThrowsAny<Exception>(() => SchemaFiles.Load(path)));
    }

    /// <summary>讀取及hash只有結構定義；同檔兩次hash一致，任一正式欄位異動必須改hash，不把不可辨識的未知值納入計畫。</summary>
    [Fact]
    public void Hash_DefinitionChanges_InvalidateReviewedPlan()
    {
        var schema = SchemaFiles.Load(SchemaPath());
        var original = SchemaFiles.Hash(schema);
        Assert.Equal(64, original.Length);
        Assert.Equal(original, SchemaFiles.Hash(SchemaFiles.Load(SchemaPath())));
        var candidate = JsonNode.Parse(File.ReadAllText(SchemaPath(), Encoding.UTF8))!;
        candidate["entities"]![0]!["fields"]![0]!["maxLength"] = 17;
        WithFile(candidate.ToJsonString(), path => Assert.NotEqual(original, SchemaFiles.Hash(SchemaFiles.Load(path))));
    }

    /// <summary>固定找本worktree的方案根，不能回退到父worktree或讀其他分支的清單。</summary>
    internal static string SchemaPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException(), "d365", "schema", "speechmessagecore.schema.json");
    }

    /// <summary>每案唯一暫存檔由本方法擁有；寫入、驗證與故障皆在finally移除，不保留設定或開啟stream。</summary>
    private static void WithFile(string content, Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "p111-schema-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllText(path, content, new UTF8Encoding(false)); action(path); }
        finally { File.Delete(path); }
    }
}

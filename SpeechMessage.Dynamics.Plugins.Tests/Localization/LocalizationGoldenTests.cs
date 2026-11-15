/*
 * 檔案整體說明
 *
 * 目的與定位：P5-04 共用 Golden 的真外殼影子驗證，實讀 Core 測試使用的同一份
 * Golden/Legacy/localization/scenarios.json 與 expected.json，不複製六案期望或只測 Core。
 * 主要功能與流程：載入有界嚴格 JSON、建立正式 seed 別名與合成 SDK，依 trigger 執行
 * 三個真外殼及固定 UTC 時鐘；逐案核對寫入、換綁摘要、錯誤分類及完整 end。期望原始
 * 欄位經同一安全投影再比較，避免把機密或個資重新放進 trace；GUID 僅在測試邊界映回
 * seed／created 別名，不使用名稱去重。文字時間欄位被共用 recorder 省略，精確秒值另由
 * LocalizedTextChangedPluginTests 的真寫入測試保護，不冒稱影子驗到被排除的原始值。
 * 使用邊界與維護：檔案來源只向上尋找本工作樹的固定相對路徑，沒有任意外部輸入；
 * 每檔上限一 MiB、JSON 三十二層，reader/stream 由 using 關閉。每案 SDK、字典、recorder
 * 都同步排空後 Dispose，無秘密、網路或背景工作；本測試不代表 D365 實機驗收。
 * 共用 Golden 的 member outputs.alias/contact 另投影成 RichMenuAlias 摘要；lineCalls
 * 仍代表真 HTTP 呼叫，因此 Shadow 情境應為空，不把零 HTTP 誤當作零換綁意圖。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Localization;
using Xunit;
namespace SpeechMessage.Dynamics.Plugins.Tests.Localization;
/// <summary>與 Core 共用檔案來源，Golden 中每筆外殼情境都必須真正執行及比較。</summary>
public sealed class LocalizationGoldenTests
{
    /// <summary>完整讀取同組六案並執行真外殼；額外意圖、錯誤或缺結尾都不能被忽略。</summary>
    [Fact]
    public void Localization_ShadowMode_MatchesExpected()
    {
        var scenarios = Read("scenarios");
        var expected = Read("expected");
        Assert.Equal("localization", (string?)scenarios["module"]);
        Assert.Equal("new", (string?)expected["basis"]);
        var cases = Assert.IsType<JArray>(scenarios["cases"]).Cast<JObject>().ToArray();
        var desired = Assert.IsType<JArray>(expected["cases"]).Cast<JObject>().ToArray();
        Assert.Equal(6, cases.Length);
        Assert.Equal(cases.Length, desired.Length);
        Assert.Equal(cases.Length, cases.Select(row => Text(row, "name")).Distinct(StringComparer.Ordinal).Count());
        foreach (var scenario in cases)
            Run(scenario, Assert.Single(desired, row => Text(row, "name") == Text(scenario, "name")));
    }
    /// <summary>只以情境資料建構 SDK 及 context；產品邏輯仍由真外殼執行。</summary>
    private static void Run(JObject scenario, JObject desired)
    {
        using var fixture = new LocalizationFixture();
        var seeds = Assert.IsType<JArray>(scenario["seed"]).Cast<JObject>().ToArray();
        var ids = seeds.ToDictionary(row => Text(row, "alias"), row => Identity(Text(row, "alias")), StringComparer.Ordinal);
        var aliases = ids.ToDictionary(pair => pair.Value, pair => pair.Key);
        var rows = seeds.ToDictionary(row => ids[Text(row, "alias")], row =>
            Entity(Text(row, "entity"), ids[Text(row, "alias")], Assert.IsType<JObject>(row["fields"]), ids));
        // Golden 不保存密文；Shadow 來源以固定壞封包證明不會進入解密，沒有任何真 token。
        foreach (var row in rows.Values.Where(row => row.LogicalName == "new_linechannel"))
            row["new_accesstokenencrypted"] = "invalid-envelope-must-never-be-decrypted";
        fixture.Service.RetrieveHandler = (table, id, _) => rows.TryGetValue(id, out var row) && row.LogicalName == table
            ? row : throw new InvalidDataException("UnknownLocalizationIdentity");
        var trigger = Assert.IsType<JObject>(scenario["trigger"]);
        var type = Text(trigger, "type");
        var tableName = Text(trigger, "entity");
        var targetId = ids[Text(trigger, "target")];
        var context = new RemoteExecutionContext
        {
            Depth = (int?)trigger["depth"] ?? 1, Stage = 40, Mode = 1,
            MessageName = Text(trigger, "message"),
            PrimaryEntityName = tableName, PrimaryEntityId = targetId,
            OrganizationId = Guid.NewGuid(), OrganizationName = Text(scenario["church"]!, "organizationName"),
            UserId = Guid.NewGuid(), CorrelationId = Guid.NewGuid()
        };
        context.InputParameters["Target"] = Entity(tableName, targetId,
            Assert.IsType<JObject>(trigger["fields"]), ids);
        if (trigger["images"]?["pre"] is JObject images)
            foreach (var image in images.Properties())
                context.PreEntityImages[image.Name] = Entity(tableName, targetId, Assert.IsType<JObject>(image.Value), ids);
        var now = DateTimeOffset.Parse(Text(scenario, "now"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        IPlugin shell = type switch
        {
            "SpeechMessage.Dynamics.Plugins.Localization.LocalizedTextChangedPlugin" =>
                new LocalizedTextChangedPlugin("{\"mode\":\"Shadow\"}", "", () => now),
            "SpeechMessage.Dynamics.Plugins.Localization.MemberLanguageChangedPlugin" =>
                new MemberLanguageChangedPlugin("{\"mode\":\"Shadow\"}", ""),
            "SpeechMessage.Dynamics.Plugins.Localization.ChurchLocaleChangedPlugin" =>
                new ChurchLocaleChangedPlugin("{\"mode\":\"Shadow\"}", ""),
            _ => throw new InvalidDataException("UnknownLocalizationEntrypoint")
        };
        Assert.Equal(type, (string?)desired["entrypoint"]);
        var failure = Record.Exception(() => fixture.Execute(shell, context));
        var expectedException = (string?)desired["outputs"]?["shellException"] ??
            (desired["exception"]?.Type == JTokenType.String ? typeof(InvalidPluginExecutionException).FullName : null);
        Assert.Equal(expectedException, failure?.GetType().FullName);
        var intents = fixture.Intents();
        Assert.NotEmpty(intents);
        Assert.True((bool?)intents.Last()["end"] == true);
        Assert.Equal(intents.Length - 1, (int?)intents.Last()["count"]);
        Assert.All(intents, intent => Assert.Null(intent["overflow"]));
        var writes = new JArray();
        var calls = new JArray();
        var created = 0;
        foreach (var intent in intents.Take(intents.Length - 1))
        {
            if ((string?)intent["operation"] == "Push")
            {
                calls.Add(new JObject
                {
                    ["method"] = "POST", ["endpoint"] = "/v2/bot/message/push",
                    ["recipients"] = new JArray(aliases[Guid.Parse(Text(intent, "contactId"))]),
                    ["messageCount"] = intent["messageCount"]!.DeepClone(),
                    ["shadow"] = new JObject
                    {
                        ["templateKey"] = intent["templateKey"]!.DeepClone(),
                        ["retryKey"] = aliases[Guid.Parse(Text(intent, "retryKey"))],
                        ["contentSha256"] = intent["contentSha256"]!.DeepClone()
                    }
                });
                continue;
            }
            var id = Guid.Parse(Text(intent, "id"));
            if ((string?)intent["operation"] == "Create")
            {
                created++;
                var observed = Assert.IsType<JArray>(scenario["observe"]).Cast<JObject>()
                    .Where(row => row["related"] is not null && Text(row, "entity") == Text(intent, "entity")).ToArray();
                aliases.Add(id, observed.Length == 1 ? Text(observed[0], "alias") :
                    "created-" + created.ToString(CultureInfo.InvariantCulture));
            }
            var write = (JObject)intent.DeepClone();
            write.Remove("seq"); write.Remove("id"); write["alias"] = aliases[id];
            NormalizeReferences(write["fields"]!, aliases);
            writes.Add(write);
        }
        // 原始 Golden 欄位同樣經 recorder 的安全投影，完整比較其餘所有欄位及額外效果。
        var expectedWrites = new JArray();
        using var projection = new RecordingOrganizationService(fixture.Service);
        foreach (var expectedWrite in Assert.IsType<JArray>(desired["writes"]).Cast<JObject>())
        {
            var alias = Text(expectedWrite, "alias");
            var id = aliases.Single(pair => pair.Value == alias).Key;
            var expectedEntity = Entity(Text(expectedWrite, "entity"), id,
                Assert.IsType<JObject>(expectedWrite["fields"]), ids);
            if (Text(expectedWrite, "operation") == "Create") projection.Create(expectedEntity);
            else if (Text(expectedWrite, "operation") == "Update") projection.Update(expectedEntity);
            else throw new InvalidDataException("UnsupportedLocalizationGoldenWrite");
            var projected = RecordingOrganizationService.ReadProjection(projection.Writes[projection.Writes.Count - 1].ProjectionJson);
            projected.Remove("id"); projected["alias"] = alias;
            NormalizeReferences(projected["fields"]!, aliases);
            expectedWrites.Add(projected);
        }
        Assert.True(JToken.DeepEquals(expectedWrites, writes), "Localization writes mismatch: " + Text(scenario, "name") +
            " expected=" + expectedWrites + " actual=" + writes);
        var expectedCalls = (JArray)Assert.IsType<JArray>(desired["lineCalls"]).DeepClone();
        if (desired["outputs"]?["alias"] is JValue aliasValue)
        {
            // 共用檔將純規則 alias 放在 outputs；只轉換格式，不重新呼叫規則取得期望。
            Assert.Empty(expectedCalls);
            var contact = Text(desired["outputs"]!, "contact");
            Assert.True(ids.ContainsKey(contact));
            expectedCalls.Add(new JObject
            {
                ["method"] = "POST", ["endpoint"] = "/v2/bot/message/push",
                ["recipients"] = new JArray(contact), ["messageCount"] = 0,
                ["shadow"] = new JObject
                {
                    ["templateKey"] = "RichMenuAlias", ["retryKey"] = contact,
                    ["contentSha256"] = AliasHash(Assert.IsType<string>(aliasValue.Value))
                }
            });
        }
        Assert.True(JToken.DeepEquals(expectedCalls, calls), "Localization calls mismatch: " + Text(scenario, "name"));
        Assert.Empty(fixture.Service.WriteRecords);
        Assert.Equal(0, fixture.TaskCount);
    }
    /// <summary>把 JSON 純值及明確 lookup marker 轉為當案 Entity，不加入任何業務預設或驗證規則。</summary>
    private static Entity Entity(string table, Guid id, JObject fields, Dictionary<string, Guid> ids)
    {
        var row = new Entity(table, id);
        foreach (var field in fields.Properties())
        {
            if (field.Value is JObject marker)
            {
                var alias = (string?)marker["ref"] ?? (string?)marker["alias"];
                if (alias is not null) row[field.Name] = new EntityReference(Text(marker, "entity"), ids[alias]);
                else if (marker["line"] is not null) row[field.Name] = "U" + new string('a', 32);
                else if (marker["optionSet"] is not null) row[field.Name] = new OptionSetValue((int)marker["optionSet"]!);
                else if (marker["option"] is not null) row[field.Name] = new OptionSetValue((int)marker["option"]!);
                else throw new InvalidDataException("UnknownLocalizationValueMarker");
            }
            else row[field.Name] = field.Value.Type == JTokenType.Null ? null : ((JValue)field.Value).Value;
        }
        return row;
    }
    /// <summary>只將正式 lookup 的 id 轉為情境 alias，其他字串（包含來源鍵與摘要）原樣保留。</summary>
    private static void NormalizeReferences(JToken token, IReadOnlyDictionary<Guid, string> aliases)
    {
        if (token is JObject item)
        {
            if (item["entity"] is not null && item["id"] is JValue id && id.Value is string text &&
                Guid.TryParse(text, out var identity) && aliases.TryGetValue(identity, out var alias))
            { item.Remove("id"); item["alias"] = alias; }
            foreach (var child in item.Properties().ToArray()) NormalizeReferences(child.Value, aliases);
        }
        else if (token is JArray array)
            foreach (var child in array) NormalizeReferences(child, aliases);
    }
    /// <summary>只雜湊 Golden 已核定的 alias，與真外殼摘要比較；雜湊 owner 同步釋放。</summary>
    private static string AliasHash(string alias)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(alias))).Replace("-", string.Empty).ToLowerInvariant();
    }
    /// <summary>固定 Core 情境主鍵保持雙目標向量；其餘 alias 以無機密雜湊建立穩定測試身分。</summary>
    private static Guid Identity(string alias)
    {
        switch (alias)
        {
            case "church-a": return new Guid("11111111-2222-3333-4444-555555555555");
            case "contact-1": return new Guid("22222222-3333-4444-5555-666666666666");
            case "channel-a": return new Guid("33333333-4444-5555-6666-777777777777");
            default:
                using (var hash = SHA256.Create()) return new Guid(hash.ComputeHash(Encoding.UTF8.GetBytes(alias)).Take(16).ToArray());
        }
    }
    /// <summary>讀取必填字串，不猜測或清洗產品資料。</summary>
    private static string Text(JToken token, string field) => (string?)token[field]
        ?? throw new InvalidDataException("MissingLocalizationText");
    /// <summary>只讀本工作樹共用 Golden；限制檔案、層數、嚴格 UTF-8、重複鍵與尾隨 JSON。</summary>
    private static JObject Read(string file)
    {
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (var level = 0; level < 10 && directory is not null; level++, directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "SpeechMessageProducts.GoodShepherd.Core.Tests", "Golden", "Legacy", "localization", file + ".json");
            if (!File.Exists(path)) continue;
            using var stream = File.OpenRead(path);
            Assert.InRange(stream.Length, 1L, 1_048_576L);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            using var json = new JsonTextReader(reader)
            { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal, MaxDepth = 32 };
            var result = JObject.Load(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            Assert.False(json.Read());
            return result;
        }
        throw new FileNotFoundException("LocalizationGoldenNotFound");
    }
}

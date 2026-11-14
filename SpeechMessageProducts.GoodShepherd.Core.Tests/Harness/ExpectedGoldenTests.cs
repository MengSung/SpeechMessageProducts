/*
 * 檔案整體說明
 *
 * 目的與定位：保護 05A §4.2 的跨模組黃金差異契約，從組件嵌入資源讀取人寫情境、
 * 實際舊入口產出的 legacy 及人工審核 expected；本檔不執行或模擬舊業務規則。
 *
 * 主要功能與流程：逐模組要求三檔齊備，驗證情境與入口身分後，遞迴比較每個 JSON
 * 值與陣列位置。每個實際差異都須有唯一 case／JSONPath 宣告，類別限 D5-6 的
 * 整數 1 至 5，理由與對應測試不可空白；未宣告、過時或祖先路徑的宣告一律拒絕。
 * 合成正反例刻意注入欄位、型別、集合及宣告錯誤，與實際舊入口證據分開。
 *
 * 使用邊界與維護：資源只含合成代號，不讀秘密、不連 D365 或 LINE。每次測試擁有
 * 自己的 JSON 樹與有界嵌入資源 stream，reader 與 stream 在 using 中逆序釋放；
 * 沒有共享快取、背景工作、Session 或跨測試可變資料。差異錯誤只輸出路徑，不回顯
 * 業務值。名稱不充當資料列身分；同名不同 ID 的列按原陣列完整比較，不能去重。
 * 缺產物必須使整合測試失敗；離線守門通過不代表實機影子比對或業務規則已驗收。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Harness;

/// <summary>
/// 黃金檔的結構與逐值差異守門；合成測試驗證守門本身，嵌入資源測試驗證各模組實際產物。
/// 所有候選與比較集合只存活於一次測試，不保留服務、認證或呼叫端物件。
/// </summary>
public sealed class ExpectedGoldenTests
{
    /// <summary>
    /// 遍歷所有 Legacy 模組，缺任一固定三檔即失敗，不以 Skip 或空集合視為成功；
    /// 實際 JSON 差異必須與期望檔中經 D5-6 分類的逐位置宣告完全一對一。
    /// </summary>
    [Fact]
    public void Expected_DiffersFromLegacy_OnlyByDeclaredDifferences()
    {
        const string prefix = "SpeechMessageProducts.GoodShepherd.Core.Tests.Golden.Legacy.";
        var resources = typeof(ExpectedGoldenTests).Assembly.GetManifestResourceNames();
        var modules = resources.Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name.Substring(prefix.Length))
            .Select(name => name.Substring(0, name.LastIndexOf('.')))
            .Select(name => name.Substring(0, name.LastIndexOf('.')))
            .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(modules);
        foreach (var module in modules)
        {
            var scenarios = ReadResource(prefix + module + ".scenarios.json");
            var legacy = ReadResource(prefix + module + ".legacy.json");
            var expected = ReadResource(prefix + module + ".expected.json");
            Validate(scenarios, legacy, expected);
            Assert.Equal(module, (string?)legacy["module"]);
            if (string.Equals(module, "attendance", StringComparison.Ordinal))
                Assert.Contains((JArray)legacy["cases"]!, item =>
                    string.Equals((string?)item["name"], "attendance-reply-changed", StringComparison.Ordinal)
                    && ((JArray)item["lineCalls"]!).Count > 0);
        }
    }

    /// <summary>
    /// 五種核定類別在具有精確差異、理由及測試名稱時都可通過；這僅驗證分類機制，
    /// 不代替人工確認該模組的理由實際符合 D5-6 所列的業務例外。
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void DeclaredDifference_ExactLeafAndAllowedCategory_Accepted(int category)
    {
        var (scenarios, legacy, expected) = Fixture();
        expected["cases"]![0]!["outputs"]!["value"] = 2;
        expected["declaredDifferences"] = new JArray(Declaration("$.cases[0].outputs.value", category));
        Validate(scenarios, legacy, expected);
    }

    /// <summary>
    /// 注入缺宣告、多宣告、空說明、錯路徑與非法分類，必須全部拒絕；
    /// 同時保護 case 與入口身分，避免重排或換名繞過原本要比對的結果。
    /// </summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("reason")]
    [InlineData("test")]
    [InlineData("case")]
    [InlineData("position")]
    [InlineData("ancestor")]
    [InlineData("category-zero")]
    [InlineData("category-six")]
    [InlineData("category-string")]
    [InlineData("category-fraction")]
    [InlineData("entrypoint")]
    [InlineData("module")]
    [InlineData("case-name")]
    public void DeclaredDifference_InvalidContract_Rejected(string fault)
    {
        var (scenarios, legacy, expected) = Fixture();
        expected["cases"]![0]!["outputs"]!["value"] = 2;
        var declaration = Declaration("$.cases[0].outputs.value", 1);
        var declarations = new JArray(declaration);
        expected["declaredDifferences"] = declarations;
        switch (fault)
        {
            case "missing": declarations.Clear(); break;
            case "extra": declarations.Add(Declaration("$.cases[0].outputs.unchanged", 1)); break;
            case "duplicate": declarations.Add(declaration.DeepClone()); break;
            case "reason": declaration["reason"] = " \t"; break;
            case "test": declaration["test"] = ""; break;
            case "case": declaration["case"] = "unknown"; break;
            case "position": declaration["position"] = "$.cases[1].outputs.value"; break;
            case "ancestor": declaration["position"] = "$.cases[0].outputs"; break;
            case "category-zero": declaration["category"] = 0; break;
            case "category-six": declaration["category"] = 6; break;
            case "category-string": declaration["category"] = "1"; break;
            case "category-fraction": declaration["category"] = 1.5m; break;
            case "entrypoint": expected["cases"]![0]!["entrypoint"] = "other.Entry"; break;
            case "module": expected["module"] = "other"; break;
            case "case-name": expected["cases"]![0]!["name"] = "other"; break;
            default: throw new InvalidOperationException("Unknown synthetic fault.");
        }
        Assert.Throws<InvalidDataException>(() => Validate(scenarios, legacy, expected));
    }

    /// <summary>
    /// 新增、移除、null 與空集合、值型別及特殊欄位名稱都要逐位置辨識；
    /// 只有完全相符的路徑宣告才能通過，不能用整個 outputs 宣告遮蔽多個子值。
    /// </summary>
    [Theory]
    [InlineData("{'added':{'leaf':1}}", "{}", "$.cases[0].outputs.added.leaf")]
    [InlineData("{}", "{'added':{'leaf':1}}", "$.cases[0].outputs.added.leaf")]
    [InlineData("{'value':null}", "{}", "$.cases[0].outputs.value")]
    [InlineData("{'value':[]}", "{'value':null}", "$.cases[0].outputs.value")]
    [InlineData("{'value':[1,2]}", "{'value':[1]}", "$.cases[0].outputs.value[1]")]
    [InlineData("{'value':1}", "{'value':'1'}", "$.cases[0].outputs.value")]
    [InlineData("{'a.b':1}", "{'a.b':2}", "$.cases[0].outputs['a.b']")]
    public void LeafDifference_StructuralAndScalarChanges_RequireExactPosition(string before, string after, string path)
    {
        var (scenarios, legacy, expected) = Fixture();
        legacy["cases"]![0]!["outputs"] = JObject.Parse(before);
        expected["cases"]![0]!["outputs"] = JObject.Parse(after);
        Assert.Throws<InvalidDataException>(() => Validate(scenarios, legacy, expected));
        expected["declaredDifferences"] = new JArray(Declaration(path, 5));
        Validate(scenarios, legacy, expected);
    }

    /// <summary>
    /// 兩筆顯示名稱完全相同但 ID 不同的列必須保留；刪除第二列必須形成其 ID 與名稱
    /// 的兩個差異，而不能因名稱相同、內容相似或字典覆寫被靜默忽略。
    /// </summary>
    [Fact]
    public void SameNameDifferentIds_AreComparedWithoutMerging()
    {
        var (scenarios, legacy, expected) = Fixture();
        var rows = new JArray(
            new JObject { ["id"] = "contact-1", ["name"] = "Synthetic Member" },
            new JObject { ["id"] = "contact-2", ["name"] = "Synthetic Member" });
        legacy["cases"]![0]!["outputs"]!["rows"] = rows;
        expected["cases"]![0]!["outputs"]!["rows"] = rows.DeepClone();
        Validate(scenarios, legacy, expected);
        ((JArray)expected["cases"]![0]!["outputs"]!["rows"]!).RemoveAt(1);
        Assert.Throws<InvalidDataException>(() => Validate(scenarios, legacy, expected));
        expected["declaredDifferences"] = new JArray(
            Declaration("$.cases[0].outputs.rows[1].id", 5),
            Declaration("$.cases[0].outputs.rows[1].name", 5));
        Validate(scenarios, legacy, expected);
    }

    /// <summary>
    /// 同一情境的兩個完整入口必須分別保留；第二個入口輸出不同時，宣告位置必須指向
    /// 第二段結果，不能因 case 名稱相同就覆寫第一段或只比較其中一段。
    /// </summary>
    [Fact]
    public void SameCaseDifferentEntrypoints_AreComparedIndependently()
    {
        var (scenarios, legacy, expected) = Fixture();
        var second = (JObject)legacy["cases"]![0]!.DeepClone();
        second["entrypoint"] = "Synthetic.OtherEntry";
        ((JArray)legacy["cases"]!).Add(second);
        ((JArray)expected["cases"]!).Add(second.DeepClone());
        Validate(scenarios, legacy, expected);
        expected["cases"]![1]!["outputs"]!["value"] = 2;
        Assert.Throws<InvalidDataException>(() => Validate(scenarios, legacy, expected));
        expected["declaredDifferences"] = new JArray(Declaration("$.cases[1].outputs.value", 5));
        Validate(scenarios, legacy, expected);
    }

    /// <summary>
    /// 相同 case 可對應不同舊入口，但完全重複的 case／入口組合須拒絕；
    /// 缺結果、缺必要欄位、重複情境及基準私帶宣告均不得當成完整黃金。
    /// </summary>
    [Theory]
    [InlineData("duplicate-case")]
    [InlineData("missing-case")]
    [InlineData("missing-field")]
    [InlineData("duplicate-scenario")]
    [InlineData("legacy-declarations")]
    public void GoldenSchema_IncompleteOrAmbiguous_Rejected(string fault)
    {
        var (scenarios, legacy, expected) = Fixture();
        switch (fault)
        {
            case "duplicate-case": ((JArray)legacy["cases"]!).Add(legacy["cases"]![0]!.DeepClone()); break;
            case "missing-case": ((JArray)expected["cases"]!).Clear(); break;
            case "missing-field": ((JObject)expected["cases"]![0]!).Remove("writes"); break;
            case "duplicate-scenario": ((JArray)scenarios["cases"]!).Add(scenarios["cases"]![0]!.DeepClone()); break;
            case "legacy-declarations": legacy["declaredDifferences"] = new JArray(); break;
            default: throw new InvalidOperationException("Unknown synthetic fault.");
        }
        Assert.Throws<InvalidDataException>(() => Validate(scenarios, legacy, expected));
    }

    /// <summary>
    /// 驗證器逐 case 以原始索引組成標準 JSONPath；只接受剛好覆蓋實際差異的唯一宣告，
    /// 不解參照任意路徑、不修改輸入，也不把宣告內含的值當成舊程式證據。
    /// </summary>
    private static void Validate(JObject scenarios, JObject legacy, JObject expected)
    {
        Require(legacy["module"]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)legacy["module"]), "ModuleMissing");
        Require(JToken.DeepEquals(legacy["module"], expected["module"]), "ModuleMismatch");
        Require(legacy.Property("declaredDifferences", StringComparison.Ordinal) == null, "LegacyContainsDeclarations");
        Require(legacy.Properties().All(property => property.Name is "module" or "cases")
            && expected.Properties().All(property => property.Name is "module" or "cases" or "declaredDifferences"), "UnknownRootField");
        Require(scenarios["cases"] is JArray, "ScenariosMissing");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in (JArray)scenarios["cases"]!)
            Require(names.Add(Text(scenario["name"])), "DuplicateScenario");
        Require(names.Count > 0, "ScenariosEmpty");
        var before = Cases(legacy, names);
        var after = Cases(expected, names);
        Require(before.Count == after.Count, "CaseCountMismatch");
        var differences = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < before.Count; index++)
        {
            Require(JToken.DeepEquals(before[index]!["name"], after[index]!["name"])
                && JToken.DeepEquals(before[index]!["entrypoint"], after[index]!["entrypoint"]), "CaseIdentityMismatch");
            foreach (var path in Diff(before[index], after[index], "$.cases[" + index + "]"))
                differences.Add(path, Text(before[index]!["name"]));
        }
        Require(expected["declaredDifferences"] is JArray, "DeclarationsMissing");
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in (JArray)expected["declaredDifferences"]!)
        {
            Require(item is JObject, "DeclarationInvalid");
            var path = Text(item["position"]);
            var name = Text(item["case"]);
            _ = Text(item["reason"]);
            _ = Text(item["test"]);
            Require(item["category"]?.Type == JTokenType.Integer
                && item["category"]!.ToString(Formatting.None) is "1" or "2" or "3" or "4" or "5", "CategoryInvalid");
            Require(declared.Add(path), "DuplicateDeclaration:" + path);
            Require(differences.TryGetValue(path, out var actualCase)
                && string.Equals(actualCase, name, StringComparison.Ordinal), "UnexpectedDeclaration:" + path);
        }
        foreach (var path in differences.Keys)
            Require(declared.Contains(path), "UndeclaredDifference:" + path);
    }

    /// <summary>
    /// 要求每個情境至少一段完整結果，並以情境與完整入口共同檢查唯一性；
    /// outputs、寫入、LINE 呼叫及 exception 型別缺漏時拒絕，避免空殼資料冒充執行結果。
    /// </summary>
    private static JArray Cases(JObject document, HashSet<string> names)
    {
        Require(document["cases"] is JArray, "CasesMissing");
        var cases = (JArray)document["cases"]!;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cases)
        {
            Require(item is JObject, "CaseInvalid");
            var name = Text(item["name"]);
            var entry = Text(item["entrypoint"]);
            Require(names.Contains(name), "UnknownCase");
            Require(identities.Add(JsonConvert.SerializeObject(new[] { name, entry })), "DuplicateCaseEntry");
            seenNames.Add(name);
            Require(item["writes"] is JArray && item["lineCalls"] is JArray && item["outputs"] is JObject, "CaseFieldsMissing");
            Require(item["exception"]?.Type is JTokenType.Null or JTokenType.String, "ExceptionMissing");
        }
        Require(seenNames.SetEquals(names), "ScenarioCoverageMissing");
        return cases;
    }

    /// <summary>
    /// 物件依屬性名稱、陣列依原始位置走訪；不存在與 JSON null 不相等，空容器也形成
    /// 可診斷位置。所有集合只在此次比較保留，不按顯示文字排序、合併或刪除資料列。
    /// </summary>
    private static IEnumerable<string> Diff(JToken? before, JToken? after, string path)
    {
        if (JToken.DeepEquals(before, after)) yield break;
        if ((before is JObject || before == null) && (after is JObject || after == null))
        {
            var keys = ((before as JObject)?.Properties().Select(property => property.Name) ?? Enumerable.Empty<string>())
                .Concat((after as JObject)?.Properties().Select(property => property.Name) ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (keys.Length == 0) yield return path;
            foreach (var key in keys)
            {
                var segment = key.Length > 0 && (char.IsLetter(key[0]) || key[0] == '_')
                    && key.All(character => char.IsLetterOrDigit(character) || character == '_')
                    ? "." + key : "['" + string.Concat(key.Select(character => character switch
                    {
                        '\\' => "\\\\",
                        '\'' => "\\'",
                        _ => character.ToString(),
                    })) + "']";
                foreach (var difference in Diff(before?[key], after?[key], path + segment)) yield return difference;
            }
        }
        else if ((before is JArray || before == null) && (after is JArray || after == null))
        {
            var length = Math.Max((before as JArray)?.Count ?? 0, (after as JArray)?.Count ?? 0);
            if (length == 0) yield return path;
            for (var index = 0; index < length; index++)
                foreach (var difference in Diff(index < ((before as JArray)?.Count ?? 0) ? before![index] : null,
                    index < ((after as JArray)?.Count ?? 0) ? after![index] : null, path + "[" + index + "]"))
                    yield return difference;
        }
        else yield return path;
    }

    /// <summary>讀取非空文字欄位；格式錯誤僅回報固定代碼，避免把值寫進測試診斷。</summary>
    private static string Text(JToken? token)
    {
        Require(token?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)token), "TextMissing");
        return (string)token!;
    }

    /// <summary>統一 fail closed，讓合成反例能精確斷言契約拒絕，而非誤收 NullReference 等錯誤。</summary>
    private static void Require(bool condition, string code)
    {
        if (!condition) throw new InvalidDataException(code);
    }

    /// <summary>
    /// 每次獨占開啟單一嵌入檔，最多 4 MiB；嚴格 UTF-8、拒重複鍵且保留日期原文及 decimal。
    /// reader、JSON reader 與 stream 都由本方法釋放，缺檔必失敗，不讀檔案系統替代來源。
    /// </summary>
    private static JObject ReadResource(string name)
    {
        using var stream = typeof(ExpectedGoldenTests).Assembly.GetManifestResourceStream(name);
        Require(stream != null, "MissingEmbeddedResource:" + name);
        Require(stream!.Length <= 4 * 1024 * 1024, "GoldenResourceTooLarge");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        using var json = new JsonTextReader(reader) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal, MaxDepth = 64 };
        var document = JObject.Load(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        Require(!json.Read(), "TrailingJsonContent");
        return document;
    }

    /// <summary>建立獨立的合成基準與期望樹；只供驗證器故障注入，絕不發布成 legacy.json。</summary>
    private static (JObject Scenarios, JObject Legacy, JObject Expected) Fixture()
    {
        var scenarios = new JObject { ["cases"] = new JArray(new JObject { ["name"] = "synthetic" }) };
        var legacy = new JObject
        {
            ["module"] = "synthetic",
            ["cases"] = new JArray(new JObject
            {
                ["name"] = "synthetic", ["entrypoint"] = "Synthetic.Entry",
                ["writes"] = new JArray(), ["lineCalls"] = new JArray(),
                ["outputs"] = new JObject { ["value"] = 1, ["unchanged"] = true }, ["exception"] = null,
            }),
        };
        var expected = (JObject)legacy.DeepClone();
        expected["declaredDifferences"] = new JArray();
        return (scenarios, legacy, expected);
    }

    /// <summary>建立合成宣告；實際模組必須另填真實刻意差異理由與保護該差異的具名測試。</summary>
    private static JObject Declaration(string position, int category) => new()
    {
        ["case"] = "synthetic", ["position"] = position, ["category"] = category,
        ["reason"] = "合成故障注入，只驗證差異契約。", ["test"] = nameof(DeclaredDifference_ExactLeafAndAllowedCategory_Accepted),
    };
}

/*
檔案整體說明
目的與定位：組合 P3-01 路由黃金候選，保留主副本的真實意圖、大小寫缺陷、原字面詞、來源與靜態回覆契約。
主要功能與流程：先以真正舊來源編譯，列出全部實際比較，再為各不同字面詞建立原文、空白、全形與大小寫變形。逐筆比較原版與觀察版結果，最後才形成固定順序 JSON；統計由實際規則計算，不硬湊計畫中的一百零三。
使用邊界與維護：本次來源最多十個各一 MiB、判斷最多五百一十二、輸入每筆四千字；全流程為呼叫區域候選，沒有共享可變快取。編譯 owner 在 finally/using 卸載；回傳 JSON 由呼叫者獨占。來源為已審核程式而非真實訊息，仍拒絕私人識別樣式或替代字元；結果只代表 matcher 執行及靜態資料委派，沒有 CRM、LINE 實機保證。
*/
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LegacyGolden;

/// <summary>離線黃金資料候選的純組合入口；驗證全部完成前不碰輸出路徑。</summary>
internal static class GoldenGenerator
{
    /// <summary>從完整來源集建立可重生候選；缺少來源、重複身分或追蹤與原執行不一致均失敗。</summary>
    internal static JsonObject Generate(IReadOnlyList<SourceDocument> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count != LegacySourceReader.AllowedPaths.Length || sources.Select(source => source.Path).Distinct(StringComparer.Ordinal).Count() != sources.Count ||
            sources.Any(source => !LegacySourceReader.AllowedPaths.Contains(source.Path, StringComparer.Ordinal) || source.Text.Length > 1024 * 1024))
            throw new InvalidDataException("Expected the exact source allowlist.");
        var compilation = LegacyCompiler.Compile(sources);
        using var original = LegacyCompiler.OpenOriginal(compilation);
        var rules = RuleCatalog.Extract(compilation);
        using var traced = LegacyCompiler.OpenTraced(compilation, rules);
        var words = rules.Select(rule => rule.Keyword).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var cases = new JsonArray();
        foreach (var word in words)
            foreach (var variant in Variants(word)) AddCase(word, variant.Name, variant.Input);
        AddCase(null, "empty", "");
        AddCase(null, "whitespace-only", " \t\u3000 ");
        AddCase(null, "unknown", "synthetic-unmatched-input");
        var sourcesJson = new JsonArray(sources.OrderBy(source => source.Path, StringComparer.Ordinal).Select(source => (JsonNode?)new JsonObject
        {
            ["copy"] = "main", ["path"] = source.Path, ["originalEncoding"] = "cp950",
            ["originalSha256"] = source.OriginalSha256, ["convertedSha256"] = source.ConvertedSha256,
            ["originalLineCount"] = source.LineCount, ["convertedLineCount"] = source.LineCount,
            ["cp950ByteRoundTrip"] = true, ["originalUnchanged"] = true,
            ["use"] = source.Path is LegacySourceReader.RoutingPath or LegacySourceReader.MatcherPath ? "compiled" : "static-only"
        }).ToArray());
        var output = new JsonObject
        {
            ["schemaVersion"] = 1, ["sourceCopy"] = "main", ["execution"] = "compiled-original-source",
            ["syntheticInputs"] = true, ["externalServicesExecuted"] = false,
            ["compiledFiles"] = new JsonArray(LegacySourceReader.RoutingPath, LegacySourceReader.MatcherPath), ["stubs"] = new JsonArray(),
            ["statistics"] = new JsonObject
            {
                ["decisionItems"] = rules.Count, ["distinctLiterals"] = words.Length,
                ["ordinalIgnoreCaseLiterals"] = words.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                ["literalsContainingHan"] = words.Count(word => word.Any(character => character is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff')),
                ["caseCount"] = cases.Count
            },
            ["sources"] = sourcesJson,
            ["rules"] = new JsonArray(rules.Select(rule => (JsonNode?)new JsonObject
            {
                ["id"] = rule.Id, ["stage"] = rule.Stage, ["method"] = rule.Method, ["keyword"] = rule.Keyword,
                ["declaredIntent"] = rule.DeclaredIntent, ["source"] = new JsonObject { ["path"] = rule.Path, ["line"] = rule.Line, ["endLine"] = rule.EndLine }
            }).ToArray()),
            ["replies"] = ReplyCatalog.Extract(sources), ["cases"] = cases
        };
        var serialized = Serialize(output);
        if (Regex.IsMatch(serialized, "(?<![A-Za-z0-9])[UCR][0-9a-fA-F]{32}(?![A-Za-z0-9])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) || serialized.Contains('\uFFFD', StringComparison.Ordinal))
            throw new InvalidDataException("Private identifier or replacement character in output.");
        return output;

        // 觀察只用來建立追溯；意圖永遠採未修改的真實來源，二者不同立即拒絕。
        void AddCase(string? keyword, string variant, string input)
        {
            if (input.Length > 4096) throw new InvalidDataException("Synthetic input exceeds limit.");
            var actual = original.Run(input);
            var observation = traced.Run(input);
            if (!string.Equals(actual[0], observation[0], StringComparison.Ordinal)) throw new InvalidDataException("Observer changed legacy behavior.");
            var rule = observation[1] == "Unknown" ? null : observation[1] == "Helper"
                ? rules.Single(item => item.Id == observation[2])
                : rules.First(item => item.Stage == observation[1] && item.Keyword == observation[2] && item.DeclaredIntent == actual[0]);
            cases.Add(new JsonObject
            {
                ["id"] = FormattableString.Invariant($"case-{cases.Count + 1:D4}"), ["keyword"] = keyword, ["variant"] = variant,
                ["input"] = input, ["actualIntent"] = actual[0], ["stage"] = observation[1], ["ruleId"] = rule?.Id,
                ["replyContract"] = string.IsNullOrWhiteSpace(input) ? "$emptyInput" : actual[0], ["dispatchDescription"] = "{original-input}",
                ["observationAgreesWithOriginal"] = true
            });
        }
    }

    /// <summary>固定序列與明確 invariant 變形；不對原 matcher 偷加全形正規化，也保留相同輸入的不同測試意圖。</summary>
    internal static IEnumerable<(string Name, string Input)> Variants(string word)
    {
        yield return ("literal", word);
        yield return ("leading-space", " \t" + word);
        yield return ("trailing-space", word + " \t");
        yield return ("both-spaces", "\u3000 " + word + " \u3000");
        yield return ("full-width", new string(word.Select(character => character is >= '!' and <= '~' ? (char)(character + 0xfee0) : character == ' ' ? '\u3000' : character).ToArray()));
        yield return ("upper-invariant", word.ToUpperInvariant());
        yield return ("lower-invariant", word.ToLowerInvariant());
    }

    /// <summary>可見繁中文字元、固定 CRLF 與末尾換行；每次新建 options，不共享可變 serializer 設定。</summary>
    internal static string Serialize(JsonNode node) => node.ToJsonString(new JsonSerializerOptions
        { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).ReplaceLineEndings("\r\n") + "\r\n";
}

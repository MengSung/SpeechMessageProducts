/*
檔案整體說明
目的與定位：定義 .NET 10／11 研究的固定合成輸入與真實產品呼叫接縫，量測驗簽、正式事件解析、Flex、系統文字、會友樹及 Newtonsoft 序列化；不載入 ChurchReport Host 或任何正式資料。
主要功能與流程：建構時建立有限且唯一的 ID、設定每案例輸入與輸出 SHA256，之後只重用唯讀輸入。事件解析只在計時外反射綁定一次私有 ParseEvents 委派，實際每次執行正式方法；樹 builder 與 DTO 由原始檔快照直接編譯，未改演算法。
使用邊界與維護：各次輸出只活到當次委派返回，不保存請求或輸出集合。合成 catalog 只代理固定系統表，沒有教會覆寫，不能代表完整文字授權路徑。正確性先斷言輸出及拒絕，再計時；父程序比較跨 runtime 的 hash，不能以相等 hash 代替全面相容性驗收。
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChurchReport.Services.MemberInfo;
using LineMessagingProcessor.AspNetCore.Webhooks;
using LineMessagingProcessor.AspNetCore.Webhooks.Signatures;
using LineMessagingProcessor.Core.Templates;
using Newtonsoft.Json;
using SpeechMessage.Localization;
using SpeechMessageProducts.LineWebhook;

/// <summary>有限案例的輸入 owner；建構完成後不再改寫輸入，每次呼叫的候選與輸出由本次操作擁有。</summary>
internal sealed class Workloads
{
    /// <summary>名稱、計時入口及非計時 hash；輸出 hash 用來偵測跨 runtime 結果差異。</summary>
    internal sealed record Case(string Name, Func<int> Run, string InputHash, string OutputHash);
    /// <summary>固定且唯讀的案例清單，不因計時結果增刪案例。</summary>
    internal readonly List<Case> Cases = new();
    /// <summary>一次綁定正式 ParseEvents，反射查找不納入測量；delegate 每次都執行完整正式解析。</summary>
    internal static readonly Func<string, ReadOnlyMemory<byte>, List<WebhookEnvelope>> Parse =
        typeof(LineWebhookEndpoints).GetMethod("ParseEvents", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<string, ReadOnlyMemory<byte>, List<WebhookEnvelope>>>();
    private readonly LineWebhookSignatureVerifier verifier = new(new SyntheticResolver());

    /// <summary>生成固定規模資料；沒有網路、檔案、隨機 ID 或時間相依輸入。</summary>
    internal Workloads()
    {
        foreach (var size in new[] { 1, 32 })
        {
            var body = Body(size, "10001");
            var signature = Sign(body, "10001");
            Add("signature_" + size, () => verifier.VerifyAsync("10001", body, signature, default).GetAwaiter().GetResult() ? 1 : 0,
                Encoding.UTF8.GetString(body), "true");
            var parsed = Parse("10001", body);
            var canonical = string.Join("|", parsed.Select(e => e.Event.GetRawText()));
            Add("parse_" + size, () => Parse("10001", body).Count, Encoding.UTF8.GetString(body), canonical);
        }
        var table = SystemTextTable.FromRows(new[] { "zh-TW", "en-US" }, new[]
        {
            new string?[] { "Bench.Message", "研究用訊息", "Research message" },
            new string?[] { "Bench.Format", "{name} 本次數量 {count}", "{name} quantity {count}" }
        });
        var key = new TextKey("Bench.Message");
        Add("text_lookup", () => table.GetText("zh-TW", key).Length, "zh-TW|Bench.Message", table.GetText("zh-TW", key));
        var formatKey = new TextKey("Bench.Format");
        var arguments = new[] { new TextArg("name", "合成使用者"), new TextArg("count", 12345) };
        Add("text_format", () => table.GetFormattedText("zh-TW", formatKey, arguments).Length,
            "zh-TW|Bench.Format|合成使用者|12345", table.GetFormattedText("zh-TW", formatKey, arguments));
        var engine = new FlexTemplateEngine(new SyntheticCatalog(table));
        var context = new TextContext(Guid.Parse("11111111-1111-1111-1111-111111111111"), "zh-TW", "zh-TW");
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "合成資料 A \"quote\"" };
        foreach (var count in new[] { 1, 20 })
        {
            var text = string.Join(",", Enumerable.Repeat("{\"type\":\"text\",\"text\":\"{{v:name}}\"}", count));
            var template = "{\"type\":\"flex\",\"altText\":\"{{t:Bench.Message}}\",\"contents\":{\"type\":\"bubble\",\"body\":{\"type\":\"box\",\"layout\":\"vertical\",\"contents\":[" + text + "]}}}";
            var filled = engine.Fill(template, context, values);
            Add("flex_" + count, () => engine.Fill(template, context, values).AltText.Length, template,
                JsonConvert.SerializeObject(filled));
        }
        foreach (var count in new[] { 100, 1000 })
        {
            var groups = Enumerable.Range(0, count).Select(i => new SmallGroupDescriptor
            {
                ListId = $"group-{i:D5}", GroupName = $"小組 {i % 50:D2}", RaceLeaderKey = $"district-{i % 10:D2}",
                RaceLeaderName = $"區長 {i % 10:D2}", AreaName = $"牧區 {i % 3}", LeaderName = "合成組長",
                GroupTime = "週六", GroupPlace = "合成地點"
            }).ToArray();
            var members = Enumerable.Range(0, count * 20).Select(i => new GroupMembershipRow
            { ListId = groups[i / 20].ListId, ContactId = $"contact-{i:D6}", IsCurrent = true }).ToArray();
            var tree = DistrictTreeBuilder.Build(groups, members, count * 20 + 10, true, "synthetic-scope");
            if (tree.Districts.Sum(d => d.MemberCount) != count * 20
                || tree.Districts.Sum(d => d.GroupCount) != count || tree.Ungrouped!.MemberCount != 10)
                throw new InvalidOperationException("Tree count oracle failed.");
            Add("tree_" + count, () => DistrictTreeBuilder.Build(groups, members, count * 20 + 10, true, "synthetic-scope").Districts.Count,
                JsonConvert.SerializeObject(new { groups, members }), JsonConvert.SerializeObject(tree));
        }
        var rows = Enumerable.Range(0, 1000).Select(i => new
        { ContactId = $"synthetic-contact-{i:D5}", FullName = "同名合成資料", Phone = "synthetic", Area = i % 10, Current = true }).ToArray();
        Add("newtonsoft_rows_1000", () => JsonConvert.SerializeObject(rows).Length,
            JsonConvert.SerializeObject(rows), JsonConvert.SerializeObject(rows));
    }

    /// <summary>正確與竄改簽章、未知 scope、解析身分、語系與相同顯示名稱均有決定性斷言；任何失敗禁止輸出效能結果。</summary>
    internal void AssertCorrectness()
    {
        var body = Body(1, "10001");
        if (!verifier.VerifyAsync("10001", body, Sign(body, "10001"), default).Result
            || verifier.VerifyAsync("10001", body, Sign(body, "10002"), default).Result
            || verifier.VerifyAsync("99999", body, Sign(body, "10001"), default).Result)
            throw new InvalidOperationException("Signature oracle failed.");
        var tampered = body.ToArray();
        tampered[10] ^= 1;
        if (verifier.VerifyAsync("10001", tampered, Sign(body, "10001"), default).Result)
            throw new InvalidOperationException("Tampered body accepted.");
        foreach (var channel in new[] { "10001", "10002" })
        {
            var parsed = Parse(channel, Body(32, channel));
            if (parsed.Count != 32 || parsed.Select(e => e.WebhookEventId).Distinct(StringComparer.Ordinal).Count() != 32
                || parsed.Any(e => e.ChannelId != channel || e.Event.GetProperty("source").GetProperty("userId").GetString() != "synthetic-user-" + channel))
                throw new InvalidOperationException("Parse oracle failed.");
        }
        var duplicate = Encoding.UTF8.GetBytes("{\"events\":[" + Event(0, "10001") + "," + Event(0, "10001") + "]}");
        var rejected = false;
        try { Parse("10001", duplicate); } catch (System.Text.Json.JsonException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Duplicate stable identity accepted.");
        var table = SystemTextTable.FromRows(new[] { "zh-TW", "en-US" }, new[]
        { new string?[] { "Bench.Message", "甲", "A" } });
        var key = new TextKey("Bench.Message");
        if (table.GetText("zh-TW", key) != "甲" || table.GetText("en-US", key) != "A" || table.GetText("zh-TW", key) != "甲")
            throw new InvalidOperationException("Language isolation failed.");
    }

    /// <summary>以 SHA256 凍結非計時輸入／輸出；不將 checksum 計算成本混入純函式案例。</summary>
    private void Add(string name, Func<int> run, string input, string output) =>
        Cases.Add(new Case(name, run, Hash(input), Hash(output)));
    /// <summary>固定 UTF-8 內容雜湊，只作用於合成資料。</summary>
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    /// <summary>固定一或三十二個事件，所有 stable ID 唯一；reject 僅供合成 queue 拒絕注入。</summary>
    internal static byte[] Body(int count, string channel, bool reject = false) =>
        Encoding.UTF8.GetBytes("{\"destination\":\"synthetic\",\"events\":[" +
            string.Join(",", Enumerable.Range(0, count).Select(i => Event(i, channel, reject))) + "]}");
    /// <summary>每個事件約三百 bytes、使用固定合成 ID；不同使用者顯示內容可以相同而身分仍獨立。</summary>
    internal static string Event(int index, string channel, bool reject = false) =>
        "{\"webhookEventId\":\"" + (reject ? "reject" : $"event-{index:D5}") +
        "\",\"type\":\"message\",\"timestamp\":1700000000000,\"replyToken\":\"synthetic-reply\",\"deliveryContext\":{\"isRedelivery\":false}," +
        "\"source\":{\"type\":\"user\",\"userId\":\"synthetic-user-" + channel +
        "\"},\"message\":{\"id\":\"message-" + index + "\",\"type\":\"text\",\"text\":\"研究用合成訊息，不含真實個資。\"}}";
    /// <summary>每次明確 Dispose HMAC；只用固定合成 secret，不記錄 key bytes。</summary>
    internal static string Sign(byte[] body, string channel)
    {
        new SyntheticResolver().TryResolveSecret(channel, out var secret);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret!));
        return Convert.ToBase64String(hmac.ComputeHash(body));
    }
    /// <summary>合成 catalog 只借用系統表，刻意不代表完整教會覆寫與授權流程。</summary>
    private sealed class SyntheticCatalog : ITextCatalog
    {
        private readonly SystemTextTable table;
        /// <summary>表為案例 owner 的唯讀值，catalog 不接管任何外部資源。</summary>
        internal SyntheticCatalog(SystemTextTable table) => this.table = table;
        /// <summary>格式化後即返回，不保留 context／args，語言由每次明確 context 決定。</summary>
        public string GetText(TextContext context, TextKey key, params TextArg[] args) =>
            table.GetFormattedText(context.TextCulture, key, args);
    }
}

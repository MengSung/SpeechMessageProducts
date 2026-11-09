/*
檔案整體說明
目的與定位：本檔保護 P3-01 舊行為黃金資料的離線交付契約，讓缺少路由或事件樣本時以真正資料斷言失敗。
主要功能與流程：由既有 Host 測試專案執行，讀取隨測試輸出複製的合成 JSON；核對路由來源、執行結果及十二類事件的必要欄位。後續產生器測試以合成舊來源證明真實編譯與執行，不連線 LINE 或 CRM。
使用邊界與維護：測試只借用唯讀 fixture，不讀舊方案設定或私人資料；每個 JsonDocument 由案例 using 擁有並釋放。缺檔、錯誤格式與缺少類別皆須明確失敗，不能用跳過或空集合假稱相容。這些結果不是實機 webhook 證據。
*/
using System.Text.Json;
using Line.Messaging.Webhooks;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>驗證已納管黃金樣本完整存在；不依賴外部舊方案、網路或殘留建置產物。</summary>
public sealed class LegacyGoldenTests
{
    /// <summary>本包核定的十二類事件名稱，為純測試常數且不持有任何使用者或執行狀態。</summary>
    private static readonly string[] ExpectedKinds = ["accountLink", "follow", "join", "leave", "memberJoined", "message.audio", "message.image", "message.location", "message.sticker", "message.text", "postback", "unfollow"];
    /// <summary>缺少路由產物時必須失敗；成功時須實際載入已執行來源的 metadata 與非空案例。</summary>
    [Fact]
    public void Routing_ContainsExecutedLegacyCasesAndSourceProvenance()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Golden", "Keywords", "routing.json");
        Assert.True(File.Exists(path), "Missing generated legacy routing fixture.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("compiled-original-source", document.RootElement.GetProperty("execution").GetString());
        Assert.NotEmpty(document.RootElement.GetProperty("cases").EnumerateArray());
        Assert.NotEmpty(document.RootElement.GetProperty("sources").EnumerateArray());
        var root = document.RootElement;
        var rules = root.GetProperty("rules").EnumerateArray().ToArray();
        var words = rules.Select(rule => rule.GetProperty("keyword").GetString()!).Distinct(StringComparer.Ordinal).ToArray();
        var statistics = root.GetProperty("statistics");
        Assert.Equal(rules.Length, statistics.GetProperty("decisionItems").GetInt32());
        Assert.Equal(words.Length, statistics.GetProperty("distinctLiterals").GetInt32());
        Assert.Equal(words.Distinct(StringComparer.OrdinalIgnoreCase).Count(), statistics.GetProperty("ordinalIgnoreCaseLiterals").GetInt32());
        Assert.Equal(words.Count(word => word.Any(character => character is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff')),
            statistics.GetProperty("literalsContainingHan").GetInt32());
        var ruleIds = rules.Select(rule => rule.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(rules.Length, ruleIds.Count);
        var sources = root.GetProperty("sources").EnumerateArray().ToDictionary(source => source.GetProperty("path").GetString()!, StringComparer.Ordinal);
        Assert.Equal(10, sources.Count);
        foreach (var source in sources.Values)
        {
            Assert.Matches("^[A-F0-9]{64}$", source.GetProperty("originalSha256").GetString()!);
            Assert.Matches("^[A-F0-9]{64}$", source.GetProperty("convertedSha256").GetString()!);
            Assert.Equal(source.GetProperty("originalLineCount").GetInt32(), source.GetProperty("convertedLineCount").GetInt32());
            Assert.True(source.GetProperty("cp950ByteRoundTrip").GetBoolean());
            Assert.True(source.GetProperty("originalUnchanged").GetBoolean());
        }
        foreach (var rule in rules)
        {
            var location = rule.GetProperty("source");
            Assert.InRange(location.GetProperty("line").GetInt32(), 1, location.GetProperty("endLine").GetInt32());
            Assert.InRange(location.GetProperty("endLine").GetInt32(), 1, sources[location.GetProperty("path").GetString()!].GetProperty("originalLineCount").GetInt32());
        }
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(words.Length * 7 + 3, cases.Length);
        Assert.Equal(cases.Length, cases.Select(item => item.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count());
        var replies = root.GetProperty("replies").GetProperty("intents");
        foreach (var item in cases)
        {
            Assert.True(item.GetProperty("observationAgreesWithOriginal").GetBoolean());
            Assert.True(replies.TryGetProperty(item.GetProperty("actualIntent").GetString()!, out _));
            if (item.GetProperty("ruleId").ValueKind != JsonValueKind.Null)
                Assert.Contains(item.GetProperty("ruleId").GetString()!, ruleIds);
        }
        foreach (var word in words)
            Assert.Equal(7, cases.Count(item => item.GetProperty("keyword").ValueKind == JsonValueKind.String && item.GetProperty("keyword").GetString() == word));
        Assert.Equal(28, replies.EnumerateObject().Count());
        Assert.Contains("您可以輸入以下的關鍵字:", replies.GetProperty("Help").GetProperty("fixedReplies")[0].GetProperty("textTemplate").GetString()!, StringComparison.Ordinal);
        Assert.Equal("您的 LINE ID: {lineId}", replies.GetProperty("QueryUserId").GetProperty("fixedReplies")[0].GetProperty("textTemplate").GetString());
        Assert.Contains(replies.GetProperty("Enrollment").GetProperty("delegations").EnumerateArray(),
            call => call.GetProperty("type").GetString() == "LessonProcessorClass" && call.GetProperty("method").GetString() == "DisplayAllActiveListAsync");
        Assert.Contains(replies.GetProperty("SundayAttendance").GetProperty("delegations").EnumerateArray(),
            call => call.GetProperty("type").GetString() == "SundayPresent" && call.GetProperty("method").GetString() == "SetSundayPresentAsync");
        Assert.Contains(replies.GetProperty("PrayerRequest").GetProperty("delegations").EnumerateArray(),
            call => call.GetProperty("type").GetString() == "PrayItem" && call.GetProperty("method").GetString() == "SetPrayItemAsync");
    }

    /// <summary>十二類官方形狀合成事件不得缺檔；逐份解析而非只以檔名數字宣稱事件已覆蓋。</summary>
    [Fact]
    public void Events_TwelveSyntheticRequestsHaveDistinctOfficialKinds()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Golden", "Events");
        Assert.True(Directory.Exists(directory), "Missing synthetic LINE event fixtures.");
        var paths = Directory.GetFiles(directory, "*.json");
        Assert.Equal(12, paths.Length);
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var item = Assert.Single(document.RootElement.GetProperty("events").EnumerateArray());
            var type = item.GetProperty("type").GetString()!;
            Assert.True(kinds.Add(type == "message" ? "message." + item.GetProperty("message").GetProperty("type").GetString() : type));
            Assert.False(item.GetProperty("deliveryContext").GetProperty("isRedelivery").GetBoolean());
            Assert.Equal(26, item.GetProperty("webhookEventId").GetString()!.Length);
            Assert.Matches("^[0-7][0-9A-HJKMNP-TV-Z]{25}$", item.GetProperty("webhookEventId").GetString()!);
            Assert.True(ids.Add(item.GetProperty("webhookEventId").GetString()!));
            Assert.Equal("active", item.GetProperty("mode").GetString());
            Assert.Equal(1735689600000L, item.GetProperty("timestamp").GetInt64());
            Assert.DoesNotContain(item.EnumerateObject(), property => property.Name == "synthetic");
            var parsed = Assert.Single(WebhookEventParser.Parse(File.ReadAllText(path)));
            Assert.Equal(item.GetProperty("webhookEventId").GetString(), parsed.WebhookEventId);
            Assert.Equal(type, parsed.Type.ToString(), ignoreCase: true);
            if (type == "message")
            {
                var message = item.GetProperty("message");
                Assert.Equal(message.GetProperty("type").GetString(), Assert.IsType<MessageEvent>(parsed).Message.Type.ToString(), ignoreCase: true);
                switch (message.GetProperty("type").GetString())
                {
                    case "text": Assert.Equal("synthetic-text", message.GetProperty("text").GetString()); break;
                    case "image": Assert.Equal("line", message.GetProperty("contentProvider").GetProperty("type").GetString()); break;
                    case "audio": Assert.Equal(1000, message.GetProperty("duration").GetInt32()); break;
                    case "location": Assert.Equal(0, message.GetProperty("latitude").GetInt32()); Assert.Equal(0, message.GetProperty("longitude").GetInt32()); break;
                    case "sticker": Assert.Equal("STATIC", message.GetProperty("stickerResourceType").GetString()); break;
                }
            }
            else if (type == "accountLink") Assert.Equal("synthetic-nonce-not-a-credential", item.GetProperty("link").GetProperty("nonce").GetString());
            else if (type == "memberJoined") Assert.Single(item.GetProperty("joined").GetProperty("members").EnumerateArray());
            else if (type == "postback") Assert.Equal("action=synthetic", item.GetProperty("postback").GetProperty("data").GetString());
        }
        Assert.Equal(ExpectedKinds, kinds.Order(StringComparer.Ordinal));
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：雙宿主讀取 P5-05 與真外殼共用的六案，將 reading 期望直接交給
 * 真統計、通知與週界線規則，不從執行結果重生期望或手寫另一份合成答案。
 * 主要功能與流程：從本測試嵌入資源嚴格讀 JSON；統計驗證每個最小 patch 欄位、
 * 分類修補及零分母，通知逐字驗文字與有序唯一收件者，生成驗兩週當地午夜轉 UTC。
 * 生成的資料庫交易與完整欄位由真活動外殼共用同檔驗證，本檔不冒充 SDK 寫入。
 * 使用邊界與維護：每檔一 MiB、深度32，stream／reader 同步 using 釋放。
 * 所有識別由合成 alias 計算，沒有正式會員、token、秘密、網路、SDK owner、背景
 * 工作或跨案例快取。旗標依情境的易變宣告只比有寫，其他值與集合逐欄精確核對。
 */
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>以人工閱讀舊碼期望驗證真規則；沒有 legacy.json，不能標成舊程式實跑。</summary>
public sealed class WeeklyReportGoldenTests
{
    /// <summary>所有共用案例均須被消費，不允許未知入口或漏案默默通過。</summary>
    [Fact]
    public void WeeklyReport_Scenarios_MatchExpected()
    {
        var inputs = Read("scenarios");
        var expected = Read("expected");
        Assert.Equal("reading", (string?)expected["basis"]);
        var scenarios = inputs["cases"]!.Children<JObject>().ToArray();
        var desired = expected["cases"]!.Children<JObject>().ToArray();
        Assert.Equal(6, scenarios.Length);
        Assert.Equal(scenarios.Select(x => (string)x["name"]!), desired.Select(x => (string)x["name"]!));
        for (var index = 0; index < scenarios.Length; index++)
        {
            var scenario = scenarios[index];
            var output = desired[index];
            var type = (string)scenario["trigger"]!["type"]!;
            if (type.EndsWith("StatisticsPlugin", StringComparison.Ordinal)) CheckStatistics(scenario, output);
            else if (type.EndsWith("NotificationPlugin", StringComparison.Ordinal)) CheckNotification(scenario, output);
            else
            {
                Assert.EndsWith("WeeklyReportGenerationActivity", type, StringComparison.Ordinal);
                var weeks = WeeklyReportGenerationRule.Weeks(
                    DateTime.Parse((string)scenario["now"]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    (string)scenario["church"]!["timeZone"]!);
                var expectedDates = output["writes"]!.Children<JObject>()
                    .Where(x => (string?)x["entity"] == "new_group_present_weekly_report")
                    .Select(x => DateTime.Parse((string)x["fields"]!["new_sunday_date"]!,
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)).ToArray();
                Assert.Equal(2, expectedDates.Length);
                var clock = new ChurchClock();
                Assert.Equal(expectedDates, weeks.Select(day => clock.ToUtc(
                    new LocalDateTimeOfChurch(day, (string)scenario["church"]!["timeZone"]!))).ToArray());
            }
        }
    }

    /// <summary>讀取 seed 的正式列與分類，不按 contact 或姓名去重，對照期望的完整最小 patch。</summary>
    private static void CheckStatistics(JObject scenario, JObject output)
    {
        var seeds = scenario["seed"]!.Children<JObject>().ToDictionary(x => (string)x["alias"]!, StringComparer.Ordinal);
        var report = seeds["report"]["fields"]!;
        var rows = seeds.Values.Where(x => (string?)x["entity"] == "new_present_record").Select(item =>
        {
            var fields = item["fields"]!;
            var contact = (string)fields["new_contact_new_present_record"]!["ref"]!;
            var member = seeds[contact]["fields"]!;
            return new WeeklyReportAttendanceRow(Id((string)item["alias"]!), Id(contact),
                (int?)fields["statecode"]?["option"] == 0, (int?)member["customertypecode"]?["option"],
                (int?)fields["new_sunday_present_this_week"], (int?)fields["new_group_present_this_week"]);
        }).ToArray();
        var result = WeeklyReportStatisticsRule.Evaluate(
            (string?)scenario["trigger"]!["images"]!["pre"]!["PreImage"]!["new_saved_flag"],
            (string?)report["new_saved_flag"], Id("event"), rows, WeeklyReportStatisticsRule.CalculationPrefix());
        var patches = output["writes"]!.Children<JObject>().ToArray();
        Assert.Equal(patches.Length - 1, result.ContactIdsNeedingClassification.Count);
        Assert.Equal(patches.Take(patches.Length - 1).Select(x => Id((string)x["alias"]!)),
            result.ContactIdsNeedingClassification);
        var expectedFields = patches[patches.Length - 1]["fields"]!;
        Assert.NotNull(result.NextSavedFlag);
        Assert.Contains("new_saved_flag", scenario["volatile"]!.Values<string>());
        Assert.Equal((int?)expectedFields["new_sunday_present_number"], result.SundayCount);
        Assert.Equal((int?)expectedFields["new_small_group_number"], result.GroupCount);
        Assert.Equal((double?)expectedFields["new_sunday_present_rate"], result.SundayRate);
        Assert.Equal((double?)expectedFields["new_small_group_rate"], result.GroupRate);
        Assert.Equal(result.EligibleCount == 0 ? 1 : 5, expectedFields.Children<JProperty>().Count());
    }

    /// <summary>四角色依首次正式識別排序，精確保留模板空格及換行，不把直接推播當成舊 multicast。</summary>
    private static void CheckNotification(JObject scenario, JObject output)
    {
        var seeds = scenario["seed"]!.Children<JObject>().ToDictionary(x => (string)x["alias"]!, StringComparer.Ordinal);
        var report = seeds["report"]["fields"]!;
        var list = seeds["list"]["fields"]!;
        string[] roles = ["new_contact_family_leader_list", "new_contact_list_vice_family_leader",
            "new_contact_race_leager_list", "new_contact_co_race_leager_list"];
        var result = WeeklyReportNotificationRule.Evaluate(Id("church"), Id("report"), Id("event"),
            (string?)scenario["trigger"]!["images"]!["pre"]!["PreImage"]!["new_paster_reply"],
            (string?)report["new_paster_reply"], (string?)list["new_contact_list_arealeader"]?["name"],
            DateTime.Parse((string)report["new_sunday_date"]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            (string)scenario["church"]!["timeZone"]!, CultureInfo.GetCultureInfo((string)scenario["church"]!["culture"]!),
            roles.Select(field => list[field]?["ref"] is { } alias ? Id((string)alias!) : Guid.Empty).ToArray(),
            WeeklyReportNotificationRule.Template((string)scenario["church"]!["culture"]!));
        var calls = output["lineCalls"]!.Children<JObject>().ToArray();
        Assert.Equal(calls.Length, result.Recipients.Count);
        if (calls.Length == 0) Assert.Null(result.Message);
        else
        {
            var call = Assert.Single(calls);
            Assert.Equal((string)call["messages"]![0]!["text"]!, result.Message);
            Assert.Equal(Id((string)call["recipients"]![0]!), Assert.Single(result.Recipients).ContactId);
        }
        Assert.Empty(output["writes"]!.Children());
    }

    /// <summary>合成 alias 只在測試中映到正式 GUID，不能作為執行期授權或名稱去重機制。</summary>
    private static Guid Id(string alias) => DeterministicGuid.Create("P5WeeklyGolden", alias);

    /// <summary>有界嵌入資源讀取；明確保留日期原文及 decimal，不依全域 JSON defaults。</summary>
    private static JObject Read(string name)
    {
        using var stream = typeof(WeeklyReportGoldenTests).Assembly.GetManifestResourceStream(
            "SpeechMessageProducts.GoodShepherd.Core.Tests.Golden.Legacy.weeklyreports." + name + ".json")
            ?? throw new InvalidDataException("Weekly report fixture missing.");
        if (stream.Length > 1_048_576) throw new InvalidDataException("Weekly report fixture too large.");
        using var text = new StreamReader(stream, new UTF8Encoding(false, true));
        using var reader = new JsonTextReader(text)
        {
            DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal, MaxDepth = 32
        };
        return JObject.Load(reader);
    }
}

/*
 * 檔案整體說明
 * 目的與定位：P5-06雙宿主出席通知規則測試，使用同份reading六案與真規則逐字核對。
 * 主要功能與流程：合成正式GUID、前後回應與舊格式驗證Create不推播、Update差異、
 * null／空字串及recordId重試鍵；故障注入空身分、超長與未知模板參數，不能截斷後發布。
 * 使用邊界與維護：嵌入JSON最多一MiB、深度32，reader與stream同步using釋放；
 * 沒有SDK、HTTP、憑證、計時器、共用可變資料或背景工作，不宣稱舊程式／實機實跑。
 */
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessageProducts.GoodShepherd.Core.Attendance;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>共同情境與必要邊界核對；正式授權與I/O仍由外殼測試驗證。</summary>
public sealed class PresentRecordRuleTests
{
    /// <summary>六案逐字核對，Create與深度拒絕保留無效果，不能把reading冒稱legacy-golden。</summary>
    [Fact]
    public void PresentRecord_Scenarios_MatchExpected()
    {
        var inputs = Read("scenarios"); var expected = Read("expected");
        Assert.Equal("reading", (string?)expected["basis"]);
        var cases = inputs["cases"]!.Children<JObject>().ToArray();
        var outputs = expected["cases"]!.Children<JObject>().ToArray();
        Assert.Equal(6, cases.Length); Assert.Equal(cases.Select(x => (string)x["name"]!), outputs.Select(x => (string)x["name"]!));
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i]; var trigger = c["trigger"]!;
            var rows = c["seed"]!.Children<JObject>().ToDictionary(x => (string)x["alias"]!);
            var member = rows["contact-1"]["fields"]!; var record = rows["present-1"]["fields"]!;
            var eligible = member["new_lineid"] is not null && (string?)member["parentcustomerid"]?["ref"] == "church-a";
            var message = PresentRecordNotificationRule.Evaluate(Id, (string)trigger["message"]!,
                (string?)trigger["images"]!["pre"]!["PresentRecordLineImage"]!["new_paster_reply"],
                (string?)record["new_paster_reply"], "Synthetic Member", (string?)record["new_explanation"],
                PresentRecordNotificationRule.Template("zh-TW"));
            if ((int)trigger["depth"]! > 2 && (string)trigger["message"]! != "Create" || !eligible) message = null;
            var calls = outputs[i]["lineCalls"]!.Children<JObject>().ToArray();
            Assert.Empty(outputs[i]["writes"]!.Children());
            Assert.Equal(calls.Length == 0 ? null : (string?)calls[0]["messages"]![0]!["text"], message);
        }
    }

    /// <summary>核定名稱保留；舊Create不通知，之後第一次回應Update才產生唯一文字。</summary>
    [Fact]
    public void PresentRecord_Created_NotifiesMemberOnce()
    {
        Assert.Null(Evaluate("Create", null, "new"));
        Assert.Equal("會員 代禱事項: 事項\r\n牧者回應: new", Evaluate("Update", "old", "new"));
        Assert.Null(Evaluate("Update", "new", "new"));
    }

    /// <summary>同值不通知，null到空字串仍通知；插入值的大括號不可再被當模板解析。</summary>
    [Fact]
    public void PresentRecord_UpdatedIrrelevantField_NoPush()
    {
        Assert.Null(Evaluate("Update", "old", "old"));
        Assert.Equal("會員 代禱事項: 事項\r\n牧者回應: ", Evaluate("Update", null, ""));
        Assert.EndsWith("{member}", Evaluate("Update", null, "{member}"), StringComparison.Ordinal);
    }

    /// <summary>05A指定直接使用正式出席主鍵；同記錄重試一致，另一記錄保持隔離。</summary>
    [Fact]
    public void PresentRecord_RetryKey_IsRecordId()
    {
        Assert.Equal(Id, PresentRecordNotificationRule.RetryKey(Id));
        Assert.NotEqual(Id, PresentRecordNotificationRule.RetryKey(new Guid("22222222-2222-2222-2222-222222222222")));
        Assert.Throws<ArgumentException>(() => PresentRecordNotificationRule.RetryKey(Guid.Empty));
        Assert.Throws<ArgumentException>(() => Evaluate("Update", null, new string('x', 2001)));
        Assert.Throws<FormatException>(() => PresentRecordNotificationRule.Evaluate(Id, "Update", "old", "new", "會員", "事項", "{member}{prayer}{reply}{unknown}"));
    }

    /// <summary>固定合成身分，非真實資料列；不保存跨案例事件或收件者狀態。</summary>
    private static readonly Guid Id = new("11111111-1111-1111-1111-111111111111");
    /// <summary>測試只提供固定純值；產品模板從同組件資源表選取。</summary>
    private static string? Evaluate(string message, string? prior, string? current) =>
        PresentRecordNotificationRule.Evaluate(Id, message, prior, current, "會員", "事項", PresentRecordNotificationRule.Template("zh-TW"));
    /// <summary>有界讀取版本控制合成資料；保留日期原文及decimal，不依全域JSON設定。</summary>
    private static JObject Read(string name)
    {
        using var stream = typeof(PresentRecordRuleTests).Assembly.GetManifestResourceStream(
            "SpeechMessageProducts.GoodShepherd.Core.Tests.Golden.Legacy.attendance." + name + ".json") ?? throw new InvalidDataException("Missing attendance fixture.");
        if (stream.Length > 1048576) throw new InvalidDataException("Fixture budget exceeded.");
        using var text = new StreamReader(stream, new UTF8Encoding(false, true));
        using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
        return JObject.Load(reader);
    }
}

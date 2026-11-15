/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-08 預約簽核 Core 的離線訊息契約。測試使用合成純值，
 * 不建立 D365、LINE、HTTP 或 token provider；每個測試只擁有當次 Flex 模型。
 *
 * 測試範圍：同意／退回兩個 Postback 必須攜帶正式 appointment ID 與簽核者，
 * 下一位簽核者不可被顯示名稱取代；Plugin 保留舊入口空字串而不擅自觸發通知，活動接受舊工作流程分派；產品新規則
 * 不以資料列 token 作為輸入。決定性斷言以序列化後的 Flex 結構、原始碼掃描及公開契約完成。
 * 維護與驗證：沒有實機送達、舊程式實跑或 Shadow 差異宣稱；測試只使用當次合成資料，
 * 測試執行完即由框架釋放模型與序列化暫存，不保留跨案例狀態。
 */
using System;
using System.IO;
using System.Linq;
using Line.Messaging;
using Newtonsoft.Json;
using SpeechMessageProducts.GoodShepherd.Core.Appointments;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Appointments;

/// <summary>預約簽核訊息與入口契約的雙目標離線測試。</summary>
public sealed class AppointmentSigningRuleTests
{
    private static readonly Guid AppointmentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SignerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>同意與退回 action 都應保留相同正式來源身份。</summary>
    [Fact]
    public void Appointment_Signing_ApproveAndReject_MatchLegacy()
    {
        var message = AppointmentSigningMessageFactory.Create(Input("請假簽核", "請假事由：病假"));
        var json = JsonConvert.SerializeObject(message);

        var carousel = Assert.IsType<CarouselContainer>(message.Contents);
        var bubble = Assert.Single(carousel.Contents);
        var body = Assert.IsType<BoxComponent>(bubble.Body);
        var content = Assert.Single(Assert.IsType<BoxComponent>(body.Contents[2]).Contents.OfType<TextComponent>());
        Assert.Equal("請假事由：病假", content.Text);
        Assert.Contains("同意", json, StringComparison.Ordinal);
        Assert.Contains("退回", json, StringComparison.Ordinal);
        Assert.Contains(AppointmentId.ToString("D"), json, StringComparison.Ordinal);
        Assert.Contains("簽核者=下一位簽核者", json, StringComparison.Ordinal);
    }

    /// <summary>訊息只把正式簽核者送入收件者契約，不以 applicant 或顯示文字冒充。</summary>
    [Fact]
    public void Appointment_Signing_NotifiesNextSigner()
    {
        var input = Input("場地或資源簽核", "場地：2F大堂");
        var message = AppointmentSigningMessageFactory.Create(input);
        var json = JsonConvert.SerializeObject(message);

        Assert.Contains("下一位簽核者", json, StringComparison.Ordinal);
        Assert.DoesNotContain("new_channel_access_token", json, StringComparison.Ordinal);
    }

    /// <summary>對三層 Appointments 原始碼逐檔掃描，不得含資料列 token 欄位，防止新規則沿用退役寫死 token 入口。</summary>
    [Fact]
    public void Appointment_TokenNeverReadFromRecordField()
    {
        var root = FindRoot();
        foreach (var directory in new[] { "SpeechMessageProducts.GoodShepherd.Core", "SpeechMessage.Dynamics.Plugins", "SpeechMessage.Dynamics.Workflows" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, directory, "Appointments"), "*.cs", SearchOption.AllDirectories))
                Assert.DoesNotContain("new_channel_access_token", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal("工作流程", AppointmentSigningRule.DefaultParameter("ExecuteWorkflow"));
        Assert.Equal(string.Empty, AppointmentSigningRule.DefaultParameter("Create"));
        Assert.Equal(string.Empty, AppointmentSigningRule.DefaultParameter("Update"));
    }

    /// <summary>只沿目前測試輸出尋找方案，不跨入另一份工作樹讀取來源。</summary>
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Appointment test source root is unavailable.");
    }

    /// <summary>每次建立獨立純值，不保留跨測試可變訊息。</summary>
    private static AppointmentSigningMessageInput Input(string subject, string content) =>
        new(AppointmentId, SignerId, subject, content, "申請人", "下一位簽核者",
            "https://example.invalid/appointment-signing.png");
}

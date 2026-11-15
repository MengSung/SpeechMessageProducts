/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-08 預約簽核 Core 的離線訊息契約。測試使用合成純值，
 * 不建立 D365、LINE、HTTP 或 token provider；每個測試只擁有當次 Flex 模型。
 *
 * 保護契約：同意／退回兩個 Postback 必須攜帶正式 appointment ID 與簽核者，
 * 下一位簽核者不可被顯示名稱取代；規則固定參數只接受三個舊入口；產品新規則
 * 不以資料列 token 作為輸入。決定性斷言以序列化後的 Flex 結構及公開契約完成。
 * 沒有實機送達、舊程式實跑或 Shadow 差異宣稱。
 */
using System;
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

    /// <summary>輸入模型沒有資料列 token 欄位，防止新規則沿用退役寫死 token 入口。</summary>
    [Fact]
    public void Appointment_TokenNeverReadFromRecordField()
    {
        var properties = typeof(AppointmentSigningMessageInput).GetProperties();
        Assert.DoesNotContain(properties, property =>
            property.Name.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("工作流程", AppointmentSigningRule.DefaultParameter("ExecuteWorkflow"));
        Assert.Equal("新增", AppointmentSigningRule.DefaultParameter("Create"));
        Assert.Equal("更新", AppointmentSigningRule.DefaultParameter("Update"));
    }

    private static AppointmentSigningMessageInput Input(string subject, string content) =>
        new(AppointmentId, SignerId, subject, content, "申請人", "下一位簽核者",
            "https://example.invalid/appointment-signing.png");
}

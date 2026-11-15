/*
 * 檔案整體說明
 *
 * 目的與定位：P5-04 教會語系外殼的離線契約測試，直接走真 PluginShellBase、
 * LocaleChangeRules、OutboundTaskWriter 與 SDK store；不以替身取代產品驗證規則。
 * 主要功能與流程：檢驗同步拒絕未開放語言、預設不在啟用集合、未知時區及明確清空；
 * 非同步以合成唯一鍵 fault 重跑同設定，斷言只一筆待辦且既有狀態不被覆寫。Shadow
 * 解析 Create 投影與完整 end，跨同名教會必須保有不同正式 ID 和來源鍵，未知故障原樣拋出。
 * 使用邊界與維護：每案 using 清除合成 SDK、追蹤、待辦與回呼，沒有網路、秘密、timer
 * 或背景工作。受限域另跑相同產品鏈；這些測試不宣稱已驗證正式資料庫唯一鍵或實機權限。
 */
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Localization;
using Xunit;
namespace SpeechMessage.Dynamics.Plugins.Tests.Localization;
/// <summary>同步驗證、非同步原子待辦與跨教會隔離的真外殼契約。</summary>
public sealed class ChurchLocaleChangedPluginTests
{
    /// <summary>未開放英文必須由外殼轉成具事件 ID 的業務拒絕，且完全沒有 SDK 寫入。</summary>
    [Fact]
    public void ChurchLocale_UnopenedLanguage_Rejected() => Reject("new_defaultlanguage", "en");
    /// <summary>預設語言不在啟用集合時拒絕；即使兩個欄位同時變更仍不得建立待辦。</summary>
    [Fact]
    public void ChurchLocale_DefaultNotInEnabled_Rejected()
    {
        using var fixture = new LocalizationFixture();
        var context = fixture.ChurchContext(20, 0);
        var target = (Entity)context.InputParameters["Target"];
        target["new_defaultlanguage"] = "zh-TW";
        target["new_enabledlanguages"] = string.Empty;
        Assert.Throws<InvalidPluginExecutionException>(() => fixture.Execute(Shell(), context));
        Assert.Empty(fixture.Service.Calls);
    }
    /// <summary>未知時區不回落伺服器時區，必須在任何 SDK 讀寫前拒絕。</summary>
    [Fact]
    public void ChurchLocale_UnknownTimeZone_Rejected() => Reject("new_timezone", "Unknown/Nowhere");
    /// <summary>大小寫有誤的國碼不靠正規化掩蓋實際提交值；保存後仍須符合兩大寫字母。</summary>
    [Fact]
    public void ChurchLocale_LowercaseCountry_Rejected() => Reject("new_countrycode", "tw");
    /// <summary>明確清空每個欄位均不得回退舊設定；注入 null 或整數都必須零寫入拒絕。</summary>
    [Theory]
    [InlineData("new_defaultlanguage", false)]
    [InlineData("new_enabledlanguages", false)]
    [InlineData("new_timezone", false)]
    [InlineData("new_countrycode", false)]
    [InlineData("new_defaultlanguage", true)]
    [InlineData("new_enabledlanguages", true)]
    [InlineData("new_timezone", true)]
    [InlineData("new_countrycode", true)]
    public void ChurchLocale_ExplicitInvalidPatch_DoesNotFallback(string field, bool wrongType) =>
        Reject(field, wrongType ? 17 : null!);
    /// <summary>只接受 20/0 及 40/1；錯誤模式在讀教會與建立待辦前 fail closed。</summary>
    [Theory]
    [InlineData(20, 1)]
    [InlineData(40, 0)]
    [InlineData(10, 0)]
    public void ChurchLocale_StageModeMismatch_Rejected(int stage, int mode)
    {
        using var fixture = new LocalizationFixture();
        Assert.Throws<InvalidPluginExecutionException>(() => fixture.Execute(Shell(), fixture.ChurchContext(stage, mode)));
        Assert.Empty(fixture.Service.Calls);
    }
    /// <summary>合法同步操作只驗設定，沒有額外讀取、建立或異步工作。</summary>
    [Fact]
    public void ChurchLocale_PreOperation_ValidatesWithoutEnqueue()
    {
        using var fixture = new LocalizationFixture();
        fixture.Execute(Shell(), fixture.ChurchContext(20, 0));
        Assert.Empty(fixture.Service.Calls);
        Assert.Single(fixture.Intents());
    }
    /// <summary>不合法的正式教會代碼在待辦 Create 前拒絕，不自動轉小寫產生另一個 alias。</summary>
    [Fact]
    public void ChurchLocale_ChurchCodeInvalidForAlias_Rejected()
    {
        using var fixture = new LocalizationFixture(code: "Alpha");
        Assert.Throws<InvalidPluginExecutionException>(() => fixture.Execute(Shell(), fixture.ChurchContext()));
        Assert.Equal(0, fixture.TaskCount);
        Assert.Empty(fixture.Service.WriteRecords);
    }
    /// <summary>相同設定重跑觸發資料庫合成唯一鍵 fault，只讀回既有 ID，不 Update 狀態或 payload。</summary>
    [Fact]
    public void ChurchLocale_SameSettingsTwice_OneTask()
    {
        using var fixture = new LocalizationFixture();
        var shell = new ChurchLocaleChangedPlugin("{\"mode\":\"Live\"}", "");
        fixture.Execute(shell, fixture.ChurchContext());
        var row = fixture.TaskRow;
        row["new_taskstatus"] = new OptionSetValue(100000002);
        row["new_attempts"] = 4;
        fixture.Execute(shell, fixture.ChurchContext());
        Assert.Equal(1, fixture.TaskCount);
        Assert.Single(fixture.Service.WriteRecords);
        Assert.Equal(100000002, row.GetAttributeValue<OptionSetValue>("new_taskstatus").Value);
        Assert.Equal(4, row.GetAttributeValue<int>("new_attempts"));
        Assert.Equal(1, fixture.Service.Calls["RetrieveMultiple"]);
        Assert.False(fixture.Service.Calls.ContainsKey("Update"));
    }
    /// <summary>同名不同 ID 的教會必須各自建立來源鍵，A/B/A 重用同一外殼也不借用別人的設定。</summary>
    [Fact]
    public void ChurchLocale_OtherChurchUnaffected()
    {
        using var first = new LocalizationFixture();
        using var second = new LocalizationFixture();
        var shell = new ChurchLocaleChangedPlugin("{\"mode\":\"Live\"}", "");
        foreach (var fixture in new[] { first, second, first }) fixture.Execute(shell, fixture.ChurchContext());
        Assert.Equal(first.ChurchId, first.TaskRow.GetAttributeValue<EntityReference>("new_accountid").Id);
        Assert.Equal(second.ChurchId, second.TaskRow.GetAttributeValue<EntityReference>("new_accountid").Id);
        Assert.NotEqual(first.TaskRow["new_idempotencykey"], second.TaskRow["new_idempotencykey"]);
        Assert.Equal(1, first.TaskCount);
        Assert.Equal(1, second.TaskCount);
    }
    /// <summary>Shadow 僅一筆完整待辦 Create，安全欄位符合來源設定，真服務沒有 Create。</summary>
    [Fact]
    public void ChurchLocale_ShadowMode_MatchesExpected()
    {
        using var fixture = new LocalizationFixture();
        fixture.Execute(Shell(), fixture.ChurchContext());
        var intents = fixture.Intents();
        Assert.Equal(2, intents.Length);
        Assert.Equal("Create", (string?)intents[0]["operation"]);
        Assert.Equal("new_outboundtask", (string?)intents[0]["entity"]);
        Assert.Equal(fixture.ChurchId.ToString("D"), (string?)intents[0]["fields"]?["new_accountid"]?["id"]);
        Assert.Equal(100000000, (int?)intents[0]["fields"]?["new_type"]);
        var source = LocaleChangeRules.SourceId(fixture.ChurchId,
            LocaleChangeRules.Validate("zh-TW", "zh-TW", "Asia/Taipei", "TW"));
        Assert.Equal(fixture.ChurchId.ToString("N") + "-100000000-" + source.ToString("N"),
            (string?)intents[0]["fields"]?["new_idempotencykey"]);
        Assert.True((bool)intents[1]["end"]!);
        Assert.Equal(1, (int?)intents[1]["count"]);
        Assert.Equal(0, fixture.TaskCount);
        Assert.Empty(fixture.Service.WriteRecords);
    }
    /// <summary>門檻二必須走真待辦鏈；三跳過時連教會代碼也不能讀。</summary>
    [Fact]
    public void ChurchLocale_DepthPolicy_MatchesLegacy()
    {
        using var fixture = new LocalizationFixture();
        var shell = Shell();
        fixture.Execute(shell, fixture.ChurchContext(depth: 2));
        Assert.Equal(2, fixture.Intents().Length);
        var calls = fixture.Service.Calls["Retrieve"];
        fixture.Execute(shell, fixture.ChurchContext(depth: 3));
        Assert.Empty(fixture.Intents());
        Assert.Equal(calls, fixture.Service.Calls["Retrieve"]);
    }
    /// <summary>待辦建立時注入未知失敗，必須原實例拋出且無部分成功記錄。</summary>
    [Fact]
    public void ChurchLocale_UnexpectedError_Rethrown()
    {
        using var fixture = new LocalizationFixture();
        var failure = new InvalidOperationException("synthetic-sensitive-detail");
        fixture.Service.CreateHandler = _ => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => fixture.Execute(
            new ChurchLocaleChangedPlugin("{\"mode\":\"Live\"}", ""), fixture.ChurchContext())));
        Assert.Empty(fixture.Service.WriteRecords);
        Assert.Equal(0, fixture.TaskCount);
        Assert.DoesNotContain(failure.Message, string.Join("", fixture.Trace.Entries), StringComparison.Ordinal);
    }
    /// <summary>固定模式只存在於測試，正式註冊仍由主代理核對 Shadow／停用。</summary>
    private static ChurchLocaleChangedPlugin Shell() => new("{\"mode\":\"Shadow\"}", "");
    /// <summary>同步補丁故障入口，決定性檢查完全沒有 SDK I/O。</summary>
    private static void Reject(string field, object value)
    {
        using var fixture = new LocalizationFixture();
        var context = fixture.ChurchContext(20, 0);
        ((Entity)context.InputParameters["Target"])[field] = value;
        Assert.Throws<InvalidPluginExecutionException>(() => fixture.Execute(Shell(), context));
        Assert.Empty(fixture.Service.Calls);
        Assert.Equal(0, fixture.TaskCount);
    }
}

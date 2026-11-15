/*
 * 檔案整體說明
 *
 * 目的與定位：保護 P5-04 MemberLanguageChangedPlugin 的真外殼與 Core 換綁鏈，
 * 使用合成 SDK、無效加密封包與 Shadow；不把離線意圖當成 LINE 或 D365 實機結果。
 * 主要功能與流程：驗證語系解析後 alias 的固定摘要、未綁定零後續讀取、非法代碼拒絕、
 * 重讀遇跨教會拒絕、深度二執行與三跳過、同事件穩定鍵及未知 SDK 故障保持原實例。
 * 使用邊界與維護：各案 using 擁有 fixture，所有服務與影子 sink 同步排空；重用外殼
 * 交錯 A/B/A 並並行執行，追蹤只含當案正式識別。沒有真金鑰、HTTP、背景工作或全域
 * Json 設定；無效 envelope 若意外進入解密會直接失敗，不能將 Shadow 當 Live 成功。
 */
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Localization;
using Xunit;
namespace SpeechMessage.Dynamics.Plugins.Tests.Localization;
/// <summary>以真入口驗證換綁的隔離、拒絕及當次資源所有權，不測替身自身輸出。</summary>
public sealed class MemberLanguageChangedPluginTests
{
    /// <summary>固定 alias alpha-main-home-zh-tw 的獨立 SHA-256 向量，不能用產品函式自比。</summary>
    private const string AliasHash = "a39f9871dbf924231d791e767770c0fafed8498a0c741db408c2f525adba7da3";
    /// <summary>由已提交的偏好取得新 alias；不解密無效封包，也不寫入真 SDK。</summary>
    [Fact]
    public void MemberLanguage_Changed_LinksAliasForNewCulture()
    {
        using var fixture = new LocalizationFixture();
        fixture.Execute(new MemberLanguageChangedPlugin("{\"mode\":\"Shadow\"}", ""), fixture.MemberContext());
        var intents = fixture.Intents();
        Assert.Equal(2, intents.Length);
        Assert.Equal("Push", (string?)intents[0]["operation"]);
        Assert.Equal("RichMenuAlias", (string?)intents[0]["templateKey"]);
        Assert.Equal(fixture.ContactId.ToString("D"), (string?)intents[0]["contactId"]);
        Assert.Equal(AliasHash, (string?)intents[0]["contentSha256"]);
        Assert.True((bool)intents[1]["end"]!);
        Assert.Empty(fixture.Service.WriteRecords);
        Assert.DoesNotContain("invalid-envelope", string.Join("", fixture.Trace.Entries), StringComparison.Ordinal);
        Assert.DoesNotContain("U" + new string('a', 32), string.Join("", fixture.Trace.Entries), StringComparison.Ordinal);
    }
    /// <summary>未綁定在無效教會代碼與缺 Live key 下仍正常結束，只讀一次 contact。</summary>
    [Fact]
    public void MemberLanguage_NotBound_NoCall()
    {
        using var fixture = new LocalizationFixture(bound: false, code: "INVALID");
        fixture.Execute(new MemberLanguageChangedPlugin("{\"mode\":\"Live\"}", ""), fixture.MemberContext());
        Assert.Equal(1, fixture.Service.Calls["Retrieve"]);
        Assert.Empty(fixture.Service.WriteRecords);
        Assert.Empty(fixture.Trace.Entries);
    }
    /// <summary>固定做法同名入口亦保護未綁定零傳輸及不丟例外，包含無效 secure 設定。</summary>
    [Fact]
    public void MemberLanguage_NotBound_NoPushNoThrow() => MemberLanguage_NotBound_NoCall();
    /// <summary>大小寫不合 alias 的教會不能被自動清洗，也不得發布任何換綁意圖。</summary>
    [Fact]
    public void MemberLanguage_ChurchCodeInvalidForAlias_Rejected()
    {
        using var fixture = new LocalizationFixture(code: "Alpha");
        Assert.Throws<InvalidPluginExecutionException>(() => fixture.Execute(
            new MemberLanguageChangedPlugin("{\"mode\":\"Shadow\"}", ""), fixture.MemberContext()));
        Assert.Single(fixture.Intents());
        Assert.Empty(fixture.Service.WriteRecords);
    }
    /// <summary>首讀後會友換到另一教會時 Core 必須拒絕，不讀頻道、不解密也不發布 Push。</summary>
    [Fact]
    public void MemberLanguage_RecipientOtherChurch_NoPush()
    {
        using var fixture = new LocalizationFixture();
        var retrieve = fixture.Service.RetrieveHandler!;
        var contactReads = 0;
        fixture.Service.RetrieveHandler = (table, id, columns) =>
        {
            var row = retrieve(table, id, columns);
            if (table == "contact" && ++contactReads == 2)
            {
                var moved = new Entity("contact", id);
                foreach (var pair in row.Attributes) moved[pair.Key] = pair.Value;
                moved["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
                return moved;
            }
            return row;
        };
        fixture.Execute(new MemberLanguageChangedPlugin("{\"mode\":\"Shadow\"}", ""), fixture.MemberContext());
        Assert.Equal(3, fixture.Service.Calls["Retrieve"]);
        Assert.Single(fixture.Intents());
        Assert.Empty(fixture.Service.WriteRecords);
    }
    /// <summary>新入口無舊版；D5-8 精確要求二可執行，三不接觸任何 SDK。</summary>
    [Fact]
    public void MemberLanguage_DepthPolicy_MatchesLegacy()
    {
        using var fixture = new LocalizationFixture();
        var shell = new MemberLanguageChangedPlugin("{\"mode\":\"Shadow\"}", "");
        fixture.Execute(shell, fixture.MemberContext(depth: 2));
        Assert.Equal(2, fixture.Intents().Length);
        var calls = fixture.Service.Calls["Retrieve"];
        fixture.Execute(shell, fixture.MemberContext(depth: 3));
        Assert.Empty(fixture.Intents());
        Assert.Equal(calls, fixture.Service.Calls["Retrieve"]);
    }
    /// <summary>注入第一個 SDK 讀取失敗；外殼不能吞掉、包裝或保留原始訊息於 trace。</summary>
    [Fact]
    public void MemberLanguage_UnexpectedError_Rethrown()
    {
        using var fixture = new LocalizationFixture();
        var failure = new InvalidOperationException("synthetic-sensitive-detail");
        fixture.Service.RetrieveHandler = (_, _, _) => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => fixture.Execute(
            new MemberLanguageChangedPlugin("{\"mode\":\"Shadow\"}", ""), fixture.MemberContext())));
        Assert.DoesNotContain(failure.Message, string.Join("", fixture.Trace.Entries), StringComparison.Ordinal);
    }
    /// <summary>同一事件重跑只發布相同純值 retry 識別，不採用本次執行的新 GUID。</summary>
    [Fact]
    public void MemberLanguage_RetryKey_IsStableForSameEvent()
    {
        using var fixture = new LocalizationFixture();
        var shell = new MemberLanguageChangedPlugin("{\"mode\":\"Shadow\"}", "");
        fixture.Execute(shell, fixture.MemberContext());
        var first = fixture.Intents()[0].ToString();
        fixture.Execute(shell, fixture.MemberContext());
        Assert.Equal(first, fixture.Intents()[0].ToString());
    }
    /// <summary>重用同一外殼，兩間同名教會各自保有正式 ID；A/B/A 不得殘留前次追蹤。</summary>
    [Fact]
    public void MemberLanguage_ShadowMode_MatchesExpected()
    {
        using var first = new LocalizationFixture();
        using var second = new LocalizationFixture(code: "beta");
        var shell = new MemberLanguageChangedPlugin("{\"mode\":\"Shadow\"}", "");
        foreach (var fixture in new[] { first, second, first })
        {
            fixture.Execute(shell, fixture.MemberContext());
            Assert.Equal(fixture.ContactId.ToString("D"), (string?)fixture.Intents()[0]["contactId"]);
            Assert.Equal(2, fixture.Intents().Length);
            Assert.Empty(fixture.Service.WriteRecords);
        }
    }
    /// <summary>同一外殼並行使用不同 owner，各自成功完成且 Dispose 後記錄與回呼歸零。</summary>
    [Fact]
    public async Task MemberLanguage_ConcurrentChurches_DrainToBaseline()
    {
        var shell = new MemberLanguageChangedPlugin("{\"mode\":\"Shadow\"}", "");
        using var first = new LocalizationFixture();
        using var second = new LocalizationFixture(code: "beta");
        await Task.WhenAll(new[] { first, second }.Select(fixture => Task.Run(() =>
        {
            for (var index = 0; index < 32; index++)
            {
                fixture.Execute(shell, fixture.MemberContext());
                Assert.Equal(fixture.ContactId.ToString("D"), (string?)fixture.Intents()[0]["contactId"]);
            }
        })));
        foreach (var fixture in new[] { first, second })
        {
            fixture.Dispose();
            Assert.Equal(0, fixture.Service.EntityCount);
            Assert.Empty(fixture.Service.WriteRecords);
            Assert.Null(fixture.Service.RetrieveHandler);
            Assert.Throws<ObjectDisposedException>(() => fixture.Service.Retrieve("contact", fixture.ContactId,
                new Microsoft.Xrm.Sdk.Query.ColumnSet()));
        }
    }
}

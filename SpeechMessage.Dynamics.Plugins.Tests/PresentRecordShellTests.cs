/*
 * 檔案整體說明
 * 目的與定位：以真PresentRecordPlugin與Core共用六案保護P5-06薄外殼接線，不連真實D365／LINE。
 * 主要功能與流程：Shadow逐欄比內容摘要與正式本人身分，另外故障注入錯教會、未綁定、
 * 缺圖及原始SDK例外，斷言零真寫入、同一例外、PreImage／篩選與舊深度語意。
 * 使用邊界與維護：每案fixture唯一擁有SDK與trace並同步Dispose；同殼A/B/A及並行
 * 驗證沒有跨教會身分殘留。無HTTP、真憑證、計時器或未觀測工作，不宣稱實機影子驗收。
 */
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Plugins.Attendance;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>真入口必要接線回歸，規則詳細文字由同份reading期望驗證。</summary>
public sealed class PresentRecordShellTests
{
    /// <summary>同份六案逐欄核對正式收件者、模板、recordId與含sender訊息摘要。</summary>
    [Fact]
    public void PresentRecord_ShadowMode_MatchesExpected() => PresentRecordGoldenFixture.RunAll();

    /// <summary>核定名稱：舊Create零通知，首次牧者回應Update一則本人意圖，無真寫入。</summary>
    [Fact]
    public void PresentRecord_Created_NotifiesMemberOnce()
    {
        using var f = Normal(); f.Context.MessageName = "Create"; f.Run(); Assert.Empty(f.Operations());
        f.Context.MessageName = "Update"; f.Run(); Assert.Single(f.Operations()); Assert.Empty(f.Service.WriteRecords);
        // 每欄合法2000字時，組合訊息仍須完整，不能被舊SDK建構式截為2000字。
        f.Rows["present-1"]["new_explanation"] = new string('p', 2000);
        f.Rows["present-1"]["new_paster_reply"] = new string('r', 2000);
        f.Run();
        var fullText = "Synthetic Member 代禱事項: " + new string('p', 2000) + "\r\n牧者回應: " + new string('r', 2000);
        var expectedHash = LineMessagingProcessor.Core.LinePushClient.ComputeMemberContentSha256(
            new Line.Messaging.ISendMessage[] { new Line.Messaging.TextMessage("") { Text = fullText } }, "Synthetic Church助手");
        Assert.Equal(expectedHash, (string?)Assert.Single(f.Operations())["contentSha256"]);
    }
    /// <summary>無關Target欄位即使資料庫回應已不同也不得補發；同值前像亦無意圖。</summary>
    [Fact]
    public void PresentRecord_UpdatedIrrelevantField_NoPush()
    {
        using var f = Normal();
        f.Context.InputParameters["Target"] = new Entity("new_present_record", f.Context.PrimaryEntityId) { ["new_explanation"] = "其他" };
        f.Run(); Assert.Empty(f.Operations());
        Assert.Equal(0, f.Reads);
    }
    /// <summary>鍵直接等正式出席主鍵；同一上下文重試不換鍵。</summary>
    [Fact]
    public void PresentRecord_RetryKey_IsRecordId()
    {
        using var f = Normal(); f.Run(); var key = (string?)Assert.Single(f.Operations())["retryKey"];
        Assert.Equal(f.Context.PrimaryEntityId.ToString("D"), key); f.Run(); Assert.Equal(key, (string?)Assert.Single(f.Operations())["retryKey"]);
    }
    /// <summary>共用模組契約同事件穩定，直接覆用核定recordId斷言，沒有新增另一種算法。</summary>
    [Fact]
    public void PresentRecord_RetryKey_IsStableForSameEvent() => PresentRecord_RetryKey_IsRecordId();
    /// <summary>Depth2放行、Depth3 Update不讀SDK；Create深度例外放行但無通知。</summary>
    [Fact]
    public void PresentRecord_DepthPolicy_MatchesLegacy()
    {
        using var f = Normal(); f.Context.Depth = 2; f.Run(); Assert.Single(f.Operations()); var reads = f.Reads;
        f.Context.Depth = 3; f.Run(); Assert.Equal(reads, f.Reads); Assert.Empty(f.Trace.Entries);
        f.Context.MessageName = "Create"; f.Run(); Assert.Empty(f.Operations()); Assert.NotEmpty(f.Trace.Entries);
    }
    /// <summary>原始SDK故障保持實例及堆疊交平台；缺必要前像則先拒絕，無真寫入。</summary>
    [Fact]
    public void PresentRecord_UnexpectedError_Rethrown()
    {
        using var f = Normal(); var failure = new InvalidOperationException("synthetic SDK failure");
        f.Service.RetrieveHandler = (_, _, _) => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => f.Run())); Assert.Empty(f.Service.WriteRecords);
        f.Context.PreEntityImages.Clear(); Assert.Throws<InvalidPluginExecutionException>(() => f.Run()); Assert.Empty(f.Operations());
    }
    /// <summary>來源教會由list組長決定，本人換另一教會不得變更來源或發送／解密。</summary>
    [Fact]
    public void PresentRecord_RecipientOtherChurch_NoPush()
    {
        using var f = Normal(); f.Rows["contact-1"]["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
        f.Run(); Assert.Empty(f.Operations()); Assert.Empty(f.Service.WriteRecords);
    }
    /// <summary>未綁定是正常拒絕，不要求channel key、不丟例外或建立推播。</summary>
    [Fact]
    public void PresentRecord_NotBound_NoPushNoThrow()
    {
        using var f = Normal(); f.Rows["contact-1"].Attributes.Remove("new_lineid"); f.Rows.Remove("channel");
        f.Run(); Assert.Empty(f.Operations()); Assert.Empty(f.Service.WriteRecords);
    }
    /// <summary>共享不可變外殼的並行A/B及再次A，各trace只含當案識別；Dispose後SDK回到零。</summary>
    [Fact]
    public async Task PresentRecord_SharedShell_IsolatesAndDrains()
    {
        using var a = Normal(); using var b = Normal();
        var old = b.Rows["contact-1"]; var differentId = Guid.NewGuid();
        b.Rows["contact-1"] = new Entity("contact", differentId) { Attributes = old.Attributes };
        b.Rows["present-1"]["new_contact_new_present_record"] = new EntityReference("contact", differentId) { Name = "Synthetic Member" };
        var shell = new PresentRecordPlugin("{\"mode\":\"Shadow\"}", "");
        await Task.WhenAll(Task.Run(() => a.Run(shell)), Task.Run(() => b.Run(shell))); a.Run(shell);
        Assert.Equal(PresentRecordGoldenFixture.Id("contact-1").ToString("D"), (string?)Assert.Single(a.Operations())["contactId"]);
        Assert.Equal(differentId.ToString("D"), (string?)Assert.Single(b.Operations())["contactId"]);
        a.Dispose(); Assert.Equal(0, a.Service.EntityCount); Assert.Empty(a.Trace.Entries);
        Assert.Throws<ObjectDisposedException>(() => a.Service.Retrieve("contact", differentId, new ColumnSet()));
    }
    /// <summary>每個案例只借一份共用正常情境，資料與資源不跨測試共用。</summary>
    private static PresentRecordGoldenFixture Normal() => new(PresentRecordGoldenFixture.Normal());
}

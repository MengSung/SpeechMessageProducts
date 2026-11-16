/*
 * 檔案整體說明
 * 目的與定位：P5-11 繳費通知真 WF 入口驗收，共用 Core 的 reading 期望，不以
 * 只測輔助函式或宣告型別代替活動執行。測試不讀秘密、不連 D365 或 LINE。
 * 主要流程：六案逐欄核對完整 Shadow，另保護原例外、Live 查詢前拒絕、無舊
 * 深度限制與活動重用。部分信任子域只授 Execution、輸出目錄唯讀與 SDK 強名
 * 必要 FullTrust，真活動及 Core 必須仍受限；不放寬反射或網路權限。
 * 使用邊界與維護：每案 using 清除 SDK／trace／故障；父 CAS 測試 finally 卸載
 * 子域。無 timer、queue 或未排空 Task；A/B/A 共用活動但資料 owner 獨立。
 * 這是離線規則、WF 與受限 CLR 證據，實機追蹤依 D5-19 延後。
 */
extern alias Workflows;
using System;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json.Linq;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using SpeechMessageProducts.GoodShepherd.Core.Tests;
using Xunit;
using FeeNotifyActivity = Workflows::SpeechMessage.Dynamics.Workflows.Fees.FeeNotifyActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fees;

/// <summary>真 WF 的固定模組測試與最小必要安全／生命週期回歸。</summary>
public sealed class FeesWorkflowTests
{
    /// <summary>同份六案由真活動執行，原故障外的正常拒絕均不得拋錯或產生推播。</summary>
    [Fact]
    public void Fees_ShadowMode_MatchesExpected()
    {
        var scenarios = (JArray)FeesGoldenFixture.Read("scenarios")["cases"]!;
        var expected = (JArray)FeesGoldenFixture.Read("expected")["cases"]!;
        var activity = new FeeNotifyActivity();
        for (var index = 0; index < scenarios.Count; index++)
        {
            using var fixture = new FeesWorkflowFixture(scenarios[index], depth: 99);
            var failure = Record.Exception(() => fixture.Invoke(activity));
            Assert.Equal((string?)expected[index]["exception"], failure?.GetType().Name);
            if (fixture.Sdk.ReadFailure is not null) Assert.Same(fixture.Sdk.ReadFailure, failure);
            fixture.Verify(expected[index]);
            if (fixture.Sdk.Message == "Create") Assert.Equal(0, fixture.Sdk.Reads);
        }
    }

    /// <summary>首次讀取真失敗保留同一例外、安全型別追蹤及零意圖 end，不吞掉錯誤。</summary>
    [Fact]
    public void Fees_UnexpectedError_Rethrown()
    {
        var scenario = FeesGoldenFixture.Read("scenarios")["cases"]![3]!;
        using var fixture = new FeesWorkflowFixture(scenario);
        Assert.Same(fixture.Sdk.ReadFailure,
            Assert.Throws<InvalidOperationException>(() => fixture.Invoke(new FeeNotifyActivity())));
        Assert.Contains(fixture.Trace.Entries, line => line.Contains("ExceptionType=System.InvalidOperationException"));
        fixture.Verify(FeesGoldenFixture.Read("expected")["cases"]![3]!);
    }

    /// <summary>依 P5-PROP-006，Live 必須在任何 fee／會友／token 查詢前拒絕。</summary>
    [Fact]
    public void Fees_Live_RefusedBeforeQuery()
    {
        using var fixture = new FeesWorkflowFixture(FeesGoldenFixture.Read("scenarios")["cases"]![0]!);
        Assert.Throws<InvalidPluginExecutionException>(() => fixture.Invoke(new FeeNotifyActivity(), "Live"));
        Assert.Equal(0, fixture.Sdk.Reads);
        Assert.Equal(0, fixture.Sdk.WriteAttempts);
    }

    /// <summary>同一活動正常、未綁定、正常交替重用，完整期望及 owner 零基線必須恢復。</summary>
    [Fact]
    public void Fees_ReusedActivity_DrainsAndIsolates()
    {
        var activity = new FeeNotifyActivity();
        var scenarios = FeesGoldenFixture.Read("scenarios")["cases"]!;
        var expected = FeesGoldenFixture.Read("expected")["cases"]!;
        foreach (var index in new[] { 0, 2, 0 })
        {
            using var fixture = new FeesWorkflowFixture(scenarios[index]!);
            fixture.Invoke(activity); fixture.Verify(expected[index]!);
            fixture.Dispose();
            Assert.Empty(fixture.Trace.Entries); Assert.Empty(fixture.Sdk.Calls);
            Assert.Null(fixture.Sdk.ReadFailure); Assert.Equal(0, fixture.Sdk.Reads);
        }
    }

    /// <summary>僅 SDK 必要組件 FullTrust；受限域真跑完整 WF／Core 通知影子鏈。</summary>
    [Fact]
    public void PartialTrust_FeesShadowChain_Runs()
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        var sdk = typeof(IPlugin).Assembly.Evidence.GetHostEvidence<StrongName>();
        Assert.NotNull(sdk);
        var domain = AppDomain.CreateDomain("P5-Fees-" + Guid.NewGuid().ToString("N"), null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile },
            permissions, sdk);
        try
        {
            var probe = (FeesPartialTrustProbe)domain.CreateInstanceAndUnwrap(
                typeof(FeesPartialTrustProbe).Assembly.FullName, typeof(FeesPartialTrustProbe).FullName);
            var result = probe.Run();
            Assert.True(string.Equals(result, "PASS", StringComparison.Ordinal), result);
        }
        finally { AppDomain.Unload(domain); }
    }
}

/// <summary>一次性跨域探針，只回傳固定成功或本機診斷，不交換活動／SDK／個資。</summary>
public sealed class FeesPartialTrustProbe : MarshalByRefObject
{
    private bool ran;
    /// <summary>確認外殼與 Core 仍受限後，使用同一人工 reading 正常情境完整比對。</summary>
    public string Run()
    {
        try
        {
            FeesGoldenFixture.Require(!ran && !AppDomain.CurrentDomain.IsFullyTrusted &&
                !GetType().Assembly.IsFullyTrusted && !typeof(FeeNotifyActivity).Assembly.IsFullyTrusted &&
                !typeof(FeeNotificationService).Assembly.IsFullyTrusted, "restricted one-shot domain");
            ran = true;
            using var fixture = new FeesWorkflowFixture(FeesGoldenFixture.Read("scenarios")["cases"]!.First!);
            fixture.Invoke(new FeeNotifyActivity());
            fixture.Verify(FeesGoldenFixture.Read("expected")["cases"]!.First!);
            return "PASS";
        }
        catch (Exception failure) { return failure.ToString(); }
    }
}

/*
 * 檔案整體說明
 * 目的與定位：P5-09 三個真 QR Workflow 活動的離線部分信任測試，不以 metadata、
 * FullTrust 或純 Core 規則測試替代沙箱可執行性；不使用 Moq 或私有反射。
 * 主要流程：每案建立僅 Execution、輸出閉包 Read／PathDiscovery 的 AppDomain，
 * 唯一 FullTrust 白名單為平台 SDK 強名；子域確認測試、WF、Plugin 及 Core 皆受限，
 * 再經真 WorkflowInvoker 與手寫 SDK 跑完整 Shadow，驗正式來源及有界零真寫入。
 * 資源與隔離：fixture 是服務、回呼及合成資料的唯一 owner，using 同步排空並清除，
 * 父域 finally 無條件卸載子域，無網路、秘密、檔案寫入、Session 或背景工作。
 * 十秒 Invoke 預算不宣稱可中斷同步程式；固定來源與讀取上限約束工作量。
 * 使用邊界與維護：只證明離線受限 CLR 執行，不宣稱 D365 實機授權或長時間 soak。
 */
extern alias Workflows;
using System;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Lessons;
using SpeechMessageProducts.GoodShepherd.Core.QrCodes;
using Xunit;
using MeetingActivity = Workflows::SpeechMessage.Dynamics.Workflows.QrCodes.MeetingQrCodeActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>每案獨占並卸載受限網域，父域才使用 xUnit，子域不能借用父域服務。</summary>
public sealed class QrCodePartialTrustTests
{
    /// <summary>三外殼都須真正執行 SDK／Shadow 鏈，任何擴權、序列化錯誤或截斷都失敗。</summary>
    [Theory]
    [InlineData("meeting")]
    [InlineData("personal")]
    [InlineData("scan")]
    public void PartialTrust_QrCodesShadowChain_Runs(string entrypoint)
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        var sdk = typeof(IPlugin).Assembly.Evidence.GetHostEvidence<StrongName>();
        Assert.NotNull(sdk);
        var domain = AppDomain.CreateDomain("P5-QR-" + Guid.NewGuid().ToString("N"), null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile },
            permissions, sdk);
        try
        {
            var probe = (QrCodePartialTrustProbe)domain.CreateInstanceAndUnwrap(
                typeof(QrCodePartialTrustProbe).Assembly.FullName, typeof(QrCodePartialTrustProbe).FullName);
            var result = probe.Run(entrypoint);
            Assert.True(string.Equals("PASS", result, StringComparison.Ordinal), result);
        }
        finally { AppDomain.Unload(domain); }
    }
}

/// <summary>公開跨域探針只交換固定案例字串及結果；實際資料、活動、服務均在子域建立。</summary>
public sealed class QrCodePartialTrustProbe : MarshalByRefObject
{
    /// <summary>同探針只執行一次，不在失敗後沿用半完成平台狀態。</summary>
    private bool ran;

    /// <summary>確認四層皆非完整信任，直接執行指定外殼，錯誤保留診斷交父域而不吞掉。</summary>
    public string Run(string entrypoint)
    {
        try
        {
            QrCodeCasFixture.Require(!ran && !AppDomain.CurrentDomain.IsFullyTrusted &&
                !GetType().Assembly.IsFullyTrusted && !typeof(MeetingActivity).Assembly.IsFullyTrusted &&
                !typeof(LessonLinkPlugin).Assembly.IsFullyTrusted &&
                !typeof(QrImageTaskService).Assembly.IsFullyTrusted &&
                !typeof(MeetingScanCountService).Assembly.IsFullyTrusted, "restricted one-shot domain");
            ran = true;
            using var fixture = new QrCodeCasFixture(entrypoint, depth: 8);
            var output = fixture.Invoke(QrCodeCasFixture.CreateActivity(entrypoint));
            QrCodeCasFixture.Require(output.TryGetValue("OutputResult", out var value) && value is int number && number == 0,
                "unassigned output default");
            fixture.Verify();
            return "PASS";
        }
        catch (Exception failure) { return failure.ToString(); }
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：P5-07 課程六個正式入口的離線 CAS 驗證，保護連結待辦、設備更新與
 * 簽核通知在部分信任下仍能產生完整 Shadow 意圖，不以純規則或完整信任代替外殼。
 * 主要功能與流程：每個具名案例建立獨立 AppDomain，只授 Execution 與輸出閉包
 * Read／PathDiscovery，唯一完整信任白名單為平台 Xrm SDK。子域驗證測試、Plugins、
 * Workflows 及課程 Core 均受限，再以手寫 SDK／context 執行正式產品鏈。
 * 使用邊界與維護：父域使用 xUnit，子域只用一般例外與合成結果字串，不使用 Moq、
 * 網路、秘密、寫檔或私有反射權限。fixture 同步清空合成資料與服務，父域 finally
 * 無條件卸載；活動傳入十秒 Invoke 等待預算，但不能強制中斷同步 CodeActivity。
 * 六個入口都依固定資料與讀取上限約束工作量，沒有另設程序終止期限；
 * 這不是 D365 實機、安全沙箱或長時間壓力驗收。
 */
extern alias Workflows;

using System;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Lessons;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using Xunit;
using LessonActivity = Workflows::SpeechMessage.Dynamics.Workflows.Lessons.LessonQrCodeActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>六案各自擁有並卸載受限域，不讓其他案例沿用 SDK、context 或通知狀態。</summary>
public sealed class LessonPartialTrustTests
{
    /// <summary>
    /// 直接執行指定正式入口的正常 Shadow 案例；檢查完整意圖、正式身分與零真 SDK
    /// 寫入，任一例外或權限放寬皆失敗。所有 xUnit 斷言只在父網域執行。
    /// </summary>
    /// <param name="entrypoint">固定案例代號，由 InlineData 決定，不是組織或使用者輸入。</param>
    [Theory]
    [InlineData("LessonProgressionPlugin")]
    [InlineData("LessonLinkPlugin")]
    [InlineData("StorLessonSigningPlugin")]
    [InlineData("LessonQrCodeActivity")]
    [InlineData("EquipmentStatusActivity")]
    [InlineData("StorLessonSigningActivity")]
    public void PartialTrust_LessonShadowChain_Runs(string entrypoint)
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        var sdk = typeof(IPlugin).Assembly.Evidence.GetHostEvidence<StrongName>();
        Assert.NotNull(sdk);
        var domain = AppDomain.CreateDomain("P5-Lessons-" + Guid.NewGuid().ToString("N"), null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile },
            permissions, sdk);
        try
        {
            var probe = (LessonPartialTrustProbe)domain.CreateInstanceAndUnwrap(
                typeof(LessonPartialTrustProbe).Assembly.FullName, typeof(LessonPartialTrustProbe).FullName);
            var result = probe.Run(entrypoint);
            Assert.True(string.Equals("PASS", result, StringComparison.Ordinal), result);
        }
        finally { AppDomain.Unload(domain); }
    }
}

/// <summary>公開跨域探針只回傳結果字串；服務與資料全在子域建立，不能借用完整信任父域。</summary>
public sealed class LessonPartialTrustProbe : MarshalByRefObject
{
    /// <summary>每個探針至多執行一案；即使失敗也不能重試沿用半完成狀態。</summary>
    private bool ran;

    /// <summary>
    /// 先確認實際執行組件與網域均非完整信任，再執行單案；不載入 xUnit 斷言或
    /// 動態代理。失敗診斷只包含固定測試輸入與例外，不會讀取真設定或機密。
    /// </summary>
    /// <param name="entrypoint">六個白名單代號之一，其餘一律拒絕。</param>
    /// <returns>完整驗證成功才回 PASS，其他情況回原始例外診斷供父域判定失敗。</returns>
    public string Run(string entrypoint)
    {
        try
        {
            if (ran || AppDomain.CurrentDomain.IsFullyTrusted || GetType().Assembly.IsFullyTrusted ||
                typeof(LessonLinkPlugin).Assembly.IsFullyTrusted || typeof(LessonActivity).Assembly.IsFullyTrusted ||
                typeof(LessonLinkService).Assembly.IsFullyTrusted)
                throw new InvalidOperationException("A restricted one-shot lesson domain is required.");
            ran = true;
            LessonCasFixture.Run(entrypoint);
            return "PASS";
        }
        catch (Exception failure) { return failure.ToString(); }
    }
}

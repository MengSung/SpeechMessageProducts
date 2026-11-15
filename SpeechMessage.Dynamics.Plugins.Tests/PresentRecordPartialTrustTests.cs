/*
 * 檔案整體說明
 *
 * 目的與定位：P5-06 出席真入口的部分信任驗證，重用同份六案及真 Shadow
 * 外殼，不以 FullTrust 或單純規則呼叫冒充沙箱相容性。
 * 主要功能與流程：父域只授 Execution 與測試閉包唯讀，平台 SDK 為唯一 full
 * trust 例外；一次性子域確認產品組件受限後執行六案、逐欄比對及零真寫入。
 * 未授網路、寫檔、私有反射權限，使用RemoteExecutionContext而非動態Mock。
 * 使用邊界與維護：每次新建域，無論成功或失敗 finally 卸載；子域內 reader、
 * SDK 與 recorder 由既有 runner 同步 using 排空，只跨域傳純結果字串。
 * 沒有真資料、秘密、連線、排程或長存 scope；本測試仍不代表 D365 實機驗收。
 */
using System;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Attendance;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>每次建立獨立 CAS 域，不擴大產品權限或保存跨測試服務。</summary>
public sealed class PresentRecordPartialTrustTests
{
    /// <summary>真出席入口執行共用六案；任一意圖、本文摘要、隔離或權限失敗皆拒絕成功。</summary>
    [Fact]
    public void PartialTrust_ShellShadowChain_Runs()
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        var sdk = typeof(IPlugin).Assembly.Evidence.GetHostEvidence<StrongName>();
        Assert.NotNull(sdk);
        var domain = AppDomain.CreateDomain("P5-Attendance-" + Guid.NewGuid().ToString("N"), null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile },
            permissions, sdk);
        try
        {
            var probe = (PresentRecordPartialTrustProbe)domain.CreateInstanceAndUnwrap(
                typeof(PresentRecordPartialTrustProbe).Assembly.FullName, typeof(PresentRecordPartialTrustProbe).FullName);
            Assert.Equal("PASS", probe.Run());
        }
        finally { AppDomain.Unload(domain); }
    }
}

/// <summary>一次性跨域測試入口，只回合成診斷，產品組件不能被列入完整信任白名單。</summary>
public sealed class PresentRecordPartialTrustProbe : MarshalByRefObject
{
    /// <summary>僅限制當域內單次呼叫，父 owner 隨後卸載整個域。</summary>
    private bool ran;
    /// <summary>在受限域實讀六案並執行真外殼；沒有 xUnit 或動態代理參與產品鏈。</summary>
    /// <returns>所有合成案例吻合才 PASS；錯誤為無機密的測試診斷。</returns>
    public string Run()
    {
        try
        {
            if (ran || AppDomain.CurrentDomain.IsFullyTrusted || GetType().Assembly.IsFullyTrusted ||
                typeof(PresentRecordPlugin).Assembly.IsFullyTrusted)
                throw new InvalidOperationException("A restricted one-shot product domain is required.");
            ran = true;
            PresentRecordGoldenFixture.RunAll();
            return "PASS";
        }
        catch (Exception failure) { return failure.ToString(); }
    }
}

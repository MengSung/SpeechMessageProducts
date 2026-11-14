/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 switch 命令從參數解析到真切換核心的離線接線，防止核心測試
 * 通過但公開 CLI 沒有路由。使用 Fixture 的合成 SDK，不讀 User Secrets 或連網。
 * 主要功能與流程：依現行唯一 diagnostics 清單開關步驟，確認正式 ID、狀態、
 * 固定輸出、組織鎖與 lease 歸還。非法組織、模式或未提供舊輪範圍在連線前拒絕。
 * 使用邊界與維護：本檔不建立真實 Plugin DLL、不啟用實機步驟；讀 repo 清單
 * 只為確認正式診斷契約。每個 fixture 根與 writer、lease 都由案例確定釋放。
 */
using System;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以實際 CLI 路由驗證模組切換，不以 helper 直接呼叫假造公開命令成功。</summary>
[Collection("Deployment working directory")]
public sealed class ModuleSwitchCliTests
{
    /// <summary>enabled→off 均成功且每次歸還租約；回到清單狀態與同一個資料庫步驟。</summary>
    [Fact]
    public void Switch_Cli_EnabledThenOff_DisposesLeasesAndPreservesStepId()
    {
        using var f = new Fixture();
        f.Engine.Apply(f.Plan(), f.Manifest, f.Image);
        var stepId = Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstep")).Id;
        var connector = new SwitchConnector(f.Service);
        using var output = new StringWriter();
        Assert.Equal(0, DeploymentCli.Run(["switch", "--org", Fixture.Organization, "--module", "diagnostics", "--to", "enabled"],
            connector, output, _ => Assert.Fail("Unexpected failure"), f.Root));
        Assert.Equal(0, f.Service.Rows.Single(row => row.Id == stepId).GetAttributeValue<OptionSetValue>("statecode").Value);
        Assert.Equal(0, DeploymentCli.Run(["switch", "--org", Fixture.Organization, "--module", "diagnostics", "--to", "off"],
            connector, output, _ => Assert.Fail("Unexpected failure"), f.Root));
        Assert.Empty(f.Plan().Operations);
        Assert.Equal(2, connector.Connects);
        Assert.Equal(2, connector.Disposals);
        Assert.DoesNotContain(Fixture.SecretValue, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>未知組織、目標及未帶程序內舊輪範圍均在 SDK 連線前拒絕，不能遺留啟用步驟。</summary>
    /// <param name="organization">組織故障輸入。</param><param name="target">目標故障輸入。</param>
    [Theory]
    [InlineData("production", "enabled")]
    [InlineData("goodshepherdback", "Live")]
    [InlineData("goodshepherdback", "legacy")]
    public void Switch_Cli_InvalidOrUnscopedLegacy_RefusesBeforeConnect(string organization, string target)
    {
        using var f = new Fixture();
        var connector = new SwitchConnector(f.Service);
        using var output = new StringWriter();
        Assert.Equal(2, DeploymentCli.Run(["switch", "--org", organization, "--module", "diagnostics", "--to", target],
            connector, output, _ => Assert.Fail("Unexpected failure"), f.Root));
        Assert.Equal(0, connector.Connects);
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>案例專用連線 lease owner；只計數不擁有網路資源，不保留跨案例服務。</summary>
    private sealed class SwitchConnector(IOrganizationService service) : IOrganizationConnector
    {
        /// <summary>本案例 Connect 次數。</summary>
        public int Connects { get; private set; }
        /// <summary>每次命令租約歸還次數。</summary>
        public int Disposals { get; private set; }
        /// <summary>建立單次租約，由 CLI 在所有返回路徑釋放。</summary>
        /// <param name="organization">CLI 已驗證的組織。</param><returns>只借用合成服務的租約。</returns>
        public OrganizationLease Connect(string organization)
        {
            Connects++;
            return new OrganizationLease(service, () => Disposals++);
        }
    }
}

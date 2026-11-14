/*
 * 檔案整體說明
 *
 * 目的與定位：保護部署工具更新回復前置的安全版本輸出；使用真實 CLI 與部署核心，
 * 只借用 DeploymentContractTests 的記憶體 SDK／虛擬時鐘，不連線 D365 或 LINE。
 *
 * 主要功能與流程：CLI 讀取本次 task 關聯的合成診斷追蹤，只能輸出唯一、四段且每段
 * 不超過 65535 的正規 PluginFileVersion。測試注入重複、溢位、非正規數字、控制字元、
 * 秘密及字首／字尾偽造；以退出碼、完整輸出及 task 刪除證明拒絕時不發布原始追蹤。
 *
 * 使用邊界與維護：每案獨占 Fixture、writer、服務與輸出根；CLI 租約由命令釋放，
 * Fixture Dispose 刪除暫存根。所有秘密皆為固定合成值，這些離線斷言不代表實機驗收。
 * 維護時須保留舊兩參數 Smoke 契約、三分鐘上限、取消分類與 finally 清理行為。
 */
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>
/// 以真實命令入口驗證版本白名單及追蹤遮罩；每案以獨立假組織注入故障，
/// 並斷言唯一租約、task 與計時器回到基線，不需要網路或正式秘密。
/// </summary>
public sealed class SmokeVersionTests
{
    /// <summary>完整追蹤混有合成秘密時，仍只能輸出驗證後版本與固定成功摘要。</summary>
    /// <param name="version">合法且已正規化的四段組件檔案版本。</param>
    [Theory]
    [InlineData("1.0.0.42")]
    [InlineData("0.0.0.0")]
    [InlineData("65535.65535.65535.65535")]
    public void Cli_Smoke_PrintsOnlyValidatedVersion(string version)
    {
        using var fixture = new Fixture();
        fixture.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready\r\nPluginFileVersion=" +
            version + "\r\n" + Fixture.SecretValue;
        var connector = new SmokeConnector(fixture.Service);
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exit = RunSmoke(fixture, connector, output);

        Assert.Equal(0, exit);
        Assert.Equal("PluginFileVersion=" + version + output.NewLine +
            "Smoke scenario passed." + output.NewLine, output.ToString());
        AssertReleased(fixture, connector);
    }

    /// <summary>
    /// 成功標記不能使不可信版本通過；注入非正規欄位與輸出注入字串，
    /// 必須回失敗、完全不發布版本或秘密，且仍清理本次 task 與租約。
    /// </summary>
    /// <param name="versionLine">附加於合法成功標記後的惡意或缺失版本欄位。</param>
    [Theory]
    [InlineData("")]
    [InlineData("PluginFileVersion=")]
    [InlineData("PluginFileVersion=1.2.3")]
    [InlineData("PluginFileVersion=1.2.3.4.5")]
    [InlineData("PluginFileVersion=1.2.3.65536")]
    [InlineData("PluginFileVersion=999999999999.2.3.4")]
    [InlineData("PluginFileVersion=01.2.3.4")]
    [InlineData("PluginFileVersion=1.2.3.+4")]
    [InlineData("PluginFileVersion=1.2.3.-4")]
    [InlineData("PluginFileVersion=１.2.3.4")]
    [InlineData("PluginFileVersion=1.2.3.4 ")]
    [InlineData(" PluginFileVersion=1.2.3.4")]
    [InlineData("prefixPluginFileVersion=1.2.3.4")]
    [InlineData("PluginFileVersion=1.2.3.4suffix")]
    [InlineData("PluginFileVersion=1.2.3.4\u001b[2J")]
    [InlineData("PluginFileVersion=1.2.3.4\0")]
    [InlineData("PluginFileVersion=1.2.3.4\rFORGED=output")]
    [InlineData("PluginFileVersion=1.2.3.4\u2028FORGED=output")]
    [InlineData("PluginFileVersion=1.2.3.4\nPluginFileVersion=1.2.3.4")]
    [InlineData("PluginFileVersion=1.2.3.4\nPluginFileVersion=invalid")]
    [InlineData("PluginFileVersion=1.2.3.4\nprefixPluginFileVersion=9.9.9.9")]
    [InlineData("PluginFileVersion=SYNTHETIC-SECRET-NOT-A-CREDENTIAL")]
    [InlineData("PluginFileVersion=1.2.3.4 SYNTHETIC-SECRET-NOT-A-CREDENTIAL")]
    public void Cli_Smoke_RejectsUntrustedVersionWithoutPublishing(string versionLine)
    {
        using var fixture = new Fixture();
        fixture.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready\n" + versionLine;
        var connector = new SmokeConnector(fixture.Service);
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        var exit = RunSmoke(fixture, connector, output);

        Assert.Equal(1, exit);
        Assert.Equal("Smoke scenario failed." + output.NewLine, output.ToString());
        AssertReleased(fixture, connector);
    }

    /// <summary>
    /// 在相同核心連續執行不同版本及缺版本追蹤，另種入不相干 correlation 的有效版本；
    /// 斷言只發布每次 task 所屬版本，缺版本不沿用先前值，所有 task 與 timer 清零。
    /// </summary>
    [Fact]
    public async Task Engine_Smoke_UsesInvocationTraceWithoutRetainingPreviousVersion()
    {
        using var fixture = new Fixture();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        fixture.Service.Seed(new Entity("plugintracelog", Guid.NewGuid())
        {
            ["correlationid"] = Guid.NewGuid(),
            ["typename"] = new StepRegistration().Type,
            ["messageblock"] = "P0-PING OK zh-TW=ready qps-ploc=ready\nPluginFileVersion=9.9.9.9"
        });

        foreach (var version in new[] { "1.0.0.41", "1.0.0.42", "1.0.0.41" })
        {
            fixture.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready\nPluginFileVersion=" + version;
            Assert.True(await fixture.Engine.SmokeAsync("p0-ping-trace", output.WriteLine, CancellationToken.None));
        }

        fixture.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready";
        Assert.False(await fixture.Engine.SmokeAsync("p0-ping-trace", output.WriteLine, CancellationToken.None));
        Assert.Equal(string.Join(output.NewLine, "PluginFileVersion=1.0.0.41",
            "PluginFileVersion=1.0.0.42", "PluginFileVersion=1.0.0.41", ""), output.ToString());
        Assert.Empty(fixture.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Equal(4, fixture.Service.Writes.Count(write => write == "Delete:task"));
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }

    /// <summary>在版本回呼注入同一例外實例；必須保留真實故障並確定性刪除 task、釋放 timer。</summary>
    [Fact]
    public async Task Engine_Smoke_OutputFailurePreservesExceptionAndCleanup()
    {
        using var fixture = new Fixture();
        fixture.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready\nPluginFileVersion=1.0.0.42";
        var failure = new IOException("synthetic-writer-failure");

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() =>
            fixture.Engine.SmokeAsync("p0-ping-trace", _ => throw failure, CancellationToken.None)));

        Assert.Empty(fixture.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Single(fixture.Service.Writes.Where(write => write == "Delete:task"));
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }

    /// <summary>
    /// 兩筆相同 correlation 的診斷追蹤即使版本相同也不能任選；
    /// 注入第二個資料庫 ID 並斷言固定資料衝突、完全無輸出與 task／timer 清理。
    /// </summary>
    [Fact]
    public async Task Engine_Smoke_DuplicateDiagnosticTraceFailsClosed()
    {
        using var fixture = new Fixture();
        fixture.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready\nPluginFileVersion=1.0.0.42";
        fixture.Service.OnTaskCreated = () =>
        {
            var original = fixture.Service.Rows.Single(row => row.LogicalName == "plugintracelog");
            fixture.Service.Seed(new Entity("plugintracelog", Guid.NewGuid())
            {
                ["correlationid"] = original["correlationid"],
                ["typename"] = original["typename"],
                ["messageblock"] = original["messageblock"]
            });
        };
        using var output = new StringWriter(CultureInfo.InvariantCulture);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Engine.SmokeAsync("p0-ping-trace", output.WriteLine, CancellationToken.None));

        Assert.Equal("", output.ToString());
        Assert.Empty(fixture.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Single(fixture.Service.Writes.Where(write => write == "Delete:task"));
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }

    /// <summary>
    /// 不同作業可共用同一 correlation；注入兩筆正式作業 ID 與唯一追蹤，
    /// 兩個入口都須只查該 correlation 一次，不把相同追蹤誤判為兩筆衝突。
    /// 真正不同追蹤 ID 仍由既有案例拒絕；本案返回時 task 與 timer 必須清零。
    /// </summary>
    /// <param name="publishVersion">是否經新版版本發布入口執行。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Engine_Smoke_SharedCorrelationReadsUniqueTraceOnce(bool publishVersion)
    {
        using var fixture = new Fixture();
        fixture.Service.Trace = "P0-PING OK zh-TW=ready qps-ploc=ready\nPluginFileVersion=1.0.0.42";
        fixture.Service.OnTaskCreated = () =>
        {
            var original = fixture.Service.Rows.Single(row => row.LogicalName == "asyncoperation");
            fixture.Service.Seed(new Entity("asyncoperation", Guid.NewGuid())
            {
                ["regardingobjectid"] = original["regardingobjectid"],
                ["correlationid"] = original["correlationid"]
            });
        };
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var success = publishVersion
            ? await fixture.Engine.SmokeAsync("p0-ping-trace", output.WriteLine, CancellationToken.None)
            : await fixture.Engine.SmokeAsync("p0-ping-trace", CancellationToken.None);
        Assert.True(success);
        Assert.Equal(publishVersion ? "PluginFileVersion=1.0.0.42" + output.NewLine : "", output.ToString());
        Assert.Equal(1, fixture.Service.TraceQueryCount);
        Assert.Equal(2, fixture.Service.Rows.Count(row => row.LogicalName == "asyncoperation"));
        Assert.Empty(fixture.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Single(fixture.Service.Writes.Where(write => write == "Delete:task"));
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }

    /// <summary>版本回呼路徑仍受虛擬三分鐘上限與 caller 取消保護；兩者均不得發布部分版本。</summary>
    /// <param name="cancel">是否在首次兩秒延遲時注入 caller 取消，否則等待完整虛擬期限。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Engine_Smoke_VersionOutputHonorsTimeoutAndCancellation(bool cancel)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        if (cancel)
        {
            fixture.Clock.OnDelay = cancellation.Cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.Engine.SmokeAsync("p0-ping-trace", output.WriteLine, cancellation.Token));
        }
        else
        {
            Assert.False(await fixture.Engine.SmokeAsync("p0-ping-trace", output.WriteLine, cancellation.Token));
            Assert.Equal(90, fixture.Clock.DelayCount);
        }

        Assert.Equal("", output.ToString());
        Assert.Empty(fixture.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Single(fixture.Service.Writes.Where(write => write == "Delete:task"));
        Assert.Equal(0, fixture.Clock.ActiveTimers);
    }

    /// <summary>只執行已核准的 trace 情境；任何意外例外通知都使離線案例失敗。</summary>
    /// <param name="fixture">擁有記憶體組織及暫存根的案例。</param>
    /// <param name="connector">僅回傳該組織的假租約來源。</param>
    /// <param name="output">本案例擁有的記憶體輸出。</param>
    /// <returns>真實 CLI 的結束碼。</returns>
    private static int RunSmoke(Fixture fixture, SmokeConnector connector, TextWriter output) =>
        DeploymentCli.Run(
            new[] { "smoke", "--org", Fixture.Organization, "--scenario", "p0-ping-trace" },
            connector, output, exception => Assert.Fail("Unexpected report: " + exception.GetType().Name),
            fixture.Root);

    /// <summary>以確定性計數驗證單一租約歸還且本次 task／虛擬 timer 清零。</summary>
    /// <param name="fixture">測試資源唯一擁有者。</param>
    /// <param name="connector">命令的假連線來源。</param>
    private static void AssertReleased(Fixture fixture, SmokeConnector connector)
    {
        Assert.Equal(1, connector.ReleaseCount);
        Assert.Empty(fixture.Service.Rows.Where(row => row.LogicalName == "task"));
        Assert.Single(fixture.Service.Writes.Where(write => write == "Delete:task"));
        Assert.Equal(0, fixture.Clock.ActiveTimers);
        Assert.Equal(0, fixture.SecretReads);
    }

    /// <summary>
    /// 每案唯一的無網路 connector；只借出 Fixture 所有的記憶體服務，
    /// 租約回呼僅計數，不持有跨案例資料或建立背景資源。
    /// </summary>
    private sealed class SmokeConnector(IOrganizationService service) : IOrganizationConnector
    {
        /// <summary>命令釋放租約的次數；成功與拒絕出口都必須恰為一次。</summary>
        public int ReleaseCount { get; private set; }

        /// <summary>確認固定測試組織後交付本次租約，釋放時只更新案例本地計數。</summary>
        /// <param name="organization">已通過 CLI 白名單的組織名稱。</param>
        /// <returns>CLI 確定性釋放的記憶體服務租約。</returns>
        public OrganizationLease Connect(string organization)
        {
            Assert.Equal(Fixture.Organization, organization);
            return new OrganizationLease(service, () => ReleaseCount++);
        }
    }
}

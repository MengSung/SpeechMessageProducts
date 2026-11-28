/*
檔案整體說明
目的與定位：FS 首次 apply 被計畫外未發布元件拒絕後，使用正式兩來源 reader 的唯讀診斷。
主要流程：只連三個白名單測試組織，先核對伺服器，再讀正式 schema 與 metadata，
以同一租約讀完整 schema 範圍 UI，交離線已驗的投影器輸出管理 ID 及固定差異欄位名稱；
另有只讀 new_linechannel 的單表診斷，不讀全組織 metadata，僅印第一個結構差異的固定類別。
型別轉換診斷另走官方唯讀訊息，只輸出 numericScope 布林；不重做已失敗的表單查詢。
信任與保密：不修改或發布任何資料；不輸出 XML、顯示名稱、秘密、Channel 或 LINE userId。
只讀值不得跨組織混用；每案獨占組態、取消來源、服務租約與純值候選，沒有共用可變狀態。
生命週期：三分鐘 owner 只約束合作取消，入場 SDK 仍等待返回；using 先釋放租約再釋放
組態與取消來源，finally 排空唯一診斷 owner，無 Task.Run、訂閱或遺留背景工作。
失敗與驗證：真故障保留 Exception 至共用診斷，LINE 關閉；輸出僅使用已驗格式，失敗拋固定摘要。
本檔 WP=FSPendingDiagnostics，成功只代表差異讀取成功，不能冒充 WP=FS 或允許略過原 guard。
*/
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using PowerPlatform.Dataverse.Client;
using SpeechMessage.Dynamics.Deployment.Schema;
using SpeechMessage.Dynamics.Deployment.Tests.Ui;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>已／未發布兩來源的唯讀觀察；只有執行明確 trait 才讀秘密及連線。</summary>
[Trait("Category", "Live3P")]
[Trait("WP", "FSPendingDiagnostics")]
public sealed class UiPendingDiagnosticLiveTests
{
    /// <summary>借入框架每案輸出；只能寫已驗固定摘要，不保存服務或 scope。</summary>
    private readonly ITestOutputHelper output;

    /// <summary>框架擁有輸出生命週期，本類不關閉它。</summary>
    /// <param name="output">每案獨立測試摘要。</param>
    public UiPendingDiagnosticLiveTests(ITestOutputHelper output) => this.output = output;

    /// <summary>完整觀察指定組織而不寫入；差異非零是診斷結果，不能據此宣稱部署通過。</summary>
    /// <param name="organization">固定三組織之一，不接受外部網址。</param>
    /// <returns>所有已入場呼叫及 owner 排空後完成。</returns>
    [Theory]
    [InlineData("goodshepherd20")]
    [InlineData("construction")]
    [InlineData("association")]
    public Task ReadOnly_PublishedAndUnpublishedUi_ReportsFixedDifferences(string organization) =>
        RunReadOnly(organization, false);

    /// <summary>只讀一個平台表的表單差異，避免重複完整 metadata 查詢，亦不寫入／發布。</summary>
    /// <returns>同一套組態／租約／診斷 owner 皆已清理後完成。</returns>
    [Fact]
    public Task ReadOnly_OnePlatformForm_ReportsStructuralDifference() => RunReadOnly("goodshepherd20", true);

    /// <summary>只診斷固定表單範圍的官方型別轉換；不執行原先已失敗的表單讀取或任何寫入。</summary>
    [Fact]
    public Task ReadOnly_FormScopeConversion_ReportsNumericShape() => RunReadOnly("goodshepherd20", true, true);

    /// <summary>共用單案 owner；窄診斷只讀固定平台表，完整診斷才讀正式 schema 交集。</summary>
    /// <param name="organization">固定白名單組織。</param><param name="narrow">是否只讀固定平台表。</param>
    /// <param name="conversionOnly">只讀官方查詢轉換結果，不執行其他表單查詢。</param>
    /// <returns>所有資源確定清理後完成。</returns>
    private async Task RunReadOnly(string organization, bool narrow, bool conversionOnly = false)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SpeechMessage", "evidence", "FS-pending-readonly", Guid.NewGuid().ToString("N"));
        ExceptionDiagnostics? diagnostics = null;
        Exception? failure = null;
        try
        {
            diagnostics = new ExceptionDiagnostics(Path.Combine(directory, "Logs"),
                outputOptions: new ExceptionOutputOptions(writeExceptionLog: true, sendLine: false));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var ct = timeout.Token;
            if (organization is not ("goodshepherd20" or "construction" or "association"))
                throw new InvalidOperationException("FS pending diagnostic target rejected.");
            var catalog = OrganizationCatalog.LoadDefault();
            var target = catalog.GetTest(organization);
            _ = ProductionGuard.Authorize(["verify", "--org", organization], catalog, null, ct);
            var configuration = DeploymentConfiguration.Create(typeof(UserSecretsOrganizationConnector).Assembly);
            using var configurationOwner = (IDisposable)configuration;
            using var lease = new UserSecretsOrganizationConnector(configuration).Connect(organization);
            ProductionGuard.VerifyServer(lease.Service, target, ct);
            if (conversionOnly)
            {
                output.WriteLine(UiPendingDiagnosticProbe.DescribeConvertedScope(lease.Service.Execute));
                return;
            }
            var definition = SchemaFiles.LoadForOrganization(DeploymentCli.FindRepositoryRoot(), organization);
            var scope = narrow
                ? UiViewDeployment.ReadPublicationScope(lease.Service, ["new_linechannel"], ct)
                : UiViewDeployment.ReadScope(lease.Service, definition, SchemaMetadata.Read(lease.Service, target, ct), ct);
            var lines = UiPendingDiagnosticProbe.Describe(scope,
                definition.Entities.Select(entity => entity.LogicalName).ToArray());
            output.WriteLine($"FS-PENDING organization={organization} count={lines.Count} diagnosticOnly=true");
            foreach (var line in lines) output.WriteLine(line);
            if (narrow)
            {
                foreach (var row in scope.Unpublished.Where(row => row.Type == "systemform"))
                {
                    var old = scope.Published.Single(item => item.Type == row.Type && item.Id == row.Id);
                    output.WriteLine($"FS-XML-DIFF id={row.Id:D} " +
                        UiPendingDiagnosticProbe.DescribeXmlStructure(old.FetchXml, row.FetchXml));
                    if (old.Hash != row.Hash)
                    {
                        var allLabels = UiPendingDiagnosticProbe.ReadAllLabels(lease.Service.Execute, row.Id, ct);
                        output.WriteLine($"FS-ALL-LABELS id={row.Id:D} " +
                            $"equalsUnpublished={allLabels == row.FetchXml} equalsNormalPublished={allLabels == old.FetchXml}");
                        output.WriteLine("FS-ALL-LABELS-DIFF vs=unpublished " +
                            UiPendingDiagnosticProbe.DescribeXmlStructure(allLabels, row.FetchXml));
                        output.WriteLine("FS-ALL-LABELS-DIFF vs=normal-published " +
                            UiPendingDiagnosticProbe.DescribeXmlStructure(allLabels, old.FetchXml));
                        var single = UiPendingDiagnosticProbe.ReadUnpublishedSingle(lease.Service.Execute, row.Id, ct);
                        output.WriteLine($"FS-SINGLE-UNPUBLISHED equalsAllLabels={single == allLabels} " +
                            $"equalsMultiple={single == row.FetchXml}");
                        output.WriteLine("FS-SINGLE-UNPUBLISHED-DIFF vs=all-labels " +
                            UiPendingDiagnosticProbe.DescribeXmlStructure(allLabels, single));
                        var aliasPublished = UiPendingDiagnosticProbe.ReadAliasedForm(lease.Service.Execute, row.Id, false, ct);
                        var aliasUnpublished = UiPendingDiagnosticProbe.ReadAliasedForm(lease.Service.Execute, row.Id, true, ct);
                        output.WriteLine($"FS-ALIASED bothEqual={aliasPublished == aliasUnpublished} " +
                            $"publishedEqualsAllLabels={aliasPublished == allLabels} unpublishedEqualsAllLabels={aliasUnpublished == allLabels}");
                        output.WriteLine("FS-ALIASED-DIFF " +
                            UiPendingDiagnosticProbe.DescribeXmlStructure(aliasPublished, aliasUnpublished));
                        output.WriteLine($"FS-ALIASED-RAW utf16Bytes={Encoding.Unicode.GetByteCount(aliasPublished)} " +
                            $"sha256={Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(aliasPublished)))}");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            if (exception is FaultException<OrganizationServiceFault> { Detail: { } fault })
                output.WriteLine(UiPendingDiagnosticProbe.DescribeFault(fault));
            failure = exception;
        }
        finally
        {
            if (diagnostics is not null)
            {
                if (failure is not null)
                    _ = diagnostics.Report(failure, "Live3P.FS.PendingDiagnostic", notify: false);
                try { await diagnostics.DisposeAsync(); }
                catch (Exception exception)
                {
                    failure ??= exception;
                    Console.Error.WriteLine("[Live3P.FS.PendingDiagnostic] DiagnosticOwnerDisposeFailed");
                }
            }
        }
        if (failure is not null)
            throw new InvalidOperationException("FS readonly pending diagnostic failed (" +
                failure.GetType().Name + "); sensitive details suppressed.");
    }
}

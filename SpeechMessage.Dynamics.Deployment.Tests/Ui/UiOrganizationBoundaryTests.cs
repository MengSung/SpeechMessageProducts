/*
檔案整體說明
目的與定位：驗證 FS 公開 UI 入口在建立 SDK 租約前即綁定產品方案與核定組織，
避免名稱合法的另一產品方案被匯出或匯入錯誤組織。本檔只有離線 parser 與路由證據。
主要流程：以正式 catalog 呼叫 DeploymentCli，注入只計數且立即拋固定例外的 connector；
反例必須回語法拒絕且零次連線，正例必須抵達 connector，防止全面拒絕也能通過測試。
所有輸入皆為固定公開組織／方案名稱；匯入案例建立已通過封裝器的合成 ZIP 與合法 artifacts
路徑，避免把壞路徑拒絕誤認為產品隔離；不讀 User Secrets、業務資料或真正 SDK。
資源與隔離：每案擁有自己的 writer、錯誤清單與 connector；沒有跨測試可變狀態，
沒有連線、背景工作、timer 或訂閱。匯入正例短暫取得正式組織鎖但 connector 在建立租約前失敗，
命令必須同步釋放鎖；案例於 finally 刪除自己的唯一暫存根，writer 與 ZIP stream 於返回前 Dispose。
故障注入與斷言：正例的固定 connector 故障僅用於觀察路由，必須原樣交共用 report；
反例必須完全不觸發 report 或 connector。這些結果不代表真實方案已存在、匯入或發布。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>保護公開 UI 命令的產品／組織配對；完全不建立外部依賴。</summary>
public sealed class UiOrganizationBoundaryTests
{
    /// <summary>合法白名單方案配到別的產品時仍須在連線前拒絕；不能只核對單一方案名稱。</summary>
    [Theory]
    [InlineData("construction", "GoodShepherd2Ui")]
    [InlineData("association", "GoodShepherd2Ui")]
    [InlineData("goodshepherd20", "GoodButlerUi")]
    [InlineData("association", "GoodButlerUi")]
    [InlineData("goodshepherd20", "GoodGuildUi")]
    [InlineData("construction", "GoodGuildUi")]
    [InlineData("goodshepherdback", "SpeechMessagePlatformUi")]
    [InlineData("goodshepherd20", "SpeechMessageUi")]
    public void Ui_PublicExport_OtherProductSolution_RefusedBeforeConnect(string organization, string solution)
    {
        using var output = new StringWriter();
        var connector = new StopConnector();
        var reported = new List<Exception>();
        var result = DeploymentCli.RunWithCatalog(
            ["ui-export", "--org", organization, "--solution", solution],
            OrganizationCatalog.LoadDefault(), connector, output, reported.Add, null);
        Assert.Equal(2, result);
        Assert.Equal(0, connector.Calls);
        Assert.Empty(reported);
    }

    /// <summary>每個核定配對都必須可抵達連線邊界；connector 的固定失敗要保留並上報，不冒稱匯出成功。</summary>
    [Theory]
    [InlineData("goodshepherdback", "SpeechMessageUi")]
    [InlineData("goodshepherd20", "SpeechMessagePlatformUi")]
    [InlineData("construction", "SpeechMessagePlatformUi")]
    [InlineData("association", "SpeechMessagePlatformUi")]
    [InlineData("goodshepherd20", "GoodShepherd2Ui")]
    [InlineData("construction", "GoodButlerUi")]
    [InlineData("association", "GoodGuildUi")]
    public void Ui_PublicExport_ApprovedPair_ReachesConnector(string organization, string solution)
    {
        using var output = new StringWriter();
        var connector = new StopConnector();
        var reported = new List<Exception>();
        var result = DeploymentCli.RunWithCatalog(
            ["ui-export", "--org", organization, "--solution", solution],
            OrganizationCatalog.LoadDefault(), connector, output, reported.Add, null);
        Assert.Equal(1, result);
        Assert.Equal(1, connector.Calls);
        Assert.Same(connector.Failure, Assert.Single(reported));
    }

    /// <summary>
    /// 錯產品的匯入使用存在、路徑合法且封裝驗證成功的 ZIP，仍必須在連線與取鎖前拒絕；
    /// 不能依不存在檔案或解析失敗取得假通過。測試唯一暫存根於 finally 清理。
    /// </summary>
    [Theory]
    [InlineData("construction", "GoodShepherd2Ui")]
    [InlineData("association", "GoodShepherd2Ui")]
    [InlineData("goodshepherd20", "GoodButlerUi")]
    [InlineData("association", "GoodButlerUi")]
    [InlineData("goodshepherd20", "GoodGuildUi")]
    [InlineData("construction", "GoodGuildUi")]
    [InlineData("goodshepherdback", "SpeechMessagePlatformUi")]
    [InlineData("goodshepherd20", "SpeechMessageUi")]
    public void Ui_PublicImport_ValidArchiveForOtherProduct_RefusedBeforeConnect(string organization, string solution)
        => RunImportBoundary(organization, solution, false);

    /// <summary>
    /// 每個核定匯入配對必須實際越過合法路徑及寫鎖到 connector；原固定故障須上報一次，
    /// 不可讓全面拒絕匯入的實作通過反例測試。using／finally 同步歸還鎖並刪除本案檔案。
    /// </summary>
    [Theory]
    [InlineData("goodshepherdback", "SpeechMessageUi")]
    [InlineData("goodshepherd20", "SpeechMessagePlatformUi")]
    [InlineData("construction", "SpeechMessagePlatformUi")]
    [InlineData("association", "SpeechMessagePlatformUi")]
    [InlineData("goodshepherd20", "GoodShepherd2Ui")]
    [InlineData("construction", "GoodButlerUi")]
    [InlineData("association", "GoodGuildUi")]
    public void Ui_PublicImport_ApprovedPairAndValidArchive_ReachesConnector(string organization, string solution)
        => RunImportBoundary(organization, solution, true);

    /// <summary>
    /// 建立單案 artifacts 與真正 ZIP，先獨立確認封裝器接受，再觀察公開命令的產品配對；
    /// 不執行 SDK 或修改組織。唯一隨機根由本方法擁有，成功或失敗都清理。
    /// </summary>
    private static void RunImportBoundary(string organization, string solution, bool approved)
    {
        var root = Path.Combine(Path.GetTempPath(), "smv-ui-org-" + Guid.NewGuid().ToString("N"));
        try
        {
            var directory = Path.Combine(root, organization);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "candidate.zip");
            var bytes = CreateArchive(solution);
            Assert.Equal(3, UiSolutionArchive.Prepare(bytes, solution).Count);
            File.WriteAllBytes(path, bytes);
            using var output = new StringWriter();
            var connector = new StopConnector();
            var reported = new List<Exception>();
            var result = DeploymentCli.RunWithCatalog(
                ["ui-import", "--org", organization, "--solution", solution, "--from", path],
                OrganizationCatalog.LoadDefault(), connector, output, reported.Add, null, root);
            Assert.Equal(approved ? 1 : 2, result);
            Assert.Equal(approved ? 1 : 0, connector.Calls);
            if (approved) Assert.Same(connector.Failure, Assert.Single(reported));
            else Assert.Empty(reported);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>只建立純合成未受管理方案；輸出與各 entry 均由方法擁有，返回前完整關閉 ZIP。</summary>
    private static byte[] CreateArchive(string solution)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "solution.xml",
                "<ImportExportXml><SolutionManifest><UniqueName>" + solution
                + "</UniqueName><Managed>0</Managed></SolutionManifest></ImportExportXml>");
            WriteEntry(archive, "customizations.xml", "<ImportExportXml><Entities /></ImportExportXml>");
            WriteEntry(archive, "[Content_Types].xml",
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
        }
        return stream.ToArray();
    }

    /// <summary>合成 XML 不含 caller 資料；以固定 UTF-8 寫入並在建立下一成員前釋放當前串流。</summary>
    private static void WriteEntry(ZipArchive archive, string name, string text)
    {
        using var output = archive.CreateEntry(name).Open();
        output.Write(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>只觀察命令是否試圖連線，固定故障不包含任何設定或來源值，也不擁有真正租約。</summary>
    private sealed class StopConnector : IOrganizationConnector
    {
        /// <summary>此案例的原始固定故障，供驗證共用回報不替換或吞掉失敗。</summary>
        internal Exception Failure { get; } = new InvalidOperationException("Synthetic connector boundary.");
        /// <summary>本案例累計連線嘗試次數，不能跨案例共用。</summary>
        internal int Calls { get; private set; }
        /// <summary>觸發一次計數即拋出本案固定故障，不解析組織設定或取得外部資源。</summary>
        public OrganizationLease Connect(string organization)
        {
            Calls++;
            throw Failure;
        }
    }
}

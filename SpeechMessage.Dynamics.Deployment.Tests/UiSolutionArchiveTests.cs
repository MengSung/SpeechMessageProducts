/*
檔案整體說明
目的與定位：以記憶體 ZIP 與合成 Dynamics 解決方案 XML，驗證 P5-03b 匯出入共用的封裝邊界，並非實機匯出結果。
主要功能與流程：核對共用實體表單移除但檢視與自有實體表單保留；注入越界路徑、大小寫重複、壓縮膨脹、DTD 及錯誤方案身分，驗證完整候選失敗而非發布部分資料。合成多成員 ZIP 分別跨越累計解壓 16MiB 與 128 成員邊界；以執行緒配置量驗證封裝在超額輸入及 ZIP 標頭超額時停止，不只斷言最終例外。
雙來源表單 FetchXML 在假 SDK 邊界還原供原 Snapshot 欄位及實體範圍斷言核對，再依原始查詢包裝 rawformxml alias；不補缺值、不改合成 XML、正式 ID 或 SDK 請求順序，其餘 QueryExpression 保持原契約。
使用邊界與擁有權：每個案例獨占記憶體串流與 ZIP，using 在斷言失敗時亦釋放；資料只含合成 GUID、固定名稱與測試內容，不讀 User Secrets、不連真實 SDK 服務、不建立背景工作。SDK 替身分別承接已發布查詢與未發布訊息，每次建立獨立快照；相同正式 ID 只有 XML 差異才構成 pending。新增的 RetrieveEntity 只返回受影響表的一致合成結構，配合完整結構護欄，不放寬原 UI 或 ZIP 斷言。RootComponent 測試故障注入未允許型別及非 shell Entity，並以 ImportSolution 呼叫不存在作為 fail-closed 斷言。往返測試核對實際封裝內容；真 Import 案例另保留完整請求順序與計畫外 ID 拒絕斷言，暫存目錄由案例 finally 清理。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Tests.Ui;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護解決方案 ZIP 的來源完整性、共用元件界線及有界記憶體契約；所有輸入皆為離線合成資料。</summary>
public sealed class UiSolutionArchiveTests
{
    private static readonly string[] NonWhitelistedImportArguments =
        { "ui-import", "--org", "production", "--from", "outside.zip" };
    private static readonly string[] ExpectedSdkRequests =
    {
        "ExportSolution",
        "RetrieveMultiple",
        "RetrieveMultiple",
        "RetrieveUnpublishedMultiple",
        "RetrieveUnpublishedMultiple",
        "RetrieveEntity",
        "RetrieveEntity",
        "RetrieveEntity",
        "RetrieveEntity",
        "RetrieveEntity",
        "RetrieveEntity",
        "ImportSolution",
        "RetrieveMultiple",
        "RetrieveMultiple",
        "RetrieveUnpublishedMultiple",
        "RetrieveUnpublishedMultiple",
        "RetrieveEntity",
        "RetrieveEntity",
        "RetrieveEntity",
        "RetrieveEntity",
        "RetrieveEntity",
        "RetrieveEntity",
        "PublishXml"
    };

    /// <summary>共用 account／contact 表單及其根參照必須移除；相同集合中的檢視及本產品實體表單必須完整保留。</summary>
    [Fact]
    public void UiExport_SharedEntityForm_NotPackaged()
    {
        var result = UiSolutionArchive.Prepare(Archive());
        var customizations = XDocument.Parse(Encoding.UTF8.GetString(result["customizations.xml"]));
        var entities = customizations.Descendants("Entity").ToArray();
        Assert.Equal(3, entities.Length);
        foreach (var entity in entities.Where(x => x.Element("Name")!.Value is "account" or "contact"))
        {
            Assert.Null(entity.Element("FormXml"));
            Assert.NotNull(entity.Element("SavedQueries"));
        }
        Assert.NotNull(entities.Single(x => x.Element("Name")!.Value == "new_example").Element("FormXml"));
        var solution = XDocument.Parse(Encoding.UTF8.GetString(result["solution.xml"]));
        var roots = solution.Descendants("RootComponent").ToArray();
        Assert.Single(roots.Where(x => (string?)x.Attribute("type") == "60"));
        Assert.All(roots.Where(x => (string?)x.Attribute("type") == "1"),
            x => Assert.Equal("2", (string?)x.Attribute("behavior")));
        Assert.Equal(new byte[] { 1, 2, 3 }, result["WebResources/smv_icon"]);
    }

    /// <summary>壓縮檔的父目錄、絕對路徑、Windows 別名與反斜線均不能成為輸出路徑；拒絕發生於任何檔案發布之前。</summary>
    [Theory]
    [InlineData("../escape.xml")]
    [InlineData("/escape.xml")]
    [InlineData("C:/escape.xml")]
    [InlineData("folder\\escape.xml")]
    [InlineData("folder/../escape.xml")]
    [InlineData("folder/CON.xml")]
    [InlineData("folder/name. ")]
    public void UiImport_ZipEntryOutsideRoot_Rejected(string name)
    {
        Assert.Throws<InvalidDataException>(() => UiSolutionArchive.Prepare(Archive(extra: name)));
    }

    /// <summary>單一高度可壓縮大檔不能繞過解壓後上限；測試只配置比單檔預算多一個位元組的有限候選。</summary>
    [Fact]
    public void UiImport_OversizedArchive_Rejected()
    {
        Assert.Throws<InvalidDataException>(() => UiSolutionArchive.Prepare(
            Archive(extra: "WebResources/large", content: new byte[8 * 1024 * 1024 + 1])));
    }

    /// <summary>
    /// 每個成員均小於 8MiB、總數小於 128、壓縮檔小於 1MiB，仍須因累計解壓超過
    /// 16MiB 而拒絕；不同分塊大小防止只修成單檔限制，所有 ZIP 由本案例同步釋放。
    /// </summary>
    [Theory]
    [InlineData(6 * 1024 * 1024)]
    [InlineData(512 * 1024)]
    public void Prepare_IndividuallySmallEntries_ExceedTotalExpandedBudget_Rejected(int entryBytes)
    {
        var archive = ArchiveAtExpandedSize(18 * 1024 * 1024, entryBytes);
        Assert.True(archive.Length < 1024 * 1024);

        Assert.Throws<InvalidDataException>(() => UiSolutionArchive.Prepare(archive));
    }

    /// <summary>累計解壓恰為 16MiB 且單檔不超過 8MiB 時仍可完整發布候選，避免用縮小既有限制取代總預算。</summary>
    [Fact]
    public void Prepare_ExactTotalExpandedBudget_RetainsAllResources()
    {
        var archive = ArchiveAtExpandedSize(16 * 1024 * 1024, 8 * 1024 * 1024);

        var files = UiSolutionArchive.Prepare(archive);

        Assert.Equal(6, files.Count);
        Assert.Equal(8 * 1024 * 1024, files["WebResources/budget-0"].Length);
        Assert.Contains("WebResources/budget-1", files.Keys);
    }

    /// <summary>即使額外成員全空，129 個成員仍不得通過；與 16MiB 累計大小的拒絕原因分開驗證。</summary>
    [Fact]
    public void Prepare_TooManyEmptyEntries_Rejected()
    {
        using var stream = new MemoryStream();
        stream.Write(Archive());
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            for (var index = 0; index < 125; index++)
                zip.CreateEntry("WebResources/empty-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Throws<InvalidDataException>(() => UiSolutionArchive.Prepare(stream.ToArray()));
    }

    /// <summary>
    /// 三個合法 8MiB 成員的合計已超額；封裝須在建立大輸出前拒絕。
    /// 配置量只量正式 Pack 呼叫，2MiB 容差遠低於 24MiB 輸出，避免只在完整配置後擲例外仍通過。
    /// </summary>
    [Fact]
    public void Pack_TotalPayloadOverBudget_RejectedBeforeLargeAllocation()
    {
        var payload = new byte[8 * 1024 * 1024];
        var files = new Dictionary<string, byte[]>
        {
            ["a"] = payload,
            ["b"] = payload,
            ["c"] = payload
        };
        var before = GC.GetAllocatedBytesForCurrentThread();

        var error = Record.Exception(() => UiSolutionArchive.Pack(files));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsType<InvalidDataException>(error);
        Assert.InRange(allocated, 0, 2 * 1024 * 1024);
    }

    /// <summary>
    /// 原文剛好 16MiB，或只預留 128 bytes 而在中央目錄關閉時超額，均不得先擴容至 32MiB。
    /// 執行緒配置量是歷次陣列總和，32MiB 容差涵蓋 8MiB→16MiB 等合法擴容與小型 ZIP 狀態；
    /// 原始完整封裝後才拒絕的實作需約 56MiB 累計配置，不能通過此斷言。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(128)]
    public void Pack_ZipOverheadExceedsBudget_DoesNotGrowOversizedBuffer(int reservedBytes)
    {
        var payload = new byte[8 * 1024 * 1024];
        var files = new Dictionary<string, byte[]>
        {
            ["a"] = payload,
            ["b"] = reservedBytes == 0 ? payload : new byte[payload.Length - reservedBytes]
        };
        var before = GC.GetAllocatedBytesForCurrentThread();

        var error = Record.Exception(() => UiSolutionArchive.Pack(files));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.IsType<InvalidDataException>(error);
        Assert.InRange(allocated, 0, 32 * 1024 * 1024);
    }

    /// <summary>預留足夠 ZIP 標頭空間的近上限候選可以往返；輸出與每個原始資源均保持完整。</summary>
    [Fact]
    public void Pack_NearOutputBudget_PreservesEveryByte()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["a"] = new byte[8 * 1024 * 1024],
            ["b"] = new byte[8 * 1024 * 1024 - 1024]
        };

        var packed = UiSolutionArchive.Pack(files);

        Assert.InRange(packed.Length, 16 * 1024 * 1024 - 1024, 16 * 1024 * 1024);
        using var stream = new MemoryStream(packed, writable: false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var item in files)
        {
            using var source = zip.GetEntry(item.Key)!.Open();
            using var target = new MemoryStream();
            source.CopyTo(target);
            Assert.Equal(item.Value, target.ToArray());
        }
    }

    /// <summary>Windows 相同身分的兩個路徑必須拒絕，不能用字典最後寫入覆蓋來隱藏衝突。</summary>
    [Fact]
    public void DuplicateArchiveIdentity_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() => UiSolutionArchive.Prepare(Archive(extra: "SOLUTION.XML")));
    }

    /// <summary>外部實體與錯誤方案／受管理封裝不可進入匯入候選；解析例外不得攜帶原 XML 本文。</summary>
    [Theory]
    [InlineData("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///private'>]><ImportExportXml>&e;</ImportExportXml>")]
    [InlineData("<ImportExportXml><SolutionManifest><UniqueName>Other</UniqueName><Managed>0</Managed></SolutionManifest></ImportExportXml>")]
    [InlineData("<ImportExportXml><SolutionManifest><UniqueName>SpeechMessageUi</UniqueName><Managed>1</Managed></SolutionManifest></ImportExportXml>")]
    public void InvalidSolutionXml_IsRejected(string solution)
    {
        var error = Assert.Throws<InvalidDataException>(() => UiSolutionArchive.Prepare(Archive(solution: solution)));
        Assert.DoesNotContain("private", error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    /// <summary>經過真 ZIP 重壓與再驗證後，剩餘路徑與每個位元組必須一致，不得遺失非 XML 資源。</summary>
    [Fact]
    public void ArchiveRoundTrip_PreservesEveryRetainedComponent()
    {
        var expected = UiSolutionArchive.Prepare(Archive());
        var actual = UiSolutionArchive.Prepare(UiSolutionArchive.Pack(expected));
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var item in expected) Assert.Equal(item.Value, actual[item.Key]);
    }

    /// <summary>UI 匯入沿用唯一組織白名單；非 goodshepherdback 必須在建立租約前拒絕且不觸發 connector。</summary>
    [Fact]
    public void UiImport_NonWhitelistedOrganization_Rejected()
    {
        using var output = new StringWriter();
        var reports = new List<Exception>();
        var result = DeploymentCli.Run(
            NonWhitelistedImportArguments,
            new RejectingConnector(), output, reports.Add);
        Assert.Equal(2, result);
        Assert.Empty(reports);
    }

    /// <summary>
    /// 合成服務回傳已／未發布相同的非空表單與檢視，真 Import 不得誤判 pending；
    /// 保留匯出檔、完整查詢／匯入／發布順序與方案身分斷言，暫存目錄由 finally 清理。
    /// </summary>
    [Fact]
    public void UiExportAndImport_UseOfflineSdkContract()
    {
        var root = Path.Combine(Path.GetTempPath(), "smv-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new UiService(Archive());
            UiSolutionDeployment.Export(service, root, "SpeechMessageUi", default);
            var exported = Path.Combine(root, "d365", "solutions", "SpeechMessageUi", "solution.xml");
            Assert.True(File.Exists(exported));

            var input = Path.Combine(root, "input.zip");
            File.WriteAllBytes(input, Archive());
            UiSolutionDeployment.Import(service, input, "SpeechMessageUi", default);
            Assert.Equal(ExpectedSdkRequests, service.Requests);
            Assert.NotNull(service.ImportedArchive);
            Assert.Equal("SpeechMessageUi", service.ImportedArchive!.Value.SolutionName);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 兩來源含相同計畫外正式 ID，但只在未發布來源改變 XML，真 Import 必須在匯入前拒絕；
    /// 例外須包含該 ID 且不能送出 ImportSolution，不能靠缺列或無效回應提前失敗。
    /// </summary>
    [Fact]
    public void UiImport_UnplannedPendingComponent_RefusedBeforeImport()
    {
        var root = Path.Combine(Path.GetTempPath(), "smv-ui-pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new UiService(
                Archive(),
                new[] { Guid.Parse("90000000-0000-0000-0000-000000000009") });
            var input = Path.Combine(root, "input.zip");
            File.WriteAllBytes(input, Archive());

            var error = Assert.Throws<InvalidDataException>(() =>
                UiSolutionDeployment.Import(service, input, "SpeechMessageUi", default));

            Assert.Contains("90000000-0000-0000-0000-000000000009", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ImportSolution", service.Requests);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 未允許的 RootComponent type 62 不得因發布範圍只收集表單／檢視而隨完整 ZIP 進入 ImportSolution。
    /// 故障注入使用合成 ZIP；決定性斷言是 Import 前拒絕，且 SDK 假服務從未收到 ImportSolution，暫存目錄由 finally 清除。
    /// </summary>
    [Fact]
    public void UiImport_UnapprovedRootComponentType_RefusedBeforeImport()
    {
        var root = Path.Combine(Path.GetTempPath(), "smv-ui-root-type-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var unsafeSolution = Solution.Replace(
                "</RootComponents>",
                "<RootComponent type=\"62\" id=\"90000000-0000-0000-0000-000000000061\" /></RootComponents>",
                StringComparison.Ordinal);
            var input = Path.Combine(root, "input.zip");
            File.WriteAllBytes(input, Archive(solution: unsafeSolution));
            var service = new UiService(Archive());

            Assert.Throws<InvalidDataException>(() =>
                UiSolutionDeployment.Import(service, input, "SpeechMessageUi", default));

            Assert.DoesNotContain("ImportSolution", service.Requests);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
    /// <summary>
    /// Entity RootComponent 的 behavior=0 會包含全部子元件；UI 匯入只允許 entity shell，完整實體必須在送出 ImportSolution 前拒絕。
    /// 合成 ZIP 將已列於 customizations 的 new_example 改成包含子元件；斷言 SDK 未收到 ImportSolution，並由 finally 清除本案目錄。
    /// </summary>
    [Fact]
    public void UiImport_EntityRootIncludesSubcomponents_RefusedBeforeImport()
    {
        var root = Path.Combine(Path.GetTempPath(), "smv-ui-entity-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var solution = Solution.Replace(
                "</RootComponents>",
                "<RootComponent type=\"1\" schemaName=\"new_example\" behavior=\"0\" /></RootComponents>",
                StringComparison.Ordinal);
            var input = Path.Combine(root, "input.zip");
            File.WriteAllBytes(input, Archive(solution: solution));
            var service = new UiService(Archive());

            Assert.Throws<InvalidDataException>(() =>
                UiSolutionDeployment.Import(service, input, "SpeechMessageUi", default));

            Assert.DoesNotContain("ImportSolution", service.Requests);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>明確標成 shell-only 的產品實體仍可隨表單／檢視匯入；正向控制須確實抵達 ImportSolution，而不是全面拒絕。</summary>
    [Fact]
    public void UiImport_EntityRootShellOnly_Allowed()
    {
        var root = Path.Combine(Path.GetTempPath(), "smv-ui-entity-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var solution = Solution.Replace(
                "</RootComponents>",
                "<RootComponent type=\"1\" schemaName=\"new_example\" behavior=\"2\" /></RootComponents>",
                StringComparison.Ordinal);
            var input = Path.Combine(root, "input.zip");
            File.WriteAllBytes(input, Archive(solution: solution));
            var service = new UiService(Archive());

            UiSolutionDeployment.Import(service, input, "SpeechMessageUi", default);

            Assert.Contains("ImportSolution", service.Requests);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }


    /// <summary>建立標準三個根檔及自有資源；可注入單一惡意 entry 或 XML，所有串流離開方法前確定關閉。</summary>
    internal static byte[] Archive(string? extra = null, byte[]? content = null, string? solution = null)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "solution.xml", Encoding.UTF8.GetBytes(solution ?? Solution));
            Add(zip, "customizations.xml", Encoding.UTF8.GetBytes(Customizations));
            Add(zip, "[Content_Types].xml", Encoding.UTF8.GetBytes("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />"));
            Add(zip, "WebResources/smv_icon", new byte[] { 1, 2, 3 });
            if (extra is not null) Add(zip, extra, content ?? new byte[] { 4 });
        }
        return stream.ToArray();
    }

    /// <summary>
    /// 從真實合成 ZIP 的成員長度計算待補原文，加入可壓縮且個別有界的資源。
    /// 大小由測試指定，不讀正式實作常數；每個 stream 在返回前關閉，不產生磁碟檔案。
    /// </summary>
    internal static byte[] ArchiveAtExpandedSize(int totalBytes, int entryBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(Archive());
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var remaining = totalBytes - zip.Entries.Sum(entry => entry.Length);
            var payload = new byte[entryBytes];
            for (var index = 0; remaining > 0; index++)
            {
                var count = (int)Math.Min(remaining, payload.Length);
                using var output = zip.CreateEntry(
                    "WebResources/budget-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)).Open();
                output.Write(payload, 0, count);
                remaining -= count;
            }
        }
        return stream.ToArray();
    }

    /// <summary>寫入本案例的單一 ZIP 成員，entry stream 不外借或跨案例保留。</summary>
    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var output = zip.CreateEntry(name).Open();
        output.Write(bytes);
    }

    /// <summary>白名單拒絕案例的 connector；若 parser 越過拒絕邊界，立即以固定例外暴露錯誤。</summary>
    private sealed class RejectingConnector : IOrganizationConnector
    {
        /// <summary>非白名單命令不應取得 organization lease。</summary>
        public OrganizationLease Connect(string organization) =>
            throw new InvalidOperationException("Connector must not be called.");
    }

    /// <summary>
    /// 承接本案例的匯出、雙來源 UI 讀取、匯入與定向發布，未列出的操作立即失敗。
    /// 每次查詢建立獨立 Entity，固定正式 ID 與範圍，不共享可變 SDK 列；
    /// archive bytes、請求紀錄與快照只活到本案例結束，沒有連線或背景資源須額外釋放。
    /// </summary>
    private sealed class UiService(byte[] exportArchive, IReadOnlyList<Guid>? pendingIds = null) : IOrganizationService
    {
        /// <summary>只含固定元件型別的唯讀測試資料，不承載任何案例或組織狀態。</summary>
        private static readonly string[] ComponentTypes = { "systemform", "savedquery" };
        /// <summary>封裝明列的固定發布範圍，供兩來源查詢共同核對；不外借或修改陣列。</summary>
        private static readonly string[] ExpectedEntityScope = { "account", "contact", "new_example" };

        /// <summary>本案例獨占的完整 SDK 呼叫紀錄，用來驗證雙來源讀取先於匯入與發布。</summary>
        internal List<string> Requests { get; } = new();
        /// <summary>只保存本案例送入匯入訊息的封裝，供方案身分與非空斷言使用。</summary>
        internal (string SolutionName, byte[] Archive)? ImportedArchive { get; private set; }

        /// <summary>
        /// 回傳合成封裝與獨立未發布快照，並記錄匯入／發布順序；不委派任何真實服務。
        /// 原始QueryBase保留到回應包裝，表單FetchXML還原後仍通過Snapshot原有欄位及範圍斷言，
        /// alias只包住本案既有formxml，不改兩來源XML差異、正式ID或請求次數。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            // 官方唯讀轉換只模擬型別對應；原讀取／分頁／故障注入仍由既有替身負責。
            if (UiFormFetchTestAdapter.Convert(request) is { } converted) return converted;
            Requests.Add(request.RequestName);
            if (request is RetrieveEntityRequest metadata)
            {
                Assert.Equal(EntityFilters.All, metadata.EntityFilters);
                Assert.Contains(metadata.LogicalName, ExpectedEntityScope);
                return new RetrieveEntityResponse
                {
                    Results = { ["EntityMetadata"] = Ui.UiImportMetadataBoundaryTests.Metadata(metadata.LogicalName) }
                };
            }
            if (request.RequestName == "ExportSolution")
                return new OrganizationResponse { Results = { ["ExportSolutionFile"] = exportArchive } };
            if (request.RequestName == "RetrieveUnpublishedMultiple")
            {
                var query = Assert.IsAssignableFrom<QueryBase>(request["Query"]);
                var page = Snapshot(UiFormFetchTestAdapter.Query(query), unpublished: true);
                return new RetrieveUnpublishedMultipleResponse
                {
                    Results = { ["EntityCollection"] = UiFormFetchTestAdapter.Wrap(query, page) }
                };
            }
            if (request.RequestName == "ImportSolution")
            {
                var bytes = Assert.IsType<byte[]>(request["CustomizationFile"]);
                ImportedArchive = ("SpeechMessageUi", bytes);
                return new OrganizationResponse();
            }
            if (request.RequestName == "PublishXml")
                return new OrganizationResponse();
            throw new NotSupportedException();
        }

        /// <summary>
        /// 以正式 ID 與固定欄位建立完整單頁；兩來源身份完全一致，只有指定計畫外表單的未發布 XML 不同。
        /// 非空且未修改的檢視不在封裝計畫內，仍必須允許匯入，防止把未發布來源的所有列誤認為 pending。
        /// </summary>
        private EntityCollection Snapshot(QueryExpression query, bool unpublished)
        {
            Assert.Contains(query.EntityName, ComponentTypes);
            var isForm = query.EntityName == "systemform";
            Assert.False(query.ColumnSet.AllColumns);
            Assert.Contains(isForm ? "formid" : "savedqueryid", query.ColumnSet.Columns);
            Assert.Contains(isForm ? "formxml" : "fetchxml", query.ColumnSet.Columns);
            var scope = Assert.Single(query.Criteria.Conditions);
            Assert.Equal(isForm ? "objecttypecode" : "returnedtypecode", scope.AttributeName);
            Assert.Equal(ConditionOperator.In, scope.Operator);
            Assert.Equal(ExpectedEntityScope, scope.Values.Cast<string>());
            var rows = new List<Entity>();
            if (isForm)
            {
                var plannedId = Guid.Parse("10000000-0000-0000-0000-000000000003");
                foreach (var id in new[] { plannedId }.Concat(pendingIds ?? Array.Empty<Guid>()))
                    rows.Add(new Entity("systemform", id)
                    {
                        ["formid"] = id,
                        ["objecttypecode"] = "new_example",
                        ["name"] = "Synthetic form",
                        ["description"] = "Offline UI snapshot",
                        ["type"] = new OptionSetValue(2),
                        ["formactivationstate"] = new OptionSetValue(1),
                        ["ismanaged"] = false,
                        ["formxml"] = unpublished && id != plannedId ? "<form><tabs /></form>" : "<form />"
                    });
            }
            else
            {
                var viewId = Guid.Parse("20000000-0000-0000-0000-000000000001");
                rows.Add(new Entity("savedquery", viewId)
                {
                    ["savedqueryid"] = viewId,
                    ["returnedtypecode"] = "account",
                    ["name"] = "Synthetic view",
                    ["description"] = "Offline UI snapshot",
                    ["querytype"] = 0,
                    ["isdefault"] = false,
                    ["statecode"] = new OptionSetValue(0),
                    ["ismanaged"] = false,
                    ["fetchxml"] = "<fetch><entity name=\"account\" /></fetch>",
                    ["layoutxml"] = "<grid />"
                });
            }
            return new EntityCollection(rows) { EntityName = query.EntityName };
        }

        /// <summary>本測試不使用單列寫入。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>本測試不使用更新。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>本測試不使用刪除。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>本測試不使用單列讀取。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>
        /// 承接真Import的已發布查詢，先記錄原順序，再還原表單FetchXML供Snapshot核對。
        /// 依原始查詢包裝獨立alias回應，其他QueryExpression維持原狀；不與未發布來源共用可變列或補造缺值。
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Requests.Add("RetrieveMultiple");
            var page = Snapshot(UiFormFetchTestAdapter.Query(query), unpublished: false);
            return UiFormFetchTestAdapter.Wrap(query, page);
        }
        /// <summary>本測試不使用關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本測試不使用解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }

    /// <summary>標準未受管理解決方案的合成根參照；三個固定 formid 分別對應下方三個實體。</summary>
    private const string Solution = """
        <ImportExportXml><SolutionManifest><UniqueName>SpeechMessageUi</UniqueName><Managed>0</Managed>
        <RootComponents>
        <RootComponent type="1" schemaName="account" behavior="0" />
        <RootComponent type="1" schemaName="contact" behavior="0" />
        <RootComponent type="60" id="{10000000-0000-0000-0000-000000000001}" />
        <RootComponent type="60" id="{10000000-0000-0000-0000-000000000002}" />
        <RootComponent type="60" id="{10000000-0000-0000-0000-000000000003}" />
        <RootComponent type="61" id="{10000000-0000-0000-0000-000000000061}" />
        </RootComponents></SolutionManifest></ImportExportXml>
        """;

    /// <summary>表單之外仍有檢視，避免測試以整個刪掉共用實體而錯誤通過。</summary>
    private const string Customizations = """
        <ImportExportXml><Entities>
        <Entity><Name>account</Name><FormXml><forms><systemform><formid>{10000000-0000-0000-0000-000000000001}</formid><form /></systemform></forms></FormXml><SavedQueries /></Entity>
        <Entity><Name>contact</Name><FormXml><forms><systemform><formid>{10000000-0000-0000-0000-000000000002}</formid><form /></systemform></forms></FormXml><SavedQueries /></Entity>
        <Entity><Name>new_example</Name><FormXml><forms><systemform><formid>{10000000-0000-0000-0000-000000000003}</formid><form /></systemform></forms></FormXml></Entity>
        </Entities></ImportExportXml>
        """;
}

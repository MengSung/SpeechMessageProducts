/*
檔案整體說明
目的與定位：驗證多產品 UI 方案白名單、account/contact D26 表單剔除、發布範圍與元件指紋／重試契約。
主要流程：以合成 ZIP/XML 建立允許及拒絕案例；以正式純邏輯檢查計畫元件與未發布元件；以合成
IOrganizationService 確認只送出按實體限縮的 PublishXmlRequest，不呼叫 PublishAllXml。
依賴與隔離：每個測試使用獨立記憶體封裝與合成 GUID；SDK 假服務只留本案請求，不連線 D365、
不讀 User Secrets、不保留租戶資料。失敗注入只模擬一次元件部分套用，ledger 以讀回候選重新規劃。
決定性斷言：非白名單方案、意外元件與 XML 指紋漂移都必須拒絕；D26 只移除 account/contact
表單且保留檢視；發布只涵蓋預期實體；部分失敗後重試清單只含實際缺少的穩定元件 ID。
驗證限制：所有證據均為離線合成，不代表任何組織已匯出、匯入或發布 UI。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>保護 UI 方案隔離、發布範圍與 apply 漂移契約；全部檔案與 SDK 請求都由案例自行擁有。</summary>
public sealed class UiSolutionLayerTests
{
    private const string PlatformSolution = "SpeechMessagePlatformUi";
    private static readonly string[] PublishedEntities = { "account", "new_linechannel" };

    /// <summary>未列入固定方案白名單的 ZIP，即使 XML 結構有效也不得成為可匯入候選。</summary>
    [Fact]
    public void Ui_SolutionNotInAllowList_Rejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            UiSolutionArchive.Prepare(Archive("UnapprovedUi"), "UnapprovedUi"));
    }

    /// <summary>平台方案允許匯入，但 D26 必須只刪除 account/contact 表單並保留檢視及自有表單。</summary>
    [Fact]
    public void Ui_AccountContactFormsStillStripped()
    {
        var prepared = UiSolutionArchive.Prepare(Archive(PlatformSolution), PlatformSolution);
        var customizations = XDocument.Parse(Encoding.UTF8.GetString(prepared["customizations.xml"]));
        var entities = customizations.Descendants("Entity").ToArray();

        Assert.Null(entities.Single(entity => (string?)entity.Element("Name") == "account").Element("FormXml"));
        Assert.Null(entities.Single(entity => (string?)entity.Element("Name") == "contact").Element("FormXml"));
        Assert.NotNull(entities.Single(entity => (string?)entity.Element("Name") == "account").Element("SavedQueries"));
        Assert.NotNull(entities.Single(entity => (string?)entity.Element("Name") == "contact").Element("SavedQueries"));
        Assert.NotNull(entities.Single(entity => (string?)entity.Element("Name") == "new_example").Element("FormXml"));
    }

    /// <summary>未發布清單包含計畫外正式元件 ID 時，發布前必須 fail closed 並指出衝突 ID。</summary>
    [Fact]
    public void Publish_UnplannedPendingChange_Refused()
    {
        var planned = new[] { Guid.Parse("10000000-0000-0000-0000-000000000001") };
        var pending = new[]
        {
            planned[0],
            Guid.Parse("20000000-0000-0000-0000-000000000002")
        };

        var error = Assert.Throws<InvalidDataException>(() => UiPublishScope.Check(pending, planned));

        Assert.Contains(pending[1].ToString("D"), error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>通過發布檢查後只送出列明實體的 PublishXmlRequest，不能以 PublishAllXml 擴大範圍。</summary>
    [Fact]
    public void Publish_UsesEntityScopedPublishXml_NotPublishAll()
    {
        var service = new PublishProbeService();

        UiPublishScope.Publish(service, PublishedEntities, CancellationToken.None);

        var request = Assert.IsType<PublishXmlRequest>(Assert.Single(service.Requests));
        Assert.Contains("<entity>account</entity>", request.ParameterXml, StringComparison.Ordinal);
        Assert.Contains("<entity>new_linechannel</entity>", request.ParameterXml, StringComparison.Ordinal);
        Assert.DoesNotContain("PublishAllXml", service.Requests.Select(item => item.RequestName));
    }

    /// <summary>plan 後 XML 改變會使同一穩定元件指紋不符，套用前拒絕而不覆蓋並行編輯。</summary>
    [Fact]
    public void Ui_FingerprintDrift_Refused()
    {
        var id = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var planned = UiComponentLedger.Capture(
            PlatformSolution, "GoodShepherd2", "1.0.0", "account", "form", id, "<form><tabs /></form>");
        var current = UiComponentLedger.Capture(
            PlatformSolution, "GoodShepherd2", "1.0.0", "account", "form", id, "<form><tabs><tab /></tabs></form>");

        Assert.Throws<InvalidDataException>(() => UiComponentLedger.VerifyFingerprint(planned, current));
    }

    /// <summary>第二個元件失敗後以讀回狀態重規劃；已完整套用的正式 ID 不可重送或重建。</summary>
    [Fact]
    public void Ui_PartialFailure_ReplanOnlyMissing()
    {
        var appliedId = Guid.Parse("40000000-0000-0000-0000-000000000004");
        var missingId = Guid.Parse("50000000-0000-0000-0000-000000000005");
        var planned = new[]
        {
            UiComponentLedger.Capture(PlatformSolution, "GoodShepherd2", "1.0.0",
                "account", "form", appliedId, "<form id='a' />"),
            UiComponentLedger.Capture(PlatformSolution, "GoodShepherd2", "1.0.0",
                "contact", "view", missingId, "<savedquery id='b' />")
        };
        var currentAfterInjectedFailure = new[] { planned[0] };

        var retry = UiComponentLedger.ReplanOnlyMissing(planned, currentAfterInjectedFailure);

        Assert.Equal(missingId, Assert.Single(retry).ComponentId);
    }

    /// <summary>建立有效但純合成的解決方案 ZIP；每個 stream 在方法返回前關閉，輸入不落地。</summary>
    private static byte[] Archive(string solutionName)
    {
        var solution = """
            <ImportExportXml><SolutionManifest><UniqueName>{solutionName}</UniqueName><Managed>0</Managed>
            <RootComponents><RootComponent type="1" schemaName="account" behavior="0" />
            <RootComponent type="1" schemaName="contact" behavior="0" />
            <RootComponent type="60" id="{60000000-0000-0000-0000-000000000006}" />
            </RootComponents></SolutionManifest></ImportExportXml>
            """.Replace("{solutionName}", solutionName, StringComparison.Ordinal);
        const string customizations = """
            <ImportExportXml><Entities>
            <Entity><Name>account</Name><FormXml><forms><systemform><formid>{60000000-0000-0000-0000-000000000006}</formid><form /></systemform></forms></FormXml><SavedQueries /></Entity>
            <Entity><Name>contact</Name><FormXml><forms><systemform><formid>{70000000-0000-0000-0000-000000000007}</formid><form /></systemform></forms></FormXml><SavedQueries /></Entity>
            <Entity><Name>new_example</Name><FormXml><forms><systemform><formid>{80000000-0000-0000-0000-000000000008}</formid><form /></systemform></forms></FormXml></Entity>
            </Entities></ImportExportXml>
            """;
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "solution.xml", Encoding.UTF8.GetBytes(solution));
            Add(zip, "customizations.xml", Encoding.UTF8.GetBytes(customizations));
            Add(zip, "[Content_Types].xml", Encoding.UTF8.GetBytes(
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />"));
        }
        return stream.ToArray();
    }

    /// <summary>寫入案例專屬 ZIP 成員並立刻釋放 entry stream，不保留共享緩衝區。</summary>
    private static void Add(ZipArchive zip, string name, byte[] bytes)
    {
        using var output = zip.CreateEntry(name).Open();
        output.Write(bytes);
    }

    /// <summary>只接受 PublishXmlRequest；任何全域發布或其他 SDK 訊息皆令案例失敗。</summary>
    private sealed class PublishProbeService : IOrganizationService
    {
        internal List<OrganizationRequest> Requests { get; } = new();

        /// <summary>記錄單案例的發布要求並回傳空合成回應，不連線或保留跨案例狀態。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Requests.Add(request);
            if (request is not PublishXmlRequest)
                throw new InvalidOperationException("Only entity-scoped PublishXml is permitted.");
            return new OrganizationResponse();
        }

        /// <summary>本發布案例不建立資料列。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>本發布案例不更新資料列。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>本發布案例不刪除資料列。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>本發布案例不讀單筆資料列。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>發布 request 契約不使用一般列查詢。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        /// <summary>本發布案例不建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
        /// <summary>本發布案例不移除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) =>
            throw new NotSupportedException();
    }
}

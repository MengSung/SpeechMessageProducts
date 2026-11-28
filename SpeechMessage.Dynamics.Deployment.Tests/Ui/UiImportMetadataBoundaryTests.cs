/*
檔案整體說明
目的與定位：FS 的 UI 匯入結構發布邊界回歸測試，直接執行 UiSolutionDeployment.Import，
以合成 SDK 證明實體層級 PublishXml 會連同既存未發布結構一起發布，不能只審表單與檢視。
主要功能與流程：建立合法且僅含 UI 的 ZIP；已／未發布 UI 完全相同，另注入實體標籤、
欄位長度、數字範圍與精度、選項標籤、關聯 cascade 及替代鍵差異。斷言第一個 Import 前拒絕；
成功控制則必須確實匯入與發布，避免靠替身不支援新查詢或後續泛用錯誤取得假陽性。
兩來源空 UI 查詢由共用轉接器辨識表單 FetchXML，依原始查詢包裝 alias 空頁；
其他 QueryExpression 與表名斷言不變，metadata 差異、取消及 wire 故障不經此轉接改寫。
使用邊界與擁有權：不連組織、不讀秘密或業務資料。每案 fixture 唯一擁有暫存 ZIP、
合成服務與 lease；Dispose 先排空服務回呼及快照，再刪除已關閉檔案。metadata 每次重新建立，
外層租約清理前 SDK 不可被產品提前 Dispose。離線發布效果不是實機結果。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Xunit;
using Xunit.Abstractions;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>以真 Import 路徑保護未審結構不被實體發布帶出的契約；所有資料均為案例專屬合成定義。</summary>
public sealed class UiImportMetadataBoundaryTests
{
    private static readonly bool[] ExpectedInitialReads = { false, true };
    private static readonly string[] UiTables = { "systemform", "savedquery" };
    private readonly ITestOutputHelper output;

    /// <summary>案例專屬輸出只記合成操作次數，不保留 SDK、metadata 本文或跨案例狀態。</summary>
    public UiImportMetadataBoundaryTests(ITestOutputHelper output) { this.output = output; }

    /// <summary>合法 UI 封裝及一致結構必須真正匯入與發布；若產品改成無條件拒絕，此控制立即失敗。</summary>
    [Fact]
    public void Import_IdenticalMetadata_SucceedsAndReleasesOnlyOuterLease()
    {
        var fixture = new ImportFixture();
        try
        {
            fixture.Import();
            Assert.Equal(1, fixture.Service.Imports);
            Assert.Equal(1, fixture.Service.Publishes);
            Assert.False(fixture.Service.PendingSchemaPublished);
            Assert.Equal(0, fixture.Service.Disposals);
            fixture.AssertArchiveClosed();
        }
        finally { fixture.Dispose(); }
        Assert.Equal(1, fixture.Service.Disposals);
        Assert.Empty(fixture.Service.MetadataReads);
    }

    /// <summary>
    /// UI 已／未發布來源相同、ZIP 沒有 schema，仍須於第一個 Import 前拒絕同表未審結構。
    /// 替身的 PublishXml 確實會帶出結構，故移除護欄會讓本案失敗而成功控制仍通過。
    /// </summary>
    [Theory]
    [InlineData("entity-label")]
    [InlineData("entity-setting")]
    [InlineData("length")]
    [InlineData("range")]
    [InlineData("precision")]
    [InlineData("option-label")]
    [InlineData("cascade")]
    [InlineData("key")]
    [InlineData("added-attribute")]
    [InlineData("removed-attribute")]
    public void Import_PendingSchema_RefusedBeforeFirstImport(string change)
    {
        using var fixture = new ImportFixture();
        fixture.Service.Change = change;

        var error = Record.Exception(() => fixture.Import());
        output.WriteLine("Observed ImportSolution={0}; PublishXml={1}; PendingSchemaPublished={2}; MetadataReads={3}",
            fixture.Service.Imports, fixture.Service.Publishes, fixture.Service.PendingSchemaPublished, fixture.Service.MetadataReads.Count);
        Assert.IsType<InvalidDataException>(error);

        Assert.Equal(0, fixture.Service.Imports);
        Assert.Equal(0, fixture.Service.Publishes);
        Assert.False(fixture.Service.PendingSchemaPublished);
        Assert.Equal(ExpectedInitialReads, fixture.Service.MetadataReads.Select(read => read.Unpublished));
        fixture.AssertArchiveClosed();
    }

    /// <summary>匯入後才出現結構差異，也必須在 PublishXml 前拒絕；不把檢查前快照當成之後仍有效。</summary>
    [Fact]
    public void Import_SchemaChangesDuringImport_RefusesPublish()
    {
        using var fixture = new ImportFixture();
        fixture.Service.AfterImport = () => fixture.Service.Change = "precision";

        Assert.Throws<InvalidDataException>(() => fixture.Import());

        Assert.Equal(1, fixture.Service.Imports);
        Assert.Equal(0, fixture.Service.Publishes);
        Assert.False(fixture.Service.PendingSchemaPublished);
    }

    /// <summary>匯入期間兩來源一起變動也須與原基準比較，不能以當下兩份相等誤當結構仍受審。</summary>
    [Fact]
    public void Import_BothMetadataSourcesDriftDuringImport_RefusesPublish()
    {
        using var fixture = new ImportFixture();
        fixture.Service.AfterImport = () =>
        {
            fixture.Service.Change = "precision";
            fixture.Service.ChangePublishedToo = true;
        };

        Assert.Throws<InvalidDataException>(() => fixture.Import());

        Assert.Equal(1, fixture.Service.Imports);
        Assert.Equal(0, fixture.Service.Publishes);
    }

    /// <summary>正式身分集合僅順序不同及 HasChanged 傳輸旗標不同不構成結構變更，成功控制仍須實際發布。</summary>
    [Fact]
    public void Import_MetadataOrderAndTransportFlagsDiffer_StillSucceeds()
    {
        using var fixture = new ImportFixture();
        fixture.Service.ReorderUnpublished = true;

        fixture.Import();

        Assert.Equal(1, fixture.Service.Imports);
        Assert.Equal(1, fixture.Service.Publishes);
    }

    /// <summary>真 SDK DataContract 往返會建立非 null 的空 ExtensionData，正常回應仍必須可匯入與發布。</summary>
    [Fact]
    public void Import_RealSdkWireRoundTripWithEmptyExtensions_Succeeds()
    {
        using var fixture = new ImportFixture();
        fixture.Service.WireRoundTrip = true;

        fixture.Import();

        Assert.Equal(1, fixture.Service.Imports);
        Assert.Equal(1, fixture.Service.Publishes);
    }

    /// <summary>
    /// SDK 的父選項欄位是非公開 DataMember，仍屬真實 wire 契約。
    /// 相同值必須成功，未發布值不同則在兩來源讀完後、第一個 Import 前拒絕，不能忽略或一律拒絕。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_PrivateSdkDataMember_WireContractIsCompared(bool pending)
    {
        using var fixture = new ImportFixture();
        fixture.Service.IncludePrivateEnumParent = true;
        fixture.Service.WireRoundTrip = true;
        if (pending) fixture.Service.Change = "private-enum-parent";

        var error = Record.Exception(() => fixture.Import());

        if (pending) Assert.IsType<InvalidDataException>(error);
        else Assert.Null(error);
        Assert.Equal(pending ? 0 : 1, fixture.Service.Imports);
        Assert.Equal(pending ? 0 : 1, fixture.Service.Publishes);
        Assert.Equal(pending ? 2 : 4, fixture.Service.MetadataReads.Count);
        fixture.AssertArchiveClosed();
    }

    /// <summary>未知伺服器欄位進入 ExtensionData 時不能靜默忽略，即使兩來源的未知欄位相同也須寫入前拒絕。</summary>
    [Fact]
    public void Import_UnknownWireMetadataMember_RefusesBeforeImport()
    {
        using var fixture = new ImportFixture();
        fixture.Service.WireRoundTrip = true;
        fixture.Service.UnknownWireMember = true;

        Assert.Throws<InvalidDataException>(() => fixture.Import());

        Assert.Equal(0, fixture.Service.Imports);
        Assert.Equal(0, fixture.Service.Publishes);
    }

    /// <summary>2048 個合法正式欄位仍須成功，防止以過小預算或把相同 ID 的兩份來源算成衝突取代完整檢查。</summary>
    [Fact]
    public void Import_Exactly2048Attributes_Succeeds()
    {
        using var fixture = new ImportFixture();
        fixture.Service.FullAttributeBudget = true;

        fixture.Import();

        Assert.Equal(1, fixture.Service.Imports);
        Assert.Equal(1, fixture.Service.Publishes);
    }

    /// <summary>真正 SDK 故障保留同一 Exception，外層告警可判斷原始型別；不得用泛用格式拒絕掩蓋故障。</summary>
    [Fact]
    public void Import_MetadataSdkFault_PreservesOriginalException()
    {
        using var fixture = new ImportFixture();
        var fault = new InvalidOperationException("Synthetic SDK failure.");
        fixture.Service.MetadataFault = fault;

        Assert.Same(fault, Record.Exception(() => fixture.Import()));

        Assert.Equal(0, fixture.Service.Imports);
        Assert.Equal(0, fixture.Service.Publishes);
    }

    /// <summary>SDK 返回時 caller 取消優先於損壞 metadata；禁止下一個查詢、Import 或提前釋放外層服務。</summary>
    [Fact]
    public void Import_CanceledDuringMetadataRead_PreservesCallerToken()
    {
        using var fixture = new ImportFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Service.AfterMetadataRead = cancellation.Cancel;
        fixture.Service.InvalidMetadata = true;

        var error = Assert.Throws<OperationCanceledException>(() => fixture.Import(cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Single(fixture.Service.MetadataReads);
        Assert.Equal(0, fixture.Service.Imports);
        Assert.Equal(0, fixture.Service.Disposals);
        fixture.AssertArchiveClosed();
    }

    /// <summary>完整結構回應錯型、錯表、空正式 ID、缺集合或超量時整份拒絕，不能以空資料當一致結構。</summary>
    [Theory]
    [InlineData("response")]
    [InlineData("scope")]
    [InlineData("empty-id")]
    [InlineData("missing-attributes")]
    [InlineData("missing-relationships")]
    [InlineData("missing-privileges")]
    [InlineData("duplicate-attribute")]
    [InlineData("duplicate-option")]
    [InlineData("duplicate-relationship")]
    [InlineData("attributes-budget")]
    [InlineData("options-budget")]
    [InlineData("text-budget")]
    public void Import_InvalidMetadata_RefusedBeforeWrite(string fault)
    {
        using var fixture = new ImportFixture();
        fixture.Service.Fault = fault;

        Assert.Throws<InvalidDataException>(() => fixture.Import());

        Assert.Equal(0, fixture.Service.Imports);
        Assert.Equal(0, fixture.Service.Publishes);
        fixture.AssertArchiveClosed();
    }

    /// <summary>一百次操作與故障交替不保留服務；每次 lease 關閉皆清除讀取紀錄與回呼，成功仍可繼續。</summary>
    [Fact]
    public void Import_RepeatedSuccessAndRejection_DrainsOwners()
    {
        for (var index = 0; index < 100; index++)
        {
            var fixture = new ImportFixture();
            try
            {
                if (index % 2 == 0) fixture.Service.Change = "length";
                if (index % 2 == 0) Assert.Throws<InvalidDataException>(() => fixture.Import());
                else fixture.Import();
                Assert.Equal(index % 2, fixture.Service.Imports);
                fixture.AssertArchiveClosed();
            }
            finally { fixture.Dispose(); }
            Assert.Equal(1, fixture.Service.Disposals);
            Assert.Empty(fixture.Service.MetadataReads);
            Assert.Null(fixture.Service.AfterImport);
            Assert.Null(fixture.Service.AfterMetadataRead);
        }
    }

    /// <summary>SDK 回應離開真 Import 後須可回收；NoInlining 消除測試堆疊暫留，弱參照不延長資源生命週期。</summary>
    [Fact]
    public void Import_CompletedOperations_DoNotRetainSdkMetadata()
    {
        var references = CompleteImports();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(80, references.Count);
        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    /// <summary>二十次獨立操作各讀四份 SDK 定義；服務仍存活時記下弱參照，owner 清理後只回傳弱參照。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> CompleteImports()
    {
        var references = new List<WeakReference>();
        for (var index = 0; index < 20; index++)
        {
            using var fixture = new ImportFixture();
            fixture.Import();
            references.AddRange(fixture.Service.MetadataReferences);
        }
        return references;
    }

    /// <summary>
    /// 建立完整且獨立的 SDK metadata 回應，供既有 UI fixture 補上必要新查詢。
    /// 正式 ID 由固定合成名稱決定；不以顯示標籤合併欄位，不取得組織或業務資料。
    /// </summary>
    internal static EntityMetadata Metadata(string name)
    {
        var entity = new EntityMetadata
        {
            MetadataId = Identity(name),
            LogicalName = name,
            SchemaName = name,
            DisplayName = new Label("合成實體", 1028),
            OwnershipType = OwnershipTypes.UserOwned,
            IsAuditEnabled = new BooleanManagedProperty(false)
        };
        Set(entity, "PrimaryIdAttribute", name + "id");
        Set(entity, "PrimaryNameAttribute", "new_name");
        Set(entity, "Attributes", new AttributeMetadata[]
        {
            new StringAttributeMetadata
            {
                MetadataId = Identity(name + ".name"), LogicalName = "new_name", MaxLength = 100,
                FormatName = StringFormatName.Text, RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
            },
            new IntegerAttributeMetadata { MetadataId = Identity(name + ".number"), LogicalName = "new_number", MinValue = 0, MaxValue = 100 },
            new DecimalAttributeMetadata { MetadataId = Identity(name + ".amount"), LogicalName = "new_amount", Precision = 2, MinValue = 0, MaxValue = 100 },
            new PicklistAttributeMetadata
            {
                MetadataId = Identity(name + ".choice"), LogicalName = "new_choice",
                OptionSet = new OptionSetMetadata { IsGlobal = false }
            }
        });
        ((PicklistAttributeMetadata)entity.Attributes[3]).OptionSet.Options.Add(new OptionMetadata(new Label("合成選項", 1028), 1));
        Set(entity, "Keys", new[] { new EntityKeyMetadata
        {
            MetadataId = Identity(name + ".key"), LogicalName = "new_key", KeyAttributes = new[] { entity.Attributes[0].LogicalName }
        } });
        Set(entity, "OneToManyRelationships", new[] { new OneToManyRelationshipMetadata
        {
            MetadataId = Identity(name + ".relationship"), SchemaName = "new_example_child",
            ReferencedEntity = name, ReferencedAttribute = name + "id",
            ReferencingEntity = "new_child", ReferencingAttribute = "new_parent",
            CascadeConfiguration = new CascadeConfiguration { Delete = CascadeType.Restrict }
        } });
        Set(entity, "ManyToOneRelationships", Array.Empty<OneToManyRelationshipMetadata>());
        Set(entity, "ManyToManyRelationships", Array.Empty<ManyToManyRelationshipMetadata>());
        Set(entity, "Privileges", Array.Empty<SecurityPrivilegeMetadata>());
        return entity;
    }

    /// <summary>合成 SDK 唯讀 setter 只用於建構伺服器回應，產品不使用此測試反射入口。</summary>
    private static void Set(object instance, string property, object? value) =>
        instance.GetType().GetProperty(property)!.SetValue(instance, value);

    /// <summary>僅在合成回應填入 SDK 已宣告的非公開序列化欄位，wire 往返仍由 SDK serializer 執行。</summary>
    private static void SetPrivateEnumParent(AttributeMetadata attribute, string value)
    {
        var property = typeof(EnumAttributeMetadata).GetProperty("ParentEnumAttributeLogicalName",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotNull(property.GetCustomAttribute<DataMemberAttribute>());
        property.SetValue(attribute, value);
    }

    /// <summary>SHA-256 只產生穩定合成 GUID，不處理密碼或充當授權、雜湊比較及資料列去重。</summary>
    private static Guid Identity(string name) => new(SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16));

    /// <summary>唯一擁有合法 ZIP 與服務租約；同步 Import 完成後才允許外層清理，沒有背景工作。</summary>
    private sealed class ImportFixture : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "smv-ui-metadata-" + Guid.NewGuid().ToString("N") + ".zip");
        private readonly OrganizationLease lease;
        /// <summary>本案獨占 SDK；產品只可借用，不能在 Import 返回前後擅自釋放。</summary>
        internal MetadataService Service { get; } = new();

        /// <summary>建立三個必要根檔的合法 UI-only 封裝；寫入 handle 在呼叫產品前關閉。</summary>
        internal ImportFixture()
        {
            lease = new OrganizationLease(Service, Service.Dispose);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var zip = new ZipArchive(output, ZipArchiveMode.Create);
            Add(zip, "solution.xml", "<ImportExportXml><SolutionManifest><UniqueName>SpeechMessageUi</UniqueName><Managed>0</Managed><RootComponents /></SolutionManifest></ImportExportXml>");
            Add(zip, "customizations.xml", "<ImportExportXml><Entities><Entity><Name>new_example</Name><FormXml /></Entity></Entities></ImportExportXml>");
            Add(zip, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
        }
        /// <summary>真產品入口保留 ZIP、UI 快照與 metadata 發布決策，不繞過任何既有護欄。</summary>
        internal void Import(CancellationToken ct = default) => UiSolutionDeployment.Import(lease.Service, path, "SpeechMessageUi", ct);
        /// <summary>獨占重開輸入證明成功與失敗皆同步釋放產品的檔案 handle。</summary>
        internal void AssertArchiveClosed()
        {
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(reopened.Length > 0);
        }
        /// <summary>唯一 owner 先釋放服務，再刪除本案檔案；斷言失敗也走 using／finally。</summary>
        public void Dispose() { lease.Dispose(); File.Delete(path); }
        /// <summary>單一成員 stream 局部擁有並同步關閉，不把 writer 傳給產品。</summary>
        private static void Add(ZipArchive zip, string name, string text)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
    }

    /// <summary>合成 entity-level 發布服務；UI 來源固定相同，metadata 差異獨立注入，禁止所有業務資料存取。</summary>
    private sealed class MetadataService : IOrganizationService, IDisposable
    {
        /// <summary>逐次記錄強型別 metadata 範圍及發布狀態，不保存 SDK request。</summary>
        internal List<(string Entity, bool Unpublished)> MetadataReads { get; } = new();
        /// <summary>只保留回應弱參照供回收斷言，Dispose 清除清單；不替產品延長任何 SDK 生命週期。</summary>
        internal List<WeakReference> MetadataReferences { get; } = new();
        /// <summary>本案待發布差異種類；null 表示成功控制。</summary>
        internal string? Change { get; set; }
        /// <summary>本案無效回應故障種類，不含外部輸入。</summary>
        internal string? Fault { get; set; }
        /// <summary>實際 ImportSolution 次數，用來證明拒絕早於第一個匯入。</summary>
        internal int Imports { get; private set; }
        /// <summary>實際 PublishXml 次數，防止先發布再報錯。</summary>
        internal int Publishes { get; private set; }
        /// <summary>實體發布會帶出未發布結構的合成伺服器效果。</summary>
        internal bool PendingSchemaPublished { get; private set; }
        /// <summary>只有外層 lease 可以呼叫一次 Dispose。</summary>
        internal int Disposals { get; private set; }
        /// <summary>匯入後注入結構漂移，Dispose 時清除閉包。</summary>
        internal Action? AfterImport { get; set; }
        /// <summary>同步 metadata 呼叫返回前注入取消，Dispose 時清除閉包。</summary>
        internal Action? AfterMetadataRead { get; set; }
        /// <summary>配合取消回傳損壞回應，驗證取消優先順序。</summary>
        internal bool InvalidMetadata { get; set; }
        /// <summary>控制匯入期間兩來源同時漂移，驗證匯入前基準仍生效。</summary>
        internal bool ChangePublishedToo { get; set; }
        /// <summary>僅對未發布來源改變集合順序及傳輸旗標，不能被當成真正 pending。</summary>
        internal bool ReorderUnpublished { get; set; }
        /// <summary>提供恰好2048個有正式身分的欄位，使用實際而非稀疏 null 回應。</summary>
        internal bool FullAttributeBudget { get; set; }
        /// <summary>以 SDK 本身的 DataContractSerializer 產生真實 wire 回應形狀，串流在 Execute 返回前關閉。</summary>
        internal bool WireRoundTrip { get; set; }
        /// <summary>讓已／未發布來源含相同的非公開父選項欄位；只有指定故障才改未發布值。</summary>
        internal bool IncludePrivateEnumParent { get; set; }
        /// <summary>在 wire 回應加入未知定義供 ExtensionData 拒絕測試，不比較原始 XML 或 JSON。</summary>
        internal bool UnknownWireMember { get; set; }
        /// <summary>由案例提供的原 SDK 故障，Dispose 清除參照；不包含外部資料。</summary>
        internal Exception? MetadataFault { get; set; }

        /// <summary>
        /// 只承接真metadata/UI訊息與匯入／實體發布；未知呼叫以NotSupportedException暴露測試缺漏。
        /// 未發布表單FetchXML先經共用轉接器辨識，再按原始查詢包裝空頁；不改metadata故障、
        /// caller取消回呼或租約所有權，也不讓轉接器補出本案不存在的UI資料。
        /// </summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            // 官方唯讀轉換只模擬型別對應；原讀取／分頁／故障注入仍由既有替身負責。
            if (UiFormFetchTestAdapter.Convert(request) is { } converted) return converted;
            Assert.Equal(0, Disposals);
            if (request is RetrieveEntityRequest retrieve)
            {
                Assert.Equal(EntityFilters.All, retrieve.EntityFilters);
                Assert.Equal("new_example", retrieve.LogicalName);
                Assert.Equal(Guid.Empty, retrieve.MetadataId);
                MetadataReads.Add((retrieve.LogicalName, retrieve.RetrieveAsIfPublished));
                if (MetadataFault is not null) throw MetadataFault;
                var entity = Metadata(retrieve.LogicalName);
                if (IncludePrivateEnumParent) SetPrivateEnumParent(entity.Attributes[3], "new_original");
                if (retrieve.RetrieveAsIfPublished || ChangePublishedToo) Mutate(entity);
                if (FullAttributeBudget)
                    Set(entity, "Attributes", Enumerable.Range(0, 2048).Select(index => new StringAttributeMetadata
                    {
                        MetadataId = Identity("bounded-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        LogicalName = "new_field" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), MaxLength = 100
                    }).ToArray());
                if (ReorderUnpublished && retrieve.RetrieveAsIfPublished)
                {
                    Set(entity, "Attributes", entity.Attributes.Reverse().ToArray());
                    Set(entity, "HasChanged", true);
                    Set(entity.Attributes[0], "HasChanged", true);
                }
                if (Fault == "scope") entity.LogicalName = "new_other";
                if (Fault == "empty-id") entity.MetadataId = Guid.Empty;
                if (Fault == "missing-attributes") Set(entity, "Attributes", null);
                if (Fault == "missing-relationships") Set(entity, "ManyToOneRelationships", null);
                if (Fault == "missing-privileges") Set(entity, "Privileges", null);
                if (Fault == "duplicate-attribute") Set(entity, "Attributes", new[] { entity.Attributes[0], entity.Attributes[0] });
                if (Fault == "duplicate-option")
                    ((PicklistAttributeMetadata)entity.Attributes[3]).OptionSet.Options.Add(new OptionMetadata(new Label("同值衝突", 1028), 1));
                if (Fault == "duplicate-relationship")
                    Set(entity, "OneToManyRelationships", new[] { entity.OneToManyRelationships[0], entity.OneToManyRelationships[0] });
                if (Fault == "attributes-budget") Set(entity, "Attributes", new AttributeMetadata[2049]);
                if (Fault == "options-budget")
                    for (var value = 2; value <= 4097; value++)
                        ((PicklistAttributeMetadata)entity.Attributes[3]).OptionSet.Options.Add(new OptionMetadata { Value = value });
                if (Fault == "text-budget") entity.DisplayName = new Label(new string('測', 1024 * 1024), 1028);
                if (WireRoundTrip)
                {
                    var serializer = new DataContractSerializer(typeof(EntityMetadata));
                    using var stream = new MemoryStream();
                    serializer.WriteObject(stream, entity);
                    stream.Position = 0;
                    var document = XDocument.Load(stream);
                    if (UnknownWireMember)
                        document.Root!.Add(new XElement(document.Root.Name.Namespace + "UnknownPublicationSetting", "pending"));
                    using var reader = document.CreateReader();
                    entity = Assert.IsType<EntityMetadata>(serializer.ReadObject(reader));
                    Assert.NotNull(entity.ExtensionData);
                }
                MetadataReferences.Add(new WeakReference(entity));
                AfterMetadataRead?.Invoke();
                return InvalidMetadata || Fault == "response" ? new OrganizationResponse() :
                    new RetrieveEntityResponse { Results = { ["EntityMetadata"] = entity } };
            }
            if (request is RetrieveUnpublishedMultipleRequest unpublished)
            {
                var query = unpublished.Query;
                _ = UiFormFetchTestAdapter.Query(query);
                return new RetrieveUnpublishedMultipleResponse
                { Results = { ["EntityCollection"] = UiFormFetchTestAdapter.Wrap(query, new EntityCollection()) } };
            }
            if (request.RequestName == "ImportSolution") { Imports++; AfterImport?.Invoke(); return new OrganizationResponse(); }
            if (request is PublishXmlRequest publish)
            {
                Assert.Contains("<entity>new_example</entity>", publish.ParameterXml, StringComparison.Ordinal);
                Publishes++;
                PendingSchemaPublished = Change is not null;
                return new PublishXmlResponse();
            }
            throw new NotSupportedException("Unexpected synthetic metadata boundary request.");
        }

        /// <summary>修改未發布來源的單一真 SDK 欄位；此差異不出現在 UI 元件或 ZIP，不能被 UI 身分檢查替代。</summary>
        private void Mutate(EntityMetadata entity)
        {
            switch (Change)
            {
                case null: break;
                case "entity-label": entity.DisplayName = new Label("未審名稱", 1028); break;
                case "entity-setting": entity.IsAuditEnabled = new BooleanManagedProperty(true); break;
                case "length": ((StringAttributeMetadata)entity.Attributes[0]).MaxLength = 200; break;
                case "range": ((IntegerAttributeMetadata)entity.Attributes[1]).MaxValue = 200; break;
                case "precision": ((DecimalAttributeMetadata)entity.Attributes[2]).Precision = 3; break;
                case "option-label": ((PicklistAttributeMetadata)entity.Attributes[3]).OptionSet.Options[0].Label = new Label("未審選項", 1028); break;
                case "cascade": entity.OneToManyRelationships[0].CascadeConfiguration.Delete = CascadeType.Cascade; break;
                case "key": entity.Keys[0].KeyAttributes = new[] { "new_number" }; break;
                case "private-enum-parent": SetPrivateEnumParent(entity.Attributes[3], "new_pending"); break;
                case "added-attribute":
                    Set(entity, "Attributes", entity.Attributes.Concat(new[] { new StringAttributeMetadata
                    { MetadataId = Identity("extra"), LogicalName = "new_extra", MaxLength = 100 } }).ToArray()); break;
                case "removed-attribute": Set(entity, "Attributes", entity.Attributes.Skip(1).ToArray()); break;
                default: throw new ArgumentException("Unknown synthetic metadata change.");
            }
        }

        /// <summary>
        /// 兩份UI來源固定相同且完整；表單FetchXML還原後仍須通過既有表名白名單，
        /// 空頁依原始查詢包裝alias，其他QueryExpression不變。不讀業務實體或保留本次查詢／回應。
        /// </summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Assert.Contains(UiFormFetchTestAdapter.Query(query).EntityName, UiTables);
            return UiFormFetchTestAdapter.Wrap(query, new EntityCollection());
        }
        /// <summary>外層 owner 釋放時清除回呼及讀取紀錄，不留跨案例參照。</summary>
        public void Dispose()
        {
            Disposals++; MetadataReads.Clear(); MetadataReferences.Clear();
            AfterImport = null; AfterMetadataRead = null; MetadataFault = null;
        }
        /// <summary>此路徑不建立業務資料。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>此路徑不更新業務資料。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>此路徑不刪除業務資料。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>此路徑不讀取業務資料。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>此路徑不建立業務關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>此路徑不移除業務關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

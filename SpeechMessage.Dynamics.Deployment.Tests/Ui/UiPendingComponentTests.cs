/*
檔案整體說明
目的與定位：FS UI 解決方案匯入的離線發布護欄測試，透過真 UiSolutionDeployment.Import 驗證已／未發布定義比較及實體隔離，避免把 RetrieveUnpublishedMultiple 全部資料誤當待發布。
主要功能與流程：每案產生合成 ZIP 與兩份 SDK 快照，注入相同／不同 XML、其他實體修改、集合及身分錯誤、續頁不完整、cookie 循環、取消及跨來源累計 XML 超額；以 Import／Publish 是否發生及其他實體是否維持未發布作決定性斷言。
使用邊界與擁有權：SDK 只模擬本案記憶體資料，不連線、不讀秘密、不建立背景工作。新增 RetrieveEntity 固定回傳同表一致結構，讓完整 metadata 護欄不掩蓋既有 UI 故障；原 UI 身分、續頁與容量斷言保持。Fixture 擁有唯一暫存檔與 OrganizationLease；Dispose 先釋放服務並清除回呼與資料，再刪檔。成功與失敗均獨占重開 ZIP 驗證輸入 handle 已關閉，不把合成發布視為實機結果。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>保護真匯入入口的差異判定、分頁完整性、取消順序與 bounded 快照生命週期。</summary>
public sealed class UiPendingComponentTests
{
    /// <summary>相同的已／未發布元件即使未列在方案內也不是 pending，真匯入及定向發布必須成功。</summary>
    [Theory]
    [InlineData("systemform")]
    [InlineData("savedquery")]
    public void Import_IdenticalPublishedAndUnpublished_UnplannedComponentIsNotPending(string type)
    {
        using var fixture = new ImportFixture();
        fixture.Service.Seed(Component(type, ComponentId));

        fixture.Import();

        Assert.Equal(1, fixture.Service.Imports);
        Assert.Equal("new_example", Assert.Single(fixture.Service.PublishedEntities));
        fixture.AssertArchiveClosed();
    }

    /// <summary>範圍內 form／fetch／layout XML 有差異時須視為 pending；不在核准正式 ID 中就在匯入前拒絕。</summary>
    [Theory]
    [InlineData("systemform", "formxml")]
    [InlineData("savedquery", "fetchxml")]
    [InlineData("savedquery", "layoutxml")]
    public void Import_InScopeXmlChanged_UnplannedIdentityRefused(string type, string column)
    {
        using var fixture = new ImportFixture();
        fixture.Service.Seed(Component(type, ComponentId));
        fixture.Service.Unpublished.Single()[column] = "<changed />";

        fixture.AssertRejected();
    }

    /// <summary>相同正式 ID 已在核准根元件時可以發布自己的 XML 差異，避免以一律拒絕取代正確比較。</summary>
    [Theory]
    [InlineData("systemform", "formxml")]
    [InlineData("savedquery", "fetchxml")]
    [InlineData("savedquery", "layoutxml")]
    public void Import_InScopeXmlChanged_PlannedIdentityCanPublish(string type, string column)
    {
        using var fixture = new ImportFixture(new[] { ComponentId });
        fixture.Service.Seed(Component(type, ComponentId));
        fixture.Service.Unpublished.Single()[column] = "<changed />";

        fixture.Import();

        Assert.Equal("<changed />", fixture.Service.Published.Single().GetAttributeValue<string>(column));
        fixture.AssertArchiveClosed();
    }

    /// <summary>另一實體的變更不參與此次 pending 比較或發布；核對查詢確有實體條件且它的已發布 XML 不變。</summary>
    [Fact]
    public void Import_OtherEntityModified_RemainsUnpublishedAndOutsideQueries()
    {
        using var fixture = new ImportFixture();
        fixture.Service.Seed(Component("systemform", ComponentId, "new_other"));
        fixture.Service.Unpublished.Single()["formxml"] = "<private-change />";

        fixture.Import();

        Assert.Equal("<form />", fixture.Service.Published.Single().GetAttributeValue<string>("formxml"));
        Assert.Equal("<private-change />", fixture.Service.Unpublished.Single().GetAttributeValue<string>("formxml"));
        Assert.All(fixture.Service.ReadScopes, names => Assert.Equal("new_example", Assert.Single(names)));
        Assert.Equal("new_example", Assert.Single(fixture.Service.PublishedEntities));
    }

    /// <summary>兩來源完整讀取後再比較；匯入後才出現的計畫外 XML 差異也須拒絕 Publish。</summary>
    [Fact]
    public void Import_UnplannedXmlChangeAfterImport_RefusesPublish()
    {
        using var fixture = new ImportFixture();
        fixture.Service.Seed(Component("systemform", ComponentId));
        fixture.Service.AfterImport = () => fixture.Service.Unpublished.Single()["formxml"] = "<late-change />";

        fixture.AssertRejected(beforeImport: false);

        Assert.Equal(1, fixture.Service.Imports);
    }

    /// <summary>錯誤集合、越界實體、空／重複正式 ID 與不完整來源不得回傳部分候選；服務租約由 fixture 確定性清除。</summary>
    [Theory]
    [InlineData("collection")]
    [InlineData("logical-name")]
    [InlineData("scope")]
    [InlineData("empty-id")]
    [InlineData("duplicate-id")]
    [InlineData("missing-published")]
    [InlineData("missing-unpublished")]
    [InlineData("page-size")]
    public void Import_InvalidSnapshot_RefusedAndOwnerReleased(string fault)
    {
        var fixture = new ImportFixture(new[] { ComponentId });
        try
        {
            fixture.Service.Seed(Component("systemform", ComponentId));
            switch (fault)
            {
                case "collection":
                    fixture.Service.Page = (_, _) => new EntityCollection { EntityName = "account" };
                    break;
                case "logical-name":
                    fixture.Service.Page = (_, _) => new EntityCollection(new[] { new Entity("account", ComponentId) });
                    break;
                case "scope":
                    fixture.Service.Page = (_, _) => new EntityCollection(new[] { Component("systemform", ComponentId, "new_other") });
                    break;
                case "empty-id": fixture.Service.Unpublished[0].Id = Guid.Empty; break;
                case "duplicate-id": fixture.Service.Unpublished.Add(Component("systemform", ComponentId)); break;
                case "missing-published": fixture.Service.Published.Clear(); break;
                case "missing-unpublished": fixture.Service.Unpublished.Clear(); break;
                case "page-size":
                    fixture.Service.Page = (_, _) => new EntityCollection(
                        Enumerable.Range(1, 257).Select(index => Component("systemform", Id(index))).ToArray());
                    break;
                default: throw new ArgumentException("Unknown snapshot fault.", nameof(fault));
            }
            fixture.AssertRejected();
        }
        finally { fixture.Dispose(); }
        Assert.Equal(1, fixture.Service.Disposals);
        Assert.Empty(fixture.Service.Published);
        Assert.Empty(fixture.Service.Unpublished);
        Assert.Empty(fixture.Service.ReadScopes);
        Assert.Empty(fixture.Service.Columns);
        Assert.Empty(fixture.Service.Ordered);
    }

    /// <summary>空續頁、遺失／循環／過長 cookie 及稀疏續頁耗盡頁數必須有界拒絕，不能等替身的一百六十次硬停止。</summary>
    [Theory]
    [InlineData("empty-page", 1)]
    [InlineData("repeat-cookie", 2)]
    [InlineData("cycle-cookie", 3)]
    [InlineData("missing-cookie", 1)]
    [InlineData("large-cookie", 1)]
    [InlineData("page-limit", 16)]
    public void Import_IncompletePaging_RefusedWithinBound(string fault, int maximumReads)
    {
        using var fixture = new ImportFixture();
        fixture.Service.Page = (query, _) =>
        {
            var page = query.PageInfo.PageNumber;
            var rows = fault == "empty-page" ? Array.Empty<Entity>() : new[] { Component(query.EntityName, Id(page)) };
            return new EntityCollection(rows)
            {
                EntityName = query.EntityName,
                MoreRecords = true,
                PagingCookie = fault switch
                {
                    "repeat-cookie" => "same-cookie",
                    "cycle-cookie" => "cycle-" + (page % 2).ToString(CultureInfo.InvariantCulture),
                    "missing-cookie" => "",
                    "large-cookie" => new string('x', 16 * 1024 + 1),
                    _ => "cookie-" + page.ToString(CultureInfo.InvariantCulture)
                }
            };
        };

        fixture.AssertRejected();

        Assert.InRange(fixture.Service.Reads, 1, maximumReads);
    }

    /// <summary>SDK 回傳同時取消且集合損壞時，必須先觀察 caller token，再做快照驗證或下一個 SDK 操作。</summary>
    [Fact]
    public void Import_CanceledDuringSdk_ThrowsCallerCancellationBeforeResponseValidation()
    {
        using var fixture = new ImportFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Service.InvalidResponse = true;
        fixture.Service.AfterRead = cancellation.Cancel;

        var error = Assert.Throws<OperationCanceledException>(() => fixture.Import(cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, fixture.Service.Reads);
        Assert.Equal(0, fixture.Service.Imports);
        Assert.Empty(fixture.Service.PublishedEntities);
        fixture.AssertArchiveClosed();
    }

    /// <summary>每欄均在一 MiB 內，兩來源合計十八 MiB 仍須拒絕；核准所有 ID 以排除一般計畫外檢查造成的假陽性。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_CombinedSnapshotXmlBudgetExceeded_RefusedDespitePlannedIds(bool mixedTypes)
    {
        using var fixture = new ImportFixture(Enumerable.Range(1, 9).Select(Id).ToArray());
        var xml = "<form>" + new string('x', 1024 * 1024 - 13) + "</form>";
        foreach (var index in Enumerable.Range(1, mixedTypes ? 7 : 9))
        {
            var view = mixedTypes && index > 5;
            var row = Component(view ? "savedquery" : "systemform", Id(index));
            row[view ? "fetchxml" : "formxml"] = xml;
            if (view) row["layoutxml"] = xml;
            fixture.Service.Seed(row);
        }

        fixture.AssertRejected();
    }

    /// <summary>恰好十六 MiB 的兩來源 XML 可正常處理；預算按 UTF-8 位元組計算，不能以任意縮小上限取得綠燈。</summary>
    [Fact]
    public void Import_CombinedSnapshotXmlExactlyAtBudget_Succeeds()
    {
        using var fixture = new ImportFixture();
        var xml = "<form>" + new string('x', 1024 * 1024 - 13) + "</form>";
        foreach (var id in Enumerable.Range(1, 8).Select(Id))
        {
            var row = Component("systemform", id);
            row["formxml"] = xml;
            fixture.Service.Seed(row);
        }

        fixture.Import();

        Assert.Equal(1, fixture.Service.Imports);
        fixture.AssertArchiveClosed();
    }

    /// <summary>單欄字數小於一 MiB 但 UTF-8 超額仍拒絕；所有 ID 已核准，不能由計畫外衝突掩蓋容量漏驗。</summary>
    [Fact]
    public void Import_SingleXmlUtf8BudgetExceeded_Refused()
    {
        using var fixture = new ImportFixture(new[] { ComponentId });
        var row = Component("systemform", ComponentId);
        row["formxml"] = "<form>" + new string('測', 400_000) + "</form>";
        fixture.Service.Seed(row);

        fixture.AssertRejected();
    }

    /// <summary>兩來源各二百五十七列需要真的續頁；全為相同資料時不誤判 pending，且查詢必須使用固定欄位及主鍵排序。</summary>
    [Fact]
    public void Import_CompleteMultiplePages_UsesScopedFixedProjection()
    {
        using var fixture = new ImportFixture();
        foreach (var id in Enumerable.Range(1, 257).Select(Id))
            fixture.Service.Seed(Component("systemform", id));

        fixture.Import();

        Assert.Equal(12, fixture.Service.Reads);
        Assert.All(fixture.Service.Columns, columns => Assert.Contains("name", columns));
        Assert.All(fixture.Service.Ordered, Assert.True);
        Assert.Equal(1, fixture.Service.Imports);
    }

    /// <summary>原本 4096 個正式 ID 的上限仍可接受；兩來源相同 ID 各有一份快照，不應錯算成雙倍不同元件而縮減容量。</summary>
    [Fact]
    public void Import_Exactly4096IdentitiesInBothSources_Succeeds()
    {
        using var fixture = new ImportFixture();
        foreach (var id in Enumerable.Range(1, 4096).Select(Id))
            fixture.Service.Seed(Component("systemform", id));

        fixture.Import();

        Assert.Equal(1, fixture.Service.Imports);
        fixture.AssertArchiveClosed();
    }

    /// <summary>4097 個正式 ID 必須在匯入前有界拒絕，不把新增已發布來源當成放寬原本元件上限的理由。</summary>
    [Fact]
    public void Import_Over4096Identities_Refused()
    {
        using var fixture = new ImportFixture();
        foreach (var id in Enumerable.Range(1, 4097).Select(Id))
            fixture.Service.Seed(Component("systemform", id));

        fixture.AssertRejected();
    }

    /// <summary>只作本案合成元件身份，不代表實機資料庫記錄。</summary>
    private static readonly Guid ComponentId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    /// <summary>產生不重複的合成正式 ID，不借用顯示名稱或清單列位置作產品身分。</summary>
    private static Guid Id(int value) => Guid.Parse("40000000-0000-0000-0000-" + value.ToString("D12", CultureInfo.InvariantCulture));

    /// <summary>建立完整合成表單／檢視投影；XML、名稱、狀態及管理旗標僅屬本案。</summary>
    private static Entity Component(string type, Guid id, string entity = "new_example")
    {
        var row = new Entity(type, id) { ["name"] = "Synthetic UI", ["description"] = "", ["ismanaged"] = false };
        if (type == "systemform")
        {
            row["objecttypecode"] = entity; row["formxml"] = "<form />";
            row["type"] = new OptionSetValue(2); row["formactivationstate"] = 1;
        }
        else
        {
            row["returnedtypecode"] = entity; row["fetchxml"] = "<fetch />"; row["layoutxml"] = "<grid />";
            row["querytype"] = 0; row["statecode"] = new OptionSetValue(0); row["isdefault"] = false;
        }
        return row;
    }

    /// <summary>每案唯一擁有暫存 ZIP 與服務租約，先清除 SDK 引用再刪除已關閉的檔案；無其他背景資源。</summary>
    private sealed class ImportFixture : IDisposable
    {
        /// <summary>由本 fixture 的租約唯一擁有的合成 SDK，不跨案例重用。</summary>
        internal PendingService Service { get; } = new();
        private readonly string path = Path.Combine(Path.GetTempPath(), "smv-ui-snapshot-" + Guid.NewGuid().ToString("N") + ".zip");
        private readonly OrganizationLease lease;

        /// <summary>以獨立 ZIP writer 建立範圍只有 new_example 的方案，核准 ID 與 SDK 回應分開控制。</summary>
        internal ImportFixture(IReadOnlyList<Guid>? planned = null)
        {
            lease = new OrganizationLease(Service, Service.Dispose);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var zip = new ZipArchive(output, ZipArchiveMode.Create);
            Add(zip, "solution.xml", new XElement("ImportExportXml",
                new XElement("SolutionManifest", new XElement("UniqueName", "SpeechMessageUi"), new XElement("Managed", "0"),
                    new XElement("RootComponents", (planned ?? Array.Empty<Guid>()).Select(id =>
                        new XElement("RootComponent", new XAttribute("type", "60"), new XAttribute("id", id.ToString("B"))))))).ToString());
            Add(zip, "customizations.xml", "<ImportExportXml><Entities><Entity><Name>new_example</Name><FormXml /></Entity></Entities></ImportExportXml>");
            Add(zip, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
        }

        /// <summary>呼叫真產品入口，不模擬 ZIP 驗證、pending 比較或發布護欄。</summary>
        internal void Import(CancellationToken ct = default) => UiSolutionDeployment.Import(lease.Service, path, "SpeechMessageUi", ct);
        /// <summary>確認固定拒絕型別、無發布及檔案排空；可指定故障應發生在 SDK 匯入之前。</summary>
        internal void AssertRejected(bool beforeImport = true)
        {
            Assert.Throws<InvalidDataException>(() => Import());
            if (beforeImport) Assert.Equal(0, Service.Imports);
            Assert.Empty(Service.PublishedEntities);
            AssertArchiveClosed();
        }
        /// <summary>獨占重開證明產品沒有延長輸入 handle 的生命週期。</summary>
        internal void AssertArchiveClosed()
        {
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(reopened.Length > 0);
        }
        /// <summary>服務先由唯一 lease owner 清除，再刪除本案檔案，斷言失敗也走此路徑。</summary>
        public void Dispose() { lease.Dispose(); File.Delete(path); }
        /// <summary>writer 只擁有單一 entry stream，離開方法即同步關閉。</summary>
        private static void Add(ZipArchive zip, string name, string text)
        {
            using var stream = zip.CreateEntry(name).Open();
            stream.Write(Encoding.UTF8.GetBytes(text));
        }
    }

    /// <summary>合成 SDK 的兩份獨立快照；固定一百六十次查詢硬停止只防錯誤實作無窮循環，不作產品容量預期。</summary>
    private sealed class PendingService : IOrganizationService, IDisposable
    {
        /// <summary>本案已發布資料，與未發布候選分開複製。</summary>
        internal List<Entity> Published { get; } = new();
        /// <summary>本案未發布定義，只有 PublishXml 指定實體才可更新另一來源。</summary>
        internal List<Entity> Unpublished { get; } = new();
        /// <summary>只保存固定查詢實體名稱以觀察隔離邊界，不存 XML 或服務參照。</summary>
        internal List<string[]> ReadScopes { get; } = new();
        /// <summary>本案觀察到的欄位投影，不跨案例共享。</summary>
        internal List<string[]> Columns { get; } = new();
        /// <summary>本案查詢是否依正式主鍵穩定排序。</summary>
        internal List<bool> Ordered { get; } = new();
        /// <summary>實際合成發布的實體集合，空值表示未發布。</summary>
        internal string[] PublishedEntities { get; private set; } = Array.Empty<string>();
        /// <summary>真 ImportSolution 要求次數，用以區分匯入前與匯入後拒絕。</summary>
        internal int Imports { get; private set; }
        /// <summary>兩來源查詢總次數，硬停止防止錯誤產品造成無限測試。</summary>
        internal int Reads { get; private set; }
        /// <summary>唯一 lease owner 的釋放次數，不代表持有任何實機連線。</summary>
        internal int Disposals { get; private set; }
        /// <summary>注入錯誤未發布回應型別，測取消優先於回應驗證。</summary>
        internal bool InvalidResponse { get; set; }
        /// <summary>本案續頁／集合故障委派，Dispose 時解除參照。</summary>
        internal Func<QueryExpression, bool, EntityCollection>? Page { get; set; }
        /// <summary>SDK 返回前注入 caller 取消，Dispose 時解除參照。</summary>
        internal Action? AfterRead { get; set; }
        /// <summary>匯入後注入未核准變更，Dispose 時解除參照。</summary>
        internal Action? AfterImport { get; set; }

        /// <summary>已／未發布分別持有 detached Entity，修改一側不會偷偷變動另一側。</summary>
        internal void Seed(Entity row) { Published.Add(Copy(row)); Unpublished.Add(Copy(row)); }
        /// <summary>只支援 reader、匯入及定向發布；發布確實更新指定實體的合成已發布資料。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveEntityRequest metadata)
            {
                Assert.Equal(EntityFilters.All, metadata.EntityFilters);
                Assert.Equal("new_example", metadata.LogicalName);
                return new RetrieveEntityResponse
                {
                    Results = { ["EntityMetadata"] = UiImportMetadataBoundaryTests.Metadata(metadata.LogicalName) }
                };
            }
            if (request is RetrieveUnpublishedMultipleRequest unpublished)
            {
                var page = Read((QueryExpression)unpublished.Query, true);
                return InvalidResponse ? new OrganizationResponse() : new RetrieveUnpublishedMultipleResponse
                { Results = { ["EntityCollection"] = page } };
            }
            if (request.RequestName == "ImportSolution") { Imports++; AfterImport?.Invoke(); return new OrganizationResponse(); }
            if (request is PublishXmlRequest publish)
            {
                PublishedEntities = XDocument.Parse(publish.ParameterXml).Descendants("entity").Select(node => node.Value).ToArray();
                Published.RemoveAll(row => PublishedEntities.Contains(EntityName(row), StringComparer.Ordinal));
                Published.AddRange(Unpublished.Where(row => PublishedEntities.Contains(EntityName(row), StringComparer.Ordinal)).Select(Copy));
                return new PublishXmlResponse();
            }
            throw new NotSupportedException("Unexpected synthetic UI request.");
        }
        /// <summary>已發布讀取走 SDK 的真實 RetrieveMultiple 入口；可回 null 配合取消，證明取消先於損壞回應驗證。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var page = Read(Assert.IsType<QueryExpression>(query), false);
            return InvalidResponse ? null! : page;
        }
        /// <summary>模擬 SDK 伺服器依查詢條件篩選與分頁；故障委派可刻意回傳不完整或越界回應。</summary>
        private EntityCollection Read(QueryExpression query, bool unpublished)
        {
            if (++Reads > 160) throw new InvalidOperationException("Synthetic paging hard stop.");
            var names = query.Criteria.Conditions.SelectMany(condition => condition.Values).OfType<string>().ToArray();
            ReadScopes.Add(names);
            Columns.Add(query.ColumnSet.Columns.ToArray());
            Ordered.Add(query.Orders.Count == 1 && query.Orders[0].AttributeName ==
                (query.EntityName == "systemform" ? "formid" : "savedqueryid") && query.Orders[0].OrderType == OrderType.Ascending);
            var rows = (unpublished ? Unpublished : Published).Where(row => row.LogicalName == query.EntityName &&
                (names.Length == 0 || names.Contains(EntityName(row), StringComparer.Ordinal))).OrderBy(row => row.Id).ToArray();
            var skip = (query.PageInfo.PageNumber - 1) * query.PageInfo.Count;
            var result = Page?.Invoke(query, unpublished) ?? new EntityCollection(rows.Skip(skip).Take(query.PageInfo.Count).Select(Copy).ToArray())
            {
                EntityName = query.EntityName, MoreRecords = skip + query.PageInfo.Count < rows.Length,
                PagingCookie = "cookie-" + query.PageInfo.PageNumber.ToString(CultureInfo.InvariantCulture)
            };
            AfterRead?.Invoke();
            return result;
        }
        /// <summary>完整複製可變 SDK 選項，不共用狀態物件。</summary>
        private static Entity Copy(Entity row)
        {
            var copy = new Entity(row.LogicalName, row.Id);
            foreach (var pair in row.Attributes)
                copy[pair.Key] = pair.Value is OptionSetValue option ? new OptionSetValue(option.Value) : pair.Value;
            return copy;
        }
        /// <summary>合成伺服器以正式 entity logical name 篩選 UI 元件。</summary>
        private static string EntityName(Entity row) => row.GetAttributeValue<string>(
            row.LogicalName == "systemform" ? "objecttypecode" : "returnedtypecode") ?? "";
        /// <summary>唯一 lease 釋放時清除兩份快照及故障回呼，不保留案例間狀態。</summary>
        public void Dispose()
        {
            Disposals++;
            Published.Clear(); Unpublished.Clear(); ReadScopes.Clear(); Columns.Clear(); Ordered.Clear();
            PublishedEntities = Array.Empty<string>();
            Page = null; AfterRead = null; AfterImport = null;
        }
        /// <summary>此流程不建立單列。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>此流程不更新單列。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>此流程不刪除單列。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>此流程不使用單列讀取。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>此流程不建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>此流程不解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

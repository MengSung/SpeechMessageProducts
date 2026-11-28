/*
 * 檔案整體說明
 *
 * 目的與定位：驗證線 A 的 F0 唯讀盤點先於 FS quiesce，測試真實 reader 與合成 SDK 邊界，
 * 不建立 connector、不讀 User Secrets、不存取網路或真組織，也不將離線結果當作實機證據。
 * 主要流程：以伺服器組織身分與 metadata 建立固定候選，再檢查送出的管理欄位及 FetchXML
 * aggregate count；業務列、個資、token、LINE ID、configuration、定義 XML 與 trace 一律禁止。
 * 故障注入：錯組織、壞 metadata、聚合 alias 型別與來源錯誤、缺失或重複管理 ID、
 * 不完整續頁、錯誤 cookie、容量超限、原始 SDK 故障以及同步呼叫返回後的 caller 取消。
 * 聚合相容契約：依 Microsoft FetchXML SDK 範例，單一 alias 可有對應 FormattedValues；
 * 測試以合成的整數展示文字保護選用註記相容，來源仍須精確指向原表與主鍵。
 * 格式註記只允許同 alias 一筆、非空且最多64字元；不作計數來源、不複製到清冊，
 * 並與錯來源／型別／額外資料交叉注入，避免相容處理繞過既有 fail-closed 守門。
 * 實機形狀契約：管理資料的穩定 ID 不依賴可空白的顯示名稱；savedquery 的 statuscode
 * 依 Microsoft metadata 為非必填，缺少時須保留 statecode 與其他純值而非拒絕整份清冊。
 * 決定性斷言：零寫入、核身先於 metadata、缺表不是零、同名不同 ID 完整保留、
 * 五千零一筆管理資料完整分頁、失敗不發布候選、來源 mutation 不影響唯讀純值結果。
 * 資源與隔離：每案擁有自己的記憶體 service 與回應，無檔案／timer／背景工作；
 * cancellation source 由 using 釋放，借用 service 不得被 reader Dispose。並行案例各自獨立，
 * 弱參照案例保持 service 存活，確認 reader 不以 cache 保留結果。GC 只用於本離線回收斷言。
 * 維護驗證：只執行 ClonedOrganizationInventoryTests 定向測試；管理欄位 oracle 獨立列出，
 * 不以產品白名單生成預期值。新增查詢必須先證明它不會讀到業務內容或擴張可寫範圍。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Organizations;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>
/// 以可拒絕非白名單請求的合成 IOrganizationService 驗證實際 reader；
/// 所有資料都是測試生成的管理身分及計數，不代表 goodshepherd20 的真實內容。
/// </summary>
public sealed class ClonedOrganizationInventoryTests
{
    /// <summary>
    /// D365 內建 metadata 可有大小寫混合屬性；必須精確保留原名稱而不轉小寫，
    /// 同時仍拒絕不合法字元。只改合成 schema，不藉欄位名稱擴大資料查詢白名單。
    /// </summary>
    [Theory]
    [InlineData("resourcesJson", true)]
    [InlineData("msdyn_EntityLogicalName", true)]
    [InlineData("field<xml>", false)]
    [InlineData("field name", false)]
    public void Read_MetadataMixedCaseNames_PreservesExactIdentityOrRefusesInvalidSyntax(string name, bool valid)
    {
        using var service = new InventoryService();
        service.Metadata[0].Attributes.Single(attribute => attribute.LogicalName == "fullname").LogicalName = name;
        if (valid)
        {
            var snapshot = ClonedOrganizationInventory.Read(service, default);
            Assert.Contains(name, snapshot.Tables.Single(table => table.LogicalName == "contact").Attributes);
            Assert.Single(service.CountQueries);
        }
        else Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
        Assert.Equal(0, service.Writes);
    }

    /// <summary>
    /// 公開唯讀命令必須真正接到 reader；成功才發布完整清冊，故障不發布部分結果，
    /// caller 取消不告警。所有已取得租約均在返回前確定歸還，不讀秘密或連真組織。
    /// </summary>
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    public void Cli_Inventory_PublishesOnlyCompleteResultAndReturnsLease(string mode)
    {
        var root = Path.Combine(Path.GetTempPath(), "3p-f0-inventory-cli-" + Guid.NewGuid().ToString("N"));
        using var service = new InventoryService();
        using var cancellation = new CancellationTokenSource();
        using var output = new StringWriter();
        var reported = new List<Exception>();
        var connector = new InventoryConnector(service);
        if (mode == "failure") service.CountFailure = new IOException("Synthetic aggregate failure.");
        if (mode == "cancel") service.AfterMetadata = cancellation.Cancel;
        try
        {
            var result = DeploymentCli.RunWithCatalog(["inventory", "--org", "goodshepherd20"],
                OrganizationCatalog.LoadDefault(), connector, output, reported.Add, null, root,
                cancellationToken: cancellation.Token);
            Assert.Equal(mode == "success" ? 0 : mode == "cancel" ? 130 : 1, result);
            Assert.Equal(1, connector.Returned);
            Assert.Equal(0, service.Writes);
            var files = Directory.Exists(root)
                ? Directory.GetFiles(root, "clone-inventory.json", SearchOption.AllDirectories) : Array.Empty<string>();
            if (mode == "success")
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Assert.Single(files)));
                Assert.Equal("goodshepherd20", json.RootElement.GetProperty("uniqueName").GetString());
                Assert.Equal(123, json.RootElement.GetProperty("tables").EnumerateArray()
                    .Single(table => table.GetProperty("logicalName").GetString() == "contact")
                    .GetProperty("recordCount").GetInt32());
                Assert.Empty(reported);
            }
            else
            {
                Assert.Empty(files);
                if (mode == "failure") Assert.Same(service.CountFailure, Assert.Single(reported));
                else Assert.Empty(reported);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    /// <summary>盤點只允許精確複本與固定語法；其他組織或寫入旗標都須零連線拒絕。</summary>
    [Theory]
    [InlineData("construction", false)]
    [InlineData("association", false)]
    [InlineData("goodshepherdback", false)]
    [InlineData("goodshepherd20", true)]
    public void Cli_Inventory_InvalidScopeOrWriteFlag_RefusedBeforeConnect(string organization, bool apply)
    {
        using var service = new InventoryService();
        using var output = new StringWriter();
        var connector = new InventoryConnector(service);
        var reported = new List<Exception>();
        var args = apply ? new[] { "inventory", "--org", organization, "--apply" }
            : new[] { "inventory", "--org", organization };
        Assert.Equal(2, DeploymentCli.RunWithCatalog(args, OrganizationCatalog.LoadDefault(),
            connector, output, reported.Add, null));
        Assert.Equal(0, connector.Calls);
        Assert.Empty(reported);
    }

    /// <summary>本案 reader 的租約來源；SDK 仍由外層測試擁有，命令只歸還租約且不 Dispose 來源。</summary>
    private sealed class InventoryConnector(InventoryService service) : IOrganizationConnector
    {
        /// <summary>固定組織連線嘗試數，只保留本案計數，不讀設定。</summary>
        internal int Calls { get; private set; }
        /// <summary>命令返回前的租約確定歸還數；與 SDK 的外層 Dispose 分開驗證。</summary>
        internal int Returned { get; private set; }
        /// <summary>只借出本案純記憶體來源，不建立或轉移真正連線。</summary>
        public OrganizationLease Connect(string organization)
        {
            Assert.Equal("goodshepherd20", organization);
            Calls++;
            return new OrganizationLease(service, () => Returned++);
        }
    }

    /// <summary>聚合回應沒有資料列 ID 仍須正確接受；缺表明示不存在，既有空表才允許回零。</summary>
    [Fact]
    public void Read_AggregatesWithoutRecordIds_ReportsCountsAndMissingTables()
    {
        using var service = new InventoryService();
        var result = ClonedOrganizationInventory.Read(service, default);
        Assert.Equal(new Guid("ec4d281b-bbe7-f111-8cce-00155d007916"), result.OrganizationId);
        Assert.Equal("goodshepherd20", result.UniqueName);
        var contact = Assert.Single(result.Tables, table => table.LogicalName == "contact");
        Assert.True(contact.Exists);
        Assert.Equal(123L, contact.RecordCount);
        Assert.Contains("new_lineid", contact.Attributes);
        var outbound = Assert.Single(result.Tables, table => table.LogicalName == "new_outboundtask");
        Assert.False(outbound.Exists);
        Assert.Null(outbound.RecordCount);
        Assert.Null(outbound.PendingOutboundCount);
        Assert.Equal(0, service.Writes);
        Assert.Equal(0, service.Disposals);
        Assert.Collection(service.Calls.Take(2),
            call => Assert.Equal("identity", call), call => Assert.Equal("metadata", call));
    }

    /// <summary>只有精確的測試組織名稱才可讀 metadata；大小寫變體與其他測試組織也不等價。</summary>
    /// <param name="name">故意不符白名單的伺服器 UniqueName。</param>
    [Theory]
    [InlineData("goodshepherdback")]
    [InlineData("construction")]
    [InlineData("GoodShepherd20")]
    [InlineData("goodshepherd20 ")]
    public void Read_WrongServerName_StopsBeforeMetadata(string name)
    {
        using var service = new InventoryService { UniqueName = name };
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
        Assert.Equal("identity", Assert.Single(service.Calls));
    }

    /// <summary>同名錯組織 ID 也拒絕；明確預期 ID 可以供外層核定的複本使用，不能略過名稱守門。</summary>
    [Fact]
    public void Read_OrganizationId_IsPinnedOrExplicitlyProvided()
    {
        using var service = new InventoryService { OrganizationId = Guid.NewGuid() };
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
        Assert.Single(service.Calls);
        var result = ClonedOrganizationInventory.Read(service, default, service.OrganizationId);
        Assert.Equal(service.OrganizationId, result.OrganizationId);
        service.Calls.Clear();
        Assert.Throws<ArgumentException>(() => ClonedOrganizationInventory.Read(service, default, Guid.Empty));
        Assert.Empty(service.Calls);
    }

    /// <summary>正式 metadata 必須有唯一身分及完整屬性；不可利用重複、缺主鍵或容量超限發布模糊盤點。</summary>
    /// <param name="fault">合成 metadata 的單一破壞方式。</param>
    [Theory]
    [InlineData("empty-metadata")]
    [InlineData("duplicate-table")]
    [InlineData("duplicate-table-id")]
    [InlineData("missing-table-id")]
    [InlineData("missing-primary")]
    [InlineData("duplicate-field")]
    [InlineData("duplicate-field-id")]
    [InlineData("missing-field-id")]
    [InlineData("too-many-tables")]
    [InlineData("too-many-fields")]
    public void Read_InvalidMetadata_RejectsBeforeDataQueries(string fault)
    {
        using var service = new InventoryService { MetadataFault = fault };
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
        Assert.Equal(2, service.Calls.Count);
    }

    /// <summary>真正存在且沒有資料時才回零；與缺表維持不同結果，避免盤點誤判無待辦。</summary>
    [Fact]
    public void Read_ExistingEmptyTable_IsDifferentFromMissingTable()
    {
        using var service = new InventoryService { Count = 0 };
        var tables = ClonedOrganizationInventory.Read(service, default).Tables;
        Assert.Equal(0L, Assert.Single(tables, table => table.LogicalName == "contact").RecordCount);
        Assert.Null(Assert.Single(tables, table => table.LogicalName == "new_fee").RecordCount);
    }

    /// <summary>所有業務候選只可在 metadata 確認存在後送出 aggregate count；待送狀態亦在伺服器篩選。</summary>
    [Fact]
    public void Read_ExistingOutbound_UsesServerFilteredCountWithoutPayload()
    {
        using var service = new InventoryService();
        service.AddTable("new_outboundtask", "new_taskstatus", "new_payload");
        var outbound = Assert.Single(ClonedOrganizationInventory.Read(service, default).Tables,
            table => table.LogicalName == "new_outboundtask");
        Assert.True(outbound.Exists);
        Assert.Equal(123L, outbound.RecordCount);
        Assert.Equal(7L, outbound.PendingOutboundCount);
        Assert.Equal(2, service.CountQueries.Count(query => query.Contains("new_outboundtask", StringComparison.Ordinal)));
        Assert.Equal(0, service.Writes);
    }

    /// <summary>聚合與一般實體有不同契約；任何多列、原始 ID、錯 alias 來源或截斷都拒絕，不能默認零。</summary>
    /// <param name="fault">計數回應的故障，除正常 alias 外不允許額外業務欄位。</param>
    [Theory]
    [InlineData("empty")]
    [InlineData("two-rows")]
    [InlineData("record-id")]
    [InlineData("missing-alias")]
    [InlineData("extra-column")]
    [InlineData("wrong-alias-table")]
    [InlineData("wrong-alias-field")]
    [InlineData("plain-value")]
    [InlineData("negative")]
    [InlineData("fraction")]
    [InlineData("more-records")]
    [InlineData("cookie")]
    [InlineData("truncated")]
    public void Read_MalformedAggregate_FailsClosed(string fault)
    {
        using var service = new InventoryService { AggregateFault = fault };
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
        Assert.Equal(0, service.Writes);
    }

    /// <summary>
    /// 官方 FetchXML SDK 範例按 alias 選用格式註記；合成不同數字展示方式及 Int32／Int64，
    /// 驗證 reader 仍只發布原始整數，不解析展示文字、不回傳 SDK 註記，也不擷取業務資料列。
    /// 本案不模擬實機傳輸，service 由各案 using 唯一擁有，返回前不得被 reader 提早釋放。
    /// </summary>
    /// <param name="count">伺服器聚合的合成原始計數，與展示文字分開驗證。</param>
    /// <param name="formatted">同一 alias 的合成數字展示值；不同分組符號不能改變計數。</param>
    /// <param name="useInt64">是否以既有契約允許的 Int64 包裝原始計數。</param>
    [Theory]
    [InlineData(0, "0", false)]
    [InlineData(123, "123", false)]
    [InlineData(1234, "1,234", false)]
    [InlineData(1234, "1.234", true)]
    [InlineData(1234, "1\u202F234", true)]
    public void Read_AggregateFormattedCount_PublishesOnlyRawInteger(int count, string formatted, bool useInt64)
    {
        using var service = new InventoryService
        {
            Count = count, FormattedCount = formatted, UseInt64Count = useInt64
        };
        var snapshot = ClonedOrganizationInventory.Read(service, default);
        Assert.Equal((long)count, Assert.Single(snapshot.Tables, table => table.LogicalName == "contact").RecordCount);
        Assert.Single(service.CountQueries);
        Assert.Equal(0, service.Writes);
        Assert.Equal(0, service.Disposals);
    }

    /// <summary>
    /// 在同 alias 已帶格式註記時交叉注入來源、型別、額外資料與集合故障；
    /// 任何單一破壞仍須整份拒絕，格式字串不得補回遺失值、遮蔽錯主鍵或擴大讀取範圍。
    /// 缺失、空白、alias 名稱及大小寫變體不是官方來源名稱的替代品；只用合成來源且零寫入。
    /// </summary>
    /// <param name="fault">在合法格式註記之外施加的單一 SDK 回應破壞。</param>
    [Theory]
    [InlineData("empty")]
    [InlineData("two-rows")]
    [InlineData("record-id")]
    [InlineData("missing-alias")]
    [InlineData("extra-column")]
    [InlineData("wrong-alias-table")]
    [InlineData("wrong-alias-field")]
    [InlineData("missing-alias-table")]
    [InlineData("empty-alias-table")]
    [InlineData("alias-table-case")]
    [InlineData("missing-alias-field")]
    [InlineData("empty-alias-field")]
    [InlineData("alias-field-case")]
    [InlineData("alias-field-is-result-alias")]
    [InlineData("result-key-is-source-field")]
    [InlineData("wrong-row-table")]
    [InlineData("related-entity")]
    [InlineData("key-attribute")]
    [InlineData("plain-value")]
    [InlineData("null-count")]
    [InlineData("text-count")]
    [InlineData("negative")]
    [InlineData("fraction")]
    [InlineData("more-records")]
    [InlineData("cookie")]
    [InlineData("truncated")]
    [InlineData("wrong-collection")]
    public void Read_AggregateFormatting_DoesNotRelaxIdentityTypeOrCollectionGuards(string fault)
    {
        using var service = new InventoryService { AggregateFault = fault, FormattedCount = "123" };
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
        Assert.Empty(service.Calls.Where(call => call == "workflow"));
        Assert.Equal(0, service.Writes);
        Assert.Equal(0, service.Disposals);
    }

    /// <summary>
    /// 格式註記只能附著在唯一固定 alias，不能夾帶其他欄位或無界文字；
    /// 用未知鍵、額外鍵、null、空字串、空白及65字元故障保護容量與額外資料邊界。
    /// 註記無效即拒絕候選，不因原始計數合法而忽略形狀衝突。
    /// </summary>
    /// <param name="fault">只破壞 FormattedValues 的單一形狀或容量。</param>
    [Theory]
    [InlineData("formatted-unexpected-key")]
    [InlineData("formatted-extra-key")]
    [InlineData("formatted-null")]
    [InlineData("formatted-empty")]
    [InlineData("formatted-whitespace")]
    [InlineData("formatted-oversized")]
    public void Read_InvalidAggregateFormatting_FailsClosed(string fault)
    {
        using var service = new InventoryService { AggregateFault = fault, FormattedCount = "123" };
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
        Assert.Equal(0, service.Writes);
    }

    /// <summary>管理清冊提供 assembly→type→step、message/filter、流程及同步狀態，且不保留 SDK 參照。</summary>
    [Fact]
    public void Read_AdministrativeRelationships_AreDetachedPureValues()
    {
        using var service = new InventoryService();
        service.AddAdministrativeGraph();
        var snapshot = ClonedOrganizationInventory.Read(service, default);
        var step = Assert.Single(snapshot.Management, row => row.Entity == "sdkmessageprocessingstep");
        var type = Assert.Single(snapshot.Management, row => row.Entity == "plugintype");
        var assembly = Assert.Single(snapshot.Management, row => row.Entity == "pluginassembly");
        Assert.Equal(type.Id, step.References["eventhandler"].Id);
        Assert.Equal("plugintype", step.References["eventhandler"].Entity);
        Assert.Equal(assembly.Id, type.References["pluginassemblyid"].Id);
        Assert.Equal(0, step.Options["mode"]);
        Assert.Equal("statecode", step.Text["filteringattributes"]);
        var mailbox = Assert.Single(snapshot.Management, row => row.Entity == "mailbox");
        Assert.Equal(2, mailbox.Options["outgoingemaildeliverymethod"]);
        Assert.False(Assert.Single(snapshot.Management, row => row.Entity == "systemform").Flags["ismanaged"]);
        Assert.True(Assert.Single(snapshot.Management, row => row.Entity == "savedquery").Flags["ismanaged"]);
        service.Rows["sdkmessageprocessingstep"][0]["mode"] = new OptionSetValue(1);
        service.Rows["plugintype"][0].GetAttributeValue<EntityReference>("pluginassemblyid").Id = Guid.NewGuid();
        Assert.Equal(0, step.Options["mode"]);
        Assert.Equal(assembly.Id, type.References["pluginassemblyid"].Id);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)step.Options).Add("bad", 3));
        Assert.Throws<NotSupportedException>(() => ((IList<ClonedInventoryManagementRow>)snapshot.Management).Clear());
        service.Metadata[0].Attributes[1].LogicalName = "mutated";
        Assert.Contains("new_lineid", snapshot.Tables.Single(table => table.LogicalName == "contact").Attributes);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>
    /// 保護管理列以資料庫 ID 而非顯示名稱作為穩定身分；合成空白 Plugin step 名稱不得阻斷
    /// 完整唯讀清冊，也不得被拿來覆蓋或合併其他列。合成 SDK 唯一由 using 測試擁有，
    /// 決定性斷言為保留同一正式 ID、原樣保留空白顯示欄且零寫入。
    /// </summary>
    [Fact]
    public void Read_BlankPluginStepName_PreservesAuthoritativeIdentity()
    {
        using var service = new InventoryService();
        service.AddAdministrativeGraph();
        var source = Assert.Single(service.Rows["sdkmessageprocessingstep"]);
        source["name"] = "";

        var snapshot = ClonedOrganizationInventory.Read(service, default);

        var step = Assert.Single(snapshot.Management, row => row.Entity == "sdkmessageprocessingstep");
        Assert.Equal(source.Id, step.Id);
        Assert.Equal("", step.Text["name"]);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>
    /// 保護 SavedQuery 的選用 status reason 與必要 state／資料庫 ID 分離：故障注入是合成伺服器
    /// 列缺少非必填 statuscode；reader 應發布完整純值而不合成預設狀態。service 由 using 擁有，
    /// 決定性斷言為保留原 ID、statecode、managed 值、明確缺少 statuscode 且零寫入。
    /// </summary>
    [Fact]
    public void Read_SavedQueryMissingStatusReason_PreservesAuthoritativeState()
    {
        using var service = new InventoryService();
        service.AddAdministrativeGraph();
        var source = Assert.Single(service.Rows["savedquery"]);
        source.Attributes.Remove("statuscode");

        var snapshot = ClonedOrganizationInventory.Read(service, default);

        var view = Assert.Single(snapshot.Management, row => row.Entity == "savedquery");
        Assert.Equal(source.Id, view.Id);
        Assert.Equal(0, view.Options["statecode"]);
        Assert.False(view.Options.ContainsKey("statuscode"));
        Assert.True(view.Flags["ismanaged"]);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>不同正式 ID 即使同名也保留；管理集合超過單頁時，五千零一筆必須完整返回。</summary>
    [Fact]
    public void Read_Management5001Rows_UsesCompleteBoundedPaging()
    {
        using var service = new InventoryService { WorkflowCount = 5001 };
        var snapshot = ClonedOrganizationInventory.Read(service, default);
        Assert.Equal(5001, snapshot.Management.Count(row => row.Entity == "workflow"));
        Assert.Equal(2, service.WorkflowPages);
        Assert.All(snapshot.Management, row => Assert.Equal("same administrative name", row.Text["name"]));
    }

    /// <summary>一般管理列須有正式且唯一 ID；型別、欄位、lookup 邊界或分頁不完整一律拒絕。</summary>
    /// <param name="fault">在真正 reader 的 SDK 回應邊界注入的單一故障。</param>
    [Theory]
    [InlineData("empty-id")]
    [InlineData("missing-id-attribute")]
    [InlineData("mismatched-id")]
    [InlineData("duplicate-id")]
    [InlineData("wrong-table")]
    [InlineData("extra-column")]
    [InlineData("missing-state")]
    [InlineData("wrong-state-type")]
    [InlineData("missing-cookie")]
    [InlineData("bad-cookie")]
    [InlineData("dtd-cookie")]
    [InlineData("wrong-cookie-page")]
    [InlineData("repeated-cookie")]
    [InlineData("oversized-cookie")]
    [InlineData("empty-continuation")]
    [InlineData("oversized-page")]
    [InlineData("page-budget")]
    public void Read_InvalidAdministrativePage_RejectsWithoutPublication(string fault)
    {
        using var service = new InventoryService { PageFault = fault, WorkflowCount = 2 };
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
        Assert.InRange(service.WorkflowPages, 1, 10);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>lookup 不得引入業務資料 ID；Plugin 型別不能指到清冊中不存在的 assembly。</summary>
    /// <param name="fault">跨邊界或不完整管理關聯。</param>
    [Theory]
    [InlineData("business-reference")]
    [InlineData("missing-assembly")]
    public void Read_InvalidManagementReference_FailsClosed(string fault)
    {
        using var service = new InventoryService();
        service.AddAdministrativeGraph();
        service.Rows["plugintype"][0]["pluginassemblyid"] =
            new EntityReference(fault == "business-reference" ? "contact" : "pluginassembly", Guid.NewGuid());
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
    }

    /// <summary>即使兩筆 message ID 各自存在，step 與它的 filter 仍須屬於相同訊息，不能發佈自相矛盾的管理鏈。</summary>
    [Fact]
    public void Read_StepAndFilterDisagreeOnMessage_FailsClosed()
    {
        using var service = new InventoryService();
        service.AddAdministrativeGraph();
        var other = new Entity("sdkmessage", Guid.NewGuid()) { ["name"] = "Create" };
        other["sdkmessageid"] = other.Id;
        service.Rows["sdkmessage"].Add(other);
        service.Rows["sdkmessageprocessingstep"][0]["sdkmessageid"] = other.ToEntityReference();
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
    }

    /// <summary>十個完整管理頁可返回五萬筆；其後其他管理表多一筆必須整份拒絕，不能突破全盤點保留上限。</summary>
    [Fact]
    public void Read_TotalAdministrativeBudget_IsBoundedAcrossTables()
    {
        using var service = new InventoryService { WorkflowCount = 50000 };
        service.AddAdministrativeGraph();
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationInventory.Read(service, default));
        Assert.Equal(10, service.WorkflowPages);
        Assert.Equal(0, service.Disposals);
    }

    /// <summary>上限本身是合法完整盤點，五萬筆恰好十頁須全部返回；不是在邊界前一律截斷。</summary>
    [Fact]
    public void Read_Exactly50000AdministrativeRows_CompletesTenPages()
    {
        using var service = new InventoryService { WorkflowCount = 50000 };
        Assert.Equal(50000, ClonedOrganizationInventory.Read(service, default).Management.Count);
        Assert.Equal(10, service.WorkflowPages);
    }

    /// <summary>正常 caller 取消只在同步 SDK drain 後返回；故障保留原 Exception，重試重新讀取而非命中 cache。</summary>
    [Fact]
    public void Read_CancellationFaultAndRetry_KeepBorrowedServiceOwnedByCaller()
    {
        using var service = new InventoryService();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => ClonedOrganizationInventory.Read(service, cancellation.Token));
        Assert.Empty(service.Calls);
        using var during = new CancellationTokenSource();
        service.AfterMetadata = during.Cancel;
        Assert.ThrowsAny<OperationCanceledException>(() => ClonedOrganizationInventory.Read(service, during.Token));
        Assert.Equal(2, service.Calls.Count);
        service.AfterMetadata = null;
        var failure = new IOException("Synthetic SDK failure.");
        service.CountFailure = failure;
        Assert.Same(failure, Assert.Throws<IOException>(() => ClonedOrganizationInventory.Read(service, default)));
        service.CountFailure = null;
        Assert.Equal(123L, Assert.Single(ClonedOrganizationInventory.Read(service, default).Tables,
            table => table.LogicalName == "contact").RecordCount);
        Assert.Equal(0, service.Disposals);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>並行操作各自帶明確預期 server ID，結果互不污染；所有工作由測試等待完成，沒有遺留 task。</summary>
    [Fact]
    public void Read_ConcurrentOrganizations_StayOperationLocal()
    {
        Parallel.For(0, 64, index =>
        {
            using var service = new InventoryService { OrganizationId = Guid.NewGuid(), Count = index };
            var snapshot = ClonedOrganizationInventory.Read(service, default, service.OrganizationId);
            Assert.Equal(service.OrganizationId, snapshot.OrganizationId);
            Assert.Equal(index, Assert.Single(snapshot.Tables, table => table.LogicalName == "contact").RecordCount);
            Assert.Equal(0, service.Disposals);
        });
    }

    /// <summary>連續一百次盤點後，service 仍存活而結果可全部回收，防止 reader 靜態 cache 或閉包保留。</summary>
    [Fact]
    public void Read_CompletedSnapshots_AreNotRetained()
    {
        using var service = new InventoryService();
        var references = Enumerable.Range(0, 100).Select(_ => ReadWeak(service)).ToArray();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(service);
    }

    /// <summary>隔離 JIT 區域變數的最後存活範圍，只讓弱參照離開方法；不修改產品生命週期。</summary>
    private static WeakReference ReadWeak(InventoryService service) => new(ReadUnretained(service));

    /// <summary>禁止 inline，避免回收斷言受到呼叫端暫存 snapshot 影響。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ClonedOrganizationInventoryResult ReadUnretained(InventoryService service) =>
        ClonedOrganizationInventory.Read(service, default);

    /// <summary>
    /// 本案唯一合成外部邊界與記憶體 owner；只接受兩種唯讀 Execute、固定管理 ColumnSet 及聚合 FetchXML。
    /// 不持有連線或機密；Dispose 僅計數，讓斷言證明 reader 沒有接管外層租約。
    /// </summary>
    private sealed class InventoryService : IOrganizationService, IDisposable
    {
        /// <summary>合成 server 的正式 ID；可改為獨立值驗證 expected ID 比對，不能授權其他 UniqueName。</summary>
        internal Guid OrganizationId { get; init; } = new("ec4d281b-bbe7-f111-8cce-00155d007916");
        /// <summary>合成 server 名稱；核身錯誤案例要求任何後續 metadata 查詢都不得發生。</summary>
        internal string UniqueName { get; init; } = "goodshepherd20";
        /// <summary>只在 metadata 回應形成時注入固定格式故障，不讀取真環境。</summary>
        internal string? MetadataFault { get; init; }
        /// <summary>只破壞 alias 或聚合外形，避免將一般 Entity 驗證誤用於 aggregate。</summary>
        internal string? AggregateFault { get; init; }
        /// <summary>管理頁面的單一故障種類；頁數仍受產品的十頁上限控制。</summary>
        internal string? PageFault { get; init; }
        /// <summary>本次合成流程定義筆數；逐頁產生，不保留五萬筆 SDK 來源以外的額外副本。</summary>
        internal int WorkflowCount { get; init; }
        /// <summary>只代表 server aggregate 的合成非負整數，不從業務資料列 Count 推算。</summary>
        internal int Count { get; init; } = 123;
        /// <summary>僅存於單一測試 service 的合成展示文字；null 表示未附註記，不存入產品快照或跨案共用。</summary>
        internal string? FormattedCount { get; init; }
        /// <summary>以既有允許的 Int64 包裝合成計數，確保格式相容不改變整數型別契約。</summary>
        internal bool UseInt64Count { get; init; }
        /// <summary>任何寫入嘗試都累加並立即拒絕，故成功盤點必須保持零。</summary>
        internal int Writes { get; private set; }
        /// <summary>唯一 owner 是測試 using；reader 返回前不得變成非零。</summary>
        internal int Disposals { get; private set; }
        /// <summary>決定性斷言完整分頁與十頁停止，沒有延遲背景讀取。</summary>
        internal int WorkflowPages { get; private set; }
        /// <summary>由測試擁有的原始 SDK 故障；reader 必須保留同一 Exception 身分。</summary>
        internal Exception? CountFailure { get; set; }
        /// <summary>同步 metadata 返回時的單次取消注入；不排程 Task、不保留外部回呼。</summary>
        internal Action? AfterMetadata { get; set; }
        /// <summary>只記固定操作名稱，不記請求內容、cookie 或個資，用於核身先後斷言。</summary>
        internal List<string> Calls { get; } = new();
        /// <summary>只保存測試的 aggregate 查詢語法；不得有業務值、外部網址或機密。</summary>
        internal List<string> CountQueries { get; } = new();
        /// <summary>測試 owner 的合成 schema，可於返回後 mutation 驗證結果已複製。</summary>
        internal List<EntityMetadata> Metadata { get; } = new();
        /// <summary>僅管理表的合成資料庫；業務表的內容永遠不由 reader 讀取。</summary>
        internal Dictionary<string, List<Entity>> Rows { get; } = new(StringComparer.Ordinal);

        /// <summary>只建會友 schema 及流程管理 schema；敏感欄位名稱存在，但它的資料值絕不可被查詢。</summary>
        internal InventoryService()
        {
            AddTable("contact", "new_lineid", "fullname");
            AddTable("workflow", "name", "type", "category", "mode", "statecode", "statuscode", "primaryentity", "ismanaged");
        }

        /// <summary>以合成正式 metadata ID 建表，回應屬性僅由測試反射模擬 SDK 反序列化。</summary>
        internal void AddTable(string name, params string[] fields)
        {
            var metadata = new EntityMetadata { MetadataId = Guid.NewGuid(), LogicalName = name };
            var primary = name == "systemform" ? "formid" : name + "id";
            Set(metadata, "PrimaryIdAttribute", primary);
            Set(metadata, "IsManaged", false);
            Set(metadata, "Attributes", new[] { primary }.Concat(fields)
                .Select(field => new AttributeMetadata { LogicalName = field, MetadataId = Guid.NewGuid() }).ToArray());
            Metadata.Add(metadata);
            Rows[name] = new List<Entity>();
        }

        /// <summary>以獨立明列的 SDK 管理欄位建立完整關聯；表單和檢視沒有定義 XML 或業務內容。</summary>
        internal void AddAdministrativeGraph()
        {
            var assembly = AddRow("pluginassembly", "name", "version", "ismanaged", "isolationmode");
            assembly["name"] = "Synthetic.Plugin"; assembly["version"] = "1.0.0.0";
            assembly["ismanaged"] = false; assembly["isolationmode"] = new OptionSetValue(2);
            var type = AddRow("plugintype", "typename", "pluginassemblyid");
            type["typename"] = "Synthetic.Plugin.Handler"; type["pluginassemblyid"] = assembly.ToEntityReference();
            var message = AddRow("sdkmessage", "name"); message["name"] = "Update";
            var filter = AddRow("sdkmessagefilter", "sdkmessageid", "primaryobjecttypecode", "secondaryobjecttypecode");
            filter["sdkmessageid"] = message.ToEntityReference(); filter["primaryobjecttypecode"] = "contact";
            var step = AddRow("sdkmessageprocessingstep", "name", "eventhandler", "sdkmessageid", "sdkmessagefilterid",
                "statecode", "statuscode", "mode", "stage", "rank", "filteringattributes");
            step["name"] = "Synthetic registration"; step["eventhandler"] = type.ToEntityReference();
            step["sdkmessageid"] = message.ToEntityReference(); step["sdkmessagefilterid"] = filter.ToEntityReference();
            step["statecode"] = new OptionSetValue(0); step["statuscode"] = new OptionSetValue(1);
            step["mode"] = new OptionSetValue(0); step["stage"] = new OptionSetValue(20);
            step["rank"] = 1; step["filteringattributes"] = "statecode";
            var job = AddRow("asyncoperation", "statecode", "statuscode", "operationtype", "workflowactivationid");
            job["statecode"] = new OptionSetValue(0); job["statuscode"] = new OptionSetValue(0);
            job["operationtype"] = new OptionSetValue(10);
            var mailbox = AddRow("mailbox", "statecode", "statuscode", "incomingemaildeliverymethod",
                "outgoingemaildeliverymethod", "actdeliverymethod");
            mailbox["statecode"] = new OptionSetValue(0); mailbox["statuscode"] = new OptionSetValue(1);
            mailbox["incomingemaildeliverymethod"] = new OptionSetValue(2);
            mailbox["outgoingemaildeliverymethod"] = new OptionSetValue(2);
            mailbox["actdeliverymethod"] = new OptionSetValue(1);
            var solution = AddRow("solution", "uniquename", "ismanaged");
            solution["uniquename"] = "SyntheticSolution"; solution["ismanaged"] = false;
            var form = AddRow("systemform", "name", "objecttypecode", "type", "formactivationstate", "ismanaged");
            form["name"] = "Synthetic form"; form["objecttypecode"] = "contact";
            form["type"] = new OptionSetValue(2); form["formactivationstate"] = new OptionSetValue(1);
            form["ismanaged"] = false;
            var view = AddRow("savedquery", "name", "returnedtypecode", "querytype", "statecode", "statuscode", "ismanaged");
            view["name"] = "Synthetic view"; view["returnedtypecode"] = "contact"; view["querytype"] = 0;
            view["statecode"] = new OptionSetValue(0); view["statuscode"] = new OptionSetValue(1); view["ismanaged"] = true;
        }

        /// <summary>只建立測試擁有的管理列，主鍵屬性與 Entity.Id 一致，不建立任何業務 ID。</summary>
        private Entity AddRow(string table, params string[] fields)
        {
            AddTable(table, fields);
            var row = new Entity(table, Guid.NewGuid());
            row[table == "systemform" ? "formid" : table + "id"] = row.Id;
            Rows[table].Add(row);
            return row;
        }

        /// <summary>核身必須先發生；Execute 的其他訊息立即拒絕，防止寫入或任意 metadata 請求。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveCurrentOrganizationRequest)
            {
                Calls.Add("identity");
                return new RetrieveCurrentOrganizationResponse { Results = { ["Detail"] = new OrganizationDetail
                { OrganizationId = OrganizationId, UniqueName = UniqueName, OrganizationVersion = "9.1.0.0" } } };
            }
            var metadataRequest = Assert.IsType<RetrieveAllEntitiesRequest>(request);
            Assert.Equal("identity", Calls[^1]);
            Assert.Equal(EntityFilters.Entity | EntityFilters.Attributes, metadataRequest.EntityFilters);
            Assert.False(metadataRequest.RetrieveAsIfPublished);
            Calls.Add("metadata");
            var values = Metadata.ToList();
            if (MetadataFault == "empty-metadata") values.Clear();
            if (MetadataFault == "duplicate-table") values.Add(values[0]);
            if (MetadataFault == "duplicate-table-id") values[1].MetadataId = values[0].MetadataId;
            if (MetadataFault == "missing-table-id") values[0].MetadataId = null;
            if (MetadataFault == "missing-primary") Set(values[0], "PrimaryIdAttribute", "missingid");
            if (MetadataFault == "duplicate-field") Set(values[0], "Attributes", new[] { values[0].Attributes[0], values[0].Attributes[0] });
            if (MetadataFault == "duplicate-field-id") values[0].Attributes[1].MetadataId = values[0].Attributes[0].MetadataId;
            if (MetadataFault == "missing-field-id") values[0].Attributes[0].MetadataId = null;
            if (MetadataFault == "too-many-tables") values = Enumerable.Repeat(values[0], 4097).ToList();
            if (MetadataFault == "too-many-fields") Set(values[0], "Attributes", Enumerable.Repeat(values[0].Attributes[0], 2049).ToArray());
            AfterMetadata?.Invoke();
            return new RetrieveAllEntitiesResponse { Results = { ["EntityMetadata"] = values.ToArray() } };
        }

        /// <summary>只接受白名單管理查詢或伺服器聚合；所有業務非聚合查詢、敏感欄位及隱含全欄讀取均拒絕。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            Assert.Contains("metadata", Calls);
            if (query is FetchExpression fetch) return Aggregate(fetch);
            var expression = Assert.IsType<QueryExpression>(query);
            Calls.Add(expression.EntityName);
            Assert.False(expression.ColumnSet.AllColumns);
            Assert.Equal(5000, expression.PageInfo.Count);
            var primary = expression.EntityName == "systemform" ? "formid" : expression.EntityName + "id";
            Assert.Contains(expression.Orders, order => order.AttributeName == primary
                && order.OrderType == OrderType.Ascending);
            var allowed = expression.EntityName switch
            {
                "workflow" => "workflowid name type category mode statecode statuscode primaryentity ismanaged",
                "asyncoperation" => "asyncoperationid statecode statuscode operationtype workflowactivationid",
                "pluginassembly" => "pluginassemblyid name version ismanaged isolationmode",
                "plugintype" => "plugintypeid typename pluginassemblyid",
                "sdkmessage" => "sdkmessageid name",
                "sdkmessagefilter" => "sdkmessagefilterid sdkmessageid primaryobjecttypecode secondaryobjecttypecode",
                "sdkmessageprocessingstep" => "sdkmessageprocessingstepid name eventhandler sdkmessageid sdkmessagefilterid statecode statuscode mode stage rank filteringattributes",
                "mailbox" => "mailboxid statecode statuscode incomingemaildeliverymethod outgoingemaildeliverymethod actdeliverymethod",
                "solution" => "solutionid uniquename ismanaged",
                "systemform" => "formid name objecttypecode type formactivationstate ismanaged",
                "savedquery" => "savedqueryid name returnedtypecode querytype statecode statuscode ismanaged",
                _ => throw new InvalidOperationException("Non-administrative row query.")
            };
            Assert.Equal(allowed.Split(' ').Order(StringComparer.Ordinal), expression.ColumnSet.Columns.Order(StringComparer.Ordinal));
            if (expression.EntityName == "asyncoperation")
                Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "statecode"
                    && condition.Operator == ConditionOperator.In && condition.Values.Count == 2
                    && condition.Values[0] is 0 && condition.Values[1] is 1);
            if (expression.EntityName != "workflow") return new EntityCollection(Rows[expression.EntityName]);
            Assert.Contains(expression.Criteria.Conditions, condition => condition.AttributeName == "type"
                && condition.Operator == ConditionOperator.Equal && condition.Values.Single().Equals(1));
            WorkflowPages++;
            var page = expression.PageInfo.PageNumber;
            var count = PageFault == "oversized-page" ? 5001
                : PageFault == "page-budget" ? 1
                : PageFault is null ? Math.Min(5000, Math.Max(0, WorkflowCount - (page - 1) * 5000))
                : page == 1 ? 1 : 1;
            if (PageFault == "empty-continuation" && page == 2) count = 0;
            var rows = Enumerable.Range(0, count).Select(index => Workflow((page - 1) * 5000 + index + 1)).ToList();
            if (PageFault == "empty-id") rows[0].Id = Guid.Empty;
            if (PageFault == "missing-id-attribute") rows[0].Attributes.Remove("workflowid");
            if (PageFault == "mismatched-id") rows[0]["workflowid"] = Guid.NewGuid();
            if (PageFault == "duplicate-id" && page == 2) rows[0] = Workflow(1);
            if (PageFault == "wrong-table") rows[0].LogicalName = "contact";
            if (PageFault == "extra-column") rows[0]["xaml"] = "forbidden synthetic definition";
            if (PageFault == "missing-state") rows[0].Attributes.Remove("statecode");
            if (PageFault == "wrong-state-type") rows[0]["statecode"] = 1;
            var more = PageFault == "page-budget" || PageFault is null && WorkflowCount > page * 5000
                || page == 1 && PageFault is
                "duplicate-id" or "missing-cookie" or "bad-cookie" or "dtd-cookie" or "wrong-cookie-page"
                or "repeated-cookie" or "empty-continuation" or "oversized-cookie";
            var cookie = rows.Count == 0 ? null : Cookie(page, rows);
            if (PageFault == "missing-cookie") cookie = null;
            if (PageFault == "bad-cookie") cookie = "<invalid";
            if (PageFault == "dtd-cookie") cookie = "<!DOCTYPE cookie [<!ENTITY x SYSTEM 'file:///never-read'>]><cookie page='1'>&x;</cookie>";
            if (PageFault == "wrong-cookie-page") cookie = Cookie(9, rows);
            if (PageFault == "repeated-cookie" && page == 2) cookie = Cookie(1, new List<Entity> { Workflow(1) });
            if (PageFault == "oversized-cookie") cookie = new string('x', 65537);
            if (page > 1)
                Assert.False(string.IsNullOrWhiteSpace(expression.PageInfo.PagingCookie));
            return new EntityCollection(rows) { MoreRecords = more, PagingCookie = more || PageFault == "repeated-cookie" ? cookie : null };
        }

        /// <summary>
        /// 驗證 FetchXML 只有單一 count 主鍵 alias；不提供資料列 ID，也不載入任何業務列內容。
        /// 選用格式字串與回應故障只存在本案記憶體，用來測試來源、數值與註記的獨立驗證，
        /// 不能藉合成 Int64、別名或顯示值推論真實 D365 的回應形狀或失敗根因。
        /// </summary>
        private EntityCollection Aggregate(FetchExpression fetch)
        {
            Calls.Add("aggregate");
            CountQueries.Add(fetch.Query);
            if (CountFailure is not null) throw CountFailure;
            var root = XElement.Parse(fetch.Query);
            Assert.Equal("fetch", root.Name.LocalName);
            Assert.Equal("true", root.Attribute("aggregate")?.Value);
            var entity = Assert.Single(root.Elements());
            var table = entity.Attribute("name")!.Value;
            Assert.Contains(Metadata, item => item.LogicalName == table);
            Assert.True(table is "contact" or "new_outboundtask");
            var attribute = Assert.Single(entity.Elements("attribute"));
            Assert.Equal(table + "id", attribute.Attribute("name")?.Value);
            Assert.Equal("count", attribute.Attribute("aggregate")?.Value);
            Assert.Null(attribute.Attribute("distinct"));
            Assert.Empty(entity.Elements("link-entity"));
            var alias = attribute.Attribute("alias")!.Value;
            Assert.Equal("inventory_count", alias);
            var filter = entity.Element("filter");
            if (filter is not null)
            {
                var condition = Assert.Single(filter.Elements());
                Assert.Equal("new_outboundtask", table);
                Assert.Equal("new_taskstatus", condition.Attribute("attribute")?.Value);
                Assert.Equal("eq", condition.Attribute("operator")?.Value);
                Assert.Equal("100000000", condition.Attribute("value")?.Value);
            }
            var count = filter is null ? Count : 7;
            var row = new Entity(table)
            {
                [alias] = new AliasedValue(table, table + "id", UseInt64Count ? (object)(long)count : count)
            };
            if (FormattedCount is not null) row.FormattedValues[alias] = FormattedCount;
            if (AggregateFault == "record-id") row.Id = Guid.NewGuid();
            if (AggregateFault == "missing-alias") row.Attributes.Clear();
            if (AggregateFault == "extra-column") row["fullname"] = "forbidden synthetic content";
            if (AggregateFault == "wrong-alias-table") row[alias] = new AliasedValue("account", table + "id", 3);
            if (AggregateFault == "wrong-alias-field") row[alias] = new AliasedValue(table, "fullname", 3);
            if (AggregateFault == "missing-alias-table") row[alias] = new AliasedValue(null, table + "id", count);
            if (AggregateFault == "empty-alias-table") row[alias] = new AliasedValue("", table + "id", count);
            if (AggregateFault == "alias-table-case") row[alias] = new AliasedValue(table.ToUpperInvariant(), table + "id", count);
            if (AggregateFault == "missing-alias-field") row[alias] = new AliasedValue(table, null, count);
            if (AggregateFault == "empty-alias-field") row[alias] = new AliasedValue(table, "", count);
            if (AggregateFault == "alias-field-case") row[alias] = new AliasedValue(table, (table + "id").ToUpperInvariant(), count);
            if (AggregateFault == "alias-field-is-result-alias") row[alias] = new AliasedValue(table, alias, count);
            if (AggregateFault == "result-key-is-source-field")
            {
                var value = row[alias];
                row.Attributes.Clear();
                row[table + "id"] = value;
            }
            if (AggregateFault == "wrong-row-table") row.LogicalName = "account";
            if (AggregateFault == "related-entity")
                row.RelatedEntities.Add(new Relationship("synthetic_relation"), new EntityCollection());
            if (AggregateFault == "key-attribute") row.KeyAttributes.Add("synthetic_key", Guid.NewGuid());
            if (AggregateFault == "plain-value") row[alias] = 123;
            if (AggregateFault == "null-count") row[alias] = new AliasedValue(table, table + "id", null);
            if (AggregateFault == "text-count") row[alias] = new AliasedValue(table, table + "id", "123");
            if (AggregateFault == "negative") row[alias] = new AliasedValue(table, table + "id", -1);
            if (AggregateFault == "fraction") row[alias] = new AliasedValue(table, table + "id", 1.5m);
            if (AggregateFault == "formatted-unexpected-key")
            {
                row.FormattedValues.Clear();
                row.FormattedValues["fullname"] = "forbidden synthetic content";
            }
            if (AggregateFault == "formatted-extra-key") row.FormattedValues["fullname"] = "forbidden synthetic content";
            if (AggregateFault == "formatted-null") row.FormattedValues[alias] = null!;
            if (AggregateFault == "formatted-empty") row.FormattedValues[alias] = "";
            if (AggregateFault == "formatted-whitespace") row.FormattedValues[alias] = " ";
            if (AggregateFault == "formatted-oversized") row.FormattedValues[alias] = new string('1', 65);
            return new EntityCollection(AggregateFault == "empty" ? new List<Entity>()
                : AggregateFault == "two-rows" ? new List<Entity> { row, row } : new List<Entity> { row })
            {
                MoreRecords = AggregateFault == "more-records",
                PagingCookie = AggregateFault == "cookie" ? "<cookie/>" : null,
                TotalRecordCountLimitExceeded = AggregateFault == "truncated",
                EntityName = AggregateFault == "wrong-collection" ? "account" : null
            };
        }

        /// <summary>固定可重現的正式流程 ID；同名資料故意重複，以保護不能依顯示名稱去重的契約。</summary>
        private static Entity Workflow(int number)
        {
            var id = new Guid(number, 0, 0, new byte[8]);
            return new Entity("workflow", id)
            {
                ["workflowid"] = id, ["name"] = "same administrative name", ["type"] = new OptionSetValue(1),
                ["category"] = new OptionSetValue(0), ["mode"] = new OptionSetValue(0),
                ["statecode"] = new OptionSetValue(1), ["statuscode"] = new OptionSetValue(2),
                ["primaryentity"] = "contact", ["ismanaged"] = false
            };
        }

        /// <summary>合成與固定主鍵排序一致的 SDK cookie，不引用檔案、網路、業務 ID 或外部 XML entity。</summary>
        private static string Cookie(int page, List<Entity> rows) => string.Format(CultureInfo.InvariantCulture,
            "<cookie page=\"{0}\"><workflowid first=\"{1}\" last=\"{2}\" /></cookie>", page, rows[0].Id, rows[^1].Id);

        /// <summary>僅模擬 SDK 反序列化唯讀屬性；產品不得用這種方式繞過 metadata 的公開 setter 邊界。</summary>
        private static void Set(object instance, string property, object value) =>
            instance.GetType().GetProperty(property)!.SetValue(instance, value);

        /// <summary>reader 沒有任何寫入權限；一旦呼叫 Create 立刻計數並拒絕。</summary>
        public Guid Create(Entity entity) { Writes++; throw new NotSupportedException(); }
        /// <summary>更新不能以 dry-run 或 metadata 名義偷渡。</summary>
        public void Update(Entity entity) { Writes++; throw new NotSupportedException(); }
        /// <summary>禁止刪除既有或合成資料。</summary>
        public void Delete(string entityName, Guid id) { Writes++; throw new NotSupportedException(); }
        /// <summary>禁止單筆業務或設定讀取；所有合法讀取都必須經已驗證的管理清冊或 aggregate。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>禁止透過建立關聯形成隱性寫入。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        { Writes++; throw new NotSupportedException(); }
        /// <summary>禁止解除關聯或清理不屬於 reader 的資料。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        { Writes++; throw new NotSupportedException(); }
        /// <summary>由測試 using 擁有；計數可檢出 reader 是否提早關閉借用的外層 service。</summary>
        public void Dispose() => Disposals++;
    }
}

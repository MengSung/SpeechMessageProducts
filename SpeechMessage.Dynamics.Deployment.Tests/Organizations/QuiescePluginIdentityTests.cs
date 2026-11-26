/*
檔案整體說明
目的與定位：保護線 A FS 的 quiesce 唯讀 Plugin 身分清冊，透過真 CLI／reader 與合成 SDK
驗證正式 step→eventhandler→plugintype→pluginassembly 關聯，不讀 Secrets 或連線任何組織。
主要流程：每案建立獨立管理列、借用 SDK 租約及暫存 artifacts，檢查完整 JSON 純值輸出；
不以產品查詢白名單生成 oracle，避免禁讀 configuration、secureconfig、XAML、content 的契約失真。
workflow oracle 明列允許 solutionid，但此舊 fixture 不提供來源 ID；完整來源、缺失解析及隔離
由獨立 QuiesceWorkflowOriginTests 保護，原有 Plugin／workflow 管理欄位斷言不變。
信箱 oracle 只增加 before-image 必需的 incoming／outgoing／ACT 三欄，不允許郵件或認證欄位；
原像與寫前持久化契約由 QuiesceMailboxDeliveryTests 另以實際信箱合成列驗證。
故障注入與斷言：錯誤 lookup／型別／表與集合、重複正式 ID、不完整或循環續頁、超量及取消，
均須在發布清冊前失敗並歸還租約；有效同名不同 ID 必須保留，缺資料明示而不猜分類。
資源與隔離：service 僅存本案記憶體資料，不開連線、task、timer 或背景工作；命令擁有租約，
測試擁有暫存根、writer 與 cancellation source，finally／Dispose 只清理本案資源。
維護驗證：使用 FsQuiesceMetadata 獨立組態執行本檔與既有 quiesce 測試；RED→GREEN 僅為
離線契約證據，不代表真實 7280 個步驟已分類或可執行 apply。原有 policy guard 必須維持。
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
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Organizations;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>以每案獨立的 SDK 來源與暫存輸出，驗證公開命令發布正式身分且沒有任何寫入。</summary>
public sealed class QuiescePluginIdentityTests
{
    /// <summary>預設 opt-out 的查詢欄位及次數維持舊替身契約，新增欄位只在明確模式要求。</summary>
    [Fact]
    public void Run_DefaultMode_DoesNotReadAnyNewMetadata()
    {
        var service = new IdentityService();
        var metadataTables = new[] { "plugintype", "pluginassembly", "solution", "publisher" };
        var workflowColumns = new[] { "statecode", "statuscode", "workflowid" };
        var stepColumns = new[] { "sdkmessageprocessingstepid", "statecode", "statuscode" };
        service.Page = query =>
        {
            Assert.DoesNotContain(query.EntityName, metadataTables);
            if (query.EntityName == "workflow")
                Assert.Equal(workflowColumns, query.ColumnSet.Columns.Order(StringComparer.Ordinal));
            if (query.EntityName == "sdkmessageprocessingstep")
                Assert.Equal(stepColumns, query.ColumnSet.Columns.Order(StringComparer.Ordinal));
            return service.DefaultPage(query);
        };
        var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default);
        Assert.Empty(result.PluginIdentities);
        Assert.Empty(result.WorkflowIdentities);
        Assert.Equal(4, service.Reads.Count);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>明確模式沒有 active step 時仍輸出 workflow metadata，但不做型別或組件的額外查詢。</summary>
    [Fact]
    public void Run_ZeroPlugins_SkipsPluginMetadataQueries()
    {
        var service = new IdentityService();
        service.Page = query => query.EntityName == "sdkmessageprocessingstep"
            ? new EntityCollection() : service.DefaultPage(query);
        var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true);
        Assert.Empty(result.PluginIdentities);
        Assert.Single(result.WorkflowIdentities);
        Assert.False(service.Reads.ContainsKey("plugintype"));
        Assert.False(service.Reads.ContainsKey("pluginassembly"));
        Assert.Equal(0, service.Writes);
    }

    /// <summary>即使 metadata 完整也不可把未分類 step 自動放行；所有列更新之前必須拒絕 apply。</summary>
    [Fact]
    public void Run_MetadataDoesNotRelaxApplyGuard()
    {
        var service = new IdentityService();
        Assert.Throws<InvalidDataException>(() =>
            ClonedOrganizationQuiesce.Run(service, Target(), true, default, includePluginIdentities: true));
        Assert.Equal(0, service.Writes);
    }

    /// <summary>完整兩頁 7280 個 step 必須保留每個正式 ID；型別及組件各最多一次查詢，避免 N+1。</summary>
    [Fact]
    public void Run_7280ActiveSteps_PublishesBothPagesWithOneMetadataReadPerSource()
    {
        var service = new IdentityService();
        service.Page = query =>
        {
            if (query.EntityName != "sdkmessageprocessingstep") return service.DefaultPage(query);
            var start = query.PageInfo.PageNumber == 1 ? 1 : 5001;
            var count = start == 1 ? 5000 : 2280;
            if (start == 5001) Assert.Equal("step-page-1", query.PageInfo.PagingCookie);
            return new EntityCollection(Enumerable.Range(start, count).Select(index => StepRow(service, PageId(index))).ToList())
            {
                EntityName = "sdkmessageprocessingstep", MoreRecords = start == 1,
                PagingCookie = start == 1 ? "step-page-1" : null
            };
        };
        var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true);
        Assert.Equal(7280, result.PluginIdentities.Count);
        Assert.Equal(7280, result.UnclassifiedPluginSteps);
        Assert.Equal(PageId(1), result.PluginIdentities[0].StepId);
        Assert.Equal(PageId(7280), result.PluginIdentities[^1].StepId);
        Assert.Equal(7280, result.PluginIdentities.Select(row => row.StepId).ToHashSet().Count);
        Assert.Equal(2, service.Reads["sdkmessageprocessingstep"]);
        Assert.Equal(1, service.Reads["plugintype"]);
        Assert.Equal(1, service.Reads["pluginassembly"]);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>每個來源十頁剛好五萬列可以完整發布，第十頁仍有續頁則不得再讀第十一頁。</summary>
    /// <param name="table">獨立驗證每個來源，不以同一個總數上限掩蓋來源界線。</param>
    /// <param name="overflow">第十頁是否宣稱尚有更多資料。</param>
    [Theory]
    [InlineData("sdkmessageprocessingstep", false)]
    [InlineData("workflow", false)]
    [InlineData("plugintype", false)]
    [InlineData("pluginassembly", false)]
    [InlineData("sdkmessageprocessingstep", true)]
    [InlineData("workflow", true)]
    [InlineData("plugintype", true)]
    [InlineData("pluginassembly", true)]
    public void Run_SourceBudget_FiftyThousandAcceptedButContinuationRefused(string table, bool overflow)
    {
        var service = new IdentityService();
        service.Page = query =>
        {
            if (query.EntityName != table) return service.DefaultPage(query);
            Assert.InRange(query.PageInfo.PageNumber, 1, 10);
            var template = table switch
            {
                "workflow" => service.Workflow, "plugintype" => service.PluginType,
                "pluginassembly" => service.Assembly, _ => service.Step
            };
            var rows = Enumerable.Range((query.PageInfo.PageNumber - 1) * 5000 + 1, 5000)
                .Select(index =>
                {
                    var row = new Entity(table, index == 1 ? template.Id : PageId(index));
                    row.Attributes.AddRange(template.Attributes);
                    row[table + "id"] = row.Id;
                    return row;
                }).ToList();
            return new EntityCollection(rows)
            {
                EntityName = table, MoreRecords = overflow || query.PageInfo.PageNumber < 10,
                PagingCookie = "page-" + query.PageInfo.PageNumber.ToString(CultureInfo.InvariantCulture)
            };
        };
        if (overflow)
            Assert.Throws<InvalidDataException>(() =>
                ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true));
        else
        {
            var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true);
            Assert.Equal(table == "sdkmessageprocessingstep" ? 50_000 : 1, result.PluginIdentities.Count);
            Assert.Equal(table == "workflow" ? 50_000 : 1, result.WorkflowIdentities.Count);
        }
        Assert.Equal(10, service.Reads[table]);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>相同 typename 及組件名稱但不同正式 ID 是合法不同記錄，不能合併或覆寫。</summary>
    [Fact]
    public void Run_SameNamesDifferentIds_RemainsSeparate()
    {
        var service = new IdentityService();
        var otherType = new Entity("plugintype", PageId(2));
        otherType.Attributes.AddRange(service.PluginType.Attributes);
        otherType["plugintypeid"] = otherType.Id;
        var otherAssembly = new Entity("pluginassembly", PageId(3));
        otherAssembly.Attributes.AddRange(service.Assembly.Attributes);
        otherAssembly["pluginassemblyid"] = otherAssembly.Id;
        otherType["pluginassemblyid"] = new EntityReference("pluginassembly", otherAssembly.Id);
        var otherStep = StepRow(service, PageId(4));
        otherStep["eventhandler"] = new EntityReference("plugintype", otherType.Id);
        service.Page = query =>
        {
            var page = service.DefaultPage(query);
            if (query.EntityName == "sdkmessageprocessingstep") page.Entities.Add(otherStep);
            if (query.EntityName == "plugintype") page.Entities.Add(otherType);
            if (query.EntityName == "pluginassembly") page.Entities.Add(otherAssembly);
            return page;
        };
        var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true);
        Assert.Equal(2, result.PluginIdentities.Count);
        Assert.Equal(service.PluginType.Id, result.PluginIdentities[0].PluginTypeId);
        Assert.Equal(otherType.Id, result.PluginIdentities[1].PluginTypeId);
        Assert.Equal(service.Assembly.Id, result.PluginIdentities[0].PluginAssemblyId);
        Assert.Equal(otherAssembly.Id, result.PluginIdentities[1].PluginAssemblyId);
        Assert.All(result.PluginIdentities, row => Assert.Equal("Synthetic.Handler", row.TypeName));
    }

    /// <summary>來源在後續 SDK 讀取及返回後修改不可汙染已驗證候選，回傳集合也必須拒絕 caller mutation。</summary>
    [Fact]
    public void Run_SnapshotDetachesSdkObjectsAndProtectsCollections()
    {
        var service = new IdentityService();
        service.Page = query =>
        {
            if (query.EntityName == "plugintype")
            {
                service.Step.GetAttributeValue<EntityReference>("eventhandler").Id = Guid.NewGuid();
                service.Workflow.GetAttributeValue<BooleanManagedProperty>("iscustomizable").Value = true;
                service.Workflow.GetAttributeValue<OptionSetValue>("scope").Value = 1;
            }
            if (query.EntityName == "pluginassembly") service.PluginType["typename"] = "Changed.AfterTypeRead";
            return service.DefaultPage(query);
        };
        var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true);
        service.Assembly["name"] = "Changed.AfterReturn";
        var identity = Assert.Single(result.PluginIdentities);
        Assert.Equal(service.PluginType.Id, identity.HandlerId);
        Assert.Equal("Synthetic.Handler", identity.TypeName);
        Assert.Equal("Synthetic.Assembly", identity.AssemblyName);
        var workflow = Assert.Single(result.WorkflowIdentities);
        Assert.False(workflow.IsCustomizable);
        Assert.Equal(4, workflow.Scope);
        Assert.Throws<NotSupportedException>(() => ((IList<QuiescePluginIdentity>)result.PluginIdentities).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<QuiesceWorkflowIdentity>)result.WorkflowIdentities).Clear());
    }

    /// <summary>故障重試必須重新讀取管理來源；前次失敗或成功的資料不得從跨命令 cache 重用。</summary>
    [Fact]
    public void Run_FailureThenRetryAndNewRead_HaveNoCrossCommandCache()
    {
        var service = new IdentityService();
        var failure = new IOException("Synthetic assembly fault.");
        service.Page = query => query.EntityName == "pluginassembly" ? throw failure : service.DefaultPage(query);
        Assert.Same(failure, Assert.Throws<IOException>(() =>
            ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true)));
        service.Page = null;
        service.Assembly["name"] = "AfterRetry";
        var first = ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true);
        service.Assembly["name"] = "NextCommand";
        var second = ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true);
        Assert.Equal("AfterRetry", Assert.Single(first.PluginIdentities).AssemblyName);
        Assert.Equal("NextCommand", Assert.Single(second.PluginIdentities).AssemblyName);
        Assert.Equal(3, service.Reads["pluginassembly"]);
    }

    /// <summary>32 個同時獨立來源即使使用相同正式 ID，也不得互相混用型別、組件或 workflow 純值。</summary>
    [Fact]
    public async Task Run_ConcurrentCommands_KeepAllMetadataLocal()
    {
        var tasks = Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            var expected = "Synthetic." + index.ToString(CultureInfo.InvariantCulture);
            var service = new IdentityService();
            service.PluginType["typename"] = expected;
            service.Assembly["name"] = expected;
            service.Workflow["scope"] = new OptionSetValue(index);
            var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true);
            Assert.Equal(expected, Assert.Single(result.PluginIdentities).TypeName);
            Assert.Equal(expected, Assert.Single(result.PluginIdentities).AssemblyName);
            Assert.Equal(index, Assert.Single(result.WorkflowIdentities).Scope);
        }));
        await Task.WhenAll(tasks);
    }

    /// <summary>持有純值結果不應保留 service、SDK 列或 lookup；強制 GC 只用於本案離線生命週期斷言。</summary>
    [Fact]
    public void Run_ResultDoesNotRetainSdkSourceGraph()
    {
        var (result, sources) = ReadWithWeakSources();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.All(sources, reference => Assert.False(reference.IsAlive));
        Assert.Equal("Synthetic.Handler", Assert.Single(result.PluginIdentities).TypeName);
        GC.KeepAlive(result);
    }

    /// <summary>關閉內聯以分開 SDK owner 的 stack lifetime，返回結果與來源弱參照而不保留閉包。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ClonedOrganizationQuiesceResult Result, WeakReference[] Sources) ReadWithWeakSources()
    {
        var service = new IdentityService();
        return (ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true),
            [new WeakReference(service), new WeakReference(service.Step), new WeakReference(service.PluginType),
             new WeakReference(service.Assembly), new WeakReference(service.Workflow)]);
    }

    /// <summary>從測試 catalog 取得核定 test 描述，沒有 SDK 或 User Secrets 連線。</summary>
    private static OrganizationDefinition Target() => OrganizationCatalog.LoadDefault().GetTest("goodshepherd20");

    /// <summary>合成正式 step，複製允許欄位並改為獨立主鍵；保持同一型別是合法多步驟情境。</summary>
    private static Entity StepRow(IdentityService service, Guid id)
    {
        var row = new Entity("sdkmessageprocessingstep", id);
        row.Attributes.AddRange(service.Step.Attributes);
        row["sdkmessageprocessingstepid"] = id;
        return row;
    }

    /// <summary>workflow 只投影正式 ID 與管理欄位；保留 ManagedProperty 值，不讀名稱或猜 keep。</summary>
    [Fact]
    public void Cli_WorkflowMetadata_PreservesManagedPropertyAndOptions()
    {
        using var fixture = new CliFixture();
        Assert.Equal(0, fixture.Run());
        using var json = fixture.Inventory();
        var inventory = json.RootElement.GetProperty("inventory");
        var workflow = Assert.Single(inventory.GetProperty("workflowIdentities").EnumerateArray());
        Assert.Equal(fixture.Service.Workflow.Id, workflow.GetProperty("workflowId").GetGuid());
        Assert.True(workflow.GetProperty("isManaged").GetBoolean());
        Assert.False(workflow.GetProperty("isCustomizable").GetBoolean());
        Assert.Equal(0, workflow.GetProperty("category").GetInt32());
        Assert.Equal(1, workflow.GetProperty("mode").GetInt32());
        Assert.Equal(1, workflow.GetProperty("type").GetInt32());
        Assert.False(workflow.GetProperty("onDemand").GetBoolean());
        Assert.Equal(4, workflow.GetProperty("scope").GetInt32());
        Assert.Equal(1, inventory.GetProperty("workflowDefinitionsToDisable").GetInt32());
        Assert.Equal(0, fixture.Service.Writes);
    }

    /// <summary>缺值保持 null；若 managed property 被錯誤地當成 bool，整份清冊須拒絕。</summary>
    /// <param name="invalid">true 注入不符合 SDK 的 bool；false 模擬 metadata 未提供欄位。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cli_WorkflowManagedProperty_MissingIsUnknownButWrongTypeFails(bool invalid)
    {
        using var fixture = new CliFixture();
        fixture.Service.Workflow.Attributes.Remove("iscustomizable");
        if (invalid) fixture.Service.Workflow["iscustomizable"] = false;
        Assert.Equal(invalid ? 1 : 0, fixture.Run());
        if (invalid)
        {
            Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
            Assert.Empty(fixture.Files());
        }
        else
        {
            using var json = fixture.Inventory();
            var row = Assert.Single(json.RootElement.GetProperty("inventory").GetProperty("workflowIdentities").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("isCustomizable").ValueKind);
        }
        Assert.Equal(1, fixture.Returned);
        Assert.Equal(0, fixture.Service.Writes);
    }

    /// <summary>公開 dry-run 必須發布正式 handler、型別與組件純值；不得只回原本 step IDs。</summary>
    [Fact]
    public void Cli_DryRun_PublishesPluginIdentityWithoutClassifyingOrWriting()
    {
        using var fixture = new CliFixture();
        Assert.Equal(0, fixture.Run());
        using var json = fixture.Inventory();
        var inventory = json.RootElement.GetProperty("inventory");
        var identity = Assert.Single(inventory.GetProperty("pluginIdentities").EnumerateArray());
        Assert.Equal(fixture.Service.Step.Id, identity.GetProperty("stepId").GetGuid());
        Assert.Equal(fixture.Service.PluginType.Id, identity.GetProperty("handlerId").GetGuid());
        Assert.Equal("plugintype", identity.GetProperty("handlerType").GetString());
        Assert.Equal(fixture.Service.PluginType.Id, identity.GetProperty("pluginTypeId").GetGuid());
        Assert.Equal("Synthetic.Handler", identity.GetProperty("typeName").GetString());
        Assert.Equal(fixture.Service.Assembly.Id, identity.GetProperty("pluginAssemblyId").GetGuid());
        Assert.Equal("Synthetic.Assembly", identity.GetProperty("assemblyName").GetString());
        Assert.Equal("0123456789abcdef", identity.GetProperty("publicKeyToken").GetString());
        Assert.True(identity.GetProperty("isManaged").GetBoolean());
        Assert.Equal("Complete", identity.GetProperty("resolution").GetString());
        Assert.Equal(1, inventory.GetProperty("unclassifiedPluginSteps").GetInt32());
        Assert.Equal(0, inventory.GetProperty("pluginStepsToDisable").GetInt32());
        Assert.Equal(1, fixture.Returned);
        Assert.Empty(fixture.Failures);
        Assert.Equal(0, fixture.Service.Writes);
        Assert.Equal(1, fixture.Service.Reads["plugintype"]);
        Assert.Equal(1, fixture.Service.Reads["pluginassembly"]);
    }

    /// <summary>未知 handler target 必須拒絕，不得輸出誤導清冊；失敗後仍歸還 SDK 租約。</summary>
    [Fact]
    public void Cli_UnknownHandler_RefusesPublicationAndReturnsLease()
    {
        using var fixture = new CliFixture();
        fixture.Service.Step["eventhandler"] = new EntityReference("contact", Guid.NewGuid());
        Assert.Equal(1, fixture.Run());
        Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
        Assert.Empty(fixture.Files());
        Assert.Equal(1, fixture.Returned);
        Assert.Equal(0, fixture.Service.Writes);
    }

    /// <summary>缺資料須明示解析階段；合法 serviceendpoint 不查其內容，也不能猜為 Plugin 或可保留。</summary>
    /// <param name="missing">要移除的管理關聯或資料列，不是業務資料。</param>
    /// <param name="resolution">獨立指定的缺失分類，不代表 disable／keep 決策。</param>
    [Theory]
    [InlineData("handler", "HandlerMissing")]
    [InlineData("endpoint", "ServiceEndpoint")]
    [InlineData("type", "PluginTypeMissing")]
    [InlineData("assembly-lookup", "PluginAssemblyLookupMissing")]
    [InlineData("assembly", "PluginAssemblyMissing")]
    [InlineData("fields", "MetadataIncomplete")]
    public void Cli_MissingMetadata_IsExplicitWithoutGuessing(string missing, string resolution)
    {
        using var fixture = new CliFixture();
        if (missing == "handler") fixture.Service.Step.Attributes.Remove("eventhandler");
        if (missing == "endpoint") fixture.Service.Step["eventhandler"] = new EntityReference("serviceendpoint", Guid.NewGuid());
        if (missing == "assembly-lookup") fixture.Service.PluginType.Attributes.Remove("pluginassemblyid");
        if (missing == "fields") fixture.Service.Assembly.Attributes.Remove("ismanaged");
        fixture.Service.Page = query =>
            (missing == "type" && query.EntityName == "plugintype"
             || missing == "assembly" && query.EntityName == "pluginassembly")
                ? new EntityCollection { EntityName = query.EntityName } : fixture.Service.DefaultPage(query);
        Assert.Equal(0, fixture.Run());
        using var json = fixture.Inventory();
        var inventory = json.RootElement.GetProperty("inventory");
        var row = Assert.Single(inventory.GetProperty("pluginIdentities").EnumerateArray());
        Assert.Equal(resolution, row.GetProperty("resolution").GetString());
        if (missing is "handler" or "endpoint") Assert.Equal(JsonValueKind.Null, row.GetProperty("pluginTypeId").ValueKind);
        if (missing == "type") Assert.Equal(JsonValueKind.Null, row.GetProperty("typeName").ValueKind);
        if (missing is "handler" or "endpoint" or "type" or "assembly-lookup" or "assembly")
            Assert.Equal(JsonValueKind.Null, row.GetProperty("assemblyName").ValueKind);
        if (missing == "fields") Assert.Equal(JsonValueKind.Null, row.GetProperty("isManaged").ValueKind);
        Assert.Equal(1, inventory.GetProperty("unclassifiedPluginSteps").GetInt32());
        Assert.Equal(0, fixture.Service.Writes);
    }

    /// <summary>所有新增 metadata 來源都要有界完整讀取；壞頁在 JSON 發布與任何寫入前拒絕。</summary>
    /// <param name="table">注入異常的正式管理表。</param>
    /// <param name="fault">空／重複身分、型別、表、集合或續頁故障。</param>
    [Theory]
    [InlineData("sdkmessageprocessingstep", "duplicate")]
    [InlineData("workflow", "duplicate")]
    [InlineData("workflow", "empty-id")]
    [InlineData("workflow", "wrong-collection")]
    [InlineData("workflow", "primary-type")]
    [InlineData("workflow", "bool-type")]
    [InlineData("workflow", "option-type")]
    [InlineData("plugintype", "duplicate")]
    [InlineData("pluginassembly", "duplicate")]
    [InlineData("plugintype", "duplicate-next-page")]
    [InlineData("pluginassembly", "terminal-cookie-repeat")]
    [InlineData("plugintype", "empty-id")]
    [InlineData("pluginassembly", "empty-id")]
    [InlineData("plugintype", "primary-mismatch")]
    [InlineData("pluginassembly", "primary-type")]
    [InlineData("plugintype", "wrong-row")]
    [InlineData("pluginassembly", "wrong-collection")]
    [InlineData("sdkmessageprocessingstep", "wrong-collection")]
    [InlineData("plugintype", "missing-cookie")]
    [InlineData("pluginassembly", "empty-page")]
    [InlineData("plugintype", "empty-terminal")]
    [InlineData("plugintype", "null-page")]
    [InlineData("plugintype", "repeated-cookie")]
    [InlineData("pluginassembly", "cycle-cookie")]
    [InlineData("plugintype", "eleventh-page")]
    [InlineData("pluginassembly", "oversize")]
    [InlineData("pluginassembly", "text-type")]
    [InlineData("plugintype", "text-type")]
    [InlineData("plugintype", "too-long-text")]
    [InlineData("pluginassembly", "too-long-text")]
    [InlineData("pluginassembly", "too-long-key")]
    [InlineData("plugintype", "extra-column")]
    [InlineData("pluginassembly", "bool-type")]
    [InlineData("plugintype", "lookup-type")]
    [InlineData("plugintype", "lookup-target")]
    [InlineData("plugintype", "lookup-id")]
    [InlineData("plugintype", "lookup-keys")]
    [InlineData("sdkmessageprocessingstep", "lookup-type")]
    [InlineData("sdkmessageprocessingstep", "lookup-id")]
    [InlineData("sdkmessageprocessingstep", "lookup-keys")]
    public void Cli_InvalidSource_RefusesWholeCandidate(string table, string fault)
    {
        using var fixture = new CliFixture();
        fixture.Service.Page = query =>
        {
            var page = fixture.Service.DefaultPage(query);
            if (query.EntityName != table) return page;
            if (fault == "null-page") return null!;
            var row = Assert.Single(page.Entities);
            switch (fault)
            {
                case "duplicate": page.Entities.Add(row); break;
                case "empty-id": row.Id = Guid.Empty; break;
                case "primary-mismatch": row[table + "id"] = Guid.NewGuid(); break;
                case "primary-type": row[table + "id"] = "invalid"; break;
                case "wrong-row": row.LogicalName = "contact"; break;
                case "wrong-collection": page.EntityName = "contact"; break;
                case "missing-cookie": page.MoreRecords = true; break;
                case "empty-page": page.Entities.Clear(); page.MoreRecords = true; page.PagingCookie = "next"; break;
                case "duplicate-next-page":
                    page.MoreRecords = query.PageInfo.PageNumber == 1; page.PagingCookie = "next"; break;
                case "empty-terminal":
                    if (query.PageInfo.PageNumber == 2) page.Entities.Clear();
                    page.MoreRecords = query.PageInfo.PageNumber == 1;
                    page.PagingCookie = query.PageInfo.PageNumber == 1 ? "next" : null;
                    break;
                case "terminal-cookie-repeat":
                    row.Id = PageId(query.PageInfo.PageNumber); row[table + "id"] = row.Id;
                    page.MoreRecords = query.PageInfo.PageNumber == 1; page.PagingCookie = "same";
                    break;
                case "oversize":
                    for (var index = 1; index <= 5000; index++) page.Entities.Add(row);
                    break;
                case "text-type": row[table == "plugintype" ? "typename" : "name"] = 42; break;
                case "too-long-text": row[table == "plugintype" ? "typename" : "name"] = new string('x', 257); break;
                case "too-long-key": row["publickeytoken"] = new string('a', 33); break;
                case "extra-column": row["content"] = "Synthetic forbidden content"; break;
                case "bool-type": row["ismanaged"] = new OptionSetValue(1); break;
                case "option-type": row["category"] = 1; break;
                case "lookup-type": row[table == "plugintype" ? "pluginassemblyid" : "eventhandler"] = Guid.NewGuid(); break;
                case "lookup-target": row["pluginassemblyid"] = new EntityReference("contact", Guid.NewGuid()); break;
                case "lookup-id": row[table == "plugintype" ? "pluginassemblyid" : "eventhandler"] =
                    new EntityReference(table == "plugintype" ? "pluginassembly" : "plugintype", Guid.Empty); break;
                case "lookup-keys":
                    row.GetAttributeValue<EntityReference>(table == "plugintype" ? "pluginassemblyid" : "eventhandler")
                        .KeyAttributes.Add("name", "Synthetic.AlternateKey");
                    break;
                default:
                    row.Id = PageId(query.PageInfo.PageNumber);
                    row[table + "id"] = row.Id;
                    page.MoreRecords = true;
                    page.PagingCookie = fault == "repeated-cookie" ? "a"
                        : fault == "cycle-cookie" ? query.PageInfo.PageNumber % 2 == 0 ? "b" : "a"
                        : query.PageInfo.PageNumber.ToString(CultureInfo.InvariantCulture);
                    break;
            }
            return page;
        };
        Assert.Equal(1, fixture.Run());
        Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
        Assert.Empty(fixture.Files());
        Assert.Equal(1, fixture.Returned);
        Assert.Equal(0, fixture.Service.Writes);
        Assert.InRange(fixture.Service.Reads[table], 1, 10);
    }

    /// <summary>各 SDK 返回後的 caller 取消與真正故障皆不可發布部分清冊，原故障與租約清理須保真。</summary>
    /// <param name="table">故障注入位置，包含最後一個管理來源。</param>
    /// <param name="cancel">true 是 caller 取消，false 是原始 IOException。</param>
    [Theory]
    [InlineData("workflow", true)]
    [InlineData("sdkmessageprocessingstep", true)]
    [InlineData("plugintype", true)]
    [InlineData("pluginassembly", true)]
    [InlineData("plugintype", false)]
    [InlineData("pluginassembly", false)]
    [InlineData("workflow", false)]
    public void Cli_CancellationOrFailure_DoesNotPublishAndReleases(string table, bool cancel)
    {
        using var fixture = new CliFixture();
        using var cancellation = new CancellationTokenSource();
        var failure = new IOException("Synthetic metadata fault.");
        fixture.Service.Page = query =>
        {
            if (query.EntityName == table)
            {
                if (cancel) cancellation.Cancel();
                else throw failure;
            }
            return fixture.Service.DefaultPage(query);
        };
        Assert.Equal(cancel ? 130 : 1, fixture.Run(cancellation.Token));
        if (cancel) Assert.Empty(fixture.Failures);
        else Assert.Same(failure, Assert.Single(fixture.Failures));
        Assert.Empty(fixture.Files());
        Assert.Equal(1, fixture.Returned);
        Assert.Equal(0, fixture.Service.Writes);
    }

    /// <summary>頁次產生不同合成正式主鍵，與測試缺陷無關，不使用產品 helper 生成 oracle。</summary>
    private static Guid PageId(int page) => new(page, 0, 0, new byte[8]);

    /// <summary>命令級檔案與租約 owner；只在獨立暫存根輸出，清理不接觸任何工作樹或其他命令。</summary>
    private sealed class CliFixture : IOrganizationConnector, IDisposable
    {
        /// <summary>本案唯一暫存根，使用隨機目錄隔離其他並行測試。</summary>
        private readonly string root = Path.Combine(Path.GetTempPath(), "fs-quiesce-identities-" + Guid.NewGuid().ToString("N"));
        /// <summary>呼叫端持有 writer，命令結束後由 fixture Dispose。</summary>
        private readonly StringWriter output = new(CultureInfo.InvariantCulture);
        /// <summary>每案只用自己的合成管理資料，不保留真連線或認證。</summary>
        internal IdentityService Service { get; } = new();
        /// <summary>真正故障交還原始 Exception；正常取消不得加入此集合。</summary>
        internal List<Exception> Failures { get; } = new();
        /// <summary>租約歸還數用於證明成功及所有失敗分支完成清理。</summary>
        internal int Returned { get; private set; }
        /// <summary>執行真公開命令；注入 catalog 與 SDK，不改工作目錄或載入認證。</summary>
        internal int Run(CancellationToken token = default) =>
            DeploymentCli.RunWithCatalog(["quiesce", "--org", "goodshepherd20"],
                OrganizationCatalog.LoadDefault(), this, output, Failures.Add, null, root,
                secretReader: _ => throw new InvalidOperationException("Secrets must not be read."),
                cancellationToken: token);
        /// <summary>只借給核准 test 組織；fixture 保留記憶體 service 所有權，命令只歸還租約。</summary>
        public OrganizationLease Connect(string organization)
        {
            Assert.Equal("goodshepherd20", organization);
            return new OrganizationLease(Service, () => Returned++);
        }
        /// <summary>清冊只有成功完整發布才存在；失敗案例檢查同一目錄沒有部分結果。</summary>
        internal string[] Files() => Directory.Exists(root)
            ? Directory.GetFiles(root, "quiesce-inventory.json", SearchOption.AllDirectories) : [];
        /// <summary>JSON 解析結果交呼叫測試 using 擁有，不將檔案串流留在 fixture。</summary>
        internal JsonDocument Inventory() => JsonDocument.Parse(File.ReadAllText(Assert.Single(Files())));
        /// <summary>writer 先關閉再刪本案暫存根；命令已同步返回，不存在待排空工作。</summary>
        public void Dispose()
        {
            output.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// 僅提供本案純管理 metadata 的 SDK 替身，獨立白名單攔截所有查詢與副作用。
    /// 不擁有任何 IDisposable 或背景資源；正式 SDK 所有權由 CLI 租約控制。
    /// </summary>
    private sealed class IdentityService : IOrganizationService
    {
        /// <summary>正式 step ID 與 lookup；測試可注入異常型別或來源 mutation。</summary>
        internal Entity Step { get; } = new("sdkmessageprocessingstep", new Guid("10000000-0000-0000-0000-000000000001"));
        /// <summary>合成型別的正式表 ID，不使用 typename 作身分。</summary>
        internal Entity PluginType { get; } = new("plugintype", new Guid("20000000-0000-0000-0000-000000000001"));
        /// <summary>合成組件正式 ID；僅提供允許的管理屬性。</summary>
        internal Entity Assembly { get; } = new("pluginassembly", new Guid("30000000-0000-0000-0000-000000000001"));
        /// <summary>合成 Microsoft 系統定義也只盤點 metadata，由主代理據證據建立 keep 清冊。</summary>
        internal Entity Workflow { get; } = new("workflow", new Guid("50000000-0000-0000-0000-000000000001"));
        /// <summary>每來源讀取次數，確保零 Plugin 不多查或超限查第十一頁。</summary>
        internal Dictionary<string, int> Reads { get; } = new(StringComparer.Ordinal);
        /// <summary>任何寫入都計數並失敗，不允許假 SDK 靜默接受副作用。</summary>
        internal int Writes { get; private set; }
        /// <summary>本案可注入單頁故障；不啟動延遲 callback 或跨案共享狀態。</summary>
        internal Func<QueryExpression, EntityCollection>? Page { get; set; }
        /// <summary>初始化可獨立手算的正式關聯與純值，沒有業務資料、內容或設定。</summary>
        internal IdentityService()
        {
            Step["sdkmessageprocessingstepid"] = Step.Id;
            Step["statecode"] = new OptionSetValue(0);
            Step["statuscode"] = new OptionSetValue(1);
            Step["eventhandler"] = new EntityReference("plugintype", PluginType.Id);
            PluginType["plugintypeid"] = PluginType.Id;
            PluginType["typename"] = "Synthetic.Handler";
            PluginType["pluginassemblyid"] = new EntityReference("pluginassembly", Assembly.Id);
            Assembly["pluginassemblyid"] = Assembly.Id;
            Assembly["name"] = "Synthetic.Assembly";
            Assembly["publickeytoken"] = "0123456789abcdef";
            Assembly["ismanaged"] = true;
            Workflow["workflowid"] = Workflow.Id;
            Workflow["statecode"] = new OptionSetValue(1);
            Workflow["statuscode"] = new OptionSetValue(2);
            Workflow["ismanaged"] = true;
            Workflow["iscustomizable"] = new BooleanManagedProperty(false);
            Workflow["category"] = new OptionSetValue(0);
            Workflow["mode"] = new OptionSetValue(1);
            Workflow["type"] = new OptionSetValue(1);
            Workflow["ondemand"] = false;
            Workflow["scope"] = new OptionSetValue(4);
        }
        /// <summary>獨立精確欄位 oracle 拒絕敏感讀取，僅傳回合成正式管理列或空集合。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            var columns = expression.EntityName switch
            {
                "asyncoperation" => new[] { "asyncoperationid", "statecode", "statuscode" },
                "workflow" => ["workflowid", "statecode", "statuscode", "ismanaged", "iscustomizable",
                    "category", "mode", "type", "ondemand", "scope", "solutionid"],
                "sdkmessageprocessingstep" => ["sdkmessageprocessingstepid", "statecode", "statuscode", "eventhandler"],
                "mailbox" => ["mailboxid", "statecode", "statuscode",
                    "incomingemaildeliverymethod", "outgoingemaildeliverymethod", "actdeliverymethod"],
                "plugintype" => ["plugintypeid", "typename", "pluginassemblyid"],
                "pluginassembly" => ["pluginassemblyid", "name", "publickeytoken", "ismanaged"],
                _ => throw new InvalidOperationException("Unexpected table.")
            };
            Assert.False(expression.ColumnSet.AllColumns);
            // oracle 獨立列完整允許欄位；DefaultPage 同時遵守實際 ColumnSet，避免替身偷偷補值。
            Assert.All(expression.ColumnSet.Columns, column => Assert.Contains(column, columns));
            Assert.Equal(5000, expression.PageInfo.Count);
            var order = Assert.Single(expression.Orders);
            Assert.Equal(expression.EntityName + "id", order.AttributeName);
            Assert.Equal(OrderType.Ascending, order.OrderType);
            Reads[expression.EntityName] = Reads.GetValueOrDefault(expression.EntityName) + 1;
            return Page is null ? DefaultPage(expression) : Page(expression);
        }
        /// <summary>產生本頁回應；各列由本案持有，讓來源 mutation／壞頁注入能觀察真 reader 行為。</summary>
        internal EntityCollection DefaultPage(QueryExpression expression)
        {
            var rows = expression.EntityName switch
            {
                "sdkmessageprocessingstep" => new List<Entity> { Step },
                "plugintype" => [PluginType],
                "pluginassembly" => [Assembly],
                "workflow" => [Workflow],
                _ => []
            };
            return new EntityCollection(rows.Select(source =>
            {
                var row = new Entity(source.LogicalName, source.Id);
                foreach (var column in expression.ColumnSet.Columns)
                    if (source.Attributes.TryGetValue(column, out var value)) row[column] = value;
                return row;
            }).ToList()) { EntityName = expression.EntityName };
        }
        /// <summary>唯一允許的 Execute 是純值組織核身，不回傳 credential 或業務資料。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Assert.IsType<RetrieveCurrentOrganizationRequest>(request);
            return new RetrieveCurrentOrganizationResponse
            {
                Results = { ["Detail"] = new OrganizationDetail
                {
                    OrganizationId = new Guid("40000000-0000-0000-0000-000000000001"),
                    UniqueName = "goodshepherd20", OrganizationVersion = "9.1.0.0"
                } }
            };
        }
        /// <summary>拒絕新增並記錄副作用，讓 dry-run 的零寫入契約可直接斷言。</summary>
        public Guid Create(Entity entity) { Writes++; throw new NotSupportedException(); }
        /// <summary>拒絕更新，分類與 apply 不得因新 metadata 被自動推導。</summary>
        public void Update(Entity entity) { Writes++; throw new NotSupportedException(); }
        /// <summary>拒絕刪除，測試不允許以移除資料達到空清冊。</summary>
        public void Delete(string entityName, Guid id) { Writes++; throw new NotSupportedException(); }
        /// <summary>不使用逐 step 單筆查詢；禁止 N+1 或未限制欄位的讀取。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>唯讀清冊不得建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        { Writes++; throw new NotSupportedException(); }
        /// <summary>唯讀清冊不得解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        { Writes++; throw new NotSupportedException(); }
    }
}

/*
檔案整體說明
目的與定位：線 A FS workflow 來源 sidecar 的獨立離線契約測試，經真實 quiesce reader
與公開 CLI 驗證 workflow.solutionid 的 Guid、solution 正式 ID 及 publisher lookup 關聯。
主要流程：每案建立獨立合成 SDK 來源與暫存 artifacts，從發布 JSON 核對純值及 Resolution；
查詢 oracle 獨立明列允許欄位，不讀 workflow name／XAML、Secrets、業務內容或任何實機。
信箱允許欄位僅增加 before-image 所需的三個 delivery 選項；本 fixture 不提供信箱資料，
原像完整性與第一筆 Update 前的落盤／flush 由 QuiesceMailboxDeliveryTests 獨立驗證。
保護契約與故障注入：同名不同 ID 必須保留；錯型別、錯 lookup、重複身分、不完整分頁、
超過十頁五萬列與 caller 取消均拒絕整份候選。缺少來源保持未知，完整來源也不建立 policy。
資源與隔離：SDK 僅為本案記憶體替身，無連線、timer 或背景服務；CLI 擁有借用租約。
測試唯一擁有 writer、取消來源與隨機暫存根；並行案各自持有來源，全部 task 完成才返回，
Dispose 只清本案 artifacts。弱參照斷言驗證持有發布結果不會保留 SDK 來源圖。
維護驗證：以 FsWorkflowOrigin 獨立 Configuration 執行，不使用 --no-restore；RED→GREEN、
JSON 不發布與租約歸還只證明離線契約，不代表真實來源已分類、已 apply 或組織已停用。
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

/// <summary>獨立合成來源驗證來源身分、完整發布、查詢預算、取消與命令生命週期，絕不連線實機。</summary>
public sealed class QuiesceWorkflowOriginTests
{
    /// <summary>漏讀來源或把 workflow.solutionid 當 lookup 會失敗；公開 JSON 必須完整保留正式鏈及管理純值。</summary>
    [Fact]
    public void Cli_ExactDatabaseIds_PublishesOriginWithoutChangingPolicy()
    {
        using var fixture = new OriginFixture();
        Assert.Equal(0, fixture.Run());
        using var json = fixture.Inventory();
        var inventory = json.RootElement.GetProperty("inventory");
        var row = Assert.Single(inventory.GetProperty("workflowIdentities").EnumerateArray());
        AssertOrigin(row, fixture.Service.Solution.Id, fixture.Service.Publisher.Id);
        Assert.Equal("synthetic_solution", row.GetProperty("solutionUniqueName").GetString());
        Assert.True(row.GetProperty("solutionIsManaged").GetBoolean());
        Assert.Equal("synthetic_publisher", row.GetProperty("publisherUniqueName").GetString());
        Assert.Equal("syn", row.GetProperty("publisherCustomizationPrefix").GetString());
        Assert.Equal("Complete", row.GetProperty("resolution").GetString());
        Assert.True(row.GetProperty("isCustomizable").GetBoolean());
        Assert.Equal(1, inventory.GetProperty("workflowDefinitionsToDisable").GetInt32());
        Assert.Single(inventory.GetProperty("candidates").EnumerateArray());
        Assert.Equal(1, fixture.Service.Reads["solution"]);
        Assert.Equal(1, fixture.Service.Reads["publisher"]);
        Assert.Equal(1, fixture.Returned);
        Assert.Empty(fixture.Failures);
        Assert.Equal(0, fixture.Service.Writes);
        Assert.DoesNotContain("synthetic", fixture.Output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>來源顯示值相同也不能覆寫正式 ID；反序回傳來源並精確配對，防止依位置或名稱選列。</summary>
    [Fact]
    public void Run_SameNamesDifferentIds_ResolvesEachExactOrigin()
    {
        var service = new OriginService();
        var workflow = Clone(service.Workflow, PageId(2));
        var solution = Clone(service.Solution, PageId(3));
        var publisher = Clone(service.Publisher, PageId(4));
        workflow["solutionid"] = solution.Id;
        solution["publisherid"] = new EntityReference("publisher", publisher.Id) { Name = "Untrusted lookup label" };
        service.Page = query =>
        {
            var page = service.DefaultPage(query);
            if (query.EntityName == "workflow") page.Entities.Add(workflow);
            if (query.EntityName == "solution") page.Entities.Insert(0, solution);
            if (query.EntityName == "publisher") page.Entities.Insert(0, publisher);
            return page;
        };
        var result = Read(service);
        using var json = Serialize(result);
        var rows = json.RootElement.GetProperty("workflowIdentities").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        AssertOrigin(rows[0], service.Solution.Id, service.Publisher.Id);
        AssertOrigin(rows[1], solution.Id, publisher.Id);
        Assert.All(rows, row => Assert.Equal("Complete", row.GetProperty("resolution").GetString()));
        Assert.Equal(1, service.Reads["solution"]);
        Assert.Equal(1, service.Reads["publisher"]);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>混合缺來源與共用來源時，只查一次完整來源；每個 workflow 保留自己的正式 ID，false 管理值不可變成未知。</summary>
    [Fact]
    public void Run_MixedMissingAndSharedOrigins_PreservesEachWorkflowAndRawFalse()
    {
        var service = new OriginService();
        var missing = Clone(service.Workflow, PageId(10));
        missing.Attributes.Remove("solutionid");
        var shared = Clone(service.Workflow, PageId(11));
        service.Solution["ismanaged"] = false;
        service.Page = query =>
        {
            var page = service.DefaultPage(query);
            if (query.EntityName == "workflow") { page.Entities.Insert(0, missing); page.Entities.Add(shared); }
            return page;
        };
        var result = Read(service);
        using var json = Serialize(result);
        var rows = json.RootElement.GetProperty("workflowIdentities").EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal("SolutionIdMissing", rows[0].GetProperty("resolution").GetString());
        Assert.Equal(missing.Id, rows[0].GetProperty("workflowId").GetGuid());
        AssertOrigin(rows[1], service.Solution.Id, service.Publisher.Id);
        AssertOrigin(rows[2], service.Solution.Id, service.Publisher.Id);
        Assert.Equal(service.Workflow.Id, rows[1].GetProperty("workflowId").GetGuid());
        Assert.Equal(shared.Id, rows[2].GetProperty("workflowId").GetGuid());
        Assert.False(rows[1].GetProperty("solutionIsManaged").GetBoolean());
        Assert.Equal(3, result.WorkflowDefinitionsToDisable);
        Assert.Equal(1, service.Reads["solution"]);
        Assert.Equal(1, service.Reads["publisher"]);
    }

    /// <summary>舊模式不要求 solutionid，也不查來源；無 workflow 時同樣不得為空清冊啟動額外查詢。</summary>
    /// <param name="metadata">是否明確啟用管理身分模式。</param>
    /// <param name="empty">是否回傳空 workflow 集合。</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Run_DefaultOrEmptyWorkflow_DoesNotQueryOrigin(bool metadata, bool empty)
    {
        var service = new OriginService();
        var workflowColumns = new[] { "statecode", "statuscode", "workflowid" };
        service.Page = query =>
        {
            if (query.EntityName == "workflow" && !metadata)
                Assert.Equal(workflowColumns, query.ColumnSet.Columns.Order(StringComparer.Ordinal));
            return query.EntityName == "workflow" && empty
                ? new EntityCollection { EntityName = "workflow" } : service.DefaultPage(query);
        };
        var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: metadata);
        Assert.Empty(result.WorkflowIdentities);
        Assert.False(service.Reads.ContainsKey("solution"));
        Assert.False(service.Reads.ContainsKey("publisher"));
        Assert.Equal(4, service.Reads.Values.Sum());
    }

    /// <summary>缺失與未知保留原始 null，不借另一列補值，也不能靠 managed／customizable 自動保留流程。</summary>
    /// <param name="missing">移除或清空的來源階段。</param>
    /// <param name="resolution">從公開清冊要求的明確缺失階段。</param>
    [Theory]
    [InlineData("workflow-id", "SolutionIdMissing")]
    [InlineData("workflow-null", "SolutionIdMissing")]
    [InlineData("solution-row", "SolutionMissing")]
    [InlineData("publisher-lookup", "PublisherLookupMissing")]
    [InlineData("publisher-null", "PublisherLookupMissing")]
    [InlineData("publisher-row", "PublisherMissing")]
    [InlineData("solution-name", "MetadataIncomplete")]
    [InlineData("solution-managed", "MetadataIncomplete")]
    [InlineData("publisher-name", "MetadataIncomplete")]
    [InlineData("publisher-prefix", "MetadataIncomplete")]
    [InlineData("publisher-blank-prefix", "MetadataIncomplete")]
    public void Cli_MissingOrigin_IsExplicitWithoutInventingClassification(string missing, string resolution)
    {
        using var fixture = new OriginFixture();
        var service = fixture.Service;
        if (missing == "workflow-id") service.Workflow.Attributes.Remove("solutionid");
        if (missing == "workflow-null") service.Workflow["solutionid"] = null;
        if (missing == "publisher-lookup") service.Solution.Attributes.Remove("publisherid");
        if (missing == "publisher-null") service.Solution["publisherid"] = null;
        if (missing == "solution-name") service.Solution.Attributes.Remove("uniquename");
        if (missing == "solution-managed") service.Solution.Attributes.Remove("ismanaged");
        if (missing == "publisher-name") service.Publisher["uniquename"] = null;
        if (missing == "publisher-prefix") service.Publisher.Attributes.Remove("customizationprefix");
        if (missing == "publisher-blank-prefix") service.Publisher["customizationprefix"] = "";
        service.Page = query =>
        {
            var page = service.DefaultPage(query);
            if (missing == "solution-row" && query.EntityName == "solution"
                || missing == "publisher-row" && query.EntityName == "publisher")
                page.Entities.Clear();
            return page;
        };
        Assert.Equal(0, fixture.Run());
        using var json = fixture.Inventory();
        var inventory = json.RootElement.GetProperty("inventory");
        var row = Assert.Single(inventory.GetProperty("workflowIdentities").EnumerateArray());
        Assert.True(row.TryGetProperty("resolution", out var actual), "Workflow origin Resolution was not published.");
        Assert.Equal(resolution, actual.GetString());
        if (missing.StartsWith("workflow", StringComparison.Ordinal))
        {
            Assert.Equal(JsonValueKind.Null, row.GetProperty("solutionId").ValueKind);
            Assert.False(service.Reads.ContainsKey("solution"));
            Assert.False(service.Reads.ContainsKey("publisher"));
        }
        else Assert.Equal(service.Solution.Id, row.GetProperty("solutionId").GetGuid());
        if (missing is "publisher-lookup" or "publisher-null" or "solution-row")
            Assert.Equal(JsonValueKind.Null, row.GetProperty("publisherId").ValueKind);
        if (missing == "publisher-row")
        {
            Assert.Equal(service.Publisher.Id, row.GetProperty("publisherId").GetGuid());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("publisherUniqueName").ValueKind);
        }
        if (missing == "solution-managed")
            Assert.Equal(JsonValueKind.Null, row.GetProperty("solutionIsManaged").ValueKind);
        if (missing == "publisher-prefix")
            Assert.Equal(JsonValueKind.Null, row.GetProperty("publisherCustomizationPrefix").ValueKind);
        Assert.Equal(1, inventory.GetProperty("workflowDefinitionsToDisable").GetInt32());
        Assert.Equal(0, service.Writes);
    }

    /// <summary>不允許空 Guid、字串或 EntityReference 冒充 Uniqueidentifier，也不允許錯 publisher target／alternate key。</summary>
    /// <param name="table">故障的管理來源。</param>
    /// <param name="fault">由本測試明確生成的型別、身分、欄位或集合違約。</param>
    [Theory]
    [InlineData("workflow", "guid-empty")]
    [InlineData("workflow", "guid-string")]
    [InlineData("workflow", "guid-reference")]
    [InlineData("workflow", "extra-column")]
    [InlineData("solution", "lookup-guid")]
    [InlineData("solution", "lookup-target")]
    [InlineData("solution", "lookup-empty")]
    [InlineData("solution", "lookup-key")]
    [InlineData("solution", "empty-id")]
    [InlineData("publisher", "empty-id")]
    [InlineData("solution", "primary-mismatch")]
    [InlineData("publisher", "primary-mismatch")]
    [InlineData("solution", "primary-type")]
    [InlineData("publisher", "primary-type")]
    [InlineData("solution", "duplicate")]
    [InlineData("publisher", "duplicate")]
    [InlineData("solution", "wrong-row")]
    [InlineData("publisher", "wrong-row")]
    [InlineData("solution", "wrong-collection")]
    [InlineData("publisher", "wrong-collection")]
    [InlineData("solution", "extra-column")]
    [InlineData("publisher", "extra-column")]
    [InlineData("solution", "text-type")]
    [InlineData("publisher", "text-type")]
    [InlineData("solution", "text-long")]
    [InlineData("publisher", "text-long")]
    [InlineData("publisher", "prefix-type")]
    [InlineData("publisher", "prefix-long")]
    [InlineData("solution", "managed-type")]
    [InlineData("solution", "row-key")]
    [InlineData("publisher", "row-key")]
    public void Cli_InvalidOrigin_RejectsWholeCandidateAndReturnsLease(string table, string fault)
    {
        using var fixture = new OriginFixture();
        fixture.Service.Page = query =>
        {
            var page = fixture.Service.DefaultPage(query);
            if (query.EntityName != table) return page;
            var row = Assert.Single(page.Entities);
            switch (fault)
            {
                case "guid-empty": row["solutionid"] = Guid.Empty; break;
                case "guid-string": row["solutionid"] = fixture.Service.Solution.Id.ToString("D"); break;
                case "guid-reference": row["solutionid"] = new EntityReference("solution", fixture.Service.Solution.Id); break;
                case "lookup-guid": row["publisherid"] = fixture.Service.Publisher.Id; break;
                case "lookup-target": row["publisherid"] = new EntityReference("contact", fixture.Service.Publisher.Id); break;
                case "lookup-empty": row["publisherid"] = new EntityReference("publisher", Guid.Empty); break;
                case "lookup-key":
                    row.GetAttributeValue<EntityReference>("publisherid").KeyAttributes.Add("uniquename", "synthetic");
                    break;
                case "empty-id": row.Id = Guid.Empty; break;
                case "primary-mismatch": row[table + "id"] = PageId(9); break;
                case "primary-type": row[table + "id"] = "synthetic"; break;
                case "duplicate": page.Entities.Add(row); break;
                case "wrong-row": row.LogicalName = "contact"; break;
                case "wrong-collection": page.EntityName = "contact"; break;
                case "extra-column": row["name"] = "Forbidden display value"; break;
                case "text-type": row["uniquename"] = 42; break;
                case "text-long": row["uniquename"] = new string('x', 257); break;
                case "prefix-type": row["customizationprefix"] = 42; break;
                case "prefix-long": row["customizationprefix"] = new string('x', 257); break;
                case "managed-type": row["ismanaged"] = new OptionSetValue(1); break;
                case "row-key": row.KeyAttributes.Add("uniquename", "synthetic"); break;
                default: throw new InvalidOperationException("Unknown synthetic fault.");
            }
            return page;
        };
        Assert.Equal(1, fixture.Run());
        Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
        Assert.Empty(fixture.Files());
        Assert.Equal(1, fixture.Returned);
        Assert.Equal(0, fixture.Service.Writes);
        Assert.DoesNotContain("Forbidden display value", fixture.Output, StringComparison.Ordinal);
    }

    /// <summary>來源依實際正式 ID 可位於第二頁；不得只查第一頁、逐 workflow 查詢或按名稱提前停止。</summary>
    /// <param name="table">把對應列放到最後一頁的來源表。</param>
    [Theory]
    [InlineData("solution")]
    [InlineData("publisher")]
    public void Run_OriginOnSecondPage_ReadsCompleteSourceOnce(string table)
    {
        var service = new OriginService();
        service.Page = query =>
        {
            if (query.EntityName != table) return service.DefaultPage(query);
            if (query.PageInfo.PageNumber == 2) Assert.Equal("origin-1", query.PageInfo.PagingCookie);
            var template = table == "solution" ? service.Solution : service.Publisher;
            return new EntityCollection(query.PageInfo.PageNumber == 1
                ? Enumerable.Range(1, 5000).Select(index => Clone(template, PageId(index))).ToList()
                : [template])
            {
                EntityName = table, MoreRecords = query.PageInfo.PageNumber == 1,
                PagingCookie = query.PageInfo.PageNumber == 1 ? "origin-1" : null
            };
        };
        var result = Read(service);
        using var json = Serialize(result);
        AssertOrigin(Assert.Single(json.RootElement.GetProperty("workflowIdentities").EnumerateArray()),
            service.Solution.Id, service.Publisher.Id);
        Assert.Equal(2, service.Reads[table]);
        Assert.Equal(1, service.Reads[table == "solution" ? "publisher" : "solution"]);
    }

    /// <summary>完整十頁五萬列是合法邊界；第十頁有續頁則拒絕整份候選且不讀第十一頁。</summary>
    /// <param name="table">獨立套用頁數與列數預算的來源。</param>
    /// <param name="overflow">第十頁是否仍宣告未完成。</param>
    [Theory]
    [InlineData("solution", false)]
    [InlineData("publisher", false)]
    [InlineData("solution", true)]
    [InlineData("publisher", true)]
    public void Run_OriginBudget_FiftyThousandAcceptedAndEleventhPageRefused(string table, bool overflow)
    {
        var service = new OriginService();
        service.Page = query =>
        {
            if (query.EntityName != table) return service.DefaultPage(query);
            Assert.InRange(query.PageInfo.PageNumber, 1, 10);
            var template = table == "solution" ? service.Solution : service.Publisher;
            return new EntityCollection(Enumerable.Range((query.PageInfo.PageNumber - 1) * 5000 + 1, 5000)
                .Select(index => Clone(template, index == 1 ? template.Id : PageId(index))).ToList())
            {
                EntityName = table, MoreRecords = overflow || query.PageInfo.PageNumber < 10,
                PagingCookie = "origin-" + query.PageInfo.PageNumber.ToString(CultureInfo.InvariantCulture)
            };
        };
        if (overflow) Assert.Throws<InvalidDataException>(() => Read(service));
        else
        {
            using var json = Serialize(Read(service));
            var row = Assert.Single(json.RootElement.GetProperty("workflowIdentities").EnumerateArray());
            Assert.Equal("Complete", row.GetProperty("resolution").GetString());
        }
        Assert.Equal(10, service.Reads[table]);
        Assert.Equal(0, service.Writes);
    }

    /// <summary>來源續頁缺損、循環、重複 ID、空終頁及單頁超量均不可發布成功或已讀部分結果。</summary>
    /// <param name="table">在來源層注入分頁失敗。</param>
    /// <param name="fault">獨立頁面故障種類。</param>
    [Theory]
    [InlineData("solution", "missing-cookie")]
    [InlineData("publisher", "missing-cookie")]
    [InlineData("solution", "repeat-cookie")]
    [InlineData("publisher", "repeat-cookie")]
    [InlineData("solution", "empty-terminal")]
    [InlineData("publisher", "empty-terminal")]
    [InlineData("solution", "duplicate-next")]
    [InlineData("publisher", "duplicate-next")]
    [InlineData("solution", "oversize")]
    [InlineData("publisher", "oversize")]
    [InlineData("solution", "null-page")]
    [InlineData("publisher", "null-page")]
    [InlineData("solution", "long-cookie")]
    [InlineData("publisher", "long-cookie")]
    public void Cli_IncompleteOriginPages_DoesNotPublish(string table, string fault)
    {
        using var fixture = new OriginFixture();
        fixture.Service.Page = query =>
        {
            var page = fixture.Service.DefaultPage(query);
            if (query.EntityName != table) return page;
            if (fault == "null-page") return null!;
            var row = Assert.Single(page.Entities);
            if (fault != "duplicate-next") { row.Id = PageId(query.PageInfo.PageNumber); row[table + "id"] = row.Id; }
            page.MoreRecords = query.PageInfo.PageNumber == 1;
            page.PagingCookie = "origin-1";
            if (fault == "missing-cookie") page.PagingCookie = null;
            if (fault == "empty-terminal" && query.PageInfo.PageNumber == 2) page.Entities.Clear();
            if (fault == "oversize")
                for (var index = 0; index < 5000; index++) page.Entities.Add(row);
            if (fault == "long-cookie") page.PagingCookie = new string('x', 65537);
            return page;
        };
        Assert.Equal(1, fixture.Run());
        Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
        Assert.Empty(fixture.Files());
        Assert.Equal(1, fixture.Returned);
        Assert.InRange(fixture.Service.Reads[table], 1, 2);
        Assert.Equal(0, fixture.Service.Writes);
    }

    /// <summary>來源同步讀取返回後即遵守 caller 取消；真正失敗保留同一 Exception，兩者都不發布且歸還租約。</summary>
    /// <param name="table">最後兩個來源的故障位置。</param>
    /// <param name="cancel">true 取消 caller，false 注入真正 SDK 故障。</param>
    [Theory]
    [InlineData("solution", true)]
    [InlineData("publisher", true)]
    [InlineData("solution", false)]
    [InlineData("publisher", false)]
    public void Cli_OriginCancellationAndFault_ReleaseWithoutPublication(string table, bool cancel)
    {
        using var fixture = new OriginFixture();
        using var cancellation = new CancellationTokenSource();
        var failure = new IOException("Synthetic origin failure.");
        fixture.Service.Page = query =>
        {
            if (query.EntityName == table)
            {
                if (cancel) cancellation.Cancel();
                else throw failure;
            }
            return fixture.Service.DefaultPage(query);
        };
        Assert.Equal(cancel ? 130 : 1, fixture.Run(token: cancellation.Token));
        if (cancel) Assert.Empty(fixture.Failures);
        else Assert.Same(failure, Assert.Single(fixture.Failures));
        Assert.Empty(fixture.Files());
        Assert.Equal(1, fixture.Returned);
        Assert.Equal(0, fixture.Service.Writes);
        if (table == "solution") Assert.False(fixture.Service.Reads.ContainsKey("publisher"));
    }

    /// <summary>在後續來源讀取及發布後修改 SDK 圖，都不能污染已驗證候選；回傳清冊拒絕 caller mutation。</summary>
    [Fact]
    public void Run_SourceMutation_CannotChangePublishedOriginOrCollections()
    {
        var service = new OriginService();
        service.Page = query =>
        {
            if (query.EntityName == "solution") service.Workflow["solutionid"] = PageId(90);
            if (query.EntityName == "publisher")
            {
                service.Solution["uniquename"] = "changed_during_read";
                service.Solution.GetAttributeValue<EntityReference>("publisherid").Id = PageId(91);
            }
            return service.DefaultPage(query);
        };
        var result = Read(service);
        service.Publisher["uniquename"] = "changed_after_return";
        service.Publisher["customizationprefix"] = "changed";
        using var json = Serialize(result);
        var row = Assert.Single(json.RootElement.GetProperty("workflowIdentities").EnumerateArray());
        AssertOrigin(row, service.Solution.Id, service.Publisher.Id);
        Assert.Equal("synthetic_solution", row.GetProperty("solutionUniqueName").GetString());
        Assert.Equal("synthetic_publisher", row.GetProperty("publisherUniqueName").GetString());
        Assert.Equal("syn", row.GetProperty("publisherCustomizationPrefix").GetString());
        Assert.Throws<NotSupportedException>(() => ((IList<QuiesceWorkflowIdentity>)result.WorkflowIdentities).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<QuiesceCandidate>)result.Candidates).Clear());
    }

    /// <summary>同正式 ID 的三十二個命令同時讀不同來源純值，防止靜態 dictionary 或上次結果跨命令外洩。</summary>
    [Fact]
    public async Task Run_ConcurrentOrigins_StayOperationLocal()
    {
        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            var service = new OriginService();
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            service.Solution["uniquename"] = "solution_" + suffix;
            service.Publisher["uniquename"] = "publisher_" + suffix;
            using var json = Serialize(Read(service));
            var row = Assert.Single(json.RootElement.GetProperty("workflowIdentities").EnumerateArray());
            AssertOrigin(row, service.Solution.Id, service.Publisher.Id);
            Assert.Equal("solution_" + suffix, row.GetProperty("solutionUniqueName").GetString());
            Assert.Equal("publisher_" + suffix, row.GetProperty("publisherUniqueName").GetString());
        })));
    }

    /// <summary>來源失敗後以同一 service 重試必須重新完整讀取；已發布純值保持原值，不能共用可變候選。</summary>
    [Fact]
    public void Run_FailedOriginThenRetry_DoesNotRetainOrReusePartialCandidate()
    {
        var service = new OriginService();
        var failure = new IOException("Synthetic publisher failure.");
        service.Page = query => query.EntityName == "publisher" ? throw failure : service.DefaultPage(query);
        Assert.Same(failure, Assert.Throws<IOException>(() => Read(service)));
        service.Page = null;
        service.Solution["uniquename"] = "after_retry";
        using var first = Serialize(Read(service));
        service.Solution["uniquename"] = "next_command";
        using var second = Serialize(Read(service));
        Assert.Equal("after_retry", Assert.Single(first.RootElement.GetProperty("workflowIdentities").EnumerateArray())
            .GetProperty("solutionUniqueName").GetString());
        Assert.Equal("next_command", Assert.Single(second.RootElement.GetProperty("workflowIdentities").EnumerateArray())
            .GetProperty("solutionUniqueName").GetString());
        Assert.Equal(3, service.Reads["solution"]);
        Assert.Equal(3, service.Reads["publisher"]);
    }

    /// <summary>呼叫端持有完整清冊時，SDK service／列／lookup 仍須可回收，證明純值結果沒有保留來源圖。</summary>
    [Fact]
    public void Run_ResultDoesNotRetainOriginSdkGraph()
    {
        var (result, sources) = ReadWithWeakSources();
        for (var attempt = 0; attempt < 3; attempt++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.All(sources, source => Assert.False(source.IsAlive));
        using var json = Serialize(result);
        Assert.Equal("Complete", Assert.Single(json.RootElement.GetProperty("workflowIdentities").EnumerateArray())
            .GetProperty("resolution").GetString());
        GC.KeepAlive(result);
    }

    /// <summary>完整 metadata 不能補 policy；apply 沒有核定清冊時，在 SDK 連線之前拒絕。</summary>
    [Fact]
    public void Cli_CompleteMetadata_CannotAuthorizeApply()
    {
        using var fixture = new OriginFixture();
        Assert.Equal(2, fixture.Run(apply: true));
        Assert.Equal(0, fixture.Returned);
        Assert.Empty(fixture.Service.Reads);
        Assert.Empty(fixture.Files());
        Assert.Equal(0, fixture.Service.Writes);
    }

    /// <summary>禁止內聯以縮短來源 stack lifetime；僅交回純值結果與不持有來源的弱參照。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ClonedOrganizationQuiesceResult Result, WeakReference[] Sources) ReadWithWeakSources()
    {
        var service = new OriginService();
        return (Read(service), [new WeakReference(service), new WeakReference(service.Workflow),
            new WeakReference(service.Solution), new WeakReference(service.Publisher),
            new WeakReference(service.Solution.GetAttributeValue<EntityReference>("publisherid"))]);
    }

    /// <summary>以真 reader 的明確模式取得純值結果；service 由測試持有，無連線或認證解析。</summary>
    private static ClonedOrganizationQuiesceResult Read(OriginService service) =>
        ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true);

    /// <summary>僅由 repository catalog 取得核定測試組織描述，從不建立真正 connector。</summary>
    private static OrganizationDefinition Target() => OrganizationCatalog.LoadDefault().GetTest("goodshepherd20");

    /// <summary>以公開 JSON 欄位形狀驗證新增屬性；即使舊 record 沒有來源屬性仍能編譯並產生真正 RED。</summary>
    private static JsonDocument Serialize(ClonedOrganizationQuiesceResult result) =>
        JsonSerializer.SerializeToDocument(result, JsonSerializerOptions.Web);

    /// <summary>正式來源身分的獨立決定性斷言，缺 sidecar 用斷言失敗而非編譯或 JSON 存取例外表示。</summary>
    private static void AssertOrigin(JsonElement row, Guid solutionId, Guid publisherId)
    {
        Assert.True(row.TryGetProperty("solutionId", out var solution), "Workflow solution identity was not published.");
        Assert.Equal(solutionId, solution.GetGuid());
        Assert.True(row.TryGetProperty("publisherId", out var publisher), "Workflow publisher identity was not published.");
        Assert.Equal(publisherId, publisher.GetGuid());
    }

    /// <summary>合成來源複本只替換正式主鍵，故障測試自行控制管理欄位，與產品的 ID 關聯實作無共用。</summary>
    private static Entity Clone(Entity template, Guid id)
    {
        var row = new Entity(template.LogicalName, id);
        row.Attributes.AddRange(template.Attributes);
        row[template.LogicalName + "id"] = id;
        return row;
    }

    /// <summary>固定頁列索引生成不重複合成 ID，避免以名稱或臨時位置充當產品正式資料身分。</summary>
    private static Guid PageId(int index) => new(index, 0, 0, new byte[8]);

    /// <summary>每案唯一的 CLI 租約與 artifacts owner；不保存真連線或秘密，Dispose 只清本案隨機暫存根。</summary>
    private sealed class OriginFixture : IOrganizationConnector, IDisposable
    {
        /// <summary>本案獨占暫存根，不寫 repo、其他命令的輸出或正式 artifacts。</summary>
        private readonly string root = Path.Combine(Path.GetTempPath(), "fs-workflow-origin-" + Guid.NewGuid().ToString("N"));
        /// <summary>本案摘要 writer，由 fixture Dispose；測試只核對沒有資料值流入診斷。</summary>
        private readonly StringWriter output = new(CultureInfo.InvariantCulture);
        /// <summary>借給真命令的純記憶體 SDK，所有管理來源由本案唯一持有。</summary>
        internal OriginService Service { get; } = new();
        /// <summary>原始故障只活到本案結束，正常 caller 取消不能進此集合。</summary>
        internal List<Exception> Failures { get; } = new();
        /// <summary>租約歸還次數，證明全部 CLI 結束路徑清理。</summary>
        internal int Returned { get; private set; }
        /// <summary>摘要文字只供無資料外洩的斷言，不打印管理值。</summary>
        internal string Output => output.ToString();
        /// <summary>經真公開 CLI 與注入 service 執行；Secrets reader 必須永遠不被呼叫。</summary>
        internal int Run(bool apply = false, CancellationToken token = default) =>
            DeploymentCli.RunWithCatalog(apply ? ["quiesce", "--org", "goodshepherd20", "--apply"]
                    : ["quiesce", "--org", "goodshepherd20"],
                OrganizationCatalog.LoadDefault(), this, output, Failures.Add, null, root,
                secretReader: _ => throw new InvalidOperationException("Secrets must not be read."),
                cancellationToken: token);
        /// <summary>只借出本案的 service；CLI 唯一擁有 lease 並在成功、失敗或取消時歸還。</summary>
        public OrganizationLease Connect(string organization)
        {
            Assert.Equal("goodshepherd20", organization);
            return new OrganizationLease(Service, () => Returned++);
        }
        /// <summary>枚舉本案全部 artifacts，失敗連暫存／半寫檔案也不得留下；成功時唯一檔案就是清冊。</summary>
        internal string[] Files() => Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [];
        /// <summary>JSON 文件交測試 using 擁有，讀取後立即關閉檔案串流。</summary>
        internal JsonDocument Inventory() => JsonDocument.Parse(File.ReadAllText(Assert.Single(Files())));
        /// <summary>命令同步返回後先關 writer，再刪本案暫存目錄，沒有未完成 SDK／背景工作。</summary>
        public void Dispose()
        {
            output.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>獨立管理來源 SDK 替身；只借用記憶體列，不建立連線、憑證、task 或其他需 Dispose 資源。</summary>
    private sealed class OriginService : IOrganizationService
    {
        /// <summary>合成流程，solutionid 明確是 Guid 而非 EntityReference；不提供名稱或定義正文。</summary>
        internal Entity Workflow { get; } = new("workflow", new Guid("51000000-0000-0000-0000-000000000001"));
        /// <summary>合成 solution 正式主鍵與 publisher lookup，不以 uniquename 作關聯鍵。</summary>
        internal Entity Solution { get; } = new("solution", new Guid("61000000-0000-0000-0000-000000000001"));
        /// <summary>合成 publisher 正式主鍵與有界管理文字，缺欄位由各案明確注入。</summary>
        internal Entity Publisher { get; } = new("publisher", new Guid("71000000-0000-0000-0000-000000000001"));
        /// <summary>每來源實際讀取次數，保護零額外查詢、非 N+1 與頁數邊界。</summary>
        internal Dictionary<string, int> Reads { get; } = new(StringComparer.Ordinal);
        /// <summary>副作用計數，所有離線 dry-run 都必須保持零。</summary>
        internal int Writes { get; private set; }
        /// <summary>同步頁面故障注入；不保留跨案 callback 或延後工作。</summary>
        internal Func<QueryExpression, EntityCollection>? Page { get; set; }
        /// <summary>初始化可人工核對的來源鏈與純值，managed／customizable 都 true 也不能自動推導 keep。</summary>
        internal OriginService()
        {
            Workflow["workflowid"] = Workflow.Id;
            Workflow["statecode"] = new OptionSetValue(1);
            Workflow["statuscode"] = new OptionSetValue(2);
            Workflow["ismanaged"] = true;
            Workflow["iscustomizable"] = new BooleanManagedProperty(true);
            Workflow["type"] = new OptionSetValue(1);
            Workflow["solutionid"] = Solution.Id;
            Solution["solutionid"] = Solution.Id;
            Solution["uniquename"] = "synthetic_solution";
            Solution["publisherid"] = new EntityReference("publisher", Publisher.Id);
            Solution["ismanaged"] = true;
            Publisher["publisherid"] = Publisher.Id;
            Publisher["uniquename"] = "synthetic_publisher";
            Publisher["customizationprefix"] = "syn";
        }
        /// <summary>獨立欄位 oracle 禁止敏感讀取，並核對正式主鍵排序、每頁預算與無關聯／無額外條件的完整來源讀取。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            var allowed = expression.EntityName switch
            {
                "asyncoperation" => new[] { "asyncoperationid", "statecode", "statuscode" },
                "workflow" => ["workflowid", "statecode", "statuscode", "ismanaged", "iscustomizable",
                    "category", "mode", "type", "ondemand", "scope", "solutionid"],
                "sdkmessageprocessingstep" => ["sdkmessageprocessingstepid", "statecode", "statuscode", "eventhandler"],
                "mailbox" => ["mailboxid", "statecode", "statuscode",
                    "incomingemaildeliverymethod", "outgoingemaildeliverymethod", "actdeliverymethod"],
                "solution" => ["solutionid", "uniquename", "publisherid", "ismanaged"],
                "publisher" => ["publisherid", "uniquename", "customizationprefix"],
                _ => throw new InvalidOperationException("Unexpected metadata source.")
            };
            Assert.False(expression.ColumnSet.AllColumns);
            Assert.All(expression.ColumnSet.Columns, column => Assert.Contains(column, allowed));
            Assert.Empty(expression.LinkEntities);
            Assert.Equal(5000, expression.PageInfo.Count);
            Assert.InRange(expression.PageInfo.PageNumber, 1, 10);
            var order = Assert.Single(expression.Orders);
            Assert.Equal(expression.EntityName + "id", order.AttributeName);
            Assert.Equal(OrderType.Ascending, order.OrderType);
            if (expression.EntityName is "solution" or "publisher")
            {
                Assert.Equal(allowed.Order(StringComparer.Ordinal), expression.ColumnSet.Columns.Order(StringComparer.Ordinal));
                Assert.Empty(expression.Criteria.Conditions);
                Assert.Empty(expression.Criteria.Filters);
                Assert.Equal(LogicalOperator.And, expression.Criteria.FilterOperator);
            }
            Reads[expression.EntityName] = Reads.GetValueOrDefault(expression.EntityName) + 1;
            return Page is null ? DefaultPage(expression) : Page(expression);
        }
        /// <summary>按實際 ColumnSet 投影來源；保留 SDK 物件參照供 mutation 注入，真 reader 必須自行 detach。</summary>
        internal EntityCollection DefaultPage(QueryExpression query)
        {
            var source = query.EntityName switch { "workflow" => Workflow, "solution" => Solution, "publisher" => Publisher, _ => null };
            var page = new EntityCollection { EntityName = query.EntityName };
            if (source is not null)
            {
                var row = new Entity(source.LogicalName, source.Id);
                foreach (var column in query.ColumnSet.Columns)
                    if (source.Attributes.TryGetValue(column, out var value)) row[column] = value;
                page.Entities.Add(row);
            }
            return page;
        }
        /// <summary>只允許正式組織核身訊息；沒有認證、租戶資料或寫入執行入口。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Assert.IsType<RetrieveCurrentOrganizationRequest>(request);
            return new RetrieveCurrentOrganizationResponse
            {
                Results = { ["Detail"] = new OrganizationDetail
                {
                    OrganizationId = new Guid("41000000-0000-0000-0000-000000000001"),
                    UniqueName = "goodshepherd20", OrganizationVersion = "9.1.0.0"
                } }
            };
        }
        /// <summary>明確拒絕新增並計數，防止 metadata 模式產生副作用。</summary>
        public Guid Create(Entity entity) { Writes++; throw new NotSupportedException(); }
        /// <summary>明確拒絕更新，純值來源完整不構成停用授權。</summary>
        public void Update(Entity entity) { Writes++; throw new NotSupportedException(); }
        /// <summary>禁止用刪資料掩蓋來源未知或重複。</summary>
        public void Delete(string entityName, Guid id) { Writes++; throw new NotSupportedException(); }
        /// <summary>單筆來源讀取會造成 N+1，故由替身明確拒絕。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>唯讀來源不得建立任何關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        { Writes++; throw new NotSupportedException(); }
        /// <summary>唯讀來源不得解除任何關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        { Writes++; throw new NotSupportedException(); }
    }
}

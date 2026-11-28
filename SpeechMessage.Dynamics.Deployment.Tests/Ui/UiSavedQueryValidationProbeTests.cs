/*
檔案整體說明
目的與定位：FS savedquery 驗證工具的純離線契約，使用正式 SchemaFiles／ViewDefinitions
及合成 SDK 委派，不讀 User Secrets、不連 D365、不執行任何實機測試或業務查詢。
主要流程：攔截每一個 ValidateSavedQueryRequest 的精確型別、參數數量、QueryType 與固定
FetchXML，對照正式產生器及同表主鍵／名稱最小 Fetch；反覆呼叫必須使用新 request。
故障注入：SDK fault、錯回應、未知例外型別、內部逾時、caller 入場前／返回後取消、
取消同時真故障、reporter 故障及越界 XML；斷言原 Exception 原件上報且失敗不能變成功。
保密與隔離：訊息、TraceText、GUID、XML 與秘密哨兵不得出現在輸出，只允許固定分類及 hash；
每案 writer／取消來源由 using 確定清理，委派與候選只活在該案，沒有快取或背景工作。
owner 驗證：只讀本工作樹 Live 原始碼的語法樹，檢查固定白名單、核身順序、metadata、
三分鐘上限、using 與 finally drain；這是離線結構斷言，不能冒充實機資源已排空。
驗收限制：成功只代表 Fetch 驗證回應形狀成立；不能證明 savedquery Create／發布成功。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>以合成回應守住唯一唯讀 SDK 能力、安全字串與同步取消契約；各案不共用可變狀態。</summary>
public sealed class UiSavedQueryValidationProbeTests
{
    /// <summary>獨立列出的正式生成 Fetch 預期；不由受測 Probe 回讀，以免自我驗證掩蓋範圍漂移。</summary>
    private const string GeneratedFetch = "<fetch><entity name=\"new_linechannel\"><attribute name=\"new_name\" /></entity></fetch>";

    /// <summary>官方最小 Fetch 形狀的固定同表對照；只驗證而不執行讀列。</summary>
    private const string MinimalFetch = "<fetch version=\"1.0\" output-format=\"xml-platform\" mapping=\"logical\" distinct=\"false\"><entity name=\"new_linechannel\"><attribute name=\"new_linechannelid\" /><attribute name=\"new_name\" /></entity></fetch>";

    /// <summary>兩種固定案例均只能送出兩參數的 ValidateSavedQuery；重跑使用新 request 且沒有寫入能力。</summary>
    /// <param name="minimal">true 驗主鍵／名稱對照，false 驗正式生成結果。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_FixedRequest_OnlyValidatesExactFetchAndQueryType(bool minimal)
    {
        using var output = new StringWriter();
        var generated = Generate();
        Assert.Equal(GeneratedFetch, generated.FetchXml);
        var reports = new List<Exception>();
        OrganizationRequest? previous = null;
        var calls = 0;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.True(UiSavedQueryValidationProbe.Run(generated, minimal, request =>
            {
                calls++;
                var typed = Assert.IsType<ValidateSavedQueryRequest>(request);
                Assert.NotSame(previous, request);
                previous = request;
                Assert.Equal("ValidateSavedQuery", request.RequestName);
                Assert.Equal(2, request.Parameters.Count);
                Assert.Equal("FetchXml,QueryType", string.Join(",", request.Parameters.Keys.OrderBy(key => key, StringComparer.Ordinal)));
                Assert.Equal(0, typed.QueryType);
                Assert.Equal(minimal ? MinimalFetch : GeneratedFetch, typed.FetchXml);
                Assert.Null(request.RequestId);
                return new ValidateSavedQueryResponse();
            }, output, reports.Add, default));
        }
        Assert.Equal(2, calls);
        Assert.Empty(reports);
        var text = output.ToString();
        Assert.Contains("result=OK fetchValidated=true persistenceProven=false", text, StringComparison.Ordinal);
        Assert.Contains("fetchSha256=" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(minimal ? MinimalFetch : GeneratedFetch))), text, StringComparison.Ordinal);
        Assert.DoesNotContain("<fetch", text, StringComparison.Ordinal);
        Assert.DoesNotContain("new_name", text, StringComparison.Ordinal);
        Assert.All(text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries), line => Assert.InRange(line.Length, 1, 1600));
    }

    /// <summary>SDK 拒絕只能上報原件一次；固定關鍵字與十六進位錯碼可見，任意本文／內層／trace 不外洩。</summary>
    [Fact]
    public void Run_SdkFault_ReportsOriginalAndOnlySafeClassification()
    {
        using var output = new StringWriter();
        var sentinel = "private-token-4df79d0d-348c-4c9e-b7e3-358678bbc453 <fetch secret='password'/>";
        var fault = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
        {
            ErrorCode = unchecked((int)0x80040216), Message = "Invalid Fetch query attribute " + sentinel,
            TraceText = "permission " + sentinel,
            InnerFault = new OrganizationServiceFault { Message = sentinel }
        }, sentinel);
        var reports = new List<Exception>();
        Assert.False(UiSavedQueryValidationProbe.Run(Generate(), false, _ => throw fault, output, reports.Add, default));
        Assert.Same(fault, Assert.Single(reports));
        var text = output.ToString();
        Assert.Contains("result=FAIL type=SdkFault code=0x80040216", text, StringComparison.Ordinal);
        Assert.Contains("fetch=True", text, StringComparison.Ordinal);
        Assert.Contains("permission=True", text, StringComparison.Ordinal);
        Assert.Contains("attribute=True", text, StringComparison.Ordinal);
        Assert.Contains("textSha256=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private-token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("4df79d0d", text, StringComparison.Ordinal);
        Assert.DoesNotContain("password", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<fetch", text, StringComparison.Ordinal);
        Assert.DoesNotContain("result=OK", text, StringComparison.Ordinal);
        Assert.InRange(text.Length, 1, 1600);
    }

    /// <summary>不受控的例外型別名稱與訊息不能直接輸出；內部取消和逾時仍屬失敗，不當成 caller 取消。</summary>
    /// <param name="kind">本案合成例外的固定代號。</param>
    /// <param name="expected">安全輸出允許的固定型別。</param>
    [Theory]
    [InlineData("unknown", "Other")]
    [InlineData("timeout", "Timeout")]
    [InlineData("cancel", "Cancellation")]
    [InlineData("invalid", "InvalidData")]
    public void Run_Exception_UsesFiniteTypeNames(string kind, string expected)
    {
        using var output = new StringWriter();
        Exception failure = kind switch
        {
            "unknown" => new PrivateTypeMustNotAppearException(),
            "timeout" => new TimeoutException("private-exception"),
            "cancel" => new OperationCanceledException("private-exception"),
            _ => new InvalidDataException("private-exception")
        };
        var reports = new List<Exception>();
        Assert.False(UiSavedQueryValidationProbe.Run(Generate(), true, _ => throw failure, output, reports.Add, default));
        Assert.Same(failure, Assert.Single(reports));
        Assert.Contains("result=FAIL type=" + expected, output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(PrivateTypeMustNotAppearException), output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>關鍵字掃描與 hash 只保留每段前四 KiB；不掃描無界字串或把超出上限的關鍵字誤報。</summary>
    [Fact]
    public void Run_LongFaultText_HasBoundedKeywordInspection()
    {
        using var output = new StringWriter();
        var fault = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault
        {
            ErrorCode = -1, Message = new string('x', 6000) + "permission private-tail"
        });
        Assert.False(UiSavedQueryValidationProbe.Run(Generate(), false, _ => throw fault, output, _ => { }, default));
        Assert.Contains("permission=False", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-tail", output.ToString(), StringComparison.Ordinal);
        Assert.InRange(output.ToString().Length, 1, 1600);
    }

    /// <summary>未知或空 SDK 回應即使沒有 fault 仍必須失敗，上報固定 InvalidDataException，不能假成功。</summary>
    /// <param name="empty">是否注入 null 回應。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_InvalidResponse_FailsClosed(bool empty)
    {
        using var output = new StringWriter();
        var reports = new List<Exception>();
        Assert.False(UiSavedQueryValidationProbe.Run(Generate(), false,
            _ => empty ? null! : new OrganizationResponse(), output, reports.Add, default));
        Assert.IsType<InvalidDataException>(Assert.Single(reports));
        Assert.DoesNotContain("result=OK", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>caller 在入場前、SDK 返回後或 SDK 拋取消時取消，都必須重新拋出，不通知、不輸出成功。</summary>
    /// <param name="phase">取消發生的固定位置。</param>
    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("throw")]
    public void Run_CallerCancellation_NeverBecomesSuccess(string phase)
    {
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        if (phase == "before") cancellation.Cancel();
        var calls = 0;
        var reports = 0;
        Assert.Throws<OperationCanceledException>(() => UiSavedQueryValidationProbe.Run(Generate(), false, _ =>
        {
            calls++;
            cancellation.Cancel();
            if (phase == "throw") throw new OperationCanceledException(cancellation.Token);
            return new ValidateSavedQueryResponse();
        }, output, _ => reports++, cancellation.Token));
        Assert.Equal(phase == "before" ? 0 : 1, calls);
        Assert.Equal(0, reports);
        Assert.Equal("", output.ToString());
    }

    /// <summary>取消同時收到真正 SDK fault 仍須保留原故障，不以已取消 token 掩蓋根因或回傳成功。</summary>
    [Fact]
    public void Run_FaultAlongsideCancellation_ReportsActualFailure()
    {
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        var fault = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = -1 });
        var reports = new List<Exception>();
        Assert.False(UiSavedQueryValidationProbe.Run(Generate(), false, _ =>
        {
            cancellation.Cancel();
            throw fault;
        }, output, reports.Add, cancellation.Token));
        Assert.Same(fault, Assert.Single(reports));
        Assert.DoesNotContain("result=OK", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>QueryType、表、欄位、filter 或 DTD 的任何漂移都必須在 SDK 委派前拒絕，不回顯非法值。</summary>
    /// <param name="kind">固定越界種類。</param>
    [Theory]
    [InlineData("type")]
    [InlineData("entity")]
    [InlineData("attribute")]
    [InlineData("filter")]
    [InlineData("dtd")]
    public void Run_UnapprovedGeneratedCandidate_IsRejectedBeforeSdk(string kind)
    {
        using var output = new StringWriter();
        var candidate = Generate();
        candidate = kind switch
        {
            "type" => candidate with { QueryType = 64 },
            "entity" => candidate with { FetchXml = GeneratedFetch.Replace("new_linechannel", "contact", StringComparison.Ordinal) },
            "attribute" => candidate with { FetchXml = GeneratedFetch.Replace("new_name", "private-secret", StringComparison.Ordinal) },
            "filter" => candidate with { FetchXml = GeneratedFetch.Replace("</entity>", "<filter /></entity>", StringComparison.Ordinal) },
            _ => candidate with { FetchXml = "<!DOCTYPE private-secret>" + GeneratedFetch }
        };
        var calls = 0;
        var reports = 0;
        var failure = Assert.Throws<ArgumentException>(() => UiSavedQueryValidationProbe.Run(candidate, true,
            _ => { calls++; return new ValidateSavedQueryResponse(); }, output, _ => reports++, default));
        Assert.Equal(0, calls);
        Assert.Equal(0, reports);
        Assert.DoesNotContain("private-secret", failure.Message, StringComparison.Ordinal);
        Assert.Equal("", output.ToString());
    }

    /// <summary>共用診斷失敗不能改寫原故障為成功，不能遞迴通知，也不能洩漏 reporter 訊息。</summary>
    [Fact]
    public void Run_ReporterThrows_PreservesFailedResult()
    {
        using var output = new StringWriter();
        var original = new TimeoutException("private-original");
        var reports = 0;
        Assert.False(UiSavedQueryValidationProbe.Run(Generate(), false, _ => throw original, output, exception =>
        {
            reports++;
            Assert.Same(original, exception);
            throw new IOException("private-reporter");
        }, default));
        Assert.Equal(1, reports);
        Assert.Contains("result=FAIL", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>離線語法斷言核對 owner 的固定身分、核身順序及 deterministic cleanup，不建立 owner 或連線。</summary>
    [Fact]
    public void LiveOwner_SourceContract_IsFixedGuardedReadOnlyAndDrained()
    {
        var root = DeploymentCli.FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "SpeechMessage.Dynamics.Deployment.Tests", "Organizations", "UiSavedQueryValidationLiveTests.cs"));
        var syntax = CSharpSyntaxTree.ParseText(source).GetRoot();
        var owner = Assert.Single(syntax.DescendantNodes().OfType<ClassDeclarationSyntax>());
        var attributes = owner.AttributeLists.ToString();
        Assert.Contains("\"Category\", \"Live3P\"", attributes, StringComparison.Ordinal);
        Assert.Contains("\"WP\", \"FSSavedQueryDiagnostics\"", attributes, StringComparison.Ordinal);
        Assert.Contains("const string organization = \"goodshepherd20\"", source, StringComparison.Ordinal);
        var ordered = new[] { "ProductionGuard.Authorize(", ".Connect(", "ProductionGuard.VerifyServer(", "new RetrieveEntityRequest", "SchemaFiles.LoadForOrganization(", "ViewDefinitions.Generate(", "UiSavedQueryValidationProbe.Run(" };
        var previous = -1;
        foreach (var token in ordered)
        {
            var position = source.IndexOf(token, StringComparison.Ordinal);
            Assert.True(position > previous, "Owner authorization/metadata/generation order changed.");
            previous = position;
        }
        Assert.Contains("using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3))", source, StringComparison.Ordinal);
        Assert.Contains("client.Timeout = TimeSpan.FromMinutes(3)", source, StringComparison.Ordinal);
        Assert.Contains("using var configurationOwner", source, StringComparison.Ordinal);
        Assert.Contains("using var lease", source, StringComparison.Ordinal);
        Assert.Contains("metadata.PrimaryIdAttribute != \"new_linechannelid\"", source, StringComparison.Ordinal);
        Assert.Contains("metadata.ObjectTypeCode is not > 0", source, StringComparison.Ordinal);
        Assert.Contains("sendLine: false", source, StringComparison.Ordinal);
        Assert.Contains(syntax.DescendantNodes().OfType<FinallyClauseSyntax>(), clause => clause.ToString().Contains("await diagnostics.DisposeAsync()", StringComparison.Ordinal));
        var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(call => call.Expression).OfType<MemberAccessExpressionSyntax>().ToArray();
        Assert.DoesNotContain(invocations, member => member.Name.Identifier.ValueText is "Update" or "Delete" or "Retrieve" or "RetrieveMultiple" or "Publish"
            || member.Name.Identifier.ValueText == "Create" && member.Expression.ToString() != "DeploymentConfiguration");
    }

    /// <summary>版面只讀同表公用 savedquery 的精確四欄；真 ID 僅驗身份，名稱／XML／值完全不輸出。</summary>
    [Fact]
    public void LayoutComparison_FixedReadScope_OutputsOnlyWhitelistedShape()
    {
        var row = LayoutRow();
        var calls = 0;
        var lines = UiSavedQueryValidationProbe.ReadLayoutComparison(Generate(), query =>
        {
            calls++;
            Assert.Equal("savedquery", query.EntityName);
            Assert.False(query.ColumnSet.AllColumns);
            Assert.Equal("layoutxml,querytype,returnedtypecode,savedqueryid", string.Join(",", query.ColumnSet.Columns.OrderBy(column => column, StringComparer.Ordinal)));
            Assert.Null(query.TopCount);
            Assert.Equal(1, query.PageInfo.PageNumber);
            Assert.Equal(16, query.PageInfo.Count);
            Assert.Null(query.PageInfo.PagingCookie);
            Assert.Empty(query.LinkEntities);
            Assert.Empty(query.Criteria.Filters);
            Assert.Equal(2, query.Criteria.Conditions.Count);
            var scope = Assert.Single(query.Criteria.Conditions.Where(condition => condition.AttributeName == "returnedtypecode"));
            Assert.Equal(Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, scope.Operator);
            Assert.Equal("new_linechannel", Assert.Single(scope.Values));
            var type = Assert.Single(query.Criteria.Conditions.Where(condition => condition.AttributeName == "querytype"));
            Assert.Equal(Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, type.Operator);
            Assert.Equal(0, Assert.Single(type.Values));
            var order = Assert.Single(query.Orders);
            Assert.Equal("savedqueryid", order.AttributeName);
            Assert.Equal(Microsoft.Xrm.Sdk.Query.OrderType.Ascending, order.OrderType);
            return new EntityCollection([row]);
        }, default);
        Assert.Equal(1, calls);
        Assert.Equal(2, lines.Count);
        Assert.Contains("source=generated", lines[0], StringComparison.Ordinal);
        Assert.Contains("source=existing index=1", lines[1], StringComparison.Ordinal);
        Assert.Contains("grid.object:uint31", lines[1], StringComparison.Ordinal);
        Assert.Contains("grid.jump:identifier", lines[1], StringComparison.Ordinal);
        Assert.Contains("grid.icon:bool01", lines[1], StringComparison.Ordinal);
        Assert.Contains("cell.width:uint31", lines[1], StringComparison.Ordinal);
        Assert.Contains("otherAttributes=1", lines[1], StringComparison.Ordinal);
        Assert.Contains("persistenceProven=false", lines[1], StringComparison.Ordinal);
        var text = string.Join("\n", lines);
        Assert.DoesNotContain("private-", text, StringComparison.Ordinal);
        Assert.DoesNotContain("new_name", text, StringComparison.Ordinal);
        Assert.DoesNotContain(row.Id.ToString(), text, StringComparison.Ordinal);
        Assert.DoesNotContain("<grid", text, StringComparison.Ordinal);
        Assert.All(lines, line => Assert.InRange(line.Length, 1, 1600));
    }

    /// <summary>重複正式身份、錯 scope／型別／投影、續頁或超量均整批拒絕，不丟棄重複資料假成功。</summary>
    /// <param name="kind">固定管理列故障注入種類。</param>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("scope")]
    [InlineData("type")]
    [InlineData("extra")]
    [InlineData("identity")]
    [InlineData("more")]
    [InlineData("cookie")]
    [InlineData("overflow")]
    public void LayoutComparison_InvalidSnapshot_FailsClosed(string kind)
    {
        var row = LayoutRow();
        var rows = new EntityCollection([row]);
        switch (kind)
        {
            case "duplicate": rows.Entities.Add(row); break;
            case "scope": row["returnedtypecode"] = "contact"; break;
            case "type": row["querytype"] = 64; break;
            case "extra": row["name"] = "private-component"; break;
            case "identity": row["savedqueryid"] = Guid.NewGuid(); break;
            case "more": rows.MoreRecords = true; rows.PagingCookie = "private-small-cookie"; break;
            case "cookie": rows.PagingCookie = new string('x', 16 * 1024 + 1); break;
            default:
                while (rows.Entities.Count < 17) rows.Entities.Add(LayoutRow());
                break;
        }
        Assert.Throws<InvalidDataException>(() => UiSavedQueryValidationProbe.ReadLayoutComparison(Generate(), _ => rows, default));
    }

    /// <summary>分頁拒絕以固定布林標記區分原因，不回顯含秘密哨兵的 cookie 原文。</summary>
    [Fact]
    public void LayoutComparison_PageFailure_ExposesOnlyFixedCauseFlags()
    {
        var rows = new EntityCollection([LayoutRow()]) { PagingCookie = new string('x', 16 * 1024 + 1) + "private-page-cookie" };
        var failure = Assert.Throws<InvalidDataException>(
            () => UiSavedQueryValidationProbe.ReadLayoutComparison(Generate(), _ => rows, default));
        var summary = UiSavedQueryValidationProbe.DescribeFailure(failure);
        Assert.Contains("page-cookie=True", summary, StringComparison.Ordinal);
        Assert.Contains("page-more=False", summary, StringComparison.Ordinal);
        Assert.Contains("page-overflow=False", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("private-page-cookie", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// D365 末頁仍可能帶 cookie；MoreRecords=false 才代表完整，不可把合法末頁當成續頁。
    /// 合成一萬六千三百八十四 bytes cookie，斷言不重送、不輸出且仍完成身份／版面驗證。
    /// </summary>
    [Fact]
    public void LayoutComparison_FinalPageCookie_IsBoundedDiscardedAndNeverReplayed()
    {
        var calls = 0;
        var lines = UiSavedQueryValidationProbe.ReadLayoutComparison(Generate(), _ =>
        {
            calls++;
            return new EntityCollection([LayoutRow()]) { PagingCookie = new string('x', 16 * 1024) };
        }, default);
        Assert.Equal(1, calls);
        Assert.Equal(2, lines.Count);
        Assert.DoesNotContain(new string('x', 100), string.Join("\n", lines), StringComparison.Ordinal);
    }

    /// <summary>末頁多位元組 cookie 仍受 UTF-8 bytes 上限，不能只檢查字元數就發布結果。</summary>
    [Fact]
    public void LayoutComparison_FinalPageCookie_RejectsUtf8ByteOverflow()
    {
        var rows = new EntityCollection([LayoutRow()]) { PagingCookie = new string('測', 6000) };
        Assert.Throws<InvalidDataException>(
            () => UiSavedQueryValidationProbe.ReadLayoutComparison(Generate(), _ => rows, default));
    }

    /// <summary>caller 在版面讀取前或返回後取消都不發布候選；沒有後續 SDK 呼叫或背景工作。</summary>
    /// <param name="before">true 入場前取消，false 注入返回前取消。</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LayoutComparison_Cancellation_DoesNotPublish(bool before)
    {
        using var cancellation = new CancellationTokenSource();
        if (before) cancellation.Cancel();
        var calls = 0;
        Assert.Throws<OperationCanceledException>(() => UiSavedQueryValidationProbe.ReadLayoutComparison(Generate(), _ =>
        {
            calls++;
            cancellation.Cancel();
            return new EntityCollection();
        }, cancellation.Token));
        Assert.Equal(before ? 0 : 1, calls);
    }

    /// <summary>DTD／外部實體與 XML 大小均先受界線限制；解析錯誤只由安全例外摘要投影，不能輸出 XML。</summary>
    [Fact]
    public void LayoutDescription_DtdAndOversize_AreRejectedWithoutUnsafeSummary()
    {
        var exception = Assert.Throws<System.Xml.XmlException>(() => UiSavedQueryValidationProbe.DescribeLayout(
            "<!DOCTYPE grid [<!ENTITY private SYSTEM 'file:///private-secret'>]><grid>&private;</grid>"));
        var summary = UiSavedQueryValidationProbe.DescribeFailure(exception);
        Assert.DoesNotContain("private-secret", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("<!DOCTYPE", summary, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => UiSavedQueryValidationProbe.DescribeLayout(new string('x', 64 * 1024 + 1)));
        Assert.Throws<InvalidDataException>(() => UiSavedQueryValidationProbe.DescribeLayout("<private-secret />"));
    }

    /// <summary>建立單案合成管理列，名稱與未知屬性含秘密哨兵；只有本案持有 Entity，沒有 SDK／連線資源。</summary>
    /// <returns>正式主鍵一致但全部值均屬合成的 savedquery 投影。</returns>
    private static Entity LayoutRow()
    {
        var id = Guid.NewGuid();
        return new Entity("savedquery", id)
        {
            ["savedqueryid"] = id, ["returnedtypecode"] = "new_linechannel", ["querytype"] = 0,
            ["layoutxml"] = "<grid name='private-display' object='10001' jump='new_name' select='1' icon='1' preview='1'><row name='private-row' id='new_linechannelid'><cell name='new_name' width='125' privateAttribute='private-value' /></row></grid>"
        };
    }

    /// <summary>載入本工作樹正式 schema，使用明確合成 metadata 產生唯一平台檢視；不把合成代碼當實機證據。</summary>
    /// <returns>只含純值的正式生成候選，生命週期由本案擁有。</returns>
    private static GeneratedView Generate()
    {
        var schema = SchemaFiles.LoadForOrganization(DeploymentCli.FindRepositoryRoot(), "goodshepherd20");
        var view = Assert.Single(schema.Views.Where(view => view.Entity == "new_linechannel"));
        return ViewDefinitions.Generate(schema, view, primaryId: "new_linechannelid", objectTypeCode: 10001);
    }

    /// <summary>注入不在允許清單的型別名稱，驗證診斷不能直接輸出 GetType().Name；不擁有任何外部資源。</summary>
    private sealed class PrivateTypeMustNotAppearException : Exception
    {
        /// <summary>建立含秘密哨兵的合成錯誤，只在單案 throw／report 鏈存活，不連線或保存 SDK。</summary>
        internal PrivateTypeMustNotAppearException() : base("private-exception") { }
    }
}

/*
檔案整體說明
目的與定位：以純合成 UI scope 保護 FS 診斷輸出契約；沒有 User Secrets 或實機連線。
主要流程：建立同正式 ID 的已／未發布純值記錄，故意把秘密哨兵放入名稱及 XML，
要求摘要只出現正式管理 ID 與固定差異欄名；兩來源完全相同時不能憑空列為待發布。
故障注入：未知實體、空 ID、重複 ID 與缺失來源都必須失敗，不能掉列或發布部分摘要。
資源與隔離：各案只擁有不可變合成值、區域陣列與摘要，沒有 stream、task、client 或靜態可變資料；
測試結束自然釋放所有引用，不需要清理真資料，結果不宣稱部署或發布驗收已完成。
維護方式：摘要欄位擴充時同步增加秘密不外洩斷言，不能把原始名稱或 XML 加回輸出。
*/
using System;
using System.IO;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>獨立合成 scope 的輸出與身份契約；每個故障皆由本案建立。</summary>
public sealed class UiPendingDiagnosticProbeTests
{
    /// <summary>官方轉換的數字 scope 不可反序列化成任意輸出；合成秘密哨兵不應離開固定布林摘要。</summary>
    [Fact]
    public void DescribeConvertedScope_ReportsOnlyNumericShape()
    {
        var result = UiPendingDiagnosticProbe.DescribeConvertedScope(request =>
        {
            Assert.IsType<QueryExpressionToFetchXmlRequest>(request);
            return new QueryExpressionToFetchXmlResponse
            {
                Results = { ["FetchXml"] = "<fetch><entity><filter><condition><value>10001</value></condition></filter></entity></fetch>" }
            };
        });
        Assert.Equal("FS-CONVERSION numericScope=True", result);
    }

    /// <summary>在故障訊息中注入秘密哨兵；診斷只能輸出預先定義的分類，不能把任意 Message 轉印。</summary>
    [Fact]
    public void DescribeFault_ContainsSensitiveText_EmitsOnlyFixedClassifications()
    {
        var result = UiPendingDiagnosticProbe.DescribeFault(new OrganizationServiceFault
        {
            ErrorCode = unchecked((int)0x80040203), Message = "SECRET-TOKEN failed to convert String to Int32 format"
        });
        Assert.Equal("FS-FAULT code=0x80040203 integer=False Int32=True String=True format=True " +
            "order=False alias=False paging=False entity=False attribute=False query=False convert=True type=False", result);
        Assert.DoesNotContain("SECRET", result, StringComparison.Ordinal);
    }

    /// <summary>名稱及 XML 注入敏感哨兵；差異摘要須精確且不得包含內容。</summary>
    [Fact]
    public void Describe_ChangedValues_ReturnsOnlyIdentityAndFixedFieldNames()
    {
        var before = Row();
        var after = before with { Name = "SECRET-NAME", FetchXml = "SECRET-XML", Hash = "changed" };
        var line = Assert.Single(UiPendingDiagnosticProbe.Describe(Scope([before], [after]), ["account"]));
        Assert.Equal($"FS-PENDING type=systemform entity=account id={before.Id:D} fields=name,formxml " +
            "publishedEmpty=False unpublishedEmpty=False parsedEqual=False", line);
        Assert.DoesNotContain("SECRET", line, StringComparison.Ordinal);
    }

    /// <summary>內容相同的完整兩來源不得誤列差異；不接受僅因未發布查詢回傳就當作 pending。</summary>
    [Fact]
    public void Describe_EqualSources_ReturnsEmpty()
    {
        var row = Row();
        Assert.Empty(UiPendingDiagnosticProbe.Describe(Scope([row], [row]), ["account"]));
    }

    /// <summary>兩來源身份完整性及白名單須在任何摘要回傳之前驗證，不能靜默去重或掉列。</summary>
    /// <param name="fault">合成故障種類，只限本案固定四種。</param>
    [Theory]
    [InlineData("unknown")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    public void Describe_InvalidIdentityOrScope_RejectsWholeResult(string fault)
    {
        var row = Row();
        var scope = fault switch
        {
            "unknown" => Scope([row with { Entity = "secret" }], [row with { Entity = "secret" }]),
            "empty" => Scope([row with { Id = Guid.Empty }], [row with { Id = Guid.Empty }]),
            "duplicate" => Scope([row, row], [row, row]),
            _ => Scope([row], [])
        };
        Assert.Throws<InvalidDataException>(() => UiPendingDiagnosticProbe.Describe(scope, ["account"]));
    }

    /// <summary>空值、純排版與真內容差異須明確區分，輸出不能包含 XML 或敏感哨兵。</summary>
    /// <param name="before">合成已發布文字。</param><param name="after">合成未發布文字。</param>
    /// <param name="expected">固定安全摘要。</param>
    [Theory]
    [InlineData("", "<form/>", "publishedEmpty=True unpublishedEmpty=False parsedEqual=False")]
    [InlineData("<form><x /></form>", "<form>\r\n <x/>\r\n</form>",
        "publishedEmpty=False unpublishedEmpty=False parsedEqual=True")]
    [InlineData("<form><x>SECRET-OLD</x></form>", "<form><x>SECRET-NEW</x></form>",
        "publishedEmpty=False unpublishedEmpty=False parsedEqual=False")]
    public void DescribeXmlShape_OnlyReportsFixedBooleanFacts(string before, string after, string expected)
    {
        var actual = UiPendingDiagnosticProbe.DescribeXmlShape(before, after);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("SECRET", actual, StringComparison.Ordinal);
    }

    /// <summary>相同的 DTD、非法 XML 或過量文字都不能假稱等值，也不得解析任何外部資源。</summary>
    [Fact]
    public void DescribeXmlShape_UnsafeOrOversizedXml_NeverClaimsEquality()
    {
        foreach (var text in new[] { "<!DOCTYPE form SYSTEM 'file:///SECRET'><form/>", "<broken",
                     new string('x', 1_048_577) })
        {
            Assert.Equal("publishedEmpty=False unpublishedEmpty=False parsedEqual=False",
                UiPendingDiagnosticProbe.DescribeXmlShape(text, text));
        }
    }

    /// <summary>結構投影只可輸出固定種類／白名單欄名，未知名稱與實際值的哨兵不得外洩。</summary>
    /// <param name="before">合成已發布 XML。</param><param name="after">合成未發布 XML。</param>
    /// <param name="expected">獨立指定的固定輸出。</param>
    [Theory]
    [InlineData("<form id='SECRET-A'/>", "<form id='SECRET-B'/>", "kind=attributes element=form attributes=id")]
    [InlineData("<form/>", "<form><SECRET-ELEMENT/></form>",
        "kind=element-count published=1 unpublished=2 changes=other:1")]
    [InlineData("<form SECRET-KEY='A'/>", "<form SECRET-KEY='B'/>",
        "kind=attributes element=form attributes=other")]
    public void DescribeXmlStructure_ReportsNoArbitraryNamesOrValues(string before, string after, string expected)
    {
        var actual = UiPendingDiagnosticProbe.DescribeXmlStructure(before, after);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("SECRET", actual, StringComparison.Ordinal);
    }

    /// <summary>全語系對照只送固定唯讀訊息、單一正式 ID、一頁一列及 formxml；不能偷偷改成寫入。</summary>
    [Fact]
    public void ReadAllLabels_FixedReadMessage_ValidatesOneFormalIdentity()
    {
        var id = Guid.NewGuid();
        var xml = UiPendingDiagnosticProbe.ReadAllLabels(request =>
        {
            Assert.Equal("RetrieveMultipleSystemFormsWithAllLabels", request.RequestName);
            var query = Assert.IsType<QueryExpression>(request.Parameters["Query"]);
            Assert.Equal("systemform", query.EntityName);
            Assert.Equal(1, query.PageInfo.Count);
            Assert.Equal(1, query.PageInfo.PageNumber);
            Assert.Equal(id, Assert.Single(Assert.Single(query.Criteria.Conditions).Values));
            Assert.Equal("formxml", Assert.Single(query.ColumnSet.Columns));
            var response = new OrganizationResponse();
            response.Results["EntityCollection"] = new EntityCollection([new Entity("systemform", id)
                { ["formxml"] = "<form/>" }]);
            return response;
        }, id, CancellationToken.None);
        Assert.Equal("<form/>", xml);
    }

    /// <summary>單筆未發布對照只允許正式 systemform 身份及 formxml，不以一般 Retrieve 冒充未發布來源。</summary>
    [Fact]
    public void ReadUnpublishedSingle_UsesExactReadMessageAndIdentity()
    {
        var id = Guid.NewGuid();
        var xml = UiPendingDiagnosticProbe.ReadUnpublishedSingle(request =>
        {
            Assert.Equal("RetrieveUnpublished", request.RequestName);
            var target = Assert.IsType<EntityReference>(request.Parameters["Target"]);
            Assert.Equal("systemform", target.LogicalName);
            Assert.Equal(id, target.Id);
            Assert.Equal("formxml", Assert.Single(Assert.IsType<ColumnSet>(request.Parameters["ColumnSet"]).Columns));
            var response = new OrganizationResponse();
            response.Results["Entity"] = new Entity("systemform", id) { ["formxml"] = "<form/>" };
            return response;
        }, id, CancellationToken.None);
        Assert.Equal("<form/>", xml);
    }

    /// <summary>兩來源 alias 診斷必須使用相同單列 FetchXML，只改讀取訊息；正式身份及 AliasedValue 來源仍須驗證。</summary>
    /// <param name="unpublished">已／未發布版本。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadAliasedForm_UsesSameFixedQueryForBothSources(bool unpublished)
    {
        var id = Guid.NewGuid();
        var xml = UiPendingDiagnosticProbe.ReadAliasedForm(request =>
        {
            Assert.Equal(unpublished ? "RetrieveUnpublishedMultiple" : "RetrieveMultiple", request.RequestName);
            var query = Assert.IsType<FetchExpression>(request.Parameters["Query"]).Query;
            Assert.Contains("alias=\"rawformxml\"", query, StringComparison.Ordinal);
            Assert.Contains(id.ToString("D"), query, StringComparison.Ordinal);
            Assert.Contains("count=\"1\"", query, StringComparison.Ordinal);
            var response = new OrganizationResponse();
            response.Results["EntityCollection"] = new EntityCollection([new Entity("systemform", id)
                { ["rawformxml"] = new AliasedValue("systemform", "formxml", "<form/>") }]);
            return response;
        }, id, unpublished, CancellationToken.None);
        Assert.Equal("<form/>", xml);
    }

    /// <summary>建立正式管理身份的合成值，字串只供測試，沒有真實 XML／名稱。</summary>
    private static UiViewState Row() => new(Guid.NewGuid(), "systemform", "account", "old", 2,
        false, 1, false, "old-xml", "", "original");

    /// <summary>建立單案不共用的兩來源容器，不持有 SDK 或其他資源。</summary>
    private static UiViewScope Scope(UiViewState[] before, UiViewState[] after) =>
        new(Guid.Empty, "", Guid.Empty, [], before, after);
}

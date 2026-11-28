/*
檔案整體說明
目的與定位：FS savedquery 疑似建立失敗的測試專屬唯讀診斷，不修改產品 reader、plan、schema
或部署 API。官方訊息依 Microsoft Learn 的 ValidateSavedQueryRequest 契約，只送 FetchXml／
QueryType 並核對 ValidateSavedQueryResponse；驗證通過絕不等於 Create、啟用或發布成功。
主要流程：僅接受正式產生器的固定 new_linechannel／new_name 結果，與同表正式主鍵加名稱的
最小 Fetch 對照；任何 QueryType／XML 漂移先拒絕，不允許 caller 指定表或任意 XML 範圍。
既有版面對照只讀同表 QueryType 0 的 savedquery 管理資料，單頁最多十六列、每份 XML
最多六十四 KiB 字元；末頁 cookie 仍限制十六 KiB UTF-8，但不重送、不輸出或保留。
輸出白名單屬性名稱與值型別、未知項目數及 hash，不印名稱、XML 或 ID。
信任與隔離：呼叫端先核對 goodshepherd20、ProductionGuard 及正式 metadata；本檔不載秘密、
不建立連線、不執行業務查詢，不擁有 SDK、writer、CTS、Task 或快取。每次新建 SDK request，
候選只活在同步呼叫，版面摘要全部驗完才發布，不保存跨組織可變狀態。
生命週期：同步 SDK 前後檢查 caller token；已入場呼叫須等返回，由外層獨占 lease 排空釋放。
XML reader／StringReader 由本檔 using 關閉，禁止 DTD／外部解析；沒有背景作業或無界遞迴。
失敗與驗證：真正 Exception 原件交共用診斷一次；caller 正常取消重新拋出，不記成功。
安全輸出只有固定案例名、有限型別、數字碼、四 KiB 截限文字的白名單關鍵字布林與 SHA256；
reporter 自身失敗只寫固定 stderr，不遞迴通知、不掩蓋原失敗。離線故障注入保護上述契約。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>固定單表、單次呼叫的診斷能力；純值常數不含身分，所有可變資料皆為 operation-local。</summary>
internal static class UiSavedQueryValidationProbe
{
    /// <summary>正式生成的唯一允許 Fetch；精確比對避免以任意表、條件、DTD 或額外欄位擴大診斷範圍。</summary>
    private const string GeneratedFetch = "<fetch><entity name=\"new_linechannel\"><attribute name=\"new_name\" /></entity></fetch>";

    /// <summary>官方最小 Fetch 形狀的同表對照，固定正式主鍵與名稱欄；僅 Validate，不執行讀列。</summary>
    private const string MinimalFetch = "<fetch version=\"1.0\" output-format=\"xml-platform\" mapping=\"logical\" distinct=\"false\"><entity name=\"new_linechannel\"><attribute name=\"new_linechannelid\" /><attribute name=\"new_name\" /></entity></fetch>";

    /// <summary>不可變診斷關鍵字，不輸出來源文字；每段文字最多掃描四 KiB，不遍歷任意 InnerFault。</summary>
    private static readonly IReadOnlyList<string> Keywords = Array.AsReadOnly(new[]
        { "fetch", "query", "attribute", "entity", "permission", "privilege", "xml", "format", "layout",
            "page-null", "page-more", "page-cookie", "page-overflow" });

    /// <summary>不可變版面屬性白名單；名稱來源僅為此清單，不回顯未知 XML 屬性或其值。</summary>
    private static readonly IReadOnlyList<string> LayoutAttributes = Array.AsReadOnly(new[]
        { "name", "object", "jump", "select", "icon", "preview", "id", "width", "disableMetaDataBinding" });

    /// <summary>
    /// 發送一個官方唯讀 ValidateSavedQueryRequest；成功只表示 Fetch 已驗證，不涉及任何持久化。
    /// 不支援 Create／Update／Delete／Publish 或業務列讀取，呼叫後不持有委派或例外參照。
    /// </summary>
    /// <param name="generated">正式產生器回傳的固定平台檢視；LayoutXml 不在此訊息驗證範圍內。</param>
    /// <param name="minimal">true 使用固定主鍵／名稱控制，false 使用精確生成結果。</param>
    /// <param name="execute">呼叫端借出的 SDK 執行能力；本方法只能建立 ValidateSavedQueryRequest。</param>
    /// <param name="output">呼叫端擁有的有界證據 writer，不由本方法關閉。</param>
    /// <param name="report">接收原 Exception 的共用診斷入口，不傳錯誤字串代替例外。</param>
    /// <param name="cancellationToken">caller 的正常取消；傳輸自己的逾時不可冒用此分類。</param>
    /// <returns>只有正確型別回應且未取消才為 true；故障為 false，不能宣稱建立成功。</returns>
    internal static bool Run(GeneratedView generated, bool minimal,
        Func<OrganizationRequest, OrganizationResponse> execute, TextWriter output,
        Action<Exception> report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(report);
        RequireGenerated(generated);
        cancellationToken.ThrowIfCancellationRequested();
        var fetch = minimal ? MinimalFetch : generated.FetchXml;
        var prefix = "FS-SAVEDQUERY case=" + (minimal ? "minimal" : "generated")
            + " queryType=0 fetchSha256=" + Hash(fetch);
        try
        {
            var response = execute(new ValidateSavedQueryRequest { FetchXml = fetch, QueryType = 0 });
            cancellationToken.ThrowIfCancellationRequested();
            if (response is not ValidateSavedQueryResponse)
                throw new InvalidDataException("Saved query validation response type rejected.");
            output.WriteLine(prefix + " result=OK fetchValidated=true persistenceProven=false");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            try { report(exception); }
            catch { Console.Error.WriteLine("[FS.SavedQueryProbe] DiagnosticReportingFailed"); }
            output.WriteLine(prefix + " " + DescribeFailure(exception));
            return false;
        }
    }

    /// <summary>
    /// 投影安全失敗摘要，有限型別不使用 GetType().Name；故障文字每段最多四 KiB，原件只交診斷入口。
    /// 不輸出 Message、Reason、TraceText、InnerFault、ErrorDetails 或 GUID，hash 不是持久化成功證據。
    /// </summary>
    /// <param name="exception">借入本次原始故障，不保存或修改。</param>
    /// <returns>固定名稱、錯碼、布林與兩段截限文字的 SHA256；沒有任意來源片段。</returns>
    internal static string DescribeFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var fault = (exception as FaultException<OrganizationServiceFault>)?.Detail;
        var kind = exception switch
        {
            FaultException<OrganizationServiceFault> => "SdkFault",
            FaultException => "TransportFault",
            TimeoutException => "Timeout",
            OperationCanceledException => "Cancellation",
            InvalidDataException => "InvalidData",
            UnauthorizedAccessException => "AccessDenied",
            IOException => "IO",
            InvalidOperationException => "InvalidOperation",
            _ => "Other"
        };
        var text = Bounded(fault?.Message ?? exception.Message) + "\n" + Bounded(fault?.TraceText);
        var code = fault is null ? "none" : "0x" + fault.ErrorCode.ToString("X8", CultureInfo.InvariantCulture);
        return "result=FAIL type=" + kind + " code=" + code + " textSha256=" + Hash(text) + " "
            + string.Join(" ", Keywords.Select(word => word + "=" + text.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// 只讀固定表的既有公用版面，核對正式 ID、scope、投影與單頁上限後才整批回傳安全摘要。
    /// 不讀 savedquery 名稱、不執行 Fetch，不讀業務列；無續頁、重試、寫入或忽略重複 ID。
    /// D365 可在 MoreRecords=false 時仍回末頁 cookie；先限字元再限 UTF-8 bytes，最多十六 KiB，
    /// 完整性仍由 MoreRecords=false 與列數決定，不把 cookie 存在誤判為缺頁。
    /// </summary>
    /// <param name="generated">正式生成候選，先驗固定 Fetch 範圍；版面由 DescribeLayout 有界解析。</param>
    /// <param name="retrieve">呼叫端借出的 savedquery 管理讀取能力，不轉移服務所有權。</param>
    /// <param name="cancellationToken">讀取前後及每列驗證的合作取消來源。</param>
    /// <returns>完整驗證後的 detached 字串陣列，沒有 Entity、XML、名稱或 ID 的參照。</returns>
    internal static IReadOnlyList<string> ReadLayoutComparison(GeneratedView generated,
        Func<QueryExpression, EntityCollection> retrieve, CancellationToken cancellationToken)
    {
        RequireGenerated(generated);
        ArgumentNullException.ThrowIfNull(retrieve);
        cancellationToken.ThrowIfCancellationRequested();
        var query = new QueryExpression("savedquery")
        {
            ColumnSet = new ColumnSet("savedqueryid", "returnedtypecode", "querytype", "layoutxml"),
            PageInfo = new PagingInfo { Count = 16, PageNumber = 1 }
        };
        query.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, "new_linechannel");
        query.Criteria.AddCondition("querytype", ConditionOperator.Equal, 0);
        query.AddOrder("savedqueryid", OrderType.Ascending);
        var rows = retrieve(query);
        cancellationToken.ThrowIfCancellationRequested();
        var cookieOverflow = rows?.PagingCookie is { } cookie
            && (cookie.Length > 16 * 1024 || Encoding.UTF8.GetByteCount(cookie) > 16 * 1024);
        if (rows is null || rows.MoreRecords || cookieOverflow || rows.Entities.Count > 16)
            throw new InvalidDataException("Saved query layout response exceeds the fixed page boundary."
                + (rows is null ? " page-null" : "")
                + (rows?.MoreRecords == true ? " page-more" : "")
                + (cookieOverflow ? " page-cookie" : "")
                + (rows?.Entities.Count > 16 ? " page-overflow" : ""));
        var lines = new List<string> { "FS-SAVEDQUERY-LAYOUT source=generated " + DescribeLayout(generated.LayoutXml) };
        var ids = new HashSet<Guid>();
        foreach (var row in rows.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row is null || row.LogicalName != "savedquery" || row.Id == Guid.Empty || !ids.Add(row.Id)
                || row.Attributes.Count != 4 || row.GetAttributeValue<Guid>("savedqueryid") != row.Id
                || row.GetAttributeValue<string>("returnedtypecode") != "new_linechannel"
                || !row.Attributes.TryGetValue("querytype", out var type) || type is not 0
                || !row.Attributes.TryGetValue("layoutxml", out var layout) || layout is not string xml)
                throw new InvalidDataException("Saved query layout row identity, scope or projection rejected.");
            lines.Add("FS-SAVEDQUERY-LAYOUT source=existing index=" + ids.Count.ToString(CultureInfo.InvariantCulture)
                + " " + DescribeLayout(xml));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return lines.ToArray();
    }

    /// <summary>
    /// 對版面做有界、禁止 DTD 的結構投影；僅列 grid／row／cell 的固定屬性名稱及值型別。
    /// 未知屬性／節點只計數，不回顯名稱；不驗斷該 XML 會被 savedquery Create 接受。
    /// </summary>
    /// <param name="xml">單案借入的版面，最多六十四 KiB 字元，不保存原文。</param>
    /// <returns>元素計數、固定屬性／型別集合及版面 SHA256；不含屬性值或 XML。</returns>
    internal static string DescribeLayout(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > 64 * 1024)
            throw new InvalidDataException("Saved query layout size rejected.");
        using var source = new StringReader(xml);
        using var reader = XmlReader.Create(source, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64 * 1024
        });
        var grid = XElement.Load(reader);
        var nodes = grid.DescendantsAndSelf().Take(129).ToArray();
        if (grid.Name != "grid" || nodes.Length > 128)
            throw new InvalidDataException("Saved query layout structural boundary rejected.");
        var shape = new SortedSet<string>(StringComparer.Ordinal);
        var unknownAttributes = 0;
        var unknownNodes = 0;
        foreach (var node in nodes)
        {
            var knownNode = node.Name == "grid" ? "grid" : node.Name == "row" ? "row" : node.Name == "cell" ? "cell" : null;
            if (knownNode is null) unknownNodes++;
            foreach (var attribute in node.Attributes())
            {
                if (knownNode is null || attribute.Name.NamespaceName.Length != 0 || !LayoutAttributes.Contains(attribute.Name.LocalName))
                {
                    unknownAttributes++;
                    continue;
                }
                var value = attribute.Value;
                var type = value.Length == 0 ? "empty"
                    : value is "0" or "1" ? "bool01"
                    : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? "uint31"
                    : value.Length <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? "identifier"
                    : "string";
                shape.Add(knownNode + "." + attribute.Name.LocalName + ":" + type);
            }
        }
        return "nodes=" + nodes.Length.ToString(CultureInfo.InvariantCulture)
            + " otherNodes=" + unknownNodes.ToString(CultureInfo.InvariantCulture)
            + " otherAttributes=" + unknownAttributes.ToString(CultureInfo.InvariantCulture)
            + " attributes=" + string.Join(",", shape) + " layoutSha256=" + Hash(xml)
            + " persistenceProven=false";
    }

    /// <summary>固定 Fetch 精確比對，不解析不受信任的 XML，也不讓 minimal 對照繞過正式生成候選驗證。</summary>
    /// <param name="generated">只由正式 ViewDefinitions.Generate 取得的單次候選。</param>
    private static void RequireGenerated(GeneratedView generated)
    {
        ArgumentNullException.ThrowIfNull(generated);
        if (generated.QueryType != 0 || generated.FetchXml != GeneratedFetch)
            throw new ArgumentException("Only the fixed generated platform view is allowed.", nameof(generated));
    }

    /// <summary>最多保留一段四 KiB 字元，不走任意深度巢狀例外，也不輸出來源本文。</summary>
    private static string Bounded(string? text) => text is null ? "" : text[..Math.Min(text.Length, 4096)];

    /// <summary>只對已固定或有界的 operation-local 字串計算 SHA256；不保存原文、buffer 或全域狀態。</summary>
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

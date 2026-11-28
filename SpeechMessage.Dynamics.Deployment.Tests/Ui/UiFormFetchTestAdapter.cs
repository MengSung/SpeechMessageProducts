/*
檔案整體說明
目的與定位：只供離線 SDK 替身辨識正式表單 reader 的 FetchXML alias 契約，不進入產品程式。
主要流程：把產品建立的固定 systemform 查詢還原成既有 Page 委派使用的 QueryExpression，
保留欄位、實體條件、唯一主鍵排序、頁碼及 cookie；不認得的結構須拒絕，不回退成全表查詢。
轉換訊息以單案序號模擬伺服器的數字 ObjectTypeCode，uiname 只供假 SDK 無狀態還原原範圍；
不維護跨測試字典，也不以這種合成對應冒充實機 metadata。
回應側：只有 FetchExpression 才把合成 formxml 包成正式 AliasedValue，複製頁面及列以免修改
測試原始資料或其他來源；缺欄位照原貌保留，不能讓錯誤來源靠 helper 自動補值過關。
資源與隔離：輸入全部由本案產品／替身產生，不讀外部 XML、秘密、SDK 或 User Secrets；
所有陣列與純值均單次區域擁有，沒有連線、stream、background task 或跨測試可變狀態。
驗證方式：正式 reader 專屬測試另外直接攔截原 FetchXML，驗證查詢與 AliasedValue 的真契約，
本轉接器只維護原有故障注入委派，不取代對實際線上查詢／發布的獨立斷言。
*/
using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>離線假 SDK 的窄轉接器；沒有任何真 SDK 呼叫或共用狀態。</summary>
internal static class UiFormFetchTestAdapter
{
    /// <summary>只替固定表單查詢模擬官方轉換；保留完整條件，拒絕未知訊息以外的自動回應。</summary>
    /// <param name="request">產品送出的唯讀 SDK 訊息。</param>
    /// <returns>已知轉換的合成回應；其他訊息回 null 供原故障替身處理。</returns>
    internal static OrganizationResponse? Convert(OrganizationRequest request)
    {
        if (request is not QueryExpressionToFetchXmlRequest convert) return null;
        var query = Assert.IsType<QueryExpression>(convert.Query);
        Assert.Equal("systemform", query.EntityName);
        var condition = Assert.Single(query.Criteria.Conditions);
        Assert.Equal(ConditionOperator.In, condition.Operator);
        var fetch = new XElement("fetch", new XAttribute("count", query.PageInfo.Count),
            new XAttribute("page", query.PageInfo.PageNumber),
            new XElement("entity", new XAttribute("name", "systemform"),
                query.ColumnSet.Columns.Select(column => new XElement("attribute", new XAttribute("name", column))),
                query.Orders.Select(order => new XElement("order", new XAttribute("attribute", order.AttributeName),
                    new XAttribute("descending", order.OrderType == OrderType.Descending ? "true" : "false"))),
                new XElement("filter", new XAttribute("type", "and"),
                    new XElement("condition", new XAttribute("attribute", condition.AttributeName), new XAttribute("operator", "in"),
                        condition.Values.Select((value, index) =>
                            new XElement("value", new XAttribute("uiname", (string)value), index + 1))))));
        if (query.PageInfo.PagingCookie is { } cookie) fetch.SetAttributeValue("paging-cookie", cookie);
        return new QueryExpressionToFetchXmlResponse { Results = { ["FetchXml"] = fetch.ToString(SaveOptions.DisableFormatting) } };
    }

    /// <summary>保留既有 QueryExpression；只接受正式 reader 的固定 systemform FetchXML。</summary>
    /// <param name="source">本案產品送出的原始查詢。</param>
    /// <returns>供原有故障注入斷言使用的操作區域查詢。</returns>
    internal static QueryExpression Query(QueryBase source)
    {
        if (source is QueryExpression existing) return existing;
        var fetch = XElement.Parse(Assert.IsType<FetchExpression>(source).Query);
        var entity = Assert.Single(fetch.Elements("entity"));
        Assert.Equal("systemform", (string?)entity.Attribute("name"));
        Assert.Equal("rawformxml", (string?)Assert.Single(entity.Elements("attribute")
            .Where(item => (string?)item.Attribute("name") == "formxml")).Attribute("alias"));
        var result = new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet(entity.Elements("attribute").Select(item => (string)item.Attribute("name")!).ToArray()),
            PageInfo = new PagingInfo
            {
                Count = (int)fetch.Attribute("count")!, PageNumber = (int)fetch.Attribute("page")!,
                PagingCookie = (string?)fetch.Attribute("paging-cookie")
            }
        };
        var order = Assert.Single(entity.Elements("order"));
        result.AddOrder((string)order.Attribute("attribute")!,
            (string?)order.Attribute("descending") == "true" ? OrderType.Descending : OrderType.Ascending);
        var condition = Assert.Single(Assert.Single(entity.Elements("filter")).Elements("condition"));
        Assert.Equal("objecttypecode", (string?)condition.Attribute("attribute"));
        Assert.Equal("in", (string?)condition.Attribute("operator"));
        result.Criteria.AddCondition("objecttypecode", ConditionOperator.In,
            condition.Elements("value").Select(value => (object)((string?)value.Attribute("uiname") ?? value.Value)).ToArray());
        return result;
    }

    /// <summary>只將 FetchXML 的表單回應包成 alias；完整保留頁面故障資訊，不改原始候選。</summary>
    /// <param name="query">原始查詢，用來區分舊式小查詢與新表單 reader。</param>
    /// <param name="source">本案假 SDK 的合成頁面。</param>
    /// <returns>本案獨立回應，沒有來源列的可變字典引用。</returns>
    internal static EntityCollection Wrap(QueryBase query, EntityCollection source)
    {
        if (query is not FetchExpression) return source;
        var result = new EntityCollection
        {
            EntityName = source.EntityName, MoreRecords = source.MoreRecords, PagingCookie = source.PagingCookie
        };
        foreach (var old in source.Entities)
        {
            var row = new Entity(old.LogicalName, old.Id);
            foreach (var pair in old.Attributes)
            {
                if (pair.Key == "formxml")
                    row["rawformxml"] = new AliasedValue("systemform", "formxml", pair.Value);
                else row[pair.Key] = pair.Value;
            }
            result.Entities.Add(row);
        }
        return result;
    }
}

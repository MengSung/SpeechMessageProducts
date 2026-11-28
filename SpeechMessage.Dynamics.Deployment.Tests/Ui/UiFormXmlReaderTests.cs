/*
檔案整體說明
目的與定位：以可區分語系投影與原始 XML 的合成 SDK，保護 FS 表單兩來源的完整性契約。
失敗重現：一般查詢回兩種不同 label 投影，但原始已／未發布內容相同；真 reader 的
UiPublishScope.Check 因此先拋同型 InvalidDataException。修正後必須仍保護真正的 label 改動。
主要流程：分別呼叫兩個公開內部 reader，另直接攔截 FetchXML 驗 scope、欄位、頁碼、cookie，
以及單一正式主鍵排序；不以測試轉接器取代對真查詢的斷言。
故障注入：錯 alias 來源、缺 alias、無效 UTF-8、取消及多餘欄位來源衝突均須拒絕整頁，
不能以投影不一致為由丟棄 XML、忽略 label、放寬元件清單或發布部分結果。
隔離與生命週期：每案只擁有區域假 SDK、純合成 XML、取消來源及回應；假 SDK 沒有外部資源，
取消來源由 using 關閉。沒有秘密、網路、資料列、背景工作或跨案可變狀態；結果不是實機驗收。
*/
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Ui;

/// <summary>使用獨立 SDK 替身驗真正 reader，而非只測 XML 字串轉換。</summary>
public sealed class UiFormXmlReaderTests
{
    /// <summary>重現一般兩來源的 locale label 投影不同；完整原始內容相同時不能誤擋發布。</summary>
    /// <param name="schemaReader">是否驗 schema reader；否則驗 UI 匯入 reader。</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Readers_LocalizedProjectionDiffersButRawXmlMatches_NoFalsePending(bool schemaReader)
    {
        var service = new FormService();
        var pending = schemaReader
            ? UiViewDeployment.Pending(UiViewDeployment.ReadPublicationScope(service, ["account"], CancellationToken.None))
            : UiPublishScope.ReadPendingComponentIds(service, ["account"], CancellationToken.None);
        UiPublishScope.Check(pending, []);
        Assert.Empty(pending);
    }

    /// <summary>真正原始 XML 的 label 值變更仍須擋下；不可藉 locale 修正忽略任何翻譯節點。</summary>
    [Fact]
    public void Readers_RawLabelReallyChanged_StillRefusesUnplannedComponent()
    {
        var service = new FormService { Changed = true };
        var pending = UiViewDeployment.Pending(
            UiViewDeployment.ReadPublicationScope(service, ["account"], CancellationToken.None));
        Assert.Equal(service.Id, Assert.Single(pending));
        Assert.Throws<InvalidDataException>(() => UiPublishScope.Check(pending, []));
    }

    /// <summary>兩來源須收到相同、有界且未修改 caller 的查詢，cookie 由 XML escaping 保真，不可多排一次主鍵。</summary>
    /// <param name="unpublished">已／未發布來源。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadPage_QueryAndSourceArePreserved(bool unpublished)
    {
        var service = new FormService();
        var query = Query();
        query.PageInfo.PageNumber = 2;
        query.PageInfo.PagingCookie = "<cookie page=\"1\" />";
        service.Observe = (fetch, source) =>
        {
            Assert.Equal(unpublished, source);
            Assert.Equal("2", (string?)fetch.Attribute("page"));
            Assert.Equal(query.PageInfo.PagingCookie, (string?)fetch.Attribute("paging-cookie"));
            var entity = Assert.Single(fetch.Elements("entity"));
            Assert.Equal("systemform", (string?)entity.Attribute("name"));
            Assert.Equal("formid", (string?)Assert.Single(entity.Elements("order")).Attribute("attribute"));
            var condition = Assert.Single(Assert.Single(entity.Elements("filter")).Elements("condition"));
            Assert.Equal("1", Assert.Single(condition.Elements("value")).Value);
            Assert.Equal("rawformxml", (string?)Assert.Single(entity.Elements("attribute")
                .Where(item => (string?)item.Attribute("name") == "formxml")).Attribute("alias"));
        };
        var page = UiFormXmlReader.ReadPage(service, query, unpublished, CancellationToken.None);
        Assert.Equal(FormService.Raw, Assert.Single(page.Entities).GetAttributeValue<string>("formxml"));
        Assert.Single(query.Orders);
        Assert.Contains("formxml", query.ColumnSet.Columns);
        Assert.DoesNotContain("rawformxml", query.ColumnSet.Columns);
        Assert.Equal(1, service.Conversions);
    }

    /// <summary>任何不完整或不可信 alias 都須拒絕，不能回退一般投影或只回前半頁。</summary>
    /// <param name="fault">固定合成故障種類。</param>
    [Theory]
    [InlineData("entity")]
    [InlineData("attribute")]
    [InlineData("missing")]
    [InlineData("encoding")]
    [InlineData("collision")]
    public void ReadPage_InvalidAlias_RejectsWholePage(string fault)
    {
        var service = new FormService { Fault = fault };
        Assert.Throws<InvalidDataException>(() => UiFormXmlReader.ReadPage(service, Query(), false, CancellationToken.None));
    }

    /// <summary>已取消不得入場；SDK 返回時取消亦不得回傳頁面，服務仍由外層 owner 管理。</summary>
    [Fact]
    public void ReadPage_CancellationBeforeAndAfterSdk_DoesNotPublishCandidate()
    {
        using var before = new CancellationTokenSource();
        before.Cancel();
        var service = new FormService();
        Assert.Throws<OperationCanceledException>(() => UiFormXmlReader.ReadPage(service, Query(), false, before.Token));
        Assert.Equal(0, service.Reads);
        using var during = new CancellationTokenSource();
        service.Observe = (_, _) => during.Cancel();
        Assert.Throws<OperationCanceledException>(() => UiFormXmlReader.ReadPage(service, Query(), false, during.Token));
        Assert.Equal(1, service.Reads);
    }

    /// <summary>不能無損翻譯的查詢與超量 cookie 必須在 SDK 前拒絕，避免默默擴大或遺失範圍。</summary>
    /// <param name="fault">固定合成查詢故障；每案獨占 query，不借用真 metadata。</param>
    [Theory]
    [InlineData("entity")]
    [InlineData("column")]
    [InlineData("filter")]
    [InlineData("scope")]
    [InlineData("order")]
    [InlineData("page")]
    [InlineData("cookie")]
    public void ReadPage_UnsupportedQuery_RejectsBeforeSdk(string fault)
    {
        var query = Query();
        switch (fault)
        {
            case "entity": query.EntityName = "contact"; break;
            case "column": query.ColumnSet.AddColumn("privatevalue"); break;
            case "filter": query.Criteria.AddCondition("type", ConditionOperator.Equal, 2); break;
            case "scope": query.Criteria.Conditions[0].Values.Add("account"); break;
            case "order": query.AddOrder("name", OrderType.Ascending); break;
            case "page": query.PageInfo.PageNumber = 17; break;
            case "cookie": query.PageInfo.PagingCookie = new string('測', 6000); break;
        }
        var service = new FormService();
        Assert.Throws<InvalidDataException>(() => UiFormXmlReader.ReadPage(service, query, false, CancellationToken.None));
        Assert.Equal(0, service.Reads);
        Assert.Equal(0, service.Conversions);
    }

    /// <summary>官方轉換不是放寬查詢的入口；損壞 XML／DTD／缺欄與未轉型條件都應在真正讀取前拒絕。</summary>
    /// <param name="fault">合成轉換回應故障，不能包含真實 metadata。</param>
    [Theory]
    [InlineData("dtd")]
    [InlineData("column")]
    [InlineData("scope")]
    [InlineData("entity")]
    public void ReadPage_InvalidConversion_RejectsBeforeReading(string fault)
    {
        var service = new FormService { ConversionFault = fault };
        Assert.Throws<InvalidDataException>(() => UiFormXmlReader.ReadPage(service, Query(), false, CancellationToken.None));
        Assert.Equal(1, service.Conversions);
        Assert.Equal(0, service.Reads);
    }

    /// <summary>轉換返回時若 owner 已取消，不能再送表單讀取；取消來源由本案 using 確定釋放。</summary>
    [Fact]
    public void ReadPage_CancelDuringConversion_DoesNotStartReading()
    {
        using var owner = new CancellationTokenSource();
        var service = new FormService { OnConversion = owner.Cancel };
        Assert.Throws<OperationCanceledException>(() => UiFormXmlReader.ReadPage(service, Query(), false, owner.Token));
        Assert.Equal(1, service.Conversions);
        Assert.Equal(0, service.Reads);
    }

    /// <summary>回應身份／scope／容量錯誤拒絕整頁，不能只把有效第一列交給發布者。</summary>
    /// <param name="fault">固定合成回應故障；全部純記憶體，不代表實機驗收。</param>
    [Theory]
    [InlineData("id")]
    [InlineData("scope")]
    [InlineData("duplicate")]
    [InlineData("cookie")]
    [InlineData("xml")]
    [InlineData("total")]
    public void ReadPage_InvalidResponse_RejectsWholePage(string fault)
    {
        var service = new FormService
        {
            Transform = page =>
            {
                var row = page.Entities.Single();
                switch (fault)
                {
                    case "id": row["formid"] = Guid.NewGuid(); break;
                    case "scope": row["objecttypecode"] = "contact"; break;
                    case "duplicate": page.Entities.Add(row); break;
                    case "cookie": page.PagingCookie = new string('測', 6000); break;
                    case "xml": row["rawformxml"] = new AliasedValue("systemform", "formxml", new string('測', 400_000)); break;
                    case "total":
                        page.Entities.Clear();
                        for (var index = 0; index < 17; index++)
                        {
                            var item = new Entity("systemform", Guid.NewGuid())
                            {
                                ["objecttypecode"] = "account",
                                ["rawformxml"] = new AliasedValue("systemform", "formxml", new string('x', 1024 * 1024))
                            };
                            page.Entities.Add(item);
                        }
                        break;
                }
                return page;
            }
        };
        Assert.Throws<InvalidDataException>(() => UiFormXmlReader.ReadPage(service, Query(), false, CancellationToken.None));
    }

    /// <summary>回應列、字典及選項值都由本次候選擁有；caller 改值不能污染 SDK 原快照及另一來源。</summary>
    [Fact]
    public void ReadPage_ResultDoesNotMutateSdkSnapshot()
    {
        EntityCollection? original = null;
        var service = new FormService { Transform = page => original = page };
        var result = UiFormXmlReader.ReadPage(service, Query(), false, CancellationToken.None);
        var row = Assert.Single(result.Entities);
        row["name"] = "caller change";
        row.GetAttributeValue<OptionSetValue>("type").Value = 99;
        var source = Assert.Single(original!.Entities);
        Assert.Equal("form", source.GetAttributeValue<string>("name"));
        Assert.Equal(2, source.GetAttributeValue<OptionSetValue>("type").Value);
        Assert.True(source.Attributes.ContainsKey("rawformxml"));
        Assert.False(source.Attributes.ContainsKey("formxml"));
    }

    /// <summary>建立兩個正式 reader 使用的固定欄位、scope 與唯一排序，不讀真 metadata。</summary>
    private static QueryExpression Query()
    {
        var query = new QueryExpression("systemform")
        {
            ColumnSet = new ColumnSet("formid", "objecttypecode", "name", "type", "formactivationstate", "ismanaged", "formxml"),
            PageInfo = new PagingInfo { Count = 256, PageNumber = 1 }
        };
        query.Criteria.AddCondition("objecttypecode", ConditionOperator.In, "account");
        query.AddOrder("formid", OrderType.Ascending);
        return query;
    }

    /// <summary>只模擬本案 UI 讀取，所有寫入都拒絕；不持有 client／背景工作或需 Dispose 的資源。</summary>
    private sealed class FormService : IOrganizationService
    {
        internal const string Raw = "<form><label languagecode='1028' description='A'/><label languagecode='1033' description='B'/></form>";
        internal Guid Id { get; } = Guid.NewGuid();
        internal bool Changed { get; init; }
        internal string? Fault { get; init; }
        internal int Reads { get; private set; }
        internal int Conversions { get; private set; }
        internal Action<XElement, bool>? Observe { get; set; }
        internal Func<EntityCollection, EntityCollection>? Transform { get; init; }
        internal string? ConversionFault { get; init; }
        internal Action? OnConversion { get; init; }

        /// <summary>未發布只允許官方唯讀訊息，保留其來源旗標供查詢斷言。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (UiFormFetchTestAdapter.Convert(request) is { } converted)
            {
                Conversions++;
                OnConversion?.Invoke();
                if (ConversionFault is not null)
                {
                    var xml = XElement.Parse((string)converted.Results["FetchXml"]);
                    if (ConversionFault == "entity") xml.Element("entity")!.SetAttributeValue("name", "contact");
                    if (ConversionFault == "column") xml.Descendants("attribute").First().Remove();
                    if (ConversionFault == "scope") xml.Descendants("value").Single().Value = "account";
                    converted.Results["FetchXml"] = ConversionFault == "dtd"
                        ? "<!DOCTYPE fetch [<!ENTITY x 'secret'>]><fetch>&x;</fetch>"
                        : xml.ToString();
                }
                return converted;
            }
            var read = Assert.IsType<RetrieveUnpublishedMultipleRequest>(request);
            return new RetrieveUnpublishedMultipleResponse { Results = { ["EntityCollection"] = Read(read.Query, true) } };
        }
        /// <summary>已發布來源委派至同一合成資料庫，不做任何寫入或快取。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => Read(query, false);

        /// <summary>一般投影刻意不同，alias 則回完整原始 XML，真實變更只注入未發布原文。</summary>
        private EntityCollection Read(QueryBase query, bool unpublished)
        {
            if (query is QueryExpression { EntityName: "savedquery" }) return new EntityCollection();
            Reads++;
            var row = new Entity("systemform", Id)
            {
                ["formid"] = Id, ["objecttypecode"] = "account", ["name"] = "form",
                ["type"] = new OptionSetValue(2), ["formactivationstate"] = new OptionSetValue(1), ["ismanaged"] = false
            };
            if (query is QueryExpression)
                row["formxml"] = unpublished ? "<form><label description='other-locale'/></form>" : "<form/>";
            else
            {
                Observe?.Invoke(XElement.Parse(Assert.IsType<FetchExpression>(query).Query), unpublished);
                var xml = Changed && unpublished ? Raw.Replace("description='B'", "description='CHANGED'", StringComparison.Ordinal) : Raw;
                if (Fault == "encoding") xml = "\uD800";
                if (Fault != "missing")
                    row["rawformxml"] = new AliasedValue(Fault == "entity" ? "contact" : "systemform",
                        Fault == "attribute" ? "name" : "formxml", xml);
                if (Fault == "collision") row["formxml"] = Raw;
            }
            var result = new EntityCollection([row]);
            return Transform is null ? result : Transform(result);
        }
        /// <summary>寫入不屬本案契約，遇到即失敗。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>單列一般讀取不是此有界兩來源契約。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>所有變更都必須由正式 apply 控制，本假服務禁止更新。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>測試不能以清理為由刪除正式元件。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>本案不建立關聯。</summary>
        public void Associate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本案不解除關聯。</summary>
        public void Disassociate(string entityName, Guid id, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

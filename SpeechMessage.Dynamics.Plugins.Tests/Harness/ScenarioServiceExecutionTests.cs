/*
檔案整體說明

目的與定位：保護 P5-02 PluginHarness 執行域內真正連結的組織服務替身契約，補足直接測試另一組件副本無法證明的整合邊界；本檔合成 Plugin 不代表 PresentRecord 或實機黃金。
主要功能與流程：每次將合成來源交由正式 Roslyn 編譯及 AppDomain runner，使用固定 alias seed 執行查詢、欄位投影、CRUD、typed Execute 與關聯。斷言實際寫入紀錄及固定例外型別，注入重複主列、未知查詢及呼叫端修改，以確認沒有空集合假成功或可變值回寫。
使用邊界與維護：沒有外部舊來源、帳號、憑證、D365、LINE 或持久檔案；測試只保留本次分離的 JSON 結果。每次 runner 是執行域、scratch 與假服務的唯一 owner，finally 卸載及清除，不共用可變案例狀態、連線或背景工作。錯誤必須由結果的 exception 顯示；若基礎設施拒絕整批，Generated 斷言先失敗，不能冒充預期的業務拒絕。
*/
using System;
using LegacyGolden.PluginHarness;
using Newtonsoft.Json.Linq;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Harness;

/// <summary>透過完整離線執行管線確認假服務被正確注入，且讀取、寫入及失敗均可觀測。</summary>
public sealed class ScenarioServiceExecutionTests
{
    private const string Scenarios = """
        {"cases":[{"name":"service","seed":[
        {"alias":"contact-1","entity":"contact","fields":{"name":"same","rank":3,"amount":{"money":12},"state":{"option":2},"parent":{"ref":"account-1","entity":"account"}}},
        {"alias":"contact-2","entity":"contact","fields":{"name":"same","rank":1}},
        {"alias":"account-1","entity":"account","fields":{"name":"parent"}}],
        "trigger":{"type":"Sample","message":"Update","entity":"contact","target":"contact-1","fields":{},"depth":1}}]}
        """;

    /// <summary>QueryExpression 與 FetchXML 的連結、篩選、排序及投影結果一致；修改回傳 Money 不得回寫表。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Harness_ServiceQueries_LinkFilterOrderAndDetachedProjection(bool fetch)
    {
        var query = fetch
            ? """
              QueryBase q=new FetchExpression("<fetch top='1'><entity name='contact'><attribute name='amount'/>"+
              "<filter><condition attribute='rank' operator='between'><value>2</value><value>4</value></condition>"+
              "<condition attribute='state' operator='eq' value='2'/></filter><order attribute='rank' descending='true'/>"+
              "<link-entity name='account' from='accountid' to='parent' alias='p' link-type='outer'>"+
              "<attribute name='name'/><filter><condition attribute='name' operator='like' value='par%'/></filter>"+
              "</link-entity></entity></fetch>");
              """
            : """
              var q=new QueryExpression("contact"){ColumnSet=new ColumnSet("amount"),TopCount=1};
              q.Criteria.AddCondition("rank",ConditionOperator.Between,2,4);
              q.Criteria.AddCondition("state",ConditionOperator.Equal,2);
              q.AddOrder("rank",OrderType.Descending);
              var link=q.AddLink("account","parent","accountid",JoinOperator.LeftOuter);
              link.EntityAlias="p";link.Columns=new ColumnSet("name");
              link.LinkCriteria.AddCondition("name",ConditionOperator.Like,"par%");
              """;
        var row = Run(query + """
            var rows=s.RetrieveMultiple(q).Entities;
            if(rows.Count!=1||rows[0].Id!=target.Id||rows[0].Contains("rank"))throw new InvalidOperationException();
            var value=(Money)rows[0]["amount"];value.Value=99;
            var fresh=s.Retrieve("contact",target.Id,new ColumnSet("amount"));
            s.Update(new Entity("contact",target.Id){["amount"]=(Money)fresh["amount"],
            ["name"]=(string)((AliasedValue)rows[0]["p.name"]).Value,["count"]=rows.Count});
            """);
        var fields = Assert.Single((JArray)row["writes"]!)["fields"]!;
        Assert.Equal(12m, (decimal)fields["amount"]!);
        Assert.Equal("parent", (string?)fields["name"]);
        Assert.Equal(1, (int)fields["count"]!);
    }

    /// <summary>實際執行 typed CRUD 與兩次 Upsert，驗證回應型別、寫入順序、null 刪欄及來源變更隔離。</summary>
    [Fact]
    public void Harness_ServiceTypedCrudAndUpsert_RecordSuccessfulPatchesOnly()
    {
        var row = Run("""
            var input=new Entity("contact"){["name"]="created",["amount"]=new Money(7)};
            var created=(CreateResponse)s.Execute(new CreateRequest{Target=input});
            ((Money)input["amount"]).Value=99;
            var id=created.id;
            var before=(RetrieveResponse)s.Execute(new RetrieveRequest{
            Target=new EntityReference("contact",id),ColumnSet=new ColumnSet("amount")});
            if(((Money)before.Entity["amount"]).Value!=7)throw new InvalidOperationException();
            s.Execute(new UpdateRequest{Target=new Entity("contact",id){["name"]=null}});
            if(s.Retrieve("contact",id,new ColumnSet(true)).Contains("name"))throw new InvalidOperationException();
            var insert=(UpsertResponse)s.Execute(new UpsertRequest{Target=new Entity("contact"){["name"]="upsert"}});
            var update=(UpsertResponse)s.Execute(new UpsertRequest{
            Target=new Entity("contact",insert.Target.Id){["name"]="changed"}});
            if(!insert.RecordCreated||update.RecordCreated)throw new InvalidOperationException();
            s.Execute(new DeleteRequest{Target=new EntityReference("contact",id)});
            s.Delete("contact",insert.Target.Id);
            """);
        var writes = (JArray)row["writes"]!;
        Assert.Equal(6, writes.Count);
        Assert.Equal("Create", (string?)writes[0]!["operation"]);
        Assert.Equal(7m, (decimal)writes[0]!["fields"]!["amount"]!);
        Assert.Equal("created-1", (string?)writes[0]!["alias"]);
        Assert.Equal(JTokenType.Null, writes[1]!["fields"]!["name"]!.Type);
        Assert.Equal("Create", (string?)writes[2]!["operation"]);
        Assert.Equal("created-2", (string?)writes[2]!["alias"]);
        Assert.Equal("Update", (string?)writes[3]!["operation"]);
        Assert.Equal("changed", (string?)writes[3]!["fields"]!["name"]);
        Assert.Equal("Delete", (string?)writes[4]!["operation"]);
        Assert.Equal("created-1", (string?)writes[4]!["alias"]);
        Assert.Empty((JObject)writes[4]!["fields"]!);
        Assert.Equal("Delete", (string?)writes[5]!["operation"]);
    }

    /// <summary>關聯重複及缺失需拒絕；失敗不修改邊，成功解除後可重建；純關聯不製造 CRUD 紀錄。</summary>
    [Fact]
    public void Harness_ServiceAssociations_FailAtomicallyAndRemainReusable()
    {
        var row = Run("""
            var contact=s.Retrieve("contact",target.Id,new ColumnSet("parent"));
            var parent=(EntityReference)contact["parent"];
            var related=new EntityReferenceCollection{parent};var relationship=new Relationship("synthetic");
            s.Associate("contact",target.Id,relationship,related);
            var rejected=false;try{s.Associate("contact",target.Id,relationship,related);}
            catch(InvalidOperationException){rejected=true;}if(!rejected)throw new InvalidOperationException();
            s.Execute(new DisassociateRequest{Target=target.ToEntityReference(),Relationship=relationship,RelatedEntities=related});
            rejected=false;try{s.Disassociate("contact",target.Id,relationship,related);}
            catch(InvalidOperationException){rejected=true;}if(!rejected)throw new InvalidOperationException();
            s.Execute(new AssociateRequest{Target=target.ToEntityReference(),Relationship=relationship,RelatedEntities=related});
            s.Disassociate("contact",target.Id,relationship,related);
            s.Update(new Entity("contact",target.Id){["name"]="associations-verified"});
            """);
        var write = Assert.Single((JArray)row["writes"]!);
        Assert.Equal("associations-verified", (string?)write["fields"]!["name"]);
    }

    /// <summary>兩種查詢連結造成重複正式 ID 時，TopCount 不能掩蓋錯誤，也不可先留下成功寫入。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Harness_ServiceFanOut_RefusesDuplicatePublishedIdentity(bool fetch)
    {
        var query = fetch
            ? """QueryBase q=new FetchExpression("<fetch top='1'><entity name='account'><link-entity name='contact' from='parent' to='accountid'/></entity></fetch>");"""
            : """var q=new QueryExpression("account"){TopCount=1};q.AddLink("contact","accountid","parent");""";
        var document = JObject.Parse(Scenarios);
        document["cases"]![0]!["seed"]![1]!["fields"]!["parent"] =
            new JObject { ["ref"] = "account-1", ["entity"] = "account" };
        var row = Run(query + "s.RetrieveMultiple(q);", document, "InvalidOperationException");
        Assert.Empty((JArray)row["writes"]!);
    }

    /// <summary>未知查詢或訊息必須傳回明確例外；不支援的語法不能因預設假服務而變成成功空集合。</summary>
    [Theory]
    [InlineData("s.RetrieveMultiple(new QueryExpression(\"contact\"){Distinct=true});")]
    [InlineData("s.RetrieveMultiple(new FetchExpression(\"<fetch aggregate='true'><entity name='contact'/></fetch>\"));")]
    [InlineData("s.Execute(new OrganizationRequest(\"UnknownSynthetic\"));")]
    public void Harness_ServiceUnsupportedFeatures_ExposeFailureWithoutWrites(string body)
    {
        var row = Run(body, null, "NotSupportedException");
        Assert.Empty((JArray)row["writes"]!);
    }

    /// <summary>正式管線擁有所有執行資源；先要求 Generated，再斷言 Plugin 例外，避免將編譯拒絕當作測試通過。</summary>
    private static JToken Run(string body, JObject? document = null, string? exception = null)
    {
        var source = """
            using System;using Microsoft.Xrm.Sdk;using Microsoft.Xrm.Sdk.Query;using Microsoft.Xrm.Sdk.Messages;
            public sealed class Sample:IPlugin{public void Execute(IServiceProvider p){
            var context=(IPluginExecutionContext)p.GetService(typeof(IPluginExecutionContext));
            var factory=(IOrganizationServiceFactory)p.GetService(typeof(IOrganizationServiceFactory));
            var s=factory.CreateOrganizationService(context.UserId);var target=(Entity)context.InputParameters["Target"];
            """ + body + "}}";
        var result = GoldenHarness.RunSources(new[] { source }, document?.ToString() ?? Scenarios);
        Assert.True((string?)result["status"] == "Generated", result.ToString());
        var row = result["cases"]![0]!;
        Assert.Equal(exception, row["exception"]!.Value<string>());
        return row;
    }
}

/*
 * 檔案整體說明
 * 目的與定位：P7既有局部choice補選項的離線契約；使用純快照及真結構引擎合成SDK。
 * 主要流程：獨立於磁碟schema定義第四值，舊快照缺值時應只規劃AddOptionValue；
 * engine真送InsertOptionValue、定向發布並重新讀取，第二次零寫入。多餘選項保留。
 * 故障與斷言：核對確切entity/attribute/value/label、已有metadata身分與第二輪寫入數，
 * 不修改既有標籤或刪除其他選項，不放寬型別、安全及未發布差異關卡。
 * 資源與隔離：每案純值及暫存目錄由fixture擁有並於Dispose移除；無網路、秘密或背景工作。
 * 測試僅屬離線SDK證據，9.1選項發布及solution實際行為另於C-b驗收。
 */
using System;
using System.Linq;
using System.IO;
using Microsoft.Xrm.Sdk.Metadata;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>選項新增保持其餘metadata不變的離線合約。</summary>
public sealed class SchemaOptionValueTests
{
    /// <summary>舊服務缺第四值，只新增該值，不重建欄位或改名。</summary>
    [Fact]
    public void Schema_MissingOptionValue_PlansAddOptionValue()
    {
        var old=WithoutRelay(SchemaPlannerTests.Definition());var snapshot=LocalSnapshot(old);
        var definition=WithRelay(old);var plan=SchemaPlanner.Create(definition,snapshot,DateTimeOffset.UtcNow);
        var op=Assert.Single(plan.Operations);Assert.Equal("AddOptionValue",op.Kind);
        Assert.Equal("new_outboundtask",op.Entity);Assert.Equal("new_type",op.Name);
    }
    /// <summary>真正engine往返補選項，已存在的標籤仍原值，第二次apply沒有寫入。</summary>
    [Fact]
    public void Schema_AddOptionValue_TwiceIdempotent()
    {
        using var f=new SchemaEngineTests.Fixture();
        var old = WithoutRelay(f.Definition);
        f.Engine.Apply(f.Engine.Plan(old,default),old,default);
        File.Delete(Path.Combine(f.Directory,"before-metadata.json"));
        var field=(PicklistAttributeMetadata)f.Service.Entities.Single(x=>x.LogicalName=="new_outboundtask").Attributes.Single(x=>x.LogicalName=="new_type");
        field.OptionSet.Options[0].Label.LocalizedLabels[0].Label="server-owned-label";
        var definition=WithRelay(f.Definition);
        var engine=new SchemaDeploymentEngine(f.Service,Path.Combine(f.Directory,"add-option"),new SchemaEngineTests.Clock());
        Assert.Empty(engine.Apply(engine.Plan(definition,default),definition,default).Operations);
        Assert.Contains(field.OptionSet.Options,x=>x.Value==100000003);
        Assert.Equal("server-owned-label",field.OptionSet.Options[0].Label.LocalizedLabels[0].Label);
        var writes=f.Service.WriteCount;
        var second=new SchemaDeploymentEngine(f.Service,Path.Combine(f.Directory,"second-option"),new SchemaEngineTests.Clock());
        Assert.Empty(second.Apply(second.Plan(definition,default),definition,default).Operations);Assert.Equal(writes,f.Service.WriteCount);
    }
    /// <summary>server有其他選項不刪、不改；只增加缺少值。</summary>
    [Fact]
    public void Schema_ExtraOptionOnServer_NotRemoved()
    {
        var definition=SchemaPlannerTests.Definition();var snapshot=SchemaPlannerTests.CompleteSnapshot(definition);
        snapshot=snapshot with { Entities=snapshot.Entities.Select(e=>e.LogicalName=="new_outboundtask" ? e with { Fields=e.Fields.Select(f=>f.LogicalName=="new_type" ? f with { Options=f.Options.Append(100000099).ToArray() } : f).ToArray() } : e).ToArray() };
        Assert.Empty(SchemaPlanner.Create(definition,snapshot,DateTimeOffset.UtcNow).Operations);
        Assert.Contains(100000099,snapshot.Entities.Single(e=>e.LogicalName=="new_outboundtask").Fields.Single(f=>f.LogicalName=="new_type").Options);
    }
    /// <summary>全域或未知choice不可由局部欄位新增訊息修改，必須在plan前拒絕。</summary>
    [Theory]
    [InlineData(true)] [InlineData(null)]
    public void Schema_GlobalOrUnknownChoiceMissingValue_Refuses(bool? global)
    {
        var old = WithoutRelay(SchemaPlannerTests.Definition()); var snapshot = LocalSnapshot(old);
        snapshot = snapshot with { Entities = snapshot.Entities.Select(e => e.LogicalName == "new_outboundtask" ? e with
        { Fields = e.Fields.Select(f => f.LogicalName == "new_type" ? f with { ChoiceIsGlobal = global } : f).ToArray() } : e).ToArray() };
        Assert.Throws<InvalidDataException>(() => SchemaPlanner.Create(WithRelay(old), snapshot, DateTimeOffset.UtcNow));
    }
    /// <summary>缺值測試明確提供局部scope，不把缺metadata當可修改的選項組。</summary>
    private static SchemaSnapshot LocalSnapshot(SchemaDefinition definition)
    {
        var snapshot = SchemaPlannerTests.CompleteSnapshot(definition);
        return snapshot with { Entities = snapshot.Entities.Select(e => e with
        { Fields = e.Fields.Select(f => f.Type == "choice" ? f with { ChoiceIsGlobal = false } : f).ToArray() }).ToArray() };
    }
    /// <summary>明確建構三選項舊版本，不因磁碟清單已新增第四值而失去缺值故障。</summary>
    private static SchemaDefinition WithoutRelay(SchemaDefinition definition) => definition with
    {
        Entities=definition.Entities.Select(e=>e.LogicalName=="new_outboundtask" ? e with { Fields=e.Fields.Select(f=>f.LogicalName=="new_type" ? f with { Options=f.Options.Where(x=>x.Value!=100000003).ToArray() } : f).ToArray() } : e).ToArray()
    };
    /// <summary>測試獨立指定第四正式值，不讀被測磁碟清單生成缺值答案。</summary>
    private static SchemaDefinition WithRelay(SchemaDefinition definition) => definition with
    {
        Entities=definition.Entities.Select(e=>e.LogicalName=="new_outboundtask" ? e with { Fields=e.Fields.Select(f=>f.LogicalName=="new_type" ? f with { Options=f.Options.Where(x=>x.Value!=100000003).Append(new SchemaOption(100000003,"MemberNotifyRelay")).ToArray() } : f).ToArray() } : e).ToArray()
    };
}

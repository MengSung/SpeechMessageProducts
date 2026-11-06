using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>純snapshot比對守門，不呼叫SDK；故障逐一改變正式型別/約束，驗證計畫不會將衝突轉成自動修改。</summary>
public sealed class SchemaPlannerTests
{
    /// <summary>沒有solution而只有既有基礎表時只新增四表、32非主名稱欄與四key；不能建立account/contact或P4欄。</summary>
    [Fact]
    public void Plan_OnlyBaseEntities_ContainsOnlyRequiredP1Additions()
    {
        var definition = Definition();
        var complete = CompleteSnapshot(definition);
        var empty = complete with
        {
            Entities = complete.Entities.Where(entity => entity.LogicalName is "account" or "contact")
                .Select(entity => entity with { Fields = Array.Empty<SchemaFieldState>(), Keys = Array.Empty<SchemaKeyState>() }).ToArray(),
            Solutions = Array.Empty<SchemaSolutionState>(), Publishers = Array.Empty<SchemaPublisherState>(), Components = Array.Empty<SchemaComponentState>()
        };
        var plan = SchemaPlanner.Create(definition, empty, Now);
        Assert.Equal(4, plan.Operations.Count(operation => operation.Kind == "CreateEntity"));
        Assert.Equal(32, plan.Operations.Count(operation => operation.Kind is "CreateAttribute" or "CreateLookup"));
        Assert.Equal(4, plan.Operations.Count(operation => operation.Kind == "CreateKey"));
        Assert.DoesNotContain(plan.Operations, operation => operation.Name == "new_sinopacprofile");
        Assert.DoesNotContain(plan.Operations, operation => operation.Kind is "Update" or "Delete");
        Assert.Equal(64, plan.Sha256.Length);
    }

    /// <summary>完整一致且所有元件已納入solution時不得再寫；來源候選只屬本案，不會因規劃而被修改。</summary>
    [Fact]
    public void Plan_CompleteSnapshot_HasNoOperationsAndVerifies()
    {
        var definition = Definition();
        var snapshot = CompleteSnapshot(definition);
        var plan = SchemaPlanner.Create(definition, snapshot, Now);
        Assert.Empty(plan.Operations);
        Assert.Empty(plan.PendingKeys);
        SchemaPlanner.ValidatePlan(plan, definition, snapshot);
    }

    /// <summary>任何型別/長度/日期/security/owner/audit/concurrency或key衝突都停止，不產生Update、Delete或替代欄位來掩蓋。</summary>
    /// <param name="fault">獨立注入的metadata約束。</param>
    [Theory]
    [InlineData("type")]
    [InlineData("length")]
    [InlineData("date")]
    [InlineData("secured")]
    [InlineData("ownership")]
    [InlineData("audit")]
    [InlineData("field-audit")]
    [InlineData("concurrency")]
    [InlineData("key")]
    [InlineData("managed-solution")]
    public void Plan_ExistingConflict_FailsClosed(string fault)
    {
        var definition = Definition();
        var snapshot = CompleteSnapshot(definition);
        var entities = snapshot.Entities.ToArray();
        var targetName = fault is "ownership" or "audit" or "field-audit" ? "new_localizedtext"
            : fault == "concurrency" ? "new_outboundtask" : "account";
        var index = Array.FindIndex(entities, entity => entity.LogicalName == targetName);
        var target = entities[index];
        var fields = target.Fields.ToArray();
        switch (fault)
        {
            case "type": fields[0] = fields[0] with { Type = "integer" }; break;
            case "length": fields[0] = fields[0] with { MaxLength = 999 }; break;
            case "date": var date = Array.FindIndex(fields, field => field.Type == "datetime"); fields[date] = fields[date] with { Behavior = "DateOnly" }; break;
            case "secured": fields[0] = fields[0] with { Secured = true }; break;
            case "ownership": target = target with { Ownership = "UserOwned" }; break;
            case "audit": target = target with { Audit = false }; break;
            case "field-audit": fields[0] = fields[0] with { Audit = false }; break;
            case "concurrency": target = target with { OptimisticConcurrency = false }; break;
            case "key": var keyEntity = Array.FindIndex(entities, entity => entity.Keys.Count > 0); entities[keyEntity] = entities[keyEntity] with { Keys = new[] { entities[keyEntity].Keys[0] with { Attributes = new[] { "new_name" } } } }; break;
            case "managed-solution": snapshot = snapshot with { Solutions = new[] { snapshot.Solutions[0] with { Managed = true } } }; break;
        }
        entities[index] = target with { Fields = fields };
        snapshot = snapshot with { Entities = entities };
        Assert.Throws<InvalidDataException>(() => SchemaPlanner.Create(definition, snapshot, Now));
    }

    /// <summary>pending key不是可驗收完成；failed key不能自動重建；已存在key不論狀態都不可再CreateKey。</summary>
    [Fact]
    public void Plan_PendingAndFailedKeys_NeverPretendActiveOrRecreate()
    {
        var definition = Definition();
        var snapshot = CompleteSnapshot(definition);
        var entities = snapshot.Entities.ToArray();
        var index = Array.FindIndex(entities, entity => entity.Keys.Count > 0);
        var key = entities[index].Keys[0];
        entities[index] = entities[index] with { Keys = new[] { key with { Status = "Pending" } } };
        var pending = SchemaPlanner.Create(definition, snapshot with { Entities = entities }, Now);
        Assert.Empty(pending.Operations);
        Assert.Single(pending.PendingKeys);
        entities[index] = entities[index] with { Keys = new[] { key with { Status = "Failed" } } };
        Assert.Throws<InvalidDataException>(() => SchemaPlanner.Create(definition, snapshot with { Entities = entities }, Now));
    }

    /// <summary>plan竄改、schema變更或組織metadata漂移全在寫入前拒絕；相同alias的另一org ID亦不能套用。</summary>
    [Fact]
    public void ValidatePlan_TamperingAndScopeDrift_AreRejected()
    {
        var definition = Definition();
        var snapshot = CompleteSnapshot(definition);
        var plan = SchemaPlanner.Create(definition, snapshot, Now);
        Assert.Throws<InvalidDataException>(() => SchemaPlanner.ValidatePlan(plan with { Sha256 = new string('0', 64) }, definition, snapshot));
        Assert.Throws<InvalidDataException>(() => SchemaPlanner.ValidatePlan(plan, definition, snapshot with { OrganizationId = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => SchemaPlanner.ValidatePlan(plan, definition, snapshot with { Version = "9.1.1.0" }));
    }

    /// <summary>固定UTC計畫時刻，不讀取本機時間、時區或文化。</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    /// <summary>同worktree唯一正式schema，每案重新讀取，避免mutable清單跨案例共享。</summary>
    internal static SchemaDefinition Definition() => SchemaFiles.Load(SchemaDefinitionTests.SchemaPath());

    /// <summary>建立本案獨佔的完整純metadata候選；不代表SDK建立成功，實際SDK契約由整合案例另驗。</summary>
    internal static SchemaSnapshot CompleteSnapshot(SchemaDefinition definition)
    {
        var entities = definition.Entities.Select(entity => new SchemaEntityState(Guid.NewGuid(), entity.LogicalName,
            entity.LogicalName + "id", entity.IsNew ? entity.PrimaryName : "name", "OrganizationOwned", entity.Audit,
            entity.OptimisticConcurrency, false,
            entity.Fields.Where(field => field.Phase == 1).Select(field => new SchemaFieldState(Guid.NewGuid(), field.LogicalName,
                field.Type, field.MaxLength == 0 ? null : field.MaxLength, field.Required ? "ApplicationRequired" : "None", field.Secured,
                field.Target.Length == 0 ? Array.Empty<string>() : new[] { field.Target }, field.Behavior, field.Format,
                field.Options.Select(option => option.Value).ToArray(), false, entity.Audit)).ToArray(),
            entity.Keys.Select(key => new SchemaKeyState(Guid.NewGuid(), key.LogicalName, key.Attributes.ToArray(), "Active")).ToArray())).ToArray();
        var publisher = new SchemaPublisherState(Guid.NewGuid(), "SpeechMessagePublisher", "new");
        var solution = new SchemaSolutionState(Guid.NewGuid(), "SpeechMessageCore", false, publisher.Id);
        var components = new List<SchemaComponentState>();
        foreach (var entity in entities)
        {
            components.Add(new SchemaComponentState(Guid.NewGuid(), 1, entity.Id));
            components.AddRange(entity.Fields.Select(field => new SchemaComponentState(Guid.NewGuid(), 2, field.Id)));
            components.AddRange(entity.Keys.Select(key => new SchemaComponentState(Guid.NewGuid(), 14, key.Id)));
        }
        return new SchemaSnapshot { OrganizationId = Guid.NewGuid(), Organization = "goodshepherdback", Version = "9.1.0.0", LanguageCode = 1028,
            Entities = entities, Publishers = new[] { publisher }, Solutions = new[] { solution }, Components = components.ToArray() };
    }
}

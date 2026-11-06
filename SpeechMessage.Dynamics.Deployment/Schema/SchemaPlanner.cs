using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>不含SDK或I/O的完整結構規劃；先檢查全部衝突再回傳候選，絕不產生Update/Delete來修補既有組織。</summary>
internal static class SchemaPlanner
{
    /// <summary>將已授權組織snapshot與正式schema作完整比較，產生依賴順序穩定的純新增計畫。</summary>
    /// <param name="definition">已完整驗證的SSOT定義。</param><param name="snapshot">本次server完整投影，不得代以其他組織。</param>
    /// <param name="createdUtc">Host提供明確UTC時刻，純函式不讀取系統時鐘。</param>
    /// <returns>只含新增／加入solution及pending key的完整候選。</returns>
    internal static SchemaPlan Create(SchemaDefinition definition, SchemaSnapshot snapshot, DateTimeOffset createdUtc)
    {
        SchemaFiles.Validate(definition);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (createdUtc.Offset != TimeSpan.Zero || snapshot.OrganizationId == Guid.Empty
            || !string.Equals(snapshot.Organization, "goodshepherdback", StringComparison.OrdinalIgnoreCase)
            || snapshot.LanguageCode <= 0 || snapshot.Entities.Count > 4096)
            throw new InvalidDataException("Schema plan scope or UTC time is invalid.");
        var operations = new List<SchemaOperation>();
        var includes = new List<SchemaOperation>();
        var pending = new List<string>();
        var solution = snapshot.Solutions.SingleOrDefault();
        if (solution is not null && (solution.Name != definition.Solution || solution.Managed)) Conflict("solution");
        var publisher = solution is not null ? snapshot.Publishers.SingleOrDefault(item => item.Id == solution.PublisherId)
            : snapshot.Publishers.SingleOrDefault(item => item.Name == definition.Publisher);
        if (solution is not null && publisher is null || publisher is not null && publisher.Prefix != definition.Prefix) Conflict("publisher");
        if (publisher is null)
        {
            var compatible = snapshot.Publishers.Where(item => item.Prefix == definition.Prefix).ToArray();
            if (compatible.Length > 1) Conflict("publisher identity");
            publisher = compatible.SingleOrDefault();
        }
        if (publisher is null) operations.Add(new SchemaOperation("CreatePublisher", "publisher", definition.Publisher, Guid.Empty));
        if (solution is null) operations.Add(new SchemaOperation("CreateSolution", "solution", definition.Solution, Guid.Empty));
        var components = snapshot.Components.Select(item => (item.Type, item.ObjectId)).ToHashSet();
        if (components.Count != snapshot.Components.Count) Conflict("duplicate component identity");
        var entities = snapshot.Entities.ToDictionary(entity => entity.LogicalName, StringComparer.Ordinal);
        foreach (var expected in definition.Entities)
        {
            entities.TryGetValue(expected.LogicalName, out var existing);
            if (existing is null)
            {
                if (!expected.IsNew) Conflict(expected.LogicalName);
                operations.Add(new SchemaOperation("CreateEntity", expected.LogicalName, expected.PrimaryName, Guid.Empty));
            }
            else if (expected.IsNew && (existing.Ownership != "OrganizationOwned" || existing.PrimaryName != expected.PrimaryName
                || existing.Audit != expected.Audit
                || expected.OptimisticConcurrency.HasValue && existing.OptimisticConcurrency != expected.OptimisticConcurrency || existing.Managed != false))
                Conflict(expected.LogicalName);
            if (existing is null || !components.Contains((1, existing.Id)))
                includes.Add(new SchemaOperation("IncludeEntity", expected.LogicalName, expected.LogicalName, existing?.Id ?? Guid.Empty));
            foreach (var field in expected.Fields.Where(field => field.Phase == 1))
            {
                var actual = existing?.Fields.SingleOrDefault(item => item.LogicalName == field.LogicalName);
                if (actual is null)
                {
                    if (existing is not null || field.LogicalName != expected.PrimaryName)
                        operations.Add(new SchemaOperation(field.Type == "lookup" ? "CreateLookup" : "CreateAttribute", expected.LogicalName, field.LogicalName, Guid.Empty));
                }
                else if (!Matches(field, actual, expected.IsNew && expected.Audit)) Conflict(expected.LogicalName + "." + field.LogicalName);
                if (actual is null || !components.Contains((2, actual.Id)))
                    includes.Add(new SchemaOperation("IncludeAttribute", expected.LogicalName, field.LogicalName, actual?.Id ?? Guid.Empty));
            }
            foreach (var key in expected.Keys)
            {
                var actual = existing?.Keys.SingleOrDefault(item => item.LogicalName == key.LogicalName);
                if (actual is null)
                    operations.Add(new SchemaOperation("CreateKey", expected.LogicalName, key.LogicalName, Guid.Empty));
                else
                {
                    if (!actual.Attributes.SequenceEqual(key.Attributes, StringComparer.Ordinal) || actual.Status is not ("Active" or "Pending" or "InProgress"))
                        Conflict(expected.LogicalName + "." + key.LogicalName);
                    if (actual.Status != "Active") pending.Add(expected.LogicalName + "." + key.LogicalName);
                }
                if (actual is null || !components.Contains((14, actual.Id)))
                    includes.Add(new SchemaOperation("IncludeKey", expected.LogicalName, key.LogicalName, actual?.Id ?? Guid.Empty));
            }
        }
        // 建立順序必須先所有表，再一般欄／lookup／key，最後納入solution；不靠JSON列順序碰巧滿足lookup依賴。
        var ordered = operations.OrderBy(operation => Order(operation.Kind)).Concat(includes).ToArray();
        var plan = new SchemaPlan
        {
            OrganizationId = snapshot.OrganizationId, PublisherId = publisher?.Id ?? Guid.Empty, Organization = snapshot.Organization, CreatedUtc = createdUtc,
            Fingerprint = SchemaFiles.HashValue(snapshot), SchemaHash = SchemaFiles.Hash(definition),
            Operations = Array.AsReadOnly(ordered), PendingKeys = Array.AsReadOnly(pending.ToArray())
        };
        return plan with { Sha256 = SchemaFiles.HashValue(plan) };
    }

    /// <summary>apply在備份／寫入之前重新讀server並重算完整預期計畫，拒絕任何欄位、操作、scope或hash漂移。</summary>
    internal static void ValidatePlan(SchemaPlan plan, SchemaDefinition definition, SchemaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.SchemaVersion != 1 || plan.Operations is null || plan.PendingKeys is null || plan.Operations.Count > 20_000
            || plan.Sha256 != SchemaFiles.HashValue(plan with { Sha256 = "" }))
            throw new InvalidDataException("Schema plan integrity validation failed.");
        var expected = Create(definition, snapshot, plan.CreatedUtc);
        if (plan.Sha256 != expected.Sha256) throw new InvalidDataException("Schema plan no longer matches the definition or organization metadata.");
    }

    /// <summary>逐一核對功能約束；null的server政策不當false，不能把managed/未知安全或日期行為當相容。</summary>
    private static bool Matches(SchemaField expected, SchemaFieldState actual, bool requireAudit) => actual.Type == expected.Type
        && actual.Managed == false && actual.Secured == expected.Secured
        && (!requireAudit || actual.Audit == true)
        && actual.Required == (expected.Required ? "ApplicationRequired" : "None")
        && (expected.MaxLength == 0 || actual.MaxLength == expected.MaxLength)
        && (expected.Type != "lookup" || actual.Targets.Count == 1 && actual.Targets[0] == expected.Target)
        && (expected.Type != "datetime" || actual.Behavior == expected.Behavior && actual.Format == expected.Format)
        && (expected.Type != "choice" || actual.Options.SequenceEqual(expected.Options.Select(option => option.Value).Order()));

    /// <summary>固定operation依賴表；未知動詞拒絕，不能透過plan注入任意SDK操作。</summary>
    private static int Order(string kind) => kind switch
    {
        "CreatePublisher" => 0, "CreateSolution" => 1, "CreateEntity" => 2,
        "CreateAttribute" => 3, "CreateLookup" => 4, "CreateKey" => 5,
        _ => throw new InvalidDataException("Unsupported schema operation.")
    };
    /// <summary>只輸出已驗證的schema身份，不帶SDK原始訊息、欄位本文或使用者資料；呼叫端必須停止整次寫入。</summary>
    private static void Conflict(string identity) => throw new InvalidDataException("Existing schema metadata conflicts with the definition: " + identity);
}

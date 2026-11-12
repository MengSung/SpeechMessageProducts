/*
 * 檔案整體說明
 *
 * 目的與定位：本檔是無I/O的結構差異與完整性核心，依正式SchemaDefinition的ActivePhase及一份完整server快照建立只新增計畫。
 * 主要功能：核對唯一組織、solution未受管理、publisher前綴、既有新表ownership/audit/concurrency，以及目前作用階段內欄位與索引的完整功能約束。
 * 所有衝突先停止整份候選；不得產生Update/Delete、改欄位型別、關閉安全或用新欄位掩蓋既有衝突。
 * 流程與輸出：新增依publisher→solution→所有表→一般欄→lookup→key排序，最後列需要加入solution的正式元件；獨立列出尚未Active的key。
 * 元件包含只承認精確正式ID的顯式列，或同表根明確Include Subcomponents；未知與外殼不得掩蓋缺漏，功能約束仍完整核對。
 * 以schema hash及包含server組織ID的metadata fingerprint建立plan hash；ValidatePlan重算即時完整預期，任何範圍、操作或定義漂移都拒絕。
 * 隔離與生命週期：只使用本次方法區域集合，不讀系統時鐘、不連SDK、不存credential／request、不持有背景工作或跨命令結果。
 * 重要限制：傳入快照必須由完整只讀投影取得；值物件、plan hash與白名單alias都不取代server授權、寫鎖或實機備份。
 * 維護驗證：SchemaPlannerTests逐一注入型別、長度、日期、安全、owner、audit、concurrency、key與managed衝突，並驗零差異、竄改與pending狀態。
 */
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
        // prefix不是publisher正式身份：多個舊publisher可合法共用new。
        // 沒有本產品solution或清單指定名稱時，只規劃建立自己的publisher，不猜選任一舊產品發行者。
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
            // D365新表根元件可包含所有子元件而沒有逐筆solutioncomponent列。
            // 只接受同一正式entity ID、確為根且官方值0；未知／不包含／僅外殼均仍要求精確子元件身份。
            var includesChildren = existing is not null && snapshot.Components.Any(component =>
                component.Type == 1 && component.ObjectId == existing.Id && component.RootBehavior == 0 && component.RootId is null);
            foreach (var field in expected.Fields.Where(field => field.Phase <= definition.ActivePhase))
            {
                var actual = existing?.Fields.SingleOrDefault(item => item.LogicalName == field.LogicalName);
                if (actual is null)
                {
                    if (existing is not null || field.LogicalName != expected.PrimaryName)
                        operations.Add(new SchemaOperation(field.Type == "lookup" ? "CreateLookup" : "CreateAttribute", expected.LogicalName, field.LogicalName, Guid.Empty));
                }
                else if (!Matches(field, actual, expected.IsNew && expected.Audit)) Conflict(expected.LogicalName + "." + field.LogicalName);
                if (actual is null || !includesChildren && !components.Contains((2, actual.Id)))
                    includes.Add(new SchemaOperation("IncludeAttribute", expected.LogicalName, field.LogicalName, actual?.Id ?? Guid.Empty));
            }
            foreach (var key in expected.Keys)
            {
                var actual = existing?.Keys.SingleOrDefault(item => item.LogicalName == key.LogicalName);
                if (actual is null)
                    operations.Add(new SchemaOperation("CreateKey", expected.LogicalName, key.LogicalName, Guid.Empty));
                else
                {
                    // 複合唯一鍵的語意是完整欄位集合；D3659.1會重排回應順序。排序後仍精確比較數量與每個名稱，缺欄／重複／替換不會通過。
                    if (!actual.Attributes.Order(StringComparer.Ordinal).SequenceEqual(key.Attributes.Order(StringComparer.Ordinal), StringComparer.Ordinal) || actual.Status is not ("Active" or "Pending" or "InProgress"))
                        Conflict(expected.LogicalName + "." + key.LogicalName);
                    if (actual.Status != "Active") pending.Add(expected.LogicalName + "." + key.LogicalName);
                }
                if (actual is null || !includesChildren && !components.Contains((14, actual.Id)))
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

/*
 * 檔案整體說明
 *
 * 目的與定位：DeploymentEngine 第二版清單的活動與具名映像註冊，與既有引擎共用
 * 本命令租約、白名單及核准計畫。輸入為已驗證清單，輸出為正式註冊 metadata。
 * 主要功能與流程：依正式組件／型別／步驟 lookup 找到唯一父列；具名映像保留原 ID
 * 更新，只刪除所屬步驟中清單已移除的映像。活動設定群組與顯示名稱，不建立步驟。
 * 使用邊界與維護：不讀業務記錄、不解析秘密、不取得 client 所有權；候選集合僅屬
 * 當次呼叫，無 static、快取、背景工作或獨立資源。任一身分衝突拒絕後續寫入；
 * plan 指紋與套用前備份由主檔負責，部分寫入失敗須由同一包的 rollback 還原。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>同一命令核心的註冊分部，借用主檔的租約服務，不建立跨命令狀態。</summary>
internal sealed partial class DeploymentEngine
{
    /// <summary>
    /// 在規劃及套用前，以即時 metadata 核對 filters／images 的欄位存在性；不查業務記錄。
    /// 每個實體只在本次呼叫讀一次，集合上限四千欄；不快取到命令或組織之外。
    /// </summary>
    private void ValidateRegistrationAttributes(RegistrationManifest manifest)
    {
        foreach (var group in manifest.Steps.GroupBy(step => step.Entity, StringComparer.Ordinal))
        {
            var required = group.SelectMany(step => step.FilteringAttributes.Concat(step.Images.SelectMany(image => image.Attributes)))
                .ToHashSet(StringComparer.Ordinal);
            if (required.Count == 0) continue;
            var response = service.Execute(new RetrieveEntityRequest { LogicalName = group.Key,
                EntityFilters = EntityFilters.Attributes, RetrieveAsIfPublished = true }) as RetrieveEntityResponse;
            var metadata = response?.EntityMetadata;
            if (metadata is null || metadata.LogicalName != group.Key || metadata.Attributes is null || metadata.Attributes.Length > 4096)
                throw new InvalidDataException("實體欄位 metadata 不完整。");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var attribute in metadata.Attributes)
                if (attribute is null || string.IsNullOrWhiteSpace(attribute.LogicalName) || !names.Add(attribute.LogicalName))
                    throw new InvalidDataException("實體欄位 metadata 身分不唯一。");
            if (!required.IsSubsetOf(names)) throw new InvalidDataException("篩選或映像欄位不存在於實體 metadata。");
        }
    }

    /// <summary>只回復匯出步驟所屬映像及活動 metadata；先驗父身份，不對其他組件新增或刪除資料。</summary>
    private void RestoreRegistrationMetadata(DeploymentExport export, Guid assemblyId)
    {
        var types = ReadPluginTypes(assemblyId);
        foreach (var previous in export.Types)
        {
            var current = types.SingleOrDefault(type => type.Id == previous.Id)
                ?? throw new InvalidDataException("活動回復需要原型別 ID。");
            if (current.GetAttributeValue<string>("friendlyname") != previous.FriendlyName ||
                current.GetAttributeValue<string>("workflowactivitygroupname") != previous.ActivityGroup)
                service.Update(new Entity(TypeLogicalName, current.Id) { ["friendlyname"] = previous.FriendlyName,
                    ["workflowactivitygroupname"] = previous.ActivityGroup, ["name"] = previous.Name });
        }
        foreach (var step in export.Steps)
        {
            var current = ReadImages(step.Id);
            foreach (var previous in export.Images.Where(image => image.StepId == step.Id))
            {
                var row = new Entity(ImageLogicalName, previous.Id)
                {
                    ["name"] = previous.Name, ["entityalias"] = previous.Alias, ["imagetype"] = new OptionSetValue(previous.Type),
                    ["attributes"] = previous.Attributes, ["messagepropertyname"] = previous.MessageProperty
                };
                if (current.Any(image => image.Id == previous.Id)) service.Update(row);
                else
                {
                    row["sdkmessageprocessingstepid"] = new EntityReference(StepLogicalName, step.Id);
                    service.Create(row);
                }
            }
            foreach (var row in current)
                if (!export.Images.Any(previous => previous.Id == row.Id)) service.Delete(ImageLogicalName, row.Id);
        }
    }

    /// <summary>活動以型別身份保留原 ID 更新；不存在時只建立 plugintype，不建立 Plugin step。</summary>
    /// <param name="typeName">核准操作中的完整型別名稱。</param><param name="manifest">相同來源清單。</param>
    private void WriteActivity(string typeName, RegistrationManifest manifest)
    {
        var activity = manifest.Activities.Single(item => item.Type == typeName);
        var assembly = FindAssembly(manifest.AssemblyName) ?? throw new InvalidDataException("活動所屬組件不存在。");
        var existing = ReadPluginTypes(assembly.Id).SingleOrDefault(type => type.GetAttributeValue<string>("typename") == typeName);
        var row = new Entity(TypeLogicalName, existing?.Id ?? Guid.Empty)
        {
            ["name"] = activity.DisplayName, ["friendlyname"] = activity.DisplayName,
            ["workflowactivitygroupname"] = manifest.ActivityGroup
        };
        if (existing is not null) service.Update(row);
        else
        {
            row["typename"] = typeName;
            row["pluginassemblyid"] = assembly.ToEntityReference();
            service.Create(row);
        }
    }

    /// <summary>讀取單一步驟的映像白名單 metadata；空白／重複 ID 或名稱衝突一律拒絕。</summary>
    /// <param name="stepId">已驗證父步驟正式 ID。</param><returns>只屬本次操作的 SDK 候選。</returns>
    private Entity[] ReadImages(Guid stepId)
    {
        var rows = RequireUniqueRowsById(service.RetrieveMultiple(new QueryExpression(ImageLogicalName)
        {
            ColumnSet = new ColumnSet("name", "entityalias", "imagetype", "attributes", "messagepropertyname", "sdkmessageprocessingstepid"),
            Criteria = Equal("sdkmessageprocessingstepid", stepId)
        }).Entities, "Plugin 映像");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
            if (ReadReferenceId(row, "sdkmessageprocessingstepid") != stepId ||
                string.IsNullOrWhiteSpace(row.GetAttributeValue<string>("name")) || !names.Add(row.GetAttributeValue<string>("name")))
                throw new InvalidDataException("映像父步驟或名稱不唯一。");
        return rows;
    }

    /// <summary>以名稱及完整 metadata 比較，欄位順序不影響等價，不允許映像數量被默默忽略。</summary>
    /// <param name="current">本次讀取候選。</param><param name="step">核准步驟。</param><returns>完全相符才為 true。</returns>
    private static bool ImagesMatch(Entity[] current, StepRegistration step) =>
        current.Length == step.Images.Count && step.Images.All(desired => current.Any(row => ImageMatches(row, desired, step.Message)));

    /// <summary>比對映像名稱、別名、種類、欄位及正式訊息屬性，避免相同名稱掩蓋擷取範圍變化。</summary>
    private static bool ImageMatches(Entity row, RegistrationImage desired, string message) =>
        row.GetAttributeValue<string>("name") == desired.Name && row.GetAttributeValue<string>("entityalias") == desired.Name &&
        ReadOption(row, "imagetype") == (desired.Type == "PreImage" ? 0 : 1) &&
        row.GetAttributeValue<string>("messagepropertyname") == ImageMessageProperty(message) &&
        string.Equals(row.GetAttributeValue<string>("attributes"), string.Join(",", desired.Attributes.OrderBy(name => name, StringComparer.Ordinal)), StringComparison.Ordinal);

    /// <summary>Create 的實體 ID 位於 Id；Update／Delete 使用 Target，不允許輸入任意 SDK 屬性。</summary>
    private static string ImageMessageProperty(string message) => message == "Create" ? "Id" : "Target";

    /// <summary>依穩定步驟身份取得唯一資料庫列；不存在或衝突均在映像寫入前拒絕。</summary>
    private Entity RegisteredStep(RegistrationManifest manifest, StepRegistration registration)
    {
        var assembly = FindAssembly(manifest.AssemblyName) ?? throw new InvalidDataException("組件不存在。");
        var type = ReadPluginTypes(assembly.Id).Single(row => row.GetAttributeValue<string>("typename") == registration.Type);
        var message = FindMessage(registration.Message) ?? throw new InvalidDataException("訊息不存在。");
        var filter = FindMessageFilter(message.Id, registration.Entity) ?? throw new InvalidDataException("篩選器不存在。");
        return FindStep(type.Id, message.Id, filter.Id, ReadStage(registration.Stage), ReadMode(registration.Mode), registration.Rank)
            ?? throw new InvalidDataException("步驟不存在。");
    }

    /// <summary>只同步已核准步驟下的具名映像，更新保留 ID；不刪除其他步驟或組件的資料。</summary>
    private void ReconcileImages(string key, RegistrationManifest manifest)
    {
        var registration = FindStepRegistration(manifest, key);
        var step = RegisteredStep(manifest, registration);
        var current = ReadImages(step.Id);
        foreach (var desired in registration.Images)
        {
            var existing = current.SingleOrDefault(row => row.GetAttributeValue<string>("name") == desired.Name);
            if (existing is not null && ImageMatches(existing, desired, registration.Message)) continue;
            var row = new Entity(ImageLogicalName, existing?.Id ?? Guid.Empty)
            {
                ["name"] = desired.Name, ["entityalias"] = desired.Name,
                ["imagetype"] = new OptionSetValue(desired.Type == "PreImage" ? 0 : 1),
                ["attributes"] = string.Join(",", desired.Attributes.OrderBy(name => name, StringComparer.Ordinal)),
                ["messagepropertyname"] = ImageMessageProperty(registration.Message)
            };
            if (existing is null)
            {
                row["sdkmessageprocessingstepid"] = step.ToEntityReference();
                service.Create(row);
            }
            else service.Update(row);
        }
        foreach (var row in current)
            if (!registration.Images.Any(desired => desired.Name == row.GetAttributeValue<string>("name"))) service.Delete(ImageLogicalName, row.Id);
    }

    /// <summary>把映像 metadata 轉成安全純值快照；不持有 SDK 參照或實體欄位值。</summary>
    private static PluginImageExport ToImageExport(Entity row) => new(row.Id,
        ReadReferenceId(row, "sdkmessageprocessingstepid") ?? throw new InvalidDataException("映像缺少父步驟。"),
        row.GetAttributeValue<string>("name") ?? "", row.GetAttributeValue<string>("entityalias") ?? "", ReadOption(row, "imagetype"),
        row.GetAttributeValue<string>("attributes") ?? "", row.GetAttributeValue<string>("messagepropertyname") ?? "");
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace SpeechMessage.Dynamics.Deployment.Schema;

/// <summary>單命令借用SDK的只新增引擎；不持有跨命令資料、client所有權或機密，所有檔案在操作內flush/close，原錯誤往共用終端傳遞。</summary>
internal sealed class SchemaDeploymentEngine
{
    private readonly IOrganizationService service;
    private readonly string directory;
    private readonly TimeProvider clock;
    /// <summary>使用既有命令lease與固定輸出根，不自建client或服務容器；caller必須先取得同組織寫鎖與合法授權。</summary>
    internal SchemaDeploymentEngine(IOrganizationService service, string directory, TimeProvider clock)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.directory = Path.GetFullPath(directory);
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>完整只讀server後交純planner，不讀Plugin DLL或業務資料；冷失敗向上保留原Exception。</summary>
    internal SchemaPlan Plan(SchemaDefinition definition, CancellationToken cancellationToken) =>
        SchemaPlanner.Create(definition, SchemaMetadata.Read(service, cancellationToken), clock.GetUtcNow());

    /// <summary>只輸出完整metadata候選，以CreateNew防覆寫，不把此結構snapshot冒稱組織資料庫備份。</summary>
    internal string Export(CancellationToken cancellationToken) =>
        SchemaFiles.WriteNew(directory, "metadata.json", SchemaMetadata.Read(service, cancellationToken));

    /// <summary>先即時重驗全部plan、將before snapshot成功flush，才依序新增；中途失敗保留證據，不自動刪除或回復既有元件。</summary>
    /// <param name="plan">已審核plan，不能以呼叫此方法取代環境隔離/備份授權。</param><param name="definition">本次SSOT。</param>
    /// <param name="cancellationToken">命令owner取消，已入場同步I/O回來後才釋放lease。</param>
    /// <returns>寫後重新讀取的計畫；pending key仍須verify，不表示索引已Active。</returns>
    internal SchemaPlan Apply(SchemaPlan plan, SchemaDefinition definition, CancellationToken cancellationToken)
    {
        var before = SchemaMetadata.Read(service, cancellationToken);
        SchemaPlanner.ValidatePlan(plan, definition, before);
        var publishing = plan.Operations.Where(operation => operation.Kind is "CreateEntity" or "CreateAttribute" or "CreateLookup" or "CreateKey")
            .Select(operation => operation.Entity).ToHashSet(StringComparer.Ordinal);
        if (publishing.Count != 0)
        {
            // PublishXml以表為粒度，先拒絕本次涉及既有表的未發布結構差異；不可順便發布未審核修改。
            var published = SchemaMetadata.Read(service, cancellationToken, retrieveAsIfPublished: false);
            foreach (var entity in before.Entities.Where(entity => publishing.Contains(entity.LogicalName)))
            {
                var old = published.Entities.SingleOrDefault(item => item.Id == entity.Id);
                if (old is null || SchemaFiles.HashValue(old) != SchemaFiles.HashValue(entity))
                    throw new InvalidDataException("Unpublished metadata changes require separate review before schema apply.");
            }
        }
        _ = SchemaFiles.WriteNew(directory, "before-metadata.json", before);
        var publisherId = plan.PublisherId;
        var created = new Dictionary<(string Entity, string Name), Guid>();
        foreach (var operation in plan.Operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (operation.Kind)
            {
                case "CreatePublisher":
                    publisherId = RequireId(service.Create(new Entity("publisher")
                    {
                        ["uniquename"] = definition.Publisher, ["friendlyname"] = definition.Publisher,
                        ["customizationprefix"] = definition.Prefix, ["customizationoptionvalueprefix"] = 10000
                    }));
                    break;
                case "CreateSolution":
                    _ = RequireId(service.Create(new Entity("solution")
                    {
                        ["uniquename"] = definition.Solution, ["friendlyname"] = definition.Solution,
                        ["version"] = "1.0.0.0", ["publisherid"] = new EntityReference("publisher", RequireId(publisherId))
                    }));
                    break;
                case "CreateEntity":
                {
                    var entity = definition.Entities.Single(entity => entity.LogicalName == operation.Entity);
                    var primary = (StringAttributeMetadata)Attribute(entity.Fields.Single(field => field.LogicalName == entity.PrimaryName), entity.Audit, before.LanguageCode);
                    var response = Execute<CreateEntityResponse>(new CreateEntityRequest
                    {
                        SolutionUniqueName = definition.Solution, HasActivities = false, HasNotes = false,
                        Entity = new EntityMetadata
                        {
                            SchemaName = entity.LogicalName, DisplayName = new Label(entity.DisplayName, before.LanguageCode),
                            DisplayCollectionName = new Label(entity.PluralName, before.LanguageCode), OwnershipType = OwnershipTypes.OrganizationOwned,
                            // 樂觀並行為server唯讀metadata；custom entity預設true，後續重新讀取必須核對outbound要求。
                            IsActivity = false, IsAuditEnabled = new BooleanManagedProperty(entity.Audit)
                        }, PrimaryAttribute = primary
                    }, cancellationToken);
                    created.Add((entity.LogicalName, ""), RequireId(response.EntityId));
                    created.Add((entity.LogicalName, entity.PrimaryName), RequireId(response.AttributeId));
                    break;
                }
                case "CreateAttribute":
                case "CreateLookup":
                {
                    var entity = definition.Entities.Single(entity => entity.LogicalName == operation.Entity);
                    var field = entity.Fields.Single(field => field.LogicalName == operation.Name && field.Phase == 1);
                    var attribute = Attribute(field, entity.IsNew && entity.Audit, before.LanguageCode);
                    Guid id;
                    if (operation.Kind == "CreateLookup")
                    {
                        var response = Execute<CreateOneToManyResponse>(new CreateOneToManyRequest
                        {
                            SolutionUniqueName = definition.Solution, Lookup = (LookupAttributeMetadata)attribute,
                            OneToManyRelationship = new OneToManyRelationshipMetadata
                            {
                                SchemaName = "new_" + entity.LogicalName + "_" + field.LogicalName,
                                ReferencedEntity = field.Target, ReferencedAttribute = field.Target + "id", ReferencingEntity = entity.LogicalName,
                                CascadeConfiguration = new CascadeConfiguration
                                {
                                    Delete = CascadeType.Restrict, Assign = CascadeType.NoCascade, Merge = CascadeType.NoCascade,
                                    Reparent = CascadeType.NoCascade, Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
                                }
                            }
                        }, cancellationToken);
                        id = RequireId(response.AttributeId);
                        _ = RequireId(response.RelationshipId);
                    }
                    else id = RequireId(Execute<CreateAttributeResponse>(new CreateAttributeRequest
                    { EntityName = entity.LogicalName, Attribute = attribute, SolutionUniqueName = definition.Solution }, cancellationToken).AttributeId);
                    created.Add((entity.LogicalName, field.LogicalName), id);
                    break;
                }
                case "CreateKey":
                {
                    var entity = definition.Entities.Single(entity => entity.LogicalName == operation.Entity);
                    var key = entity.Keys.Single(key => key.LogicalName == operation.Name);
                    var response = Execute<CreateEntityKeyResponse>(new CreateEntityKeyRequest
                    {
                        EntityName = entity.LogicalName, SolutionUniqueName = definition.Solution,
                        EntityKey = new EntityKeyMetadata { SchemaName = key.LogicalName, DisplayName = new Label(key.LogicalName, before.LanguageCode), KeyAttributes = key.Attributes.ToArray() }
                    }, cancellationToken);
                    created.Add((entity.LogicalName, key.LogicalName), RequireId(response.EntityKeyId));
                    break;
                }
                case "IncludeEntity":
                case "IncludeAttribute":
                case "IncludeKey":
                {
                    var type = operation.Kind == "IncludeEntity" ? 1 : operation.Kind == "IncludeAttribute" ? 2 : 14;
                    var name = operation.Kind == "IncludeEntity" ? "" : operation.Name;
                    var id = operation.Id != Guid.Empty ? operation.Id : created[(operation.Entity, name)];
                    _ = Execute<AddSolutionComponentResponse>(new AddSolutionComponentRequest
                    {
                        ComponentType = type, ComponentId = RequireId(id), SolutionUniqueName = definition.Solution,
                        AddRequiredComponents = false, DoNotIncludeSubcomponents = true
                    }, cancellationToken);
                    break;
                }
                default: throw new InvalidDataException("Unsupported schema operation.");
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        var changedEntities = plan.Operations.Where(operation => operation.Kind is "CreateEntity" or "CreateAttribute" or "CreateLookup" or "CreateKey")
            .Select(operation => operation.Entity).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (changedEntities.Length != 0)
        {
            // RetrieveAsIfPublished只供衝突盤點，不能當已發布證據；僅定向發布本plan新增所涉表，不PublishAll其他產品變更。
            var xml = new XElement("importexportxml", new XElement("entities", changedEntities.Select(name => new XElement("entity", name))));
            _ = Execute<PublishXmlResponse>(new PublishXmlRequest { ParameterXml = xml.ToString(SaveOptions.DisableFormatting) }, cancellationToken);
        }
        var after = SchemaMetadata.Read(service, cancellationToken, retrieveAsIfPublished: false);
        var remaining = SchemaPlanner.Create(definition, after, clock.GetUtcNow());
        _ = SchemaFiles.WriteNew(directory, "after-metadata.json", after);
        if (remaining.Operations.Count != 0) throw new InvalidOperationException("Schema apply completed with missing metadata components.");
        return remaining;
    }

    /// <summary>建構明確新欄位；固定附錄型別、日期與安全，沒有UpdateAttribute或任何既有欄位轉換。</summary>
    private static AttributeMetadata Attribute(SchemaField field, bool audit, int language)
    {
        AttributeMetadata attribute = field.Type switch
        {
            "string" => new StringAttributeMetadata { MaxLength = field.MaxLength, Format = StringFormat.Text },
            "memo" => new MemoAttributeMetadata { MaxLength = field.MaxLength, Format = StringFormat.TextArea },
            "integer" => new IntegerAttributeMetadata { MinValue = int.MinValue, MaxValue = int.MaxValue, Format = IntegerFormat.None },
            "datetime" => new DateTimeAttributeMetadata { DateTimeBehavior = DateTimeBehavior.UserLocal, Format = DateTimeFormat.DateAndTime },
            "lookup" => new LookupAttributeMetadata(),
            "choice" => new PicklistAttributeMetadata { OptionSet = Options(field, language) },
            _ => throw new InvalidDataException("Unsupported schema attribute.")
        };
        attribute.SchemaName = field.LogicalName;
        attribute.DisplayName = new Label(field.DisplayName, language);
        attribute.RequiredLevel = new AttributeRequiredLevelManagedProperty(field.Required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None);
        attribute.IsSecured = field.Secured;
        attribute.IsAuditEnabled = new BooleanManagedProperty(audit);
        return attribute;
    }

    /// <summary>只建立本欄位局部選項，不產生或修改全域OptionSet；正式數值完全沿用清單。</summary>
    private static OptionSetMetadata Options(SchemaField field, int language)
    {
        var options = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
        foreach (var option in field.Options) options.Options.Add(new OptionMetadata(new Label(option.Label, language), option.Value));
        return options;
    }

    /// <summary>每次寫入前後核對caller取消與強型別回應，保留SDK原故障，不作假成功或catch後靜默繼續。</summary>
    private T Execute<T>(OrganizationRequest request, CancellationToken cancellationToken) where T : OrganizationResponse
    {
        cancellationToken.ThrowIfCancellationRequested();
        var response = service.Execute(request);
        cancellationToken.ThrowIfCancellationRequested();
        return response as T ?? throw new InvalidDataException("Schema write returned an unexpected response.");
    }
    /// <summary>所有新元件只能使用SDK正式ID，不能捏造Guid或用操作token代替資料庫身份。</summary>
    private static Guid RequireId(Guid value) => value != Guid.Empty ? value : throw new InvalidDataException("Schema write returned no stable identity.");
}

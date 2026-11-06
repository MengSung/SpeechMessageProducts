/*
 * 檔案整體說明
 *
 * 目的與定位：本檔把真SchemaDeploymentEngine／SchemaMetadata／SchemaPlanner接到嚴格合成SDK資料庫，驗完整新增與重讀流程。
 * 主要契約：每次寫入前before snapshot必須完整flush且檔案已關閉；只新增metadata，不Update/Delete既有資料，原account ID保持不變。
 * 具體斷言：四key、四日期及兩加密欄的SDK參數精確；定向publish一次，第二輪重新plan/apply完全零寫入且元件齊全。
 * 故障注入：plan竄改、舊證據同名、SDK原故障與未發布結構差異；失敗不覆寫before、不還原checkpoint、不刪既有元件。
 * 生命週期：Fixture擁有唯一Guid暫存目錄與合成Store；產品借用SDK，所有stream關閉後fixture才刪目錄。沒有真連線、credential或LINE。
 * Store以SDK實際請求建立下一次metadata回應，不從planner複製預期結果；SDK readonly屬性只在測試回應建構時設定。
 * 維護要求：新operation要擴充真SDK參數及往返斷言，未知message一律拒絕；本離線整合不是P1-12實機部署或索引唯一性證據。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>真引擎與真metadata投影對嚴格合成SDK往返，驗實際建立參數、backup先flush、兩次apply與漂移；不連D365。</summary>
public sealed class SchemaEngineTests
{
    /// <summary>首次只新增、確認全部元件與明確日期/安全；第二次重新plan/apply零SDK寫入，原表metadata ID不得改變。</summary>
    [Fact]
    public void Apply_Twice_IsAdditiveIdempotentAndKeepsBaseIdentity()
    {
        using var fixture = new Fixture();
        var accountId = fixture.Service.Entities[0].MetadataId;
        var initial = fixture.Engine.Plan(fixture.Definition, default);
        Assert.Equal(0, fixture.Service.WriteCount);
        var after = fixture.Engine.Apply(initial, fixture.Definition, default);
        Assert.Empty(after.Operations);
        Assert.Empty(after.PendingKeys);
        Assert.True(fixture.Service.WriteCount > 0);
        Assert.Equal(accountId, fixture.Service.Entities.Single(entity => entity.LogicalName == "account").MetadataId);
        Assert.Equal(4, fixture.Service.KeyCreates);
        Assert.Equal(4, fixture.Service.DateCreates);
        Assert.Equal(2, fixture.Service.SecuredCreates);
        Assert.Equal(1, fixture.Service.Publishes);
        var writes = fixture.Service.WriteCount;
        var second = new SchemaDeploymentEngine(fixture.Service, Path.Combine(fixture.Directory, "second"), new Clock());
        Assert.Empty(second.Apply(second.Plan(fixture.Definition, default), fixture.Definition, default).Operations);
        Assert.Equal(writes, fixture.Service.WriteCount);
        Assert.Equal(0, fixture.Service.Writes);
    }

    /// <summary>plan竄改與來源schema漂移先拒絕，沒有backup就不可發任何寫入；診斷保留原I/O失敗。</summary>
    [Fact]
    public void Apply_TamperingAndBackupFailure_PerformNoWrites()
    {
        using var fixture = new Fixture();
        var plan = fixture.Engine.Plan(fixture.Definition, default);
        Assert.Throws<InvalidDataException>(() => fixture.Engine.Apply(plan with { Fingerprint = "tampered" }, fixture.Definition, default));
        Assert.Equal(0, fixture.Service.WriteCount);
        Directory.CreateDirectory(fixture.Directory);
        File.WriteAllText(Path.Combine(fixture.Directory, "before-metadata.json"), "existing evidence");
        Assert.ThrowsAny<IOException>(() => fixture.Engine.Apply(plan, fixture.Definition, default));
        Assert.Equal(0, fixture.Service.WriteCount);
        Assert.Equal("existing evidence", File.ReadAllText(Path.Combine(fixture.Directory, "before-metadata.json")));
    }

    /// <summary>定向publish也可能發布同表他人未發布變更；注入published/unpublished差異，在任何備份與寫入之前停止。</summary>
    [Fact]
    public void Apply_UnrelatedUnpublishedChanges_StopsBeforeAnyWrite()
    {
        using var fixture = new Fixture();
        var plan = fixture.Engine.Plan(fixture.Definition, default);
        fixture.Service.UnpublishedChanges = true;
        Assert.Throws<InvalidDataException>(() => fixture.Engine.Apply(plan, fixture.Definition, default));
        Assert.Equal(0, fixture.Service.WriteCount);
    }

    /// <summary>SDK寫入故障傳同例外、保存before snapshot且不回復/刪除既有資料；下一次須重新plan而非假稱全數成功。</summary>
    [Fact]
    public void Apply_SdkFailure_PreservesOriginalAndBeforeSnapshot()
    {
        using var fixture = new Fixture();
        var failure = new InvalidOperationException("synthetic-private-sdk-failure");
        fixture.Service.WriteFailure = failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => fixture.Engine.Apply(fixture.Engine.Plan(fixture.Definition, default), fixture.Definition, default)));
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "before-metadata.json")));
        Assert.Equal(2, fixture.Service.Entities.Count);
        Assert.Equal(0, fixture.Service.Writes);
    }

    /// <summary>Export完整定義只有metadata，不包含任何SDK設定或資料列；UTF8無BOM、CRLF且不覆寫舊證據。</summary>
    [Fact]
    public void Export_OnlyMetadata_IsOwnedAndNeverOverwritesEvidence()
    {
        using var fixture = new Fixture();
        var path = fixture.Engine.Export(default);
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("configuration", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("languageCode", text, StringComparison.Ordinal);
        Assert.ThrowsAny<IOException>(() => fixture.Engine.Export(default));
        Assert.Equal(0, fixture.Service.WriteCount);
    }

    /// <summary>固定時鐘沒有timer或可變caller狀態；只提供本測試的UTC建立時間。</summary>
    internal sealed class Clock : TimeProvider
    {
        /// <summary>明確UTC，不讀取系統預設文化或時區。</summary>
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    }

    /// <summary>每案擁有獨立合成資料與Guid目錄；唯一資源是本案檔案，結束時恰一次清除，SDK無真連線。</summary>
    internal sealed class Fixture : IDisposable
    {
        /// <summary>唯一輸出根，不接受使用者路徑或跨測試共用。</summary>
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "p111-engine-" + Guid.NewGuid().ToString("N"));
        /// <summary>每案重新讀取的正式schema。</summary>
        internal SchemaDefinition Definition { get; } = SchemaPlannerTests.Definition();
        /// <summary>只允許metadata的SDK替身，資料完全屬本案。</summary>
        internal Store Service { get; }
        /// <summary>待測真引擎，借用本案服務與固定時鐘。</summary>
        internal SchemaDeploymentEngine Engine { get; }
        /// <summary>先建立owner邊界再交引擎，產品不得接管或保存外部全域狀態。</summary>
        internal Fixture() { Service = new Store(Directory); Engine = new SchemaDeploymentEngine(Service, Directory, new Clock()); }
        /// <summary>產品所有stream已關閉後才移除本案獨占資料夾；不刪任一既有業務或其他案輸出。</summary>
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true); }
    }

    /// <summary>合成metadata資料庫；把實際SDK建立參數重新投影回產品讀取，避免只驗證mock被呼叫而未驗後續plan/verify。</summary>
    internal sealed class Store(string directory) : SchemaMetadataTests.MetadataService
    {
        private readonly List<Entity> rows = new();
        /// <summary>只有metadata寫入可遞增，資料寫入沿用基類拒絕。</summary>
        internal int WriteCount { get; private set; }
        /// <summary>獨立確認四個正式key都真正建立。</summary>
        internal int KeyCreates { get; private set; }
        /// <summary>獨立確認四個時間點都明確UserLocal/DateAndTime。</summary>
        internal int DateCreates { get; private set; }
        /// <summary>兩個加密欄安全設定都必須出現在真實SDK參數。</summary>
        internal int SecuredCreates { get; private set; }
        /// <summary>新增結構後必須定向publish一次，不可以RetrieveAsIfPublished當作已發布。</summary>
        internal int Publishes { get; private set; }
        /// <summary>只在寫入邊界注入的原故障；不混同只讀或backup錯誤。</summary>
        internal Exception? WriteFailure { get; set; }
        /// <summary>只讀已發布metadata注入與pending定義不同，不能讓apply順便發布未審核內容。</summary>
        internal bool UnpublishedChanges { get; set; }
        /// <summary>在完成一輪apply後將P1欄位隱藏於已發布回應，驗證CLI不能把僅未發布定義視為成功；不改合成DB候選。</summary>
        internal bool MissingPublishedField { get; set; }

        /// <summary>建立publisher/solution是唯一允許的資料型message；任何其他實體Create都拒絕。</summary>
        public override Guid Create(Entity entity)
        {
            Assert.True(entity.LogicalName is "publisher" or "solution");
            BeforeWrite();
            var row = new Entity(entity.LogicalName, Guid.NewGuid());
            foreach (var pair in entity.Attributes) row[pair.Key] = pair.Value;
            if (entity.LogicalName == "solution") row["ismanaged"] = false;
            rows.Add(row);
            return row.Id;
        }

        /// <summary>metadata定義查詢重用基類白名單，根據真實建立結果回完整列，任何設定本文查詢仍拒絕。</summary>
        public override EntityCollection RetrieveMultiple(QueryBase query)
        {
            _ = base.RetrieveMultiple(query);
            var expression = (QueryExpression)query;
            return new EntityCollection(rows.Where(row => row.LogicalName == expression.EntityName).ToList());
        }

        /// <summary>只實作正式新增與加入solution訊息；未知/update/delete/publish-all一律由基類拒絕。</summary>
        public override OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is RetrieveAllEntitiesRequest { RetrieveAsIfPublished: false } && MissingPublishedField)
            {
                var response = (RetrieveAllEntitiesResponse)base.Execute(request);
                var account = Entities.Single(entity => entity.LogicalName == "account");
                Assert.Contains(account.Attributes, field => field.LogicalName == "new_defaultlanguage");
                var publishedAccount = new EntityMetadata { MetadataId = account.MetadataId, LogicalName = account.LogicalName,
                    OwnershipType = account.OwnershipType, IsAuditEnabled = account.IsAuditEnabled };
                SetResponse(publishedAccount, "PrimaryIdAttribute", account.PrimaryIdAttribute);
                SetResponse(publishedAccount, "PrimaryNameAttribute", account.PrimaryNameAttribute);
                SetResponse(publishedAccount, "Attributes", account.Attributes.Where(field => field.LogicalName != "new_defaultlanguage").ToArray());
                SetResponse(publishedAccount, "Keys", account.Keys);
                response.Results["EntityMetadata"] = response.EntityMetadata.Select(entity => entity == account ? publishedAccount : entity).ToArray();
                return response;
            }
            if (request is RetrieveAllEntitiesRequest { RetrieveAsIfPublished: false } && UnpublishedChanges)
            {
                var response = base.Execute(request);
                ((StringAttributeMetadata)Entities[0].Attributes[0]).MaxLength = 161;
                return response;
            }
            if (request is CreateEntityRequest createEntity)
            {
                BeforeWrite(); Assert.Equal("SpeechMessageCore", createEntity.SolutionUniqueName);
                var entity = createEntity.Entity;
                Assert.Equal(OwnershipTypes.OrganizationOwned, entity.OwnershipType);
                entity.MetadataId = Guid.NewGuid(); entity.LogicalName = entity.SchemaName;
                SetResponse(entity, "IsManaged", false);
                SetResponse(entity, "IsOptimisticConcurrencyEnabled", true);
                var primary = Materialize(createEntity.PrimaryAttribute);
                SetResponse(entity, "PrimaryIdAttribute", entity.LogicalName + "id");
                SetResponse(entity, "PrimaryNameAttribute", primary.LogicalName);
                SetResponse(entity, "Attributes", new AttributeMetadata[] { primary });
                SetResponse(entity, "Keys", Array.Empty<EntityKeyMetadata>());
                Entities.Add(entity);
                return new CreateEntityResponse { Results = { ["EntityId"] = entity.MetadataId.Value, ["AttributeId"] = primary.MetadataId!.Value } };
            }
            if (request is CreateAttributeRequest createField)
            {
                BeforeWrite(); Assert.Equal("SpeechMessageCore", createField.SolutionUniqueName);
                var field = Materialize(createField.Attribute); AddField(createField.EntityName, field);
                return new CreateAttributeResponse { Results = { ["AttributeId"] = field.MetadataId!.Value } };
            }
            if (request is CreateOneToManyRequest createLookup)
            {
                BeforeWrite(); Assert.Equal("SpeechMessageCore", createLookup.SolutionUniqueName);
                var field = Materialize(createLookup.Lookup);
                ((LookupAttributeMetadata)field).Targets = new[] { createLookup.OneToManyRelationship.ReferencedEntity };
                Assert.Contains(Entities, entity => entity.LogicalName == createLookup.OneToManyRelationship.ReferencedEntity);
                AddField(createLookup.OneToManyRelationship.ReferencingEntity, field);
                return new CreateOneToManyResponse { Results = { ["AttributeId"] = field.MetadataId!.Value, ["RelationshipId"] = Guid.NewGuid() } };
            }
            if (request is CreateEntityKeyRequest createKey)
            {
                BeforeWrite(); Assert.Equal("SpeechMessageCore", createKey.SolutionUniqueName); KeyCreates++;
                var key = createKey.EntityKey; key.MetadataId = Guid.NewGuid(); key.LogicalName = key.SchemaName;
                SetResponse(key, "EntityKeyIndexStatus", EntityKeyIndexStatus.Active);
                var entity = Entities.Single(entity => entity.LogicalName == createKey.EntityName);
                Assert.All(key.KeyAttributes, name => Assert.Contains(entity.Attributes, field => field.LogicalName == name));
                SetResponse(entity, "Keys", entity.Keys.Append(key).ToArray());
                return new CreateEntityKeyResponse { Results = { ["EntityKeyId"] = key.MetadataId.Value } };
            }
            if (request is AddSolutionComponentRequest include)
            {
                BeforeWrite(); Assert.Equal("SpeechMessageCore", include.SolutionUniqueName);
                Assert.False(include.AddRequiredComponents);
                if (!rows.Any(row => row.LogicalName == "solutioncomponent" && row.GetAttributeValue<Guid>("objectid") == include.ComponentId))
                    rows.Add(new Entity("solutioncomponent", Guid.NewGuid()) { ["objectid"] = include.ComponentId, ["componenttype"] = new OptionSetValue(include.ComponentType) });
                return new AddSolutionComponentResponse();
            }
            if (request is PublishXmlRequest publish)
            {
                BeforeWrite();
                Assert.Contains("<entities>", publish.ParameterXml, StringComparison.Ordinal);
                Assert.DoesNotContain("<all", publish.ParameterXml, StringComparison.OrdinalIgnoreCase);
                Publishes++;
                return new PublishXmlResponse();
            }
            return base.Execute(request);
        }

        /// <summary>每次SDK寫入前確認before snapshot已可完整讀取，證明stream已flush/close；原故障身份不包裝。</summary>
        private void BeforeWrite()
        {
            var path = Assert.Single(System.IO.Directory.GetFiles(directory, "before-metadata.json", SearchOption.AllDirectories));
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) Assert.True(file.Length > 0);
            if (WriteFailure is { } failure) throw failure;
            WriteCount++;
        }

        /// <summary>把SDK真參數儲存為下一次RetrieveAllEntities的回應；額外斷言日期與加密欄位，不從產品規則生成期望。</summary>
        private AttributeMetadata Materialize(AttributeMetadata field)
        {
            field.MetadataId = Guid.NewGuid(); field.LogicalName = field.SchemaName; SetResponse(field, "IsManaged", false);
            if (field is DateTimeAttributeMetadata date)
            {
                Assert.Equal("UserLocal", date.DateTimeBehavior.Value); Assert.Equal(DateTimeFormat.DateAndTime, date.Format); DateCreates++;
            }
            if (field.LogicalName.EndsWith("encrypted", StringComparison.Ordinal)) { Assert.True(field.IsSecured); SecuredCreates++; }
            return field;
        }
        /// <summary>只在合成DB候選內新增，不允許覆蓋同邏輯名；正式DB同名衝突同樣必須使產品停止。</summary>
        private void AddField(string table, AttributeMetadata field)
        {
            var entity = Entities.Single(entity => entity.LogicalName == table);
            Assert.DoesNotContain(entity.Attributes, existing => existing.LogicalName == field.LogicalName);
            SetResponse(entity, "Attributes", entity.Attributes.Append(field).ToArray());
        }
    }
}

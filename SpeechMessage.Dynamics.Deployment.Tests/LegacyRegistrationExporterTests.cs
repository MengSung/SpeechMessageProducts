/*
 * 檔案整體說明
 *
 * 目的與定位：保護 Deployment 的舊註冊唯讀匯出契約，全部使用案例獨占的合成 SDK，
 * 不建立網路連線、不讀 User Secrets，也不把離線斷言當成測試組織驗收。
 * 主要功能與流程：以正式 lookup 串接組件、型別、步驟、映像及流程，注入跨頁重複 ID、
 * 惡意 XML、敏感設定與來源變動，確認只有完整且脫離 SDK 的安全 metadata 快照會發布。
 * 使用邊界與維護：替身只保留本案例記憶體列，所有寫入 API 均拒絕；不擁有 client、timer、
 * stream 或背景工作。測試輸入與輸出只活到案例結束，沒有跨案例共用狀態及正式組織資料。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以合成正式 lookup 與故障注入驗證匯出隔離、完整發布、欄位白名單及分頁上限。</summary>
public sealed class LegacyRegistrationExporterTests
{
    /// <summary>若移除組件排除規則便會匯出自家或平台型別；不同 ID 的同名第三方列仍須保留。</summary>
    [Fact]
    public void ExportLegacy_ExcludesOwnAndMicrosoftAssemblies()
    {
        var service = new ExportOrganization();
        service.Add("pluginassembly", 1, ("name", "SpeechMessage.Dynamics.Plugins"));
        service.Add("pluginassembly", 2, ("name", "Microsoft.Crm"));
        service.Add("pluginassembly", 3, ("name", "Vendor.Legacy"));
        service.Add("pluginassembly", 4, ("name", "Vendor.Legacy"));
        service.Add("pluginassembly", 5, ("name", "SpeechMessage.Dynamics.Workflows"));
        var snapshot = LegacyRegistrationExporter.Export(service);
        Assert.Equal(2, snapshot.Assemblies.Count);
        Assert.All(snapshot.Assemblies, row => Assert.Equal("Vendor.Legacy", row.Name));
    }

    /// <summary>使用真實 lookup ID 而非顯示名稱建立關聯；驗證啟用計數、映像與活動流程均屬正確父列。</summary>
    [Fact]
    public void ExportLegacy_AssociatesLookupsAndCountsEnabledRows()
    {
        var service = Scenario();
        service.Add("pluginassembly", 8, ("name", "Another.Vendor"));
        var snapshot = LegacyRegistrationExporter.Export(service, pageSize: 1);
        Assert.Equal(1, snapshot.EnabledStepCount);
        Assert.Equal(1, snapshot.EnabledWorkflowCount);
        var step = Assert.Single(snapshot.Steps);
        Assert.Equal(Id(2), step.TypeId);
        Assert.Equal("Create", step.Message);
        Assert.Equal("contact", step.PrimaryEntity);
        Assert.Equal(Id(3), Assert.Single(snapshot.Images).StepId);
        Assert.Equal(Id(2), Assert.Single(Assert.Single(snapshot.Workflows).ActivityTypeIds));
        Assert.True(service.QueryCount > 7);
    }

    /// <summary>白名單查詢及 DTO 投影均須拒絕秘密欄位；合成敏感資料即使由服務額外回傳也不得序列化。</summary>
    [Fact]
    public void ExportLegacy_DoesNotReadOrPublishSecureFieldsOrXaml()
    {
        var service = Scenario();
        foreach (var row in service.Rows)
        {
            row["configuration"] = "SYNTHETIC-SECRET";
            row["secureconfig"] = "SYNTHETIC-SECRET";
            row["messageblock"] = "SYNTHETIC-SECRET";
        }

        var json = JsonSerializer.Serialize(LegacyRegistrationExporter.Export(service));
        Assert.DoesNotContain("SYNTHETIC-SECRET", json, StringComparison.Ordinal);
        Assert.DoesNotContain("AssemblyQualifiedName", json, StringComparison.Ordinal);
        Assert.DoesNotContain("xaml", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>跨頁相同正式 ID 不得覆蓋或去重；重播第二頁必須在發布前失敗。</summary>
    [Fact]
    public void ExportLegacy_DuplicateAuthoritativeIdAcrossPages_FailsClosed()
    {
        var service = Scenario();
        service.DuplicatePage = true;
        Assert.Throws<InvalidDataException>(() => LegacyRegistrationExporter.Export(service, pageSize: 1));
    }

    /// <summary>分頁數與總筆數皆是明確界線；不完整資料不得被當成成功快照。</summary>
    [Fact]
    public void ExportLegacy_PagingAndRowLimits_FailClosed()
    {
        var service = Scenario();
        service.Add("pluginassembly", 8, ("name", "Another.Vendor"));
        Assert.Throws<InvalidDataException>(() => LegacyRegistrationExporter.Export(service, pageSize: 1, maximumPages: 1));
        Assert.Throws<InvalidDataException>(() => LegacyRegistrationExporter.Export(service, maximumRows: 1));
    }

    /// <summary>XML 的外部實體與錯誤原文不能外洩；惡意 DTD 必須回固定安全錯誤。</summary>
    [Theory]
    [InlineData("<!DOCTYPE x [<!ENTITY secret SYSTEM 'file:///SYNTHETIC-SECRET'>]><x>&secret;</x>")]
    [InlineData("<SYNTHETIC-SECRET")]
    public void ExportLegacy_UnsafeWorkflowXml_FailsWithoutPayload(string xml)
    {
        var service = Scenario();
        service.Rows.Single(row => row.LogicalName == "workflow")["xaml"] = xml;
        var error = Assert.Throws<InvalidDataException>(() => LegacyRegistrationExporter.Export(service));
        Assert.DoesNotContain("SYNTHETIC-SECRET", error.ToString(), StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    /// <summary>只在文字、註解及相似型別出現的活動名稱不能被誤認為活動參照。</summary>
    [Theory]
    [InlineData("<Root><!-- Vendor.Legacy.Send --><Value>Vendor.Legacy.Send</Value></Root>")]
    [InlineData("<Root AssemblyQualifiedName='Vendor.Legacy.SendExtra, Vendor.Legacy'/>")]
    [InlineData("<Root AssemblyQualifiedName='Vendor.Legacy.Send, Wrong.Assembly'/>")]
    public void ExportLegacy_TextAndWrongAssembly_DoNotMatchActivities(string xml)
    {
        var service = Scenario();
        service.Rows.Single(row => row.LogicalName == "workflow")["xaml"] = xml;
        Assert.Empty(LegacyRegistrationExporter.Export(service).Workflows);
    }

    /// <summary>活動 XML namespace 的完整 CLR namespace、組件簡名及 local name 必須精確對應。</summary>
    [Fact]
    public void ExportLegacy_ClrNamespaceActivity_IsMatched()
    {
        var service = Scenario();
        service.Rows.Single(row => row.LogicalName == "workflow")["xaml"] =
            "<Root xmlns:a='clr-namespace:Vendor.Legacy;assembly=Vendor.Legacy'><a:Send/></Root>";
        Assert.Equal(Id(2), Assert.Single(Assert.Single(LegacyRegistrationExporter.Export(service).Workflows).ActivityTypeIds));
    }

    /// <summary>發布後修改 SDK Entity 與 OptionSetValue 不得改變舊快照；重新匯出仍取得獨立候選。</summary>
    [Fact]
    public void ExportLegacy_SnapshotIsDetachedAndDeterministic()
    {
        var service = Scenario();
        var snapshot = LegacyRegistrationExporter.Export(service);
        var before = JsonSerializer.Serialize(snapshot);
        service.Rows.Reverse();
        Assert.Equal(before, JsonSerializer.Serialize(LegacyRegistrationExporter.Export(service)));
        service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").GetAttributeValue<OptionSetValue>("statecode").Value = 1;
        Assert.Equal(1, snapshot.EnabledStepCount);
        Assert.Equal(0, LegacyRegistrationExporter.Export(service).EnabledStepCount);
        Assert.Equal(before, JsonSerializer.Serialize(snapshot));
        Assert.Throws<NotSupportedException>(() => ((IList<LegacyAssemblyMetadata>)snapshot.Assemblies).Clear());
    }

    /// <summary>呼叫端在開始前取消必須零 SDK 呼叫；匯出器不釋放借入服務或產生背景工作。</summary>
    [Fact]
    public void ExportLegacy_CancelledCaller_PerformsNoQueries()
    {
        var service = Scenario();
        Assert.Throws<OperationCanceledException>(() => LegacyRegistrationExporter.Export(service, cancellationToken: new CancellationToken(true)));
        Assert.Equal(0, service.QueryCount);
    }

    /// <summary>缺少型別父查閱或狀態不能推測為安全停用；正式關聯不完整須拒絕完整發布。</summary>
    [Fact]
    public void ExportLegacy_MissingRequiredMetadata_FailsClosed()
    {
        var service = Scenario();
        service.Rows.Single(row => row.LogicalName == "sdkmessageprocessingstep").Attributes.Remove("statecode");
        Assert.Throws<InvalidDataException>(() => LegacyRegistrationExporter.Export(service));
    }

    /// <summary>建立可人工核對的七層 metadata；所有名稱及 ID 都是本案例的非機密合成值。</summary>
    /// <returns>沒有 OS 資源、只由本案例持有的 SDK 替身。</returns>
    private static ExportOrganization Scenario()
    {
        var service = new ExportOrganization();
        service.Add("pluginassembly", 1, ("name", "Vendor.Legacy"), ("version", "1.0.0.0"), ("isolationmode", new OptionSetValue(2)));
        service.Add("plugintype", 2, ("pluginassemblyid", Ref("pluginassembly", 1)), ("typename", "Vendor.Legacy.Send"), ("isworkflowactivity", true));
        service.Add("sdkmessageprocessingstep", 3, ("eventhandler", Ref("plugintype", 2)), ("sdkmessageid", Ref("sdkmessage", 4)),
            ("sdkmessagefilterid", Ref("sdkmessagefilter", 5)), ("statecode", new OptionSetValue(0)),
            ("name", "Legacy step"), ("stage", new OptionSetValue(40)), ("mode", new OptionSetValue(1)), ("rank", 1));
        service.Add("sdkmessage", 4, ("name", "Create"));
        service.Add("sdkmessagefilter", 5, ("primaryobjecttypecode", "contact"));
        service.Add("sdkmessageprocessingstepimage", 6, ("sdkmessageprocessingstepid", Ref("sdkmessageprocessingstep", 3)),
            ("name", "PostImage"), ("entityalias", "PostImage"), ("imagetype", new OptionSetValue(1)), ("attributes", "fullname"));
        service.Add("workflow", 7, ("name", "Legacy workflow"), ("primaryentity", "contact"), ("statecode", new OptionSetValue(1)),
            ("category", new OptionSetValue(0)), ("type", new OptionSetValue(1)),
            ("xaml", "<Root AssemblyQualifiedName='Vendor.Legacy.Send, Vendor.Legacy, Version=1.0.0.0'><Value>SYNTHETIC-SECRET</Value></Root>"));
        return service;
    }

    /// <summary>固定資料庫 ID，避免測試期待值依產品排序或投影邏輯計算。</summary>
    /// <param name="value">合成尾碼。</param><returns>可重現 GUID。</returns>
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);

    /// <summary>建立顯示名稱刻意無意義的 lookup，以確保正式 ID 才是關聯依據。</summary>
    /// <param name="table">正式 logical name。</param><param name="value">合成 ID。</param><returns>案例獨占參照。</returns>
    private static EntityReference Ref(string table, int value) => new(table, Id(value)) { Name = "wrong display label" };

    /// <summary>
    /// 僅支援白名單 QueryExpression 的離線服務。資料與查詢次數只屬本案例；
    /// 故意回傳完整 Entity 以驗證產品不會把 SDK 的額外敏感欄位放入 DTO，沒有 client 釋放責任。
    /// </summary>
    private sealed class ExportOrganization : IOrganizationService
    {
        /// <summary>案例獨占的來源資料；不與其他服務或測試共用。</summary>
        public List<Entity> Rows { get; } = new();
        /// <summary>用於零查詢取消契約與分頁分支的觀察計數。</summary>
        public int QueryCount { get; private set; }
        /// <summary>注入第二頁重播首筆資料的傳輸故障。</summary>
        public bool DuplicatePage { get; set; }

        /// <summary>新增合成列，保留正式 ID 及欄位型別，無遠端寫入。</summary>
        /// <param name="table">實體名。</param><param name="id">固定 ID。</param><param name="fields">合成 metadata。</param>
        public void Add(string table, int id, params (string Name, object Value)[] fields)
        {
            var row = new Entity(table, Id(id));
            foreach (var field in fields) row[field.Name] = field.Value;
            Rows.Add(row);
        }

        /// <summary>執行欄位安全檢查與正式 ID 分頁；任何秘密欄位或所有欄位要求都立即失敗。</summary>
        /// <param name="query">產品產生的查詢。</param><returns>本頁合成資料與真實分頁旗標。</returns>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            QueryCount++;
            var request = Assert.IsType<QueryExpression>(query);
            Assert.False(request.ColumnSet.AllColumns);
            Assert.DoesNotContain("configuration", request.ColumnSet.Columns);
            Assert.DoesNotContain("secureconfig", request.ColumnSet.Columns);
            Assert.DoesNotContain("messageblock", request.ColumnSet.Columns);
            Assert.NotEqual("sdkmessageprocessingstepsecureconfig", request.EntityName);
            var rows = Rows.Where(row => row.LogicalName == request.EntityName)
                .Where(row => request.Criteria.Conditions.All(condition =>
                {
                    var value = condition.AttributeName == row.LogicalName + "id" ? row.Id :
                        row.Attributes.TryGetValue(condition.AttributeName, out var attribute) ? attribute : null;
                    if (value is EntityReference reference) value = reference.Id;
                    if (value is OptionSetValue option) value = option.Value;
                    return condition.Operator switch
                    {
                        ConditionOperator.In => condition.Values.Contains(value),
                        ConditionOperator.Equal => Equals(value, condition.Values[0]),
                        _ => throw new NotSupportedException(),
                    };
                })).OrderBy(row => row.Id).ToArray();
            var offset = (request.PageInfo.PageNumber - 1) * request.PageInfo.Count;
            var page = rows.Skip(DuplicatePage && offset > 0 ? 0 : offset).Take(request.PageInfo.Count).ToArray();
            var result = new EntityCollection(page) { MoreRecords = DuplicatePage || offset + page.Length < rows.Length };
            result.PagingCookie = result.MoreRecords ? "safe-cookie" : null;
            return result;
        }

        /// <summary>匯出器不得建立資料。</summary>
        /// <param name="entity">不使用。</param><returns>永不回傳。</returns>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>匯出器不得更新資料。</summary><param name="entity">不使用。</param>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>匯出器不得刪除資料。</summary><param name="entityName">不使用。</param><param name="id">不使用。</param>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>僅允許分頁查詢，不接受任意單筆讀取。</summary>
        /// <param name="entityName">不使用。</param><param name="id">不使用。</param><param name="columnSet">不使用。</param><returns>永不回傳。</returns>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>拒絕 Execute，防止藏入非唯讀 SDK 要求。</summary><param name="request">不使用。</param><returns>永不回傳。</returns>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        /// <summary>拒絕關聯寫入。</summary><param name="entityName">不使用。</param><param name="entityId">不使用。</param><param name="relationship">不使用。</param><param name="relatedEntities">不使用。</param>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>拒絕解除關聯寫入。</summary><param name="entityName">不使用。</param><param name="entityId">不使用。</param><param name="relationship">不使用。</param><param name="relatedEntities">不使用。</param>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}
